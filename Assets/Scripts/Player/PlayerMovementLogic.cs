using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementLogic : MonoBehaviour
{
    [Header("Configurações de Movimento")]
    [SerializeField] public float walkSpeed = 1.5f;
    [SerializeField] public float runSpeed = 8.0f;
    [SerializeField] public float rotationSpeed = 10f;

    [Header("Orientação da Câmera (Estilo Hunter/Shooter)")]
    [Tooltip("Quando ativado, a rotação do corpo é orientada diretamente pela câmera")]
    [SerializeField] private bool orientToCamera = false;
    public bool OrientToCamera { get => orientToCamera; set => orientToCamera = value; }

    [Header("Configurações de Gravidade e Pulo")]
    [SerializeField] public float gravity = -9.81f;
    [SerializeField] public float jumpHeight = 1.5f;

    [Header("Configurações de Voo (Modo Espectador)")]
    [SerializeField] public float spectatorFlySpeed = 7.5f;
    [SerializeField] public float spectatorFastFlySpeed = 15.0f;
    [SerializeField] public float spectatorVerticalSpeed = 6.0f;
    [Tooltip("Centro da área jogável da cidade para delimitação do voo do fantasma")]
    [SerializeField] public Vector2 ghostFlightCenter = new Vector2(0.2f, -39.9f);
    [Tooltip("Raio máximo que o fantasma pode voar em relação ao centro (dentro das paredes de 74.07m)")]
    [SerializeField] public float maxGhostFlightRadius = 73.5f;
    [Tooltip("Altitude mínima de voo do fantasma")]
    [SerializeField] public float minGhostFlightAltitude = -0.2f;
    [Tooltip("Altitude máxima de voo do fantasma (abaixo do teto de 40m)")]
    [SerializeField] public float maxGhostFlightAltitude = 38.0f;

    [Header("Sistema de Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina = 100f;
    [SerializeField] private float staminaDrainRate = 22f; // ~4.5s de corrida contínua
    [SerializeField] private float staminaRegenRate = 25f; // ~4s para regeneração total
    [SerializeField] private float staminaRegenDelay = 0.8f; // Tempo após parar de correr para iniciar a regeneração

    private CharacterController characterController;
    private PlayerController playerController;
    private bool isRunning = false;
    private float verticalVelocity; // Cuida da força de cair/subir
    private float lastDrainTime = -10f;
    private bool isExhausted = false;

    public float CurrentStamina => currentStamina;
    public float MaxStamina => maxStamina;
    public bool IsExhausted => isExhausted;
    public bool CanRun => !isExhausted && currentStamina > 0.05f;

    public void SetStaminaStats(float max, float drainRate, float regenRate, float regenDelay)
    {
        maxStamina = max;
        currentStamina = max;
        staminaDrainRate = drainRate;
        staminaRegenRate = regenRate;
        staminaRegenDelay = regenDelay;
    }

    public void Initialize()
    {
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
    }

    public void SetRunningState(bool running)
    {
        isRunning = running;
    }

    public void ResetVerticalVelocity()
    {
        verticalVelocity = -2f;
    }

    // Função de pulo agora vive aqui
    public bool TriggerJump()
    {
        if (characterController != null && characterController.enabled && characterController.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            return true;
        }
        return false;
    }

    // Processa tudo junto: Frente/Lados + Cima/Baixo (estilo Genshin Impact)
    public void HandleMovement(Vector2 moveInput, Vector3 camForward, Vector3 camRight, bool forceFaceCamera = false)
    {
        if (characterController == null || !characterController.enabled) return;

        bool isMovingInput = moveInput.sqrMagnitude > 0.01f;
        bool isActuallyRunning = isRunning && isMovingInput && CanRun;

        // Gerenciamento de Stamina
        if (isActuallyRunning)
        {
            currentStamina -= staminaDrainRate * Time.deltaTime;
            lastDrainTime = Time.time;
            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                isExhausted = true;
                isActuallyRunning = false;
            }
        }
        else
        {
            if (Time.time - lastDrainTime >= staminaRegenDelay)
            {
                currentStamina += staminaRegenRate * Time.deltaTime;
                if (currentStamina >= maxStamina)
                {
                    currentStamina = maxStamina;
                    isExhausted = false;
                }
                else if (isExhausted && currentStamina >= maxStamina * 0.20f)
                {
                    isExhausted = false; // Recupera a capacidade de correr ao atingir 20%
                }
            }
        }

        // 1. Calcula movimento horizontal (Chão)
        Vector3 moveDirection = (camForward * moveInput.y) + (camRight * moveInput.x);
        float currentSpeed = isActuallyRunning ? runSpeed : walkSpeed;
        Vector3 horizontalMove = moveDirection * currentSpeed;

        // Rotação do personagem estilo Genshin Impact:
        if (forceFaceCamera)
        {
            // Mirando com mira telescópica/bola de fogo ou atacando: Hunter alinha o corpo com o centro da câmera/mira
            if (camForward != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(camForward), rotationSpeed * 2.5f * Time.deltaTime);
            }
        }
        else if (moveDirection != Vector3.zero)
        {
            // Movimentação estilo Genshin Impact:
            // Ao segurar W (ou qualquer direção de movimento) e girar a câmera, o personagem vira suavemente na direção do movimento orientado pela câmera
            float rotMultiplier = orientToCamera ? 1.5f : 1.0f;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDirection), rotationSpeed * rotMultiplier * Time.deltaTime);
        }
        // Quando parado (moveDirection == Vector3.zero) e não está mirando/atacando:
        // NÃO sobrescreve a rotação, permitindo que a câmera gire 360° livremente em volta do personagem!

        // 2. Calcula repulsão suave contra outros pedestres (NPCs, Players e Hunter)
        // Permite cruzar e passar por dentro uns dos outros (intangibilidade),
        // mas empurra suavemente para fora se sobrepostos, impedindo que fiquem dentro do outro.
        Vector3 repulsionVelocity = CalculatePedestrianRepulsion();

        // 3. Calcula movimento vertical (Ar / Gravidade)
        if (characterController.isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f; // Mantém o boneco colado no chão
        }
        verticalVelocity += gravity * Time.deltaTime;
        Vector3 verticalMove = new Vector3(0, verticalVelocity, 0);

        // 4. Aplica a soma de todos os movimentos no Character Controller de forma integrada!
        Vector3 totalMovement = (horizontalMove + repulsionVelocity + verticalMove) * Time.deltaTime;
        characterController.Move(totalMovement);
    }

    /// <summary>
    /// Processa o voo livre tridimensional no modo espectador (fantasmas).
    /// WASD: deslocamento planar relativo à câmera.
    /// Espaço: sobe / ganha altitude.
    /// Ctrl: desce / perde altitude.
    /// Shift: impulso rápido (boost).
    /// Sem aplicação de gravidade nem atrito de solo.
    /// </summary>
    public void HandleSpectatorFlight(Vector2 moveInput, Vector3 camForward, Vector3 camRight, float verticalInput, bool isFast)
    {
        if (Time.deltaTime <= 0f) return;

        EnsureSafeGhostControllerStepOffset();

        float speed = isFast ? spectatorFastFlySpeed : spectatorFlySpeed;
        float vSpeed = isFast ? (spectatorVerticalSpeed * 1.5f) : spectatorVerticalSpeed;

        // Projeta direções da câmera no plano horizontal para controle WASD puro e intuitivo
        Vector3 forwardFlat = new Vector3(camForward.x, 0f, camForward.z).normalized;
        Vector3 rightFlat = new Vector3(camRight.x, 0f, camRight.z).normalized;

        Vector3 horizontalDir = (forwardFlat * moveInput.y) + (rightFlat * moveInput.x);
        if (horizontalDir.sqrMagnitude > 1f)
        {
            horizontalDir.Normalize();
        }

        Vector3 horizontalMove = horizontalDir * speed;
        Vector3 verticalMove = Vector3.up * (verticalInput * vSpeed);

        // Rotação suave do corpo/fantasma na direção para onde está se movendo horizontalmente
        if (horizontalDir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(horizontalDir), rotationSpeed * 1.5f * Time.deltaTime);
        }

        Vector3 startPos = transform.position;
        Vector3 desiredPos = startPos + ((horizontalMove + verticalMove) * Time.deltaTime);

        // Pré-contenção suave no cilindro de voo e abaixo da face inferior do Boundary_Ceiling (Y=38.0m)
        float safeMaxAltitude = GetSafeGhostMaxAltitude();
        ClampToGhostFlightBounds(ref desiredPos, safeMaxAltitude, out _);

        // Executa a translação no CharacterController (ou diretamente no transform se inativo)
        Vector3 safeMovement = desiredPos - startPos;
        if (characterController != null && characterController.enabled)
        {
            characterController.Move(safeMovement);
        }
        else
        {
            transform.position += safeMovement;
        }

        // Garante rigorosamente a contenção caso alguma colisão externa empurre o fantasma além do limite
        Vector3 currentPos = transform.position;
        Vector2 flatOffset = new Vector2(currentPos.x - ghostFlightCenter.x, currentPos.z - ghostFlightCenter.y);
        bool outOfBounds = flatOffset.magnitude > (maxGhostFlightRadius + 0.02f)
            || currentPos.y > (safeMaxAltitude + 0.02f)
            || currentPos.y < (minGhostFlightAltitude - 0.02f);

        if (outOfBounds && ClampToGhostFlightBounds(ref currentPos, safeMaxAltitude, out _))
        {
            if (characterController != null && characterController.enabled)
            {
                EnsureSafeGhostControllerStepOffset();
                characterController.enabled = false;
                transform.position = currentPos;
                characterController.enabled = true;
            }
            else
            {
                transform.position = currentPos;
            }
        }

        // Reseta gravidade residual para que o espectador mantenha altitude estável sem cair
        verticalVelocity = 0f;
    }

    private float GetSafeGhostMaxAltitude()
    {
        if (characterController == null) return maxGhostFlightAltitude;
        float ceilingColliderBottomY = 38.0f;
        float topExtent = characterController.height + characterController.skinWidth + 0.02f;
        return Mathf.Min(maxGhostFlightAltitude, ceilingColliderBottomY - topExtent);
    }

    private bool ClampToGhostFlightBounds(ref Vector3 pos, float safeMaxAltitude, out bool clamped)
    {
        clamped = false;

        Vector2 flatOffset = new Vector2(pos.x - ghostFlightCenter.x, pos.z - ghostFlightCenter.y);
        float flatDist = flatOffset.magnitude;
        if (flatDist > maxGhostFlightRadius)
        {
            Vector2 dir = flatDist > 0.0001f ? (flatOffset / flatDist) : Vector2.right;
            pos.x = ghostFlightCenter.x + dir.x * maxGhostFlightRadius;
            pos.z = ghostFlightCenter.y + dir.y * maxGhostFlightRadius;
            clamped = true;
        }

        if (pos.y > safeMaxAltitude)
        {
            pos.y = safeMaxAltitude;
            clamped = true;
        }
        else if (pos.y < minGhostFlightAltitude)
        {
            pos.y = minGhostFlightAltitude;
            clamped = true;
        }

        return clamped;
    }

    private void EnsureSafeGhostControllerStepOffset()
    {
        if (characterController == null) return;
        float maxAllowedStep = characterController.height + (characterController.radius * 2f);
        if (characterController.stepOffset > maxAllowedStep)
        {
            characterController.stepOffset = Mathf.Min(0.05f, maxAllowedStep);
        }
    }

    /// <summary>
    /// Calcula uma velocidade de repulsão suave (em m/s) quando o personagem sobrepõe outro pedestre (NPC, Player ou Hunter).
    /// Os personagens são intangíveis entre si (conseguem cruzar e passar por dentro uns dos outros),
    /// mas a repulsão empurra ambos suavemente para fora, impedindo que fiquem ocultos ou sobrepostos por muito tempo.
    /// A física é 100% simétrica e idêntica para NPCs, Players e Hunter.
    /// </summary>
    private Vector3 CalculatePedestrianRepulsion()
    {
        if (characterController == null || !characterController.enabled) return Vector3.zero;
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (playerController != null && (playerController.IsDead || playerController.IsGhost)) return Vector3.zero;

        Vector3 myPos = transform.position;
        Vector3 totalRepulsion = Vector3.zero;
        const float repulsionRadius = 0.80f; // Raio em que a repulsão suave atua ao penetrarem
        const float maxRepulsionSpeed = 1.8f; // Velocidade máxima de repulsão suave (m/s)

        // 1. Repulsão contra outros Players vivos (incluindo o Hunter)
        var players = PlayerController.ActivePlayers;
        if (players != null)
        {
            int playerCount = players.Count;
            for (int i = 0; i < playerCount; i++)
            {
                var other = players[i];
                if (other == null || other.gameObject == gameObject || other.IsDead || other.IsGhost) continue;

                Vector3 otherPos = other.transform.position;
                float yDiff = Mathf.Abs(myPos.y - otherPos.y);
                if (yDiff > 1.8f) continue; // Ignora se estiverem em pisos/andares verticais diferentes

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
        }

        // 2. Repulsão contra NPCs vivos
        var npcs = NPCController.ActiveNpcs;
        if (npcs != null)
        {
            int npcCount = npcs.Count;
            for (int i = 0; i < npcCount; i++)
            {
                var npc = npcs[i];
                if (npc == null || npc.IsDead) continue;

                Vector3 npcPos = npc.transform.position;
                float yDiff = Mathf.Abs(myPos.y - npcPos.y);
                if (yDiff > 1.8f) continue; // Ignora se estiverem em pisos/andares verticais diferentes

                Vector3 diff = myPos - npcPos;
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

        // Limita a repulsão máxima para evitar acelerações exageradas caso vários personagens se cruzem juntos
        if (totalRepulsion.magnitude > maxRepulsionSpeed * 1.5f)
        {
            totalRepulsion = totalRepulsion.normalized * (maxRepulsionSpeed * 1.5f);
        }

        return totalRepulsion;
    }

    public bool CheckIfGrounded()
    {
        return characterController != null && characterController.enabled && characterController.isGrounded;
    }
}
