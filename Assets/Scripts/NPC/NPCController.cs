using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PlayerAnimationLogic))]
[RequireComponent(typeof(PlayerFootstepLogic))]
[RequireComponent(typeof(PlayerCharacterRandomizer))]
public class NPCController : NetworkBehaviour
{
    public enum NpcState
    {
        Idle,
        Walking,
        Conversing,
        Dancing,
        InteractingTask
    }

    [Header("Paridade de Movimento (Idêntico ao Player)")]
    [Tooltip("Velocidade de caminhada (idêntica ao PlayerMovementLogic)")]
    [SerializeField] public float walkSpeed = 1.5f;

    [Tooltip("Velocidade de interpolação da rotação (idêntica ao PlayerMovementLogic)")]
    [SerializeField] private float rotationSpeed = 10.0f;

    [Tooltip("Distância mínima para considerar que chegou ao nó")]
    [SerializeField] private float stoppingDistance = 0.5f;

    [Header("Configurações de IA e Rotina")]
    [Tooltip("Tempo mínimo em Idle (segundos)")]
    [SerializeField] private float minIdleTime = 2.5f;

    [Tooltip("Tempo máximo em Idle (segundos)")]
    [SerializeField] private float maxIdleTime = 6.0f;

    public const float SocialInteractionDistance = 3.0f;

    [Tooltip("Distância de detecção para interagir com o jogador ou outro pedestre")]
    [SerializeField] private float socialDistance = SocialInteractionDistance;

    [Tooltip("Chance (0 a 1) de cumprimentar o Player ou outro NPC que passar perto")]
    [Range(0f, 1f)]
    [SerializeField] private float socialGreetingChance = 0.35f;

    [Header("Comportamento de Dança Aleatória")]
    [Tooltip("Intervalo mínimo entre tentativas de dança espontânea")]
    [SerializeField] private float minDanceInterval = 20f;
    [Tooltip("Intervalo máximo entre tentativas de dança espontânea")]
    [SerializeField] private float maxDanceInterval = 50f;
    [Tooltip("Chance (0 a 1) do NPC dançar ao disparar o timer espontâneo")]
    [Range(0f, 1f)]
    [SerializeField] private float randomDanceChance = 0.40f;

    private float randomDanceTimer = 0f;

    [Header("Fingimento de Tarefas (Confundir Hunters)")]
    [Tooltip("Intervalo mínimo entre tentativas do NPC se aproximar de uma task")]
    [SerializeField] private float minTaskPretendInterval = 12f;
    [Tooltip("Intervalo máximo entre tentativas do NPC se aproximar de uma task")]
    [SerializeField] private float maxTaskPretendInterval = 28f;
    [Tooltip("Chance (0 a 1) do NPC decidir ir fingir uma task ao disparar o timer")]
    [Range(0f, 1f)]
    [SerializeField] private float taskPretendChance = 0.55f;

    private float taskPretendTimer = 0f;
    private TaskObject currentPretendTask = null;
    private Vector3 pretendTaskInteractionSpot = Vector3.zero;
    private bool isNavigatingToPretendTask = false;

    [Header("Comportamento de Dança Próximo à Morte (Mascarar Sobreviventes)")]
    [Tooltip("Distância máxima para detectar a presença do Hunter e avaliar se vai dançar")]
    [SerializeField] private float hunterDanceDetectionRadius = 9.0f;
    [Tooltip("Distância ideal do Hunter para o NPC parar e dançar (dentro do círculo roxo)")]
    [SerializeField] private float hunterDanceStopDistance = 2.4f;
    [Tooltip("Cooldown individual para o NPC não repetir dança para o Hunter com frequência")]
    [SerializeField] private float hunterDanceCooldown = 25.0f;
    [Tooltip("Chance (0 a 1) do NPC decidir ir dançar para o Hunter quando ele passar perto")]
    [Range(0f, 1f)]
    [SerializeField] private float hunterDanceChance = 0.35f;

    private float hunterDanceTimer = 0f;
    private float hunterDanceCooldownTimer = 0f;
    private PlayerController targetHunterToDance = null;
    private Vector3 hunterDanceSpot = Vector3.zero;
    private bool isNavigatingToHunterDance = false;
    private bool isDancingForHunter = false;

    // Limite de NPCs dançando simultaneamente para a Morte (evita aglomeração excessiva)
    private static int activeNpcsDancingForHunter = 0;

    [Header("Limites Urbanos de Segurança (Circular)")]
    [SerializeField] private Vector2 cityCenter = new Vector2(0.2f, -39.9f);
    [SerializeField] private float cityRadius = 69.0f;
    [SerializeField] private Vector2 xBounds = new Vector2(-74f, 74f);
    [SerializeField] private Vector2 zBounds = new Vector2(-114f, 34f);

    private NavMeshAgent agent;
    private PlayerAnimationLogic animationLogic;
    private PlayerFootstepLogic footstepLogic;
    private PlayerCharacterRandomizer characterRandomizer;

    private NpcState currentState = NpcState.Idle;
    private float stateTimer = 0f;
    private float socialCooldownTimer = 0f;
    private float speedSyncTimer = 0f;

    // Navegação por Calçadas (Waypoints)
    private PedestrianNode currentNode;
    private PedestrianNode previousNode;

    // Micro-olhares em Idle
    private Quaternion idleTargetRotation;
    private float microLookTimer = 0f;
    private float walkGraceTimer = 0f;

    // Sincronização e Áudio
    private Vector3 lastClientPosition;
    private Vector3 lastStuckCheckPosition;
    private float stuckCheckTimer = 0f;
    private float spontaneousPauseTimer = 15f;

    // Estado de Morte
    private bool isDead = false;
    public bool IsDead => isDead;

    // Lista estática de todos os NPCs ativos no servidor para interações sociais rápidas
    private static readonly List<NPCController> activeNpcs = new List<NPCController>();
    public static IReadOnlyList<NPCController> ActiveNpcs => activeNpcs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        activeNpcs.Clear();
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animationLogic = GetComponent<PlayerAnimationLogic>();
        footstepLogic = GetComponent<PlayerFootstepLogic>();
        characterRandomizer = GetComponent<PlayerCharacterRandomizer>();

        // Garante que o CapsuleCollider seja trigger em tempo de execução para evitar efeito trator (depenetração rígida do PhysX)
        var capsuleCol = GetComponent<CapsuleCollider>();
        if (capsuleCol != null)
        {
            capsuleCol.isTrigger = true;
        }

        // Desacopla a rotação mecânica do NavMesh para usarmos a mesma interpolação suave do jogador
        agent.updateRotation = false;
        agent.speed = walkSpeed;
        agent.acceleration = 8.0f;
        agent.stoppingDistance = stoppingDistance;
        agent.radius = 0.45f;
        agent.height = 1.8f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.avoidancePriority = 50;

