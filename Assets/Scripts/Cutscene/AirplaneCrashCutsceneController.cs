using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controla a cutscene cinemática de abertura com ar cômico antes da partida começar:
/// - Enredo inspirado em 'Premonição' (Final Destination):
///   O voo de passageiros sofre pane catastrófica, oscila cômico pelo céu enfumaçado,
///   cai nas redondezas da cidade e explode violentamente!
/// - Os jogadores humanos (sobreviventes) conseguem milagrosamente escapar vivos do avião em chamas,
///   tombam/rolam pelo asfalto e correm em pânico para dentro da cidade para se esconderem.
/// - Após a fuga dos sobreviventes, a Morte (Hunter) desce dos céus envolta em uma colossal bola de fogo
///   para cobrar a vida de quem enganou o destino.
/// - Os destroços do avião em chamas permanecem no local do impacto durante toda a partida
///   como elemento físico e atmosférico do cenário.
/// </summary>
public class AirplaneCrashCutsceneController : NetworkBehaviour
{
    public static AirplaneCrashCutsceneController Instance { get; private set; }

    [Header("Configuração de Duração")]
    [Tooltip("Duração total da cutscene em segundos")]
    [SerializeField] private float cutsceneDuration = 10.8f;

    [Header("Prefabs de Cenário e Atores")]
    [Tooltip("Prefab do avião de passageiros (Aircraft Pack)")]
    [SerializeField] private GameObject airplanePrefab;

