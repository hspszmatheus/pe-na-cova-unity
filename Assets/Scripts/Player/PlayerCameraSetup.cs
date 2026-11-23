using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class PlayerCameraSetup : NetworkBehaviour
{
    [Header("Câmera Local")]
    [SerializeField] private GameObject localCameraObject;

    [Header("Configuração de Mira (Aim Zoom)")]
    [SerializeField] private float normalFov = 65f;
    [SerializeField] private float aimFov = 48f;
    [SerializeField] private float normalRadius = 3.6f;
    [SerializeField] private float aimRadius = 2.4f;
    [SerializeField] private float zoomSpeed = 10f;

    [Header("Deslocamento de Mira TPS (Over-the-Shoulder)")]
    [Tooltip("Posição da composição de tela normal (0, 0 = personagem centralizado)")]
    [SerializeField] private Vector2 normalScreenPosition = Vector2.zero;
    [Tooltip("Posição da composição de tela ao mirar (-0.28, 0 = personagem à esquerda no estilo TPS)")]
    [SerializeField] private Vector2 aimScreenPosition = new Vector2(-0.28f, 0f);
    [Tooltip("Offset horizontal sobre o ombro direito na mira (em metros)")]
    [SerializeField] private float aimShoulderOffset = 0.80f;
    [Tooltip("Offset vertical adicional ao mirar")]
    [SerializeField] private float aimVerticalOffset = 0f;
    [Tooltip("Offset central normal do alvo")]
    [SerializeField] private Vector3 normalTargetOffset = new Vector3(0f, 1.4f, 0f);

    [Header("Proteção de Colisão com o Chão (Ground Clearance)")]
    [SerializeField] private float minGroundClearance = 0.35f;
    [SerializeField] private LayerMask groundLayerMask = 1; // Default

    [Header("Transparência ao Olhar Para Cima / Câmera Próxima")]
    [Tooltip("Distância da câmera ao centro do personagem onde a transparência começa a agir")]
    [SerializeField] private float fadeStartDistance = 2.4f;
    [Tooltip("Distância da câmera onde o personagem atinge a transparência máxima")]
    [SerializeField] private float fadeEndDistance = 1.1f;
    [Tooltip("Inclinação da câmera para cima (pitch) onde começa a ficar transparente")]
    [SerializeField] private float lookUpFadeStart = 0.15f; // ~8 graus para cima
    [Tooltip("Inclinação da câmera para cima (pitch) onde atinge transparência máxima")]
    [SerializeField] private float lookUpFadeEnd = 0.55f; // ~33 graus para cima
    [Tooltip("Opacidade mínima do personagem (ex: 0.25 = 25% visível / 75% transparente)")]
    [Range(0.05f, 1f)]
    [SerializeField] private float minAlpha = 0.25f;
    [Tooltip("Velocidade da transição suave de transparência")]
    [SerializeField] private float fadeSpeed = 12f;
    [Tooltip("Offset vertical do centro do personagem para cálculo de distância")]
    [SerializeField] private float characterFocusHeight = 1.35f;

    [Header("Efeito de Medo (Proximidade do Hunter)")]
    [Tooltip("Perfil de ruído 6D do Cinemachine para tremor de medo")]
    [SerializeField] private Unity.Cinemachine.NoiseSettings fearNoiseProfile;
    [Tooltip("Raio mínimo permitido durante o zoom do medo para não acionar transparência do personagem")]
    [SerializeField] private float minAllowedFearRadius = 2.55f;

    [Header("Câmera de Queda do Hunter (Intro Bola de Fogo)")]
    [Tooltip("Ângulo superior da câmera durante a queda da bola de fogo (em graus, mirando para o chão)")]
    [SerializeField] private float hunterFallSuperiorPitch = 82f;
    [Tooltip("Inclinação normal da câmera ao aterrissar no solo")]
    [SerializeField] private float hunterFallLandingPitch = 22f;

    private HunterKnockdownLogic knockdownLogic;
    private HunterSpawnIntroSequence hunterSpawnIntro;
    private PlayerController playerController;
    private Unity.Cinemachine.CinemachineCamera virtualCamera;
    private Unity.Cinemachine.CinemachineOrbitalFollow orbitalFollow;
    private Unity.Cinemachine.CinemachineRotationComposer rotationComposer;
    private Unity.Cinemachine.CinemachineBasicMultiChannelPerlin noisePerlin;
    private Unity.Cinemachine.CinemachineInputAxisController inputAxisController;
    private Unity.Cinemachine.CinemachineDeoccluder deoccluder;
    private Unity.Cinemachine.NoiseSettings runtimeFallbackNoiseProfile;
    private Camera unityCamera;
    private bool isAiming = false;
    private float currentAimProgress = 0f;
    private float savedYaw = 0f;
    private float introCameraOrbitAngle = 0f;

    private float fearFovDelta = 0f;
    private float fearRadiusDelta = 0f;
    private float fearShakeAmplitude = 0f;
    private float fearShakeFrequency = 1f;
    private float fearRollDegrees = 0f;
    private Vector2 fearScreenJitter = Vector2.zero;

    private readonly List<Renderer> fadeRenderers = new List<Renderer>();
    private MaterialPropertyBlock propBlock;
    private static readonly int CharacterAlphaId = Shader.PropertyToID("_CharacterAlpha");
    private float currentAlpha = 1f;

    // Âncora acústica centrada no personagem para áudio 3D simétrico e realista
    private GameObject audioListenerAnchor;
    private AudioListener cameraAudioListener;

    public Camera UnityCamera
    {
        get
        {
            if (unityCamera == null && localCameraObject != null)
            {
                unityCamera = localCameraObject.GetComponentInChildren<Camera>(true);
            }
            return unityCamera;
        }
    }

    public void SetAiming(bool aiming)
    {
        isAiming = aiming;
    }

    public void SetFearCameraState(
        float fovReduction,
        float radiusReduction,
        float shakeAmplitude,
        float shakeFrequency,
        float rollDegrees,
        Vector2 screenJitter)
    {
        fearFovDelta = Mathf.Max(0f, fovReduction);
        fearRadiusDelta = Mathf.Max(0f, radiusReduction);
        fearShakeAmplitude = Mathf.Max(0f, shakeAmplitude);
        fearShakeFrequency = Mathf.Max(0.05f, shakeFrequency);
        fearRollDegrees = rollDegrees;
        fearScreenJitter = screenJitter;
    }

    public void ResetFearCameraState()
    {
        fearFovDelta = 0f;
        fearRadiusDelta = 0f;
        fearShakeAmplitude = 0f;
        fearShakeFrequency = 1f;
        fearRollDegrees = 0f;
        fearScreenJitter = Vector2.zero;

        if (noisePerlin != null)
        {
            noisePerlin.AmplitudeGain = 0f;
        }
        if (virtualCamera != null)
        {
            virtualCamera.Lens.Dutch = 0f;
        }
    }

    void Awake()
    {
        Physics.queriesHitBackfaces = true;
        gameObject.tag = "Player";
        knockdownLogic = GetComponent<HunterKnockdownLogic>();
        propBlock = new MaterialPropertyBlock();
        CacheFadeRenderers();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            if (!InGamePauseMenuController.IsMenuOpen && !InGamePauseMenuController.IsGameOverVisible)
            {
                InGamePauseMenuController.LockCursor();
            }

            if (localCameraObject != null)
            {
                localCameraObject.SetActive(true);
                var cam = localCameraObject.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    cam.tag = "MainCamera";
                    unityCamera = cam;
                    cameraAudioListener = cam.GetComponent<AudioListener>();
                    if (cameraAudioListener != null)
                    {
                        cameraAudioListener.enabled = false;
                    }
                }

                // Cria uma âncora para o AudioListener centralizada no tórax do personagem.
                // Isso garante que distâncias e raio de audição sejam 100% simétricos em 360°,
                // sem distorções causadas pela distância de recuo da câmera em terceira pessoa.
                if (audioListenerAnchor == null)
                {
                    audioListenerAnchor = new GameObject("PlayerAudioListenerAnchor");
                    audioListenerAnchor.transform.SetParent(transform, false);
                    audioListenerAnchor.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                    audioListenerAnchor.AddComponent<AudioListener>();
                }
            }
            CacheFadeRenderers();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            InGamePauseMenuController.UnlockCursor();
        }
        if (audioListenerAnchor != null)
        {
            Destroy(audioListenerAnchor);
            audioListenerAnchor = null;
        }
        if (cameraAudioListener != null)
        {
            cameraAudioListener.enabled = true;
        }
        ResetCharacterTransparency();
        ResetFearCameraState();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (IsOwner)
        {
            InGamePauseMenuController.UnlockCursor();
        }
        if (audioListenerAnchor != null)
        {
            Destroy(audioListenerAnchor);
            audioListenerAnchor = null;
        }
        if (cameraAudioListener != null)
        {
            cameraAudioListener.enabled = true;
        }
        if (runtimeFallbackNoiseProfile != null)
        {
            Destroy(runtimeFallbackNoiseProfile);
            runtimeFallbackNoiseProfile = null;
        }
        base.OnDestroy();
    }

    private void OnDisable()
    {
        if (IsOwner)
        {
            InGamePauseMenuController.UnlockCursor();
        }
        ResetCharacterTransparency();
        ResetFearCameraState();
        if (inputAxisController != null)
        {
            inputAxisController.enabled = true;
        }
        if (deoccluder != null)
        {
            deoccluder.enabled = true;
        }
    }

    private Unity.Cinemachine.NoiseSettings EnsureFearNoiseProfile()
    {
        if (fearNoiseProfile != null) return fearNoiseProfile;

#if UNITY_EDITOR
        fearNoiseProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.Cinemachine.NoiseSettings>(
            "Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset");
        if (fearNoiseProfile != null) return fearNoiseProfile;
#endif

        if (runtimeFallbackNoiseProfile == null)
        {
            runtimeFallbackNoiseProfile = ScriptableObject.CreateInstance<Unity.Cinemachine.NoiseSettings>();
            runtimeFallbackNoiseProfile.hideFlags = HideFlags.HideAndDontSave;
            runtimeFallbackNoiseProfile.PositionNoise = new[]
            {
                MakeNoiseBand(3.2f, 0.011f, true, 1.9f, 0.059f, true, 3.33f, 0.021f, true),
                MakeNoiseBand(7.7f, 0.009f, true, 9.1f, 0.04f, false, 9.22f, 0.009f, true),
                MakeNoiseBand(51.51f, 0.002f, true, 55.54f, 0.05f, true, 58.55f, 0.017f, true)
            };
            runtimeFallbackNoiseProfile.OrientationNoise = new[]
            {
                MakeNoiseBand(5.83f, 0.09f, true, 1.8f, 0.059f, true, 2.38f, 0.017f, true),
                MakeNoiseBand(9.17f, 0.14f, true, 11.35f, 0.041f, true, 10.52f, 0.009f, true),
                MakeNoiseBand(57.17f, 0.15f, true, 54.17f, 0.048f, true, 63.76f, 0.016f, true)
            };
        }
        return runtimeFallbackNoiseProfile;
    }

    private static Unity.Cinemachine.NoiseSettings.TransformNoiseParams MakeNoiseBand(
        float fx, float ax, bool cx,
        float fy, float ay, bool cy,
        float fz, float az, bool cz)
    {
        return new Unity.Cinemachine.NoiseSettings.TransformNoiseParams
        {
            X = new Unity.Cinemachine.NoiseSettings.NoiseParams { Frequency = fx, Amplitude = ax, Constant = cx },
            Y = new Unity.Cinemachine.NoiseSettings.NoiseParams { Frequency = fy, Amplitude = ay, Constant = cy },
            Z = new Unity.Cinemachine.NoiseSettings.NoiseParams { Frequency = fz, Amplitude = az, Constant = cz }
        };
    }

    private void CacheFadeRenderers()
    {
        fadeRenderers.Clear();
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in allRenderers)
        {
            if (r == null) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;

            bool hasAlphaProp = false;
            var mats = r.sharedMaterials;
            if (mats != null)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].HasProperty(CharacterAlphaId))
                    {
                        hasAlphaProp = true;
                        break;
                    }
                }
            }

            if (hasAlphaProp)
            {
                fadeRenderers.Add(r);
            }
        }
    }

    void LateUpdate()
    {
        if (!IsOwner || localCameraObject == null) return;

        if (knockdownLogic == null)
        {
            knockdownLogic = GetComponent<HunterKnockdownLogic>();
        }

        if (hunterSpawnIntro == null)
        {
            hunterSpawnIntro = GetComponent<HunterSpawnIntroSequence>();
        }

        if (virtualCamera == null)
        {
            virtualCamera = localCameraObject.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>(true);
        }

        if (orbitalFollow == null && virtualCamera != null)
        {
            orbitalFollow = virtualCamera.GetComponent<Unity.Cinemachine.CinemachineOrbitalFollow>();
        }

        if (inputAxisController == null && virtualCamera != null)
        {
            inputAxisController = virtualCamera.GetComponent<Unity.Cinemachine.CinemachineInputAxisController>();
        }

        if (deoccluder == null && virtualCamera != null)
        {
            deoccluder = virtualCamera.GetComponent<Unity.Cinemachine.CinemachineDeoccluder>();
            if (deoccluder != null)
            {
                deoccluder.IgnoreTag = "Player";
            }
        }

        if (unityCamera == null)
        {
            unityCamera = localCameraObject.GetComponentInChildren<Camera>(true);
        }

        if (hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro)
        {
            // Desativa entrada de mouse durante a descida da bola de fogo para manter o enquadramento superior estabilizado
            if (inputAxisController != null && inputAxisController.enabled)
            {
                inputAxisController.enabled = false;
            }

            // A câmera gira suavemente ao redor da bola de fogo que está despencando dos céus
            introCameraOrbitAngle += 26f * Time.deltaTime;
            savedYaw = transform.eulerAngles.y + introCameraOrbitAngle;
            localCameraObject.transform.rotation = Quaternion.Euler(0f, savedYaw, 0f);

            if (orbitalFollow != null)
            {
                // Garante que o limite superior da órbita permita o ângulo superior desejado sem corte
                var vRange = orbitalFollow.VerticalAxis.Range;
                if (vRange.y < hunterFallSuperiorPitch + 5f)
                {
                    vRange.y = hunterFallSuperiorPitch + 8f;
                    orbitalFollow.VerticalAxis.Range = vRange;
                }

                orbitalFollow.HorizontalAxis.Value = introCameraOrbitAngle;

                // Ângulo superior (inclinado para baixo em direção ao chão) para focar na cidade/cenário
                // e esconder completamente o horizonte e qualquer área vazia fora do mapa.
                // A transição para o ângulo de aterrissagem ocorre apenas nos instantes finais (últimos 15%),
                // quando o Hunter já se encontra muito próximo do chão entre as construções e árvores.
                float progress = hunterSpawnIntro.FallProgress;
                float blend = Mathf.Clamp01((progress - 0.85f) / 0.15f);
                float smoothBlend = Mathf.SmoothStep(0f, 1f, blend);
                float currentPitch = Mathf.Lerp(hunterFallSuperiorPitch, hunterFallLandingPitch, smoothBlend);
                orbitalFollow.VerticalAxis.Value = currentPitch;
            }
        }
        else if (InGamePauseMenuController.IsMenuOpen || InGamePauseMenuController.IsLeavingMatch || InGamePauseMenuController.IsGameOverVisible)
        {
            if (inputAxisController != null && inputAxisController.enabled)
            {
                inputAxisController.enabled = false;
            }

            isAiming = false;
            // Enquanto o menu de pausa estiver aberto, congela a rotação da câmera no último ângulo salvo
            localCameraObject.transform.rotation = Quaternion.Euler(0f, savedYaw, 0f);
        }
        else if (knockdownLogic != null && knockdownLogic.IsKnockedDown)
        {
            // O jogador do Hunter mantém o controle total da rotação da câmera durante o voo e queda para ver quem o chutou
            if (inputAxisController != null && !inputAxisController.enabled)
            {
                inputAxisController.enabled = true;
            }

            // O deoccluder permanece ativado para impedir categoricamente que a câmera entre em prédios.
            // Árvores, postes e veículos estão na camada EnvironmentProp e são ignorados pelo deoccluder.
            // O corpo do próprio jogador (tag 'Player') também é ignorado para nunca colidir com o Hunter.
            if (deoccluder != null)
            {
                if (!deoccluder.enabled) deoccluder.enabled = true;
                if (deoccluder.IgnoreTag != "Player") deoccluder.IgnoreTag = "Player";
            }

            isAiming = false;
            introCameraOrbitAngle = 0f;

            // Mantém a base da câmera nivelada com o horizonte (Pitch=0, Roll=0), evitando girar de ponta-cabeça com o corpo capotando
            float currentCamYaw = unityCamera != null ? unityCamera.transform.eulerAngles.y : savedYaw;
            savedYaw = currentCamYaw;
            localCameraObject.transform.rotation = Quaternion.Euler(0f, currentCamYaw, 0f);
        }
        else
        {
            if (inputAxisController != null && !inputAxisController.enabled)
            {
                inputAxisController.enabled = true;
            }

            // Reativa a colisão de câmera normal quando o personagem está de pé
            if (deoccluder != null && !deoccluder.enabled)
            {
                deoccluder.enabled = true;
            }

            introCameraOrbitAngle = 0f;
            // Em estado normal, acompanha a rotação padrão do personagem
            savedYaw = transform.eulerAngles.y;
            localCameraObject.transform.localRotation = Quaternion.identity;
        }

        // Suporte a zoom suave de mira e enquadramento TPS over-the-shoulder
        if (localCameraObject != null)
        {
            if (virtualCamera != null)
            {
                if (rotationComposer == null)
                {
                    rotationComposer = virtualCamera.GetComponent<Unity.Cinemachine.CinemachineRotationComposer>();
                }

                if (noisePerlin == null)
                {
                    noisePerlin = virtualCamera.GetComponent<Unity.Cinemachine.CinemachineBasicMultiChannelPerlin>();
                    if (noisePerlin == null && (fearShakeAmplitude > 0.001f || fearNoiseProfile != null))
                    {
                        noisePerlin = virtualCamera.gameObject.AddComponent<Unity.Cinemachine.CinemachineBasicMultiChannelPerlin>();
                    }
                }
                if (noisePerlin != null && noisePerlin.NoiseProfile == null)
                {
                    noisePerlin.NoiseProfile = EnsureFearNoiseProfile();
                }
            }
        }

        if (virtualCamera != null)
        {
            float targetAimProgress = isAiming ? 1f : 0f;
            currentAimProgress = Mathf.Lerp(currentAimProgress, targetAimProgress, zoomSpeed * Time.deltaTime);

            // 1. Zoom de Campo de Visão (FOV) + Zoom/Inclinação de Medo (Hunter próximo)
            float baseFov = Mathf.Lerp(normalFov, aimFov, currentAimProgress);
            virtualCamera.Lens.FieldOfView = Mathf.Clamp(baseFov - fearFovDelta, 28f, 95f);
            virtualCamera.Lens.Dutch = fearRollDegrees;

            // 1b. Tremor Perlin 6D sincronizado com o sistema de medo
            if (noisePerlin != null)
            {
                noisePerlin.AmplitudeGain = fearShakeAmplitude;
                noisePerlin.FrequencyGain = fearShakeFrequency;
            }

            // 2. Raio da Órbita e deslocamento sobre o ombro direito
            if (orbitalFollow != null)
            {
                float baseRadius = Mathf.Lerp(normalRadius, aimRadius, currentAimProgress);
                float minAllowed = Mathf.Min(baseRadius, minAllowedFearRadius);
                orbitalFollow.Radius = fearRadiusDelta > 0.001f
                    ? Mathf.Max(minAllowed, baseRadius - fearRadiusDelta)
                    : baseRadius;
                if (orbitalFollow.OrbitStyle == Unity.Cinemachine.CinemachineOrbitalFollow.OrbitStyles.ThreeRing)
                {
                    var centerOrbit = orbitalFollow.Orbits.Center;
                    centerOrbit.Radius = orbitalFollow.Radius;
                    orbitalFollow.Orbits.Center = centerOrbit;
                }

                Vector3 camRight = unityCamera != null
                    ? Vector3.ProjectOnPlane(unityCamera.transform.right, Vector3.up).normalized
                    : transform.right;

                if (playerController == null) playerController = GetComponent<PlayerController>();
                Vector3 baseOffset = normalTargetOffset;
                if (knockdownLogic != null && knockdownLogic.IsKnockedDown)
                {
                    baseOffset = new Vector3(0f, 0.9f, 0f);
                }
                else if (playerController != null && playerController.IsGhost)
                {
                    baseOffset = new Vector3(0f, 0.95f, 0f);
                }

                Vector3 dynamicOffset = baseOffset 
                    + camRight * (aimShoulderOffset * currentAimProgress)
                    + Vector3.up * (aimVerticalOffset * currentAimProgress);

                orbitalFollow.TargetOffset = dynamicOffset;
            }

            // 3. Enquadramento da tela (coloca o personagem à esquerda, liberando o centro da tela para mira)
            if (rotationComposer != null)
            {
                var comp = rotationComposer.Composition;
                comp.ScreenPosition = Vector2.Lerp(normalScreenPosition, aimScreenPosition, currentAimProgress) + fearScreenJitter;
                rotationComposer.Composition = comp;

                // rotationComposer foca no centro normal do personagem (normalTargetOffset)
                // para que ScreenPosition posicione o Hunter consistentemente à esquerda da tela,
                // enquanto orbitalFollow.TargetOffset desloca a câmera sobre o ombro direito, liberando o retículo central.
                Vector3 baseOffset = normalTargetOffset;
                if (knockdownLogic != null && knockdownLogic.IsKnockedDown)
                {
                    baseOffset = new Vector3(0f, 0.9f, 0f);
                }
                else if (playerController != null && playerController.IsGhost)
                {
                    baseOffset = new Vector3(0f, 0.95f, 0f);
                }
                rotationComposer.TargetOffset = baseOffset;
            }
        }

        // Proteção contra colisão com o chão: impede fisicamente que a câmera atravesse o piso
        if (unityCamera == null && localCameraObject != null)
        {
            unityCamera = localCameraObject.GetComponentInChildren<Camera>(true);
        }

        if (unityCamera != null && unityCamera.enabled)
        {
            Vector3 camPos = unityCamera.transform.position;
            // Proteção contra colisão com o chão: impede fisicamente que a câmera atravesse o piso
            if (Physics.Raycast(camPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                float safeY = hit.point.y + minGroundClearance;
                if (camPos.y < safeY)
                {
                    unityCamera.transform.position = new Vector3(camPos.x, safeY, camPos.z);
                }
            }
        }

        // Sincroniza a orientação acústica da âncora do AudioListener com a rotação da câmera
        if (audioListenerAnchor != null && unityCamera != null)
        {
            audioListenerAnchor.transform.rotation = unityCamera.transform.rotation;
        }

        // Atualiza transparência suave do personagem ao olhar para cima ou quando a câmera estiver muito próxima
        UpdateCharacterTransparency();
    }

    private void UpdateCharacterTransparency()
    {
        if (unityCamera == null || !unityCamera.enabled) return;

        if (fadeRenderers.Count == 0)
        {
            CacheFadeRenderers();
            if (fadeRenderers.Count == 0) return;
        }

        Vector3 camPos = unityCamera.transform.position;
        Vector3 focusPoint = transform.position + Vector3.up * characterFocusHeight;
        float distanceToCharacter = Vector3.Distance(camPos, focusPoint);

        // Fator por proximidade física da câmera ao personagem
        float distFactor = Mathf.InverseLerp(fadeEndDistance, fadeStartDistance, distanceToCharacter);

        // Fator por inclinação da câmera para cima (pitch up)
        // Ao olhar para cima, cam.transform.forward aponta para cima (Y positivo)
        float forwardY = unityCamera.transform.forward.y;
        float pitchFactor = Mathf.InverseLerp(lookUpFadeEnd, lookUpFadeStart, forwardY);

        // Se a câmera estiver muito próxima OU estiver olhando para cima, calcula a transparência desejada
        float targetNormalized = Mathf.Min(distFactor, pitchFactor);
        float targetAlpha = Mathf.Lerp(minAlpha, 1f, targetNormalized);

        if (targetAlpha > 0.99f)
        {
            targetAlpha = 1f;
        }

        currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);
        if (currentAlpha > 0.995f)
        {
            currentAlpha = 1f;
        }

        if (propBlock == null)
        {
            propBlock = new MaterialPropertyBlock();
        }

        propBlock.SetFloat(CharacterAlphaId, currentAlpha);
        for (int i = 0; i < fadeRenderers.Count; i++)
        {
            var r = fadeRenderers[i];
            if (r != null)
            {
                r.SetPropertyBlock(propBlock);
            }
        }
    }

    private void ResetCharacterTransparency()
    {
        currentAlpha = 1f;
        if (fadeRenderers.Count == 0) return;
        if (propBlock == null) propBlock = new MaterialPropertyBlock();
        propBlock.SetFloat(CharacterAlphaId, 1f);
        for (int i = 0; i < fadeRenderers.Count; i++)
        {
            var r = fadeRenderers[i];
            if (r != null)
            {
                r.SetPropertyBlock(propBlock);
            }
        }
    }
}