        HunterPreMatchVisibility.EnsureAttached(gameObject);
    }

    void Start()
    {
        // Garante que se o NPC estiver no modo offline ou instanciado sem spawn de rede, roda a IA normalmente
        if (!IsSpawned)
        {
            InitializeOffline();
        }
    }

    private void InitializeOffline()
    {
        lastClientPosition = transform.position;
        lastStuckCheckPosition = transform.position;
        idleTargetRotation = transform.rotation;

        if (footstepLogic != null) footstepLogic.Initialize();

        if (!activeNpcs.Contains(this))
        {
            activeNpcs.Add(this);
            PlayerController.IgnoreAllCharacterCollisions();
        }

        if (agent != null && !agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
            }
        }

        SyncWalkSpeedWithPlayer();

        if (CityPedestrianNetwork.Instance != null && CityPedestrianNetwork.Instance.HasNodes)
        {
            currentNode = CityPedestrianNetwork.Instance.GetClosestNode(transform.position);
        }

        SetState(NpcState.Idle, Random.Range(1.0f, 3.0f));
        randomDanceTimer = Random.Range(minDanceInterval, maxDanceInterval);
        taskPretendTimer = Random.Range(minTaskPretendInterval, maxTaskPretendInterval);
    }

    private void OnEnable()
    {
        if (characterRandomizer == null) characterRandomizer = GetComponent<PlayerCharacterRandomizer>();
        if (characterRandomizer != null) characterRandomizer.OnCharacterVisualsChanged += HandleCharacterVisualsChanged;
    }

    private void OnDisable()
    {
        if (characterRandomizer != null) characterRandomizer.OnCharacterVisualsChanged -= HandleCharacterVisualsChanged;
    }

    private void HandleCharacterVisualsChanged(GameObject newVisual)
    {
        if (animationLogic == null) return;
        animationLogic.Initialize();

        if (currentState == NpcState.Dancing)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            if (stateTimer > 0.3f)
            {
                animationLogic.TriggerDance1Animation();
            }
        }
        else if (currentState == NpcState.Conversing)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
        }
        else
        {
            bool isMoving = false;
            if ((IsServer || !IsSpawned) && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                isMoving = agent.velocity.sqrMagnitude > 0.05f;
            }
            else
            {
                float clientSpeed = (transform.position - lastClientPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
                isMoving = clientSpeed > 0.15f;
            }
            animationLogic.HandleAnimation(isMoving ? Vector2.up : Vector2.zero, isRunning: false, isGrounded: true);
        }
    }

    public override void OnNetworkSpawn()
    {
        lastClientPosition = transform.position;
        lastStuckCheckPosition = transform.position;
        idleTargetRotation = transform.rotation;

        footstepLogic.Initialize();

        if (!activeNpcs.Contains(this))
        {
            activeNpcs.Add(this);
            PlayerController.IgnoreAllCharacterCollisions();
        }

        if (IsServer)
        {
            if (agent != null && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
                {
                    agent.Warp(navHit.position);
                }
            }

            SyncWalkSpeedWithPlayer();

            // Vincula ao nó de calçada mais próximo, se houver rede de pedestres na cena
            if (CityPedestrianNetwork.Instance != null && CityPedestrianNetwork.Instance.HasNodes)
            {
                currentNode = CityPedestrianNetwork.Instance.GetClosestNode(transform.position);
            }

            // Inicia em Idle com tempo descompassado para naturalidade
            SetState(NpcState.Idle, Random.Range(1.0f, 3.0f));

            // Descompassa o temporizador de dança espontânea para que os NPCs não dancem todos juntos
            randomDanceTimer = Random.Range(minDanceInterval, maxDanceInterval);

            // Descompassa o temporizador de fingimento de task
            taskPretendTimer = Random.Range(minTaskPretendInterval, maxTaskPretendInterval);
        }
        else
        {
            // Clientes recebem a posição via NetworkTransform
            if (agent != null) agent.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (greetingResponseCoroutine != null)
        {
            StopCoroutine(greetingResponseCoroutine);
            greetingResponseCoroutine = null;
        }

        activeNpcs.Remove(this);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        activeNpcs.Remove(this);
        if (isDancingForHunter)
        {
            isDancingForHunter = false;
            activeNpcsDancingForHunter = Mathf.Max(0, activeNpcsDancingForHunter - 1);
        }
    }

    void Update()
    {
        if (isDead) return;

        if (IsServer || !IsSpawned)
        {
            UpdateRotation();
            UpdateServerAI();
        }
        else
        {
            // No cliente, atualiza a animação com base no deslocamento real do NetworkTransform
            float clientSpeed = (transform.position - lastClientPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            bool isMoving = clientSpeed > 0.15f;
            if (animationLogic != null)
            {
                animationLogic.HandleAnimation(isMoving ? Vector2.up : Vector2.zero, isRunning: false, isGrounded: true);
            }
        }

        UpdateFootsteps();
    }

    /// <summary>
    /// Rotação suave em arco idêntica à fórmula do PlayerMovementLogic (Quaternion.Slerp a 10f).
    /// </summary>
    private void UpdateRotation()
    {
        if (currentState == NpcState.Walking)
        {
            Vector3 desiredDir = agent.desiredVelocity;
            desiredDir.y = 0f;

            if (desiredDir.sqrMagnitude > 0.04f)
            {
                Quaternion targetRot = Quaternion.LookRotation(desiredDir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }
        else if (currentState == NpcState.Idle || currentState == NpcState.Conversing || currentState == NpcState.Dancing || currentState == NpcState.InteractingTask)
        {
            // Em Idle, conversa, dança ou fingindo task, rotaciona suavemente para a direção do olhar ou interlocutor
            float rotSpeed = (currentState == NpcState.Conversing || currentState == NpcState.InteractingTask) ? 8.0f : 4.0f;
            transform.rotation = Quaternion.Slerp(transform.rotation, idleTargetRotation, rotSpeed * Time.deltaTime);
        }
    }

    private void UpdateServerAI()
    {
        if (agent == null) return;
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
            }
            if (!agent.isOnNavMesh) return;
        }

        stateTimer -= Time.deltaTime;
        if (socialCooldownTimer > 0f) socialCooldownTimer -= Time.deltaTime;

        // Temporizador de dança espontânea aleatória do NPC
        randomDanceTimer -= Time.deltaTime;
        if (randomDanceTimer <= 0f)
        {
            randomDanceTimer = Random.Range(minDanceInterval, maxDanceInterval);
            if (currentState == NpcState.Walking || currentState == NpcState.Idle)
            {
                if (Random.value < randomDanceChance)
                {
                    PerformDance(Random.value < 0.5f ? 1 : 2, 4.0f);
                }
            }
        }

        // Temporizador de fingimento de task do NPC (para confundir os Hunters)
        taskPretendTimer -= Time.deltaTime;
        if (taskPretendTimer <= 0f)
        {
            taskPretendTimer = Random.Range(minTaskPretendInterval, maxTaskPretendInterval);
            if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
            {
                // Não finge tasks antes da Morte aterrissar e a partida começar
            }
            else if (!isNavigatingToPretendTask && !isNavigatingToHunterDance && currentState != NpcState.InteractingTask && (currentState == NpcState.Walking || currentState == NpcState.Idle))
            {
                if (Random.value < taskPretendChance)
                {
                    TryStartPretendTask();
                }
            }
        }

        if (hunterDanceCooldownTimer > 0f) hunterDanceCooldownTimer -= Time.deltaTime;

        // Temporizador de aproximação e dança com a Morte (Hunter) para mascarar jogadores
        hunterDanceTimer -= Time.deltaTime;
        if (hunterDanceTimer <= 0f)
        {
            hunterDanceTimer = Random.Range(1.8f, 3.0f);
            if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
            {
                // Não dança para a Morte antes dela aterrissar
            }
            else if (!isNavigatingToHunterDance && !isNavigatingToPretendTask &&
                currentState != NpcState.InteractingTask && currentState != NpcState.Conversing && currentState != NpcState.Dancing &&
                hunterDanceCooldownTimer <= 0f && activeNpcsDancingForHunter < 2)
            {
                var closestHunter = PlayerController.GetClosestHunterPlayer(transform.position);
                if (closestHunter != null && !closestHunter.IsDead && !closestHunter.IsGhost && closestHunter.IsSpawned)
                {
                    float dist = Vector3.Distance(transform.position, closestHunter.transform.position);
                    if (dist <= hunterDanceDetectionRadius)
                    {
                        if (Random.value < hunterDanceChance)
                        {
                            TryStartHunterDance(closestHunter);
                        }
                        else
                        {
                            hunterDanceCooldownTimer = Random.Range(10f, 18f);
                        }
                    }
                }
            }
        }

        // Sincroniza periodicamente a velocidade com o Player caso mude em tempo de execução
        speedSyncTimer += Time.deltaTime;
        if (speedSyncTimer >= 2.0f)
        {
            speedSyncTimer = 0f;
            SyncWalkSpeedWithPlayer();
        }

        // Gerencia separação física e empurrão mútuo contra outros NPCs e Players
        UpdatePedestrianCollisionSeparation();

        switch (currentState)
        {
            case NpcState.Idle:
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
                ProcessMicroLooks();

                // Verifica se um jogador ou outro NPC passa por perto enquanto o NPC está parado
                CheckNearbyPedestrians();

                if (stateTimer <= 0f)
                {
                    NavigateToNextTarget();
                }
                break;

            case NpcState.Walking:
                bool isActuallyMoving = agent.velocity.sqrMagnitude > 0.05f;
                animationLogic.HandleAnimation(isActuallyMoving ? Vector2.up : Vector2.zero, isRunning: false, isGrounded: true);

                if (walkGraceTimer > 0f)
                {
                    walkGraceTimer -= Time.deltaTime;
                }
                else
                {
                    // Checa se estava a caminho da Morte (Hunter) para dançar na área circular
                    if (isNavigatingToHunterDance)
                    {
                        if (targetHunterToDance == null || targetHunterToDance.IsDead || targetHunterToDance.IsGhost || !targetHunterToDance.IsSpawned)
                        {
                            isNavigatingToHunterDance = false;
                            targetHunterToDance = null;
                        }
                        else
                        {
                            float distToHunter = Vector3.Distance(transform.position, targetHunterToDance.transform.position);
                            if (distToHunter > 14.0f)
                            {
                                isNavigatingToHunterDance = false;
                                targetHunterToDance = null;
                            }
                            else
                            {
                                // Se o Hunter se moveu mais de 3.2m do ponto original, reajusta suavemente o destino
                                if (Vector3.Distance(hunterDanceSpot, targetHunterToDance.transform.position) > 3.2f)
                                {
                                    Vector3 newDir = (transform.position - targetHunterToDance.transform.position);
                                    newDir.y = 0f;
                                    Vector3 newSpot = targetHunterToDance.transform.position + newDir.normalized * Random.Range(1.8f, 2.6f);
                                    if (NavMesh.SamplePosition(newSpot, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                                    {
                                        hunterDanceSpot = hit.position;
                                        if (agent.isOnNavMesh) agent.SetDestination(hunterDanceSpot);
                                    }
                                }

                                // Área circular de dança do Hunter (DanceRadius = 3.5m)
                                bool withinDanceArea = distToHunter <= 3.2f;
                                bool reachedSpot = (!agent.pathPending && agent.remainingDistance <= 0.75f) || Vector3.Distance(transform.position, hunterDanceSpot) <= 0.9f;

                                if (withinDanceArea && (reachedSpot || distToHunter <= hunterDanceStopDistance))
                                {
                                    isNavigatingToHunterDance = false;
                                    agent.isStopped = true;
                                    agent.velocity = Vector3.zero;
                                    agent.ResetPath();

                                    Vector3 dirToHunter = targetHunterToDance.transform.position - transform.position;
                                    dirToHunter.y = 0f;
                                    if (dirToHunter.sqrMagnitude > 0.01f)
                                    {
                                        idleTargetRotation = Quaternion.LookRotation(dirToHunter.normalized);
                                        transform.rotation = idleTargetRotation;
                                    }

                                    targetHunterToDance = null;
                                    isDancingForHunter = true;
                                    activeNpcsDancingForHunter++;

                                    // Dispara a dança dentro da área circular do Hunter (Dance 1 ou Dance 2 por 4.0s)
                                    PerformDance(Random.value < 0.5f ? 1 : 2, 4.0f);
                                    break;
                                }
                            }
                        }
                    }

                    // Checa se estava a caminho de uma task para fingir interação
                    if (isNavigatingToPretendTask)
                    {
                        if (currentPretendTask == null || !currentPretendTask.gameObject.activeInHierarchy || !currentPretendTask.gameObject.CompareTag(TaskObject.TaskTag))
                        {
                            isNavigatingToPretendTask = false;
                            currentPretendTask = null;
                        }
                        else
                        {
                            float distToTask = Vector3.Distance(transform.position, currentPretendTask.transform.position);
                            float distToSpot = Vector3.Distance(transform.position, pretendTaskInteractionSpot);

                            // O NPC SÓ pode realizar a task se estiver estritamente dentro da InteractionDistance do objeto
                            bool withinInteractionRange = distToTask <= currentPretendTask.InteractionDistance;
                            bool reachedSpot = (!agent.pathPending && agent.remainingDistance <= 0.65f) || distToSpot <= 0.85f;

                            if (withinInteractionRange && reachedSpot)
                            {
                                isNavigatingToPretendTask = false;
                                agent.isStopped = true;
                                agent.velocity = Vector3.zero;
                                agent.ResetPath();

                                Vector3 dir = currentPretendTask.transform.position - transform.position;
                                dir.y = 0f;
                                if (dir.sqrMagnitude > 0.01f)
                                {
                                    idleTargetRotation = Quaternion.LookRotation(dir.normalized);
                                }

                                // Duração do fingimento é exatamente igual à duração do player
                                float duration = TaskManager.Instance != null ? TaskManager.Instance.TaskInteractionDuration : 4.0f;
                                SetState(NpcState.InteractingTask, duration);
                                break;
                            }
                            else if (!agent.pathPending && agent.hasPath && agent.remainingDistance <= agent.stoppingDistance && !withinInteractionRange)
                            {
                                // Caminho bloqueado ou terminou longe da task: nunca fazer task no vazio!
                                isNavigatingToPretendTask = false;
                                currentPretendTask = null;
                                SetState(NpcState.Idle, Random.Range(1.0f, 2.0f));
                                break;
                            }
                        }
                    }

                    // Verifica se chegou ao nó ou destino (exige path calculado e restante menor ou proximidade física ao nó alvo)
                    bool hasValidPath = agent.hasPath && !agent.pathPending;
                    bool closeToTarget = currentNode != null && Vector3.Distance(transform.position, currentNode.Position) <= agent.stoppingDistance + 0.35f;
                    if ((hasValidPath && agent.remainingDistance <= agent.stoppingDistance) || closeToTarget)
                    {
                        OnReachedDestination();
                        break;
                    }
                }

                // Checagem social: se passar por outro pedestre
                CheckNearbyPedestrians();

                // Pausa ou dança espontânea aleatória durante a caminhada
                spontaneousPauseTimer -= Time.deltaTime;
                if (spontaneousPauseTimer <= 0f)
                {
                    spontaneousPauseTimer = Random.Range(18f, 35f);
                    float roll = Random.value;
                    if (roll < 0.08f)
                    {
                        // Chance de dançar espontaneamente por 4 segundos
                        PerformDance(Random.value < 0.5f ? 1 : 2, 4.0f);
                        break;
                    }
                    else if (roll < 0.25f)
                    {
                        SetState(NpcState.Idle, Random.Range(1.5f, 3.5f));
                        break;
                    }
                }

                // Anti-travamento (se não conseguir avançar por 3 segundos)
                stuckCheckTimer += Time.deltaTime;
                if (stuckCheckTimer >= 3.0f)
                {
                    stuckCheckTimer = 0f;
                    if (Vector3.Distance(transform.position, lastStuckCheckPosition) < 0.35f)
                    {
                        // Travado, descansa e procura novo caminho
                        SetState(NpcState.Idle, Random.Range(1.5f, 2.5f));
                    }
                    lastStuckCheckPosition = transform.position;
                }

                // Tempo limite de caminhada
                if (stateTimer <= 0f)
                {
                    SetState(NpcState.Idle, Random.Range(minIdleTime, maxIdleTime));
                }
                break;

            case NpcState.Conversing:
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
                if (stateTimer <= 0f)
                {
                    // Fim da conversa, segue o caminho
                    socialCooldownTimer = Random.Range(20f, 40f);
                    SetState(NpcState.Walking, Random.Range(10f, 20f));
                    NavigateToNextTarget();
                }
                break;

            case NpcState.Dancing:
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
                if (stateTimer <= 0f)
                {
                    if (isDancingForHunter)
                    {
                        isDancingForHunter = false;
                        activeNpcsDancingForHunter = Mathf.Max(0, activeNpcsDancingForHunter - 1);
                    }
                    if (animationLogic != null) animationLogic.StopDanceAnimation();
                    if (IsSpawned)
                    {
                        StopDanceClientRpc();
                    }
                    SetState(NpcState.Idle, Random.Range(1.0f, 2.5f));
                }
                break;

            case NpcState.InteractingTask:
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                if (currentPretendTask != null)
                {
                    Vector3 lookDir = currentPretendTask.transform.position - transform.position;
                    lookDir.y = 0f;
                    if (lookDir.sqrMagnitude > 0.01f)
                    {
                        idleTargetRotation = Quaternion.LookRotation(lookDir.normalized);
                    }
                }
                if (stateTimer <= 0f)
                {
                    // Fim do fingimento de task (exatamente igual ao player). Não contabiliza task!
                    Vector3 completionPos = currentPretendTask != null ? currentPretendTask.transform.position : transform.position;
                    GameAudioManager.Instance?.PlayTaskComplete(completionPos);
                    currentPretendTask = null;
                    isNavigatingToPretendTask = false;
                    SetState(NpcState.Idle, Random.Range(1.2f, 2.5f));
                }
                break;
        }
    }

    /// <summary>
    /// Aplica uma velocidade de repulsão suave (em m/s) quando o NPC sobrepõe outro pedestre (outro NPC, Player ou Hunter).
    /// Os personagens são intangíveis entre si (conseguem cruzar e passar por dentro uns dos outros),
    /// mas a repulsão empurra ambos suavemente para fora, impedindo que fiquem ocultos ou sobrepostos por muito tempo.
    /// A física é 100% simétrica e idêntica para NPCs, Players e Hunter.
    /// </summary>
    private void UpdatePedestrianCollisionSeparation()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || isDead) return;
        if (currentState == NpcState.InteractingTask) return; // Não empurra o NPC enquanto estiver executando uma task!

        Vector3 myPos = transform.position;
        Vector3 totalRepulsion = Vector3.zero;
        const float repulsionRadius = 0.80f;
        const float maxRepulsionSpeed = 1.8f;

        // 1. Repulsão contra outros NPCs ativos no servidor
        int npcCount = activeNpcs.Count;
        for (int i = 0; i < npcCount; i++)
        {
            var other = activeNpcs[i];
            if (other == null || other == this || other.isDead) continue;

            Vector3 otherPos = other.transform.position;
            float yDiff = Mathf.Abs(myPos.y - otherPos.y);
            if (yDiff > 1.8f) continue; // Ignora se estiverem em andares verticais diferentes

            Vector3 diff = myPos - otherPos;
            diff.y = 0f;
            float dist = diff.magnitude;

            // Repulsão só atua se estiverem de fato sobrepostos horizontalmente (menos de 0.80m)
            if (dist < repulsionRadius)
            {
                float factor = 1f - (dist / repulsionRadius); // 0 na borda (0.80m), 1 no centro exato (0m)
                Vector3 pushDir = dist > 0.02f ? (diff / dist) : (transform.right.sqrMagnitude > 0.01f ? transform.right : Vector3.right);
                totalRepulsion += pushDir * (maxRepulsionSpeed * factor);
            }
        }

        // 2. Repulsão contra Players e Hunter ativos
        var players = PlayerController.ActivePlayers;
        if (players != null)
        {
            int playerCount = players.Count;
            for (int i = 0; i < playerCount; i++)
            {
                var player = players[i];
                if (player == null || player.IsDead || player.IsGhost) continue;

                Vector3 playerPos = player.transform.position;
                float yDiff = Mathf.Abs(myPos.y - playerPos.y);
                if (yDiff > 1.8f) continue; // Ignora se estiverem em andares verticais diferentes

                Vector3 diff = myPos - playerPos;
                diff.y = 0f;
                float dist = diff.magnitude;

                // Repulsão só atua se estiverem de fato sobrepostos horizontalmente (menos de 0.80m)
                if (dist < repulsionRadius)
                {
                    float factor = 1f - (dist / repulsionRadius);
                    Vector3 pushDir = dist > 0.02f ? (diff / dist) : (transform.right.sqrMagnitude > 0.01f ? transform.right : Vector3.right);
                    totalRepulsion += pushDir * (maxRepulsionSpeed * factor);
                }
            }
        }

        if (totalRepulsion.magnitude > maxRepulsionSpeed * 1.5f)
        {
            totalRepulsion = totalRepulsion.normalized * (maxRepulsionSpeed * 1.5f);
        }

        // Aplica o deslocamento suave de repulsão no NavMeshAgent
        if (totalRepulsion.sqrMagnitude > 0.0001f)
        {
            agent.Move(totalRepulsion * Time.deltaTime);
        }
    }

    private void SetState(NpcState newState, float duration)
    {
        NpcState prevState = currentState;
        currentState = newState;
        stateTimer = duration;
        stuckCheckTimer = 0f;
        lastStuckCheckPosition = transform.position;

        // Se estava fingindo task e mudou de estado, encerra a animação de task
        if (prevState == NpcState.InteractingTask && newState != NpcState.InteractingTask)
        {
            if (animationLogic != null)
            {
                animationLogic.SetTaskInteractionAnimation(false);
            }
            if (IsSpawned)
            {
                PlayTaskInteractClientRpc(false);
            }
        }

        // Se entrou no estado de fingir task, dispara a animação de task e congela o agent
        if (newState == NpcState.InteractingTask)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                agent.ResetPath();
            }
            if (animationLogic != null)
            {
                animationLogic.SetTaskInteractionAnimation(true);
            }

            Vector3 soundPos = currentPretendTask != null ? currentPretendTask.transform.position : transform.position;
            GameAudioManager.Instance?.PlayTaskStart(soundPos);

            if (IsSpawned)
            {
                PlayTaskInteractClientRpc(true);
            }
        }

        if (currentState == NpcState.Idle)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                agent.ResetPath();
            }

            if (animationLogic != null)
            {
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            }

            if (currentNode != null && currentNode.preferredLookDirection.sqrMagnitude > 0.01f)
            {
                idleTargetRotation = Quaternion.LookRotation(currentNode.preferredLookDirection.normalized);
            }
            else
            {
                idleTargetRotation = transform.rotation;
                // Garante que não está colado de cara na parede
                AvoidFacingWallsDirectly();
            }
            microLookTimer = Random.Range(1.5f, 3.0f);
        }
        else if (currentState == NpcState.Walking)
        {
            walkGraceTimer = 0.5f;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }
        else if (currentState == NpcState.Conversing || currentState == NpcState.Dancing || currentState == NpcState.InteractingTask)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                agent.ResetPath();
            }

            if (animationLogic != null)
            {
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            }
        }
    }

    private void OnReachedDestination()
    {
        if (currentNode != null)
        {
            // Variedades específicas de comportamento baseadas nos objetos do cenário:
            if (currentNode.nodeType == PedestrianNodeType.ParkGarden)
            {
                // Em praças/parques/jardins, chance maior de dançar espontaneamente (25%)
                if (Random.value < 0.25f)
                {
                    PerformDance(Random.value < 0.5f ? 1 : 2, 4.0f);
                    return;
                }
            }
            else if (currentNode.nodeType == PedestrianNodeType.TaskSpot)
            {
                // Perto de locais de tarefas, chance alta de se aproximar e operar a task (65%)
                if (!isNavigatingToPretendTask && Random.value < 0.65f)
                {
                    TaskObject nearbyTask = GetClosestTask(transform.position, 8.0f);
                    if (nearbyTask != null)
                    {
                        TryStartPretendTask(nearbyTask);
                    }
                    else
                    {
                        TryStartPretendTask();
                    }

                    if (isNavigatingToPretendTask)
                    {
                        return;
                    }
                }
            }

            // Pausa aleatória natural baseada no tipo e configuração do nó
            float pauseChance = currentNode.isPausePoint ? currentNode.pauseChance : 0.20f;
            if (Random.value < pauseChance)
            {
                // Chance de dançar aleatoriamente ao invés de apenas ficar em Idle
                if (Random.value < 0.10f)
                {
                    PerformDance(Random.value < 0.5f ? 1 : 2, 4.0f);
                    return;
                }

                float duration = Random.Range(currentNode.minPauseDuration, currentNode.maxPauseDuration);
                SetState(NpcState.Idle, duration);

                if (currentNode.preferredLookDirection.sqrMagnitude > 0.01f)
                {
                    idleTargetRotation = Quaternion.LookRotation(currentNode.preferredLookDirection.normalized);
                }
                return;
            }

            // Caso contrário, continua caminhando fluidamente para o próximo nó
            NavigateToNextTarget();
        }
        else
        {
            if (Random.value < 0.12f)
            {
                PerformDance(Random.value < 0.5f ? 1 : 2, 4.0f);
                return;
            }
            SetState(NpcState.Idle, Random.Range(minIdleTime, maxIdleTime));
        }
    }

    private void NavigateToNextTarget()
    {
        // 1. Tenta caminhar pela rede de calçadas
        if (CityPedestrianNetwork.Instance != null && CityPedestrianNetwork.Instance.HasNodes)
        {
            if (currentNode == null)
            {
                currentNode = CityPedestrianNetwork.Instance.GetClosestNode(transform.position);
            }

            PedestrianNode next = currentNode != null ? currentNode.GetRandomNeighbor(previousNode) : null;
            if (next == null) next = CityPedestrianNetwork.Instance.GetRandomNode();

            if (next != null)
            {
                previousNode = currentNode;
                currentNode = next;

                if (agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                    agent.SetDestination(next.Position);
                    float dist = Vector3.Distance(transform.position, next.Position);
                    SetState(NpcState.Walking, (dist / walkSpeed) + 6.0f);
                    return;
                }
            }
        }

        // 2. Fallback inteligente no NavMesh se não houver rede de nós configurada
        PickSmartNavMeshDestination();
    }

    private void PickSmartNavMeshDestination()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(10f, 30f);

            Vector3 offset = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            Vector3 candidatePos = transform.position + offset;

            // Restringe rigorosamente ao círculo delimitado
            Vector2 candidate2D = new Vector2(candidatePos.x, candidatePos.z);
            if (Vector2.Distance(candidate2D, cityCenter) > cityRadius)
            {
                candidate2D = cityCenter + (candidate2D - cityCenter).normalized * (cityRadius - 2.0f);
                candidatePos.x = candidate2D.x;
                candidatePos.z = candidate2D.y;
            }

            candidatePos.x = Mathf.Clamp(candidatePos.x, xBounds.x, xBounds.y);
            candidatePos.z = Mathf.Clamp(candidatePos.z, zBounds.x, zBounds.y);

            if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 8.0f, NavMesh.AllAreas))
            {
                if (Vector2.Distance(new Vector2(hit.position.x, hit.position.z), cityCenter) <= cityRadius)
                {
                    NavMeshPath path = new NavMeshPath();
                    if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        agent.SetDestination(hit.position);
                        SetState(NpcState.Walking, (distance / walkSpeed) + 6.0f);
                        return;
                    }
                }
            }
        }

        SetState(NpcState.Idle, 2.0f);
    }

    /// <summary>
    /// Evita que o NPC fique parado encarando uma parede ou poste a centímetros de distância.
    /// </summary>
    private void AvoidFacingWallsDirectly()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 1.0f;
        if (Physics.Raycast(rayOrigin, transform.forward, out RaycastHit hit, 2.2f))
        {
            // Se há um obstáculo próximo na frente, vira para trás ou para o lado livre
            Vector3 avoidDir = Vector3.Reflect(transform.forward, hit.normal);
            avoidDir.y = 0f;
            if (avoidDir.sqrMagnitude > 0.01f)
            {
                idleTargetRotation = Quaternion.LookRotation(avoidDir.normalized);
            }
            else
            {
                idleTargetRotation = Quaternion.LookRotation(-transform.forward);
            }
        }
    }

    /// <summary>
    /// Micro-olhares: durante o Idle, faz pequenos giros suaves de cabeça/corpo como uma pessoa real.
    /// </summary>
    private void ProcessMicroLooks()
    {
        microLookTimer -= Time.deltaTime;
        if (microLookTimer <= 0f)
        {
            microLookTimer = Random.Range(2.0f, 4.0f);

            // Sorteia um desvio leve de ângulo (-30 a +30 graus)
            float lookAngle = Random.Range(-30f, 30f);
            idleTargetRotation = Quaternion.Euler(0f, transform.eulerAngles.y + lookAngle, 0f);
        }
    }

    /// <summary>
    /// Interação social com outros pedestres que passam perto.
    /// NPCs só cumprimentam jogadores se o jogador tiver acionado o cumprimento primeiro.
    /// </summary>
    private void CheckNearbyPedestrians()
    {
        if (socialCooldownTimer > 0f) return;
        if (currentState == NpcState.Conversing || currentState == NpcState.Dancing) return;

        // Checa outros NPCs que estejam passando para conversas casuais de ambiente (usa snapshot para segurança de coleção)
        var npcsSnapshot = activeNpcs.ToArray();
        foreach (var other in npcsSnapshot)
        {
            if (other == null || other == this || other.isDead) continue;
            if (other.socialCooldownTimer > 0f) continue;
            if (other.currentState == NpcState.Conversing || other.currentState == NpcState.Dancing) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist <= socialDistance && Random.value < socialGreetingChance)
            {
                // Inicia conversa casual com outro NPC
                StartConversationWith(other);
                break;
            }
        }
    }

    /// <summary>
    /// Encontra o NPC mais adequado para interagir e cumprimentar o jogador.
    /// Funciona perfeitamente tanto no Servidor quanto no Cliente (para orientação e rotação do jogador).
    /// </summary>
    public static NPCController FindBestGreetingTarget(Vector3 playerPosition, Vector3 playerForward, float maxDistance = SocialInteractionDistance)
    {
        NPCController bestNpc = null;
        float bestScore = float.MinValue;

        var npcs = activeNpcs;
        if (npcs == null || npcs.Count == 0) return null;

        int count = npcs.Count;
        for (int i = 0; i < count; i++)
        {
            var npc = npcs[i];
            if (npc == null || npc.isDead || !npc.gameObject.activeInHierarchy) continue;

            // Se for no servidor com NavMeshAgent ativo, valida se está na malha
            if (npc.agent != null && npc.agent.enabled && !npc.agent.isOnNavMesh) continue;

            Vector3 toNpc = npc.transform.position - playerPosition;
            if (Mathf.Abs(toNpc.y) > 2.0f) continue;
            toNpc.y = 0f;
            float dist = toNpc.magnitude;

            if (dist > maxDistance || dist < 0.1f) continue;

            Vector3 dirToNpc = toNpc / dist;
            float playerDot = Vector3.Dot(playerForward.normalized, dirToNpc);

            // Permite cumprimentar quem está na frente ou nas laterais; só ignora se estiver bem nas costas a não ser que esteja colado
            if (playerDot < -0.25f && dist > 1.8f) continue;

            Vector3 dirToPlayer = -dirToNpc;
            float npcDot = Vector3.Dot(npc.transform.forward, dirToPlayer);

            // Pontuação: proximidade + se está no campo de visão do jogador + se o NPC está de frente/perfil para o jogador
            float score = (maxDistance - dist) * 2.0f + (playerDot * 2.5f) + (npcDot * 1.5f);

            // Prefere NPCs que não estejam no meio de outra conversa
            if (npc.currentState != NpcState.Conversing)
            {
                score += 4.0f;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestNpc = npc;
            }
        }

        return bestNpc;
    }

    /// <summary>
    /// Chamado pelo servidor quando um jogador aciona a animação de cumprimento.
    /// O NPC mais próximo e alinhado para, vira em direção ao jogador e responde ao aceno.
    /// </summary>
    public static void RespondToPlayerGreeting(Vector3 playerPosition, Vector3 playerForward, ulong specificNpcId = ulong.MaxValue)
    {
        NPCController targetNpc = null;
        if (specificNpcId != ulong.MaxValue)
        {
            var npcs = activeNpcs;
            int count = npcs.Count;
            for (int i = 0; i < count; i++)
            {
                var n = npcs[i];
                if (n != null && n.NetworkObjectId == specificNpcId && !n.isDead && n.gameObject.activeInHierarchy)
                {
                    targetNpc = n;
                    break;
                }
            }
        }

        if (targetNpc == null)
        {
            targetNpc = FindBestGreetingTarget(playerPosition, playerForward);
        }

        if (targetNpc != null)
        {
            targetNpc.PerformGreetingResponse(playerPosition);
        }
    }

    private Coroutine greetingResponseCoroutine;

    /// <summary>
    /// Faz o NPC parar, virar em direção ao jogador e responder com aceno após um leve atraso humano.
    /// </summary>
    public void PerformGreetingResponse(Vector3 playerPosition)
    {
        if (greetingResponseCoroutine != null)
        {
            StopCoroutine(greetingResponseCoroutine);
        }
        greetingResponseCoroutine = StartCoroutine(DelayedGreetingRoutine(playerPosition));
    }

    private IEnumerator DelayedGreetingRoutine(Vector3 playerPosition)
    {
        // 1. Vira o NPC em direção ao jogador
        Vector3 dirToPlayer = (playerPosition - transform.position).normalized;
        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude > 0.01f)
        {
            idleTargetRotation = Quaternion.LookRotation(dirToPlayer);
        }

        // 2. Entra em estado de cumprimento/pausa por 3.5 a 4.5 segundos
        float greetDuration = Random.Range(3.5f, 4.5f);
        SetState(NpcState.Conversing, greetDuration);

        // 3. Pequeno atraso humano (0.15s - 0.28s) para não parecer um espelho robótico instantâneo
        yield return new WaitForSeconds(Random.Range(0.15f, 0.28f));

        // Reajusta a mira caso o player tenha dado um passo
        dirToPlayer = (playerPosition - transform.position).normalized;
        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude > 0.01f)
        {
            idleTargetRotation = Quaternion.LookRotation(dirToPlayer);
        }

        // 4. Dispara a animação de cumprimento localmente e replica para os clientes
        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            animationLogic.TriggerGreetingAnimation();
        }
        if (IsSpawned)
        {
            PlayGreetingClientRpc(playerPosition);
        }

        // 5. Aplica cooldown social
        socialCooldownTimer = Random.Range(15f, 25f);
        greetingResponseCoroutine = null;
    }

    public void StartConversationWith(NPCController interlocutor)
    {
        if (interlocutor == null || isDead || interlocutor.isDead) return;

        // Vira um para o outro
        Vector3 dirToOther = (interlocutor.transform.position - transform.position).normalized;
        dirToOther.y = 0f;
        if (dirToOther.sqrMagnitude > 0.01f)
        {
            idleTargetRotation = Quaternion.LookRotation(dirToOther);
            interlocutor.idleTargetRotation = Quaternion.LookRotation(-dirToOther);
        }

        float conversationDuration = Random.Range(4.0f, 6.0f);
        socialCooldownTimer = Random.Range(15f, 25f);
        interlocutor.socialCooldownTimer = Random.Range(15f, 25f);

        SetState(NpcState.Conversing, conversationDuration);
        interlocutor.SetState(NpcState.Conversing, conversationDuration);

        // Dispara animação de cumprimento no servidor
        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            animationLogic.TriggerGreetingAnimation();
        }
        if (interlocutor.animationLogic != null)
        {
            interlocutor.animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            interlocutor.animationLogic.TriggerGreetingAnimation();
        }

        // Replica para todos os clientes conectados
        if (IsSpawned)
        {
            PlayGreetingClientRpc(interlocutor.transform.position);
        }
        if (interlocutor.IsSpawned)
        {
            interlocutor.PlayGreetingClientRpc(transform.position);
        }
    }

    /// <summary>
    /// Encontra um ponto válido no NavMesh adjacente ao objeto da Task dentro de sua InteractionDistance.
    /// Testa leques de direções radiais e garante que haja um caminho completo (PathComplete) até ele.
    /// </summary>
    private bool TryFindTaskInteractionSpot(TaskObject task, Vector3 fromPos, out Vector3 spot)
    {
        spot = Vector3.zero;
        if (task == null) return false;

        Vector3 taskPos = task.transform.position;
        float interactDist = task.InteractionDistance;
        float sampleRadius = Mathf.Clamp(interactDist * 0.70f, 1.2f, 2.2f);

        Vector3 approachDir = fromPos - taskPos;
        approachDir.y = 0f;
        if (approachDir.sqrMagnitude < 0.01f) approachDir = task.transform.forward;
        approachDir.Normalize();

        float[] angles = new float[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 160f, -160f, 180f };
        UnityEngine.AI.NavMeshPath testPath = new UnityEngine.AI.NavMeshPath();

        for (int i = 0; i < angles.Length; i++)
        {
            Quaternion rot = Quaternion.Euler(0f, angles[i], 0f);
            Vector3 candidatePos = taskPos + (rot * approachDir) * sampleRadius;

            if (UnityEngine.AI.NavMesh.SamplePosition(candidatePos, out UnityEngine.AI.NavMeshHit hit, 1.4f, UnityEngine.AI.NavMesh.AllAreas))
            {
                float d = Vector3.Distance(hit.position, taskPos);
                if (d <= interactDist * 0.95f)
                {
                    if (agent.CalculatePath(hit.position, testPath) && testPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    {
                        spot = hit.position;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Retorna o TaskObject mais próximo da posição informada dentro do raio limite.
    /// </summary>
    private TaskObject GetClosestTask(Vector3 pos, float maxRadius)
    {
        var allTasks = TaskObject.AllTasks;
        if (allTasks == null) return null;

        TaskObject closest = null;
        float minDist = maxRadius;
        for (int i = 0; i < allTasks.Count; i++)
        {
            var t = allTasks[i];
            if (t == null || !t.gameObject.activeInHierarchy || !t.gameObject.CompareTag(TaskObject.TaskTag)) continue;
            float d = Vector3.Distance(pos, t.transform.position);
            if (d < minDist)
            {
                minDist = d;
                closest = t;
            }
        }
        return closest;
    }

    /// <summary>
    /// Seleciona uma task próxima para o NPC se aproximar e fingir interagir, comportando-se idêntico ao player.
    /// NPCs realizam tasks SOMENTE em objetos marcados com a tag "Task" e que possuam TaskObject.
    /// NPCs NÃO têm uma task específica fixa atribuída e suas ações NÃO contabilizam pontuação para vitória.
    /// </summary>
    private void TryStartPretendTask(TaskObject targetTask = null)
    {
        if (isDead || agent == null || !agent.isOnNavMesh) return;

        var allTasks = TaskObject.AllTasks;
        if (allTasks == null || allTasks.Count == 0) return;

        Vector3 myPos = transform.position;
        TaskObject bestTask = null;
        Vector3 bestSpot = Vector3.zero;

        // Se uma task específica foi informada (ex: nó TaskSpot de pedestre), tenta primeiro utilizá-la
        if (targetTask != null && targetTask.gameObject.activeInHierarchy && targetTask.gameObject.CompareTag(TaskObject.TaskTag))
        {
            if (TryFindTaskInteractionSpot(targetTask, myPos, out Vector3 spot))
            {
                bestTask = targetTask;
                bestSpot = spot;
            }
        }

        // Se não foi informada ou não estava alcançável, busca aleatoriamente entre as tasks ativas próximas
        if (bestTask == null)
        {
            float bestDist = 32f;
            int startIdx = Random.Range(0, allTasks.Count);
            for (int i = 0; i < allTasks.Count; i++)
            {
                int idx = (startIdx + i) % allTasks.Count;
                var task = allTasks[idx];
                if (task == null || !task.gameObject.activeInHierarchy) continue;

                // NPCs realizam tasks SOMENTE em objetos marcados com a tag "Task"
                if (!task.gameObject.CompareTag(TaskObject.TaskTag)) continue;

                float dist = Vector3.Distance(myPos, task.transform.position);
                if (dist < bestDist)
                {
                    if (TryFindTaskInteractionSpot(task, myPos, out Vector3 spot))
                    {
                        bestTask = task;
                        bestSpot = spot;
                        bestDist = dist;
                        if (dist < 12f) break; // Se encontrou uma task imediata, já pode usá-la
                    }
                }
            }
        }

        if (bestTask != null)
        {
            currentPretendTask = bestTask;
            pretendTaskInteractionSpot = bestSpot;
            isNavigatingToPretendTask = true;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(bestSpot);
            }
            float dist = Vector3.Distance(transform.position, bestSpot);
            SetState(NpcState.Walking, (dist / walkSpeed) + 8.0f);
        }
    }

    /// <summary>
    /// Inicia o deslocamento inteligente do NPC em direção à Morte (Hunter) para dançar dentro do raio circular.
    /// Isso cria uma camuflagem social perfeita para sobreviventes que precisam executar a tarefa de dançar perto da Morte.
    /// </summary>
    private void TryStartHunterDance(PlayerController hunter)
    {
        if (hunter == null || agent == null || !agent.isOnNavMesh) return;

        Vector3 hunterPos = hunter.transform.position;
        Vector3 dirFromHunter = (transform.position - hunterPos);
        dirFromHunter.y = 0f;
        if (dirFromHunter.sqrMagnitude < 0.01f)
        {
            dirFromHunter = transform.forward;
        }
        else
        {
            dirFromHunter.Normalize();
        }

        // Escolhe uma posição dentro do raio da área circular (entre 1.8m e 2.6m da Morte)
        float targetDist = Random.Range(1.8f, 2.6f);
        Vector3 desiredSpot = hunterPos + dirFromHunter * targetDist;

        if (NavMesh.SamplePosition(desiredSpot, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
        {
            hunterDanceSpot = hit.position;
            targetHunterToDance = hunter;
            isNavigatingToHunterDance = true;
            hunterDanceCooldownTimer = Random.Range(hunterDanceCooldown, hunterDanceCooldown + 15f);

            // Cancela pretend task se estava a caminho
            isNavigatingToPretendTask = false;
            currentPretendTask = null;

            agent.isStopped = false;
            agent.SetDestination(hunterDanceSpot);
            float dist = Vector3.Distance(transform.position, hunterDanceSpot);
            SetState(NpcState.Walking, (dist / walkSpeed) + 6.0f);
        }
    }

    /// <summary>
    /// Faz o NPC dançar espontaneamente por 4.0 segundos com movimentação bloqueada.
    /// </summary>
    public void PerformDance(int danceType, float duration = 4.0f)
    {
        if (isDead) return;
        SetState(NpcState.Dancing, duration);
        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            if (danceType == 1)
            {
                animationLogic.TriggerDance1Animation();
            }
            else
            {
                animationLogic.TriggerDance2Animation();
            }
        }
        if (IsSpawned)
        {
            PlayDanceClientRpc(danceType);
        }
    }

    [ClientRpc]
    private void PlayGreetingClientRpc(Vector3 lookAtPosition)
    {
        if (IsServer) return; // Servidor já executou localmente

        Vector3 dir = lookAtPosition - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            animationLogic.TriggerGreetingAnimation();
        }
    }

    [ClientRpc]
    private void PlayDanceClientRpc(int danceType)
    {
        if (IsServer) return; // Servidor já executou localmente

        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            if (danceType == 1)
            {
                animationLogic.TriggerDance1Animation();
            }
            else
            {
                animationLogic.TriggerDance2Animation();
            }
        }
    }

    [ClientRpc]
    private void StopDanceClientRpc()
    {
        if (IsServer) return; // Servidor já executou localmente

        if (animationLogic != null)
        {
            animationLogic.StopDanceAnimation();
        }
    }

    [ClientRpc]
    private void PlayTaskInteractClientRpc(bool isInteracting)
    {
        if (IsServer) return; // Servidor já executou localmente

        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            animationLogic.SetTaskInteractionAnimation(isInteracting);
        }

        Vector3 soundPos = currentPretendTask != null ? currentPretendTask.transform.position : transform.position;
        if (isInteracting)
        {
            GameAudioManager.Instance?.PlayTaskStart(soundPos);
        }
        else
        {
            GameAudioManager.Instance?.PlayTaskComplete(transform.position);
            if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.IsHunter)
            {
                GameAudioManager.Instance?.PlayHunterTaskAlert(transform.position);
            }
        }
    }

    /// <summary>
    /// Sincroniza dinamicamente a velocidade de caminhada com os sobreviventes normais (a Morte anda mais rápido).
    /// </summary>
    private void SyncWalkSpeedWithPlayer()
    {
        if (PlayerController.ActivePlayers != null && PlayerController.ActivePlayers.Count > 0)
        {
            PlayerController survivor = null;
            for (int i = 0; i < PlayerController.ActivePlayers.Count; i++)
            {
                var p = PlayerController.ActivePlayers[i];
                if (p != null && !p.IsHunter && !p.IsDead && !p.IsGhost)
                {
                    survivor = p;
                    break;
                }
            }

            if (survivor != null)
            {
                var playerMove = survivor.GetComponent<PlayerMovementLogic>();
                if (playerMove != null && playerMove.walkSpeed > 0.1f)
                {
                    walkSpeed = playerMove.walkSpeed;
                    if (agent != null && Mathf.Abs(agent.speed - walkSpeed) > 0.01f)
                    {
                        agent.speed = walkSpeed;
                    }
                }
            }
        }
    }

    private void UpdateFootsteps()
    {
        // Culling espacial de passos: se o NPC estiver fora do raio audível do jogador local, ignora o processamento
        var localPlayer = PlayerController.LocalInstance;
        float maxDist = (footstepLogic != null) ? footstepLogic.MaxAudibleDistance : 9.0f;
        float maxDistSqr = maxDist * maxDist;

        if (localPlayer != null)
        {
            float distSqr = (transform.position - localPlayer.transform.position).sqrMagnitude;
            if (distSqr > maxDistSqr)
            {
                lastClientPosition = transform.position;
                return;
            }
        }
        else if (Camera.main != null)
        {
            float distSqr = (transform.position - Camera.main.transform.position).sqrMagnitude;
            if (distSqr > maxDistSqr)
            {
                lastClientPosition = transform.position;
                return;
            }
        }

        float currentSpeed = (transform.position - lastClientPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        lastClientPosition = transform.position;

        bool isMoving = currentSpeed > 0.2f;
        footstepLogic.HandleFootsteps(isMoving, isRunning: false, isGrounded: true);
    }

    /// <summary>
    /// Chamado no Servidor quando o NPC é atingido pelo golpe de foice ou bola de fogo da Morte.
    /// Aplica penalidade ao Hunter que matou o NPC errado (perde 1 vida).
    /// Executa a animação Falling Back Death, aguarda a finalização e spawna um novo NPC substituto.
    /// </summary>
    public void KillByHunter(PlayerController killerHunter = null)
    {
        if (!IsServer || isDead) return;
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;
        isDead = true;
        currentPretendTask = null;
        isNavigatingToPretendTask = false;
        if (isDancingForHunter)
        {
            isDancingForHunter = false;
            activeNpcsDancingForHunter = Mathf.Max(0, activeNpcsDancingForHunter - 1);
        }
        isNavigatingToHunterDance = false;
        targetHunterToDance = null;
        if (animationLogic != null)
        {
            animationLogic.SetTaskInteractionAnimation(false);
        }

        activeNpcs.Remove(this);

        // Se o autor foi um Hunter, penaliza o Hunter retirando 1 das suas 3 vidas
        if (killerHunter == null)
        {
            var allHunters = FindObjectsByType<HunterKnockdownLogic>(FindObjectsInactive.Exclude);
            if (allHunters != null && allHunters.Length > 0)
            {
                HunterKnockdownLogic closestHunter = null;
                float closestDist = float.MaxValue;
                foreach (var h in allHunters)
                {
                    if (h == null) continue;
                    float d = Vector3.Distance(transform.position, h.transform.position);
                    if (d < closestDist)
                    {
                        closestDist = d;
                        closestHunter = h;
                    }
                }
                if (closestHunter != null)
                {
                    killerHunter = closestHunter.GetComponent<PlayerController>();
                }
            }
        }

        if (killerHunter != null)
        {
            var hunterLives = killerHunter.GetComponent<HunterKnockdownLogic>();
            if (hunterLives != null)
            {
                hunterLives.PenalizeWrongNpcKill();
            }
        }

        if (greetingResponseCoroutine != null)
        {
            StopCoroutine(greetingResponseCoroutine);
            greetingResponseCoroutine = null;
        }

        if (agent != null)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            agent.enabled = false;
        }

        // Passagem desimpedida: desativa colisor imediatamente para a Morte correr reto sem esbarrar
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        SnapToGround();

        // Replica o disparo da animação de morte para todos os clientes
        KillNpcClientRpc();

        StartCoroutine(NpcDeathRoutine());
    }

    [ClientRpc]
    private void KillNpcClientRpc()
    {
        isDead = true;
        GameAudioManager.Instance?.PlayPlayerDeath(transform.position);

        if (agent != null) agent.enabled = false;

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        SnapToGround();

        // Dispara a animação Falling Back Death via PlayerAnimationLogic
        if (animationLogic != null)
        {
            animationLogic.SetTaskInteractionAnimation(false);
            animationLogic.TriggerDeathAnimation();
        }
    }

    /// <summary>
    /// Dispara raycast para baixo ignorando outros personagens para fixar o corpo morto firme no piso da cidade.
    /// </summary>
    private void SnapToGround()
    {
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (hit.collider.GetComponentInParent<NPCController>() != null && hit.collider.GetComponentInParent<NPCController>() != this) continue;

            transform.position = hit.point;
            break;
        }
    }

    private IEnumerator NpcDeathRoutine()
    {
        // Aguarda a duração da animação Falling Back Death (2.4s)
        yield return new WaitForSeconds(2.4f);

        // Spawna um novo NPC substituto na cidade para repor a população urbana
        if (GameSpawner.Instance != null)
        {
            GameSpawner.Instance.SpawnReplacementNPC();
        }

        // Despawna na rede e destrói o NPC morto
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
