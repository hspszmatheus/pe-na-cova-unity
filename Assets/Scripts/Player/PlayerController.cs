using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerMovementLogic))]
[RequireComponent(typeof(PlayerCameraLogic))]
[RequireComponent(typeof(PlayerAnimationLogic))]
[RequireComponent(typeof(PlayerFootstepLogic))]
public class PlayerController : NetworkBehaviour
{
    public static readonly List<PlayerController> ActivePlayers = new List<PlayerController>();
    public static PlayerController LocalInstance { get; private set; }
    public static PlayerController LocalPlayer => LocalInstance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ActivePlayers.Clear();
        LocalInstance = null;
    }

    public static PlayerController GetPlayerByClientId(ulong clientId)
    {
        if (ActivePlayers == null) return null;
        for (int i = 0; i < ActivePlayers.Count; i++)
        {
            if (ActivePlayers[i] != null && ActivePlayers[i].OwnerClientId == clientId)
            {
                return ActivePlayers[i];
            }
        }
        return null;
    }

    [Header("Identificação")]
    [Tooltip("Define se este personagem é o Hunter ou um Player comum")]
    [SerializeField] private bool isHunter = false;
    public bool IsHunter => isHunter;

    [Header("Identificação de Jogador e Nickname")]
    private readonly NetworkVariable<FixedString64Bytes> networkNickname = new NetworkVariable<FixedString64Bytes>(
        string.Empty,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public string Nickname => networkNickname.Value.ToString();
    public event System.Action<string> OnNicknameChanged;

    [Header("Morte e Modo Fantasma (Espectador)")]
    [Tooltip("Objeto visual do fantasma LittleGhost anexado ao jogador")]
    [SerializeField] private GameObject ghostVisual;
    [Tooltip("Prefab do modelo de fantasma LittleGhost para instanciação (fallback)")]
    [SerializeField] private GameObject ghostPrefab;
    [Tooltip("Prefabs das skins de fantasmas LittleGhost (1 a 7) para sorteio aleatório")]
    [SerializeField] private GameObject[] ghostPrefabs;
    [Tooltip("Material transparente espectral do fantasma")]
    [SerializeField] private Material ghostTransparentMat;
    [Tooltip("Prefab do orbe de alma (SoulOrb) roxo para identificação de sobreviventes")]
    [SerializeField] private GameObject soulOrbPrefab;

    private readonly NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> isGhost = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> ghostSkinIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsDead => isDead.Value;
    public bool IsGhost => isGhost.Value;
    public bool IsDying => isDying;
    private bool isDying = false;

    [Header("Combate (Chute do Player)")]
    [Tooltip("Tempo de recarga entre chutes em segundos")]
    [SerializeField] private float kickCooldown = 15.0f;

    [Tooltip("Alcance máximo do chute para acertar o Hunter")]
    [SerializeField] private float kickRange = 2.8f;

    [Header("Combate da Morte (Ataque com Foice)")]
    [Tooltip("Tempo de recarga entre ataques da Morte em segundos")]
    [SerializeField] private float hunterAttackCooldown = 1.8f;
    [Tooltip("Alcance do golpe cortante da Foice")]
    [SerializeField] private float hunterAttackRange = 2.8f;
    [Tooltip("Ângulo frontal de corte da Foice")]
    [SerializeField] private float hunterAttackAngle = 80f;

    [Header("Combate da Morte (Lançamento de Bola de Fogo)")]
    [Tooltip("Tempo de recarga entre lançamentos de bola de fogo em segundos")]
    [SerializeField] private float fireballCooldown = 6.0f;
    [Tooltip("Prefab da bola de fogo (HunterFireball)")]
    [SerializeField] private GameObject fireballPrefab;
    [Tooltip("Prefab do impacto explosivo da bola de fogo no Player (efeito roxo)")]
    [SerializeField] private GameObject fireballExplosionPrefab;
    [Tooltip("Prefab do impacto de fogo ao atingir NPC ou obstáculo (variante de fogo)")]
    [SerializeField] private GameObject fireHitEffectPrefab;
    [Tooltip("Prefab do impacto roxo da foice ao atingir player")]
    [SerializeField] private GameObject scytheHitEffectPrefab;
    [Tooltip("Prefab do efeito visual de carga na mão direita")]
    [SerializeField] private GameObject handChargeVfxPrefab;
    [Tooltip("Prefab do efeito sutil de spawn dos sobreviventes")]
    [SerializeField] private GameObject playerSpawnVfxPrefab;

    private HunterSpawnIntroSequence hunterSpawnIntro = null;
    public HunterSpawnIntroSequence HunterSpawnIntro => hunterSpawnIntro;

    [Header("Habilidade da Morte (Revelação Espiritual - Tecla E)")]
    [Tooltip("Tempo em segundos necessário para segurar a tecla E para conjurar a revelação (reduzido para 1.5s para conjuração mais rápida)")]
    [SerializeField] private float hunterRevealCastDuration = 1.5f;
    [Tooltip("Raio de alcance da revelação ao redor da Morte (em metros, 16.5m - 3x o raio original de 5.5m)")]
    [SerializeField] private float hunterRevealRadius = 16.5f;
    [Tooltip("Duração em segundos da revelação dos sobreviventes pegos no raio")]
    [SerializeField] private float hunterRevealDuration = 5.0f;
    [Tooltip("Tempo de recarga (cooldown) da habilidade em segundos")]
    [SerializeField] private float hunterRevealCooldown = 10.0f;

    private float lastHunterRevealTime = -999f;
    private float hunterRevealHoldTimer = 0f;
    private bool isCastingReveal = false;
    public bool IsCastingReveal => isCastingReveal;
    public float HunterRevealCooldown => hunterRevealCooldown;
    public float LastHunterRevealTime => lastHunterRevealTime;

    private Coroutine temporaryRevealRoutine = null;
    private bool isTemporarilyRevealed = false;
    public bool IsTemporarilyRevealed => isTemporarilyRevealed;

    private float lastKickTime = -999f;
    private float lastHunterAttackTime = -10f;
    private float lastFireballTime = -10f;
    private bool isAttacking = false;
    public bool IsAttacking => isAttacking;
    private bool isCastingFireball = false;
    public bool IsCastingFireball => isCastingFireball;
    private bool isAiming = false;
    public bool IsAiming => isAiming;
    private Coroutine attackRoutine = null;
    private Coroutine fireballRoutine = null;
    private Transform hunterRightHandBone = null;

    private Vector2 moveAmount;
    private bool isPressingRun = false;

    private PlayerInput playerInput;
    private CharacterController characterController;
    private PlayerMovementLogic movementLogic;
    private PlayerCameraLogic cameraLogic;
    private PlayerAnimationLogic animationLogic;
    private PlayerFootstepLogic footstepLogic;
    private HunterKnockdownLogic hunterKnockdownLogic;
    private HunterWeaponVisuals hunterWeaponVisuals;
    private PlayerCameraSetup cameraSetup;
    private PlayerTaskIndicatorController taskIndicatorController;
    private HunterProximityFearEffect proximityFearEffect;

    public HunterKnockdownLogic KnockdownLogic => hunterKnockdownLogic;
    public HunterWeaponVisuals WeaponVisuals => hunterWeaponVisuals;

    void Awake()
    {
        ResetVisualsTransform();
        playerInput = GetComponent<PlayerInput>();
        characterController = GetComponent<CharacterController>();
        movementLogic = GetComponent<PlayerMovementLogic>();
        cameraLogic = GetComponent<PlayerCameraLogic>();
        animationLogic = GetComponent<PlayerAnimationLogic>();
        footstepLogic = GetComponent<PlayerFootstepLogic>();
        hunterKnockdownLogic = GetComponent<HunterKnockdownLogic>();
        hunterWeaponVisuals = GetComponent<HunterWeaponVisuals>();
        cameraSetup = GetComponent<PlayerCameraSetup>();
        taskIndicatorController = GetComponent<PlayerTaskIndicatorController>();
        var existingObstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (existingObstacle != null)
        {
            if (Application.isPlaying) Destroy(existingObstacle); else DestroyImmediate(existingObstacle);
        }

        // Remove preventivamente qualquer Rigidbody ou CapsuleCollider residual para evitar conflito com o CharacterController
        var allRbs = GetComponents<Rigidbody>();
        foreach (var r in allRbs)
        {
            if (Application.isPlaying) Destroy(r); else DestroyImmediate(r);
        }
        var allCapsules = GetComponents<CapsuleCollider>();
        foreach (var c in allCapsules)
        {
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        if (isHunter && hunterKnockdownLogic == null)
        {
            hunterKnockdownLogic = gameObject.AddComponent<HunterKnockdownLogic>();
        }
        if (isHunter && hunterWeaponVisuals == null)
        {
            hunterWeaponVisuals = gameObject.AddComponent<HunterWeaponVisuals>();
        }
        if (isHunter && hunterSpawnIntro == null)
        {
            hunterSpawnIntro = GetComponent<HunterSpawnIntroSequence>();
            if (hunterSpawnIntro == null)
            {
                hunterSpawnIntro = gameObject.AddComponent<HunterSpawnIntroSequence>();
            }
        }
        if (!isHunter)
        {
            HunterPreMatchVisibility.EnsureAttached(gameObject);
        }

        // Padroniza as dimensões do colisor físico para 1.8m de altura, idêntico aos NPCs
        if (characterController != null)
        {
            characterController.height = 1.8f;
            characterController.center = new Vector3(0f, 0.9f, 0f);
            characterController.radius = 0.45f;
            characterController.stepOffset = 0.3f;
        }

        movementLogic.Initialize();
        if (isHunter)
        {
            movementLogic.OrientToCamera = true;
            // A Morte anda e corre um pouco mais rápido que os sobreviventes
            movementLogic.walkSpeed = 1.8f;
            movementLogic.runSpeed = 8.8f;
            // Aumenta a stamina da Morte (Hunter) em 3 vezes (300 de stamina máxima, regeneração proporcional de 75/s)
            movementLogic.SetStaminaStats(300f, 22f, 75f, 0.8f);
        }
        else
        {
            movementLogic.walkSpeed = 1.5f;
            movementLogic.runSpeed = 7.5f;
            // Garante o tempo de recarga padrão de 15 segundos para o chute do sobrevivente
            if (kickCooldown < 15.0f)
            {
                kickCooldown = 15.0f;
            }
        }
        cameraLogic.Initialize();
        animationLogic.Initialize();
        footstepLogic.Initialize();
        if (!isHunter)
        {
            proximityFearEffect = GetComponent<HunterProximityFearEffect>();
            if (proximityFearEffect == null)
            {
                proximityFearEffect = gameObject.AddComponent<HunterProximityFearEffect>();
            }
            proximityFearEffect.Initialize();
        }
        if (hunterKnockdownLogic != null)
        {
            hunterKnockdownLogic.Initialize();
        }
    }

    public override void OnNetworkSpawn()
    {
        ActivePlayers.Add(this);
        EnsureGhostVisual();

        isGhost.OnValueChanged += OnGhostStateChanged;
        ghostSkinIndex.OnValueChanged += OnGhostSkinChanged;
        if (isGhost.Value)
        {
            UpdateGhostVisuals(true);
        }

        if (IsOwner)
        {
            LocalInstance = this;
            RefreshAllGhostVisibilities();
            HunterPreMatchVisibility.RefreshAll();
            playerInput.enabled = true;
            BindInputActions();

            string chosenNick = PlayerNameUIManager.GetActivePlayerNickname();
            if (string.IsNullOrEmpty(chosenNick) || chosenNick == "Player")
            {
                chosenNick = isHunter ? "A Morte" : $"Player {OwnerClientId + 1}";
            }
            SubmitNicknameServerRpc(chosenNick);

            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.SetLocalRole(isHunter);
            }
            if (!isHunter && TaskManager.Instance != null && GameSpawner.Instance != null && GameSpawner.Instance.IsMatchActive)
            {
                TaskManager.Instance.RequestAssignedTargetTaskRpc(OwnerClientId);
            }
            if (taskIndicatorController == null)
            {
                taskIndicatorController = GetComponent<PlayerTaskIndicatorController>();
                if (taskIndicatorController == null)
                {
                    taskIndicatorController = gameObject.AddComponent<PlayerTaskIndicatorController>();
                }
            }
            taskIndicatorController.InitializeForLocalPlayer();
            if (isHunter && movementLogic != null)
            {
                movementLogic.OrientToCamera = true;
            }
        }
        else
        {
            playerInput.enabled = false;
            // Para personagens remotos no cliente local, desativa detecção mútua do CharacterController
            // para permitir passagem livre / intangibilidade entre jogadores
            if (characterController != null)
            {
                characterController.detectCollisions = false;
            }
        }

        networkNickname.OnValueChanged += (oldVal, newVal) => {
            OnNicknameChanged?.Invoke(newVal.ToString());
        };

        if (!string.IsNullOrEmpty(networkNickname.Value.ToString()))
        {
            OnNicknameChanged?.Invoke(networkNickname.Value.ToString());
        }

        // Anexa o visual de destaque (orbe de alma roxo e nickname) se for sobrevivente
        if (!isHunter)
        {
            var highlight = GetComponent<SurvivorHighlightVisuals>();
            if (highlight == null)
            {
                highlight = gameObject.AddComponent<SurvivorHighlightVisuals>();
            }
            if (highlight != null && soulOrbPrefab != null)
            {
                highlight.SetSoulOrbPrefab(soulOrbPrefab);
            }

            // Executa a sequência e animação de surgimento dos sobreviventes
            PlayPlayerSpawnSequence();
        }
        else
        {
            // Anexa o visual de destaque para Hunters aliados (orbe de alma de fogo, contorno de fogo e nickname)
            var hunterHighlight = GetComponent<HunterHighlightVisuals>();
            if (hunterHighlight == null)
            {
                hunterHighlight = gameObject.AddComponent<HunterHighlightVisuals>();
            }
            if (hunterHighlight != null && soulOrbPrefab != null)
            {
                hunterHighlight.SetSoulOrbPrefab(soulOrbPrefab);
            }
        }

        // Garante que o PhysX ignore colisões rígidas entre TODOS os jogadores (Hunter e Players) e NPCs
        IgnoreAllCharacterCollisions();
    }

    [ServerRpc]
    private void SubmitNicknameServerRpc(FixedString64Bytes chosenNick)
    {
        networkNickname.Value = chosenNick;
    }

    void Start()
    {
        ResetVisualsTransform();
        EnsureGhostVisual();
        if (!IsSpawned)
        {
            BindInputActions();
        }
        if (!IsSpawned && GameHUDController.Instance != null)
        {
            GameHUDController.Instance.SetLocalRole(isHunter);
        }
        if (isHunter && movementLogic != null)
        {
            movementLogic.OrientToCamera = true;
        }

        if (!IsSpawned && !isHunter)
        {
            PlayPlayerSpawnSequence();
        }

        // Garante ignorar colisões mesmo em testes locais/offline
        IgnoreAllCharacterCollisions();

        var randomizer = GetComponent<PlayerCharacterRandomizer>();
        if (randomizer != null)
        {
            randomizer.OnCharacterVisualsChanged += (charObj) => {
                if (isMarkedForDeath) ApplyMarkedForDeathVisuals(true);
            };
        }
    }

    public override void OnNetworkDespawn()
    {
        if (isCastingReveal)
        {
            CancelHunterRevealCasting();
        }
        else if (isHunter)
        {
            GameAudioManager.Instance?.StopHunterRevealCharge();
        }

        if (IsOwner)
        {
            TaskObject.ClearAllLocalTargets();
            taskIndicatorController?.ClearTarget();
        }
        if (IsOwner && LocalInstance == this)
        {
            LocalInstance = null;
        }
        ActivePlayers.Remove(this);
        isGhost.OnValueChanged -= OnGhostStateChanged;
        ghostSkinIndex.OnValueChanged -= OnGhostSkinChanged;
    }

    private void OnDisable()
    {
        if (isCastingReveal)
        {
            CancelHunterRevealCasting();
        }
        else if (isHunter)
        {
            GameAudioManager.Instance?.StopHunterRevealCharge();
        }
    }

    public override void OnDestroy()
    {
        if (isCastingReveal)
        {
            CancelHunterRevealCasting();
        }
        else if (isHunter)
        {
            GameAudioManager.Instance?.StopHunterRevealCharge();
        }

        if (IsOwner && LocalInstance == this)
        {
            LocalInstance = null;
        }
        ActivePlayers.Remove(this);
        if (playerInput != null && playerInput.actions != null)
        {
            var kickAction = playerInput.actions.FindAction("Kick");
            if (kickAction != null) kickAction.performed -= OnKick;

            var attackAction = playerInput.actions.FindAction("Attack");
            if (attackAction != null) attackAction.performed -= OnAttack;

            var aimAction = playerInput.actions.FindAction("Aim");
            if (aimAction != null)
            {
                aimAction.started -= OnAim;
                aimAction.performed -= OnAim;
                aimAction.canceled -= OnAim;
            }

            var fireballAction = playerInput.actions.FindAction("Fireball");
            if (fireballAction != null) fireballAction.performed -= OnFireball;
        }
        base.OnDestroy();
    }

    /// <summary>
    /// Desabilita colisões físicas do PhysX entre todos os CharacterControllers dos jogadores (Player vs Hunter, Player vs Player)
    /// e também contra os colisores de todos os NPCs.
    /// Garante que todos (Player, Hunter e NPC) sejam 100% intangíveis entre si (atravessam uns aos outros livremente)
    /// enquanto o sistema de repulsão suave governa o afastamento mútuo.
    /// </summary>
    public static void IgnoreAllCharacterCollisions()
    {
        int pCount = ActivePlayers.Count;
        for (int i = 0; i < pCount; i++)
        {
            var p1 = ActivePlayers[i];
            if (p1 == null) continue;
            var col1 = p1.characterController != null ? p1.characterController : p1.GetComponent<CharacterController>();
            if (col1 == null) continue;

            // Ignora colisão física entre CharacterControllers de jogadores (incluindo Morte/Hunter)
            for (int j = i + 1; j < pCount; j++)
            {
                var p2 = ActivePlayers[j];
                if (p2 == null) continue;
                var col2 = p2.characterController != null ? p2.characterController : p2.GetComponent<CharacterController>();
                if (col2 != null)
                {
                    Physics.IgnoreCollision(col1, col2, true);
                }
            }

            // Ignora colisão física contra todos os NPCs ativos
            if (NPCController.ActiveNpcs != null)
            {
                int npcCount = NPCController.ActiveNpcs.Count;
                for (int k = 0; k < npcCount; k++)
                {
                    var npc = NPCController.ActiveNpcs[k];
                    if (npc == null) continue;
                    var npcCol = npc.GetComponent<Collider>();
                    if (npcCol != null)
                    {
                        Physics.IgnoreCollision(col1, npcCol, true);
                    }
                }
            }
        }
    }

    private void BindInputActions()
    {
        if (playerInput == null || playerInput.actions == null) return;

        var kickAction = playerInput.actions.FindAction("Kick");
        if (kickAction != null)
        {
            kickAction.performed -= OnKick;
            kickAction.performed += OnKick;
        }

        var attackAction = playerInput.actions.FindAction("Attack");
        if (attackAction != null)
        {
            attackAction.performed -= OnAttack;
            attackAction.performed += OnAttack;
        }

        var aimAction = playerInput.actions.FindAction("Aim");
        if (aimAction != null)
        {
            aimAction.started -= OnAim;
            aimAction.started += OnAim;
            aimAction.performed -= OnAim;
            aimAction.performed += OnAim;
            aimAction.canceled -= OnAim;
            aimAction.canceled += OnAim;
        }

        var fireballAction = playerInput.actions.FindAction("Fireball");
        if (fireballAction != null)
        {
            fireballAction.performed -= OnFireball;
            fireballAction.performed += OnFireball;
        }
    }

    void Update()
    {
        // Garante periodicamente que novos jogadores ou NPCs continuem com colisão ignorada no PhysX
        if (Time.frameCount % 30 == 0)
        {
            IgnoreAllCharacterCollisions();
        }

        // Atualiza dinamicamente a visibilidade do modelo fantasma para a perspectiva deste cliente
        if (isGhost.Value)
        {
            UpdateGhostVisibility();
        }

        if (!IsOwner) return;

        // Se estiver executando a animação de morte, mantém firme no solo e bloqueia qualquer movimentação
        if (isDying)
        {
            return;
        }

        // Enquanto o menu de pausa in-game (ESC) estiver aberto ou saindo da partida, trava entradas locais
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch)
        {
            HandlePausedUpdate();
            return;
        }

        // Modo Espectador (Fantasma): controle de voo tridimensional livre (WASD + Espaço subir + Ctrl descer)
        if (isGhost.Value)
        {
            HandleSpectatorUpdate();
            return;
        }

        // Se a Morte está em sua descida de introdução dos céus (5s), bloqueia qualquer entrada/controle
        if (isHunter && hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro)
        {
            return;
        }

        // Se o Hunter foi chutado e está no ar/chão como pino de boliche, a física do Rigidbody assume
        if (isHunter && hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown)
        {
            if (isAiming) SetAiming(false);
            if (movementLogic != null) movementLogic.OrientToCamera = false;
            if (isAttacking || isCastingFireball)
            {
                isAttacking = false;
                isCastingFireball = false;
                if (attackRoutine != null) { StopCoroutine(attackRoutine); attackRoutine = null; }
                if (fireballRoutine != null) { StopCoroutine(fireballRoutine); fireballRoutine = null; }
                if (hunterWeaponVisuals != null) hunterWeaponVisuals.StopAttackVisuals();
                if (animationLogic != null)
                {
                    animationLogic.ResetAttackTrigger();
                    animationLogic.ResetFireballTrigger();
                }
            }
            return;
        }

        if (isHunter && movementLogic != null)
        {
            movementLogic.OrientToCamera = true;

            // Sincroniza dinamicamente o estado de mira (segurando botão direito do mouse ou LT no controle)
            bool aimHeld = false;
            if (playerInput != null && playerInput.actions != null)
            {
                var aimAction = playerInput.actions.FindAction("Aim");
                if (aimAction != null)
                {
                    aimHeld = aimAction.IsPressed();
                }
            }
            if (!aimHeld && UnityEngine.InputSystem.Mouse.current != null)
            {
                aimHeld = UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
            }

            if (aimHeld != isAiming)
            {
                SetAiming(aimHeld);
            }
        }

        // Sincroniza dinamicamente o estado da tecla Shift a cada frame para manter a corrida ativa após atacar
        bool isRunHeld = isPressingRun;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
            {
                isRunHeld = true;
            }
            else
            {
                var runAction = playerInput != null && playerInput.actions != null ? playerInput.actions.FindAction("Run") : null;
                if (runAction == null || !runAction.enabled || !runAction.IsPressed())
                {
                    isRunHeld = false;
                }
            }
        }
        else if (playerInput != null && playerInput.actions != null)
        {
            var runAction = playerInput.actions.FindAction("Run");
            if (runAction != null && runAction.enabled)
            {
                isRunHeld = runAction.IsPressed();
            }
        }
        isPressingRun = isRunHeld;

        // Durante o ataque, conjuração ou mira da Morte (Hunter), ela não corre mesmo segurando Shift (apenas anda).
        // Também respeita a stamina: ao esgotar, não consegue correr até recuperar o fôlego
        bool canRunNow = isPressingRun && !isAttacking && !isCastingFireball && (!isHunter || !isAiming) && (movementLogic != null && movementLogic.CanRun);
        movementLogic.SetRunningState(canRunNow);

        Vector3 camForward = cameraLogic.GetForwardDirection();
        Vector3 camRight = cameraLogic.GetRightDirection();

        // Durante o ataque ou lançamento de bola de fogo do Hunter, ele avança lentamente (35% da velocidade de caminhada)
        // Durante o chute do Player normal, o movimento é pausado para focar na execução do golpe
        Vector2 effectiveMove;
        bool forceFaceCamera;
        if (isHunter)
        {
            if (isCastingReveal)
            {
                effectiveMove = Vector2.zero;
                forceFaceCamera = false;
            }
            else
            {
                effectiveMove = (isAttacking || isCastingFireball) ? moveAmount * 0.35f : moveAmount;
                // Se estiver se movendo (ex: apertando A, W+A, S), o Hunter vira o corpo para o ângulo do movimento
                // exibindo a animação correspondente à direção em que está andando.
                // Só força o corpo a olhar rigidamente para a câmera se estiver parado mirando, atacando ou conjurando magia.
                bool isMovingInput = moveAmount.sqrMagnitude > 0.01f;
                forceFaceCamera = isAttacking || isCastingFireball || (isAiming && !isMovingInput);
            }
        }
        else
        {
            effectiveMove = (isAttacking || isGreeting || isDancing) ? Vector2.zero : moveAmount;
            forceFaceCamera = false;
        }
        movementLogic.HandleMovement(effectiveMove, camForward, camRight, forceFaceCamera);

        // Se NÃO for fantasma, processa as animações humanoides e passos
        if (!isGhost.Value)
        {
            bool isGrounded = movementLogic.CheckIfGrounded();
            // Para a Morte: quando mirando ou conjurando revelação, não corre segurando shift e só anda/fica parado
            // Para o sobrevivente em chute, saudação ou dança: mantém isPressingRun direto para que o Animator saiba para onde transicionar no fim da ação
            bool animRunning = isHunter ? (canRunNow && !isCastingReveal) : ((isAttacking || isGreeting || isDancing) ? isPressingRun : canRunNow);
            animationLogic.HandleAnimation(effectiveMove, animRunning, isGrounded);

            bool isMoving = effectiveMove.sqrMagnitude > 0.01f;
            // Passos durante o ataque respeitam canRunNow (som de caminhada pesada, não de corrida)
            footstepLogic.HandleFootsteps(isMoving, canRunNow, isGrounded);
        }

        // Atualiza a interface gráfica (Stamina e Cooldowns) para o jogador local
        if (GameHUDController.Instance != null && movementLogic != null)
        {
            GameHUDController.Instance.SetStamina(movementLogic.CurrentStamina, movementLogic.MaxStamina);
            if (isHunter)
            {
                float timeSinceFireball = Time.time - lastFireballTime;
                GameHUDController.Instance.SetFireballCooldown(timeSinceFireball, fireballCooldown);
                UpdateHunterRevealAbility();
            }
            else
            {
                float timeSinceKick = Time.time - lastKickTime;
                GameHUDController.Instance.SetKickCooldown(timeSinceKick, kickCooldown);
                UpdateSurvivorRevelationHUD();
            }
        }

        // Interação com objetos de Task (apenas para Sobreviventes vivos)
        if (!isHunter && !isGhost.Value && !isDead.Value && !isDying)
        {
            if (currentAssignedTaskIndex == -1)
            {
                if (Time.time < nextTaskAvailableTime)
                {
                    float remaining = Mathf.Max(0.5f, nextTaskAvailableTime - Time.time);
                    GameHUDController.Instance?.SetTargetTaskObjective($"Aguardando próximo objetivo ({Mathf.CeilToInt(remaining)}s)...");
                }
                else if (GameSpawner.Instance != null && (GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.HasMatchStarted) && !GameSpawner.Instance.IsGameOver)
                {
                    if (!hasRequestedInitialTask)
                    {
                        hasRequestedInitialTask = true;
                        TaskManager.Instance?.RequestAssignedTargetTaskRpc(OwnerClientId);
                    }
                    else if (Time.time >= nextTaskAvailableTime + 2.5f)
                    {
                        nextTaskAvailableTime = Time.time + 3.0f;
                        TaskManager.Instance?.RequestAssignedTargetTaskRpc(OwnerClientId);
                    }
                }
                else if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("Aguardando início da partida...");
                }
            }

            UpdateTaskInteraction(moveAmount.sqrMagnitude > 0.01f);
        }
    }

    private void HandlePausedUpdate()
    {
        if (isAiming)
        {
            SetAiming(false);
        }

        if (isCastingReveal)
        {
            CancelHunterRevealCasting();
        }

        isPressingRun = false;
        if (movementLogic != null)
        {
            movementLogic.SetRunningState(false);
        }

        if (!isGhost.Value)
        {
            bool hunterKnockedDown = isHunter && hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown;
            if (!hunterKnockedDown && movementLogic != null && cameraLogic != null)
            {
                movementLogic.HandleMovement(Vector2.zero, cameraLogic.GetForwardDirection(), cameraLogic.GetRightDirection(), false);
            }

            bool isGrounded = movementLogic != null && movementLogic.CheckIfGrounded();
            animationLogic?.HandleAnimation(Vector2.zero, false, isGrounded);
            footstepLogic?.HandleFootsteps(false, false, isGrounded);
        }

        if (GameHUDController.Instance != null && movementLogic != null)
        {
            GameHUDController.Instance.SetStamina(movementLogic.CurrentStamina, movementLogic.MaxStamina);
            if (isHunter)
            {
                GameHUDController.Instance.SetFireballCooldown(Time.time - lastFireballTime, fireballCooldown);
            }
            else
            {
                GameHUDController.Instance.SetKickCooldown(Time.time - lastKickTime, kickCooldown);
                UpdateSurvivorRevelationHUD();
            }
        }
    }

    [Header("Sistema de Tasks")]
    private float currentTaskHoldTime = 0f;
    private bool isInteractingWithTask = false;
    private readonly HashSet<int> localCompletedTaskIndices = new HashSet<int>();
    private int currentAssignedTaskIndex = -1;
    public int CurrentAssignedTaskIndex => currentAssignedTaskIndex;

    [Header("Inatividade de Tasks & Tempo até Revelação")]
    private float taskAssignedTime = 0f;
    private float taskTimeoutDuration = 85.0f;
    private bool hasAssignedTask = false;
    private float lastTaskRequestTime = 0f;
    private float nextTaskAvailableTime = 0f;
    private bool hasRequestedInitialTask = false;

    /// <summary>
    /// Retorna o Hunter vivo mais próximo da posição especificada.
    /// </summary>
    public static PlayerController GetClosestHunterPlayer(Vector3 fromPosition)
    {
        if (ActivePlayers == null) return null;
        PlayerController closest = null;
        float minDist = float.MaxValue;
        for (int i = 0; i < ActivePlayers.Count; i++)
        {
            var p = ActivePlayers[i];
            if (p != null && p.IsHunter && !p.IsDead && !p.IsGhost)
            {
                float d = Vector3.Distance(fromPosition, p.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    closest = p;
                }
            }
        }
        return closest;
    }

    /// <summary>
    /// Aplica ou remove localmente a silhueta roxa de task no Hunter.
    /// </summary>
    public static void SetHunterTaskTargetVisual(PlayerController hunter, bool isTarget)
    {
        if (hunter == null) return;
        const uint purpleOutlineMask = 4u; // TargetTaskRenderingLayerMask (Bit 2 - Roxo LineworkLite)
        var renderers = hunter.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (isTarget)
            {
                r.renderingLayerMask |= purpleOutlineMask;
            }
            else
            {
                r.renderingLayerMask &= ~purpleOutlineMask;
            }
        }
    }

    private TaskObject GetAssignedTaskObject()
    {
        if (currentAssignedTaskIndex < 0) return null;
        var allTasks = TaskObject.AllTasks;
        if (allTasks != null && currentAssignedTaskIndex < allTasks.Count)
        {
            return allTasks[currentAssignedTaskIndex];
        }
        return null;
    }

    private void CompleteLocalHunterKickTask()
    {
        var hunter = GetClosestHunterPlayer(transform.position);
        if (hunter != null)
        {
            SetHunterTaskTargetVisual(hunter, false);
        }

        taskIndicatorController?.ClearTarget();
        currentAssignedTaskIndex = -1;
        hasAssignedTask = false;
        nextTaskAvailableTime = Time.time + (TaskManager.Instance != null ? TaskManager.Instance.PlayerNextTaskCooldown : 10.0f);

        GameAudioManager.Instance?.PlayTaskComplete(transform.position);

        GameHUDController.Instance?.SetTargetTaskObjective("Aguardando próximo objetivo (10s)...");
        GameHUDController.Instance?.SetTaskPrompt(true, "A Morte", alreadyCompleted: true, isTarget: true);

        // Envia confirmação ao servidor
        CompleteTaskServerRpc(TaskManager.HunterKickTaskIndex);
    }

    private void CompleteLocalHunterDanceTask()
    {
        var hunter = GetClosestHunterPlayer(transform.position);
        if (hunter != null)
        {
            SetHunterTaskTargetVisual(hunter, false);
        }

        HunterDanceAreaVisual.Hide();
        taskIndicatorController?.ClearTarget();
        currentAssignedTaskIndex = -1;
        hasAssignedTask = false;
        nextTaskAvailableTime = Time.time + (TaskManager.Instance != null ? TaskManager.Instance.PlayerNextTaskCooldown : 10.0f);

        GameAudioManager.Instance?.PlayTaskComplete(transform.position);

        GameHUDController.Instance?.SetTargetTaskObjective("Aguardando próximo objetivo (10s)...");
        GameHUDController.Instance?.SetTaskPrompt(true, "Dançar", alreadyCompleted: true, isTarget: true);

        // Envia confirmação ao servidor
        CompleteTaskServerRpc(TaskManager.HunterDanceTaskIndex);
    }

    /// <summary>
    /// Atribui uma nova tarefa objetivo para o jogador local.
    /// Remove o contorno roxo da tarefa anterior (restaurando cinza) e
    /// ativa o contorno ROXO na nova tarefa objetivo que somente este jogador enxerga.
    /// Suporta tasks de objeto, chutar o Hunter (Morte) e dançar próximo à Morte.
    /// </summary>
    public void SetAssignedTargetTask(int taskIndex)
    {
        if (isDead.Value || isGhost.Value || isDying || isHunter)
        {
            currentAssignedTaskIndex = -1;
            hasAssignedTask = false;
            TaskObject.ClearAllLocalTargets();
            taskIndicatorController?.ClearTarget();
            if (IsOwner)
            {
                GameHUDController.Instance?.SetTargetTaskObjective("");
                GameHUDController.Instance?.SetTaskPrompt(false, "", false);
                GameHUDController.Instance?.HideSurvivorRevelationTimer();
            }
            return;
        }

        TaskObject.EnsureTasksSorted();
        var allTasks = TaskObject.AllTasks;

        // Se havia uma tarefa anterior com contorno roxo, volta para cinza
        if (currentAssignedTaskIndex >= 0 && currentAssignedTaskIndex < allTasks.Count)
        {
            var prevTask = allTasks[currentAssignedTaskIndex];
            if (prevTask != null)
            {
                prevTask.SetAsLocalTarget(false);
            }
        }
        else if (currentAssignedTaskIndex == TaskManager.HunterKickTaskIndex || currentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex)
        {
            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null)
            {
                SetHunterTaskTargetVisual(hunter, false);
            }
            HunterDanceAreaVisual.Hide();
        }

        currentAssignedTaskIndex = taskIndex;

        if (TaskManager.IsValidTaskIndex(taskIndex))
        {
            hasRequestedInitialTask = true;
            nextTaskAvailableTime = 0f;
            taskAssignedTime = Time.time;
            taskTimeoutDuration = TaskManager.Instance != null ? TaskManager.Instance.TaskInactivityTimeout : 85.0f;
            hasAssignedTask = true;
        }
        else
        {
            hasAssignedTask = false;
            if (nextTaskAvailableTime <= Time.time)
            {
                nextTaskAvailableTime = Time.time + (TaskManager.Instance != null ? TaskManager.Instance.PlayerNextTaskCooldown : 10.0f);
            }
        }

        // Ativa o contorno ROXO na nova tarefa objetivo
        if (currentAssignedTaskIndex == TaskManager.HunterKickTaskIndex)
        {
            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null)
            {
                SetHunterTaskTargetVisual(hunter, true);
            }
            HunterDanceAreaVisual.Hide();

            string objName = "Chutar a Morte";
            if (isMarkedForDeath)
            {
                GameHUDController.Instance?.SetTargetTaskObjective($"<color=#FF4444>[REVELADO AO HUNTER!]</color> {objName}");
            }
            else
            {
                GameHUDController.Instance?.SetTargetTaskObjective(objName);
            }
            Debug.Log($"[PlayerController] Nova task objetivo atribuída: CHUTAR A MORTE marcada em ROXO para o jogador local!");
        }
        else if (currentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex)
        {
            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null)
            {
                SetHunterTaskTargetVisual(hunter, true);
                HunterDanceAreaVisual.Show(hunter.transform);
            }

            string objName = "Dançar Próximo à Morte [2 ou 3]";
            if (isMarkedForDeath)
            {
                GameHUDController.Instance?.SetTargetTaskObjective($"<color=#FF4444>[REVELADO AO HUNTER!]</color> {objName}");
            }
            else
            {
                GameHUDController.Instance?.SetTargetTaskObjective(objName);
            }
            Debug.Log($"[PlayerController] Nova task objetivo atribuída: DANÇAR PRÓXIMO À MORTE marcada em ROXO com CÍRCULO no chão para o jogador local!");
        }
        else if (currentAssignedTaskIndex >= 0 && currentAssignedTaskIndex < allTasks.Count)
        {
            var newTask = allTasks[currentAssignedTaskIndex];
            if (newTask != null)
            {
                newTask.SetAsLocalTarget(true);
                string objName = $"Interagir com {newTask.TaskName}";
                if (isMarkedForDeath)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective($"<color=#FF4444>[REVELADO AO HUNTER!]</color> {objName}");
                }
                else
                {
                    GameHUDController.Instance?.SetTargetTaskObjective(objName);
                }
                Debug.Log($"[PlayerController] Nova task objetivo atribuída: #{taskIndex} ('{newTask.TaskName}') marcada em ROXO para o jogador local!");
            }
        }
        else
        {
            GameHUDController.Instance?.SetTargetTaskObjective("Aguardando próximo objetivo (10s)...");
        }

        // Atualiza o direcional do Target Indicators para este sobrevivente local
        if (IsOwner && !isHunter)
        {
            if (taskIndicatorController == null)
            {
                taskIndicatorController = GetComponent<PlayerTaskIndicatorController>();
                if (taskIndicatorController == null)
                {
                    taskIndicatorController = gameObject.AddComponent<PlayerTaskIndicatorController>();
                }
            }
            taskIndicatorController.UpdateTargetTask(taskIndex);
        }
    }

    #region Marcado para a Morte (Inatividade de Tasks)

    [Header("Marcado para a Morte (Inatividade de Tasks)")]
    private bool isMarkedForDeath = false;
    public bool IsMarkedForDeath => isMarkedForDeath;

    /// <summary>
    /// Define o estado de "Marcado para a Morte" deste sobrevivente por inatividade (não concluiu task em 1m25s).
    /// Quando verdadeiro, ativa o contorno roxo (TargetTaskRenderingLayerMask = 4), torna a SoulOrb visível para o Hunter,
    /// e emite um alerta na tela.
    /// </summary>
    public void SetMarkedForDeath(bool marked)
    {
        isMarkedForDeath = marked;
        ApplyMarkedForDeathVisuals(marked);

        if (marked)
        {
            GameAudioManager.Instance?.PlayMarkedForDeath(transform.position);
        }

        if (IsOwner && !isHunter)
        {
            var allTasks = TaskObject.AllTasks;
            bool hasValidTask = currentAssignedTaskIndex >= 0 && allTasks != null &&
                                currentAssignedTaskIndex < allTasks.Count && allTasks[currentAssignedTaskIndex] != null;
            bool isHunterKickTask = currentAssignedTaskIndex == TaskManager.HunterKickTaskIndex;
            bool isHunterDanceTask = currentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex;

            if (marked)
            {
                if (isHunterKickTask)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("<color=#FF4444>[REVELADO AO HUNTER!]</color> Chutar a Morte");
                }
                else if (isHunterDanceTask)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("<color=#FF4444>[REVELADO AO HUNTER!]</color> Dançar Próximo à Morte [2 ou 3]");
                }
                else if (hasValidTask)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective($"<color=#FF4444>[REVELADO AO HUNTER!]</color> Interagir com {allTasks[currentAssignedTaskIndex].TaskName}");
                }
                else
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("<color=#FF4444>VOCÊ ESTÁ REVELADO AO HUNTER!</color>");
                }
            }
            else
            {
                if (isHunterKickTask)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("Chutar a Morte");
                }
                else if (isHunterDanceTask)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("Dançar Próximo à Morte [2 ou 3]");
                }
                else if (hasValidTask)
                {
                    GameHUDController.Instance?.SetTargetTaskObjective($"Interagir com {allTasks[currentAssignedTaskIndex].TaskName}");
                }
                else
                {
                    GameHUDController.Instance?.SetTargetTaskObjective("Aguardando próximo objetivo (10s)...");
                }
            }
        }

        Debug.Log($"[PlayerController] Player '{Nickname}' (Client {OwnerClientId}) Marcado para a Morte: {marked}");
    }

    /// <summary>
    /// Aplica ou remove a rendering layer de contorno roxo (m_Bits: 4) nos renderers do modelo deste jogador.
    /// </summary>
    public void ApplyMarkedForDeathVisuals(bool marked)
    {
        const uint purpleOutlineMask = 4u; // Bit 2 do LineworkLite (TargetTaskOutline - Roxo)
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            if (marked)
            {
                r.renderingLayerMask |= purpleOutlineMask;
            }
            else
            {
                r.renderingLayerMask &= ~purpleOutlineMask;
            }
        }
    }

    #endregion

    /// <summary>
    /// Gerencia a proximidade e a interação contínua (segurar o botão esquerdo) em objetos de Task.
    /// </summary>
    private void UpdateTaskInteraction(bool isMoving)
    {
        // Se a partida não está ativa (a Morte ainda está caindo do céu) ou o jogo acabou, nenhuma task pode ser realizada
        if (GameSpawner.Instance != null && (!GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.IsGameOver))
        {
            if (isInteractingWithTask) CancelTaskInteraction();
            GameHUDController.Instance?.SetTaskPrompt(false, "", false);
            return;
        }

        // Se o sobrevivente ainda não tem nenhuma task atribuída, não exibe prompts de task
        if (currentAssignedTaskIndex == -1 || !TaskManager.IsValidTaskIndex(currentAssignedTaskIndex))
        {
            if (isInteractingWithTask) CancelTaskInteraction();
            GameHUDController.Instance?.SetTaskPrompt(false, "", false);
            return;
        }

        // Se a task atual do jogador for Chutar o Hunter (a Morte)
        if (currentAssignedTaskIndex == TaskManager.HunterKickTaskIndex)
        {
            if (isInteractingWithTask) CancelTaskInteraction();

            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null && !hunter.IsDead && !hunter.IsGhost)
            {
                float dist = Vector3.Distance(transform.position, hunter.transform.position);
                float promptDist = kickRange + 1.2f;
                if (dist <= promptDist)
                {
                    GameHUDController.Instance?.SetTaskPrompt(true, "A Morte", alreadyCompleted: false, isTarget: true);
                }
                else
                {
                    GameHUDController.Instance?.SetTaskPrompt(false, "", false);
                }
            }
            else
            {
                GameHUDController.Instance?.SetTaskPrompt(false, "", false);
            }
            return;
        }

        // Se a task atual do jogador for Dançar Próximo à Morte
        if (currentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex)
        {
            if (isInteractingWithTask) CancelTaskInteraction();

            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null && !hunter.IsDead && !hunter.IsGhost)
            {
                // Garante que o círculo roxo no chão esteja ativo no Hunter mais próximo
                HunterDanceAreaVisual.Show(hunter.transform);

                bool insideArea = HunterDanceAreaVisual.IsPlayerInsideDanceArea(transform.position, hunter.transform);
                if (insideArea)
                {
                    GameHUDController.Instance?.SetTaskPrompt(true, "Dançar", alreadyCompleted: false, isTarget: true);
                }
                else
                {
                    float dist = Vector3.Distance(transform.position, hunter.transform.position);
                    if (dist <= 10.0f)
                    {
                        GameHUDController.Instance?.SetTaskPrompt(true, "Aproxime-se para Dançar", alreadyCompleted: false, isTarget: true);
                    }
                    else
                    {
                        GameHUDController.Instance?.SetTaskPrompt(false, "", false);
                    }
                }
            }
            else
            {
                HunterDanceAreaVisual.Hide();
                GameHUDController.Instance?.SetTaskPrompt(false, "", false);
            }
            return;
        }

        var allTasks = TaskObject.AllTasks;
        if (allTasks == null || allTasks.Count == 0)
        {
            if (isInteractingWithTask) CancelTaskInteraction();
            return;
        }

        Vector3 myPos = transform.position;
        TaskObject nearestTask = null;
        int nearestTaskIndex = -1;
        float minDistance = float.MaxValue;

        for (int i = 0; i < allTasks.Count; i++)
        {
            var task = allTasks[i];
            if (task == null || !task.gameObject.activeInHierarchy) continue;

            float dist = Vector3.Distance(myPos, task.transform.position);
            if (dist <= task.InteractionDistance && dist < minDistance)
            {
                minDistance = dist;
                nearestTask = task;
                nearestTaskIndex = i;
            }
        }

        if (nearestTask != null && nearestTaskIndex >= 0)
        {
            // O jogador SÓ pode interagir com a task marcada exclusivamente para ele (contorno roxo)
            bool isMyTarget = (nearestTaskIndex == currentAssignedTaskIndex);
            if (!isMyTarget)
            {
                if (isInteractingWithTask)
                {
                    CancelTaskInteraction();
                }
                GameHUDController.Instance?.SetTaskPrompt(true, nearestTask.TaskName, alreadyCompleted: false, isTarget: false);
                return;
            }

            bool alreadyDone = localCompletedTaskIndices.Contains(nearestTaskIndex) ||
                               (TaskManager.Instance != null && TaskManager.Instance.HasPlayerCompletedTask(OwnerClientId, nearestTaskIndex));

            if (alreadyDone)
            {
                if (isInteractingWithTask)
                {
                    CancelTaskInteraction();
                }
                GameHUDController.Instance?.SetTaskPrompt(true, nearestTask.TaskName, alreadyCompleted: true, isTarget: true);
                return;
            }

            // Verifica se o jogador está segurando o botão esquerdo do mouse (LMB / Attack)
            bool lmbHeld = false;
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                lmbHeld = UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            }
            if (!lmbHeld && playerInput != null && playerInput.actions != null)
            {
                var attackAction = playerInput.actions.FindAction("Attack");
                if (attackAction != null && attackAction.IsPressed())
                {
                    lmbHeld = true;
                }
            }

            if (lmbHeld)
            {
                // Se o jogador estiver se movendo, cancela a interação para exigir que ele fique parado na estação
                if (isMoving)
                {
                    if (isInteractingWithTask)
                    {
                        CancelTaskInteraction();
                    }
                    GameHUDController.Instance?.SetTaskPrompt(true, nearestTask.TaskName, alreadyCompleted: false, isTarget: true);
                    return;
                }

                if (!isInteractingWithTask)
                {
                    isInteractingWithTask = true;
                    GameAudioManager.Instance?.PlayTaskStart(nearestTask.transform.position);
                    if (animationLogic != null)
                    {
                        animationLogic.SetTaskInteractionAnimation(true);
                    }
                }

                // Rotaciona suavemente para olhar de frente para o objeto da task
                Vector3 dirToTask = nearestTask.transform.position - transform.position;
                dirToTask.y = 0f;
                if (dirToTask.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dirToTask.normalized), 8f * Time.deltaTime);
                }

                currentTaskHoldTime += Time.deltaTime;

                float duration = TaskManager.Instance != null ? TaskManager.Instance.TaskInteractionDuration : 10.0f;
                GameHUDController.Instance?.SetTaskInteractionProgress(true, currentTaskHoldTime, duration, nearestTask.TaskName);

                if (currentTaskHoldTime >= duration)
                {
                    // Task concluída com sucesso!
                    isInteractingWithTask = false;
                    currentTaskHoldTime = 0f;
                    if (animationLogic != null)
                    {
                        animationLogic.SetTaskInteractionAnimation(false);
                    }
                    GameHUDController.Instance?.SetTaskInteractionProgress(false, 0, duration, "");

                    CompleteLocalTask(nearestTask, nearestTaskIndex);
                }
            }
            else
            {
                // Se soltou o botão antes de completar, a task não é concluída e reseta o progresso para 0
                if (isInteractingWithTask)
                {
                    CancelTaskInteraction();
                }

                GameHUDController.Instance?.SetTaskPrompt(true, nearestTask.TaskName, alreadyCompleted: false, isTarget: true);
            }
        }
        else
        {
            // Fora de alcance de qualquer task
            if (isInteractingWithTask)
            {
                CancelTaskInteraction();
            }
            GameHUDController.Instance?.SetTaskPrompt(false, "", false);
        }
    }

    public void CancelTaskInteraction()
    {
        if (isInteractingWithTask && animationLogic != null)
        {
            animationLogic.SetTaskInteractionAnimation(false);
        }
        isInteractingWithTask = false;
        currentTaskHoldTime = 0f;
        float duration = TaskManager.Instance != null ? TaskManager.Instance.TaskInteractionDuration : 10.0f;
        GameHUDController.Instance?.SetTaskInteractionProgress(false, 0, duration, "");
    }

    private void CompleteLocalTask(TaskObject task, int taskIndex)
    {
        if (localCompletedTaskIndices.Contains(taskIndex)) return;
        localCompletedTaskIndices.Add(taskIndex);

        // Remove o contorno roxo localmente para este jogador
        task.SetAsLocalTarget(false);
        taskIndicatorController?.ClearTarget();
        currentAssignedTaskIndex = -1;
        hasAssignedTask = false;
        nextTaskAvailableTime = Time.time + (TaskManager.Instance != null ? TaskManager.Instance.PlayerNextTaskCooldown : 10.0f);

        GameAudioManager.Instance?.PlayTaskComplete(task.transform.position);

        GameHUDController.Instance?.SetTargetTaskObjective("Aguardando próximo objetivo (10s)...");
        GameHUDController.Instance?.SetTaskPrompt(true, task.TaskName, alreadyCompleted: true, isTarget: true);

        // Envia confirmação ao servidor
        CompleteTaskServerRpc(taskIndex);
    }

    [ServerRpc]
    private void CompleteTaskServerRpc(int taskIndex)
    {
        if (GameSpawner.Instance != null && (!GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.IsGameOver))
        {
            return;
        }
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnPlayerCompletedTask(OwnerClientId, taskIndex);
        }
    }

    public void OnLocalTaskCompletedConfirmed(int taskIndex)
    {
        localCompletedTaskIndices.Add(taskIndex);
        if (taskIndex >= 0)
        {
            var allTasks = TaskObject.AllTasks;
            if (taskIndex < allTasks.Count && allTasks[taskIndex] != null)
            {
                allTasks[taskIndex].SetAsLocalTarget(false);
            }
        }
        else if (taskIndex == TaskManager.HunterKickTaskIndex || taskIndex == TaskManager.HunterDanceTaskIndex)
        {
            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null)
            {
                SetHunterTaskTargetVisual(hunter, false);
            }
            HunterDanceAreaVisual.Hide();
        }
        currentAssignedTaskIndex = -1;
        taskIndicatorController?.ClearTarget();
        hasAssignedTask = false;
        nextTaskAvailableTime = Time.time + (TaskManager.Instance != null ? TaskManager.Instance.PlayerNextTaskCooldown : 10.0f);
        UpdateSurvivorRevelationHUD();
    }

    /// <summary>
    /// Atualiza o elemento de interface que exibe a contagem regressiva e a barra que cai até o sobrevivente ser revelado ao Hunter.
    /// </summary>
    private void UpdateSurvivorRevelationHUD()
    {
        if (GameHUDController.Instance == null) return;

        if (isHunter || isGhost.Value || isDead.Value || isDying)
        {
            GameHUDController.Instance.HideSurvivorRevelationTimer();
            return;
        }

        // Se a partida ainda não começou (a Morte ainda está caindo do céu), oculta o timer
        if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
        {
            GameHUDController.Instance.HideSurvivorRevelationTimer();
            return;
        }

        float remaining = 0f;
        if (hasAssignedTask)
        {
            float elapsed = Time.time - taskAssignedTime;
            remaining = Mathf.Max(0f, taskTimeoutDuration - elapsed);
        }

        GameHUDController.Instance.SetSurvivorRevelationTimer(
            remaining,
            taskTimeoutDuration,
            isMarkedForDeath,
            hasAssignedTask
        );
    }

    /// <summary>
    /// Processa o ciclo de atualização do jogador no Modo Espectador (Fantasma).
    /// Permite voo livre:
    /// - WASD: movimento horizontal orientado pela câmera.
    /// - Espaço: voa para cima (ganha altitude).
    /// - Ctrl: voa para baixo (perde altitude).
    /// - Shift: impulso rápido (boost de voo).
    /// - Sem gravidade, sem atrito e sem bloqueio em pequenos obstáculos.
    /// </summary>
    private void HandleSpectatorUpdate()
    {
        // 1. Entrada horizontal (WASD / Teclas direcionais / Analógico)
        Vector2 move = moveAmount;
        if (move.sqrMagnitude < 0.01f && UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            float x = 0f;
            float y = 0f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            move = new Vector2(x, y);
        }

        // 2. Entrada vertical (Espaço = voar para cima / Ctrl = voar para baixo)
        float verticalInput = 0f;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb.spaceKey.isPressed)
            {
                verticalInput += 1f;
            }
            if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)
            {
                verticalInput -= 1f;
            }
        }

        if (verticalInput == 0f && playerInput != null && playerInput.actions != null)
        {
            var jumpAction = playerInput.actions.FindAction("Jump");
            if (jumpAction != null && jumpAction.IsPressed())
            {
                verticalInput += 1f;
            }
            var crouchAction = playerInput.actions.FindAction("Crouch");
            if (crouchAction != null && crouchAction.IsPressed())
            {
                verticalInput -= 1f;
            }
        }

        // 3. Velocidade rápida (Shift / Boost de voo)
        bool isFast = isPressingRun;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
            {
                isFast = true;
            }
        }

        // 4. Direções relativas à câmera
        Vector3 camForward = cameraLogic != null ? cameraLogic.GetForwardDirection() : transform.forward;
        Vector3 camRight = cameraLogic != null ? cameraLogic.GetRightDirection() : transform.right;

        // 5. Executa a movimentação de voo livre
        if (movementLogic != null)
        {
            movementLogic.HandleSpectatorFlight(move, camForward, camRight, verticalInput, isFast);
        }

        // 6. Inclinação suave e flutuação do modelo do fantasma ao subir/descer
        if (ghostVisual != null)
        {
            float targetPitch = verticalInput * -12f;
            Quaternion targetRot = Quaternion.Euler(targetPitch, 0f, 0f);
            ghostVisual.transform.localRotation = Quaternion.Slerp(ghostVisual.transform.localRotation, targetRot, 8f * Time.deltaTime);
        }

        // 7. Mantém a interface de stamina cheia para não incomodar o espectador
        if (GameHUDController.Instance != null && movementLogic != null)
        {
            GameHUDController.Instance.SetStamina(movementLogic.MaxStamina, movementLogic.MaxStamina);
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        moveAmount = context.ReadValue<Vector2>();
    }

    public void OnRun(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch)
        {
            isPressingRun = false;
            movementLogic?.SetRunningState(false);
            return;
        }

        isPressingRun = context.ReadValueAsButton();
        bool canRun = isPressingRun && !isAttacking && !isCastingFireball && (!isHunter || !isAiming) && (movementLogic != null && movementLogic.CanRun);
        movementLogic.SetRunningState(canRun);
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (isDying || isGhost.Value) return;
        if (isGreeting || isDancing) return; // Não pula enquanto estiver no cumprimento social ou dançando

        if (context.started)
        {
            // Se for o Hunter e ele estiver caído no chão, aperta Espaço para levantar
            if (isHunter && hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown)
            {
                if (hunterKnockdownLogic.CanRecover)
                {
                    hunterKnockdownLogic.TryRecover();
                }
                return;
            }

            if (movementLogic.TriggerJump())
            {
                animationLogic.TriggerJumpAnimation();
            }
        }
    }

    public void OnKick(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;

        if (context.started || context.performed)
        {
            // Fantasmas e jogadores mortos NÃO podem chutar!
            if (!isHunter && !isGhost.Value && !isDead.Value && !isDying)
            {
                PerformKick();
            }
        }
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;
        if (isHunter && hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro) return;

        if (context.performed)
        {
            if (isHunter && !isDead.Value && !isGhost.Value && !isDying && !isAttacking && !isCastingFireball && !isCastingReveal)
            {
                if (isAiming)
                {
                    PerformHunterFireball();
                }
                else
                {
                    PerformHunterAttack();
                }
            }
        }
    }

    public void OnAim(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch)
        {
            if (isAiming) SetAiming(false);
            return;
        }
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;
        if (isHunter && hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro) return;
        if (isHunter && isCastingReveal)
        {
            if (isAiming) SetAiming(false);
            return;
        }

        if (context.started || context.performed)
        {
            SetAiming(true);
        }
        else if (context.canceled)
        {
            SetAiming(false);
        }
    }

    public void OnFireball(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;
        if (isHunter && hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro) return;

        if (context.performed)
        {
            if (isHunter && !isDead.Value && !isGhost.Value && !isDying && !isAttacking && !isCastingFireball && !isCastingReveal)
            {
                PerformHunterFireball();
            }
        }
    }

    public void SetAiming(bool aiming)
    {
        if (!isHunter || isDead.Value || isGhost.Value || isDying)
        {
            isAiming = false;
            return;
        }

        if (hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown)
        {
            isAiming = false;
            return;
        }

        if (isAiming == aiming) return;
        isAiming = aiming;

        if (movementLogic != null)
        {
            bool canRun = isPressingRun && !isAttacking && !isCastingFireball && (!isHunter || !isAiming);
            movementLogic.SetRunningState(canRun);
        }

        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.SetAiming(isAiming);
        }

        if (cameraSetup != null)
        {
            cameraSetup.SetAiming(isAiming);
        }
    }

    private void PerformHunterFireball()
    {
        if (isAttacking || isCastingFireball) return;
        if (hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown) return;
        if (Time.time - lastFireballTime < fireballCooldown) return;
        lastFireballTime = Time.time;

        // Alinha o personagem na direção em que a câmera do jogador está mirando
        if (cameraLogic != null)
        {
            Vector3 lookDir = cameraLogic.GetForwardDirection();
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }

        // Dispara a animação Standing 1H Magic Attack 01
        animationLogic.TriggerFireballAnimation();

        // Replica o início da invocação na rede para os outros jogadores verem o fogo na mão
        if (IsSpawned)
        {
            TriggerHunterFireballInvocationServerRpc();
        }

        if (fireballRoutine != null) StopCoroutine(fireballRoutine);
        fireballRoutine = StartCoroutine(HunterFireballRoutine());

        Debug.Log("[PlayerController] A Morte disparou Bola de Fogo (Standing 1H Magic Attack 01)!");
    }

    private IEnumerator HunterFireballRoutine()
    {
        isAttacking = true;
        isCastingFireball = true;

        GameObject chargeVfxInstance = null;
        try
        {
            Transform handBone = GetHunterRightHand();
            HunterFireballInvocation invComp = null;

            // 1. Spawna efeito de invocação mágica na mão direita durante o windup
            if (handChargeVfxPrefab != null && handBone != null)
            {
                chargeVfxInstance = Instantiate(handChargeVfxPrefab, handBone);
                chargeVfxInstance.transform.localPosition = new Vector3(0.04f, 0.04f, 0f);
                chargeVfxInstance.transform.localRotation = Quaternion.identity;
                invComp = chargeVfxInstance.GetComponent<HunterFireballInvocation>();
            }

            // 2. Aguarda até o instante exato em que a mão direita avança para frente (0.65s com speed 1.25)
            float chargeDuration = 0.65f;
            float elapsed = 0f;
            while (elapsed < chargeDuration)
            {
                // Enquanto carrega o feitiço, mantém o Hunter orientado para a câmera/mira
                if (cameraLogic != null)
                {
                    Vector3 lookDir = cameraLogic.GetForwardDirection();
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        transform.rotation = Quaternion.LookRotation(lookDir);
                    }
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Transiciona organicamente o efeito de invocação na mão para o projétil
            if (invComp != null)
            {
                invComp.ReleaseAndDestroy();
                chargeVfxInstance = null;
            }
            else if (chargeVfxInstance != null)
            {
                Destroy(chargeVfxInstance);
                chargeVfxInstance = null;
            }

            // 3. Calcula a posição de lançamento na mão direita e a direção em direção à mira da câmera
            Vector3 spawnPos = handBone != null 
                ? handBone.position + transform.forward * 0.35f + Vector3.up * 0.1f 
                : transform.position + Vector3.up * 1.45f + transform.forward * 0.6f;
            Vector3 shootDir = CalculateFireballTargetDirection(spawnPos, out PlayerController aimedPlayer, out NPCController aimedNpc);

            // 4. Spawna o projétil da bola de fogo localmente (priorizando exatamente o alvo focado)
            SpawnFireballProjectile(spawnPos, shootDir, aimedPlayer, aimedNpc);

            // 5. Replica o lançamento para os outros clientes na rede
            if (IsSpawned)
            {
                ulong pNetId = (aimedPlayer != null && aimedPlayer.IsSpawned) ? aimedPlayer.NetworkObjectId : ulong.MaxValue;
                ulong nNetId = (aimedNpc != null && aimedNpc.IsSpawned) ? aimedNpc.NetworkObjectId : ulong.MaxValue;
                TriggerHunterFireballServerRpc(spawnPos, shootDir, pNetId, nNetId);
            }

            // 6. Aguarda o término da animação de conjuração
            yield return new WaitForSeconds(0.65f);
        }
        finally
        {
            if (chargeVfxInstance != null) Destroy(chargeVfxInstance);
            isAttacking = false;
            isCastingFireball = false;
            fireballRoutine = null;
            if (animationLogic != null)
            {
                animationLogic.ResetFireballTrigger();
            }
        }
    }

    private Vector3 CalculateFireballTargetDirection(Vector3 spawnPos, out PlayerController aimedPlayer, out NPCController aimedNpc)
    {
        aimedPlayer = null;
        aimedNpc = null;

        Camera mainCam = null;
        if (cameraSetup != null && cameraSetup.UnityCamera != null)
        {
            mainCam = cameraSetup.UnityCamera;
        }
        if (mainCam == null)
        {
            mainCam = Camera.main;
        }
        if (mainCam == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null && cam.enabled) mainCam = cam;
        }

        if (mainCam == null)
        {
            Vector3 fwd = cameraLogic != null ? cameraLogic.GetForwardDirection() : transform.forward;
            return fwd;
        }

        // Raycast a partir do centro da tela (mira/crosshair exatamente no centro)
        Ray aimRay = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = aimRay.origin + aimRay.direction * 45f;
        float bestTargetDist = float.MaxValue;

        // 1. Prioridade: Se a mira estiver apontada diretamente para um Player sobrevivente
        if (ActivePlayers != null)
        {
            for (int i = 0; i < ActivePlayers.Count; i++)
            {
                var p = ActivePlayers[i];
                if (p == null || p == this || p.IsHunter || p.IsDead || p.IsGhost) continue;

                Vector3 pBottom = p.transform.position + Vector3.up * 0.25f;
                Vector3 pTop = p.transform.position + Vector3.up * 1.65f;
                float d = DistanceRayToSegment(aimRay, pBottom, pTop, out float distRay, out _, out Vector3 ptTarget);

                // Tolerância de mira (0.45m da linha de centro do personagem)
                if (d <= 0.45f && distRay > 1.2f && distRay < bestTargetDist)
                {
                    bestTargetDist = distRay;
                    targetPoint = ptTarget;
                    aimedPlayer = p;
                    aimedNpc = null;
                }
            }
        }

        // 2. Prioridade: Se a mira estiver apontada diretamente para um NPC
        if (NPCController.ActiveNpcs != null)
        {
            var npcs = NPCController.ActiveNpcs;
            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                if (n == null || n.IsDead) continue;

                Vector3 nBottom = n.transform.position + Vector3.up * 0.25f;
                Vector3 nTop = n.transform.position + Vector3.up * 1.65f;
                float d = DistanceRayToSegment(aimRay, nBottom, nTop, out float distRay, out _, out Vector3 ptTarget);

                if (d <= 0.45f && distRay > 1.2f && distRay < bestTargetDist)
                {
                    bestTargetDist = distRay;
                    targetPoint = ptTarget;
                    aimedNpc = n;
                    aimedPlayer = null;
                }
            }
        }

        // 3. Raycast contra o cenário (paredes, portas, chão, obstáculos físicos)
        int mask = ~0;
        RaycastHit[] hits = Physics.RaycastAll(aimRay, 100f, mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(this.transform)) continue;
            var hitPlayer = h.collider.GetComponentInParent<PlayerController>();
            if (hitPlayer == this) continue;

            // Ignora colisões atrás do ombro/olhos do Hunter
            if (Vector3.Dot(h.point - transform.position, aimRay.direction) < -0.2f) continue;

            // Se o obstáculo do cenário estiver mais próximo que o alvo focado, a parede bloqueia a visão
            if (h.distance < bestTargetDist)
            {
                targetPoint = h.point;
                aimedPlayer = null;
                aimedNpc = null;
            }
            break;
        }

        // Trajetória limpa convergindo da mão até o ponto mirado pela mira
        Vector3 dir = (targetPoint - spawnPos).normalized;
        if (dir.sqrMagnitude < 0.001f || Vector3.Dot(dir, aimRay.direction) < 0.2f)
        {
            dir = aimRay.direction;
        }

        return dir;
    }

    /// <summary>
    /// Calcula a menor distância entre a linha de visada da mira e a coluna corporal do alvo (CCD de mira).
    /// </summary>
    public static float DistanceRayToSegment(Ray ray, Vector3 segA, Vector3 segB, out float distAlongRay, out Vector3 ptOnRay, out Vector3 ptOnTarget)
    {
        Vector3 u = ray.direction;
        Vector3 v = segB - segA;
        Vector3 w = ray.origin - segA;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);

        float denom = a * c - b * b;
        float s, t;

        if (denom < 1e-6f)
        {
            s = 0f;
            t = Mathf.Clamp01(e / c);
        }
        else
        {
            s = (b * e - c * d) / denom;
            t = (a * e - b * d) / denom;
            if (s < 0f)
            {
                s = 0f;
                t = Mathf.Clamp01(e / c);
            }
            else
            {
                t = Mathf.Clamp01(t);
                s = Mathf.Max(0f, Vector3.Dot(u, (segA + v * t) - ray.origin));
            }
        }

        distAlongRay = s;
        ptOnRay = ray.origin + u * s;
        ptOnTarget = segA + v * t;
        return Vector3.Distance(ptOnRay, ptOnTarget);
    }

    private void SpawnFireballProjectile(Vector3 spawnPos, Vector3 shootDir, PlayerController aimedPlayer = null, NPCController aimedNpc = null)
    {
        GameObject prefabToSpawn = fireballPrefab;
        if (prefabToSpawn == null)
        {
#if UNITY_EDITOR
            prefabToSpawn = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/HunterFireball.prefab");
#endif
        }

        if (prefabToSpawn != null)
        {
            GameObject projObj = Instantiate(prefabToSpawn, spawnPos, Quaternion.LookRotation(shootDir));
            var proj = projObj.GetComponent<HunterFireballProjectile>();
            if (proj != null)
            {
                proj.Initialize(shootDir, this, fireballExplosionPrefab, fireHitEffectPrefab, aimedPlayer, aimedNpc);
            }
        }
    }

    [ServerRpc]
    private void TriggerHunterFireballInvocationServerRpc()
    {
        TriggerHunterFireballInvocationClientRpc();
    }

    [ClientRpc]
    private void TriggerHunterFireballInvocationClientRpc()
    {
        if (IsOwner) return; // O atacante já está executando localmente
        if (fireballRoutine != null) StopCoroutine(fireballRoutine);
        fireballRoutine = StartCoroutine(HunterRemoteInvocationRoutine());
    }

    private IEnumerator HunterRemoteInvocationRoutine()
    {
        GameObject remoteInvocationInstance = null;
        try
        {
            Transform handBone = GetHunterRightHand();
            if (handChargeVfxPrefab != null && handBone != null)
            {
                remoteInvocationInstance = Instantiate(handChargeVfxPrefab, handBone);
                remoteInvocationInstance.transform.localPosition = new Vector3(0.04f, 0.04f, 0f);
                remoteInvocationInstance.transform.localRotation = Quaternion.identity;
            }

            yield return new WaitForSeconds(0.65f);

            if (remoteInvocationInstance != null)
            {
                var invComp = remoteInvocationInstance.GetComponent<HunterFireballInvocation>();
                if (invComp != null) invComp.ReleaseAndDestroy();
                else Destroy(remoteInvocationInstance);
                remoteInvocationInstance = null;
            }
        }
        finally
        {
            if (remoteInvocationInstance != null) Destroy(remoteInvocationInstance);
            fireballRoutine = null;
        }
    }

    [ServerRpc]
    private void TriggerHunterFireballServerRpc(Vector3 spawnPos, Vector3 shootDir, ulong targetPlayerNetId, ulong targetNpcNetId)
    {
        TriggerHunterFireballClientRpc(spawnPos, shootDir, targetPlayerNetId, targetNpcNetId);
    }

    [ClientRpc]
    private void TriggerHunterFireballClientRpc(Vector3 spawnPos, Vector3 shootDir, ulong targetPlayerNetId, ulong targetNpcNetId)
    {
        if (IsOwner) return; // O atacante já instanciou localmente

        PlayerController targetPlayer = null;
        NPCController targetNpc = null;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (targetPlayerNetId != ulong.MaxValue && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetPlayerNetId, out var pObj))
            {
                targetPlayer = pObj.GetComponent<PlayerController>();
            }
            if (targetNpcNetId != ulong.MaxValue && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNpcNetId, out var nObj))
            {
                targetNpc = nObj.GetComponent<NPCController>();
            }
        }

        SpawnFireballProjectile(spawnPos, shootDir, targetPlayer, targetNpc);
    }

    public void ReportFireballKillPlayer(PlayerController targetPlayer)
    {
        if (targetPlayer == null || targetPlayer.IsHunter || targetPlayer.IsDead || targetPlayer.IsGhost) return;

        if (IsServer)
        {
            targetPlayer.KillByHunter(this, "fireball");
        }
        else if (IsSpawned)
        {
            RequestKillPlayerFromFireballServerRpc(targetPlayer.NetworkObjectId);
        }
    }

    public void ReportFireballKillNpc(NPCController targetNpc)
    {
        if (targetNpc == null || targetNpc.IsDead) return;

        if (IsServer)
        {
            targetNpc.KillByHunter(this);
        }
        else if (IsSpawned)
        {
            RequestKillNpcFromFireballServerRpc(targetNpc.NetworkObjectId);
        }
    }

    #region Habilidade da Morte (Revelação Espiritual - Tecla E)

    private void UpdateHunterRevealAbility()
    {
        if (!IsOwner || !isHunter) return;
        if (isDead.Value || isGhost.Value || isDying)
        {
            if (isCastingReveal) CancelHunterRevealCasting();
            return;
        }

        if (hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro)
        {
            if (isCastingReveal) CancelHunterRevealCasting();
            return;
        }

        if (hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown)
        {
            if (isCastingReveal) CancelHunterRevealCasting();
            return;
        }

        float timeSinceReveal = Time.time - lastHunterRevealTime;
        GameHUDController.Instance?.SetHunterRevealCooldown(timeSinceReveal, hunterRevealCooldown);

        bool onCooldown = timeSinceReveal < hunterRevealCooldown;

        bool isPressingE = false;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            isPressingE = UnityEngine.InputSystem.Keyboard.current.eKey.isPressed;
        }
        if (!isPressingE && playerInput != null && playerInput.actions != null)
        {
            var kickAction = playerInput.actions.FindAction("Kick");
            if (kickAction != null && kickAction.IsPressed())
            {
                isPressingE = true;
            }
        }

        // Se está segurando a tecla E, não está em cooldown, e não está atacando/mirando
        if (isPressingE && !onCooldown && !isAttacking && !isCastingFireball)
        {
            if (!isCastingReveal)
            {
                isCastingReveal = true;
                hunterRevealHoldTimer = 0f;
                GameAudioManager.Instance?.StartHunterRevealCharge(transform);
                animationLogic?.SetHunterRevealCastAnimation(true);
                HunterRevealAreaVisual.Show(transform, hunterRevealRadius);

                if (IsSpawned)
                {
                    TriggerHunterRevealCastServerRpc(true);
                }
            }

            hunterRevealHoldTimer += Time.deltaTime;
            float castProgress = Mathf.Clamp01(hunterRevealHoldTimer / hunterRevealCastDuration);
            GameAudioManager.Instance?.UpdateHunterRevealChargePitch(castProgress, transform.position);

            // Atualiza barra de conjuração no HUD central
            GameHUDController.Instance?.SetHunterAbilityCastingProgress(true, hunterRevealHoldTimer, hunterRevealCastDuration, "REVELAR SOBREVIVENTES");

            if (hunterRevealHoldTimer >= hunterRevealCastDuration)
            {
                ExecuteHunterRevealAbility();
            }
        }
        else
        {
            if (isCastingReveal)
            {
                CancelHunterRevealCasting();
            }
        }
    }

    public void CancelHunterRevealCasting()
    {
        isCastingReveal = false;
        hunterRevealHoldTimer = 0f;
        GameAudioManager.Instance?.StopHunterRevealCharge();
        animationLogic?.SetHunterRevealCastAnimation(false, isCancelled: true);
        GameHUDController.Instance?.SetHunterAbilityCastingProgress(false, 0f, hunterRevealCastDuration, "");
        HunterRevealAreaVisual.Hide();

        if (IsSpawned)
        {
            TriggerHunterRevealCastServerRpc(false, isCancelled: true);
        }
    }

    private void ExecuteHunterRevealAbility()
    {
        isCastingReveal = false;
        hunterRevealHoldTimer = 0f;
        lastHunterRevealTime = Time.time;
        animationLogic?.SetHunterRevealCastAnimation(false, isCancelled: false);
        GameHUDController.Instance?.SetHunterAbilityCastingProgress(false, 0f, hunterRevealCastDuration, "");
        HunterRevealAreaVisual.TriggerPulseAndHide(transform.position, hunterRevealRadius);

        GameAudioManager.Instance?.StopHunterRevealCharge();
        GameAudioManager.Instance?.PlayHunterRevealRelease(transform.position);

        if (IsSpawned)
        {
            TriggerHunterRevealCastServerRpc(false, isCancelled: false);
            RequestHunterRevealServerRpc(transform.position, hunterRevealRadius, hunterRevealDuration);
        }
    }

    [ServerRpc]
    private void TriggerHunterRevealCastServerRpc(bool isCasting, bool isCancelled = false)
    {
        TriggerHunterRevealCastClientRpc(isCasting, isCancelled);
    }

    [ClientRpc]
    private void TriggerHunterRevealCastClientRpc(bool isCasting, bool isCancelled = false)
    {
        if (IsOwner) return; // O dono local já gerencia diretamente

        animationLogic?.SetHunterRevealCastAnimation(isCasting, isCancelled);
        if (isCasting)
        {
            HunterRevealAreaVisual.Show(transform, hunterRevealRadius);
            GameAudioManager.Instance?.StartHunterRevealCharge(transform);
        }
        else
        {
            HunterRevealAreaVisual.Hide();
            GameAudioManager.Instance?.StopHunterRevealCharge();
        }
    }

    [ServerRpc]
    private void RequestHunterRevealServerRpc(Vector3 center, float radius, float duration)
    {
        if (GameSpawner.Instance != null && (!GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.IsGameOver))
            return;

        List<ulong> revealedIds = new List<ulong>();
        if (ActivePlayers != null)
        {
            for (int i = 0; i < ActivePlayers.Count; i++)
            {
                var p = ActivePlayers[i];
                if (p == null || p.IsHunter || p.IsDead || p.IsGhost) continue;

                float dist = Vector3.Distance(center, p.transform.position);
                if (dist <= radius)
                {
                    revealedIds.Add(p.OwnerClientId);
                }
            }
        }

        TriggerHunterRevealOutcomeClientRpc(revealedIds.ToArray(), duration);
    }

    [ClientRpc]
    private void TriggerHunterRevealOutcomeClientRpc(ulong[] revealedClientIds, float duration)
    {
        if (!IsOwner)
        {
            GameAudioManager.Instance?.StopHunterRevealCharge();
            HunterRevealAreaVisual.TriggerPulseAndHide(transform.position, hunterRevealRadius);
            GameAudioManager.Instance?.PlayHunterRevealRelease(transform.position);
        }

        bool isLocalHunterPlayer = IsLocalPlayerHunter();

        if (isLocalHunterPlayer)
        {
            if (revealedClientIds != null && revealedClientIds.Length > 0)
            {
                GameAudioManager.Instance?.PlayHunterRevealSurvivorPing(transform.position);
                GameHUDController.Instance?.ShowScreenNotice($"<color=#B026FF>{revealedClientIds.Length} SOBREVIVENTE(S) REVELADO(S)!</color>");

                var taskIndicator = LocalPlayer != null ? LocalPlayer.GetComponent<PlayerTaskIndicatorController>() : null;
                for (int i = 0; i < revealedClientIds.Length; i++)
                {
                    var survivor = GetPlayerByClientId(revealedClientIds[i]);
                    if (survivor != null)
                    {
                        survivor.SetTemporaryHunterReveal(duration);
                        taskIndicator?.TriggerHunterMarkedPlayerAlert(survivor, duration);
                    }
                }
            }
            else
            {
                GameHUDController.Instance?.ShowScreenNotice("<color=#CCCCCC>Nenhum sobrevivente no alcance.</color>");
            }
        }

        if (revealedClientIds != null && LocalPlayer != null)
        {
            ulong myId = LocalPlayer.OwnerClientId;
            for (int i = 0; i < revealedClientIds.Length; i++)
            {
                if (revealedClientIds[i] == myId)
                {
                    LocalPlayer.SetTemporaryHunterReveal(duration);
                    GameAudioManager.Instance?.PlayMarkedForDeath(LocalPlayer.transform.position);
                    GameHUDController.Instance?.ShowScreenNotice("<color=#FF4444>[ALERTA] VOCÊ FOI REVELADO PELA MORTE!</color>");
                    break;
                }
            }
        }
    }

    public void SetTemporaryHunterReveal(float duration)
    {
        if (temporaryRevealRoutine != null)
        {
            StopCoroutine(temporaryRevealRoutine);
        }
        temporaryRevealRoutine = StartCoroutine(TemporaryHunterRevealRoutine(duration));
    }

    private IEnumerator TemporaryHunterRevealRoutine(float duration)
    {
        isTemporarilyRevealed = true;
        ApplyMarkedForDeathVisuals(true);

        yield return new WaitForSeconds(duration);

        isTemporarilyRevealed = false;
        temporaryRevealRoutine = null;
        if (!isMarkedForDeath)
        {
            ApplyMarkedForDeathVisuals(false);
        }
    }

    #endregion

    [ServerRpc(RequireOwnership = false)]
    private void RequestKillPlayerFromFireballServerRpc(ulong targetNetworkId, ServerRpcParams rpcParams = default)
    {
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out var netObj))
            {
                var targetPlayer = netObj.GetComponent<PlayerController>();
                if (targetPlayer != null && !targetPlayer.IsHunter && !targetPlayer.IsDead && !targetPlayer.IsGhost)
                {
                    PlayerController senderKiller = this;
                    if (NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client) && client.PlayerObject != null)
                    {
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null) senderKiller = pc;
                    }
                    targetPlayer.KillByHunter(senderKiller, "fireball");
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestKillNpcFromFireballServerRpc(ulong targetNetworkId, ServerRpcParams rpcParams = default)
    {
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out var netObj))
            {
                var targetNpc = netObj.GetComponent<NPCController>();
                if (targetNpc != null && !targetNpc.IsDead)
                {
                    PlayerController senderHunter = this;
                    if (NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client) && client.PlayerObject != null)
                    {
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null) senderHunter = pc;
                    }
                    targetNpc.KillByHunter(senderHunter);
                }
            }
        }
    }

    private Transform GetHunterRightHand()
    {
        if (hunterRightHandBone != null) return hunterRightHandBone;

        Transform visualsT = transform.Find("Visuals");
        if (visualsT != null)
        {
            foreach (var t in visualsT.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "RightHand" || t.name.ToLower().Contains("righthand") || t.name.ToLower().Contains("hand.r"))
                {
                    hunterRightHandBone = t;
                    return hunterRightHandBone;
                }
            }
        }
        return transform;
    }

    private void PerformHunterAttack()
    {
        if (isAttacking) return;
        if (hunterKnockdownLogic != null && hunterKnockdownLogic.IsKnockedDown) return;
        if (Time.time - lastHunterAttackTime < hunterAttackCooldown) return;
        lastHunterAttackTime = Time.time;

        // Alinha o personagem na direção em que a câmera do jogador está mirando
        if (cameraLogic != null)
        {
            Vector3 lookDir = cameraLogic.GetForwardDirection();
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }

        // Dispara o visual da foice (surge nas mãos acompanhando o corte e desaparece ao final de 1.10s)
        if (hunterWeaponVisuals != null)
        {
            hunterWeaponVisuals.PlayAttackVisuals(1.10f);
        }

        // Dispara a animação de ataque da foice (Harvesting)
        animationLogic.TriggerAttackAnimation();

        // Replica o visual da foice para os outros clientes na rede
        // (o ClientNetworkAnimator já sincroniza o trigger "Attack" nativamente na rede)
        if (IsSpawned)
        {
            TriggerHunterAttackVisualServerRpc();
        }

        if (attackRoutine != null) StopCoroutine(attackRoutine);
        attackRoutine = StartCoroutine(HunterAttackRoutine());

        Debug.Log("[PlayerController] A Morte desferiu o golpe de Foice (Harvesting)!");
    }

    [ServerRpc]
    private void TriggerHunterAttackVisualServerRpc()
    {
        TriggerHunterAttackVisualClientRpc();
    }

    [ClientRpc]
    private void TriggerHunterAttackVisualClientRpc()
    {
        if (IsOwner) return; // O atacante já executou localmente

        if (hunterWeaponVisuals != null)
        {
            hunterWeaponVisuals.PlayAttackVisuals(1.10f);
        }
        GameAudioManager.Instance?.PlayHunterScytheSwing(transform.position);
        // Nota: animationLogic.TriggerAttackAnimation() foi removido daqui pois o ClientNetworkAnimator
        // já sincroniza o trigger "Attack" na rede nativamente. Duplicar causava o bug do ataque repetir sem foice.
    }

    private IEnumerator HunterAttackRoutine()
    {
        isAttacking = true;
        try
        {
            // Sincronizado com o momento exato da descida cortante da foice e corte do Slash (0.20s)
            yield return new WaitForSeconds(0.20f);
            GameAudioManager.Instance?.PlayHunterScytheSwing(transform.position);

            ExecuteHunterSlashDamage();

            // O golpe e o arco da foice completam a trajetória com peso e lentidão (0.90s adicionais, totalizando 1.10s)
            yield return new WaitForSeconds(0.90f);
        }
        finally
        {
            // O finally garante que isAttacking SEMPRE volte para false, mesmo diante de exceções
            isAttacking = false;
            attackRoutine = null;
            if (animationLogic != null)
            {
                animationLogic.ResetAttackTrigger();
            }
        }
    }

    /// <summary>
    /// Executa o cálculo de alcance e ângulo frontal do golpe cortante da foice da Morte.
    /// Atinge tanto jogadores sobreviventes quanto pedestres (NPCs).
    /// </summary>
    private void ExecuteHunterSlashDamage()
    {
        try
        {
            float slashRange = hunterAttackRange;
            float slashAngle = hunterAttackAngle;

            HashSet<ulong> targetedPlayers = new HashSet<ulong>();
            HashSet<ulong> targetedNpcs = new HashSet<ulong>();

            // 1. Detecta Players Sobreviventes vivos no alcance do golpe (usa ToArray para evitar coleção modificada)
            PlayerController[] playersSnapshot = ActivePlayers.ToArray();
            foreach (var player in playersSnapshot)
            {
                if (player == null || player == this || player.IsHunter || player.IsDead || player.IsGhost) continue;

                Vector3 toTarget = player.transform.position - transform.position;
                if (Mathf.Abs(toTarget.y) > 2.2f) continue;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                float angle = Vector3.Angle(transform.forward, toTarget);

                if (dist <= slashRange && angle <= slashAngle)
                {
                    if (targetedPlayers.Add(player.NetworkObjectId))
                    {
                        Debug.Log($"[PlayerController] Foice da Morte atingiu Player {player.NetworkObjectId}!");
                        Vector3 hitPos = player.transform.position + Vector3.up * 1.0f;
                        Vector3 hitDir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
                        TriggerScytheHitEffect(hitPos, hitDir, isRealPlayer: true);

                        if (IsServer)
                        {
                            player.KillByHunter(this, "scythe");
                        }
                        else
                        {
                            RequestKillPlayerServerRpc(player.NetworkObjectId);
                        }
                    }
                }
            }

            // 2. Detecta NPCs vivos no alcance do golpe (usa ToArray para evitar InvalidOperationException ao remover da lista)
            NPCController[] npcsSnapshot = NPCController.ActiveNpcs.ToArray();
            foreach (var npc in npcsSnapshot)
            {
                if (npc == null || npc.IsDead) continue;

                Vector3 toTarget = npc.transform.position - transform.position;
                if (Mathf.Abs(toTarget.y) > 2.2f) continue;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                float angle = Vector3.Angle(transform.forward, toTarget);

                if (dist <= slashRange && angle <= slashAngle)
                {
                    if (targetedNpcs.Add(npc.NetworkObjectId))
                    {
                        Debug.Log($"[PlayerController] Foice da Morte atingiu NPC {npc.NetworkObjectId}!");
                        Vector3 hitPos = npc.transform.position + Vector3.up * 1.0f;
                        Vector3 hitDir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
                        TriggerScytheHitEffect(hitPos, hitDir, isRealPlayer: false);

                        if (IsServer)
                        {
                            npc.KillByHunter(this);
                        }
                        else
                        {
                            RequestKillNpcServerRpc(npc.NetworkObjectId);
                        }
                    }
                }
            }

            // 3. Fallback via Physics.OverlapSphere para garantir acerto em colisores na cena
            Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward * 1.5f + Vector3.up * 1.0f, 1.8f);
            foreach (var hit in hits)
            {
                if (hit == null) continue;

                var p = hit.GetComponentInParent<PlayerController>();
                if (p != null && p != this && !p.IsHunter && !p.IsDead && !p.IsGhost)
                {
                    if (targetedPlayers.Add(p.NetworkObjectId))
                    {
                        Vector3 hitPos = p.transform.position + Vector3.up * 1.0f;
                        Vector3 hitDir = (p.transform.position - transform.position).normalized;
                        if (hitDir.sqrMagnitude < 0.001f) hitDir = transform.forward;
                        TriggerScytheHitEffect(hitPos, hitDir, isRealPlayer: true);

                        if (IsServer) p.KillByHunter(this, "scythe");
                        else RequestKillPlayerServerRpc(p.NetworkObjectId);
                    }
                }

                var n = hit.GetComponentInParent<NPCController>();
                if (n != null && !n.IsDead)
                {
                    if (targetedNpcs.Add(n.NetworkObjectId))
                    {
                        Vector3 hitPos = n.transform.position + Vector3.up * 1.0f;
                        Vector3 hitDir = (n.transform.position - transform.position).normalized;
                        if (hitDir.sqrMagnitude < 0.001f) hitDir = transform.forward;
                        TriggerScytheHitEffect(hitPos, hitDir, isRealPlayer: false);

                        if (IsServer) n.KillByHunter(this);
                        else RequestKillNpcServerRpc(n.NetworkObjectId);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PlayerController] Erro ao executar dano da foice da Morte: {ex}");
        }
    }

    /// <summary>
    /// Spawna o efeito de impacto (roxo para Player real, fogo para NPC) na posição do golpe da foice e replica pela rede.
    /// </summary>
    public void TriggerScytheHitEffect(Vector3 hitPos, Vector3 hitDir, bool isRealPlayer)
    {
        SpawnScytheHitEffect(hitPos, hitDir, isRealPlayer);

        if (IsSpawned)
        {
            TriggerScytheHitEffectServerRpc(hitPos, hitDir, isRealPlayer);
        }
    }

    [ServerRpc]
    private void TriggerScytheHitEffectServerRpc(Vector3 hitPos, Vector3 hitDir, bool isRealPlayer)
    {
        TriggerScytheHitEffectClientRpc(hitPos, hitDir, isRealPlayer);
    }

    [ClientRpc]
    private void TriggerScytheHitEffectClientRpc(Vector3 hitPos, Vector3 hitDir, bool isRealPlayer)
    {
        if (IsOwner) return; // O atacante já instanciou localmente com latência zero
        SpawnScytheHitEffect(hitPos, hitDir, isRealPlayer);
    }

    public void SpawnScytheHitEffect(Vector3 hitPos, Vector3 hitDir, bool isRealPlayer)
    {
        GameAudioManager.Instance?.PlayHunterScytheHit(hitPos, isRealPlayer);
        GameObject effectPrefab = null;

        if (isRealPlayer)
        {
            effectPrefab = scytheHitEffectPrefab != null ? scytheHitEffectPrefab : fireballExplosionPrefab;
            if (effectPrefab == null)
            {
#if UNITY_EDITOR
                effectPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Eric VFX Studio/Free Game VFX/Prefab/FX_Purple_Hit_02.prefab");
#endif
            }
        }
        else
        {
            effectPrefab = fireHitEffectPrefab;
            if (effectPrefab == null)
            {
#if UNITY_EDITOR
                effectPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Fire_Hit_02.prefab");
#endif
                if (effectPrefab == null)
                {
                    effectPrefab = Resources.Load<GameObject>("FX_Fire_Hit_02");
                }
                if (effectPrefab == null)
                {
                    effectPrefab = scytheHitEffectPrefab != null ? scytheHitEffectPrefab : fireballExplosionPrefab;
                }
            }
        }

        if (effectPrefab != null)
        {
            Quaternion rot = hitDir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(hitDir) : Quaternion.identity;
            GameObject fx = Instantiate(effectPrefab, hitPos, rot);
            if (Application.isPlaying)
            {
                Destroy(fx, 2.0f);
            }
            else
            {
                DestroyImmediate(fx);
            }
        }
    }

    /// <summary>
    /// Spawna a sequência de surgimento dos sobreviventes:
    /// - Spawna o efeito de partículas roxas (FX_Player_Spawn) no peito do jogador.
    /// - Reseta e garante a escala (1,1,1) e posição local (0,0,0) sem alterar ou corromper a malha.
    /// - O efeito visual é invisível para a Morte (Hunter) para que ela não identifique os players lá de cima.
    /// - Não atrasa o jogador: controle instantâneo desde o primeiro frame.
    /// </summary>
    public void PlayPlayerSpawnSequence()
    {
        if (isHunter) return;

        ResetVisualsTransform();

        // O efeito visual de spawn NUNCA é criado na visão da Morte (Hunter)
        if (IsLocalPlayerHunter())
        {
            return;
        }

        SpawnPlayerSpawnEffect();
    }

    /// <summary>
    /// Verifica se o jogador local desta tela/máquina é a Morte (Hunter).
    /// </summary>
    public static bool IsLocalPlayerHunter()
    {
        if (LocalInstance != null && LocalInstance.IsHunter)
        {
            return true;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            var localPlayerObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (localPlayerObj != null)
            {
                var pc = localPlayerObj.GetComponent<PlayerController>();
                if (pc != null && pc.IsHunter)
                {
                    return true;
                }
            }
        }

        if (GameHUDController.Instance != null && GameHUDController.Instance.IsLocalHunter)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Garante que todos os objetos visuais do personagem (Visuals e filhos) estejam na posição (0,0,0)
    /// e escala (1,1,1), prevenindo qualquer bug de malha invisível, distorcida ou enterrada.
    /// </summary>
    public void ResetVisualsTransform()
    {
        Transform visuals = transform.Find("Visuals");
        if (visuals != null)
        {
            visuals.localScale = Vector3.one;
            visuals.localPosition = Vector3.zero;
            visuals.localRotation = Quaternion.identity;

            for (int i = 0; i < visuals.childCount; i++)
            {
                Transform child = visuals.GetChild(i);
                if (child != null && !child.name.StartsWith("GhostVisual"))
                {
                    child.localScale = Vector3.one;
                    child.localPosition = Vector3.zero;
                }
            }
        }

        var rand = GetComponent<PlayerCharacterRandomizer>();
        if (rand != null && rand.characters != null)
        {
            for (int i = 0; i < rand.characters.Length; i++)
            {
                if (rand.characters[i] != null)
                {
                    rand.characters[i].transform.localScale = Vector3.one;
                    rand.characters[i].transform.localPosition = Vector3.zero;
                }
            }

            var activeChar = rand.GetActiveCharacterObject();
            if (activeChar != null && !IsGhost && !IsDead)
            {
                activeChar.SetActive(true);
                activeChar.transform.localScale = Vector3.one;
                activeChar.transform.localPosition = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// Retorna a posição mundial exata do peito/coração do personagem (coincidindo com o Soul Orb).
    /// </summary>
    public Vector3 GetChestPosition(out Transform parentBone)
    {
        parentBone = null;
        var highlight = GetComponent<SurvivorHighlightVisuals>();

        var rand = GetComponent<PlayerCharacterRandomizer>();
        if (rand != null)
        {
            var activeChar = rand.GetActiveCharacterObject();
            if (activeChar != null)
            {
                var anim = activeChar.GetComponent<Animator>();
                if (anim != null && anim.isHuman)
                {
                    parentBone = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.UpperChest);
                }
            }
        }

        if (highlight != null)
        {
            return highlight.GetChestWorldPosition();
        }

        if (parentBone != null)
        {
            return parentBone.TransformPoint(new Vector3(0f, 0.10f, 0f));
        }

        return transform.position + Vector3.up * 1.19f;
    }

    /// <summary>
    /// Spawna o efeito visual de surgimento roxo dos sobreviventes na mesma altura e posição do peito.
    /// Inacessível e invisível para a Morte.
    /// </summary>
    public void SpawnPlayerSpawnEffect()
    {
        if (IsLocalPlayerHunter())
        {
            return;
        }

        GameObject effectPrefab = playerSpawnVfxPrefab;
        if (effectPrefab == null)
        {
            effectPrefab = Resources.Load<GameObject>("FX_Player_Spawn");
#if UNITY_EDITOR
            if (effectPrefab == null)
            {
                effectPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Player_Spawn.prefab");
            }
#endif
        }

        if (effectPrefab != null)
        {
            Vector3 spawnPos = GetChestPosition(out Transform parentBone);
            GameObject fx = Instantiate(effectPrefab, spawnPos, Quaternion.identity);
            fx.AddComponent<PlayerSpawnEffectVisibility>();

            // Acompanha o osso do peito do sobrevivente se ele começar a andar imediatamente
            if (parentBone != null)
            {
                fx.transform.SetParent(parentBone, true);
            }
            else
            {
                fx.transform.SetParent(transform, true);
            }

            // Garante que todos os sistemas de partículas toquem
            var psList = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < psList.Length; i++)
            {
                psList[i].Clear();
                psList[i].Play(true);
            }

            if (Application.isPlaying)
            {
                Destroy(fx, 2.5f);
            }
            else
            {
                DestroyImmediate(fx);
            }
        }
    }

    [ServerRpc]
    private void RequestKillPlayerServerRpc(ulong targetNetworkId, ServerRpcParams rpcParams = default)
    {
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out var netObj))
            {
                var targetPlayer = netObj.GetComponent<PlayerController>();
                if (targetPlayer != null && !targetPlayer.IsHunter && !targetPlayer.IsDead && !targetPlayer.IsGhost)
                {
                    if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= 5.5f)
                    {
                        PlayerController senderKiller = this;
                        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client) && client.PlayerObject != null)
                        {
                            var pc = client.PlayerObject.GetComponent<PlayerController>();
                            if (pc != null) senderKiller = pc;
                        }
                        targetPlayer.KillByHunter(senderKiller, "scythe");
                    }
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestKillNpcServerRpc(ulong targetNetworkId, ServerRpcParams rpcParams = default)
    {
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out var netObj))
            {
                var targetNpc = netObj.GetComponent<NPCController>();
                if (targetNpc != null && !targetNpc.IsDead)
                {
                    PlayerController senderHunter = this;
                    if (NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client) && client.PlayerObject != null)
                    {
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null) senderHunter = pc;
                    }
                    targetNpc.KillByHunter(senderHunter);
                }
            }
        }
    }

    /// <summary>
    /// Chamado pelo Servidor quando este jogador é eliminado pelo golpe de foice ou bola de fogo da Morte.
    /// </summary>
    public void KillByHunter(PlayerController killer = null, string weaponType = "scythe")
    {
        if (!IsServer || isDead.Value || isGhost.Value) return;
        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver) return;

        isDead.Value = true;
        Debug.Log($"[PlayerController] Player {NetworkObjectId} foi morto pela Morte!");

        // Notifica o Kill Feed no canto superior direito para todos os jogadores
        string victimName = !string.IsNullOrWhiteSpace(Nickname) ? Nickname : $"Player {OwnerClientId + 1}";
        string killerName = killer != null && !string.IsNullOrWhiteSpace(killer.Nickname) 
            ? killer.Nickname 
            : "A Morte";
        GameSpawner.Instance?.BroadcastElimination(killerName, victimName, isHunterKill: true, weaponType: weaponType);

        GameSpawner.Instance?.CheckAllSurvivorsEliminated();
        GameSpawner.Instance?.RebalanceNpcModelsForLivingSurvivors();

        // Fixa a posição firme no solo para não morrer no ar
        SnapToGround();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        PlayPlayerDeathClientRpc();
        StartCoroutine(ServerPlayerDeathRoutine());
    }

    /// <summary>
    /// Chamado pelo Servidor quando este Hunter comete 3 erros (elimina 3 NPCs inocentes).
    /// O Hunter perde todas as vidas, é eliminado e morre no jogo.
    /// </summary>
    public void KillHunterFromMistakes()
    {
        if (!IsServer || isDead.Value) return;

        isDead.Value = true;
        string hName = !string.IsNullOrWhiteSpace(Nickname) ? Nickname : "A Morte";
        Debug.LogWarning($"[PlayerController] Hunter {hName} (ID {NetworkObjectId}) cometeu 3 erros de NPC e foi ELIMINADO!");

        // Transmite no Kill Feed que o Hunter foi eliminado por 3 erros
        GameSpawner.Instance?.BroadcastElimination("3 Erros de NPC", hName, isHunterKill: false, weaponType: "skull");

        SnapToGround();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        PlayPlayerDeathClientRpc();
        StartCoroutine(ServerPlayerDeathRoutine());

        // Notifica o GameSpawner para verificar fim de jogo (vitória dos sobreviventes)
        GameSpawner.Instance?.CheckHunterLivesEliminated();
    }

    [ClientRpc]
    private void PlayPlayerDeathClientRpc()
    {
        StartCoroutine(LocalDeathSequenceRoutine());
    }

    private IEnumerator LocalDeathSequenceRoutine()
    {
        isDying = true;
        GameAudioManager.Instance?.PlayPlayerDeath(transform.position);

        if (greetingRoutine != null)
        {
            StopCoroutine(greetingRoutine);
            greetingRoutine = null;
        }
        if (danceRoutine != null)
        {
            StopCoroutine(danceRoutine);
            danceRoutine = null;
        }
        isGreeting = false;
        isDancing = false;

        if (IsOwner)
        {
            currentAssignedTaskIndex = -1;
            hasAssignedTask = false;
            TaskObject.ClearAllLocalTargets();
            if (taskIndicatorController != null)
            {
                taskIndicatorController.ClearTarget();
                taskIndicatorController.SetCanvasVisible(false);
            }
            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.SetTargetTaskObjective("");
                GameHUDController.Instance.SetTaskPrompt(false, "", false);
                GameHUDController.Instance.HideSurvivorRevelationTimer();
            }
        }

        if (movementLogic != null) movementLogic.ResetVerticalVelocity();

        // Fixa o personagem firme no solo evitando flutuação
        SnapToGround();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        // Dispara a animação Falling Back Death via PlayerAnimationLogic
        if (animationLogic != null)
        {
            animationLogic.TriggerDeathAnimation();
        }

        // Aguarda a duração total da animação de morte (2.2s)
        yield return new WaitForSeconds(2.2f);

        isDying = false;

        // Transforma no modelo de fantasma semitransparente LittleGhost_LP (1)
        UpdateGhostVisuals(true);
    }

    /// <summary>
    /// Dispara raycast para baixo ignorando colisores de players e NPCs para encontrar o piso exato da cena.
    /// </summary>
    private void SnapToGround()
    {
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (hit.collider.GetComponentInParent<NPCController>() != null) continue;

            if (characterController != null) characterController.enabled = false;
            transform.position = hit.point;
            break;
        }
    }

    private IEnumerator ServerPlayerDeathRoutine()
    {
        yield return new WaitForSeconds(2.2f);
        int totalSkins = (ghostPrefabs != null && ghostPrefabs.Length > 0) ? ghostPrefabs.Length : 7;
        ghostSkinIndex.Value = Random.Range(0, totalSkins);
        isGhost.Value = true;
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.UnassignTargetTaskForPlayer(OwnerClientId);
        }
        GameSpawner.Instance?.CheckAllSurvivorsEliminated();
    }

    private void OnGhostStateChanged(bool oldVal, bool newVal)
    {
        UpdateGhostVisuals(newVal);
        if (newVal)
        {
            currentAssignedTaskIndex = -1;
            hasAssignedTask = false;
            TaskObject.ClearAllLocalTargets();
            if (taskIndicatorController != null)
            {
                taskIndicatorController.ClearTarget();
                taskIndicatorController.SetCanvasVisible(false);
            }
            if (IsOwner && GameHUDController.Instance != null)
            {
                GameHUDController.Instance.SetTargetTaskObjective("");
                GameHUDController.Instance.SetTaskPrompt(false, "", false);
                GameHUDController.Instance.HideSurvivorRevelationTimer();
                GameHUDController.Instance.SetSpectatorMode(true);
            }
        }
        else
        {
            if (IsOwner && GameHUDController.Instance != null)
            {
                GameHUDController.Instance.SetSpectatorMode(false);
            }
        }
    }

    private void OnGhostSkinChanged(int oldVal, int newVal)
    {
        if (isGhost.Value)
        {
            UpdateGhostVisuals(true);
        }
    }

    /// <summary>
    /// Garante que o modelo de fantasma LittleGhost_LP esteja instanciado como filho de Visuals e com shader Toon aplicado.
    /// Sorteia ou utiliza a skin sincronizada via rede (ghostSkinIndex).
    /// </summary>
    public void EnsureGhostVisual()
    {
        var visualsT = transform.Find("Visuals");
        if (visualsT == null) return;

        // Se a lista de prefabs não estiver populada, busca os 7 prefabs
        if (ghostPrefabs == null || ghostPrefabs.Length == 0)
        {
#if UNITY_EDITOR
            var list = new System.Collections.Generic.List<GameObject>();
            for (int i = 1; i <= 7; i++)
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Little_GhostLP(FREE)/Prefabs/LittleGhost_LP ({i}).prefab");
                if (p != null) list.Add(p);
            }
            if (list.Count > 0) ghostPrefabs = list.ToArray();
#endif
        }

        int targetSkin = ghostSkinIndex.Value;
        if (targetSkin < 0)
        {
            int count = (ghostPrefabs != null && ghostPrefabs.Length > 0) ? ghostPrefabs.Length : 7;
            targetSkin = (int)(OwnerClientId % (ulong)count);
        }

        GameObject selectedTemplate = null;
        if (ghostPrefabs != null && targetSkin >= 0 && targetSkin < ghostPrefabs.Length)
        {
            selectedTemplate = ghostPrefabs[targetSkin];
        }
