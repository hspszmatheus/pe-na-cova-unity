using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class LobbyReadyManager : NetworkBehaviour
{
    [Header("Configuração de Cena")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Configuração de NPCs (Fixo em 80)")]
    public const int FixedNpcCount = 80;

    /// <summary>
    /// Quantidade de NPCs na partida fixada em 80 a pedido do usuário.
    /// Persiste entre cenas para ser lida pelo GameSpawner.
    /// </summary>
    public static int ConfiguredNpcCount = FixedNpcCount;

    // Sincronizado pela rede: sempre fixo em 80
    private NetworkVariable<int> npcCountNetVar = new NetworkVariable<int>(
        FixedNpcCount,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkList<ulong> readyPlayerIds;

    private Button readyBtn;
    private Button startBtn;
    private VisualElement rightColumn;
    private UIDocument uiDocument;

    // Elementos da barra de controle de NPCs
    private SliderInt npcSlider;
    private Label npcCountValueLabel;
    private Label npcHintLabel;

    // Controle de bloqueio do Host durante tentativa de entrada de jogadores
    private ISession currentSession = null;
    private readonly HashSet<string> pendingJoiningPlayerIds = new HashSet<string>();
    private readonly Dictionary<string, Coroutine> joiningTimeoutCoroutines = new Dictionary<string, Coroutine>();
    private const float JoinTimeoutDuration = 10f;
    private float sessionDiscrepancyStartTime = -1f;
    private bool isReadyToStart = false;

    private void Awake()
    {
        readyPlayerIds = new NetworkList<ulong>();
        ConfiguredNpcCount = FixedNpcCount;
        PlayerPrefs.SetInt("Lobby_NpcCount", FixedNpcCount);
    }

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        var root = uiDocument.rootVisualElement;

        readyBtn = root.Q<Button>("btn-ready");
        startBtn = root.Q<Button>("btn-start");
        rightColumn = root.Q<VisualElement>("RightColumn");

        npcSlider = root.Q<SliderInt>("slider-npc-count");
        npcCountValueLabel = root.Q<Label>("label-npc-count-value");
        npcHintLabel = root.Q<Label>("label-npc-hint");

        if (rightColumn != null)
        {
            // Oculta a tela da sala inteira antes do jogador conectar
            rightColumn.style.display = DisplayStyle.None;
        }

        if (readyBtn != null)
        {
            readyBtn.clicked += OnReadyButtonClicked;
            readyBtn.style.display = DisplayStyle.None;
            readyBtn.text = "ESTOU PRONTO!";
        }

        if (startBtn != null)
        {
            startBtn.clicked += OnStartButtonClicked;
            startBtn.SetEnabled(false);
            startBtn.style.display = DisplayStyle.None;
            startBtn.text = "INICIAR PARTIDA";
        }

        if (npcSlider != null)
        {
            npcSlider.style.display = DisplayStyle.None;
        }
        var npcBox = root.Q<VisualElement>("npc-settings-container");
        if (npcBox != null)
        {
            npcBox.style.display = DisplayStyle.None;
        }

        UpdateNpcUI(FixedNpcCount);
    }

    private void OnDisable()
    {
        if (readyBtn != null) readyBtn.clicked -= OnReadyButtonClicked;
        if (startBtn != null) startBtn.clicked -= OnStartButtonClicked;

        if (npcSlider != null)
        {
            npcSlider.UnregisterValueChangedCallback(OnNpcSliderValueChanged);
        }

        ClearPendingJoining();
        UnbindSession();
    }

    private void Update()
    {
        if (!IsServer) return;

        // Garante que o host encontre e se inscreva na sessão UGS assim que ela estiver criada
        if (currentSession == null)
        {
            var s = GetActiveSession();
            if (s != null)
            {
                BindSession(s);
                EvaluateAllPlayersReady();
            }
        }
        else
        {
            // Monitora se discrepância de jogadores entre UGS e Netcode atingiu o tempo limite de timeout
            if (sessionDiscrepancyStartTime > 0f)
            {
                if (Time.unscaledTime - sessionDiscrepancyStartTime >= JoinTimeoutDuration)
                {
                    EvaluateAllPlayersReady();
                }
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        readyPlayerIds.OnListChanged += OnReadyListChanged;
        npcCountNetVar.OnValueChanged += OnNpcCountNetworkChanged;

        // O jogador entrou ou criou uma sala, então mostramos a coluna da direita!
        if (rightColumn != null)
        {
            rightColumn.style.display = DisplayStyle.Flex;
        }

        if (IsServer)
        {
            // DONO DA SALA (Host/Server):
            if (readyPlayerIds != null)
            {
                try { readyPlayerIds.Clear(); } catch { }
            }

            ClearPendingJoining();

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback = null;
                if (NetworkManager.Singleton.NetworkConfig != null)
                {
                    NetworkManager.Singleton.NetworkConfig.ConnectionApproval = false;
                }

                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            npcCountNetVar.Value = FixedNpcCount;
            ConfiguredNpcCount = FixedNpcCount;
            PlayerPrefs.SetInt("Lobby_NpcCount", FixedNpcCount);

            if (startBtn != null)
            {
                startBtn.style.display = DisplayStyle.Flex;
                startBtn.text = "INICIAR PARTIDA";
            }
            if (readyBtn != null) readyBtn.style.display = DisplayStyle.None;

            var session = GetActiveSession();
            if (session != null)
            {
                BindSession(session);
            }

            EvaluateAllPlayersReady();
        }
        else
        {
            // CLIENTE (Não é o dono da sala):
            ConfiguredNpcCount = FixedNpcCount;

            if (startBtn != null) startBtn.style.display = DisplayStyle.None;
            if (readyBtn != null)
            {
                readyBtn.style.display = DisplayStyle.Flex;
                readyBtn.text = "ESTOU PRONTO!";
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        readyPlayerIds.OnListChanged -= OnReadyListChanged;
        npcCountNetVar.OnValueChanged -= OnNpcCountNetworkChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        ClearPendingJoining();
        UnbindSession();

        // O jogador saiu ou a sala foi desfeita, então escondemos a coluna novamente
        if (rightColumn != null)
        {
            rightColumn.style.display = DisplayStyle.None;
        }
    }

    #region Gerenciamento de Sessão UGS e Bloqueio de Entrada

    private ISession GetActiveSession()
    {
        try
        {
            if (MultiplayerService.Instance != null && MultiplayerService.Instance.Sessions != null)
            {
                foreach (var kvp in MultiplayerService.Instance.Sessions)
                {
                    if (kvp.Value != null) return kvp.Value;
                }
            }
        }
        catch { }
        return null;
    }

    private void BindSession(ISession session)
    {
        if (session == null || currentSession == session) return;
        UnbindSession();
        currentSession = session;
        currentSession.PlayerJoined += OnSessionPlayerJoined;
        currentSession.PlayerHasLeft += OnSessionPlayerHasLeft;
        currentSession.RemovedFromSession += OnSessionEnded;
        currentSession.Deleted += OnSessionEnded;
    }

    private void UnbindSession()
    {
        if (currentSession != null)
        {
            try
            {
                currentSession.PlayerJoined -= OnSessionPlayerJoined;
                currentSession.PlayerHasLeft -= OnSessionPlayerHasLeft;
                currentSession.RemovedFromSession -= OnSessionEnded;
                currentSession.Deleted -= OnSessionEnded;
            }
            catch { }
            currentSession = null;
        }
    }

    private void OnSessionEnded()
    {
        ClearPendingJoining();
        EvaluateAllPlayersReady();
    }

    private void OnSessionPlayerJoined(string playerId)
    {
        if (!IsServer) return;

        // Se o evento for referente ao próprio Host, não bloqueia
        if (currentSession != null && (playerId == currentSession.Host || (currentSession.CurrentPlayer != null && playerId == currentSession.CurrentPlayer.Id)))
        {
            return;
        }

        Debug.Log($"[LobbyReadyManager] Jogador '{playerId}' tentando entrar na sala. Bloqueando botão de iniciar.");

        if (!pendingJoiningPlayerIds.Contains(playerId))
        {
            pendingJoiningPlayerIds.Add(playerId);
        }

        if (joiningTimeoutCoroutines.TryGetValue(playerId, out Coroutine existingRoutine))
        {
            if (existingRoutine != null) StopCoroutine(existingRoutine);
        }

        joiningTimeoutCoroutines[playerId] = StartCoroutine(JoiningPlayerTimeoutRoutine(playerId, JoinTimeoutDuration));

        EvaluateAllPlayersReady();
    }

    private void OnSessionPlayerHasLeft(string playerId)
    {
        if (!IsServer) return;

        Debug.Log($"[LobbyReadyManager] Jogador '{playerId}' saiu ou cancelou a entrada.");

        if (pendingJoiningPlayerIds.Contains(playerId))
        {
            if (joiningTimeoutCoroutines.TryGetValue(playerId, out Coroutine routine))
            {
                if (routine != null) StopCoroutine(routine);
                joiningTimeoutCoroutines.Remove(playerId);
            }
            pendingJoiningPlayerIds.Remove(playerId);
        }

        EvaluateAllPlayersReady();
    }

    private IEnumerator JoiningPlayerTimeoutRoutine(string playerId, float timeoutSeconds)
    {
        yield return new WaitForSecondsRealtime(timeoutSeconds);

        if (pendingJoiningPlayerIds.Contains(playerId))
        {
            Debug.LogWarning($"[LobbyReadyManager] Timeout de {timeoutSeconds}s ao aguardar entrada do jogador '{playerId}'. Liberando bloqueio.");
            pendingJoiningPlayerIds.Remove(playerId);
            joiningTimeoutCoroutines.Remove(playerId);
            EvaluateAllPlayersReady();
        }
    }

    private void ClearPendingJoining()
    {
        foreach (var kvp in joiningTimeoutCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        joiningTimeoutCoroutines.Clear();
        pendingJoiningPlayerIds.Clear();
        sessionDiscrepancyStartTime = -1f;
    }

    private int GetSessionPlayerCount()
    {
        if (currentSession != null && currentSession.Players != null)
        {
            return currentSession.Players.Count;
        }
        return 0;
    }

    private bool IsAnyPlayerJoining()
    {
        if (pendingJoiningPlayerIds.Count > 0)
        {
            return true;
        }

        int sessionCount = GetSessionPlayerCount();
        int netcodeCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;

        if (sessionCount > netcodeCount)
        {
            // Mais jogadores na sessão UGS do que conectados no Netcode (tentando entrar)
            if (sessionDiscrepancyStartTime < 0f)
            {
                sessionDiscrepancyStartTime = Time.unscaledTime;
            }

            if (Time.unscaledTime - sessionDiscrepancyStartTime < JoinTimeoutDuration)
            {
                return true;
            }
        }
        else
        {
            sessionDiscrepancyStartTime = -1f;
        }

        return false;
    }

    #endregion

    private void OnNpcSliderValueChanged(ChangeEvent<int> evt)
    {
        // Quantidade de NPCs é permanentemente fixada em 80 (FixedNpcCount) a pedido de design
        ConfiguredNpcCount = FixedNpcCount;
        PlayerPrefs.SetInt("Lobby_NpcCount", FixedNpcCount);
        if (IsServer)
        {
            npcCountNetVar.Value = FixedNpcCount;
        }
        if (npcSlider != null)
        {
            npcSlider.SetValueWithoutNotify(FixedNpcCount);
        }
        UpdateNpcUI(FixedNpcCount);
    }

    private void OnNpcCountNetworkChanged(int oldVal, int newVal)
    {
        ConfiguredNpcCount = newVal;
        if (npcSlider != null && npcSlider.value != newVal)
        {
            npcSlider.SetValueWithoutNotify(newVal);
        }
        UpdateNpcUI(newVal);
    }

    private void UpdateNpcUI(int count)
    {
        if (npcCountValueLabel != null)
        {
            npcCountValueLabel.text = count.ToString();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[LobbyReadyManager] Cliente Netcode {clientId} conectou com sucesso.");

        // Um jogador que estava tentando entrar completou a conexão Netcode
        if (pendingJoiningPlayerIds.Count > 0)
        {
            var first = pendingJoiningPlayerIds.First();
            if (joiningTimeoutCoroutines.TryGetValue(first, out Coroutine routine))
            {
                if (routine != null) StopCoroutine(routine);
                joiningTimeoutCoroutines.Remove(first);
            }
            pendingJoiningPlayerIds.Remove(first);
        }

        int sessionCount = GetSessionPlayerCount();
        int netcodeCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
        if (sessionCount <= netcodeCount && pendingJoiningPlayerIds.Count > 0)
        {
            ClearPendingJoining();
        }

        EvaluateAllPlayersReady();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[LobbyReadyManager] Cliente Netcode {clientId} desconectou.");

        if (readyPlayerIds.Contains(clientId))
        {
            readyPlayerIds.Remove(clientId);
        }

        EvaluateAllPlayersReady();
    }

    private void OnReadyButtonClicked()
    {
        if (!IsClient || IsServer) return;
        ToggleReadyServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleReadyServerRpc(ulong clientId)
    {
        if (readyPlayerIds.Contains(clientId))
        {
            readyPlayerIds.Remove(clientId);
        }
        else
        {
            readyPlayerIds.Add(clientId);
        }

        EvaluateAllPlayersReady();
    }

    private void OnReadyListChanged(NetworkListEvent<ulong> changeEvent)
    {
        if (readyBtn != null && !IsServer)
        {
            bool isReady = readyPlayerIds.Contains(NetworkManager.Singleton.LocalClientId);
            readyBtn.text = isReady ? "CANCELAR PRONTO" : "ESTOU PRONTO!";
            if (isReady)
            {
                readyBtn.RemoveFromClassList("cartoon-btn--orange");
                readyBtn.AddToClassList("cartoon-btn--green");
            }
            else
            {
                readyBtn.RemoveFromClassList("cartoon-btn--green");
                readyBtn.AddToClassList("cartoon-btn--orange");
            }
        }

        if (IsServer)
        {
            EvaluateAllPlayersReady();
        }
    }

    private void EvaluateAllPlayersReady()
    {
        if (!IsServer) return;

        int connectedCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
        bool isJoining = IsAnyPlayerJoining();
        bool readyToStart = false;

        if (isJoining)
        {
            // Bloqueio prioritário: Alguém está tentando entrar na sala
            readyToStart = false;
        }
        else if (connectedCount == 1)
        {
            // Host jogando sozinho
            readyToStart = true;
        }
        else if (connectedCount > 1)
        {
            // Todos os outros jogadores conectados devem ter clicado em Ready
            int requiredReadyCount = connectedCount - 1;
            readyToStart = (readyPlayerIds.Count == requiredReadyCount);
        }

        isReadyToStart = readyToStart;
        ApplyStartButtonState(readyToStart, isJoining);
        if (IsSpawned && IsServer)
        {
            UpdateStartButtonClientRpc(readyToStart, isJoining);
        }
    }

    private void ApplyStartButtonState(bool ready, bool isConnecting)
    {
        if (startBtn == null) return;

        startBtn.SetEnabled(ready);

        if (isConnecting)
        {
            startBtn.text = "JOGADOR ENTRANDO...";
            startBtn.RemoveFromClassList("cartoon-btn--green");
            startBtn.AddToClassList("cartoon-btn--orange");
        }
        else if (ready)
        {
            startBtn.text = "INICIAR PARTIDA";
            startBtn.RemoveFromClassList("cartoon-btn--orange");
            startBtn.AddToClassList("cartoon-btn--green");
        }
        else
        {
            startBtn.text = "AGUARDANDO JOGADORES...";
            startBtn.RemoveFromClassList("cartoon-btn--green");
            startBtn.AddToClassList("cartoon-btn--orange");
        }
    }

    [ClientRpc]
    private void UpdateStartButtonClientRpc(bool isReadyToStart, bool isConnecting)
    {
        if (IsServer)
        {
            ApplyStartButtonState(isReadyToStart, isConnecting);
        }
    }

    [ClientRpc]
    private void ShowGameStartingLoadingClientRpc()
    {
        GameLoadingScreenController.Instance?.Show(
            "ENTRANDO EM PENA COVA...",
            "Carregando cenário e sincronizando jogadores...",
            minDisplayTime: 2.0f,
            autoDismiss: false);
    }

    private void OnStartButtonClicked()
    {
        if (!IsServer) return;

        // Trava rigorosa: Não permite início se alguém estiver entrando ou não estiver pronto
        if (!isReadyToStart || IsAnyPlayerJoining())
        {
            Debug.LogWarning("[LobbyReadyManager] Início da partida bloqueado: jogador entrando ou jogadores não estão prontos.");
            return;
        }

        // Exibe a tela de carregamento com a caveira da Morte em todos os jogadores da sala
        ShowGameStartingLoadingClientRpc();

        // Assegura que a quantidade fixa de 80 NPCs seja consolidada antes de trocar de cena
        ConfiguredNpcCount = FixedNpcCount;
        PlayerPrefs.SetInt("Lobby_NpcCount", FixedNpcCount);
        npcCountNetVar.Value = FixedNpcCount;

        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }
}
