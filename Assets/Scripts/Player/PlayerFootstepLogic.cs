using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gerencia o som de passos do jogador com:
/// - Sincronização de cadência (intervalo) entre andar e correr usando ciclo normalizado
/// - Velocidade de reprodução (pitch) dinâmica conforme a velocidade
/// - Detecção de superfície via Raycast para trocar o clip de áudio (rua/grama/etc.)
/// - Suporte a jogadores remotos no multiplayer
/// - Primeiro passo responsivo ao iniciar o movimento
/// - Proteção contra repetição em spam de teclas (cooldown mínimo)
/// - Tolerância para oscilações de contato com o chão ao correr (grace period)
/// - Filtro passa-baixa (low-pass) para eliminar estalos e garantir passos aveludados
/// - Som de impacto/passo ao aterrissar após saltar ou cair
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PlayerFootstepLogic : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    //  SUPERFÍCIES
    // ──────────────────────────────────────────────────────────────
    [System.Serializable]
    public class FootstepSurface
    {
        [Tooltip("Tag do GameObject que representa esta superfície (ex: 'Street', 'Grass')")]
        public string surfaceTag = "Untagged";
        [Tooltip("Clips de áudio para esta superfície (um será escolhido aleatoriamente a cada passo)")]
        public AudioClip[] clips;
    }

    [Header("Superfícies e Áudios")]
    [Tooltip("Configure uma entrada por tipo de superfície. A tag do objeto abaixo do player é usada para selecionar o som correto.")]
    [SerializeField] private FootstepSurface[] surfaces;

    [Tooltip("Clips usados quando nenhuma superfície cadastrada for detectada (fallback)")]
    [SerializeField] private AudioClip[] defaultClips;

    // ──────────────────────────────────────────────────────────────
    //  VOLUME
    // ──────────────────────────────────────────────────────────────
    [Header("Volume")]
    [Tooltip("Volume dos passos (ajustado para ser suave e sutil, sem sobrecarregar o som ambiente)")]
    [SerializeField, Range(0f, 1f)] private float stepVolume = 0.018f;
    [SerializeField, Range(0f, 0.05f)] private float volumeVariation = 0.003f;

    // ──────────────────────────────────────────────────────────────
    //  ÁUDIO ESPACIAL E DISTÂNCIA (PROXIMIDADE)
    // ──────────────────────────────────────────────────────────────
    [Header("Áudio Espacial e Distância")]
    [Tooltip("Distância máxima em metros para ouvir passos de NPCs e outros jogadores. Além desta distância, o som é 100% inaudível.")]
    [SerializeField] private float maxAudibleDistance = 9.0f;
    [Tooltip("Distância mínima em metros onde o volume dos passos atinge o máximo (100%)")]
    [SerializeField] private float minAudibleDistance = 2.0f;

    public float MaxAudibleDistance => maxAudibleDistance;
    public float MinAudibleDistance => minAudibleDistance;

    // ──────────────────────────────────────────────────────────────
    //  PITCH (TOM E VELOCIDADE)
    // ──────────────────────────────────────────────────────────────
    [Header("Velocidade e Tom (Pitch)")]
    [Tooltip("Velocidade/tom ao caminhar (tom ligeiramente mais grave deixa o impacto mais macio)")]
    [SerializeField] private float walkPitch = 0.88f;
    [Tooltip("Velocidade/tom ao correr (ajustado para não ficar fino ou estridente)")]
    [SerializeField] private float runPitch = 1.02f;
    [Tooltip("Variação sutil de pitch em cada passo para soar natural sem desafinar")]
    [SerializeField] private float pitchJitter = 0.02f;

    // ──────────────────────────────────────────────────────────────
    //  FILTRO SUAVE (LOW-PASS)
    // ──────────────────────────────────────────────────────────────
    [Header("Filtro Suave (Amortecimento de Agudos)")]
    [Tooltip("Aplica um filtro passa-baixa para cortar estalos secos e agudos estridentes dos sapatos")]
    [SerializeField] private bool useLowPassFilter = true;
    [Tooltip("Frequência de corte do filtro (2000 Hz deixa o som bem abafado, aveludado e macio)")]
    [SerializeField] private float lowPassCutoff = 2000f;

    // ──────────────────────────────────────────────────────────────
    //  CADÊNCIA
    // ──────────────────────────────────────────────────────────────
    [Header("Cadência dos Passos (Intervalo em segundos)")]
    [Tooltip("Intervalo entre passos ao caminhar")]
    [SerializeField] private float walkStepInterval = 0.5f;
    [Tooltip("Intervalo entre passos ao correr")]
    [SerializeField] private float runStepInterval = 0.3f;

    // ──────────────────────────────────────────────────────────────
    //  ESTABILIDADE / ANTI-GLITCH
    // ──────────────────────────────────────────────────────────────
    [Header("Estabilidade")]
    [Tooltip("Tempo mínimo absoluto entre dois passos para evitar sons duplicados ou metralhadora ao teclar W rápido")]
    [SerializeField] private float minStepCooldown = 0.22f;
    [Tooltip("Tolerância de tempo para micro-perdas de contato com o chão ao correr sem reiniciar o passo")]
    [SerializeField] private float stopGracePeriod = 0.12f;

    // ──────────────────────────────────────────────────────────────
    //  PULO E ATERRISSAGEM
    // ──────────────────────────────────────────────────────────────
    [Header("Pulo e Aterrissagem")]
    [Tooltip("Tempo mínimo no ar (segundos) para considerar um salto/queda e tocar som de passo ao aterrissar")]
    [SerializeField] private float minAirTimeToLand = 0.12f;
    [Tooltip("Multiplicador de volume ao aterrissar")]
    [SerializeField] private float landVolumeMultiplier = 1.15f;
    [Tooltip("Pitch da aterrissagem (um tom ligeiramente mais grave transmite a sensação de peso ao tocar o solo)")]
    [SerializeField] private float landPitch = 0.82f;

    // ──────────────────────────────────────────────────────────────
    //  RAYCAST
    // ──────────────────────────────────────────────────────────────
    [Header("Detecção de Superfície")]
    [Tooltip("Distância do Raycast para baixo para detectar a superfície")]
    [SerializeField] private float groundCheckDistance = 1.2f;
    [Tooltip("Layers que o Raycast deve atingir para detectar superfície")]
    [SerializeField] private LayerMask groundLayers = ~0; // Todos por padrão

    // ──────────────────────────────────────────────────────────────
    //  ESTADO INTERNO
    // ──────────────────────────────────────────────────────────────
    private AudioSource audioSource;
    private PlayerController playerController;
    private CharacterController characterController;
    private float stepCycle = 0f;
    private float lastStepTime = -10f;
    private float stopTimer = 0f;
    private bool isCurrentlyMoving = false;
    private int lastClipIndex = -1;
    private AudioClip proceduralStepClip;
    private Vector3 lastPosition;
    private bool wasGrounded = true;
    private float airTime = 0f;

    // ──────────────────────────────────────────────────────────────
    //  INICIALIZAÇÃO
    // ──────────────────────────────────────────────────────────────
    public void Initialize()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        lastPosition = transform.position;
        wasGrounded = true;
        airTime = 0f;

        ConfigureAudioSource();

        // Configura o filtro passa-baixa para suavizar agudos e estalos secos
        if (useLowPassFilter)
        {
            AudioLowPassFilter lowPass = GetComponent<AudioLowPassFilter>();
            if (lowPass == null)
                lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = lowPassCutoff;
            lowPass.lowpassResonanceQ = 1.0f;
        }

        // Gera clipe procedural como último recurso (sem nenhum clip configurado)
        bool hasAnyClip = (defaultClips != null && defaultClips.Length > 0);
        if (!hasAnyClip && surfaces != null)
        {
            foreach (var s in surfaces)
            {
                if (s.clips != null && s.clips.Length > 0) { hasAnyClip = true; break; }
            }
        }
        if (!hasAnyClip)
            proceduralStepClip = CreateProceduralFootstepClip();
    }

    /// <summary>
    /// Configura o AudioSource com atenuação e espacialização adequadas.
    /// Para o jogador local: som 2D puro direto no fone (sem perdas por ângulo ou distância da câmera).
    /// Para NPCs e jogadores remotos: som 3D espacial com curva suave que zera na distância máxima.
    /// </summary>
    public void ConfigureAudioSource()
    {
        if (audioSource == null) return;

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.dopplerLevel = 0f;
        audioSource.spread = 0f;

        bool isLocal = (playerController != null && playerController.IsOwner);
        if (isLocal)
        {
            audioSource.spatialBlend = 0.0f;
            audioSource.minDistance = 1.0f;
            audioSource.maxDistance = 50.0f;
        }
        else
        {
            audioSource.spatialBlend = 1.0f;
            audioSource.minDistance = minAudibleDistance;
            audioSource.maxDistance = maxAudibleDistance;
            audioSource.rolloffMode = AudioRolloffMode.Custom;

            float minRatio = Mathf.Clamp01(minAudibleDistance / Mathf.Max(0.1f, maxAudibleDistance));
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 1.0f, 0f, 0f),
                new Keyframe(minRatio, 1.0f, 0f, -1.2f),
                new Keyframe(0.50f, 0.35f, -1.0f, -1.0f),
                new Keyframe(0.80f, 0.08f, -0.4f, -0.4f),
                new Keyframe(1.0f, 0.0f, 0f, 0f)
            );
            audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        }
    }

    /// <summary>
    /// Verifica se a entidade está dentro do raio audível do jogador local ou da câmera principal.
    /// </summary>
    public bool IsWithinAudibleRange()
    {
        var localPlayer = PlayerController.LocalInstance;
        if (localPlayer != null)
        {
            float distSqr = (transform.position - localPlayer.transform.position).sqrMagnitude;
            return distSqr <= maxAudibleDistance * maxAudibleDistance;
        }

        if (Camera.main != null)
        {
            float distSqr = (transform.position - Camera.main.transform.position).sqrMagnitude;
            return distSqr <= maxAudibleDistance * maxAudibleDistance;
        }

        return false;
    }

    // ──────────────────────────────────────────────────────────────
    //  CHAMADO PELO PlayerController (jogador local / dono)
    // ──────────────────────────────────────────────────────────────
    public void HandleFootsteps(bool isMoving, bool isRunning, bool isGrounded)
    {
        if (playerController != null && playerController.IsOwner && audioSource != null && audioSource.spatialBlend != 0f)
        {
            ConfigureAudioSource();
        }
        ProcessFootstepCycle(isMoving, isRunning, isGrounded);
    }

    // ──────────────────────────────────────────────────────────────
    //  SUPORTE A JOGADORES REMOTOS (proxies de rede)
    // ──────────────────────────────────────────────────────────────
    private void Update()
    {
        // O jogador local é gerenciado diretamente pelo PlayerController
        if (playerController != null && playerController.IsOwner)
        {
            lastPosition = transform.position;
            return;
        }

        // Culling para jogadores remotos: se estiver fora do alcance audível, não processa passos
        if (!IsWithinAudibleRange())
        {
            lastPosition = transform.position;
            return;
        }

        Vector3 currentPos = transform.position;
        Vector3 delta = currentPos - lastPosition;
        delta.y = 0;
        float horizontalSpeed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = currentPos;

        bool isMoving = horizontalSpeed > 0.2f;
        bool isGrounded = (characterController != null && characterController.enabled) ? characterController.isGrounded : false;
        bool isRunning = horizontalSpeed > 4.0f;

        ProcessFootstepCycle(isMoving, isRunning, isGrounded);
    }

    // ──────────────────────────────────────────────────────────────
    //  NÚCLEO DO PROCESSAMENTO DO CICLO DE PASSOS (ANTI-GLITCH)
    // ──────────────────────────────────────────────────────────────
    private void ProcessFootstepCycle(bool isMoving, bool isRunning, bool isGrounded)
    {
        // ──────────────────────────────────────────────────────────────
        //  1. VERIFICAÇÃO DE ATERRISSAGEM (PULO / QUEDA)
        // ──────────────────────────────────────────────────────────────
        if (!isGrounded)
        {
            airTime += Time.deltaTime;
            wasGrounded = false;
        }
        else
        {
            // Se estava no ar e acabou de tocar o chão:
            if (!wasGrounded)
            {
                if (airTime >= minAirTimeToLand)
                {
                    PlayLandStep();
                    stepCycle = 0f;
                }
                airTime = 0f;
                wasGrounded = true;
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  2. MOVIMENTO HORIZONTAL NO SOLO (PASSOS)
        // ──────────────────────────────────────────────────────────────
        bool hasValidMovement = isMoving && isGrounded;

        if (!hasValidMovement)
        {
            stopTimer += Time.deltaTime;
            // Só considera o boneco realmente parado após a tolerância
            // Isso evita que micro-pulos ou irregularidades no terreno resetem o passo
            if (stopTimer >= stopGracePeriod)
            {
                isCurrentlyMoving = false;
                stepCycle = 0f;
            }
            return;
        }

        // Movimento válido ativo: zera o contador de parada
        stopTimer = 0f;

        // Se estava completamente parado e acabou de iniciar movimento:
        if (!isCurrentlyMoving)
        {
            isCurrentlyMoving = true;
            stepCycle = 0f;

            // Toca o primeiro passo imediatamente se o cooldown permitir
            // (evita metralhadora de sons se o jogador ficar teclando W repetidamente)
            // Se acabou de aterrissar, PlayLandStep já tocou o som e atualizou lastStepTime
            if (Time.time - lastStepTime >= minStepCooldown)
            {
                PlayStep(isRunning);
            }
            return;
        }

        // Avanço suave do ciclo de passos (0.0 até 1.0)
        // Usar ciclo normalizado evita repetições instantâneas ao segurar/soltar Shift
        float currentInterval = isRunning ? runStepInterval : walkStepInterval;
        stepCycle += Time.deltaTime / Mathf.Max(0.05f, currentInterval);

        if (stepCycle >= 1f)
        {
            if (Time.time - lastStepTime >= minStepCooldown)
            {
                PlayStep(isRunning);
                stepCycle = 0f;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  REPRODUÇÃO DO SOM DE ATERRISSAGEM (IMPACTO COM O CHÃO)
    // ──────────────────────────────────────────────────────────────
    public void PlayLandStep()
    {
        if (audioSource == null) return;

        bool isLocal = (playerController != null && playerController.IsOwner);
        if (!isLocal && !IsWithinAudibleRange())
            return;

        lastStepTime = Time.time;

        float randomPitch = landPitch + Random.Range(-pitchJitter, pitchJitter);
        float landVol = Mathf.Clamp01(stepVolume * landVolumeMultiplier + Random.Range(-volumeVariation, volumeVariation));

        audioSource.pitch = Mathf.Clamp(randomPitch, 0.5f, 2.5f);

        AudioClip clipToPlay = GetClipForCurrentSurface();
        if (clipToPlay != null)
            audioSource.PlayOneShot(clipToPlay, landVol);
    }

    // ──────────────────────────────────────────────────────────────
    //  REPRODUÇÃO DO PASSO
    // ──────────────────────────────────────────────────────────────
    private void PlayStep(bool isRunning)
    {
        if (audioSource == null) return;

        bool isLocal = (playerController != null && playerController.IsOwner);
        if (!isLocal && !IsWithinAudibleRange())
            return;

        // Proteção extra: nunca reproduz dois passos mais rápido que o cooldown mínimo
        if (Time.time - lastStepTime < minStepCooldown) return;
        lastStepTime = Time.time;

        float basePitch = isRunning ? runPitch : walkPitch;
        float randomPitch = basePitch + Random.Range(-pitchJitter, pitchJitter);
        float randomVolume = Mathf.Clamp01(stepVolume + Random.Range(-volumeVariation, volumeVariation));

        audioSource.pitch = Mathf.Clamp(randomPitch, 0.5f, 2.5f);

        AudioClip clipToPlay = GetClipForCurrentSurface();
        if (clipToPlay != null)
            audioSource.PlayOneShot(clipToPlay, randomVolume);
    }

    // ──────────────────────────────────────────────────────────────
    //  DETECÇÃO DE SUPERFÍCIE POR RAYCAST
    // ──────────────────────────────────────────────────────────────
    private AudioClip GetClipForCurrentSurface()
    {
        // Lança um Raycast para baixo a partir do centro do CharacterController
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundLayers))
        {
            if (surfaces != null)
            {
                foreach (FootstepSurface surface in surfaces)
                {
                    if (hit.collider.CompareTag(surface.surfaceTag) && surface.clips != null && surface.clips.Length > 0)
                        return PickRandom(surface.clips);
                }
            }
        }

        // Fallback: defaultClips ou procedural
        if (defaultClips != null && defaultClips.Length > 0)
            return PickRandom(defaultClips);

        if (proceduralStepClip == null)
            proceduralStepClip = CreateProceduralFootstepClip();
        return proceduralStepClip;
    }

    private AudioClip PickRandom(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        if (clips.Length == 1) return clips[0];

        int newIndex = Random.Range(0, clips.Length);
        if (newIndex == lastClipIndex)
            newIndex = (newIndex + 1) % clips.Length;
        lastClipIndex = newIndex;
        return clips[newIndex];
    }

    // ──────────────────────────────────────────────────────────────
    //  FALLBACK PROCEDURAL (sem nenhum clip atribuído)
    // ──────────────────────────────────────────────────────────────
    private AudioClip CreateProceduralFootstepClip()
    {
        int sampleRate = 44100;
        float duration = 0.08f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float noise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 28f);
            float lowFreq = Mathf.Sin(2f * Mathf.PI * 120f * (i / (float)sampleRate))
                            * Mathf.Exp(-t * 18f) * 0.5f;
            samples[i] = (noise * 0.5f + lowFreq) * (1f - t);
        }

        AudioClip clip = AudioClip.Create("ProceduralFootstep", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