#if UNITY_EDITOR
        if (selectedTemplate == null)
        {
            int prefabNum = Mathf.Clamp(targetSkin + 1, 1, 7);
            selectedTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Little_GhostLP(FREE)/Prefabs/LittleGhost_LP ({prefabNum}).prefab");
        }
#endif
        if (selectedTemplate == null) selectedTemplate = ghostPrefab;

        string targetGhostName = selectedTemplate != null ? $"GhostVisual_{selectedTemplate.name}" : "GhostVisual";

        // Se o fantasma atual for de outra skin, remove para instanciar a nova skin correta
        if (ghostVisual != null && ghostVisual.name != targetGhostName)
        {
            if (Application.isPlaying) Destroy(ghostVisual); else DestroyImmediate(ghostVisual);
            ghostVisual = null;
        }

        if (ghostVisual == null)
        {
            var existingGhost = visualsT.Find(targetGhostName);
            if (existingGhost != null)
            {
                ghostVisual = existingGhost.gameObject;
            }
            else
            {
                // Remove qualquer GhostVisual legado ou de outra skin
                for (int i = visualsT.childCount - 1; i >= 0; i--)
                {
                    var child = visualsT.GetChild(i);
                    if (child.name.StartsWith("GhostVisual"))
                    {
                        if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
                    }
                }

                if (selectedTemplate != null)
                {
                    GameObject ghostObj = Instantiate(selectedTemplate, visualsT);
                    ghostObj.name = targetGhostName;

                    // Remove preventivamente colisores e física para não colidir com o jogador nem com o cenário
                    foreach (var col in ghostObj.GetComponentsInChildren<Collider>(true))
                    {
                        if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
                    }
                    foreach (var rb in ghostObj.GetComponentsInChildren<Rigidbody>(true))
                    {
                        if (Application.isPlaying) Destroy(rb); else DestroyImmediate(rb);
                    }

                    ghostObj.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                    ghostObj.transform.localRotation = Quaternion.identity;
                    ghostObj.transform.localScale = new Vector3(1.25f, 1.25f, 1.25f);
                    ghostObj.SetActive(false);

                    ghostVisual = ghostObj;
                }
            }
        }

        if (ghostVisual != null)
        {
            ApplyGhostToonMaterial(ghostVisual);
        }
    }

    /// <summary>
    /// Aplica o material e shader Toon translúcido (Toon/GhostToonTransparent) ao modelo de fantasma espectador,
    /// preservando a skin, texturas e acessórios característicos de cada variante.
    /// </summary>
    private void ApplyGhostToonMaterial(GameObject ghostObj)
    {
        if (ghostObj == null) return;

        Shader toonShader = Shader.Find("Toon/GhostToonTransparent") 
            ?? Shader.Find("Toon/TGS_CustomToonOutline") 
            ?? Shader.Find("Toon/TGS_CustomToon");

        var renderers = ghostObj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;

            if (toonShader != null && r.sharedMaterial != null && r.sharedMaterial.shader != toonShader)
            {
                r.material.shader = toonShader;
            }

            // Garante a semitransparência de fantasma mantendo a estética Toon e contornos
            if (r.material != null)
            {
                if (r.material.HasProperty("_CharacterAlpha"))
                {
                    r.material.SetFloat("_CharacterAlpha", 0.40f);
                }
            }
        }
    }

    /// <summary>
    /// Alterna a visibilidade e física entre o modelo humanoide normal e o modelo fantasma espectador.
    /// </summary>
    public void UpdateGhostVisuals(bool ghostMode)
    {
        EnsureGhostVisual();

        if (ghostMode)
        {
            if (characterController != null)
            {
                // Ajusta stepOffset antes de reduzir height/radius para evitar "Step Offset must be less or equal to <scaled Height> + <scaled Radius> * 2"
                characterController.enabled = false;
                characterController.detectCollisions = false;
                characterController.stepOffset = 0.05f;
                characterController.radius = 0.05f;
                characterController.height = 0.1f;
                characterController.center = new Vector3(0f, 0.05f, 0f);
                characterController.enabled = true;
            }

            // Oculta todos os modelos humanoides (Character 01 a 10)
            var visualsT = transform.Find("Visuals");
            if (visualsT != null)
            {
                foreach (Transform child in visualsT)
                {
                    if (!child.name.StartsWith("GhostVisual"))
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }

            // Aplica a regra de visibilidade do fantasma (invisível para Hunter e vivos)
            UpdateGhostVisibility();

            if (IsOwner)
            {
                Debug.Log("[PlayerController] Você se transformou em Fantasma (LittleGhost)! Modo espectador ativado. Voo livre com WASD + Espaço (subir) + Ctrl (descer).");
            }
        }
        else
        {
            if (ghostVisual != null)
            {
                ghostVisual.SetActive(false);
            }
        }

        RefreshAllGhostVisibilities();
    }

    public void UpdateGhostVisibility()
    {
        if (ghostVisual == null) return;

        if (!isGhost.Value)
        {
            if (ghostVisual.activeSelf) ghostVisual.SetActive(false);
            return;
        }

        bool shouldShow = false;

        if (IsOwner)
        {
            // O próprio jogador morto deve ver seu fantasma em terceira pessoa
            shouldShow = true;
        }
        else
        {
            var local = LocalInstance;
            if (local != null)
            {
                // Se o cliente local for a Morte ou um player vivo, NÃO pode ver o fantasma!
                // Apenas se o local for espectador (morto ou fantasma) e não for Hunter ele pode ver.
                if (!local.IsHunter && (local.IsDead || local.IsGhost))
                {
                    shouldShow = true;
                }
            }
        }

        if (ghostVisual.activeSelf != shouldShow)
        {
            ghostVisual.SetActive(shouldShow);
        }
    }

    /// <summary>
    /// Atualiza a visibilidade de todos os jogadores fantasmas ativos na cena.
    /// </summary>
    public static void RefreshAllGhostVisibilities()
    {
        if (ActivePlayers == null) return;
        for (int i = 0; i < ActivePlayers.Count; i++)
        {
            var p = ActivePlayers[i];
            if (p != null)
            {
                p.UpdateGhostVisibility();
            }
        }
    }

    private void PerformKick()
    {
        if (!IsOwner) return;
        if (isGreeting || isDancing) return; // Não chuta enquanto estiver no cumprimento social ou dançando
        if (Time.time - lastKickTime < kickCooldown) return;
        lastKickTime = Time.time;

        if (animationLogic == null) animationLogic = GetComponent<PlayerAnimationLogic>();

        // Procura pelo alvo válido mais próximo (a Morte / Hunter) em qualquer ângulo (360 graus)
        HunterKnockdownLogic targetHunter = FindClosestValidKickTarget(out Vector3 dirToTarget, out float targetDist);

        if (targetHunter != null)
        {
            // Se a Morte estiver perto (mesmo que atrás ou em outro ângulo),
            // o personagem vira imediatamente em direção a ela e executa o chute
            if (dirToTarget.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(dirToTarget);
            }

            // Dispara a animação Front Kick no jogador atacante
            if (animationLogic != null) animationLogic.TriggerKickAnimation();
            GameAudioManager.Instance?.PlayPlayerKick(transform.position);

            if (isActiveAndEnabled)
            {
                if (attackRoutine != null) StopCoroutine(attackRoutine);
                attackRoutine = StartCoroutine(KickRoutine());
            }

            // Acerta a Morte lançando-a no ar na direção do chute
            Vector3 kickDir = (dirToTarget + Vector3.up * 0.35f).normalized;
            targetHunter.TakeKick(kickDir);

            if (currentAssignedTaskIndex == TaskManager.HunterKickTaskIndex)
            {
                if (GameSpawner.Instance != null && GameSpawner.Instance.IsMatchActive && !GameSpawner.Instance.IsGameOver)
                {
                    CompleteLocalHunterKickTask();
                }
            }

            Debug.Log($"[PlayerController] Chute certeiro na Morte ({targetHunter.name}) a {targetDist:F2}m! Personagem virou e atingiu o alvo.");
        }
        else
        {
            // Se estiver longe (ou sem a Morte por perto):
            // O chute é executado no ar na direção em que o personagem já está virado.
            // (Tarefas de objetos de cenário são completadas exclusivamente segurando o botão de interação durante o tempo estipulado)
            if (animationLogic != null) animationLogic.TriggerKickAnimation();
            GameAudioManager.Instance?.PlayPlayerKick(transform.position);

            if (isActiveAndEnabled)
            {
                if (attackRoutine != null) StopCoroutine(attackRoutine);
                attackRoutine = StartCoroutine(KickRoutine());
            }

            Debug.Log("[PlayerController] Chute executado sem alvo próximo. Manteve a direção atual do personagem.");
        }
    }

    private IEnumerator KickRoutine()
    {
        isAttacking = true;
        try
        {
            yield return new WaitForSeconds(0.75f);
        }
        finally
        {
            isAttacking = false;
            attackRoutine = null;
        }
    }

    private HunterKnockdownLogic FindClosestValidKickTarget(out Vector3 dirToTarget, out float targetDist)
    {
        dirToTarget = transform.forward;
        targetDist = float.MaxValue;

        HunterKnockdownLogic bestStandingTarget = null;
        float bestStandingDist = kickRange;
        Vector3 bestStandingDir = Vector3.zero;

        HunterKnockdownLogic bestAnyTarget = null;
        float bestAnyDist = kickRange;
        Vector3 bestAnyDir = Vector3.zero;

        // 1. Procura na lista de jogadores ativos (snapshot de rede)
        PlayerController[] playersSnapshot = ActivePlayers.ToArray();
        foreach (var player in playersSnapshot)
        {
            if (player == null || player == this || !player.IsHunter || player.IsDead || player.IsGhost) continue;

            Vector3 diff = player.transform.position - transform.position;
            // Tolerância vertical expandida (permite chutar a Morte quicando no ar ou em desníveis)
            if (Mathf.Abs(diff.y) > 3.8f) continue;

            diff.y = 0f;
            float horizDist = diff.magnitude;

            // Se a Morte estiver em queda/rolamento com colisor dinâmico, afere também o ponto mais próximo do corpo
            var col = player.GetComponent<Collider>();
            if (col != null && col.enabled)
            {
                Vector3 closest = col.ClosestPoint(transform.position + Vector3.up * 0.9f);
                Vector3 colDiff = closest - (transform.position + Vector3.up * 0.9f);
                colDiff.y = 0f;
                if (colDiff.magnitude < horizDist)
                {
                    horizDist = colDiff.magnitude;
                }
            }

            if (horizDist <= kickRange)
            {
                var kd = player.GetComponent<HunterKnockdownLogic>();
                if (kd != null)
                {
                    Vector3 normDir = horizDist > 0.001f ? (diff / horizDist) : transform.forward;

                    if (!kd.IsKnockedDown)
                    {
                        if (horizDist < bestStandingDist)
                        {
                            bestStandingDist = horizDist;
                            bestStandingTarget = kd;
                            bestStandingDir = normDir;
                        }
                    }

                    if (horizDist < bestAnyDist)
                    {
                        bestAnyDist = horizDist;
                        bestAnyTarget = kd;
                        bestAnyDir = normDir;
                    }
                }
            }
        }

        // 2. Busca direta por HunterKnockdownLogic na cena (cobre testes locais, offline ou Hunters instanciados dinamicamente)
        if (bestStandingTarget == null && bestAnyTarget == null)
        {
            HunterKnockdownLogic[] allHunters = Object.FindObjectsByType<HunterKnockdownLogic>(FindObjectsInactive.Exclude);
            foreach (var kd in allHunters)
            {
                if (kd == null || kd.gameObject == gameObject) continue;

                var pc = kd.GetComponent<PlayerController>();
                if (pc != null && (pc.IsDead || pc.IsGhost)) continue;

                Vector3 diff = kd.transform.position - transform.position;
                if (Mathf.Abs(diff.y) > 3.8f) continue;

                diff.y = 0f;
                float horizDist = diff.magnitude;

                var col = kd.GetComponent<Collider>();
                if (col != null && col.enabled)
                {
                    Vector3 closest = col.ClosestPoint(transform.position + Vector3.up * 0.9f);
                    Vector3 colDiff = closest - (transform.position + Vector3.up * 0.9f);
                    colDiff.y = 0f;
                    if (colDiff.magnitude < horizDist)
                    {
                        horizDist = colDiff.magnitude;
                    }
                }

                if (horizDist <= kickRange)
                {
                    Vector3 normDir = horizDist > 0.001f ? (diff / horizDist) : transform.forward;

                    if (!kd.IsKnockedDown)
                    {
                        if (horizDist < bestStandingDist)
                        {
                            bestStandingDist = horizDist;
                            bestStandingTarget = kd;
                            bestStandingDir = normDir;
                        }
                    }

                    if (horizDist < bestAnyDist)
                    {
                        bestAnyDist = horizDist;
                        bestAnyTarget = kd;
                        bestAnyDir = normDir;
                    }
                }
            }
        }

        // 3. Fallback via Physics.OverlapSphere para garantir detecção em colisores da cena
        if (bestStandingTarget == null && bestAnyTarget == null)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, kickRange, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                var kd = hit.GetComponentInParent<HunterKnockdownLogic>();
                if (kd != null && kd.gameObject != gameObject)
                {
                    Vector3 diff = kd.transform.position - transform.position;
                    if (Mathf.Abs(diff.y) > 3.8f) continue;

                    diff.y = 0f;
                    float horizDist = diff.magnitude;

                    var col = hit;
                    if (col != null && col.enabled)
                    {
                        Vector3 closest = col.ClosestPoint(transform.position + Vector3.up * 0.9f);
                        Vector3 colDiff = closest - (transform.position + Vector3.up * 0.9f);
                        colDiff.y = 0f;
                        if (colDiff.magnitude < horizDist)
                        {
                            horizDist = colDiff.magnitude;
                        }
                    }

                    if (horizDist <= kickRange)
                    {
                        Vector3 normDir = horizDist > 0.001f ? (diff / horizDist) : transform.forward;

                        if (!kd.IsKnockedDown)
                        {
                            if (horizDist < bestStandingDist)
                            {
                                bestStandingDist = horizDist;
                                bestStandingTarget = kd;
                                bestStandingDir = normDir;
                            }
                        }

                        if (horizDist < bestAnyDist)
                        {
                            bestAnyDist = horizDist;
                            bestAnyTarget = kd;
                            bestAnyDir = normDir;
                        }
                    }
                }
            }
        }

        // Prioriza o Hunter em pé se houver; caso contrário, qualquer Hunter no alcance
        if (bestStandingTarget != null)
        {
            dirToTarget = bestStandingDir;
            targetDist = bestStandingDist;
            return bestStandingTarget;
        }

        if (bestAnyTarget != null)
        {
            dirToTarget = bestAnyDir;
            targetDist = bestAnyDist;
            return bestAnyTarget;
        }

        return null;
    }

    private Coroutine greetingRoutine;
    private bool isGreeting = false;

    private Coroutine danceRoutine;
    private bool isDancing = false;

    private IEnumerator GreetingInteractionRoutine(NPCController targetNpc)
    {
        isGreeting = true;

        try
        {
            // 1. Calcula direção para encarar o NPC
            Vector3 dirToNpc = (targetNpc != null) ? (targetNpc.transform.position - transform.position) : transform.forward;
            dirToNpc.y = 0f;
            Quaternion targetRot = (dirToNpc.sqrMagnitude > 0.001f)
                ? Quaternion.LookRotation(dirToNpc.normalized)
                : transform.rotation;

            // 2. Dispara animação de aceno na rede
            if (animationLogic != null)
            {
                animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
                animationLogic.TriggerGreetingAnimation();
            }

            if (IsSpawned)
            {
                TriggerGreetingVisualServerRpc();
            }

            // 3. Notifica o servidor para o NPC responder ao cumprimento
            ulong targetNpcId = targetNpc != null ? targetNpc.NetworkObjectId : ulong.MaxValue;
            if (IsSpawned)
            {
                NotifyGreetingServerRpc(targetNpcId);
            }
            else if (targetNpc != null)
            {
                targetNpc.PerformGreetingResponse(transform.position);
            }

            // 4. Rotaciona suavemente para encarar o NPC (mesma velocidade angular dos NPCs: 8.0f)
            float rotateTime = 0.35f;
            float rotElapsed = 0f;
            float rotSpeed = 8.0f;

            while (rotElapsed < rotateTime)
            {
                if (isDying || isGhost.Value) yield break;
                if (targetNpc != null && targetNpc.IsDead) break;

                if (targetNpc != null)
                {
                    Vector3 currentDir = targetNpc.transform.position - transform.position;
                    currentDir.y = 0f;
                    if (currentDir.sqrMagnitude > 0.001f)
                    {
                        targetRot = Quaternion.LookRotation(currentDir.normalized);
                    }
                }

                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotSpeed * Time.deltaTime);

                rotElapsed += Time.deltaTime;
                yield return null;
            }

            transform.rotation = targetRot;

            // 5. Mantém o jogador travado até o término da interação/animação de aceno (~3.5 segundos)
            float remainingDuration = 3.2f;
            float elapsed = 0f;
            while (elapsed < remainingDuration)
            {
                if (isDying || isGhost.Value) yield break;
                if (targetNpc != null && targetNpc.IsDead) break;

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            isGreeting = false;
            greetingRoutine = null;
        }
    }

    public void OnGreeting(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (isDying || isGhost.Value) return;
        if (isHunter) return; // Regra: Hunters não acenam nem dançam
        if (isGreeting || isDancing) return; // Bloqueado: Já está executando outra ação social/emote

        if (context.started)
        {
            // 1. Procura NPC próximo DENTRO DO RAIO DE 3M (NPCController.SocialInteractionDistance)
            NPCController targetNpc = NPCController.FindBestGreetingTarget(transform.position, transform.forward, NPCController.SocialInteractionDistance);

            // REGRA: Se estiver fora do raio de 3m (nenhum NPC próximo), NÃO ativa nada!
            // O jogador continua andando ou parado normalmente onde estiver.
            if (targetNpc == null)
            {
                return;
            }

            // 2. Se estiver dentro do raio, ativa o high five e bloqueia o movimento até terminar a interação
            if (greetingRoutine != null)
            {
                StopCoroutine(greetingRoutine);
            }
            greetingRoutine = StartCoroutine(GreetingInteractionRoutine(targetNpc));
        }
    }

    [ServerRpc]
    private void NotifyGreetingServerRpc(ulong targetNpcNetworkId)
    {
        if (isHunter) return;
        NPCController.RespondToPlayerGreeting(transform.position, transform.forward, targetNpcNetworkId);
    }

    private IEnumerator DanceRoutine(int danceType)
    {
        isDancing = true;

        try
        {
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
                TriggerDanceVisualServerRpc(danceType);
            }

            // Bloqueia o movimento durante exatamente 4.0 segundos
            float duration = 4.0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (isDying || isGhost.Value) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            isDancing = false;
            danceRoutine = null;
            if (animationLogic != null)
            {
                animationLogic.StopDanceAnimation();
            }
            if (IsSpawned)
            {
                TriggerStopDanceVisualServerRpc();
            }
        }
    }

    [ServerRpc]
    private void TriggerGreetingVisualServerRpc()
    {
        TriggerGreetingVisualClientRpc();
    }

    [ClientRpc]
    private void TriggerGreetingVisualClientRpc()
    {
        if (IsOwner) return; // O jogador local já disparou a animação
        if (animationLogic != null)
        {
            animationLogic.HandleAnimation(Vector2.zero, isRunning: false, isGrounded: true);
            animationLogic.TriggerGreetingAnimation();
        }
    }

    [ServerRpc]
    private void TriggerDanceVisualServerRpc(int danceType)
    {
        TriggerDanceVisualClientRpc(danceType);
    }

    [ClientRpc]
    private void TriggerDanceVisualClientRpc(int danceType)
    {
        if (IsOwner) return; // O jogador local já disparou a animação
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

    [ServerRpc]
    private void TriggerStopDanceVisualServerRpc()
    {
        TriggerStopDanceVisualClientRpc();
    }

    [ClientRpc]
    private void TriggerStopDanceVisualClientRpc()
    {
        if (IsOwner) return;
        if (animationLogic != null)
        {
            animationLogic.StopDanceAnimation();
        }
    }

    private void CheckAndCompleteHunterDanceTask()
    {
        if (!IsOwner) return;
        if (GameSpawner.Instance != null && (!GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.IsGameOver))
        {
            return;
        }

        if (currentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex)
        {
            var hunter = GetClosestHunterPlayer(transform.position);
            if (hunter != null && !hunter.IsDead && !hunter.IsGhost)
            {
                if (HunterDanceAreaVisual.IsPlayerInsideDanceArea(transform.position, hunter.transform))
                {
                    CompleteLocalHunterDanceTask();
                }
            }
        }
    }

    public void OnDance1(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (isDying || isGhost.Value) return;
        if (isHunter) return; // Regra: Hunters não acenam nem dançam
        if (isGreeting || isDancing) return;

        if (context.started)
        {
            CheckAndCompleteHunterDanceTask();

            if (danceRoutine != null) StopCoroutine(danceRoutine);
            danceRoutine = StartCoroutine(DanceRoutine(1));
        }
    }

    public void OnDance2(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch) return;
        if (isDying || isGhost.Value) return;
        if (isHunter) return; // Regra: Hunters não acenam nem dançam
        if (isGreeting || isDancing) return;

        if (context.started)
        {
            CheckAndCompleteHunterDanceTask();

            if (danceRoutine != null) StopCoroutine(danceRoutine);
            danceRoutine = StartCoroutine(DanceRoutine(2));
        }
    }
}

/// <summary>
/// Monitora o efeito de surgimento (spawn) do sobrevivente.
/// Se a tela local for a Morte (Hunter), destrói o efeito instantaneamente para que ela nunca identifique quem é o jogador.
/// </summary>
public class PlayerSpawnEffectVisibility : MonoBehaviour
{
    private void Awake()
    {
        if (PlayerController.IsLocalPlayerHunter())
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (PlayerController.IsLocalPlayerHunter())
        {
            Destroy(gameObject);
        }
    }
}
