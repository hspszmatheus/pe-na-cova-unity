using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSpawner : NetworkBehaviour
{
    [Header("Configuração do Player")]
    [Tooltip("Prefab do Player para instanciar na rede")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Configuração do Hunter")]
    [Tooltip("Prefab do Hunter para instanciar na rede")]
    [SerializeField] private GameObject hunterPrefab;

    [Header("Configuração de Tempo de Partida")]
    [Tooltip("Duração total da partida após o spawn do Hunter em segundos (5 minutos = 300s)")]
    [SerializeField] private float matchDurationSeconds = 300.0f;

    [Tooltip("Duração do spawn inicial do Hunter nos céus antes de iniciar a partida (5 segundos)")]
    [SerializeField] private float hunterIntroDurationSeconds = 5.0f;

    private readonly NetworkVariable<float> matchTimeRemainingNetVar = new NetworkVariable<float>(
        300.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> isMatchTimerRunningNetVar = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Indica se o cronômetro da partida está ativo (ou seja, os 5 segundos da descida da Morte já terminaram e o jogo começou).
    /// </summary>
    public bool IsMatchActive => isMatchTimerRunningNetVar != null && isMatchTimerRunningNetVar.Value;

    private bool hasMatchStarted = false;

    /// <summary>
    /// Retorna true quando o cronômetro da partida já iniciou (ou o jogo já terminou),
    /// mantendo jogadores e NPCs visíveis na tela pós-jogo mesmo após o cronômetro parar.
    /// </summary>
    public bool HasMatchStarted => hasMatchStarted || IsMatchActive || isGameOver;

    private readonly NetworkVariable<int> aliveSurvivorsCountNetVar = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly HashSet<ulong> activeHunterClientIds = new HashSet<ulong>();
    private readonly HashSet<ulong> activeNormalPlayerClientIds = new HashSet<ulong>();
    private readonly Dictionary<ulong, int> survivorModelAssignments = new Dictionary<ulong, int>();
    private int initialNormalPlayerCount = 0;

    public IReadOnlyCollection<ulong> ActiveHunterClientIds => activeHunterClientIds;
    public IReadOnlyCollection<ulong> ActiveNormalPlayerClientIds => activeNormalPlayerClientIds;
    public bool IsClientHunter(ulong clientId) => activeHunterClientIds.Contains(clientId);

    private bool initialSpawnCompleted = false;
    private bool isGameActive = false;
    private bool isGameOver = false;
    public bool IsGameOver => isGameOver;

    // UI / Banner de Fim de Jogo
    private string gameOverTitle = null;
    private string gameOverSubtitle = null;
    private float gameOverCountdown = 0f;

    public static GameSpawner Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
        EnsurePrefabsLoaded();
        EnsureNavMeshActive();
    }

    /// <summary>
    /// Garante que a malha de navegação (NavMesh) esteja sempre ativa e registrada no mundo do jogo,
    /// mesmo se o componente NavMeshSurface estiver desabilitado no Inspector ou na cena.
    /// </summary>
    public static void EnsureNavMeshActive()
    {
        var surfaces = Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsInactive.Include);
        if (surfaces != null && surfaces.Length > 0)
        {
            foreach (var surface in surfaces)
            {
                if (surface != null)
                {
                    if (!surface.enabled)
                    {
                        surface.enabled = true;
                    }
                    if (surface.navMeshData != null)
                    {
                        surface.RemoveData();
                        surface.AddData();
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (isMatchTimerRunningNetVar.Value || isGameOver)
        {
            hasMatchStarted = true;
        }

        if (IsServer && isGameActive && !isGameOver)
        {
            if (isMatchTimerRunningNetVar.Value)
            {
                if (matchTimeRemainingNetVar.Value > 0f)
                {
                    matchTimeRemainingNetVar.Value = Mathf.Max(0f, matchTimeRemainingNetVar.Value - Time.deltaTime);

                    if (matchTimeRemainingNetVar.Value <= 0f)
                    {
                        Debug.Log("[GameSpawner] Tempo esgotado (5 minutos)! Vitória dos sobreviventes.");
                        EndGame(survivorsWon: true, subtitleReason: "TEMPO ESGOTADO - SOBREVIVENTES ESCAPARAM");
                    }
                }
            }
        }

        // Atualiza a interface gráfica do HUD em tempo real enquanto a partida estiver ativa
        if (GameHUDController.Instance != null && !isGameOver)
        {
            GameHUDController.Instance.SetMatchTimer(matchTimeRemainingNetVar.Value, isMatchTimerRunningNetVar.Value);
            GameHUDController.Instance.SetAlivePlayersCount(aliveSurvivorsCountNetVar.Value);
        }
    }

    private void EnsurePrefabsLoaded()
    {
        if (playerPrefab == null)
        {
            playerPrefab = Resources.Load<GameObject>("Player");
#if UNITY_EDITOR
            if (playerPrefab == null)
            {
                playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
            }
#endif
        }

        if (hunterPrefab == null)
        {
            hunterPrefab = Resources.Load<GameObject>("Hunter");
            if (hunterPrefab == null && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.NetworkConfig != null && Unity.Netcode.NetworkManager.Singleton.NetworkConfig.Prefabs != null)
            {
                foreach (var p in Unity.Netcode.NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                {
                    if (p.Prefab != null && p.Prefab.name == "Hunter")
                    {
                        hunterPrefab = p.Prefab;
                        break;
                    }
                }
            }
#if UNITY_EDITOR
            if (hunterPrefab == null)
            {
                hunterPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Hunter.prefab");
            }
#endif
        }

        if (npcPrefab == null)
        {
            npcPrefab = Resources.Load<GameObject>("NPC");
#if UNITY_EDITOR
            if (npcPrefab == null)
            {
                npcPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/NPC.prefab");
            }
#endif
        }
    }

    [Header("Configuração de NPCs")]
    [Tooltip("Prefab do NPC para instanciar na rede")]
    [SerializeField] private GameObject npcPrefab;

    [Tooltip("Quantidade de NPCs que vão andar pela cidade")]
    [SerializeField] private int npcCount = 15;

    [Tooltip("Se verdadeiro, spawna os NPCs automaticamente ao iniciar a partida")]
    [SerializeField] private bool spawnNpcsOnStart = true;

    private bool hasSpawnedNpcs = false;

    [Header("Spawn Aleatório na Cidade")]
    [Tooltip("Gera pontos de spawn aleatórios e seguros dentro do perímetro da cidade")]
    [SerializeField] private bool useRandomCitySpawn = true;

    [Header("Área Delimitada da Cidade (Circular / Baseada na Referência)")]
    [Tooltip("Centro da área jogável da cidade (Vector2 X/Z)")]
    [SerializeField] private Vector2 cityCenter = new Vector2(0.2f, -39.9f);

    [Tooltip("Raio máximo da área jogável da cidade para spawns seguros (distância segura das paredes)")]
    [SerializeField] private float cityRadius = 69.0f;

    [Tooltip("Limites X da área urbana para spawn (fallback de compatibilidade)")]
    [SerializeField] private Vector2 xBounds = new Vector2(-74f, 74f);

    [Tooltip("Limites Z da área urbana para spawn (fallback de compatibilidade)")]
    [SerializeField] private Vector2 zBounds = new Vector2(-114f, 34f);

    [Tooltip("Altura mínima permitida do solo para spawn (evita cair no vazio)")]
    [SerializeField] private float minGroundY = -0.3f;

    [Tooltip("Altura máxima permitida do solo para spawn (evita telhados de prédios)")]
    [SerializeField] private float maxGroundY = 1.2f;

    [Tooltip("Distância mínima entre jogadores que spawnarem na mesma rodada")]
    [SerializeField] private float minDistanceBetweenPlayers = 3.5f;

    [Tooltip("Tentativas máximas por jogador para encontrar um ponto seguro")]
    [SerializeField] private int maxSpawnAttempts = 60;

    [Header("Spawn Manual (Fallback / Opcional)")]
    [Tooltip("Pontos fixos usados se useRandomCitySpawn for desativado ou como fallback")]
    [SerializeField] private Transform[] spawnPoints;

    private readonly HashSet<ulong> loadedClients = new HashSet<ulong>();
    private Coroutine initialSpawnTimeoutCoroutine;
    private Coroutine clientLoadingTimeoutCoroutine;
    private bool previousConnectionApproval;
    private bool capturedConnectionApproval;

    public override void OnNetworkSpawn()
    {
        EnsurePrefabsLoaded();

        hasMatchStarted = isMatchTimerRunningNetVar.Value || isGameOver;
        isMatchTimerRunningNetVar.OnValueChanged += OnMatchTimerRunningChanged;
        HunterPreMatchVisibility.RefreshAll();

        if (NetworkManager.Singleton != null)
        {
            if (IsServer)
            {
                // Regra: SEM LATE JOIN. Configura ConnectionApproval no Server para rejeitar conexões no meio do jogo.
                previousConnectionApproval = NetworkManager.Singleton.NetworkConfig.ConnectionApproval;
                capturedConnectionApproval = true;
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCheck;

                if (NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
                }

                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

                // O Host registra seu próprio carregamento no Server
                RegisterClientLoaded(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                // No cliente, escuta se foi desconectado para retornar ao Lobby
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientLocalDisconnect;

                // Notifica o Servidor de que este cliente completou o carregamento da cena
                NotifyClientSceneLoadedServerRpc();

                // Timeout de proteção caso o servidor demore ou caia antes de iniciar
                if (clientLoadingTimeoutCoroutine != null) StopCoroutine(clientLoadingTimeoutCoroutine);
                clientLoadingTimeoutCoroutine = StartCoroutine(ClientLoadingTimeoutRoutine());
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyClientSceneLoadedServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        RegisterClientLoaded(clientId);
    }

    private void RegisterClientLoaded(ulong clientId)
    {
        if (!IsServer || initialSpawnCompleted) return;

        loadedClients.Add(clientId);

        int totalCount = NetworkManager.Singleton != null ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count) : 1;
        int readyCount = Mathf.Min(loadedClients.Count, totalCount);

        Debug.Log($"[GameSpawner] Jogador {clientId} carregou a cena. Sincronização: {readyCount}/{totalCount}");

        // Atualiza a tela de carregamento de todos os jogadores conectados
        UpdateLoadingScreenSyncClientRpc(readyCount, totalCount);

        if (readyCount >= totalCount)
        {
            if (initialSpawnTimeoutCoroutine != null)
            {
                StopCoroutine(initialSpawnTimeoutCoroutine);
                initialSpawnTimeoutCoroutine = null;
            }

            StartCoroutine(StartSynchronizedMatchRoutine());
        }
        else if (initialSpawnTimeoutCoroutine == null)
        {
            initialSpawnTimeoutCoroutine = StartCoroutine(InitialSpawnTimeoutRoutine());
        }
    }

    [ClientRpc]
    private void UpdateLoadingScreenSyncClientRpc(int readyCount, int totalCount)
    {
        if (readyCount >= totalCount && clientLoadingTimeoutCoroutine != null)
        {
            StopCoroutine(clientLoadingTimeoutCoroutine);
            clientLoadingTimeoutCoroutine = null;
        }

        if (GameLoadingScreenController.Instance != null && GameLoadingScreenController.Instance.IsVisible)
        {
            float progress = totalCount > 0 ? (float)readyCount / totalCount : 1f;
            string subtitle = readyCount < totalCount
                ? $"Aguardando todos os jogadores carregarem o mapa ({readyCount}/{totalCount})..."
                : $"Todos os jogadores conectados! Preparando partida ({readyCount}/{totalCount})...";

            GameLoadingScreenController.Instance.SetSyncStatus("SINCRONIZANDO JOGADORES...", subtitle, progress);
        }
    }

    private IEnumerator StartSynchronizedMatchRoutine()
    {
        if (initialSpawnCompleted) yield break;

        Debug.Log("[GameSpawner] Todos os jogadores prontos! Sincronizando início da partida...");

        if (clientLoadingTimeoutCoroutine != null)
        {
            StopCoroutine(clientLoadingTimeoutCoroutine);
            clientLoadingTimeoutCoroutine = null;
        }

        int totalCount = NetworkManager.Singleton != null ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count) : 1;
        UpdateLoadingScreenSyncClientRpc(totalCount, totalCount);

        // Dá um pequeno respiro (0.35s) para que todos os clientes estabilizem a barra em 100%
        yield return new WaitForSeconds(0.35f);

        SpawnInitialPlayers();
    }

    public override void OnNetworkDespawn()
    {
        if (clientLoadingTimeoutCoroutine != null)
        {
            StopCoroutine(clientLoadingTimeoutCoroutine);
            clientLoadingTimeoutCoroutine = null;
        }

        isMatchTimerRunningNetVar.OnValueChanged -= OnMatchTimerRunningChanged;
        hasMatchStarted = false;

        if (NetworkManager.Singleton != null)
        {
            if (IsServer)
            {
                if (NetworkManager.Singleton.ConnectionApprovalCallback == ConnectionApprovalCheck)
                {
                    NetworkManager.Singleton.ConnectionApprovalCallback = null;
                    if (capturedConnectionApproval)
                    {
                        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = previousConnectionApproval;
                        capturedConnectionApproval = false;
                    }
                }

                if (NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
                }

                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            else
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientLocalDisconnect;
            }
        }
    }

    private void OnMatchTimerRunningChanged(bool previousValue, bool newValue)
    {
        if (newValue || isGameOver)
        {
            hasMatchStarted = true;
        }

        if (newValue)
        {
            HunterPreMatchVisibility.RefreshAll();
            GameHUDController.Instance?.SetTasksPanelVisible(true);
        }
    }

    public const int MaxPlayersPerRoom = 8;

    /// <summary>
    /// Rejeita qualquer tentativa de conexão após o início do jogo (Regra: Sem Late Join).
    /// </summary>
    private void ConnectionApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        if (isGameActive || initialSpawnCompleted)
        {
            response.Approved = false;
            response.Reason = "A partida já começou. Não é permitido entrar durante o jogo.";
            response.Pending = false;
            Debug.LogWarning($"[GameSpawner] Conexão bloqueada para Client {request.ClientNetworkId}: Partida já em andamento (Sem Late Join).");
            return;
        }

        // Limite máximo de 8 jogadores na sala/partida
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count >= MaxPlayersPerRoom)
        {
            response.Approved = false;
            response.Reason = $"A sala já atingiu o limite máximo de {MaxPlayersPerRoom} jogadores.";
            response.Pending = false;
            Debug.LogWarning($"[GameSpawner] Conexão bloqueada para Client {request.ClientNetworkId}: Limite máximo de {MaxPlayersPerRoom} jogadores atingido.");
            return;
        }

        response.Approved = true;
        response.CreatePlayerObject = false; // GameSpawner gerencia o spawn dos jogadores
        response.Pending = false;
    }

    private void HandleClientLocalDisconnect(ulong disconnectedClientId)
    {
        if (InGamePauseMenuController.IsLeavingMatch)
        {
            return;
        }

        if (!IsServer && NetworkManager.Singleton != null && disconnectedClientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.LogWarning("[GameSpawner] Desconectado do servidor. Retornando ao navegador de salas...");
            if (GameLoadingScreenController.Instance != null)
            {
                GameLoadingScreenController.Instance.AbortLoadingAndReturnToRoomBrowser("Desconectado do servidor.");
            }
            else
            {
                LobbyMenuFlowController.ForceReturnToRoomBrowser = true;
                UnityEngine.SceneManagement.SceneManager.LoadScene("JoinByBrowsing");
            }
        }
    }

    private IEnumerator ClientLoadingTimeoutRoutine()
    {
        yield return new WaitForSeconds(15.0f);

        if (!initialSpawnCompleted)
        {
            Debug.LogWarning("[GameSpawner] Timeout do cliente atingido (15s): Servidor demorou a iniciar a partida. Retornando à escolha de salas...");
            if (GameLoadingScreenController.Instance != null)
            {
                GameLoadingScreenController.Instance.AbortLoadingAndReturnToRoomBrowser("Tempo limite esgotado.");
            }
            else
            {
                LobbyMenuFlowController.ForceReturnToRoomBrowser = true;
                UnityEngine.SceneManagement.SceneManager.LoadScene("JoinByBrowsing");
            }
        }
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (!IsServer) return;

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.LoadComplete:
                RegisterClientLoaded(sceneEvent.ClientId);
                break;

            case SceneEventType.LoadEventCompleted:
                if (!initialSpawnCompleted)
                {
                    if (initialSpawnTimeoutCoroutine != null)
                    {
                        StopCoroutine(initialSpawnTimeoutCoroutine);
                        initialSpawnTimeoutCoroutine = null;
                    }
                    StartCoroutine(StartSynchronizedMatchRoutine());
                }
                break;
        }
    }

    private IEnumerator InitialSpawnTimeoutRoutine()
    {
        // Aguarda até 15 segundos para computadores com carregamento mais lento
        yield return new WaitForSeconds(15.0f);

        if (!initialSpawnCompleted)
        {
            Debug.LogWarning("[GameSpawner] Timeout de sincronização de carregamento atingido (15s).");
            int totalCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;
            if (totalCount <= 0)
            {
                Debug.LogWarning("[GameSpawner] Nenhum jogador conectado no timeout. Retornando ao navegador de salas...");
                if (GameLoadingScreenController.Instance != null)
                {
                    GameLoadingScreenController.Instance.AbortLoadingAndReturnToRoomBrowser("Nenhum jogador conectado.");
                }
                else
                {
                    LobbyMenuFlowController.ForceReturnToRoomBrowser = true;
                    UnityEngine.SceneManagement.SceneManager.LoadScene("JoinByBrowsing");
                }
                yield break;
            }

            StartCoroutine(StartSynchronizedMatchRoutine());
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        // Se a partida já começou e algum cliente conseguiu conectar, desconecta imediatamente (Sem Late Join)
        if (isGameActive || initialSpawnCompleted)
        {
            Debug.LogWarning($"[GameSpawner] Desconectando Client ID {clientId}: A partida já começou (Sem Late Join).");
            NetworkManager.Singleton.DisconnectClient(clientId, "A partida já começou. Não é permitido entrar durante o jogo.");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        loadedClients.Remove(clientId);

        // Se a partida ainda não havia começado, atualiza o status de sincronização e verifica se os restantes estão prontos
        if (!initialSpawnCompleted)
        {
            int totalCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;
            int readyCount = Mathf.Min(loadedClients.Count, totalCount);
            Debug.LogWarning($"[GameSpawner] Jogador {clientId} desconectou antes do início da partida. Sincronização: {readyCount}/{totalCount}");

            UpdateLoadingScreenSyncClientRpc(readyCount, Mathf.Max(1, totalCount));

            if (totalCount > 0 && readyCount >= totalCount)
            {
                if (initialSpawnTimeoutCoroutine != null)
                {
                    StopCoroutine(initialSpawnTimeoutCoroutine);
                    initialSpawnTimeoutCoroutine = null;
                }
                StartCoroutine(StartSynchronizedMatchRoutine());
            }
            return;
        }

        // Se a partida ainda não havia começado ou já terminou, não precisa avaliar vitória/derrota
        if (!isGameActive || isGameOver) return;

        // REGRA 1: "se o hunter cair ou kitar o jogo termina"
        // Com múltiplos Hunters: se um Hunter desconectar, a partida só termina se TODOS os Hunters tiverem kitado
        if (activeHunterClientIds.Contains(clientId))
        {
            activeHunterClientIds.Remove(clientId);
            Debug.LogWarning($"[GameSpawner] Um Hunter (Client ID {clientId}) desconectou ou kitou da sala! Hunters restantes: {activeHunterClientIds.Count}");

            if (activeHunterClientIds.Count == 0)
            {
                Debug.LogWarning("[GameSpawner] Todos os Hunters desconectaram! Fim de jogo.");
                EndGame(survivorsWon: true, subtitleReason: "ALL HUNTERS DISCONNECTED");
                return;
            }
            else
            {
                Debug.Log($"[GameSpawner] A partida continua! Ainda há {activeHunterClientIds.Count} Hunter(s) ativo(s) na partida.");
            }
        }

        // REGRA 2: "e somente se todos os player normais kitarem que o jogo termina (se tiver pelomenos 1 e o hunter o jogo continua)"
        if (activeNormalPlayerClientIds.Contains(clientId))
        {
            activeNormalPlayerClientIds.Remove(clientId);
            UpdateAliveSurvivorsCount();
            Debug.Log($"[GameSpawner] Um sobrevivente (Client ID {clientId}) desconectou. Sobreviventes restantes: {activeNormalPlayerClientIds.Count}");

            if (initialNormalPlayerCount > 0 && activeNormalPlayerClientIds.Count == 0)
            {
                Debug.LogWarning("[GameSpawner] Todos os sobreviventes normais desconectaram! Fim de jogo.");
                EndGame(survivorsWon: false, subtitleReason: "ALL SURVIVORS DISCONNECTED");
                return;
            }
            else
            {
                Debug.Log($"[GameSpawner] O jogo continua! Ainda há {activeNormalPlayerClientIds.Count} sobrevivente(s) ativo(s) e Hunter(s) na partida.");
                RebalanceNpcModelsForLivingSurvivors();
            }
        }
    }

    [Header("Spawn da Morte (Ponto de Queda)")]
    [Tooltip("Posição onde o Hunter spawna e despenca dos céus")]
    [SerializeField] private Vector3 hunterPlazaSpawnPoint = new Vector3(-56.09f, 0.45f, -33.03f);

    public Vector3 GetHunterPlazaSpawnPosition()
    {
        Vector3 targetPos = hunterPlazaSpawnPoint;

        // Procura a superfície real do piso logo abaixo da posição de spawn
        RaycastHit[] hits = Physics.RaycastAll(targetPos + Vector3.up * 20f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider != null && hit.collider.name.ToLower().Contains("referencia")) continue;
            return hit.point + Vector3.up * 0.45f;
        }

        if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit navHit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return navHit.position + Vector3.up * 0.40f;
        }

        return targetPos;
    }

    /// <summary>
    /// Calcula a quantidade de Hunters proporcional ao número total de jogadores na partida:
    /// - Até 5 jogadores: 1 Hunter (ex: até 5 jogadores -> 4 sobreviventes e 1 hunter)
    /// - 6 jogadores em diante (6 a 8 jogadores): 2 Hunters (ex: numa partida de 6 jogadores -> 4 sobreviventes e 2 hunters)
    /// </summary>
    public static int GetHunterCount(int totalPlayers)
    {
        if (totalPlayers >= 6) return 2;
        return 1;
    }

    private void SpawnInitialPlayers()
    {
        if (!IsServer || initialSpawnCompleted) return;

        // Garante que o skybox aleatório da partida esteja selecionado e sincronizado
        if (MatchSkyboxManager.Instance != null && MatchSkyboxManager.Instance.CurrentSkyboxIndex < 0)
        {
            MatchSkyboxManager.Instance.SelectAndApplyRandomSkybox();
        }

        EnsurePrefabsLoaded();

        if (playerPrefab == null)
        {
            Debug.LogError("[GameSpawner] playerPrefab não está configurado!");
            return;
        }

        // Filtra clientes conectados que ainda não possuem um PlayerObject ativo
        List<ulong> clientsToSpawn = new List<ulong>();
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
            {
                continue;
            }
            clientsToSpawn.Add(clientId);
        }

        if (clientsToSpawn.Count == 0) return;

        initialSpawnCompleted = true;

        // Embaralha aleatoriamente a lista de jogadores conectados (Fisher-Yates) para sorteio justo dos papéis
        for (int i = 0; i < clientsToSpawn.Count; i++)
        {
            int r = Random.Range(i, clientsToSpawn.Count);
            ulong temp = clientsToSpawn[i];
            clientsToSpawn[i] = clientsToSpawn[r];
            clientsToSpawn[r] = temp;
        }

        // Determina a quantidade de Hunters proporcional ao número total de jogadores conectados
        int totalPlayers = clientsToSpawn.Count;
        int hunterCount = Mathf.Clamp(GetHunterCount(totalPlayers), 1, totalPlayers);

        activeHunterClientIds.Clear();
        activeNormalPlayerClientIds.Clear();

        for (int i = 0; i < clientsToSpawn.Count; i++)
        {
            ulong clientId = clientsToSpawn[i];
            if (i < hunterCount)
            {
                activeHunterClientIds.Add(clientId);
            }
            else
            {
                activeNormalPlayerClientIds.Add(clientId);
            }
        }
        initialNormalPlayerCount = activeNormalPlayerClientIds.Count;

        Debug.Log($"[GameSpawner] === Início de Partida ===\n" +
                  $"Total de jogadores conectados: {totalPlayers}\n" +
                  $"Hunters sorteados ({activeHunterClientIds.Count}): {string.Join(", ", activeHunterClientIds)}\n" +
                  $"Sobreviventes normais ({initialNormalPlayerCount}): {string.Join(", ", activeNormalPlayerClientIds)}");

        // Sorteia modelos visuais distintos para cada jogador sobrevivente
        int totalPlayerModels = 10;
        if (playerPrefab != null)
        {
            var pRand = playerPrefab.GetComponent<PlayerCharacterRandomizer>();
            if (pRand != null && pRand.characters != null && pRand.characters.Length > 0)
            {
                totalPlayerModels = pRand.characters.Length;
            }
        }

        List<int> availableModels = new List<int>(totalPlayerModels);
        for (int m = 0; m < totalPlayerModels; m++) availableModels.Add(m);
        for (int m = 0; m < availableModels.Count; m++)
        {
            int r = Random.Range(m, availableModels.Count);
            int tmp = availableModels[m];
            availableModels[m] = availableModels[r];
            availableModels[r] = tmp;
        }

        survivorModelAssignments.Clear();
        List<int> survivorModelIndices = new List<int>();

        int s = 0;
        foreach (ulong sId in activeNormalPlayerClientIds)
        {
            int modelIdx = (s < availableModels.Count) ? availableModels[s] : Random.Range(0, totalPlayerModels);
            survivorModelAssignments[sId] = modelIdx;
            survivorModelIndices.Add(modelIdx);
            s++;
        }

        // Se a cutscene de abertura do avião estiver disponível e ainda não tiver sido reproduzida, dispara a cena antes do spawn
        if (AirplaneCrashCutsceneController.Instance != null && !AirplaneCrashCutsceneController.Instance.HasPlayedCutscene)
        {
            StartCoroutine(PlayCutsceneThenSpawnRoutine(clientsToSpawn, survivorModelIndices));
            return;
        }

        ExecutePlayerSpawning(clientsToSpawn, survivorModelIndices);
    }

    private IEnumerator PlayCutsceneThenSpawnRoutine(List<ulong> clientsToSpawn, List<int> survivorModelIndices)
    {
        Debug.Log("[GameSpawner] Iniciando cutscene cinemática do avião em queda (Premonição)...");
        AirplaneCrashCutsceneController.Instance.StartCutsceneClientRpc(survivorModelIndices != null ? survivorModelIndices.ToArray() : new int[0]);

        while (AirplaneCrashCutsceneController.Instance != null && AirplaneCrashCutsceneController.Instance.IsCutsceneActive)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.2f);
        Debug.Log("[GameSpawner] Cutscene do avião concluída com sucesso! Prosseguindo com spawn dos jogadores e descida da Morte.");
        ExecutePlayerSpawning(clientsToSpawn, survivorModelIndices);
    }

    private void ExecutePlayerSpawning(List<ulong> clientsToSpawn, List<int> survivorModelIndices)
    {
        List<Vector3> usedSpawnsThisWave = new List<Vector3>();

        // Garante que os Hunters sejam spawnados primeiro na lista para que suas instâncias
        // já estejam inicializadas antes da replicação dos sobreviventes
        clientsToSpawn.Sort((a, b) =>
        {
            bool aIsHunter = activeHunterClientIds.Contains(a);
            bool bIsHunter = activeHunterClientIds.Contains(b);
            if (aIsHunter && !bIsHunter) return -1;
            if (!aIsHunter && bIsHunter) return 1;
            return a.CompareTo(b);
        });

        foreach (ulong clientId in clientsToSpawn)
        {
            bool isHunter = activeHunterClientIds.Contains(clientId);
            Vector3 spawnPos;
            Quaternion spawnRot;

            // Tanto o Hunter quanto os sobreviventes spawnam de forma aleatória e segura na cidade
            GetSpawnTransform(usedSpawnsThisWave, out spawnPos, out spawnRot);
            usedSpawnsThisWave.Add(spawnPos);

            GameObject prefabToUse = (isHunter && hunterPrefab != null) ? hunterPrefab : playerPrefab;

            Debug.Log($"[GameSpawner] Spawnando Client ID {clientId} como {(isHunter ? "HUNTER (Spawn Aleatório)" : "PLAYER")} na posição {spawnPos}");

            GameObject playerInstance = Instantiate(prefabToUse, spawnPos, spawnRot);

            if (!isHunter && survivorModelAssignments.TryGetValue(clientId, out int assignedModel))
            {
                var rand = playerInstance.GetComponent<PlayerCharacterRandomizer>();
                if (rand != null)
                {
                    rand.SetCharacterIndex(assignedModel);
                }
            }

            playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }

        // Spawna os NPCs na cidade junto com a rodada inicial de jogadores
        if (spawnNpcsOnStart && !hasSpawnedNpcs)
        {
            SpawnAllNPCs(usedSpawnsThisWave, survivorModelIndices);
            hasSpawnedNpcs = true;
        }

        isGameActive = true;
        UpdateAliveSurvivorsCount();

        // Inicializa as tasks da partida proporcionalmente à quantidade de sobreviventes (Tabela: 1->3, 2->6, ..., N->N*3)
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.InitializeTasksForMatch(initialNormalPlayerCount);
        }

        StartCoroutine(StartMatchTimerRoutine());
    }

    private IEnumerator StartMatchTimerRoutine()
    {
        matchTimeRemainingNetVar.Value = matchDurationSeconds;
        isMatchTimerRunningNetVar.Value = false;
        UpdateAliveSurvivorsCount();

        // Aguarda os 5 segundos da descida da Morte dos céus
        yield return new WaitForSeconds(hunterIntroDurationSeconds);

        if (isGameActive && !isGameOver)
        {
            isMatchTimerRunningNetVar.Value = true;
            Debug.Log("[GameSpawner] 5 segundos de spawn da Morte concluídos! Cronômetro de partida de 5 minutos iniciado.");
            GameAudioManager.Instance?.StartGameplayMusic(1.5f);

            // A Morte aterrissou no solo! Agora a partida começou de verdade: libera as tasks para os sobreviventes
            if (TaskManager.Instance != null)
            {
                TaskManager.Instance.StartInitialTasksForSurvivors();
            }
        }
    }

    /// <summary>
    /// Recalcula e sincroniza via rede a quantidade atual de sobreviventes vivos na partida.
    /// </summary>
    public void UpdateAliveSurvivorsCount()
    {
        if (!IsServer) return;

        var players = FindObjectsByType<PlayerController>();
        int alive = 0;
        foreach (var p in players)
        {
            if (p == null || p.IsHunter) continue;
            if (!p.IsDead && !p.IsGhost)
            {
                alive++;
            }
        }

        aliveSurvivorsCountNetVar.Value = alive;
    }

    /// <summary>
    /// Verifica se todos os sobreviventes reais foram eliminados (mortos ou em modo fantasma).
    /// Finaliza o jogo declarando vitória dos Hunters caso não restem sobreviventes vivos.
    /// </summary>
    public void CheckAllSurvivorsEliminated()
    {
        if (!IsServer || !isGameActive || isGameOver || initialNormalPlayerCount == 0) return;

        UpdateAliveSurvivorsCount();

        if (aliveSurvivorsCountNetVar.Value == 0)
        {
            Debug.Log("[GameSpawner] Todos os sobreviventes foram eliminados! Vitória dos Hunters.");
            EndGame(survivorsWon: false, subtitleReason: "TODOS OS SOBREVIVENTES ELIMINADOS");
        }
        else
        {
            // Se ainda restam sobreviventes vivos, readapta as proporções dos NPCs na cidade
            RebalanceNpcModelsForLivingSurvivors();
        }
    }

    /// <summary>
    /// Verifica se todos os Hunters na partida perderam todas as suas 3 vidas (por matar NPCs errados).
    /// Se todos os Hunters forem eliminados, finaliza a partida com vitória dos sobreviventes!
    /// </summary>
    public void CheckHunterLivesEliminated()
    {
        if (!IsServer || !isGameActive || isGameOver) return;

        var hunters = FindObjectsByType<HunterKnockdownLogic>();
        bool anyHunterAlive = false;
        foreach (var h in hunters)
        {
            if (h != null && h.CurrentLives.Value > 0)
            {
                var pc = h.GetComponent<PlayerController>();
                if (pc == null || !pc.IsDead)
                {
                    anyHunterAlive = true;
                    break;
                }
            }
        }

        if (!anyHunterAlive && hunters.Length > 0)
        {
            Debug.Log("[GameSpawner] Todos os Hunters perderam suas 3 vidas por matar NPCs errados! Vitória dos sobreviventes.");
            EndGame(survivorsWon: true, subtitleReason: "A MORTE FOI DERROTADA");
        }
    }

    /// <summary>
    /// Vitória dos sobreviventes por conclusão de todas as tasks do mapa!
    /// Segunda condição de vitória dos sobreviventes.
    /// </summary>
    public void TriggerSurvivorsWinByTasks()
    {
        if (!IsServer || isGameOver) return;
        Debug.Log("[GameSpawner] Todas as tasks foram concluídas pelos sobreviventes! Vitória dos sobreviventes.");
        EndGame(survivorsWon: true, subtitleReason: "TODAS AS TAREFAS CONCLUÍDAS - SOBREVIVENTES ESCAPARAM");
    }

    /// <summary>
    /// Finaliza o jogo de forma sincronizada e retorna todos ao Lobby.
    /// Exibe VICTORY / DEFEAT de forma simples em inglês para cada jogador,
    /// oculta o HUD e revela a aura roxa dos sobreviventes vivos para os Hunters por 5 segundos.
    /// </summary>
    private void EndGame(bool survivorsWon, string subtitleReason)
    {
        if (!IsServer || isGameOver) return;

        isGameOver = true;
        isGameActive = false;
        isMatchTimerRunningNetVar.Value = false;

        if (gradualRebalanceCoroutine != null)
        {
            StopCoroutine(gradualRebalanceCoroutine);
            gradualRebalanceCoroutine = null;
        }

        Debug.Log($"[GameSpawner] =====================================\n" +
                  $"[GameSpawner] FIM DE JOGO: survivorsWon={survivorsWon}, reason={subtitleReason}\n" +
                  $"[GameSpawner] =====================================");

        // Notifica todos os clientes para exibir a tela de fim de jogo durante exatamente 5 segundos
        ShowGameOverClientRpc(survivorsWon, subtitleReason, 5.0f);

        StartCoroutine(EndGameRoutine());
    }

    private IEnumerator EndGameRoutine()
    {
        yield return new WaitForSeconds(5.0f);

        // Exibe a tela de carregamento para todos os jogadores ao retornar ao saguão
        ShowReturnToLobbyLoadingClientRpc();
        yield return new WaitForSeconds(0.35f);

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
            if (NetworkManager.Singleton.NetworkConfig != null)
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = false;
            }

            if (NetworkManager.Singleton.SceneManager != null)
            {
                Debug.Log("[GameSpawner] Carregando cena de Lobby (JoinByBrowsing)...");
                NetworkManager.Singleton.SceneManager.LoadScene("JoinByBrowsing", LoadSceneMode.Single);
            }
        }
    }

    [ClientRpc]
    private void ShowReturnToLobbyLoadingClientRpc()
    {
        if (GameLoadingScreenController.Instance != null)
        {
            GameLoadingScreenController.Instance.Show("RETORNANDO AO SAGUÃO...", "Sincronizando estatísticas da partida...", minDisplayTime: 2.0f, autoDismiss: true);
        }
    }

    [ClientRpc]
    private void ShowGameOverClientRpc(bool survivorsWon, string subtitleReason, float duration)
    {
        isGameOver = true;
        isGameActive = false;
        gameOverCountdown = duration;

        // Oculta completamente o HUD durante os 5 segundos da conclusão do jogo
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.SetHudVisible(false);
        }

        // Determina se este cliente local venceu ou perdeu
        bool amIHunter = PlayerController.IsLocalPlayerHunter();
        bool localWon = survivorsWon ? !amIHunter : amIHunter;

        gameOverTitle = localWon ? "VITÓRIA" : "DERROTA";
        string ptSubtitle = TranslateGameOverReason(subtitleReason, survivorsWon);
        string ptDescription = GetGameOverDescription(localWon, amIHunter);
        gameOverSubtitle = ptSubtitle;

        // Exibe o painel de Fim de Jogo no estilo visual padronizado do Menu (UI Toolkit)
        if (InGamePauseMenuController.Instance != null)
        {
            InGamePauseMenuController.Instance.ShowGameOver(localWon, gameOverTitle, ptSubtitle, ptDescription, duration);
        }
        else
        {
            GameAudioManager.Instance?.StopGameplayMusic(1.0f);
            if (localWon)
            {
                GameAudioManager.Instance?.PlayVictoryFireworks();
            }
        }

        StartCoroutine(GameOverCountdownRoutine(duration));
    }

    private static string TranslateGameOverReason(string rawReason, bool survivorsWon)
    {
        if (string.IsNullOrEmpty(rawReason))
        {
            return survivorsWon ? "SOBREVIVENTES ESCAPARAM!" : "A MORTE VENCEU!";
        }

        string upper = rawReason.ToUpperInvariant();
        if (upper.Contains("TIME") || upper.Contains("TEMPO"))
            return "TEMPO ESGOTADO - SOBREVIVENTES ESCAPARAM!";
        if (upper.Contains("ALL SURVIVORS") || (upper.Contains("ELIMINATED") && !upper.Contains("HUNTER")) || upper.Contains("SOBREVIVENTES ELIMINADOS"))
            return "TODOS OS SOBREVIVENTES FORAM ELIMINADOS!";
        if ((upper.Contains("HUNTER") || upper.Contains("MORTE")) && (upper.Contains("ELIMINATED") || upper.Contains("LIVES") || upper.Contains("DERROTADA")))
            return "A MORTE FOI DERROTADA! (PERDEU TODAS AS VIDAS)";
        if (upper.Contains("TASK") || upper.Contains("TAREFA"))
            return "TODAS AS TAREFAS FORAM CONCLUÍDAS!";

        return rawReason;
    }

    private static string GetGameOverDescription(bool localWon, bool amIHunter)
    {
        if (localWon)
        {
            return amIHunter
                ? "Você expurgou todos os sobreviventes e cumpriu seu dever mortal!"
                : "Você escapou da Morte e sobreviveu à partida!";
        }
        else
        {
            return amIHunter
                ? "Os sobreviventes escaparam do seu alcance desta vez..."
                : "A Morte encontrou você e seus companheiros...";
        }
    }

    /// <summary>
    /// Transmite o evento de eliminação de um jogador para todos os clientes conectados exibirem no Kill Feed FPS.
    /// Invocado pelo Servidor.
    /// </summary>
    public void BroadcastElimination(string killerName, string victimName, bool isHunterKill, string weaponType = "scythe")
    {
        if (!IsServer) return;
        BroadcastEliminationClientRpc(killerName ?? "A Morte", victimName ?? "Jogador", isHunterKill, weaponType ?? "scythe");
    }

    [ClientRpc]
    private void BroadcastEliminationClientRpc(string killerName, string victimName, bool isHunterKill, string weaponType)
    {
        Debug.Log($"[KillFeed] Eliminação transmitida: {killerName} eliminou {victimName} via {weaponType} (isHunterKill={isHunterKill})");
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.AddKillFeedEntry(killerName, victimName, isHunterKill, weaponType);
        }
    }

    private IEnumerator GameOverCountdownRoutine(float duration)
    {
        float timer = duration;
        while (timer > 0f)
        {
            gameOverCountdown = timer;
            if (InGamePauseMenuController.Instance != null)
            {
                InGamePauseMenuController.Instance.UpdateGameOverCountdown(timer, duration);
            }
            yield return null;
            timer -= Time.deltaTime;
        }
        gameOverCountdown = 0f;
    }

    private void OnGUI()
    {
        // Se o painel moderno em UI Toolkit já estiver visível, dispensa a renderização do OnGUI antigo
        if (InGamePauseMenuController.IsGameOverVisible)
        {
            return;
        }

        if (string.IsNullOrEmpty(gameOverTitle)) return;

        float bannerWidth = 560f;
        float bannerHeight = 136f;
        float x = (Screen.width - bannerWidth) * 0.5f;
        float y = Screen.height * 0.12f;

        // Fundo escuro semitransparente moderno e limpo
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.82f);
        GUI.DrawTexture(new Rect(x, y, bannerWidth, bannerHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        bool isVictory = gameOverTitle == "VITÓRIA" || gameOverTitle == "VICTORY";
        Color titleColor = isVictory 
            ? new Color(1.0f, 0.84f, 0.15f) // Dourado brilhante
            : new Color(1.0f, 0.25f, 0.25f); // Vermelho carmesim

        GUIStyle shadowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 52,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0f, 0f, 0f, 0.9f) }
        };

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 52,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = titleColor }
        };

        GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.92f, 0.92f, 0.95f) }
        };

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.75f, 0.75f, 0.80f, 0.85f) }
        };

        // Sombra e Título (VITÓRIA / DERROTA)
        GUI.Label(new Rect(x + 2, y + 10 + 2, bannerWidth, 55), gameOverTitle, shadowStyle);
        GUI.Label(new Rect(x, y + 10, bannerWidth, 55), gameOverTitle, titleStyle);

        // Subtítulo descritivo em português
        if (!string.IsNullOrEmpty(gameOverSubtitle))
        {
            GUI.Label(new Rect(x, y + 68, bannerWidth, 26), gameOverSubtitle, subtitleStyle);
        }

        // Contador regressivo de retorno ao lobby (5s)
        GUI.Label(new Rect(x, y + 96, bannerWidth, 22), $"Retornando ao saguão em {Mathf.CeilToInt(gameOverCountdown)}s...", subStyle);
    }

    /// <summary>
    /// Spawna a quantidade configurada de NPCs aleatoriamente pelas ruas da cidade (até 80 NPCs).
    /// Os NPCs são clones/cópias dos jogadores sobreviventes:
    /// - Se há 1 jogador sobrevivente, todos os NPCs são cópias desse jogador.
    /// - Se há 2 jogadores sobreviventes, 50% dos NPCs copiam um jogador e 50% copiam o outro (ex: 40 e 40).
    /// - Se há N jogadores sobreviventes, a proporção é distribuída igualmente entre eles e embaralhada pelo mapa.
    /// </summary>
    public void SpawnAllNPCs(List<Vector3> occupiedPositions = null, List<int> survivorModelIndices = null)
    {
        if (!IsServer || npcPrefab == null) return;
        EnsureNavMeshActive();

        if (LobbyReadyManager.ConfiguredNpcCount > 0)
        {
            npcCount = Mathf.Clamp(LobbyReadyManager.ConfiguredNpcCount, 1, 80);
        }
        else
        {
            npcCount = Mathf.Clamp(PlayerPrefs.GetInt("Lobby_NpcCount", npcCount), 1, 80);
        }

        if (npcCount <= 0) return;

        int totalNpcModels = 10;
        var npcRandComp = npcPrefab.GetComponent<PlayerCharacterRandomizer>();
        if (npcRandComp != null && npcRandComp.characters != null && npcRandComp.characters.Length > 0)
        {
            totalNpcModels = npcRandComp.characters.Length;
        }

        // Se survivorModelIndices não foi fornecido, tenta coletar dos sobreviventes ativos
        if (survivorModelIndices == null || survivorModelIndices.Count == 0)
        {
            survivorModelIndices = GetLivingSurvivorModelIndices();
        }

        // Distribui proporcionalmente os modelos dos sobreviventes para cada NPC
        List<int> npcModelList = new List<int>(npcCount);
        if (survivorModelIndices != null && survivorModelIndices.Count > 0)
        {
            int numSurvivors = survivorModelIndices.Count;
            for (int i = 0; i < npcCount; i++)
            {
                int survivorIdx = i % numSurvivors;
                npcModelList.Add(survivorModelIndices[survivorIdx]);
            }

            // Embaralha para que os clones fiquem distribuídos uniformemente por toda a cidade
            for (int i = 0; i < npcModelList.Count; i++)
            {
                int r = Random.Range(i, npcModelList.Count);
                int temp = npcModelList[i];
                npcModelList[i] = npcModelList[r];
                npcModelList[r] = temp;
            }
        }
        else
        {
            // Fallback caso não haja nenhum sobrevivente na partida (ex: teste solo apenas com o Hunter)
            for (int i = 0; i < npcCount; i++)
            {
                npcModelList.Add(Random.Range(0, totalNpcModels));
            }
        }

        List<Vector3> usedSpawns = occupiedPositions ?? new List<Vector3>();

        int spawnedCount = 0;
        for (int i = 0; i < npcCount; i++)
        {
            Vector3 spawnPos;
            if (TryFindClearCitySpawn(usedSpawns, out spawnPos))
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out UnityEngine.AI.NavMeshHit hit, 3.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    spawnPos = hit.position;
                }
            }
            else if (CityPedestrianNetwork.Instance != null && CityPedestrianNetwork.Instance.HasNodes)
            {
                var randomNode = CityPedestrianNetwork.Instance.GetRandomNode();
                spawnPos = randomNode != null ? randomNode.transform.position : new Vector3(cityCenter.x, 0.1f, cityCenter.y);
            }
            else
            {
                float fallbackAngle = Random.Range(0f, Mathf.PI * 2f);
                float fallbackDist = Mathf.Sqrt(Random.Range(0f, 1f)) * cityRadius;
                spawnPos = new Vector3(cityCenter.x + Mathf.Cos(fallbackAngle) * fallbackDist, 0.1f, cityCenter.y + Mathf.Sin(fallbackAngle) * fallbackDist);
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out UnityEngine.AI.NavMeshHit finalNavHit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                spawnPos = finalNavHit.position;
            }

            usedSpawns.Add(spawnPos);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject npcInstance = Instantiate(npcPrefab, spawnPos, spawnRot);

            int chosenModel = (i < npcModelList.Count) ? npcModelList[i] : -1;
            var rand = npcInstance.GetComponent<PlayerCharacterRandomizer>();
            if (rand != null && chosenModel >= 0)
            {
                rand.SetCharacterIndex(chosenModel);
            }

            npcInstance.GetComponent<NetworkObject>().Spawn(true);
            spawnedCount++;
        }

        Debug.Log($"[GameSpawner] {spawnedCount}/{npcCount} NPCs instanciados e espalhados pela cidade com sucesso! (Clonados de {survivorModelIndices?.Count ?? 0} sobreviventes)");
    }

    /// <summary>
    /// Retorna a lista dos índices de modelos visuais de todos os sobreviventes reais atualmente vivos.
    /// Ignora a Morte (Hunters), sobreviventes mortos (IsDead) e espectadores (IsGhost).
    /// </summary>
    public List<int> GetLivingSurvivorModelIndices()
    {
        List<int> models = new List<int>();

        if (PlayerController.ActivePlayers != null)
        {
            foreach (var p in PlayerController.ActivePlayers)
            {
                if (p == null || p.IsHunter || p.IsDead || p.IsGhost || !p.IsSpawned) continue;

                if (activeNormalPlayerClientIds.Count > 0 && !activeNormalPlayerClientIds.Contains(p.OwnerClientId))
                {
                    continue;
                }

                int model = -1;
                var rand = p.GetComponent<PlayerCharacterRandomizer>();
                if (rand != null && rand.selectedIndex.Value >= 0)
                {
                    model = rand.selectedIndex.Value;
                }
                else if (survivorModelAssignments.TryGetValue(p.OwnerClientId, out int assignedModel))
                {
                    model = assignedModel;
                }

                if (model >= 0)
                {
                    models.Add(model);
                }
            }
        }

        if (models.Count == 0 && activeNormalPlayerClientIds.Count > 0)
        {
            var allPlayers = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p == null || p.IsHunter || p.IsDead || p.IsGhost || !p.IsSpawned) continue;
                if (!activeNormalPlayerClientIds.Contains(p.OwnerClientId)) continue;

                int model = -1;
                var rand = p.GetComponent<PlayerCharacterRandomizer>();
                if (rand != null && rand.selectedIndex.Value >= 0)
                {
                    model = rand.selectedIndex.Value;
                }
                else if (survivorModelAssignments.TryGetValue(p.OwnerClientId, out int assignedModel))
                {
                    model = assignedModel;
                }

                if (model >= 0)
                {
                    models.Add(model);
                }
            }
        }

        return models;
    }

    /// <summary>
    /// Escolhe o melhor modelo para um novo NPC ou NPC substituto,
    /// selecionando o modelo do sobrevivente vivo que atualmente possui a menor quantidade de NPCs na cidade.
    /// Isso mantém as proporções sempre perfeitamente equilibradas.
    /// </summary>
    public int GetBalancedModelForNewNpc()
    {
        List<int> livingModels = GetLivingSurvivorModelIndices();
        if (livingModels == null || livingModels.Count == 0)
        {
            return Random.Range(0, 10);
        }

        if (livingModels.Count == 1)
        {
            return livingModels[0];
        }

        // Conta quantos NPCs ativos no momento possuem cada modelo dos sobreviventes vivos
        Dictionary<int, int> counts = new Dictionary<int, int>();
        foreach (int m in livingModels)
        {
            if (!counts.ContainsKey(m)) counts[m] = 0;
        }

        var activeNpcs = NPCController.ActiveNpcs;
        if (activeNpcs != null)
        {
            foreach (var npc in activeNpcs)
            {
                if (npc == null || npc.IsDead || !npc.gameObject.activeInHierarchy) continue;
                var rand = npc.GetComponent<PlayerCharacterRandomizer>();
                if (rand != null && counts.ContainsKey(rand.selectedIndex.Value))
                {
                    counts[rand.selectedIndex.Value]++;
                }
            }
        }

        int minCount = int.MaxValue;
        List<int> leastCommonModels = new List<int>();
        foreach (var kvp in counts)
        {
            if (kvp.Value < minCount)
            {
                minCount = kvp.Value;
                leastCommonModels.Clear();
                leastCommonModels.Add(kvp.Key);
            }
            else if (kvp.Value == minCount)
            {
                leastCommonModels.Add(kvp.Key);
            }
        }

        return leastCommonModels[Random.Range(0, leastCommonModels.Count)];
    }

    [Header("Transição Gradual de Aparência dos NPCs")]
    [Tooltip("Duração total em segundos para que todos os NPCs adaptem sua aparência aos sobreviventes vivos")]
    [SerializeField] private float npcAppearanceTransitionDuration = 15.0f;

    private Coroutine gradualRebalanceCoroutine;

    /// <summary>
    /// Recalcula e redistribui proporcionalmente a aparência dos NPCs ativos na cidade
    /// para corresponderem exclusivamente aos jogadores sobreviventes que continuam vivos na partida.
    /// A transição ocorre de forma gradual ao longo de 15 segundos para não alertar os Hunters.
    /// Exemplo: com 2 players (40 NPCs cada), se o player 1 morre, os 40 NPCs que o copiavam
    /// passam a copiar gradualmente o player 2 ao longo de 15 segundos, totalizando 100% de clones do sobrevivente restante.
    /// </summary>
    public void RebalanceNpcModelsForLivingSurvivors()
    {
        if (!IsServer || isGameOver) return;

        List<int> livingSurvivorModels = GetLivingSurvivorModelIndices();
        if (livingSurvivorModels == null || livingSurvivorModels.Count == 0)
        {
            Debug.Log("[GameSpawner] RebalanceNpcModels: Nenhum sobrevivente vivo restante.");
            if (gradualRebalanceCoroutine != null)
            {
                StopCoroutine(gradualRebalanceCoroutine);
                gradualRebalanceCoroutine = null;
            }
            return;
        }

        var activeNpcs = NPCController.ActiveNpcs;
        if (activeNpcs == null || activeNpcs.Count == 0)
        {
            Debug.Log("[GameSpawner] RebalanceNpcModels: Nenhum NPC ativo encontrado.");
            return;
        }

        List<NPCController> validNpcs = new List<NPCController>();
        foreach (var npc in activeNpcs)
        {
            if (npc != null && !npc.IsDead && npc.gameObject.activeInHierarchy)
            {
                validNpcs.Add(npc);
            }
        }

        int totalNpcs = validNpcs.Count;
        if (totalNpcs == 0) return;

        int numLiving = livingSurvivorModels.Count;
        int baseCount = totalNpcs / numLiving;
        int remainder = totalNpcs % numLiving;

        // Calcula a cota alvo para cada modelo
        Dictionary<int, int> targetCounts = new Dictionary<int, int>();
        for (int i = 0; i < numLiving; i++)
        {
            int model = livingSurvivorModels[i];
            int countForThis = baseCount + (i < remainder ? 1 : 0);
            if (targetCounts.ContainsKey(model))
            {
                targetCounts[model] += countForThis;
            }
            else
            {
                targetCounts[model] = countForThis;
            }
        }

        HashSet<int> validLivingModelsSet = new HashSet<int>(targetCounts.Keys);
        Dictionary<int, List<NPCController>> currentNpcGroups = new Dictionary<int, List<NPCController>>();
        foreach (int m in targetCounts.Keys)
        {
            currentNpcGroups[m] = new List<NPCController>();
        }

        List<NPCController> npcsNeedingReassignment = new List<NPCController>();

        // Agrupa os NPCs ativos
        foreach (var npc in validNpcs)
        {
            var rand = npc.GetComponent<PlayerCharacterRandomizer>();
            int currentModel = (rand != null) ? rand.selectedIndex.Value : -1;

            if (currentModel >= 0 && validLivingModelsSet.Contains(currentModel))
            {
                currentNpcGroups[currentModel].Add(npc);
            }
            else
            {
                // NPC pertence a um jogador que morreu (ou é inválido), precisa mudar
                npcsNeedingReassignment.Add(npc);
            }
        }

        // Se algum grupo vivo tiver mais NPCs que sua cota alvo, o excesso é movido para reatribuição
        foreach (int m in targetCounts.Keys)
        {
            int target = targetCounts[m];
            var list = currentNpcGroups[m];
            while (list.Count > target)
            {
                var extraNpc = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                npcsNeedingReassignment.Add(extraNpc);
            }
        }

        // Embaralha os NPCs que precisam de nova aparência para distribuir aleatoriamente pelo mapa
        for (int i = 0; i < npcsNeedingReassignment.Count; i++)
        {
            int r = Random.Range(i, npcsNeedingReassignment.Count);
            var temp = npcsNeedingReassignment[i];
            npcsNeedingReassignment[i] = npcsNeedingReassignment[r];
            npcsNeedingReassignment[r] = temp;
        }

        // Prepara a lista de reatribuições (NPC -> Novo Modelo)
        List<System.Tuple<NPCController, int>> reassignments = new List<System.Tuple<NPCController, int>>();
        int reassignIdx = 0;
        foreach (int m in targetCounts.Keys)
        {
            int target = targetCounts[m];
            int currentCount = currentNpcGroups[m].Count;
            int needed = target - currentCount;

            for (int k = 0; k < needed && reassignIdx < npcsNeedingReassignment.Count; k++)
            {
                var npcToChange = npcsNeedingReassignment[reassignIdx++];
                reassignments.Add(new System.Tuple<NPCController, int>(npcToChange, m));
            }
        }

        // Interrompe qualquer transição gradual anterior para recalcular de forma suave
        if (gradualRebalanceCoroutine != null)
        {
            StopCoroutine(gradualRebalanceCoroutine);
            gradualRebalanceCoroutine = null;
        }

        if (reassignments.Count > 0)
        {
            string distributionReport = string.Join(", ", targetCounts.Select(kvp => $"Modelo {kvp.Key}: {kvp.Value} NPCs"));
            Debug.Log($"[GameSpawner] Iniciando rebalanceamento gradual de aparência: {reassignments.Count} NPCs trocarão de modelo ao longo de {npcAppearanceTransitionDuration:F1}s para não alertar os Hunters. Nova meta: {distributionReport}");
            gradualRebalanceCoroutine = StartCoroutine(GradualRebalanceNpcModelsRoutine(reassignments, npcAppearanceTransitionDuration));
        }
        else
        {
            Debug.Log($"[GameSpawner] Proporção de NPCs já está equilibrada para os {numLiving} sobrevivente(s) vivo(s).");
        }
    }

    /// <summary>
    /// Corrotina que altera a aparência dos NPCs de forma gradual e distribuída ao longo de 15 segundos,
    /// evitando que todos os NPCs mudem ao mesmo tempo e alertem os Hunters da morte do jogador.
    /// </summary>
    private IEnumerator GradualRebalanceNpcModelsRoutine(List<System.Tuple<NPCController, int>> reassignments, float totalDuration)
    {
        if (reassignments == null || reassignments.Count == 0) yield break;

        int totalCount = reassignments.Count;
        float interval = totalDuration / totalCount;
        int successfulChanges = 0;

        for (int i = 0; i < totalCount; i++)
        {
            if (!IsServer || !isGameActive || isGameOver)
            {
                yield break;
            }

            var item = reassignments[i];
            var npc = item.Item1;
            int targetModel = item.Item2;

            if (npc != null && !npc.IsDead && npc.gameObject.activeInHierarchy)
            {
                var rand = npc.GetComponent<PlayerCharacterRandomizer>();
                if (rand != null && rand.selectedIndex.Value != targetModel)
                {
                    rand.SetCharacterIndex(targetModel);
                    successfulChanges++;
                }
            }

            // Aguarda o intervalo antes de alterar o próximo NPC
            if (interval > 0.001f)
            {
                yield return new WaitForSeconds(interval);
            }
            else
            {
                yield return null;
            }
        }

        Debug.Log($"[GameSpawner] Rebalanceamento gradual concluído com sucesso! {successfulChanges} NPCs adaptaram sua aparência.");
        gradualRebalanceCoroutine = null;
    }

    /// <summary>
    /// Obtém uma posição e rotação seguras para o spawn do jogador.
    /// Prioriza o algoritmo procedural anti-colisão na cidade se useRandomCitySpawn estiver ativo.
    /// </summary>
    public void GetSpawnTransform(List<Vector3> occupiedPositions, out Vector3 spawnPos, out Quaternion spawnRot)
    {
        if (useRandomCitySpawn)
        {
            if (TryFindClearCitySpawn(occupiedPositions, out spawnPos))
            {
                spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                return;
            }
        }

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int randomIndex = Random.Range(0, spawnPoints.Length);
            var pt = spawnPoints[randomIndex];
            if (pt != null)
            {
                spawnPos = pt.position;
                spawnRot = pt.rotation;
                return;
            }
        }

        spawnPos = new Vector3(0f, 0.45f, -10f);
        spawnRot = Quaternion.identity;
    }

    /// <summary>
    /// Varre a área urbana por Raycast e validação de cápsula de colisão (Physics.OverlapCapsule)
    /// garantindo que o local esteja completamente livre de prédios, carros, árvores e muros.
    /// </summary>
    public bool TryFindClearCitySpawn(List<Vector3> occupiedPositions, out Vector3 position)
    {
        position = Vector3.zero;

        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            // Amostragem circular uniforme dentro do raio delimitado pela referência
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Mathf.Sqrt(Random.Range(0f, 1f)) * cityRadius;
            float rx = cityCenter.x + Mathf.Cos(angle) * dist;
            float rz = cityCenter.y + Mathf.Sin(angle) * dist;

            Vector3 rayOrigin = new Vector3(rx, 35f, rz);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 45f))
            {
                if (hit.point.y < minGroundY || hit.point.y > maxGroundY)
                    continue;

                string hitName = hit.collider.gameObject.name.ToLower();
                string hitParent = hit.collider.transform.parent != null ? hit.collider.transform.parent.name.ToLower() : "";
                if (hitName.Contains("car") || hitParent.Contains("vehicle") ||
                    hitName.Contains("building") || hitParent.Contains("building") ||
                    hitName.Contains("tree"))
                {
                    continue;
                }

                Vector3 candidatePos = hit.point + Vector3.up * 0.45f;

                if (occupiedPositions != null)
                {
                    bool tooClose = false;
                    foreach (var occ in occupiedPositions)
                    {
                        if (Vector3.Distance(occ, candidatePos) < minDistanceBetweenPlayers)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;
                }

                Vector3 capBottom = candidatePos + Vector3.up * 0.5f;
                Vector3 capTop = candidatePos + Vector3.up * 1.5f;
                Collider[] overlaps = Physics.OverlapCapsule(capBottom, capTop, 0.45f);

                bool blocked = false;
                foreach (var col in overlaps)
                {
                    if (col == hit.collider) continue;
                    if (col is TerrainCollider) continue;
                    if (col.isTrigger) continue;

                    string colName = col.gameObject.name.ToLower();

                    if (col.CompareTag("Street") || col.CompareTag("Grass") ||
                        colName.Contains("road") || colName.Contains("pavement") ||
                        colName.Contains("roundabout") || colName.Contains("parking"))
                    {
                        continue;
                    }

                    blocked = true;
                    break;
                }

                if (!blocked)
                {
                    position = candidatePos;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Spawna um novo NPC substituto em algum ponto ou calçada da cidade quando a Morte elimina um pedestre.
    /// </summary>
    public void SpawnReplacementNPC()
    {
        if (!IsServer) return;
        EnsurePrefabsLoaded();
        if (npcPrefab == null) return;
        EnsureNavMeshActive();

        Vector3 spawnPos;
        if (CityPedestrianNetwork.Instance != null && CityPedestrianNetwork.Instance.HasNodes)
        {
            var randomNode = CityPedestrianNetwork.Instance.GetRandomNode();
            spawnPos = randomNode != null ? randomNode.transform.position : new Vector3(cityCenter.x, 0.1f, cityCenter.y);
        }
        else if (TryFindClearCitySpawn(new List<Vector3>(), out spawnPos))
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out UnityEngine.AI.NavMeshHit hit, 3.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                spawnPos = hit.position;
            }
        }
        else
        {
            float fallbackAngle = Random.Range(0f, Mathf.PI * 2f);
            float fallbackDist = Mathf.Sqrt(Random.Range(0f, 1f)) * cityRadius;
            spawnPos = new Vector3(cityCenter.x + Mathf.Cos(fallbackAngle) * fallbackDist, 0.1f, cityCenter.y + Mathf.Sin(fallbackAngle) * fallbackDist);
        }

        if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out UnityEngine.AI.NavMeshHit finalNavHit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            spawnPos = finalNavHit.position;
        }

        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject npcInstance = Instantiate(npcPrefab, spawnPos, spawnRot);

        int chosenModel = GetBalancedModelForNewNpc();
        var rand = npcInstance.GetComponent<PlayerCharacterRandomizer>();
        if (rand != null && chosenModel >= 0)
        {
            rand.SetCharacterIndex(chosenModel);
        }

        npcInstance.GetComponent<NetworkObject>().Spawn(true);
        Debug.Log($"[GameSpawner] Novo NPC substituto spawnado com sucesso na posição {spawnPos} com modelo {chosenModel}!");
    }

    private void OnDrawGizmosSelected()
    {
        // Desenha o limite circular jogável da cidade
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.85f);
        int segments = 64;
        Vector3 prev = new Vector3(cityCenter.x + cityRadius, 0.5f, cityCenter.y);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = new Vector3(cityCenter.x + Mathf.Cos(a) * cityRadius, 0.5f, cityCenter.y + Mathf.Sin(a) * cityRadius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        Gizmos.color = new Color(0f, 0.7f, 1f, 0.4f);
        Gizmos.DrawWireSphere(new Vector3(cityCenter.x, 0.5f, cityCenter.y), cityRadius);
    }
}