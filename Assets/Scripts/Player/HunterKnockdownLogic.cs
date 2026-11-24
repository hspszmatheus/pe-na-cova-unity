using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class HunterKnockdownLogic : NetworkBehaviour
{
    [Header("Física do Pino de Boliche")]
    [Tooltip("Força horizontal do lançamento")]
    [SerializeField] private float horizontalForce = 10.5f;

    [Tooltip("Força vertical do arco de voo")]
    [SerializeField] private float verticalForce = 10.5f;

    [Tooltip("Velocidade angular do capotamento estilo pino")]
    [SerializeField] private float tumbleTorque = 16.0f;

    [Header("Material Físico Quicante")]
    [Tooltip("PhysicsMaterial de alta restituição elástica")]
    [SerializeField] private PhysicsMaterial bowlingPhysicMaterial;

    [Header("Recuperação")]
    [Tooltip("Tempo mínimo em segundos antes de poder apertar Espaço para se levantar")]
    [SerializeField] private float recoveryCooldown = 2.5f;

    [Tooltip("Tempo máximo em segundos até o Hunter se levantar automaticamente")]
    [SerializeField] private float maxKnockdownDuration = 6.0f;

    [Header("Vidas do Hunter")]
    public const int MaxLives = 3;
    public NetworkVariable<int> CurrentLives { get; } = new NetworkVariable<int>(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private CharacterController characterController;
    private PlayerMovementLogic movementLogic;
    private PlayerAnimationLogic animationLogic;

    // Componentes dinâmicos adicionados e destruídos EXCLUSIVAMENTE via código
    private Rigidbody rb;
    private CapsuleCollider dynamicCapsuleCollider;

    private bool isKnockedDown = false;
    private float knockdownStartTime;

    public bool IsKnockedDown => isKnockedDown;
    public bool CanRecover => isKnockedDown && (Time.time - knockdownStartTime >= recoveryCooldown);

    public void Initialize()
    {
        characterController = GetComponent<CharacterController>();
        movementLogic = GetComponent<PlayerMovementLogic>();
        animationLogic = GetComponent<PlayerAnimationLogic>();

        // Carrega o material de quique se não estiver atribuído
        if (bowlingPhysicMaterial == null)
        {
            bowlingPhysicMaterial = Resources.Load<PhysicsMaterial>("BowlingPinPhysicsMaterial");
            if (bowlingPhysicMaterial == null)
            {
#if UNITY_EDITOR
                bowlingPhysicMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Settings/Physics/BowlingPinPhysicsMaterial.physicMaterial");
#endif
            }
        }

        // Garante que NENHUM Rigidbody ou CapsuleCollider residual exista no GameObject durante a inicialização,
        // garantindo que o CharacterController funcione sem conflito durante a movimentação normal.
        CleanupDynamicPhysics();
    }

    void Awake()
    {
        Initialize();
        if (GetComponent<HunterOverheadLifeVisuals>() == null)
        {
            gameObject.AddComponent<HunterOverheadLifeVisuals>();
        }
    }

    void Start()
    {
    }

    public override void OnNetworkSpawn()
    {
        CurrentLives.OnValueChanged += OnLivesChanged;
        RegisterToHUD();

        var pc = GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.OnNicknameChanged += nick =>
            {
                if (GameHUDController.Instance != null && IsSpawned)
                {
                    GameHUDController.Instance.UpdateHunterLives(NetworkObjectId, nick, CurrentLives.Value, MaxLives);
                }
            };
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentLives.OnValueChanged -= OnLivesChanged;
        UnregisterFromHUD();
    }

    private void OnLivesChanged(int oldVal, int newVal)
    {
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.UpdateHunterLives(NetworkObjectId, GetHunterDisplayName(), newVal, MaxLives);
        }
    }

    public void RegisterToHUD()
    {
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.UpdateHunterLives(NetworkObjectId, GetHunterDisplayName(), CurrentLives.Value, MaxLives);
        }
    }

    public void UnregisterFromHUD()
    {
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.UnregisterHunter(NetworkObjectId);
        }
    }

    public string GetHunterDisplayName()
    {
        var pc = GetComponent<PlayerController>();
        if (pc != null && !string.IsNullOrWhiteSpace(pc.Nickname))
        {
            return pc.Nickname;
        }
        return $"Hunter {OwnerClientId + 1}";
    }

    /// <summary>
    /// Penaliza este Hunter por matar um NPC inocente ao invés de um jogador.
    /// Retira 1 das 3 vidas. Se cometer 3 erros (0 vidas restantes), o Hunter morre e os sobreviventes vencem!
    /// </summary>
    public void PenalizeWrongNpcKill()
    {
        if (!IsServer) return;

        if (CurrentLives.Value > 0)
        {
            CurrentLives.Value = Mathf.Max(0, CurrentLives.Value - 1);
            var pc = GetComponent<PlayerController>();
            string hName = GetHunterDisplayName();
            Debug.LogWarning($"[HunterKnockdownLogic] {hName} matou um NPC inocente por engano! Vidas restantes: {CurrentLives.Value}/{MaxLives}");

            if (CurrentLives.Value == 0)
            {
                Debug.LogWarning($"[HunterKnockdownLogic] {hName} cometeu 3 erros de NPC e foi ELIMINADO!");
                if (pc != null)
                {
                    pc.KillHunterFromMistakes();
                }
                else
                {
                    GameSpawner.Instance?.CheckHunterLivesEliminated();
                }
            }
        }
    }

    public void TakeDamage(int damage = 1, string attackerName = "Sobreviventes")
    {
        if (IsServer)
        {
            CurrentLives.Value = Mathf.Max(0, CurrentLives.Value - damage);
            Debug.Log($"[HunterKnockdownLogic] Hunter tomou dano de {attackerName}! Vidas restantes: {CurrentLives.Value}/{MaxLives}");

            if (CurrentLives.Value == 0)
            {
                Debug.LogWarning("[HunterKnockdownLogic] Todas as vidas do Hunter foram zeradas!");
                var pc = GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.KillHunterFromMistakes();
                }
                else
                {
                    GameSpawner.Instance?.CheckHunterLivesEliminated();
                }
            }
        }
    }

    void OnDisable()
    {
        GameAudioManager.Instance?.StopHunterRevealCharge();

        // Se o objeto for desativado durante a queda, limpa os componentes dinâmicos
        if (isKnockedDown)
        {
            CleanupDynamicPhysics();
            isKnockedDown = false;
        }
    }

    void Update()
    {
        if (!isKnockedDown) return;

        // Auto-recuperação após o tempo limite
        if (!IsSpawned || IsOwner)
        {
            if (Time.time - knockdownStartTime >= maxKnockdownDuration)
            {
                TryRecover();
            }
        }
    }

    /// <summary>
    /// Chamado pelo atacante (Player normal) quando acerta o chute no Hunter.
    /// </summary>
    public void TakeKick(Vector3 kickDir)
    {
        if (IsSpawned)
        {
            RequestKickServerRpc(kickDir);
        }
        else
        {
            ApplyKick(kickDir);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestKickServerRpc(Vector3 kickDir, ServerRpcParams rpcParams = default)
    {
        // O chute do sobrevivente aplica o efeito de stun/knockdown por 5s no Hunter
        ApplyKickClientRpc(kickDir);
    }

    [ClientRpc]
    private void ApplyKickClientRpc(Vector3 kickDir)
    {
        ApplyKick(kickDir);
    }

    public void ApplyKick(Vector3 kickDir)
    {
        GameAudioManager.Instance?.PlayPlayerKickHit(transform.position);
        GameAudioManager.Instance?.PlayHunterKickScream(transform.position);

        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (movementLogic == null) movementLogic = GetComponent<PlayerMovementLogic>();
        if (animationLogic == null) animationLogic = GetComponent<PlayerAnimationLogic>();

        bool wasAlreadyKnockedDown = isKnockedDown;
        isKnockedDown = true;
        knockdownStartTime = Time.time;

        // Cancela imediatamente qualquer golpe, foice ou magia em canalização
        var pc = GetComponent<PlayerController>();
        if (pc != null && pc.IsCastingReveal)
        {
            pc.CancelHunterRevealCasting();
        }
        GameAudioManager.Instance?.StopHunterRevealCharge();

        var weaponVisuals = GetComponent<HunterWeaponVisuals>();
        if (weaponVisuals != null) weaponVisuals.StopAttackVisuals();
        if (animationLogic != null) animationLogic.ResetAttackTrigger();

        // 1. Desativa CharacterController e lógica de movimento para liberar o GameObject para a física pura
        if (characterController != null) characterController.enabled = false;
        if (movementLogic != null) movementLogic.enabled = false;

        // 2. Trava o boneco rígido ("duro" como um pino de boliche)
        if (animationLogic != null) animationLogic.SetStiffState(true);

        // 3. Adiciona ou reaplica impulso no Rigidbody quicante EXCLUSIVAMENTE POR CÓDIGO
        // (apenas o dono simula a física e transmite via ClientNetworkTransform)
        if (!IsSpawned || IsOwner)
        {
            SetupDynamicPhysics(kickDir);
        }

        Debug.Log($"[HunterKnockdownLogic] Hunter levou Flying Kick {(wasAlreadyKnockedDown ? "(combo encadeado no ar/chão)" : "")}! Lançado na direção {kickDir}");
    }

    private void SetupDynamicPhysics(Vector3 kickDir)
    {
        // Se já possui Rigidbody ativo (já está voando, quicando ou atordoada no chão):
        // Acorda o Rigidbody e reaplica o novo impulso de lançamento e capotamento tridimensional!
        if (rb != null)
        {
            rb.WakeUp();
            Vector3 newLaunchVelocity = kickDir.normalized * horizontalForce + Vector3.up * verticalForce;
            rb.linearVelocity = newLaunchVelocity;

            Vector3 newTumbleAxis = Vector3.Cross(Vector3.up, kickDir.normalized).normalized;
            if (newTumbleAxis.sqrMagnitude < 0.01f) newTumbleAxis = Vector3.right;

            rb.angularVelocity = newTumbleAxis * tumbleTorque + Random.insideUnitSphere * 2.0f;
            return;
        }

        // Limpa qualquer colisor/rigidbody anterior por segurança
        CleanupDynamicPhysics();

        // 1. Adiciona CapsuleCollider com PhysicsMaterial quicante
        gameObject.tag = "Player";
        dynamicCapsuleCollider = gameObject.AddComponent<CapsuleCollider>();
        dynamicCapsuleCollider.center = new Vector3(0, 0.9f, 0);
        dynamicCapsuleCollider.radius = 0.45f;
        dynamicCapsuleCollider.height = 1.8f;
        if (bowlingPhysicMaterial != null)
        {
            dynamicCapsuleCollider.material = bowlingPhysicMaterial;
        }

        // 2. Adiciona Rigidbody nativo dinâmico
        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 75.0f;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 0.4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.isKinematic = false;
        rb.detectCollisions = true;

        // Centro de massa na altura do tórax/quadril para rotacionar no meio do corpo e nunca nos pés
        rb.centerOfMass = new Vector3(0, 0.9f, 0);

        // Lança o pino em arco parabólico
        Vector3 launchVelocity = kickDir.normalized * horizontalForce + Vector3.up * verticalForce;
        rb.linearVelocity = launchVelocity;

        // Torque tridimensional de capotamento (perpendicular ao vetor do chute)
        Vector3 tumbleAxis = Vector3.Cross(Vector3.up, kickDir.normalized).normalized;
        if (tumbleAxis.sqrMagnitude < 0.01f) tumbleAxis = Vector3.right;

        rb.angularVelocity = tumbleAxis * tumbleTorque + Random.insideUnitSphere * 2.0f;
    }

    /// <summary>
    /// Tenta se levantar (acionado ao apertar Espaço pelo jogador do Hunter ou automaticamente).
    /// </summary>
    public void TryRecover()
    {
        if (!CanRecover) return;

        if (IsSpawned)
        {
            RequestRecoverServerRpc();
        }
        else
        {
            ApplyRecover();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRecoverServerRpc()
    {
        ApplyRecoverClientRpc();
    }

    [ClientRpc]
    private void ApplyRecoverClientRpc()
    {
        ApplyRecover();
    }

    public void ApplyRecover()
    {
        if (!isKnockedDown) return;

        isKnockedDown = false;

        if (!IsSpawned || IsOwner)
        {
            // Remove completamente o Rigidbody e CapsuleCollider dinâmicos para não conflitar com o CharacterController
            CleanupDynamicPhysics();

            // Corrige a rotação para ficar em pé virado na direção que a câmera do jogador está olhando
            var camLogic = GetComponent<PlayerCameraLogic>();
            Vector3 camFwd = camLogic != null ? camLogic.GetForwardDirection() : Vector3.zero;
            if (camFwd.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(camFwd, Vector3.up);
            }
            else
            {
                float currentYaw = transform.eulerAngles.y;
                transform.rotation = Quaternion.Euler(0, currentYaw, 0);
            }

            // Garante que o personagem esteja no chão e não preso dentro da geometria
            if (Physics.Raycast(transform.position + Vector3.up * 1.0f, Vector3.down, out RaycastHit hit, 3.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + Vector3.up * 0.05f;
            }
        }

        // Reativa CharacterController e movimentação pura
        if (characterController != null)
        {
            characterController.enabled = true;
            PlayerController.IgnoreAllCharacterCollisions();
        }
        if (movementLogic != null) movementLogic.enabled = true;

        // Descongela as animações normais
        if (animationLogic != null) animationLogic.SetStiffState(false);

        Debug.Log("[HunterKnockdownLogic] Hunter se levantou. Rigidbody removido e CharacterController reativado.");
    }

    /// <summary>
    /// Destrói e remove completamente qualquer Rigidbody ou CapsuleCollider do GameObject.
    /// </summary>
    private void CleanupDynamicPhysics()
    {
        var allRbs = GetComponents<Rigidbody>();
        foreach (var r in allRbs)
        {
            if (r != null)
            {
                r.linearVelocity = Vector3.zero;
                r.angularVelocity = Vector3.zero;
                r.isKinematic = true;
                r.detectCollisions = false;

                if (Application.isPlaying)
                    Destroy(r);
                else
                    DestroyImmediate(r);
            }
        }
        rb = null;

        var allCapsules = GetComponents<CapsuleCollider>();
        foreach (var c in allCapsules)
        {
            if (c != null)
            {
                c.enabled = false;

                if (Application.isPlaying)
                    Destroy(c);
                else
                    DestroyImmediate(c);
            }
        }
        dynamicCapsuleCollider = null;
    }
}