    [Tooltip("Prefab do jogador para criação dos dublês sobreviventes")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Prefabs de Efeitos Visuais (VFX)")]
    [SerializeField] private GameObject smokeVfxPrefab;
    [SerializeField] private GameObject fireVfxPrefab;
    [SerializeField] private GameObject groundFireVfxPrefab;
    [SerializeField] private GameObject explosionImpactVfxPrefab;
    [SerializeField] private GameObject fireHitVfxPrefab;

    [Header("Efeitos Sonoros (SFX)")]
    [SerializeField] private AudioClip windDescendClip;
    [SerializeField] private AudioClip chargeDroneClip;
    [SerializeField] private AudioClip explosionClip;
    [SerializeField] private AudioClip crashImpactClip;
    [SerializeField] private AudioClip landingCrunchClip;
    [SerializeField] private AudioClip comedicBodyThudClip;
    [SerializeField] private AudioClip hatchPopClip;
    [SerializeField] private AudioClip secondaryPopClip;
    [SerializeField] private AudioClip scrambleFleeClip;

    [Header("Local de Impacto")]
    [Tooltip("Posição final do avião no solo na área oeste junto aos boundaries")]
    [SerializeField] private Vector3 crashSitePosition = new Vector3(-86.55f, 0.40f, -35.07f);

    [Tooltip("Rotação final do avião descansando sobre a vegetação/rochas")]
    [SerializeField] private Vector3 crashSiteRotationEuler = new Vector3(11.0f, 47.998f, 351.0f);

    [Tooltip("Ponto inicial do voo no céu (fora do cenário)")]
    [SerializeField] private Vector3 flightStartPosition = new Vector3(-190.59f, 65.0f, -128.75f);

    [Tooltip("Ponto intermediário da curva do voo")]
    [SerializeField] private Vector3 flightMidPosition = new Vector3(-138.57f, 26.0f, -81.91f);

    [Header("Menu / Cartoon UI Theme")]
    [SerializeField] private Texture2D cartoonPanelCream;
    [SerializeField] private Texture2D cartoonSliderTrack;
    [SerializeField] private Texture2D cartoonBtnBlue;
    [SerializeField] private Texture2D cartoonBtnOrange;
    [SerializeField] private Texture2D cartoonBtnGreen;
    [SerializeField] private Texture2D cartoonBadgeBg;

    // Estado da cutscene
    private bool isCutsceneActive = false;
    private bool hasPlayedCutscene = false;
    public bool IsCutsceneActive => isCutsceneActive;
    public bool HasPlayedCutscene => hasPlayedCutscene;
    public static bool IsAnyCutsceneRunning => Instance != null && Instance.isCutsceneActive;

    // Componentes de cena em execução
    private Camera cutsceneCamera;
    private AudioListener cutsceneAudioListener;
    private AudioSource audioSourceGlobal;
    private AudioSource audioSourcePlane;
    private GameObject activePlaneInstance;
    private Transform propellerL;
    private Transform propellerR;
    private GameObject planeSmokeEmitter;
    private GameObject planeFireEmitter;

    // Atores dublês dos sobreviventes
    private readonly List<GameObject> stuntActors = new List<GameObject>();
    private readonly List<Animator> stuntAnimators = new List<Animator>();

    // Câmera e trauma (screen shake)
    private static readonly Vector3 DefaultCamPosPhase1 = new Vector3(-68.0f, 16.0f, -18.0f);
    private static readonly Vector3 DefaultCamLookPhase1 = new Vector3(-155.0f, 45.0f, -98.0f);
    private float cameraTrauma = 0f;
    private Vector3 currentCamPosTarget = DefaultCamPosPhase1;
    private Vector3 currentCamLookTarget = DefaultCamLookPhase1;
    private float currentFovTarget = 52f;

    // Sistema Unânime de Pular Cutscene (Segurar Barra de Espaço)
    public const float SpaceHoldDurationToSkip = 1.5f;
    private float localSpaceHoldTimer = 0f;
    private bool isLocalPlayerReadyToSkip = false;
    private int syncedReadyCount = 0;
    private int syncedTotalCount = 1;
    private readonly HashSet<ulong> readySkipClientIds = new HashSet<ulong>();
    public float LocalHoldProgress => Mathf.Clamp01(localSpaceHoldTimer / SpaceHoldDurationToSkip);
    public int SyncedReadyCount => syncedReadyCount;
    public int SyncedTotalCount => syncedTotalCount;

    // Transição Cinemática entre o fim da cutscene e o início da gameplay (Fade suave)
    private float transitionFadeAlpha = 0f;
    private bool isTransitioningToGameplay = false;
    public bool IsTransitioningToGameplay => isTransitioningToGameplay;

    public event Action OnCutsceneFinished;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        CleanupAllStuntSurvivors();
        EnsureAssetsLoaded();
        SetupCutsceneCamera();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        CleanupAllStuntSurvivors();
        if (Instance == this) Instance = null;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnectedDuringCutscene;
        }
    }

    private void EnsureAssetsLoaded()
    {
#if UNITY_EDITOR
        if (airplanePrefab == null)
            airplanePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Polyeler/Simple Aircraft Pack/Prefabs/Passenger Aircraft V1.prefab");

        if (playerPrefab == null)
            playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");

        if (smokeVfxPrefab == null)
            smokeVfxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFXPACK_FIRE_WALLCOEUR/Prefab/VFX_BlackSmoke.prefab");

        if (fireVfxPrefab == null)
            fireVfxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFXPACK_FIRE_WALLCOEUR/Prefab/VFX_Fire.prefab");

        if (groundFireVfxPrefab == null)
            groundFireVfxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFXPACK_FIRE_WALLCOEUR/Prefab/VFX_GroundFire_Circle.prefab");

        if (explosionImpactVfxPrefab == null)
            explosionImpactVfxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Hunter_Spawn_Impact.prefab");

        if (fireHitVfxPrefab == null)
            fireHitVfxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Fire_Hit_02.prefab");

        if (windDescendClip == null)
            windDescendClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/25_Wind_01.wav");

        if (chargeDroneClip == null)
            chargeDroneClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/45_Charge_05.wav");

        if (explosionClip == null)
            explosionClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/04_Fire_explosion_04_medium.wav");

        if (crashImpactClip == null)
            crashImpactClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/18_Thunder_02.wav");

        if (landingCrunchClip == null)
            landingCrunchClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/12_Player_Movement_SFX/45_Landing_01.wav");

        if (comedicBodyThudClip == null)
            comedicBodyThudClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_Battle_SFX/15_Impact_flesh_02.wav");

        if (hatchPopClip == null)
            hatchPopClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-04.wav");

        if (secondaryPopClip == null)
            secondaryPopClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-30.wav");

        if (scrambleFleeClip == null)
            scrambleFleeClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_Battle_SFX/51_Flee_02.wav");

        if (cartoonPanelCream == null)
            cartoonPanelCream = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/cartoon_panel_cream.png");
        if (cartoonSliderTrack == null)
            cartoonSliderTrack = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/cartoon_slider_track.png");
        if (cartoonBtnBlue == null)
            cartoonBtnBlue = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/cartoon_btn_blue.png");
        if (cartoonBtnOrange == null)
            cartoonBtnOrange = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/cartoon_btn_orange.png");
        if (cartoonBtnGreen == null)
            cartoonBtnGreen = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/cartoon_btn_green.png");
        if (cartoonBadgeBg == null)
            cartoonBadgeBg = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/cartoon_list_bg.png");
#endif
    }

    private void SetupCutsceneCamera()
    {
        Transform camChild = transform.Find("CutsceneCamera");
        if (camChild == null)
        {
            GameObject camObj = new GameObject("CutsceneCamera");
            camObj.transform.SetParent(transform, false);
            cutsceneCamera = camObj.AddComponent<Camera>();
            camChild = camObj.transform;
        }
        else
        {
            cutsceneCamera = camChild.GetComponent<Camera>();
        }

        cutsceneCamera.clearFlags = CameraClearFlags.Skybox;
        cutsceneCamera.depth = 20; // Sobrepõe qualquer câmera padrão
        cutsceneCamera.cullingMask &= ~(1 << CityBoundaryVisualController.CityBoundaryRenderingLayerIndex);
        cutsceneCamera.transform.position = DefaultCamPosPhase1;
        cutsceneCamera.transform.LookAt(DefaultCamLookPhase1);
        cutsceneCamera.fieldOfView = 52f;
        cutsceneCamera.enabled = false;

        cutsceneAudioListener = camChild.GetComponent<AudioListener>();
        if (cutsceneAudioListener == null)
        {
            cutsceneAudioListener = camChild.gameObject.AddComponent<AudioListener>();
        }
        cutsceneAudioListener.enabled = false;

        audioSourceGlobal = gameObject.GetComponent<AudioSource>();
        if (audioSourceGlobal == null)
        {
            audioSourceGlobal = gameObject.AddComponent<AudioSource>();
        }
        audioSourceGlobal.playOnAwake = false;
        audioSourceGlobal.spatialBlend = 0f; // 2D som de tela
    }

    private void Start()
    {
        // Se a cena foi aberta diretamente no Editor sem rede ativa, executa prévia local automática
        if (!isCutsceneActive && !hasPlayedCutscene)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                Debug.Log("[AirplaneCutscene] Modo standalone / prévia do Editor detectado. Iniciando cutscene local.");
                StartCoroutine(EditorDirectPlayRoutine());
            }
        }
    }

    private IEnumerator EditorDirectPlayRoutine()
    {
        yield return new WaitForSeconds(0.3f);
        StartCutsceneLocally(new int[] { 0, 1, 2, 3 });
    }

    /// <summary>
    /// Dispara a cutscene em todos os clientes conectados de forma perfeitamente sincronizada.
    /// Invocado pelo Servidor / Host via GameSpawner.
    /// </summary>
    [ClientRpc]
    public void StartCutsceneClientRpc(int[] survivorModelIndices)
    {
        StartCutsceneLocally(survivorModelIndices);
    }

    public void StartCutsceneLocally(int[] survivorModelIndices)
    {
        if (isCutsceneActive) return;

        EnsureAssetsLoaded();
        CleanupAllStuntSurvivors();
        if (cutsceneCamera == null) SetupCutsceneCamera();
        cutsceneCamera.gameObject.SetActive(true);
        isCutsceneActive = true;
        hasPlayedCutscene = true;

        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.SetHudVisible(false);
        }

        if (!InGamePauseMenuController.IsMenuOpen && !InGamePauseMenuController.IsGameOverVisible)
        {
            InGamePauseMenuController.LockCursor();
        }

        currentCamPosTarget = DefaultCamPosPhase1;
        currentCamLookTarget = DefaultCamLookPhase1;
        currentFovTarget = 52f;

        if (cutsceneCamera != null)
        {
            cutsceneCamera.transform.position = currentCamPosTarget;
            cutsceneCamera.transform.LookAt(currentCamLookTarget);
            cutsceneCamera.fieldOfView = currentFovTarget;
            cutsceneCamera.enabled = true;
            cutsceneCamera.tag = "MainCamera";
        }
        if (cutsceneAudioListener != null)
        {
            cutsceneAudioListener.enabled = true;
        }

        // Dispensa suavemente a tela de carregamento sincronizada revelando a cutscene de abertura
        GameLoadingScreenController.Instance?.Hide(fadeDuration: 0.6f, ignoreMinDisplayTime: true);

        // Reseta contadores e votos para pular a cutscene
        localSpaceHoldTimer = 0f;
        isLocalPlayerReadyToSkip = false;
        readySkipClientIds.Clear();
        syncedReadyCount = 0;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                syncedTotalCount = Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count);
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnectedDuringCutscene;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnServerClientDisconnectedDuringCutscene;
                UpdateSkipStatusClientRpc(0, syncedTotalCount);
            }
        }
        else
        {
            syncedTotalCount = 1;
        }

        // Desativa as bolinhas/pontinhos do boundary durante toda a cutscene cinemática
        var boundaryVisual = FindFirstObjectByType<CityBoundaryVisualController>();
        if (boundaryVisual != null)
        {
            boundaryVisual.SetVisualsSuppressed(true);
        }

        StartCoroutine(CutsceneSequenceRoutine(survivorModelIndices));
    }

    private void Update()
    {
        if (!isCutsceneActive) return;

        UpdateSpaceHoldSkip();
    }

    /// <summary>
    /// Gerencia o preenchimento da barra ao segurar a barra de espaço para pular a cutscene.
    /// Exige que TODOS os jogadores da partida completem a barra para pular em consenso.
    /// </summary>
    private void UpdateSpaceHoldSkip()
    {
        // Se este jogador local já completou sua barra, seu voto está assegurado
        if (isLocalPlayerReadyToSkip) return;

        bool isSpacePressed = false;
        var kb = Keyboard.current;
        if (kb != null)
        {
            isSpacePressed = kb.spaceKey.isPressed;
        }

        if (isSpacePressed)
        {
            localSpaceHoldTimer += Time.deltaTime;
            if (localSpaceHoldTimer >= SpaceHoldDurationToSkip)
            {
                localSpaceHoldTimer = SpaceHoldDurationToSkip;
                isLocalPlayerReadyToSkip = true;
                SubmitLocalSkipVote(true);
            }
        }
        else
        {
            // Se soltou a barra de espaço antes de completar, a barra esvazia suavemente
            if (localSpaceHoldTimer > 0f)
            {
                localSpaceHoldTimer = Mathf.Max(0f, localSpaceHoldTimer - Time.deltaTime * 3.5f);
            }
        }
    }

    private void SubmitLocalSkipVote(bool ready)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                RegisterServerSkipVote(NetworkManager.Singleton.LocalClientId, ready);
            }
            else
            {
                SubmitSkipVoteServerRpc(ready);
            }
        }
        else
        {
            // Standalone / modo offline / teste de Editor
            syncedReadyCount = ready ? 1 : 0;
            syncedTotalCount = 1;
            if (syncedReadyCount >= syncedTotalCount)
            {
                StopAllCoroutines();
                StartCoroutine(TransitionToGameplayRoutine(fastFade: true));
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitSkipVoteServerRpc(bool ready, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        RegisterServerSkipVote(clientId, ready);
    }

    private void RegisterServerSkipVote(ulong clientId, bool ready)
    {
        if (!IsServer) return;

        if (ready)
        {
            readySkipClientIds.Add(clientId);
        }
        else
        {
            readySkipClientIds.Remove(clientId);
        }

        int totalCount = NetworkManager.Singleton != null ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count) : 1;
        int readyCount = readySkipClientIds.Count;

        syncedReadyCount = readyCount;
        syncedTotalCount = totalCount;
        UpdateSkipStatusClientRpc(readyCount, totalCount);

        // Se todos os jogadores completaram a barra de espaço, a cutscene é pulada para todos
        if (readyCount >= totalCount && totalCount > 0)
        {
            FastForwardCutsceneClientRpc();
        }
    }

    private void OnServerClientDisconnectedDuringCutscene(ulong clientId)
    {
        if (!IsServer || !isCutsceneActive) return;

        readySkipClientIds.Remove(clientId);
        int totalCount = NetworkManager.Singleton != null ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count) : 1;
        int readyCount = readySkipClientIds.Count;

        syncedReadyCount = readyCount;
        syncedTotalCount = totalCount;
        UpdateSkipStatusClientRpc(readyCount, totalCount);

        if (readyCount >= totalCount && totalCount > 0)
        {
            FastForwardCutsceneClientRpc();
        }
    }

    [ClientRpc]
    private void UpdateSkipStatusClientRpc(int readyCount, int totalCount)
    {
        syncedReadyCount = readyCount;
        syncedTotalCount = totalCount;
    }

    [ClientRpc]
    private void FastForwardCutsceneClientRpc()
    {
        if (!isCutsceneActive) return;
        StopAllCoroutines();
        StartCoroutine(TransitionToGameplayRoutine(fastFade: true));
    }

    private void LateUpdate()
    {
        if (!isCutsceneActive || cutsceneCamera == null) return;

        // Aplica tremor de câmera (Trauma Shake)
        if (cameraTrauma > 0f)
        {
            cameraTrauma = Mathf.Max(0f, cameraTrauma - Time.deltaTime * 0.9f);
            float shakePower = cameraTrauma * cameraTrauma;
            float shakeX = (Mathf.PerlinNoise(Time.time * 28f, 0f) - 0.5f) * 1.8f * shakePower;
            float shakeY = (Mathf.PerlinNoise(0f, Time.time * 28f) - 0.5f) * 1.8f * shakePower;
            float shakeRot = (Mathf.PerlinNoise(Time.time * 35f, Time.time * 35f) - 0.5f) * 8.0f * shakePower;

            cutsceneCamera.transform.position = currentCamPosTarget + new Vector3(shakeX, shakeY, 0f);
            Vector3 lookDir = currentCamLookTarget - currentCamPosTarget;
            if (lookDir != Vector3.zero)
            {
                cutsceneCamera.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up) * Quaternion.Euler(0f, 0f, shakeRot);
            }
        }
        else
        {
            cutsceneCamera.transform.position = currentCamPosTarget;
            Vector3 lookDir = currentCamLookTarget - currentCamPosTarget;
            if (lookDir != Vector3.zero)
            {
                cutsceneCamera.transform.LookAt(currentCamLookTarget);
            }
        }

        cutsceneCamera.fieldOfView = currentFovTarget;
    }

    private IEnumerator CutsceneSequenceRoutine(int[] survivorModelIndices)
    {
        // =========================================================================
        // 1. LOCALIZA OU INSTANCIA O AVIÃO E EFEITOS
        // =========================================================================
        if (activePlaneInstance == null)
        {
            var existingPlane = GameObject.Find("Cutscene_Airplane_V1");
            if (existingPlane != null)
            {
                activePlaneInstance = existingPlane;
            }
            else if (airplanePrefab != null)
            {
                activePlaneInstance = Instantiate(airplanePrefab, flightStartPosition, Quaternion.identity);
                activePlaneInstance.name = "Cutscene_Airplane_V1";
            }
        }

        if (activePlaneInstance != null)
        {
            activePlaneInstance.transform.position = flightStartPosition;
            activePlaneInstance.transform.rotation = Quaternion.identity;

            // Localiza as hélices
            propellerL = activePlaneInstance.transform.Find("PropellerL");
            propellerR = activePlaneInstance.transform.Find("PropellerR");

            // Instancia fumaça e fogo no motor esquerdo durante o voo
            Transform engineTarget = propellerL != null ? propellerL : activePlaneInstance.transform;
            if (smokeVfxPrefab != null && planeSmokeEmitter == null)
            {
                planeSmokeEmitter = Instantiate(smokeVfxPrefab, engineTarget.position, Quaternion.identity, engineTarget);
                planeSmokeEmitter.transform.localPosition = new Vector3(0f, 0f, -1.5f);
            }
            if (fireVfxPrefab != null && planeFireEmitter == null)
            {
                planeFireEmitter = Instantiate(fireVfxPrefab, engineTarget.position, Quaternion.identity, engineTarget);
                planeFireEmitter.transform.localPosition = new Vector3(0f, 0f, -1.0f);
            }

            if (audioSourcePlane == null)
            {
                audioSourcePlane = activePlaneInstance.GetComponent<AudioSource>();
                if (audioSourcePlane == null) audioSourcePlane = activePlaneInstance.AddComponent<AudioSource>();
                audioSourcePlane.spatialBlend = 0.5f;
                audioSourcePlane.playOnAwake = false;
            }
        }

        // =========================================================================
        // FASE 1: O VOO DESGOVERNADO E APROXIMAÇÃO (0.0s até 3.6s)
        // =========================================================================
        if (windDescendClip != null) audioSourceGlobal.PlayOneShot(windDescendClip, 0.9f);
        if (chargeDroneClip != null) audioSourceGlobal.PlayOneShot(chargeDroneClip, 0.6f);

        // Posição inicial da câmera: tomada alta acompanhando o voo no céu
        Vector3 camPosPhase1 = DefaultCamPosPhase1;
        Vector3 camLookPhase1 = DefaultCamLookPhase1;
        currentCamPosTarget = camPosPhase1;
        currentCamLookTarget = camLookPhase1;
        currentFovTarget = 52f;

        float flightDuration = 3.6f;
        float elapsedFlight = 0f;

        while (elapsedFlight < flightDuration)
        {
            float t = elapsedFlight / flightDuration;

            // Curva de Bezier Quadrática (Start -> Mid -> Crash)
            Vector3 planePos = CalculateBezier(flightStartPosition, flightMidPosition, crashSitePosition, t);
            if (activePlaneInstance != null)
            {
                activePlaneInstance.transform.position = planePos;

                // Rotação voltada para a trajetória + oscilação cômica de emergência (Wobble)
                Vector3 nextPos = CalculateBezier(flightStartPosition, flightMidPosition, crashSitePosition, Mathf.Min(1f, t + 0.05f));
                Vector3 forwardDir = (nextPos - planePos).normalized;
                if (forwardDir != Vector3.zero)
                {
                    float rollWobble = Mathf.Sin(elapsedFlight * 16f) * (1f - t * 0.35f) * 16.0f;
                    float pitchWobble = Mathf.Sin(elapsedFlight * 12f) * 4.0f;
                    Quaternion baseRot = Quaternion.LookRotation(forwardDir, Vector3.up);
                    Quaternion flightRot = baseRot * Quaternion.Euler(pitchWobble, 0f, rollWobble);

                    if (t > 0.80f)
                    {
                        float blendT = (t - 0.80f) / 0.20f;
                        Quaternion targetFinalRot = Quaternion.Euler(crashSiteRotationEuler);
                        activePlaneInstance.transform.rotation = Quaternion.Slerp(flightRot, targetFinalRot, blendT);
                    }
                    else
                    {
                        activePlaneInstance.transform.rotation = flightRot;
                    }
                }
            }

            // Gira as hélices em alta velocidade
            if (propellerL != null) propellerL.Rotate(Vector3.forward, 1400f * Time.deltaTime, Space.Self);
            if (propellerR != null) propellerR.Rotate(Vector3.forward, 1400f * Time.deltaTime, Space.Self);

            // Câmera acompanha suavemente a descida
            currentCamLookTarget = Vector3.Lerp(camLookPhase1, planePos + Vector3.up * 1.5f, t * 0.90f);
            currentFovTarget = Mathf.Lerp(52f, 44f, t);

            yield return null;
            elapsedFlight += Time.deltaTime;
        }

        // =========================================================================
        // FASE 2: IMPACTO E EXPLOSÃO COLOSSAL (3.6s até 5.6s)
        // =========================================================================
        // Corta a câmera para a visão da divisa voltada para as rochas e o avião
        Vector3 camPosPhase2 = new Vector3(-66.0f, 4.5f, -22.0f);
        Vector3 camLookPhase2 = crashSitePosition + Vector3.up * 2.2f;
        currentCamPosTarget = camPosPhase2;
        currentCamLookTarget = camLookPhase2;
        currentFovTarget = 52f;

        // Assenta o avião na posição final de destroços
        if (activePlaneInstance != null)
        {
            activePlaneInstance.transform.position = crashSitePosition;
            activePlaneInstance.transform.rotation = Quaternion.Euler(crashSiteRotationEuler);
        }

        // Desliga rotação das hélices
        propellerL = null;
        propellerR = null;

        // Dispara efeitos de impacto e fogo no solo
        if (explosionImpactVfxPrefab != null)
        {
            Instantiate(explosionImpactVfxPrefab, crashSitePosition + Vector3.up * 0.5f, Quaternion.identity);
        }
        if (fireHitVfxPrefab != null)
        {
            Instantiate(fireHitVfxPrefab, crashSitePosition + Vector3.up * 1.5f, Quaternion.identity);
        }
        if (groundFireVfxPrefab != null)
        {
            Instantiate(groundFireVfxPrefab, crashSitePosition, Quaternion.identity, activePlaneInstance.transform);
        }

        // Áudios de impacto e explosão
        if (explosionClip != null) audioSourceGlobal.PlayOneShot(explosionClip, 1.0f);
        if (crashImpactClip != null) audioSourceGlobal.PlayOneShot(crashImpactClip, 0.9f);
        if (landingCrunchClip != null) audioSourceGlobal.PlayOneShot(landingCrunchClip, 0.85f);

        // Tremor máximo na tela (Trauma Shake)
        cameraTrauma = 1.0f;

        yield return new WaitForSeconds(1.0f);

        // Estouro secundário cômico no motor após o primeiro baque
        if (secondaryPopClip != null) audioSourceGlobal.PlayOneShot(secondaryPopClip, 0.85f);
        if (fireHitVfxPrefab != null)
        {
            Instantiate(fireHitVfxPrefab, crashSitePosition + new Vector3(-3f, 2f, 2f), Quaternion.identity);
        }
        cameraTrauma = 0.45f;

        yield return new WaitForSeconds(1.0f);

        // =========================================================================
        // FASE 3: FUGA CÔMICA DOS SOBREVIVENTES (5.6s até 9.2s)
        // =========================================================================
        // Câmera posicionada na rua de fuga, olhando em direção à cidade de costas para o avião,
        // garantindo que não seja visível nenhuma parede do avião sendo atravessada
        Vector3 camPosPhase3 = new Vector3(-71.0f, 1.8f, -34.0f);
        Vector3 camLookPhase3 = new Vector3(-48.0f, 1.2f, -32.5f);
        currentCamPosTarget = camPosPhase3;
        currentCamLookTarget = camLookPhase3;
        currentFovTarget = 50f;

        // Efeito cômico de abertura da escotilha
        if (hatchPopClip != null) audioSourceGlobal.PlayOneShot(hatchPopClip, 0.95f);

        // Determina os modelos para os dublês
        List<int> modelsToUse = new List<int>();
        if (survivorModelIndices != null && survivorModelIndices.Length > 0)
        {
            modelsToUse.AddRange(survivorModelIndices);
        }
        else
        {
            modelsToUse.Add(0); // Padrão
            modelsToUse.Add(1);
        }

        // Limita a até 4 dublês em tela para a cena ficar limpa e fluida
        int stuntCount = Mathf.Clamp(modelsToUse.Count, 1, 4);

        // Spawna os dublês sequencialmente saindo em debandada pela rua
        for (int i = 0; i < stuntCount; i++)
        {
            int modelIdx = modelsToUse[i % modelsToUse.Count];
            Vector3 stuntSpawnPos = new Vector3(-68.5f - i * 1.5f, 0.05f, -33.6f + (i - 1.5f) * 0.9f);
            StartCoroutine(StuntActorEscapeRoutine(i, modelIdx, stuntSpawnPos));
            yield return new WaitForSeconds(0.22f);
        }

        yield return new WaitForSeconds(1.0f);

        // Mini-estouro secundário cômico no motor enquanto fogem
        if (secondaryPopClip != null) audioSourceGlobal.PlayOneShot(secondaryPopClip, 0.8f);
        if (scrambleFleeClip != null) audioSourceGlobal.PlayOneShot(scrambleFleeClip, 0.9f);
        cameraTrauma = 0.25f;

        yield return new WaitForSeconds(1.0f);

        // =========================================================================
        // FASE 4: TRANSIÇÃO E REVELAÇÃO DA MORTE (9.2s até 10.8s)
        // =========================================================================
        // Câmera sobe suavemente em grua e olha em direção à praça e ao centro da cidade
        Vector3 camPosPhase4 = new Vector3(-58.0f, 16.0f, -34.0f);
        Vector3 camLookPhase4 = new Vector3(-15.0f, 3.5f, -35.0f); // Direção do centro da cidade

        float transDuration = 1.6f;
        float elapsedTrans = 0f;
        while (elapsedTrans < transDuration)
        {
            float t = elapsedTrans / transDuration;
            currentCamPosTarget = Vector3.Lerp(camPosPhase3, camPosPhase4, t);
            currentCamLookTarget = Vector3.Lerp(camLookPhase3, camLookPhase4, t);
            currentFovTarget = Mathf.Lerp(50f, 62f, t);
            yield return null;
            elapsedTrans += Time.deltaTime;
        }

        // Dispara a transição suave com fade cinematográfico para a gameplay
        yield return StartCoroutine(TransitionToGameplayRoutine(fastFade: false));
    }

    private IEnumerator TransitionToGameplayRoutine(bool fastFade)
    {
        if (isTransitioningToGameplay) yield break;
        isTransitioningToGameplay = true;

        float fadeOutDuration = fastFade ? 0.35f : 0.65f;
        float elapsed = 0f;

        // 1. Fade suave da cutscene para a tela preta
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            transitionFadeAlpha = Mathf.Clamp01(elapsed / fadeOutDuration);
            yield return null;
        }
        transitionFadeAlpha = 1f;

        // 2. Finaliza a cutscene enquanto a tela está 100% preta
        EndCutsceneSequence();

        // 3. Pausa breve para o GameSpawner instanciar os jogadores e inicializar as câmeras locais
        yield return new WaitForSeconds(0.35f);

        // 4. Fade-in suave revelando a gameplay da partida
        float fadeInDuration = 0.75f;
        elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            transitionFadeAlpha = Mathf.Clamp01(1f - (elapsed / fadeInDuration));
            yield return null;
        }

        transitionFadeAlpha = 0f;
        isTransitioningToGameplay = false;
    }

    private IEnumerator StuntActorEscapeRoutine(int actorIndex, int characterModelIndex, Vector3 spawnOrigin)
    {
        if (playerPrefab == null) yield break;

        // Instancia o dublê (sem scripts de rede para não poluir o Netcode)
        GameObject actor = new GameObject($"StuntSurvivor_{actorIndex}");
        actor.transform.position = spawnOrigin;
        actor.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        // Clona a malha do personagem correspondente
        GameObject modelPrefab = null;
        Transform playerVisuals = playerPrefab.transform.Find("Visuals");
        if (playerVisuals != null && characterModelIndex >= 0 && characterModelIndex < playerVisuals.childCount)
        {
            modelPrefab = playerVisuals.GetChild(characterModelIndex).gameObject;
        }
        else if (playerVisuals != null && playerVisuals.childCount > 0)
        {
            modelPrefab = playerVisuals.GetChild(0).gameObject;
        }

        if (modelPrefab == null) yield break;

        GameObject actorVisual = Instantiate(modelPrefab, actor.transform);
        actorVisual.SetActive(true);
        actorVisual.transform.localPosition = Vector3.zero;
        actorVisual.transform.localRotation = Quaternion.identity;

        Animator anim = actorVisual.GetComponent<Animator>();
        stuntActors.Add(actor);
        if (anim != null) stuntAnimators.Add(anim);

        // Configuração de animação idêntica ao PlayerController e NPCController:
        // 'Walking', 'Running' e 'IsGrounded' devem ser true para manter a transição contínua
        // na animação Running sem que o Animator caia no estado Idle ou engasgue.
        if (anim != null)
        {
            anim.applyRootMotion = false;
            anim.SetBool("Walking", true);
            anim.SetBool("Running", true);
            anim.SetBool("IsGrounded", true);
            // Variação de fase de passadas para não correrem em uníssono mecânico
            anim.Play("Running", 0, (actorIndex * 0.28f) % 1.0f);
            anim.speed = 1.05f;
        }

        // Faixa de corrida na rua/calçada para cada personagem correr de forma organizada
        float targetLaneZ = -33.6f + (actorIndex - 1.5f) * 1.35f;
        // Velocidade natural de corrida sincronizada com os passos da animação (7.35 m/s)
        float fleeSpeed = 7.35f + actorIndex * 0.15f;

        // Som de passos e correria desesperada
        if (scrambleFleeClip != null && actorIndex == 0)
        {
            audioSourceGlobal.PlayOneShot(scrambleFleeClip, 0.85f);
        }

        while (isCutsceneActive && actor != null)
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                if (anim != null)
                {
                    anim.SetBool("Walking", true);
                    anim.SetBool("Running", true);
                    anim.SetBool("IsGrounded", true);
                }

                // Corrida contínua ao longo da avenida em direção à cidade (+X) com aderência ao piso
                float moveX = fleeSpeed * dt;
                float currentZ = actor.transform.position.z;
                float newZ = Mathf.MoveTowards(currentZ, targetLaneZ, 2.8f * dt);
                float newX = actor.transform.position.x + moveX;

                float currentY = actor.transform.position.y;
                float targetY = 0.05f;
                if (Physics.Raycast(new Vector3(newX, 10f, newZ), Vector3.down, out RaycastHit groundHit, 25f, ~0, QueryTriggerInteraction.Ignore))
                {
                    targetY = groundHit.point.y + 0.05f;
                }
                float newY = Mathf.MoveTowards(currentY, targetY, 15f * dt);

                actor.transform.position = new Vector3(newX, newY, newZ);

                // Orientação suave do corpo na direção exata do percurso
                Vector3 moveDir = new Vector3(moveX, 0f, newZ - currentZ);
                if (moveDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized, Vector3.up);
                    actor.transform.rotation = Quaternion.Slerp(actor.transform.rotation, targetRot, 12f * dt);
                }
            }
            yield return null;
        }
    }

    private void CleanupAllStuntSurvivors()
    {
        for (int i = stuntActors.Count - 1; i >= 0; i--)
        {
            if (stuntActors[i] != null)
            {
                Destroy(stuntActors[i]);
            }
        }
        stuntActors.Clear();
        stuntAnimators.Clear();

        // Limpa proativamente qualquer dublê solto na cena
        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allObjects.Length; i++)
        {
            var go = allObjects[i];
            if (go != null && go.name.StartsWith("StuntSurvivor"))
            {
                Destroy(go);
            }
        }
    }

    private void SetSubtitle(string title, string text)
    {
        // Legendas desativadas a pedido do usuário
    }

    private void EndCutsceneSequence()
    {
        isCutsceneActive = false;
        hasPlayedCutscene = true;

        // Transforma o avião em destroço permanente do mapa sem colisores artificiais
        // (os limites do mapa/boundaries já impedem os jogadores de alcançarem o local)
        if (activePlaneInstance != null)
        {
            activePlaneInstance.transform.position = crashSitePosition;
            activePlaneInstance.transform.rotation = Quaternion.Euler(crashSiteRotationEuler);

            Debug.Log("[AirplaneCutscene] Destroços do avião preservados permanentemente no local do impacto junto aos boundaries.");
        }

        // Limpa dublês da cutscene
        CleanupAllStuntSurvivors();

        // Desativa a câmera cinemática exclusiva e reseta sua tag para não conflitar com a câmera de gameplay
        if (cutsceneCamera != null)
        {
            cutsceneCamera.enabled = false;
            cutsceneCamera.tag = "Untagged";
            cutsceneCamera.gameObject.SetActive(false);
        }
        if (cutsceneAudioListener != null) cutsceneAudioListener.enabled = false;

        // Restaura a exibição das bolinhas/pontinhos do boundary para a gameplay normal
        var boundaryVisual = FindFirstObjectByType<CityBoundaryVisualController>();
        if (boundaryVisual != null)
        {
            boundaryVisual.SetVisualsSuppressed(false);
        }

        // CRUCIAL: Restaura visibilidade do HUD e Menu ESC para os jogadores!
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.SetHudVisible(true);
        }
        if (InGamePauseMenuController.Instance != null)
        {
            InGamePauseMenuController.Instance.SetHudVisible(true);
        }

        Debug.Log("[AirplaneCutscene] Cutscene concluída com sucesso! Disparando início da partida e chegada da Morte.");
        GameAudioManager.Instance?.StartGameplayMusic(1.5f);
        OnCutsceneFinished?.Invoke();
    }

    private Vector3 CalculateBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    private void OnGUI()
    {
        // 1. Overlay de Transição / Fade Cinemático para o Início da Gameplay (Tela preta com alpha suave)
        if (isTransitioningToGameplay && transitionFadeAlpha > 0.001f)
        {
            GUI.color = new Color(0f, 0f, 0f, transitionFadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        if (!isCutsceneActive) return;

        // Renderiza as faixas pretas de Cinema (Letterbox 10% topo e base)
        float barHeight = Screen.height * 0.10f;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, barHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, Screen.height - barHeight, Screen.width, barHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // =========================================================================
        // BARRA SIMPLES E MINIMALISTA DE PULAR CUTSCENE (SEM TEXTOS DESNECESSÁRIOS)
        // =========================================================================
        float barW = 270f;
        float barH = 34f;
        float barX = Screen.width - barW - 24f;
        float barY = Screen.height - barHeight + (barHeight - barH) * 0.5f;

        if (barHeight < 40f)
        {
            barY = Screen.height - barH - 12f;
        }

        Rect trackRect = new Rect(barX, barY, barW, barH);

        // Fundo da barra (Track escuro com transparência suave e borda sutil)
        if (cartoonSliderTrack != null)
        {
            GUIStyle trackStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = cartoonSliderTrack },
                border = new RectOffset(14, 14, 10, 10)
            };
            GUI.Box(trackRect, GUIContent.none, trackStyle);
        }
        else
        {
            GUI.color = new Color(0.12f, 0.08f, 0.05f, 0.75f);
            GUI.DrawTexture(trackRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Progresso de preenchimento ao segurar a barra de espaço
        float progress = LocalHoldProgress;
        if (isLocalPlayerReadyToSkip || progress > 0.01f)
        {
            float fillPct = isLocalPlayerReadyToSkip ? 1f : progress;
            float fillW = Mathf.Clamp(barW * fillPct, 12f, barW);
            Rect fillRect = new Rect(barX, barY, fillW, barH);

            Texture2D fillTex = isLocalPlayerReadyToSkip ? cartoonBtnGreen : cartoonBtnOrange;
            if (fillTex != null)
            {
                GUIStyle fillStyle = new GUIStyle(GUI.skin.box)
                {
                    normal = { background = fillTex },
                    border = new RectOffset(12, 12, 12, 12)
                };
                GUI.Box(fillRect, GUIContent.none, fillStyle);
            }
            else
            {
                GUI.color = isLocalPlayerReadyToSkip ? new Color(0.22f, 0.76f, 0.36f, 0.92f) : new Color(1.0f, 0.65f, 0.15f, 0.92f);
                GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        // Texto limpo e direto: apenas a instrução e a contagem unânime
        GUIStyle textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        string labelText;
        if (isLocalPlayerReadyToSkip)
        {
            labelText = $"✓ Pronto!  ({syncedReadyCount}/{syncedTotalCount})";
        }
        else
        {
            labelText = $"Segure [ESPAÇO] para pular  ({syncedReadyCount}/{syncedTotalCount})";
        }

        // Sombra de texto suave para garantir nitidez máxima sobre o vídeo/cenário
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.Label(new Rect(barX, barY + 1.2f, barW, barH), labelText, textStyle);
        GUI.color = Color.white;
        GUI.Label(trackRect, labelText, textStyle);
    }
}
