using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gerencia o sistema de tarefas (Tasks) no jogo multiplayer:
/// - Calcula o total de tasks necessárias com base na quantidade de jogadores Sobreviventes (Tabela: 1->3, 2->6, ..., N->N*3).
/// - Controla o progresso global das tasks sincronizado via rede (NetworkVariable).
/// - Valida e contabiliza as tarefas concluídas pelos jogadores.
/// - Regra: A primeira task fica disponível assim que a partida começa.
/// - Regra: A cada vez que o player completar uma task, 10s depois libera outra.
/// - Regra: 3.35s após a conclusão de uma task, é emitido um alerta para os Hunters com contorno vermelho e direcional/target vermelho no local.
/// - Regra: O alerta dura exatamente 5.0s para os Hunters, e então o contorno volta ao cinza normal e a task pode ser sorteada novamente.
/// - Regra: Os players nunca fazem as mesmas tasks ao mesmo tempo (cada um possui um objetivo exclusivo).
/// - Dispara a vitória dos Sobreviventes quando todas as tasks forem concluídas.
/// </summary>
public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Configuração de Interação")]
    [Tooltip("Tempo em segundos que o jogador (e o NPC ao fingir) precisa segurar o botão esquerdo para completar a task")]
    [SerializeField] private float taskInteractionDuration = 10.0f;
    public float TaskInteractionDuration => taskInteractionDuration;

    [Header("Configuração de Alertas e Timers")]
    [Tooltip("Tempo de atraso após a conclusão da task antes de alertar os Hunters")]
    [SerializeField] private float hunterAlertDelayAfterCompletion = 3.35f;
    [Tooltip("Duração do alerta (contorno vermelho e indicador) visível para os Hunters")]
    [SerializeField] private float hunterAlertDuration = 5.0f;
    [Tooltip("Tempo de espera para o player receber sua próxima task após concluir a anterior")]
    [SerializeField] private float playerNextTaskCooldown = 10.0f;
    public float PlayerNextTaskCooldown => playerNextTaskCooldown;

    [Header("Proporção / Chances de Sorteio dos Tipos de Tasks (Chances Iguais: 1/3 para cada)")]
    [Tooltip("Peso proporcional para sortear tarefas de Interagir com Objetos de cenário (Padrão: 33.33%)")]
    [Range(0f, 100f)]
    [SerializeField] private float kickObjectWeight = 33.333f;

    [Tooltip("Peso proporcional para sortear tarefa de Chutar a Morte - Hunter (Padrão: 33.33%)")]
    [Range(0f, 100f)]
    [SerializeField] private float kickHunterWeight = 33.333f;

    [Tooltip("Peso proporcional para sortear tarefa de Dançar com 2 ou 3 na Frente da Morte (Padrão: 33.33%)")]
    [Range(0f, 100f)]
    [SerializeField] private float danceNearHunterWeight = 33.333f;

    [Tooltip("Se ativado, evita atribuir a mesma tarefa da Morte (ou categoria) duas vezes consecutivas para o mesmo jogador caso haja outras tarefas disponíveis")]
    [SerializeField] private bool preventConsecutiveHunterTasks = true;

    [Tooltip("Permite que um jogador tenha a task de Chutar o Hunter e outro jogador tenha a de Dançar perto do Hunter ao mesmo tempo (cada uma continua exclusiva para 1 jogador)")]
    [SerializeField] private bool allowSimultaneousDifferentHunterTasks = false;

    public float KickObjectWeight
    {
        get => kickObjectWeight;
        set => kickObjectWeight = Mathf.Max(0f, value);
    }

    public float KickHunterWeight
    {
        get => kickHunterWeight;
        set => kickHunterWeight = Mathf.Max(0f, value);
    }

    public float DanceNearHunterWeight
    {
        get => danceNearHunterWeight;
        set => danceNearHunterWeight = Mathf.Max(0f, value);
    }

    public bool PreventConsecutiveHunterTasks
    {
        get => preventConsecutiveHunterTasks;
        set => preventConsecutiveHunterTasks = value;
    }

    public bool AllowSimultaneousDifferentHunterTasks
    {
        get => allowSimultaneousDifferentHunterTasks;
        set => allowSimultaneousDifferentHunterTasks = value;
    }

    /// <summary>
    /// Retorna as porcentagens calculadas para cada categoria de tarefa com base nos pesos configurados.
    /// Exemplo com pesos 50, 25, 25: (50%, 25%, 25%).
    /// </summary>
    public (float objectPct, float kickHunterPct, float danceHunterPct) GetConfiguredPercentages()
    {
        float total = kickObjectWeight + kickHunterWeight + danceNearHunterWeight;
        if (total <= 0.0001f) return (0f, 0f, 0f);
        return (
            (kickObjectWeight / total) * 100f,
            (kickHunterWeight / total) * 100f,
            (danceNearHunterWeight / total) * 100f
        );
    }

    /// <summary>
    /// Ajusta os pesos de sorteio das tarefas programaticamente.
    /// </summary>
    public void SetTaskWeights(float objectWeight, float kickHunter, float danceHunter)
    {
        kickObjectWeight = Mathf.Max(0f, objectWeight);
        kickHunterWeight = Mathf.Max(0f, kickHunter);
        danceNearHunterWeight = Mathf.Max(0f, danceHunter);
    }

    private void OnValidate()
    {
        kickObjectWeight = Mathf.Max(0f, kickObjectWeight);
        kickHunterWeight = Mathf.Max(0f, kickHunterWeight);
        danceNearHunterWeight = Mathf.Max(0f, danceNearHunterWeight);

        if (kickObjectWeight <= 0f && kickHunterWeight <= 0f && danceNearHunterWeight <= 0f)
        {
            kickObjectWeight = 33.333f;
            kickHunterWeight = 33.333f;
            danceNearHunterWeight = 33.333f;
        }
    }

    [Header("Configuração de Inatividade de Tasks")]
    [Tooltip("Tempo limite para o sobrevivente concluir uma tarefa antes de ser marcado para a Morte (1 min e 25s = 85s)")]
    [SerializeField] private float taskInactivityTimeout = 85.0f;
    public float TaskInactivityTimeout => taskInactivityTimeout;

    [Tooltip("Duração do alerta de 'Marcado para a Morte' visível para o Hunter")]
    [SerializeField] private float inactivityAlertDuration = 10.0f;
    public float InactivityAlertDuration => inactivityAlertDuration;

    [Tooltip("Tempo de espera para liberar a próxima task após o término do alerta de inatividade")]
    [SerializeField] private float postAlertTaskCooldown = 10.0f;
    public float PostAlertTaskCooldown => postAlertTaskCooldown;

    [Header("Configuração de Quantidade de Tarefas")]
    [Tooltip("Quantidade mínima de tarefas que uma partida pode exigir (Padrão: 7)")]
    [SerializeField] private int minTasksRequired = 7;

    [Tooltip("Quantidade de tarefas adicionadas por sobrevivente na partida (Padrão: 3)")]
    [SerializeField] private int tasksPerSurvivor = 3;

    public int MinTasksRequired
    {
        get => minTasksRequired;
        set => minTasksRequired = Mathf.Max(1, value);
    }

    public int TasksPerSurvivor
    {
        get => tasksPerSurvivor;
        set => tasksPerSurvivor = Mathf.Max(1, value);
    }

    [Header("Sincronização de Rede")]
    public readonly NetworkVariable<int> TotalTasksRequired = new NetworkVariable<int>(
        7,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public readonly NetworkVariable<int> CompletedTasksCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Constantes de Tipos de Tarefas")]
    public const int NoTaskIndex = -1;
    public const int HunterKickTaskIndex = -100;
    public const string HunterKickTaskName = "Chutar a Morte";
    public const int HunterDanceTaskIndex = -101;
    public const string HunterDanceTaskName = "Dançar Próximo à Morte [2 ou 3]";

    public static bool IsValidTaskIndex(int taskIndex)
    {
        return taskIndex >= 0 || taskIndex == HunterKickTaskIndex || taskIndex == HunterDanceTaskIndex;
    }

    public static bool IsHunterTask(int taskIndex)
    {
        return taskIndex == HunterKickTaskIndex || taskIndex == HunterDanceTaskIndex;
    }

    // Registro no Servidor das tasks concluídas por cada jogador (clientId -> set de índices de tasks)
    private readonly Dictionary<ulong, HashSet<int>> playerCompletedTasks = new Dictionary<ulong, HashSet<int>>();

    // Registro no Servidor da task objetivo atualmente atribuída para cada jogador (clientId -> taskIndex, ou -1 se em espera)
    private readonly Dictionary<ulong, int> playerAssignedTasks = new Dictionary<ulong, int>();

    // Registro da última task atribuída a cada jogador (para evitar repetir Hunter duas vezes seguidas)
    private readonly Dictionary<ulong, int> playerPreviousTasks = new Dictionary<ulong, int>();

    // Registro no Servidor do timestamp (Time.time) em que a próxima task pode ser sorteada para o jogador (cooldown de 10s)
    private readonly Dictionary<ulong, float> playerCooldownExpiryTimes = new Dictionary<ulong, float>();

    // Registro no Servidor das tasks atualmente em alerta de Hunter / pós-conclusão (não podem ser sorteadas até o alerta expirar)
    private readonly HashSet<int> activeAlertTasks = new HashSet<int>();

    // Controle no Servidor dos timers de inatividade por clientId
    private readonly Dictionary<ulong, Coroutine> playerInactivityCoroutines = new Dictionary<ulong, Coroutine>();
    private readonly HashSet<ulong> activeMarkedForDeathClients = new HashSet<ulong>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnNetworkSpawn()
    {
        TotalTasksRequired.OnValueChanged += HandleTasksChanged;
        CompletedTasksCount.OnValueChanged += HandleTasksChanged;

        // Atualiza a interface gráfica imediatamente ao entrar na rede
        UpdateHUDTaskProgress();
    }

    public override void OnNetworkDespawn()
    {
        TotalTasksRequired.OnValueChanged -= HandleTasksChanged;
        CompletedTasksCount.OnValueChanged -= HandleTasksChanged;

        if (IsServer)
        {
            ClearAllInactivityTimers();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void HandleTasksChanged(int previousValue, int newValue)
    {
        UpdateHUDTaskProgress();
    }

    private void UpdateHUDTaskProgress()
    {
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.SetTaskProgress(CompletedTasksCount.Value, TotalTasksRequired.Value);
        }
    }

    /// <summary>
    /// Retorna a quantidade de tasks totais de acordo com as regras do jogo:
    /// - O mínimo de tasks começa em 7.
    /// - Escala proporcionalmente por sobrevivente com base na quantidade configurada (padrão: 3 por sobrevivente).
    /// </summary>
    public static int GetRequiredTasksForPlayerCount(int playerCount, int minTasks = 7, int perSurvivor = 3)
    {
        int safeCount = Mathf.Max(1, playerCount);
        return Mathf.Max(minTasks, safeCount * perSurvivor);
    }

    /// <summary>
    /// Retorna a quantidade de tasks totais para esta instância com base nas propriedades configuradas no Inspector.
    /// </summary>
    public int GetRequiredTasksForPlayerCount(int playerCount)
    {
        return GetRequiredTasksForPlayerCount(playerCount, minTasksRequired, tasksPerSurvivor);
    }

    /// <summary>
    /// Inicializa a contagem de tarefas para a partida (Invocado pelo Servidor no início do jogo).
    /// A 1ª task de cada jogador fica disponível assim que a partida começa.
    /// </summary>
    public void InitializeTasksForMatch(int survivorCount)
    {
        if (!IsServer) return;

        int required = GetRequiredTasksForPlayerCount(survivorCount);
        TotalTasksRequired.Value = required;
        CompletedTasksCount.Value = 0;
        playerCompletedTasks.Clear();
        playerAssignedTasks.Clear();
        playerPreviousTasks.Clear();
        activeAlertTasks.Clear();
        ClearAllInactivityTimers();

        TaskObject.EnsureTasksSorted();

        // As tasks agora só são atribuídas quando a Morte cair do céu (StartInitialTasksForSurvivors)
        Debug.Log($"[TaskManager] Estrutura de tasks preparada com {survivorCount} sobrevivente(s). Total de tasks necessárias: {required}. Aguardando a Morte aterrissar...");
        UpdateHUDTaskProgress();
    }

    /// <summary>
    /// Atribui as primeiras tarefas para todos os sobreviventes da partida.
    /// Chamado exclusivamente após a Morte cair do céu e aterrissar no solo (quando a partida começa de verdade).
    /// </summary>
    public void StartInitialTasksForSurvivors()
    {
        if (!IsServer) return;

        if (GameSpawner.Instance != null && GameSpawner.Instance.ActiveNormalPlayerClientIds != null)
        {
            foreach (var clientId in GameSpawner.Instance.ActiveNormalPlayerClientIds)
            {
                AssignNewTargetTaskForPlayer(clientId, excludeCurrentlyAssignedToOthers: true);
            }
        }

        Debug.Log($"[TaskManager] A Morte aterrissou no solo! Primeiras tasks distribuídas para {playerAssignedTasks.Count} sobrevivente(s).");
        UpdateHUDTaskProgress();
    }

    /// <summary>
    /// Verifica se há pelo menos um Hunter vivo e ativo na partida.
    /// </summary>
    public bool IsAnyHunterAliveAndActive()
    {
        if (GameSpawner.Instance == null || GameSpawner.Instance.IsGameOver || !GameSpawner.Instance.IsMatchActive)
            return false;

        if (GameSpawner.Instance.ActiveHunterClientIds == null || GameSpawner.Instance.ActiveHunterClientIds.Count == 0)
            return false;

        return true;
    }

    private void Update()
    {
        if (!IsServer) return;

        // Se algum jogador estiver com a task de chutar a Morte, mas o Hunter morreu ou desconectou, reatribui
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsMatchActive && !GameSpawner.Instance.IsGameOver)
        {
            bool hunterAlive = IsAnyHunterAliveAndActive();
            if (!hunterAlive)
            {
                List<ulong> needReassign = null;
                foreach (var kvp in playerAssignedTasks)
                {
                    if (kvp.Value == HunterKickTaskIndex || kvp.Value == HunterDanceTaskIndex)
                    {
                        if (needReassign == null) needReassign = new List<ulong>();
                        needReassign.Add(kvp.Key);
                    }
                }

                if (needReassign != null)
                {
                    foreach (var cId in needReassign)
                    {
                        Debug.LogWarning($"[TaskManager] Hunter indisponível para Client {cId}. Reatribuindo para task de objeto.");
                        AssignNewTargetTaskForPlayer(cId, excludeCurrentlyAssignedToOthers: true, force: true);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Sorteia e atribui uma nova tarefa objetivo para o jogador sobrevivente especificado.
    /// Suporta três tipos de tasks com probabilidades proporcionais configuráveis:
    /// 1. Chutar objetos tasks (cenário)
    /// 2. Chutar o Hunter (Morte)
    /// 3. Dançar próximo à Morte
    /// Regra: Os players nunca fazem as mesmas tasks ao mesmo tempo (garante exclusividade total).
    /// </summary>
    public int AssignNewTargetTaskForPlayer(ulong clientId, bool excludeCurrentlyAssignedToOthers, bool force = false)
    {
        if (!IsServer) return NoTaskIndex;

        // Proteção 1: Se o jogador já possui uma tarefa válida atribuída, NUNCA sobrescrever mid-action a menos que force == true!
        if (!force && playerAssignedTasks.TryGetValue(clientId, out int currentAssigned) && IsValidTaskIndex(currentAssigned))
        {
            Debug.Log($"[TaskManager] Client {clientId} já possui task ativa #{currentAssigned}. Mantendo a task atual sem interrupções.");
            return currentAssigned;
        }

        // Proteção 2: Se o jogador está dentro do cooldown de 10s pós-conclusão, NUNCA sortear prematuramente!
        if (!force && playerCooldownExpiryTimes.TryGetValue(clientId, out float expireTime) && Time.time < expireTime)
        {
            Debug.Log($"[TaskManager] Client {clientId} ainda está no cooldown de próxima task ({expireTime - Time.time:F1}s restantes). Aguardando o tempo regulamentar.");
            return NoTaskIndex;
        }

        TaskObject.EnsureTasksSorted();
        var allTasks = TaskObject.AllTasks;
        if (allTasks == null || allTasks.Count == 0)
        {
            Debug.LogWarning("[TaskManager] Nenhuma Task encontrada no cenário para atribuir!");
            return NoTaskIndex;
        }

        if (!playerCompletedTasks.TryGetValue(clientId, out var completedSet))
        {
            completedSet = new HashSet<int>();
            playerCompletedTasks[clientId] = completedSet;
        }

        // Tarefas ocupadas no momento (REGRA: nenhum player pode fazer a mesma task ao mesmo tempo)
        var busyTasks = new HashSet<int>();
        bool isKickHunterBusy = false;
        bool isDanceHunterBusy = false;

        if (excludeCurrentlyAssignedToOthers)
        {
            foreach (var kvp in playerAssignedTasks)
            {
                if (kvp.Key != clientId)
                {
                    if (kvp.Value >= 0)
                    {
                        busyTasks.Add(kvp.Value);
                    }
                    else if (kvp.Value == HunterKickTaskIndex)
                    {
                        isKickHunterBusy = true;
                    }
                    else if (kvp.Value == HunterDanceTaskIndex)
                    {
                        isDanceHunterBusy = true;
                    }
                }
            }
        }

        // Se tarefas simultâneas diferentes no Hunter NÃO estiverem liberadas,
        // qualquer tarefa ativa no Hunter ocupa ambos os tipos (Kick e Dance)
        if (!allowSimultaneousDifferentHunterTasks && (isKickHunterBusy || isDanceHunterBusy))
        {
            isKickHunterBusy = true;
            isDanceHunterBusy = true;
        }

        // Tarefas em alerta para o Hunter não podem ser sorteadas até o alerta expirar (5s)
        foreach (int alertTask in activeAlertTasks)
        {
            busyTasks.Add(alertTask);
        }

        // 1. Candidatos preferenciais: tarefas de objeto que este jogador ainda NÃO fez e que NÃO estão ocupadas
        List<int> candidates = new List<int>();
        for (int i = 0; i < allTasks.Count; i++)
        {
            if (allTasks[i] == null) continue;
            if (!busyTasks.Contains(i) && !completedSet.Contains(i))
            {
                candidates.Add(i);
            }
        }

        // 2. Se o jogador já fez todas as tarefas de objeto no mapa, permite sortear as livres que voltaram ao normal
        if (candidates.Count == 0)
        {
            for (int i = 0; i < allTasks.Count; i++)
            {
                if (allTasks[i] == null) continue;
                if (!busyTasks.Contains(i))
                {
                    candidates.Add(i);
                }
            }
        }

        bool hunterAvailable = IsAnyHunterAliveAndActive();
        bool canAssignObjects = (candidates.Count > 0) && (kickObjectWeight > 0f);
        bool canAssignKick = hunterAvailable && !isKickHunterBusy && (kickHunterWeight > 0f);
        bool canAssignDance = hunterAvailable && !isDanceHunterBusy && (danceNearHunterWeight > 0f);

        // Verifica a última tarefa realizada por este jogador para evitar repetição consecutiva
        playerPreviousTasks.TryGetValue(clientId, out int prevTaskIndex);

        float effectiveObjectWeight = canAssignObjects ? kickObjectWeight : 0f;
        float effectiveKickWeight = canAssignKick ? kickHunterWeight : 0f;
        float effectiveDanceWeight = canAssignDance ? danceNearHunterWeight : 0f;

        if (preventConsecutiveHunterTasks)
        {
            if (prevTaskIndex == HunterKickTaskIndex && (effectiveObjectWeight > 0f || effectiveDanceWeight > 0f))
            {
                effectiveKickWeight = 0f;
            }
            else if (prevTaskIndex == HunterDanceTaskIndex && (effectiveObjectWeight > 0f || effectiveKickWeight > 0f))
            {
                effectiveDanceWeight = 0f;
            }
        }

        float totalWeight = effectiveObjectWeight + effectiveKickWeight + effectiveDanceWeight;
        int chosenIndex = -1;

        if (totalWeight > 0.0001f)
        {
            float roll = UnityEngine.Random.Range(0f, totalWeight);
            if (roll < effectiveObjectWeight)
            {
                chosenIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }
            else if (roll < effectiveObjectWeight + effectiveKickWeight)
            {
                chosenIndex = HunterKickTaskIndex;
            }
            else
            {
                chosenIndex = HunterDanceTaskIndex;
            }
        }
        else
        {
            // Fallback de contingência caso os pesos efetivos somem zero
            if (candidates.Count > 0)
            {
                chosenIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }
            else if (hunterAvailable && !isKickHunterBusy && !isDanceHunterBusy)
            {
                chosenIndex = (UnityEngine.Random.value < 0.5f) ? HunterKickTaskIndex : HunterDanceTaskIndex;
            }
            else if (hunterAvailable && !isKickHunterBusy)
            {
                chosenIndex = HunterKickTaskIndex;
            }
            else if (hunterAvailable && !isDanceHunterBusy)
            {
                chosenIndex = HunterDanceTaskIndex;
            }
        }

        // Se todas as tasks do mapa e o Hunter estiverem ocupados/indisponíveis:
        if (chosenIndex == -1)
        {
            Debug.LogWarning($"[TaskManager] Nenhuma task livre no momento para o Client {clientId}. Ficará aguardando liberação.");
            playerAssignedTasks[clientId] = NoTaskIndex;
            AssignTargetTaskClientRpc(clientId, NoTaskIndex);
            return NoTaskIndex;
        }

        playerCooldownExpiryTimes.Remove(clientId);
        playerAssignedTasks[clientId] = chosenIndex;
        playerPreviousTasks[clientId] = chosenIndex;

        string taskDisplayName = (chosenIndex == HunterKickTaskIndex)
            ? HunterKickTaskName
            : (chosenIndex == HunterDanceTaskIndex)
                ? HunterDanceTaskName
                : (chosenIndex >= 0 && chosenIndex < allTasks.Count && allTasks[chosenIndex] != null ? allTasks[chosenIndex].TaskName : $"Task #{chosenIndex}");

        string categoryName = (chosenIndex == HunterKickTaskIndex) ? "Chutar a Morte"
            : (chosenIndex == HunterDanceTaskIndex) ? "Dançar na Frente da Morte [2 ou 3]"
            : "Interagir com Objeto";

        Debug.Log($"[TaskManager] Sorteio proporcional: [Pesos Efetivos: Obj={effectiveObjectWeight:F1}, KickHunter={effectiveKickWeight:F1}, DanceHunter={effectiveDanceWeight:F1} | Total={totalWeight:F1}] -> Atribuída task #{chosenIndex} ('{taskDisplayName}') [{categoryName}] para o Client {clientId}");

        AssignTargetTaskClientRpc(clientId, chosenIndex);

        // Inicia a contagem regressiva de inatividade (85s) para o sobrevivente concluir a tarefa
        StartInactivityTimerForPlayer(clientId);

        return chosenIndex;
    }

    /// <summary>
    /// Retorna a task objetivo atualmente atribuída a um jogador.
    /// </summary>
    public int GetPlayerAssignedTask(ulong clientId)
    {
        if (playerAssignedTasks.TryGetValue(clientId, out int idx))
        {
            return idx;
        }
        return NoTaskIndex;
    }

    /// <summary>
    /// Desassocia a task do jogador quando ele é eliminado/morre.
    /// </summary>
    public void UnassignTargetTaskForPlayer(ulong clientId)
    {
        if (!IsServer) return;
        StopInactivityTimerForPlayer(clientId);

        if (activeMarkedForDeathClients.Remove(clientId))
        {
            EndPlayerMarkedForDeathClientRpc(clientId);
        }

        playerCooldownExpiryTimes.Remove(clientId);
        playerAssignedTasks[clientId] = NoTaskIndex;
        AssignTargetTaskClientRpc(clientId, NoTaskIndex);
    }

    [Rpc(SendTo.Server)]
    public void RequestAssignedTargetTaskRpc(ulong requestingClientId)
    {
        if (!IsServer) return;
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsClientHunter(requestingClientId))
        {
            return; // Hunters não recebem tasks
        }

        // Se a partida ainda não começou de verdade (a Morte ainda está caindo do céu), não atribui ainda
        if (GameSpawner.Instance == null || !GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.IsGameOver)
        {
            return;
        }

        // 1. Se o jogador já tem uma tarefa válida ativa, apenas reenvia a mesma tarefa existente!
        if (playerAssignedTasks.TryGetValue(requestingClientId, out int assigned) && IsValidTaskIndex(assigned))
        {
            AssignTargetTaskClientRpc(requestingClientId, assigned);
            return;
        }

        // 2. Se o jogador está dentro do cooldown de 10s pós-conclusão, informa NoTaskIndex (-1) e não gera nova task
        if (playerCooldownExpiryTimes.TryGetValue(requestingClientId, out float expireTime) && Time.time < expireTime)
        {
            AssignTargetTaskClientRpc(requestingClientId, NoTaskIndex);
            return;
        }

        // 3. Apenas se realmente não tiver task e não estiver em cooldown:
        AssignNewTargetTaskForPlayer(requestingClientId, excludeCurrentlyAssignedToOthers: true);
    }

    /// <summary>
    /// Verifica se um jogador específico já completou uma tarefa específica.
    /// </summary>
    public bool HasPlayerCompletedTask(ulong clientId, int taskIndex)
    {
        if (playerCompletedTasks.TryGetValue(clientId, out var set))
        {
            return set.Contains(taskIndex);
        }
        return false;
    }

    /// <summary>
    /// Registra a conclusão de uma tarefa por um jogador real (Invocado pelo Servidor).
    /// </summary>
    public void OnPlayerCompletedTask(ulong clientId, int taskIndex)
    {
        if (!IsServer) return;

        if (GameSpawner.Instance != null && (GameSpawner.Instance.IsGameOver || !GameSpawner.Instance.IsMatchActive))
        {
            Debug.LogWarning($"[TaskManager] Tentativa de completar task rejeitada: Jogo finalizado ou inativo.");
            return;
        }

        // Hunters não podem completar tasks
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsClientHunter(clientId))
        {
            Debug.LogWarning($"[TaskManager] Tentativa de completar task rejeitada: Client {clientId} é Hunter.");
            return;
        }

        // Validação: o jogador SÓ PODE completar a tarefa objetivo marcada para ele!
        if (!playerAssignedTasks.TryGetValue(clientId, out int assignedIndex) || assignedIndex != taskIndex)
        {
            Debug.LogWarning($"[TaskManager] Tentativa de completar task #{taskIndex} rejeitada! O objetivo atual do Client {clientId} é #{assignedIndex}.");
            return;
        }

        // Inicializa o set do jogador caso não exista
        if (!playerCompletedTasks.TryGetValue(clientId, out var set))
        {
            set = new HashSet<int>();
            playerCompletedTasks[clientId] = set;
        }

        set.Add(taskIndex);

        // Desassocia a task do jogador (ele fica sem task durante os 10s regulamentares de cooldown)
        playerAssignedTasks[clientId] = NoTaskIndex;
        playerCooldownExpiryTimes[clientId] = Time.time + playerNextTaskCooldown;

        // Cancela o timer de inatividade já que a tarefa foi cumprida com sucesso
        StopInactivityTimerForPlayer(clientId);

        // Regra: A revelação (aura roxa, soulorb e direcional no Hunter) dura até o jogador fazer a task!
        // Como o jogador completou sua task agora, removemos a revelação da Morte.
        if (activeMarkedForDeathClients.Remove(clientId))
        {
            Debug.Log($"[TaskManager] Client {clientId} completou sua task #{taskIndex} e encerrou sua revelação! Removendo contorno roxo, soulorb e direcional no Hunter.");
            EndPlayerMarkedForDeathClientRpc(clientId);
        }

        CompletedTasksCount.Value = Mathf.Min(TotalTasksRequired.Value, CompletedTasksCount.Value + 1);

        Debug.Log($"[TaskManager] Sucesso! Client {clientId} completou sua task objetivo #{taskIndex}. Progresso: {CompletedTasksCount.Value}/{TotalTasksRequired.Value}");

        // Confirma para o cliente executor (limpa a task atual e o direcional)
        ConfirmTaskCompletedClientRpc(clientId, taskIndex);
        AssignTargetTaskClientRpc(clientId, NoTaskIndex);

        // Verifica condição de vitória por Tasks
        bool survivorsWon = CompletedTasksCount.Value >= TotalTasksRequired.Value;
        if (survivorsWon)
        {
            Debug.Log("[TaskManager] TODAS AS TASKS FORAM CONCLUÍDAS! Vitória dos sobreviventes.");
            if (GameSpawner.Instance != null)
            {
                GameSpawner.Instance.TriggerSurvivorsWinByTasks();
            }
        }

        // Tarefas de objeto de cenário geram alerta com contorno vermelho para os Hunters
        if (taskIndex >= 0)
        {
            activeAlertTasks.Add(taskIndex);
            StartCoroutine(HunterAlertRoutine(taskIndex, hunterAlertDelayAfterCompletion, hunterAlertDuration));
        }

        // 2. Cooldown da próxima task para o player: exatamente 10.0s após concluir a task
        if (!survivorsWon)
        {
            StartCoroutine(PlayerNextTaskRoutine(clientId, playerNextTaskCooldown));
        }
    }

    /// <summary>
    /// Rotina do alerta da Morte (Hunter):
    /// 1. Espera delayBeforeAlert (3.35s).
    /// 2. Dispara alerta vermelho (contorno + direcional) aos Hunters por alertDuration (10.0s).
    /// 3. Encerra o alerta, remove contorno vermelho e libera a task para futuros sorteios.
    /// </summary>
    private IEnumerator HunterAlertRoutine(int taskIndex, float delayBeforeAlert, float alertDuration)
    {
        yield return new WaitForSeconds(delayBeforeAlert);

        if (GameSpawner.Instance != null && (GameSpawner.Instance.IsGameOver || !GameSpawner.Instance.IsMatchActive))
        {
            activeAlertTasks.Remove(taskIndex);
            yield break;
        }

        Debug.Log($"[TaskManager] {delayBeforeAlert:F2}s após conclusão: Disparando alerta da Task #{taskIndex} para os Hunters por {alertDuration:F1}s!");
        TriggerHunterTaskAlertClientRpc(taskIndex, alertDuration);

        yield return new WaitForSeconds(alertDuration);

        Debug.Log($"[TaskManager] {alertDuration:F1}s de alerta expirados: Encerrando aviso da Task #{taskIndex} para os Hunters.");
        EndHunterTaskAlertClientRpc(taskIndex);
        activeAlertTasks.Remove(taskIndex);

        // Se havia algum jogador sem task esperando liberar, tenta atribuir agora
        CheckPendingUnassignedPlayers();
    }

    /// <summary>
    /// Rotina de espera para liberação da próxima task do sobrevivente (10.0s).
    /// </summary>
    private IEnumerator PlayerNextTaskRoutine(ulong clientId, float cooldown)
    {
        yield return new WaitForSeconds(cooldown);

        playerCooldownExpiryTimes.Remove(clientId);

        if (GameSpawner.Instance != null && !GameSpawner.Instance.IsGameOver && GameSpawner.Instance.IsMatchActive)
        {
            if (GameSpawner.Instance.ActiveNormalPlayerClientIds != null &&
                GameSpawner.Instance.ActiveNormalPlayerClientIds.Contains(clientId))
            {
                if (CompletedTasksCount.Value < TotalTasksRequired.Value)
                {
                    Debug.Log($"[TaskManager] {cooldown:F1}s decorridos! Liberando e sorteando próxima task para o Client {clientId}.");
                    AssignNewTargetTaskForPlayer(clientId, excludeCurrentlyAssignedToOthers: true, force: true);
                }
            }
        }
    }

    /// <summary>
    /// Verifica se algum jogador sobrevivente ativo está sem task e tenta atribuir caso tarefas tenham sido liberadas.
    /// </summary>
    private void CheckPendingUnassignedPlayers()
    {
        if (GameSpawner.Instance == null || GameSpawner.Instance.IsGameOver || !GameSpawner.Instance.IsMatchActive) return;
        if (CompletedTasksCount.Value >= TotalTasksRequired.Value) return;

        if (GameSpawner.Instance.ActiveNormalPlayerClientIds != null)
        {
            foreach (ulong cId in GameSpawner.Instance.ActiveNormalPlayerClientIds)
            {
                // Se o jogador já possui uma task válida ativa, JAMAIS trocar!
                if (playerAssignedTasks.TryGetValue(cId, out int assigned) && IsValidTaskIndex(assigned))
                {
                    continue;
                }

                // Se o jogador está dentro do cooldown de 10s pós-conclusão, aguarda PlayerNextTaskRoutine!
                if (playerCooldownExpiryTimes.TryGetValue(cId, out float expireTime) && Time.time < expireTime)
                {
                    continue;
                }

                // Apenas atribui se estiver sem task e fora do cooldown
                AssignNewTargetTaskForPlayer(cId, excludeCurrentlyAssignedToOthers: true);
            }
        }
    }

    [ClientRpc]
    private void AssignTargetTaskClientRpc(ulong targetClientId, int taskIndex)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == targetClientId)
        {
            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.SetAssignedTargetTask(taskIndex);
            }
        }
    }

    [ClientRpc]
    private void ConfirmTaskCompletedClientRpc(ulong targetClientId, int taskIndex)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == targetClientId)
        {
            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.OnLocalTaskCompletedConfirmed(taskIndex);
            }
        }
    }

    [ClientRpc]
    private void TriggerHunterTaskAlertClientRpc(int taskIndex, float duration)
    {
        // Executado exclusivamente pelos Hunters na máquina local
        if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.IsHunter)
        {
            var indicatorCtrl = PlayerController.LocalPlayer.GetComponent<PlayerTaskIndicatorController>();
            if (indicatorCtrl != null)
            {
                indicatorCtrl.TriggerHunterAlert(taskIndex, duration);
            }
        }
    }

    [ClientRpc]
    private void EndHunterTaskAlertClientRpc(int taskIndex)
    {
        // Executado exclusivamente pelos Hunters na máquina local
        if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.IsHunter)
        {
            var indicatorCtrl = PlayerController.LocalPlayer.GetComponent<PlayerTaskIndicatorController>();
            if (indicatorCtrl != null)
            {
                indicatorCtrl.EndHunterAlert(taskIndex);
            }
        }
    }

    #region Inactivity & Marcado para a Morte

    /// <summary>
    /// Inicia a contagem regressiva de inatividade (85s) para o sobrevivente concluir sua tarefa.
    /// Se o tempo expirar sem concluir a task, o jogador é Marcado para a Morte.
    /// </summary>
    private void StartInactivityTimerForPlayer(ulong clientId)
    {
        if (!IsServer) return;
        StopInactivityTimerForPlayer(clientId);

        Coroutine c = StartCoroutine(PlayerTaskInactivityRoutine(clientId, taskInactivityTimeout));
        playerInactivityCoroutines[clientId] = c;
    }

    /// <summary>
    /// Interrompe o timer de inatividade de um jogador (por exemplo, ao concluir a task ou morrer).
    /// </summary>
    private void StopInactivityTimerForPlayer(ulong clientId)
    {
        if (playerInactivityCoroutines.TryGetValue(clientId, out Coroutine c) && c != null)
        {
            StopCoroutine(c);
        }
        playerInactivityCoroutines.Remove(clientId);
    }

    /// <summary>
    /// Cancela e limpa todos os timers e alertas de inatividade de todos os jogadores.
    /// </summary>
    private void ClearAllInactivityTimers()
    {
        foreach (var kvp in playerInactivityCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        playerInactivityCoroutines.Clear();
        playerCooldownExpiryTimes.Clear();

        foreach (var clientId in activeMarkedForDeathClients)
        {
            EndPlayerMarkedForDeathClientRpc(clientId);
        }
        activeMarkedForDeathClients.Clear();
    }

    /// <summary>
    /// Rotina que aguarda 85s (taskInactivityTimeout) com a task atribuída.
    /// Regra: O jogador NÃO precisa receber outra task, pode continuar na mesma task!
    /// Se dentro do tempo estipulado ele não fizer a task, ele é revelado ao Hunter.
    /// Ele continua sendo revelado até fazer a task!
    /// </summary>
    private IEnumerator PlayerTaskInactivityRoutine(ulong clientId, float timeout)
    {
        yield return new WaitForSeconds(timeout);

        playerInactivityCoroutines.Remove(clientId);

        // Se a partida acabou ou cliente não está mais ativo, encerra
        if (GameSpawner.Instance == null || GameSpawner.Instance.IsGameOver || !GameSpawner.Instance.IsMatchActive)
        {
            yield break;
        }

        if (GameSpawner.Instance.ActiveNormalPlayerClientIds == null ||
            !GameSpawner.Instance.ActiveNormalPlayerClientIds.Contains(clientId))
        {
            yield break;
        }

        // Se o jogador ainda tem sua tarefa pendente e não a concluiu dentro do tempo
        if (playerAssignedTasks.TryGetValue(clientId, out int currentTaskIndex) && IsValidTaskIndex(currentTaskIndex))
        {
            Debug.LogWarning($"[TaskManager] TEMPO ESGOTADO ({timeout:F1}s) para o Client {clientId} na Task #{currentTaskIndex}! Sobrevivente REVELADO ao Hunter até que conclua a task!");

            // O jogador continua exatamente na mesma task! Não perde nem troca de task.
            // Ativa a revelação contínua (contorno roxo, soulorb e direcional no Hunter) até ele completá-la.
            activeMarkedForDeathClients.Add(clientId);
            TriggerPlayerMarkedForDeathClientRpc(clientId);
        }
    }

    [ClientRpc]
    private void TriggerPlayerMarkedForDeathClientRpc(ulong markedClientId)
    {
        Debug.Log($"[TaskManager] RPC Marcado para a Morte recebido para o Client {markedClientId}. A aura permanecerá ativa até que ele complete uma task!");

        // Atualiza o estado visual do jogador marcado (contorno roxo e soulorb visível)
        var targetPlayer = PlayerController.GetPlayerByClientId(markedClientId);
        if (targetPlayer != null)
        {
            targetPlayer.SetMarkedForDeath(true);
        }

        // Se o jogador local for o Hunter, ativa o indicador direcional/marcador contínuo apontando para o jogador marcado (0f = sem expiração por timer)
        if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.IsHunter && targetPlayer != null)
        {
            var indicatorCtrl = PlayerController.LocalPlayer.GetComponent<PlayerTaskIndicatorController>();
            if (indicatorCtrl != null)
            {
                indicatorCtrl.TriggerHunterMarkedPlayerAlert(targetPlayer, 0f);
            }
        }
    }

    [ClientRpc]
    private void EndPlayerMarkedForDeathClientRpc(ulong markedClientId)
    {
        Debug.Log($"[TaskManager] RPC Fim de Marcado para a Morte recebido para o Client {markedClientId}.");

        // Restaura o estado normal do jogador (remove contorno roxo)
        var targetPlayer = PlayerController.GetPlayerByClientId(markedClientId);
        if (targetPlayer != null)
        {
            targetPlayer.SetMarkedForDeath(false);
        }

        // Se o jogador local for o Hunter, encerra o indicador apontando para este jogador
        if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.IsHunter)
        {
            var indicatorCtrl = PlayerController.LocalPlayer.GetComponent<PlayerTaskIndicatorController>();
            if (indicatorCtrl != null)
            {
                indicatorCtrl.EndHunterMarkedPlayerAlert(markedClientId);
            }
        }
    }

    #endregion
}
