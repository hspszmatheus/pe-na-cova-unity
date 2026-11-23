using System.Collections;
using System.Collections.Generic;
using Blocks.Sessions.Common;
using TMPro;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UIButton = UnityEngine.UIElements.Button;
using UISlider = UnityEngine.UIElements.Slider;
using UGUIButton = UnityEngine.UI.Button;
using UGUIImage = UnityEngine.UI.Image;
using UGUISlider = UnityEngine.UI.Slider;
using UGUIText = UnityEngine.UI.Text;
using Cursor = UnityEngine.Cursor;

/// <summary>
/// Orchestrates the 3-step Cartoon menu flow inside JoinByBrowsing.unity without touching UGS/NGO lifecycle:
/// Step 1 (StartScreen): Blurred 3D Gas Station + walking NPCs backdrop with ONLY the centered START button.
/// Step 2 (NicknameWindow): Centered Cartoon Nickname modal with TMP_InputField + bouncy CONFIRM button.
/// Step 3 (LobbyHud): Cleanly separated Create / Browse Room HUD with staggered Cartoon button & panel animations.
/// </summary>
public class LobbyMenuFlowController : MonoBehaviour
{
    public enum MenuStep
    {
        StartScreen = 0,
        NicknameWindow = 1,
        LobbyHud = 2
    }

    [Header("Start Page (UGUI - Step 1)")]
    [SerializeField] private GameObject startPageCanvas;
    [SerializeField] private CanvasGroup startCanvasGroup;
    [SerializeField] private GameObject gameTitlePanel;
    [SerializeField] private CanvasGroup gameTitleGroup;
    [SerializeField] private UGUIButton startButton;
    [SerializeField] private CanvasGroup startButtonGroup;

    [Header("Nickname Window (UGUI - Step 2)")]
    [SerializeField] private GameObject nicknameCard;
    [SerializeField] private CanvasGroup nicknameCardGroup;
    [SerializeField] private RectTransform nicknameHeaderRibbon;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private UGUIImage nicknameInputBg;
    [SerializeField] private UGUIButton confirmNicknameButton;
    [SerializeField] private Sprite inputNormalSprite;
    [SerializeField] private Sprite inputFocusedSprite;
    [SerializeField] private PlayerNameUIManager playerNameManager;

    [Header("Lobby HUD (UI Toolkit - Step 3)")]
    [SerializeField] private UIDocument lobbyUIDocument;

    [Header("Quit Game & Confirmation")]
    [SerializeField] private UGUIButton uguiQuitButton;
    [SerializeField] private GameObject uguiQuitConfirmDialog;
    [SerializeField] private UGUIButton uguiQuitConfirmOkButton;
    [SerializeField] private UGUIButton uguiQuitConfirmCancelButton;

    [Header("Menu Audio Mute & Volume")]
    [SerializeField] private UGUIButton uguiMuteButton;
    [SerializeField] private UGUIImage uguiMuteIconImage;
    [SerializeField] private Sprite muteSoundOnSprite;
    [SerializeField] private Sprite muteSoundOffSprite;

    [Header("Menu Audio Volume")]
    [SerializeField] private GameObject uguiVolumeBox;
    [SerializeField] private UGUISlider uguiVolumeSlider;
    [SerializeField] private UGUIText uguiVolumePercentText;

    [Header("Transition Timing (FCCartoonGUI_UIAnimation curves)")]
    [SerializeField] private float startFadeDuration = 0.20f;
    [SerializeField] private float nicknamePopDuration = 0.34f;
    [SerializeField] private float nicknameFadeDuration = 0.22f;
    [SerializeField] private float lobbyPopDuration = 0.34f;

    [Header("Connection Watchdog & Timeout")]
    [SerializeField] private float connectionTimeoutSeconds = 12f;

    [Header("Animação de Flutuação do Título Pé na Cova")]
    [SerializeField] private float titleBobAmplitude = 5.0f;
    [SerializeField] private float titleBobFrequency = 2.0f;
    [SerializeField] private float titleBreathingAmplitude = 0.02f;
    [SerializeField] private float titleTiltAngle = 1.2f;

    private Vector2 gameTitleBasePos;
    private bool gameTitleBasePosCaptured;

    private VisualElement lobbyHudWrapper;
    private VisualElement leftColumn;
    private VisualElement loadingRoomCard;
    private Label loadingRoomTitleLabel;
    private Label loadingRoomSubtitleLabel;
    private readonly VisualElement[] loadingDots = new VisualElement[3];
    private VisualElement rightColumn;
    private VisualElement rightColumnModal;

    private Coroutine transitionRoutine;
    private Coroutine rightColumnPopRoutine;

    private SessionObserver sessionObserver;
    private ISession activeObservedSession;
    private string resolvedSessionType = "default-session";

    private MenuStep currentStep = MenuStep.StartScreen;
    private bool isTransitioning;
    private bool lobbyOpened;
    private bool wasRightColumnVisible;
    private bool soloConnectedRoomMode;
    private bool isConnectingToRoom;
    private bool isAddingSessionAsync;
    private bool loadingCardShown;
    private float loadingElapsed;
    private float loadingCardRevealDelay;
    private float connectedRoomGraceTimer;
    private UIButton toolkitQuitButton;
    private UIButton toolkitMuteButton;
    private VisualElement toolkitMuteIcon;
    private VisualElement toolkitVolumeBox;
    private UISlider toolkitVolumeSlider;
    private Label toolkitVolumePercentLabel;
    private VisualElement toolkitQuitConfirmModal;
    private UIButton toolkitQuitConfirmOk;
    private UIButton toolkitQuitConfirmCancel;
    private bool isQuittingGame;

    private static bool hasCompletedInitialMenu;

    /// <summary>
    /// Força o retorno direto ao navegador de salas (LeftColumn) ao carregar JoinByBrowsing,
    /// ignorando sessões antigas ou telas de inicialização.
    /// </summary>
    public static bool ForceReturnToRoomBrowser { get; set; } = false;
    public static LobbyMenuFlowController Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        hasCompletedInitialMenu = false;
        ForceReturnToRoomBrowser = false;
    }

    private Vector2 nicknameCardBasePos;
    private bool nicknameBasePosCaptured;

    public bool IsLobbyOpened => lobbyOpened;
    public MenuStep CurrentStep => currentStep;

    private void Awake()
    {
        Instance = this;
        // Garante cursor destravado e visível em todos os menus do saguão/lobby
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (nicknameCard != null)
        {
            var cardRt = nicknameCard.GetComponent<RectTransform>();
            if (cardRt != null)
            {
                nicknameCardBasePos = cardRt.anchoredPosition;
                nicknameBasePosCaptured = true;
            }
        }

        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);

        if (uguiQuitButton != null)
            uguiQuitButton.onClick.AddListener(OnQuitGameClicked);

        if (uguiMuteButton != null)
            uguiMuteButton.onClick.AddListener(OnToggleMuteClicked);

        if (uguiVolumeSlider != null)
            uguiVolumeSlider.onValueChanged.AddListener(OnUguiVolumeChanged);

        if (uguiQuitConfirmOkButton != null)
            uguiQuitConfirmOkButton.onClick.AddListener(OnConfirmQuitClicked);

        if (uguiQuitConfirmCancelButton != null)
            uguiQuitConfirmCancelButton.onClick.AddListener(OnCancelQuitClicked);

        if (uguiQuitConfirmDialog != null)
            uguiQuitConfirmDialog.SetActive(false);

        if (confirmNicknameButton != null)
            confirmNicknameButton.onClick.AddListener(OnConfirmNicknameClicked);

        if (nicknameInput != null)
        {
            nicknameInput.onValueChanged.AddListener(OnNicknameValueChanged);
            nicknameInput.onSelect.AddListener(OnNicknameSelected);
            nicknameInput.onDeselect.AddListener(OnNicknameDeselected);
            nicknameInput.onSubmit.AddListener(OnNicknameSubmitted);
        }

        if (ForceReturnToRoomBrowser)
        {
            hasCompletedInitialMenu = true;
            PurgeActiveMultiplayerSessions();
            CancelPendingConnection();
            OpenLobbyDirectly(soloConnectedRoom: false);
        }
        else if (IsConnectedToRoom())
        {
            OpenLobbyDirectly(soloConnectedRoom: true);
        }
        else if (hasCompletedInitialMenu)
        {
            OpenLobbyDirectly(soloConnectedRoom: false);
        }
        else
        {
            HideLobbyInitially();
        }
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (GameAudioManager.Instance != null)
        {
            UpdateMuteButtonVisuals(GameAudioManager.Instance.IsMenuMuted);
            UpdateVolumeVisuals(GameAudioManager.MasterVolume, GameAudioManager.Instance.IsMenuMuted);
            GameAudioManager.Instance.OnMenuMuteStateChanged += OnAudioMuteStateChanged;
            GameAudioManager.OnMasterVolumeChanged += OnAudioVolumeChanged;
        }

        if (ForceReturnToRoomBrowser)
        {
            hasCompletedInitialMenu = true;
            PurgeActiveMultiplayerSessions();
            CancelPendingConnection();
            OpenLobbyDirectly(soloConnectedRoom: false);
            StartCoroutine(ClearForceReturnToRoomBrowserRoutine());
        }
        else if (IsConnectedToRoom())
        {
            if (!lobbyOpened || !soloConnectedRoomMode)
                OpenLobbyDirectly(soloConnectedRoom: true);
        }
        else if (hasCompletedInitialMenu)
        {
            if (!lobbyOpened)
                OpenLobbyDirectly(soloConnectedRoom: false);
        }
        else if (!lobbyOpened)
        {
            HideLobbyInitially();
        }

        PopulateInitialNickname();

        if (currentStep == MenuStep.StartScreen && startButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);

        EnsureSessionObserverAttached();

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientDisconnectCallback += OnNetworkManagerClientDisconnect;
        }
    }

    private void PopulateInitialNickname()
    {
        if (nicknameInput == null) return;
        string saved = PlayerNameUIManager.GetActivePlayerNickname();
        if (!string.IsNullOrEmpty(saved) && saved != "Player")
        {
            nicknameInput.SetTextWithoutNotify(saved);
        }
    }

    private void OnNicknameValueChanged(string value)
    {
        PlayerNameUIManager.SaveDraftNickname(value);
    }

    private void OnNicknameSelected(string _)
    {
        if (nicknameInputBg != null && inputFocusedSprite != null)
            nicknameInputBg.sprite = inputFocusedSprite;
    }

    private void OnNicknameDeselected(string _)
    {
        if (nicknameInputBg != null && inputNormalSprite != null)
            nicknameInputBg.sprite = inputNormalSprite;
    }

    private void OnNicknameSubmitted(string _)
    {
        if (currentStep == MenuStep.NicknameWindow && !isTransitioning)
            OnConfirmNicknameClicked();
    }

    private void Update()
    {
        if (currentStep == MenuStep.StartScreen)
        {
            AnimateGameTitleIdle();
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (uguiQuitConfirmDialog != null && uguiQuitConfirmDialog.activeSelf)
            {
                OnCancelQuitClicked();
            }
            else if (toolkitQuitConfirmModal != null && toolkitQuitConfirmModal.style.display == DisplayStyle.Flex)
            {
                OnCancelQuitClicked();
            }
        }
    }

    private void AnimateGameTitleIdle()
    {
        if (gameTitlePanel == null || !gameTitlePanel.activeSelf || isTransitioning)
            return;

        var titleRt = gameTitlePanel.GetComponent<RectTransform>();
        if (titleRt == null)
            return;

        if (!gameTitleBasePosCaptured)
        {
            gameTitleBasePos = titleRt.anchoredPosition;
            gameTitleBasePosCaptured = true;
        }

        float time = Time.unscaledTime * titleBobFrequency;
        float yOffset = Mathf.Sin(time) * titleBobAmplitude;
        float scale = 1.0f + Mathf.Cos(time * 0.85f) * titleBreathingAmplitude;
        float tilt = Mathf.Sin(time * 0.65f) * titleTiltAngle;

        titleRt.anchoredPosition = gameTitleBasePos + new Vector2(0f, yOffset);
        titleRt.localScale = new Vector3(scale, scale, 1.0f);
        titleRt.localRotation = Quaternion.Euler(0f, 0f, tilt);
    }

    private void LateUpdate()
    {
        EnsureSessionObserverAttached();

        // Guard against UIDocument rebuilding/attaching its VisualTree after Start().
        if (!lobbyOpened)
        {
            EnsureLobbyHidden();
            return;
        }

        EnsureLobbyVisualTreeBound();

        bool connected = IsConnectedToRoom();
        bool roomVisible = IsRightColumnVisible();

        if (!soloConnectedRoomMode && (connected || roomVisible) && !ForceReturnToRoomBrowser)
        {
            soloConnectedRoomMode = true;
            connectedRoomGraceTimer = 0f;
            if (!roomVisible)
            {
                BeginRoomLoading(immediateShow: true, subtitle: "Conectando à sala...");
            }
            else
            {
                ApplyColumnLayoutForMode();
            }
        }

        if (soloConnectedRoomMode)
        {
            connectedRoomGraceTimer += Time.unscaledDeltaTime;

            if (isConnectingToRoom)
            {
                loadingElapsed += Time.unscaledDeltaTime;

                if (roomVisible)
                {
                    FinishRoomLoadingToConnectedRoom();
                }
                else if (loadingElapsed >= connectionTimeoutSeconds)
                {
                    HandleConnectionTimeout();
                }
                else if (!isAddingSessionAsync && !connected && loadingElapsed >= 2.0f)
                {
                    ExitSoloConnectedRoomToBrowser();
                }
                else
                {
                    AnimateLoadingRoomCard();
                }
            }
            else if (!roomVisible && !connected && (wasRightColumnVisible || connectedRoomGraceTimer >= 1.5f))
            {
                ExitSoloConnectedRoomToBrowser();
            }
        }

        // Detect when CurrentSession (RightColumn) appears/disappears and trigger cartoon entrance.
        RefreshRightColumnPopupState();
    }

    private IEnumerator ClearForceReturnToRoomBrowserRoutine()
    {
        yield return null;
        yield return null;
        PurgeActiveMultiplayerSessions();
        ForceReturnToRoomBrowser = false;
        soloConnectedRoomMode = false;
        ApplyColumnLayoutForMode();
        TriggerBrowserRefresh();
    }

    private static void PurgeActiveMultiplayerSessions()
    {
        try
        {
            var service = MultiplayerService.Instance;
            if (service?.Sessions != null && service.Sessions.Count > 0)
            {
                foreach (var session in new List<ISession>(service.Sessions.Values))
                {
                    if (session == null) continue;
                    try
                    {
                        if (session.IsHost)
                        {
                            _ = session.AsHost().DeleteAsync();
                        }
                        else
                        {
                            _ = session.LeaveAsync();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[LobbyMenuFlowController] Erro ao purgar sessão residual: {ex.Message}");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LobbyMenuFlowController] Erro ao acessar MultiplayerService no purge: {ex.Message}");
        }
    }

    private static bool IsConnectedToRoom()
    {
        if (ForceReturnToRoomBrowser)
        {
            return false;
        }

        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsServer || (nm.IsClient && nm.IsConnectedClient)))
        {
            return true;
        }

        try
        {
            if (MultiplayerService.Instance != null && MultiplayerService.Instance.Sessions != null)
            {
                foreach (var kvp in MultiplayerService.Instance.Sessions)
                {
                    if (kvp.Value != null)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Services may not be initialized yet on cold boot.
        }

        return false;
    }

    private void CacheLobbyVisualElements(VisualElement root)
    {
        lobbyHudWrapper = root.Q<VisualElement>("LobbyHudWrapper");
        leftColumn = root.Q<VisualElement>("LeftColumn");
        loadingRoomCard = root.Q<VisualElement>("LoadingRoomCard");
        loadingRoomTitleLabel = root.Q<Label>("loading-room-title");
        loadingRoomSubtitleLabel = root.Q<Label>("loading-room-subtitle");
        for (int i = 0; i < loadingDots.Length; i++)
        {
            loadingDots[i] = root.Q<VisualElement>($"loading-dot-{i}");
        }
        rightColumn = root.Q<VisualElement>("RightColumn");
        rightColumnModal = rightColumn != null ? rightColumn.Q<VisualElement>(className: "blocks-modal") : null;

        var newToolkitQuitButton = root.Q<UIButton>("btn-lobby-quit-game");
        if (newToolkitQuitButton != null && newToolkitQuitButton != toolkitQuitButton)
        {
            if (toolkitQuitButton != null)
                toolkitQuitButton.clicked -= OnQuitGameClicked;
            toolkitQuitButton = newToolkitQuitButton;
            toolkitQuitButton.clicked += OnQuitGameClicked;
        }

        var newToolkitMuteButton = root.Q<UIButton>("btn-lobby-mute-sound");
        toolkitMuteIcon = root.Q<VisualElement>("menu-mute-icon");
        if (newToolkitMuteButton != null && newToolkitMuteButton != toolkitMuteButton)
        {
            if (toolkitMuteButton != null)
                toolkitMuteButton.clicked -= OnToggleMuteClicked;
            toolkitMuteButton = newToolkitMuteButton;
            toolkitMuteButton.clicked += OnToggleMuteClicked;
            if (GameAudioManager.Instance != null)
                UpdateMuteButtonVisuals(GameAudioManager.Instance.IsMenuMuted);
        }

        var newVolumeBox = root.Q<VisualElement>("menu-volume-box");
        var newVolumeSlider = root.Q<UISlider>("menu-volume-slider");
        var newVolumePercent = root.Q<Label>("menu-volume-percent");
        if (newVolumeSlider != null && newVolumeSlider != toolkitVolumeSlider)
        {
            if (toolkitVolumeSlider != null)
                toolkitVolumeSlider.UnregisterValueChangedCallback(OnToolkitVolumeChanged);
            toolkitVolumeSlider = newVolumeSlider;
            toolkitVolumePercentLabel = newVolumePercent;
            toolkitVolumeBox = newVolumeBox;
            toolkitVolumeSlider.RegisterValueChangedCallback(OnToolkitVolumeChanged);
            UpdateVolumeVisuals(GameAudioManager.MasterVolume, GameAudioManager.IsMasterMuted);
        }

        var newQuitModal = root.Q<VisualElement>("QuitConfirmModal");
        if (newQuitModal != null)
        {
            toolkitQuitConfirmModal = newQuitModal;
            var okBtn = root.Q<UIButton>("btn-quit-confirm-ok");
            if (okBtn != null && okBtn != toolkitQuitConfirmOk)
            {
                if (toolkitQuitConfirmOk != null)
                    toolkitQuitConfirmOk.clicked -= OnConfirmQuitClicked;
                toolkitQuitConfirmOk = okBtn;
                toolkitQuitConfirmOk.clicked += OnConfirmQuitClicked;
            }

            var cancelBtn = root.Q<UIButton>("btn-quit-confirm-cancel");
            if (cancelBtn != null && cancelBtn != toolkitQuitConfirmCancel)
            {
                if (toolkitQuitConfirmCancel != null)
                    toolkitQuitConfirmCancel.clicked -= OnCancelQuitClicked;
                toolkitQuitConfirmCancel = cancelBtn;
                toolkitQuitConfirmCancel.clicked += OnCancelQuitClicked;
            }
        }

        var mainContainer = root.Q<VisualElement>("MainContainer");
        if (mainContainer != null && mainContainer.dataSource is SessionSettings settings && !string.IsNullOrEmpty(settings.sessionType))
        {
            resolvedSessionType = settings.sessionType;
        }
    }

    private void EnsureLobbyVisualTreeBound()
    {
        if (lobbyUIDocument == null || lobbyUIDocument.rootVisualElement == null)
            return;

        var root = lobbyUIDocument.rootVisualElement;
        if (NeedsVisualCacheRefresh())
        {
            CacheLobbyVisualElements(root);
            ApplyColumnLayoutForMode();
            if (!isConnectingToRoom)
                HideLoadingCard();
            else if (loadingCardShown)
                ShowLoadingCard();
            ApplyCartoonButtonVariants(root);
        }

        if (!isTransitioning && lobbyHudWrapper != null && lobbyHudWrapper.resolvedStyle.display == DisplayStyle.None)
        {
            lobbyHudWrapper.style.display = DisplayStyle.Flex;
            lobbyHudWrapper.style.opacity = 1f;
        }

        if (soloConnectedRoomMode)
        {
            if (leftColumn != null && leftColumn.resolvedStyle.display != DisplayStyle.None)
                ApplyColumnLayoutForMode();
            else if (rightColumn != null && !rightColumn.ClassListContains("lobby-right-column--solo"))
                rightColumn.AddToClassList("lobby-right-column--solo");
        }
    }

    private void ApplyColumnLayoutForMode()
    {
        if (leftColumn != null)
        {
            leftColumn.style.display = soloConnectedRoomMode ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (rightColumn != null)
        {
            if (soloConnectedRoomMode)
                rightColumn.AddToClassList("lobby-right-column--solo");
            else
                rightColumn.RemoveFromClassList("lobby-right-column--solo");
        }
    }

    private void EnsureSessionObserverAttached()
    {
        if (sessionObserver != null)
            return;

        try
        {
            sessionObserver = new SessionObserver(resolvedSessionType);
            sessionObserver.AddingSessionStarted += OnAddingSessionStarted;
            sessionObserver.AddingSessionFailed += OnAddingSessionFailed;
            sessionObserver.SessionAdded += OnSessionAdded;
            if (sessionObserver.Session != null)
            {
                AttachObservedSession(sessionObserver.Session);
            }
        }
        catch
        {
            sessionObserver = null;
        }
    }

    private void AttachObservedSession(ISession session)
    {
        ClearObservedSession();
        activeObservedSession = session;
        if (activeObservedSession != null)
        {
            activeObservedSession.RemovedFromSession += OnObservedSessionRemoved;
            activeObservedSession.Deleted += OnObservedSessionRemoved;
        }
    }

    private void ClearObservedSession()
    {
        if (activeObservedSession != null)
        {
            activeObservedSession.RemovedFromSession -= OnObservedSessionRemoved;
            activeObservedSession.Deleted -= OnObservedSessionRemoved;
            activeObservedSession = null;
        }
    }

    private void DisposeSessionObserver()
    {
        ClearObservedSession();

        if (sessionObserver != null)
        {
            sessionObserver.AddingSessionStarted -= OnAddingSessionStarted;
            sessionObserver.AddingSessionFailed -= OnAddingSessionFailed;
            sessionObserver.SessionAdded -= OnSessionAdded;
            sessionObserver.Dispose();
            sessionObserver = null;
        }
    }

    private void OnAddingSessionStarted(AddingSessionOptions _)
    {
        isAddingSessionAsync = true;
        if (!lobbyOpened)
            return;

        BeginRoomLoading(immediateShow: true, subtitle: "Conectando à sala...");
    }

    private void OnAddingSessionFailed(AddingSessionOptions _, SessionException ex)
    {
        isAddingSessionAsync = false;
        Debug.LogWarning($"[LobbyMenuFlowController] Falha ao adicionar sessão: {ex?.Message}. Retornando ao navegador...");
        if (lobbyOpened && isConnectingToRoom && !IsConnectedToRoom() && !IsRightColumnVisible())
        {
            CancelPendingConnection();
            ExitSoloConnectedRoomToBrowser();
            TriggerBrowserRefresh();
        }
    }

    private void OnSessionAdded(ISession newSession)
    {
        AttachObservedSession(newSession);
        isAddingSessionAsync = false;

        if (!lobbyOpened)
            return;

        if (!IsRightColumnVisible())
        {
            BeginRoomLoading(immediateShow: loadingCardShown || isConnectingToRoom, subtitle: "Entrando na sala...");
        }
        else
        {
            FinishRoomLoadingToConnectedRoom();
        }
    }

    private void OnObservedSessionRemoved()
    {
        ClearObservedSession();
        isAddingSessionAsync = false;

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.ConnectionApprovalCallback = null;
            if (nm.NetworkConfig != null)
            {
                nm.NetworkConfig.ConnectionApproval = false;
            }
        }

        if (lobbyOpened && soloConnectedRoomMode)
        {
            ExitSoloConnectedRoomToBrowser();
        }
    }

    private bool NeedsVisualCacheRefresh()
    {
        return lobbyHudWrapper == null
            || lobbyHudWrapper.panel == null
            || loadingRoomCard == null
            || loadingRoomCard.panel == null;
    }

    private void BeginRoomLoading(bool immediateShow, string subtitle)
    {
        if (NeedsVisualCacheRefresh() && lobbyUIDocument != null && lobbyUIDocument.rootVisualElement != null)
        {
            CacheLobbyVisualElements(lobbyUIDocument.rootVisualElement);
            ApplyCartoonButtonVariants(lobbyUIDocument.rootVisualElement);
        }

        soloConnectedRoomMode = true;
        isConnectingToRoom = true;
        loadingElapsed = 0f;
        connectedRoomGraceTimer = 0f;
        loadingCardRevealDelay = immediateShow ? 0f : 0.14f;

        if (loadingRoomSubtitleLabel != null && !string.IsNullOrEmpty(subtitle))
            loadingRoomSubtitleLabel.text = subtitle;

        if (loadingRoomTitleLabel != null)
            loadingRoomTitleLabel.text = "Carregando...";

        ApplyColumnLayoutForMode();
        if (immediateShow)
            ShowLoadingCard();
        else
            HideLoadingCard();
    }

    private void ShowLoadingCard()
    {
        if (loadingRoomCard == null)
            return;

        loadingRoomCard.style.display = DisplayStyle.Flex;
        loadingRoomCard.style.opacity = 1f;
        loadingCardShown = true;
    }

    private void HideLoadingCard()
    {
        loadingCardShown = false;
        if (loadingRoomCard != null)
        {
            loadingRoomCard.style.display = DisplayStyle.None;
            ClearColumnInlineTransforms(loadingRoomCard);
        }
    }

    private void AnimateLoadingRoomCard()
    {
        if (!loadingCardShown && loadingElapsed >= loadingCardRevealDelay)
        {
            ShowLoadingCard();
        }

        if (loadingRoomCard == null || !loadingCardShown)
            return;

        float visibleElapsed = Mathf.Max(0f, loadingElapsed - loadingCardRevealDelay);
        float enterU = Mathf.Clamp01(visibleElapsed / 0.20f);
        float cardScale = enterU < 1f ? Mathf.LerpUnclamped(0.88f, 1f, BackOut(enterU)) : 1f;
        loadingRoomCard.style.opacity = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(visibleElapsed / 0.08f));
        loadingRoomCard.style.scale = new StyleScale(new Scale(new Vector3(cardScale, cardScale, 1f)));

        float t = Time.unscaledTime;
        for (int i = 0; i < loadingDots.Length; i++)
        {
            var dot = loadingDots[i];
            if (dot == null) continue;

            float wave = Mathf.Max(0f, Mathf.Sin((t * 5.2f) - (i * 0.75f)));
            float dotScale = 1f + (wave * 0.28f);
            float dotY = -wave * 7f;
            dot.style.scale = new StyleScale(new Scale(new Vector3(dotScale, dotScale, 1f)));
            dot.style.translate = new StyleTranslate(new Translate(0f, dotY));
        }

        if (loadingRoomTitleLabel != null)
        {
            int dotCount = ((int)(t * 2.8f) % 3) + 1;
            loadingRoomTitleLabel.text = dotCount == 1
                ? "Carregando."
                : (dotCount == 2 ? "Carregando.." : "Carregando...");
        }
    }

    private void FinishRoomLoadingToConnectedRoom()
    {
        isConnectingToRoom = false;
        isAddingSessionAsync = false;
        HideLoadingCard();
        soloConnectedRoomMode = true;
        ApplyColumnLayoutForMode();
    }

    private void OpenLobbyDirectly(bool soloConnectedRoom)
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        hasCompletedInitialMenu = true;
        currentStep = MenuStep.LobbyHud;
        isTransitioning = false;
        lobbyOpened = true;
        soloConnectedRoomMode = soloConnectedRoom;
        connectedRoomGraceTimer = 0f;

        if (startButton != null)
        {
            var startAnim = startButton.GetComponent<CartoonButtonAnimator>();
            if (startAnim != null) startAnim.enabled = false;
            startButton.gameObject.SetActive(false);
        }

        if (startCanvasGroup != null)
        {
            startCanvasGroup.alpha = 0f;
            startCanvasGroup.interactable = false;
            startCanvasGroup.blocksRaycasts = false;
        }

        if (startPageCanvas != null)
            startPageCanvas.SetActive(false);

        if (nicknameCardGroup != null)
        {
            nicknameCardGroup.alpha = 0f;
            nicknameCardGroup.interactable = false;
            nicknameCardGroup.blocksRaycasts = false;
        }

        if (nicknameCard != null)
            nicknameCard.SetActive(false);

        if (lobbyUIDocument == null || lobbyUIDocument.rootVisualElement == null)
        {
            wasRightColumnVisible = false;
            isConnectingToRoom = soloConnectedRoom;
            loadingCardRevealDelay = 0.14f;
            return;
        }

        var root = lobbyUIDocument.rootVisualElement;
        CacheLobbyVisualElements(root);

        if (lobbyHudWrapper != null)
        {
            lobbyHudWrapper.style.display = DisplayStyle.Flex;
            lobbyHudWrapper.style.opacity = 1f;
        }

        ApplyColumnLayoutForMode();
        ApplyCartoonButtonVariants(root);

        wasRightColumnVisible = IsRightColumnVisible();
        if (soloConnectedRoom && !wasRightColumnVisible)
        {
            BeginRoomLoading(immediateShow: false, subtitle: "Reconectando à sala...");
        }
        else
        {
            FinishLoadingStateOnly();
            if (wasRightColumnVisible && rightColumn != null)
            {
                ClearColumnInlineTransforms(rightColumn);
            }
            if (!soloConnectedRoom)
            {
                TriggerBrowserRefresh();
            }
        }
    }

    private void FinishLoadingStateOnly()
    {
        isConnectingToRoom = false;
        isAddingSessionAsync = false;
        HideLoadingCard();
    }

    public void ExitSoloConnectedRoomToBrowser()
    {
        if (rightColumnPopRoutine != null)
        {
            StopCoroutine(rightColumnPopRoutine);
            rightColumnPopRoutine = null;
        }
        if (rightColumn != null)
            ClearColumnInlineTransforms(rightColumn);

        FinishLoadingStateOnly();
        soloConnectedRoomMode = false;
        wasRightColumnVisible = false;
        ApplyColumnLayoutForMode();

        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null)
        {
            nm.ConnectionApprovalCallback = null;
            if (nm.NetworkConfig != null)
            {
                nm.NetworkConfig.ConnectionApproval = false;
            }
        }

        if (lobbyUIDocument != null && lobbyUIDocument.rootVisualElement != null)
        {
            ApplyCartoonButtonVariants(lobbyUIDocument.rootVisualElement);
        }

        TriggerBrowserRefresh();
    }

    private void HandleConnectionTimeout()
    {
        Debug.LogWarning($"[LobbyMenuFlowController] Tempo limite de conexão à sala esgotado ({connectionTimeoutSeconds}s). Cancelando tentativa e retornando ao navegador de salas.");
        CancelPendingConnection();
        ExitSoloConnectedRoomToBrowser();
    }

    private void CancelPendingConnection()
    {
        isConnectingToRoom = false;
        isAddingSessionAsync = false;

        try
        {
            if (activeObservedSession != null)
            {
                if (activeObservedSession.IsHost)
                {
                    _ = activeObservedSession.AsHost().DeleteAsync();
                }
                else
                {
                    _ = activeObservedSession.LeaveAsync();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LobbyMenuFlowController] Erro ao sair da sessão no cancelamento: {ex.Message}");
        }

        try
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.ConnectionApprovalCallback = null;
                if (nm.NetworkConfig != null)
                {
                    nm.NetworkConfig.ConnectionApproval = false;
                }

                if (nm.IsListening && !nm.ShutdownInProgress)
                {
                    nm.Shutdown();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LobbyMenuFlowController] Erro ao desligar NetworkManager no cancelamento: {ex.Message}");
        }
    }

    private void OnNetworkManagerClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && clientId == nm.LocalClientId)
        {
            if (isConnectingToRoom)
            {
                Debug.LogWarning("[LobbyMenuFlowController] Desconectado durante o carregamento da sala. Retornando ao navegador...");
                CancelPendingConnection();
                ExitSoloConnectedRoomToBrowser();
            }
            else if (soloConnectedRoomMode)
            {
                Debug.LogWarning("[LobbyMenuFlowController] Desconectado da sala. Retornando ao navegador...");
                CancelPendingConnection();
                ExitSoloConnectedRoomToBrowser();
            }
        }
    }

    public void TriggerBrowserRefresh()
    {
        if (leftColumn != null)
        {
            var browserEl = leftColumn.Q<Blocks.Sessions.SessionBrowserElement>();
            if (browserEl != null)
            {
                browserEl.RefreshSessionList(clearSelection: true);
                return;
            }
        }

        if (lobbyUIDocument != null && lobbyUIDocument.rootVisualElement != null)
        {
            var browserEl = lobbyUIDocument.rootVisualElement.Q<Blocks.Sessions.SessionBrowserElement>();
            browserEl?.RefreshSessionList(clearSelection: true);
        }
    }

    private void EnsureLobbyHidden()
    {
        if (lobbyUIDocument == null || lobbyUIDocument.rootVisualElement == null)
            return;

        var root = lobbyUIDocument.rootVisualElement;
        if (NeedsVisualCacheRefresh())
        {
            CacheLobbyVisualElements(root);
            ApplyCartoonButtonVariants(root);
        }

        HideLoadingCard();

        if (lobbyHudWrapper != null && (lobbyHudWrapper.resolvedStyle.display != DisplayStyle.None || lobbyHudWrapper.resolvedStyle.opacity > 0.001f))
        {
            lobbyHudWrapper.style.display = DisplayStyle.None;
            lobbyHudWrapper.style.opacity = 0f;
            ApplyCartoonButtonVariants(root);
        }
    }

    private void RefreshRightColumnPopupState()
    {
        if (lobbyUIDocument == null || lobbyUIDocument.rootVisualElement == null)
            return;

        var root = lobbyUIDocument.rootVisualElement;
        if (rightColumn == null || rightColumn.panel == null)
        {
            rightColumn = root.Q<VisualElement>("RightColumn");
            rightColumnModal = rightColumn != null ? rightColumn.Q<VisualElement>(className: "blocks-modal") : null;
        }

        bool isRightVisible = IsRightColumnVisible();
        if (isRightVisible && !wasRightColumnVisible)
        {
            wasRightColumnVisible = true;
            ApplyCartoonButtonVariants(root);

            if (!isTransitioning && rightColumn != null)
            {
                if (rightColumnPopRoutine != null)
                    StopCoroutine(rightColumnPopRoutine);
                rightColumn.style.opacity = 0f;
                rightColumnPopRoutine = StartCoroutine(AnimateRightColumnEntranceRoutine());
            }
        }
        else if (!isRightVisible && wasRightColumnVisible)
        {
            wasRightColumnVisible = false;
        }
    }

    private bool IsRightColumnVisible()
    {
        if (ForceReturnToRoomBrowser)
            return false;

        if (rightColumn == null || rightColumn.resolvedStyle.display == DisplayStyle.None)
            return false;

        if (rightColumnModal != null && rightColumnModal.resolvedStyle.display == DisplayStyle.None)
            return false;

        return rightColumn.layout.width > 10f;
    }

    private void OnDestroy()
    {
        DisposeSessionObserver();

        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartClicked);

        if (uguiQuitButton != null)
            uguiQuitButton.onClick.RemoveListener(OnQuitGameClicked);

        if (uguiMuteButton != null)
            uguiMuteButton.onClick.RemoveListener(OnToggleMuteClicked);

        if (uguiVolumeSlider != null)
            uguiVolumeSlider.onValueChanged.RemoveListener(OnUguiVolumeChanged);

        if (uguiQuitConfirmOkButton != null)
            uguiQuitConfirmOkButton.onClick.RemoveListener(OnConfirmQuitClicked);

        if (uguiQuitConfirmCancelButton != null)
            uguiQuitConfirmCancelButton.onClick.RemoveListener(OnCancelQuitClicked);

        if (toolkitQuitButton != null)
            toolkitQuitButton.clicked -= OnQuitGameClicked;

        if (toolkitMuteButton != null)
            toolkitMuteButton.clicked -= OnToggleMuteClicked;

        if (toolkitVolumeSlider != null)
            toolkitVolumeSlider.UnregisterValueChangedCallback(OnToolkitVolumeChanged);

        if (toolkitQuitConfirmOk != null)
            toolkitQuitConfirmOk.clicked -= OnConfirmQuitClicked;

        if (toolkitQuitConfirmCancel != null)
            toolkitQuitConfirmCancel.clicked -= OnCancelQuitClicked;

        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.OnMenuMuteStateChanged -= OnAudioMuteStateChanged;
            GameAudioManager.OnMasterVolumeChanged -= OnAudioVolumeChanged;
        }

        if (confirmNicknameButton != null)
            confirmNicknameButton.onClick.RemoveListener(OnConfirmNicknameClicked);

        if (nicknameInput != null)
        {
            nicknameInput.onValueChanged.RemoveListener(OnNicknameValueChanged);
            nicknameInput.onSelect.RemoveListener(OnNicknameSelected);
            nicknameInput.onDeselect.RemoveListener(OnNicknameDeselected);
            nicknameInput.onSubmit.RemoveListener(OnNicknameSubmitted);
        }

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientDisconnectCallback -= OnNetworkManagerClientDisconnect;
        }

        if (Instance == this) Instance = null;
    }

    private void EnsureGameTitleBound()
    {
        if (gameTitlePanel == null && startPageCanvas != null)
        {
            var tr = startPageCanvas.transform.Find("GameTitlePanel");
            if (tr != null)
            {
                gameTitlePanel = tr.gameObject;
                gameTitleGroup = gameTitlePanel.GetComponent<CanvasGroup>();
            }
        }
    }

    private void HideLobbyInitially()
    {
        currentStep = MenuStep.StartScreen;
        isTransitioning = false;
        lobbyOpened = false;
        wasRightColumnVisible = false;

        EnsureGameTitleBound();

        if (startPageCanvas != null)
            startPageCanvas.SetActive(true);

        if (startCanvasGroup != null)
        {
            startCanvasGroup.alpha = 1f;
            startCanvasGroup.interactable = true;
            startCanvasGroup.blocksRaycasts = true;
        }

        if (gameTitlePanel != null)
        {
            gameTitlePanel.SetActive(true);
            gameTitlePanel.transform.localScale = Vector3.one;
            gameTitlePanel.transform.localRotation = Quaternion.identity;
        }

        if (gameTitleGroup != null)
        {
            gameTitleGroup.alpha = 1f;
            gameTitleGroup.interactable = true;
            gameTitleGroup.blocksRaycasts = true;
        }

        if (startButton != null)
        {
            var txt = startButton.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txt != null) txt.text = "Começar";
            var tmp = startButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (tmp != null) tmp.text = "Começar";

            startButton.gameObject.SetActive(true);
            startButton.transform.localScale = Vector3.one;
            startButton.transform.localRotation = Quaternion.identity;
            var startAnim = startButton.GetComponent<CartoonButtonAnimator>();
            if (startAnim != null)
            {
                startAnim.enabled = true;
                startAnim.PlayPopBounce(0.68f, 7.2f, -2.5f);
            }
        }

        if (startButtonGroup != null)
        {
            startButtonGroup.alpha = 1f;
            startButtonGroup.interactable = true;
            startButtonGroup.blocksRaycasts = true;
        }

        if (uguiQuitButton != null)
        {
            uguiQuitButton.gameObject.SetActive(true);
            uguiQuitButton.interactable = true;
            uguiQuitButton.transform.localScale = Vector3.one;
            uguiQuitButton.transform.localRotation = Quaternion.identity;
            var quitAnim = uguiQuitButton.GetComponent<CartoonButtonAnimator>();
            if (quitAnim != null)
            {
                quitAnim.enabled = true;
                quitAnim.PlayPopBounce(0.72f, 6.5f, 0f);
            }
        }

        if (uguiVolumeBox != null)
        {
            uguiVolumeBox.SetActive(true);
            uguiVolumeBox.transform.localScale = Vector3.one;
            uguiVolumeBox.transform.localRotation = Quaternion.identity;
        }

        if (nicknameCard != null)
            nicknameCard.SetActive(false);

        if (nicknameCardGroup != null)
        {
            nicknameCardGroup.alpha = 0f;
            nicknameCardGroup.interactable = false;
            nicknameCardGroup.blocksRaycasts = false;
        }

        if (lobbyUIDocument == null || lobbyUIDocument.rootVisualElement == null)
            return;

        var root = lobbyUIDocument.rootVisualElement;
        CacheLobbyVisualElements(root);
        HideLoadingCard();

        ApplyCartoonButtonVariants(root);

        if (lobbyHudWrapper != null)
        {
            lobbyHudWrapper.style.display = DisplayStyle.None;
            lobbyHudWrapper.style.opacity = 0f;
        }
    }

    /// <summary>
    /// Step 1 -> Step 2: Animates out the centered START button and pops in the centered Nickname window.
    /// </summary>
    public void OnStartClicked()
    {
        if (currentStep != MenuStep.StartScreen || isTransitioning)
            return;

        GameAudioManager.Instance?.PlayButtonConfirm();
        isTransitioning = true;

        if (startButtonGroup != null)
        {
            startButtonGroup.interactable = false;
            startButtonGroup.blocksRaycasts = false;
        }

        if (startButton != null)
        {
            var anim = startButton.GetComponent<CartoonButtonAnimator>();
            if (anim != null)
                anim.enabled = false;
        }

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionStartToNicknameRoutine());
    }

    /// <summary>
    /// Step 2 -> Step 3: Confirms the typed nickname, animates out the Nickname window, and opens the Room Browser HUD.
    /// </summary>
    public void OnConfirmNicknameClicked()
    {
        if (currentStep != MenuStep.NicknameWindow || isTransitioning)
            return;

        GameAudioManager.Instance?.PlayButtonConfirm();
        hasCompletedInitialMenu = true;
        isTransitioning = true;

        string chosenNickname = nicknameInput != null ? nicknameInput.text : string.Empty;
        if (playerNameManager != null)
            _ = playerNameManager.ApplyNicknameAsync(chosenNickname);
        else
            PlayerNameUIManager.SaveDraftNickname(chosenNickname);

        if (nicknameCardGroup != null)
        {
            nicknameCardGroup.interactable = false;
            nicknameCardGroup.blocksRaycasts = false;
        }

        if (confirmNicknameButton != null)
        {
            var anim = confirmNicknameButton.GetComponent<CartoonButtonAnimator>();
            if (anim != null)
                anim.enabled = false;
        }

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionNicknameToLobbyRoutine());
    }

    private IEnumerator TransitionStartToNicknameRoutine()
    {
        EnsureGameTitleBound();

        // 1. Playful squash-punch fade out on START button and Game Title
        RectTransform startRt = startButton != null ? startButton.GetComponent<RectTransform>() : null;
        RectTransform titleRt = gameTitlePanel != null ? gameTitlePanel.GetComponent<RectTransform>() : null;
        float t = 0f;
        while (t < startFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float n = Mathf.Clamp01(t / Mathf.Max(0.05f, startFadeDuration));

            if (startButtonGroup != null)
                startButtonGroup.alpha = 1f - Mathf.Pow(n, 1.3f);

            if (gameTitleGroup != null)
                gameTitleGroup.alpha = 1f - Mathf.Pow(n, 1.3f);

            if (titleRt != null)
            {
                float ts = Mathf.Lerp(1.0f, 0.85f, n);
                titleRt.localScale = Vector3.one * ts;
            }

            if (startRt != null)
            {
                float s = n < 0.35f
                    ? Mathf.Lerp(1.0f, 1.13f, n / 0.35f)
                    : Mathf.Lerp(1.13f, 0.62f, (n - 0.35f) / 0.65f);
                startRt.localScale = Vector3.one * s;
                startRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -5.5f, n));
            }

            yield return null;
        }

        if (startButton != null)
            startButton.gameObject.SetActive(false);

        if (gameTitlePanel != null)
            gameTitlePanel.SetActive(false);

        // 2. Pop in centered Nickname Card with staggered Ribbon / Input / Cartoon Button bounce
        if (nicknameCard != null)
        {
            GameAudioManager.Instance?.PlayCardPop();
            nicknameCard.SetActive(true);
            RectTransform cardRt = nicknameCard.GetComponent<RectTransform>();
            if (cardRt != null && !nicknameBasePosCaptured)
            {
                nicknameCardBasePos = cardRt.anchoredPosition;
                nicknameBasePosCaptured = true;
            }

            RectTransform inputRt = nicknameInput != null ? nicknameInput.GetComponent<RectTransform>() : null;
            CartoonButtonAnimator confirmAnim = confirmNicknameButton != null
                ? confirmNicknameButton.GetComponent<CartoonButtonAnimator>()
                : null;

            if (nicknameHeaderRibbon != null)
                nicknameHeaderRibbon.localScale = Vector3.one * 0.78f;
            if (inputRt != null)
                inputRt.localScale = Vector3.one * 0.84f;

            if (confirmNicknameButton != null)
            {
                confirmNicknameButton.transform.localScale = Vector3.one * 0.64f;
                if (confirmAnim != null)
                {
                    confirmAnim.enabled = true;
                    confirmAnim.PlayPopBounce(0.60f, 7.6f, 3.2f);
                }
            }

            float popT = 0f;
            while (popT < nicknamePopDuration)
            {
                popT += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(popT / Mathf.Max(0.05f, nicknamePopDuration));
                float backScale = BackOut(u);

                if (nicknameCardGroup != null)
                    nicknameCardGroup.alpha = Mathf.Clamp01(u * 2.4f);

                if (cardRt != null)
                {
                    float cardScale = Mathf.LerpUnclamped(0.72f, 1f, backScale);
                    float yDrop = Mathf.LerpUnclamped(32f, 0f, EaseOutCubic(u));
                    float rock = Mathf.Sin(u * Mathf.PI * 1.5f) * (1f - u) * -3.0f;
                    cardRt.localScale = Vector3.one * cardScale;
                    cardRt.anchoredPosition = nicknameCardBasePos + new Vector2(0f, yDrop);
                    cardRt.localRotation = Quaternion.Euler(0f, 0f, rock);
                }

                if (nicknameHeaderRibbon != null)
                {
                    float v = Mathf.Clamp01((u - 0.10f) / 0.90f);
                    float ribScale = Mathf.LerpUnclamped(0.78f, 1f, BackOut(v));
                    float ribTilt = Mathf.Sin(v * Mathf.PI) * (1f - v) * 2.5f;
                    nicknameHeaderRibbon.localScale = Vector3.one * ribScale;
                    nicknameHeaderRibbon.localRotation = Quaternion.Euler(0f, 0f, ribTilt);
                }

                if (inputRt != null)
                {
                    float w = Mathf.Clamp01((u - 0.16f) / 0.84f);
                    float inScale = Mathf.LerpUnclamped(0.84f, 1f, BackOut(w));
                    inputRt.localScale = Vector3.one * inScale;
                }

                yield return null;
            }

            if (cardRt != null)
            {
                cardRt.localScale = Vector3.one;
                cardRt.anchoredPosition = nicknameCardBasePos;
                cardRt.localRotation = Quaternion.identity;
            }

            if (nicknameHeaderRibbon != null)
            {
                nicknameHeaderRibbon.localScale = Vector3.one;
                nicknameHeaderRibbon.localRotation = Quaternion.identity;
            }

            if (inputRt != null)
                inputRt.localScale = Vector3.one;

            if (nicknameCardGroup != null)
            {
                nicknameCardGroup.alpha = 1f;
                nicknameCardGroup.interactable = true;
                nicknameCardGroup.blocksRaycasts = true;
            }
        }

        currentStep = MenuStep.NicknameWindow;
        isTransitioning = false;

        if (nicknameInput != null)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(nicknameInput.gameObject);
            nicknameInput.ActivateInputField();
            nicknameInput.MoveTextEnd(false);
        }
    }

    private IEnumerator TransitionNicknameToLobbyRoutine()
    {
        // 1. Celebratory click punch on CONFIRM button & bounce-out on Nickname window
        RectTransform cardRt = nicknameCard != null ? nicknameCard.GetComponent<RectTransform>() : null;
        RectTransform confirmRt = confirmNicknameButton != null ? confirmNicknameButton.GetComponent<RectTransform>() : null;

        float t = 0f;
        while (t < nicknameFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float n = Mathf.Clamp01(t / Mathf.Max(0.05f, nicknameFadeDuration));

            if (confirmRt != null)
            {
                float cScale = n < 0.30f
                    ? Mathf.Lerp(1.0f, 1.15f, n / 0.30f)
                    : Mathf.Lerp(1.15f, 0.78f, (n - 0.30f) / 0.70f);
                confirmRt.localScale = Vector3.one * cScale;
            }

            if (cardRt != null)
            {
                float kScale = n < 0.28f
                    ? Mathf.Lerp(1.0f, 1.04f, n / 0.28f)
                    : Mathf.Lerp(1.04f, 0.76f, (n - 0.28f) / 0.72f);
                cardRt.localScale = Vector3.one * kScale;
                cardRt.anchoredPosition = nicknameCardBasePos + new Vector2(0f, Mathf.Lerp(0f, 20f, n * n));
                cardRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 3.2f, n));
            }

            if (nicknameCardGroup != null)
                nicknameCardGroup.alpha = 1f - Mathf.Pow(n, 1.4f);

            if (startCanvasGroup != null && n > 0.3f)
                startCanvasGroup.alpha = 1f - ((n - 0.3f) / 0.7f);

            yield return null;
        }

        if (nicknameCard != null)
            nicknameCard.SetActive(false);
        if (startPageCanvas != null)
            startPageCanvas.SetActive(false);

        lobbyOpened = true;
        currentStep = MenuStep.LobbyHud;

        // 2. Reveal Lobby HUD with FCCartoonGUI overshoot scale + staggered button entrance
        if (lobbyUIDocument != null && lobbyUIDocument.rootVisualElement != null)
        {
            var root = lobbyUIDocument.rootVisualElement;
            CacheLobbyVisualElements(root);
            if (!isConnectingToRoom)
                HideLoadingCard();

            ApplyCartoonButtonVariants(root);

            if (lobbyHudWrapper != null)
            {
                lobbyHudWrapper.style.display = DisplayStyle.Flex;
                lobbyHudWrapper.style.opacity = 0f;
            }

            // Wait one frame so UI Toolkit lays out Blocks elements cleanly
            yield return null;
            ApplyCartoonButtonVariants(root);

            List<VisualElement> animatedTargets = CollectPopTargets(leftColumn);
            foreach (var target in animatedTargets)
            {
                target.style.scale = new StyleScale(new Scale(new Vector3(0.74f, 0.74f, 1f)));
                target.style.opacity = 0f;
            }

            bool rightVisibleNow = IsRightColumnVisible();
            wasRightColumnVisible = rightVisibleNow;

            float popT = 0f;
            while (popT < lobbyPopDuration)
            {
                popT += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(popT / Mathf.Max(0.05f, lobbyPopDuration));
                float easeScale = BackOut(u);
                float scaleVal = Mathf.LerpUnclamped(0.84f, 1f, easeScale);
                float slideY = Mathf.Lerp(26f, 0f, EaseOutCubic(u));
                float rockDeg = Mathf.Sin(u * Mathf.PI) * (1f - u) * -2.2f;

                if (lobbyHudWrapper != null)
                    lobbyHudWrapper.style.opacity = Mathf.Clamp01(u * 1.8f);

                if (leftColumn != null)
                {
                    leftColumn.style.scale = new StyleScale(new Scale(new Vector3(scaleVal, scaleVal, 1f)));
                    leftColumn.style.translate = new StyleTranslate(new Translate(0f, slideY));
                    leftColumn.style.rotate = new StyleRotate(new Rotate(new Angle(rockDeg, AngleUnit.Degree)));
                }

                if (rightVisibleNow && rightColumn != null)
                {
                    rightColumn.style.scale = new StyleScale(new Scale(new Vector3(scaleVal, scaleVal, 1f)));
                    rightColumn.style.translate = new StyleTranslate(new Translate(0f, slideY));
                    rightColumn.style.rotate = new StyleRotate(new Rotate(new Angle(-rockDeg, AngleUnit.Degree)));
                }

                ApplyStaggeredTargetsFrame(animatedTargets, u);
                yield return null;
            }

            if (lobbyHudWrapper != null)
                lobbyHudWrapper.style.opacity = 1f;

            ClearColumnInlineTransforms(leftColumn);
            if (rightVisibleNow)
                ClearColumnInlineTransforms(rightColumn);
            ClearTargetsInlineTransforms(animatedTargets);

            TriggerBrowserRefresh();
        }

        isTransitioning = false;
    }

    private IEnumerator AnimateRightColumnEntranceRoutine()
    {
        if (rightColumn == null)
            yield break;

        yield return null;

        List<VisualElement> targets = CollectPopTargets(rightColumn);
        foreach (var target in targets)
        {
            target.style.scale = new StyleScale(new Scale(new Vector3(0.74f, 0.74f, 1f)));
            target.style.opacity = 0f;
        }

        float dur = Mathf.Max(0.24f, lobbyPopDuration * 0.9f);
        float t = 0f;
        while (t < dur && rightColumn != null)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float scaleVal = Mathf.LerpUnclamped(0.84f, 1f, BackOut(u));
            float slideX = soloConnectedRoomMode ? 0f : Mathf.Lerp(28f, 0f, EaseOutCubic(u));
            float slideY = soloConnectedRoomMode ? Mathf.Lerp(24f, 0f, EaseOutCubic(u)) : 0f;
            float rockDeg = Mathf.Sin(u * Mathf.PI) * (1f - u) * 2.2f;

            rightColumn.style.opacity = Mathf.Clamp01(u * 1.9f);
            rightColumn.style.scale = new StyleScale(new Scale(new Vector3(scaleVal, scaleVal, 1f)));
            rightColumn.style.translate = new StyleTranslate(new Translate(slideX, slideY));
            rightColumn.style.rotate = new StyleRotate(new Rotate(new Angle(rockDeg, AngleUnit.Degree)));

            ApplyStaggeredTargetsFrame(targets, u);
            yield return null;
        }

        ClearColumnInlineTransforms(rightColumn);
        ClearTargetsInlineTransforms(targets);
    }

    private static List<VisualElement> CollectPopTargets(VisualElement container)
    {
        var targets = new List<VisualElement>();
        if (container == null) return targets;

        container.Query<VisualElement>(className: "blocks-header").ForEach(el =>
        {
            if (el != null && el.resolvedStyle.display != DisplayStyle.None && !targets.Contains(el))
                targets.Add(el);
        });

        container.Query<UIButton>().ForEach(btn =>
        {
            if (btn == null || btn.resolvedStyle.display == DisplayStyle.None)
                return;
            if (btn.parent != null && btn.parent.ClassListContains("blocks-copy-session--header"))
                return;
            if (!targets.Contains(btn))
                targets.Add(btn);
        });

        return targets;
    }

    private static void ApplyStaggeredTargetsFrame(List<VisualElement> targets, float normalizedTime)
    {
        if (targets == null || targets.Count == 0) return;

        for (int i = 0; i < targets.Count; i++)
        {
            VisualElement el = targets[i];
            if (el == null) continue;

            float delay = Mathf.Min(0.42f, i * 0.085f);
            float localU = Mathf.Clamp01((normalizedTime - delay) / Mathf.Max(0.25f, 1f - delay));
            float s = Mathf.LerpUnclamped(0.74f, 1f, BackOut(localU));
            float sign = (i % 2 == 0) ? 1f : -1f;
            float tilt = Mathf.Sin(localU * Mathf.PI) * (1f - localU) * 2.6f * sign;

            el.style.opacity = Mathf.Clamp01(localU * 2.2f);
            el.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
            el.style.rotate = new StyleRotate(new Rotate(new Angle(tilt, AngleUnit.Degree)));
        }
    }

    private static void ClearColumnInlineTransforms(VisualElement col)
    {
        if (col == null) return;
        col.style.opacity = StyleKeyword.Null;
        col.style.scale = StyleKeyword.Null;
        col.style.translate = StyleKeyword.Null;
        col.style.rotate = StyleKeyword.Null;
    }

    private static void ClearTargetsInlineTransforms(List<VisualElement> targets)
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Count; i++)
        {
            var el = targets[i];
            if (el == null) continue;
            el.style.opacity = StyleKeyword.Null;
            el.style.scale = StyleKeyword.Null;
            el.style.rotate = StyleKeyword.Null;
        }
    }

    private static float EaseOutCubic(float t)
    {
        float inv = 1f - Mathf.Clamp01(t);
        return 1f - (inv * inv * inv);
    }

    private static float BackOut(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private static readonly HashSet<VisualElement> registeredAudioElements = new HashSet<VisualElement>();

    /// <summary>
    /// Tags UI Toolkit buttons (including dynamically created Blocks.Sessions buttons)
    /// with cartoon color variants without touching any click callbacks or DataBindings.
    /// </summary>
    public static void ApplyCartoonButtonVariants(VisualElement root)
    {
        if (root == null) return;

        var buttons = new List<UIButton>();
        root.Query<UIButton>().ToList(buttons);

        foreach (var btn in buttons)
        {
            if (btn == null) continue;
            // Skip tiny header copy icon button
            if (btn.parent != null && btn.parent.ClassListContains("blocks-copy-session--header"))
                continue;

            string upper = (btn.text ?? string.Empty).Trim().ToUpperInvariant();
            string name = btn.name ?? string.Empty;

            btn.RemoveFromClassList("cartoon-btn--green");
            btn.RemoveFromClassList("cartoon-btn--orange");
            btn.RemoveFromClassList("cartoon-btn--blue");
            btn.RemoveFromClassList("cartoon-btn--red");

            if (name == "btn-confirm-name" || name == "btn-start" || upper == "CREATE" || upper == "CRIAR" || upper == "CRIAR SALA" || name == "btn-quit-confirm-cancel")
            {
                btn.AddToClassList("cartoon-btn--blue");
            }
            else if (name == "btn-ready" || upper == "JOIN" || upper == "ENTRAR" || upper == "ENTRAR NA SALA" || upper == "REFRESH LIST" || upper == "ATUALIZAR LISTA" || upper == "CANCEL READY" || upper == "CANCELAR PRONTO")
            {
                btn.AddToClassList("cartoon-btn--orange");
            }
            else if (name == "btn-lobby-quit-game" || name == "btn-quit-confirm-ok" || upper.Contains("LEAVE") || upper.Contains("SAIR"))
            {
                btn.AddToClassList("cartoon-btn--red");
            }
            else
            {
                btn.AddToClassList("cartoon-btn--blue");
            }

            if (!registeredAudioElements.Contains(btn))
            {
                registeredAudioElements.Add(btn);
                btn.RegisterCallback<MouseEnterEvent>(evt =>
                {
                    if (btn.enabledSelf)
                        GameAudioManager.Instance?.PlayButtonHover();
                });
                btn.RegisterCallback<ClickEvent>(evt =>
                {
                    if (!btn.enabledSelf) return;
                    string u = (btn.text ?? string.Empty).Trim().ToUpperInvariant();
                    string n = btn.name ?? string.Empty;
                    if (n == "btn-lobby-quit-game" || u.Contains("LEAVE") || u.Contains("SAIR"))
                    {
                        GameAudioManager.Instance?.PlayButtonCancel();
                    }
                    else if (n == "btn-confirm-name" || n == "btn-start" || u.Contains("CRIAR") || u.Contains("ENTRAR") || u == "CREATE" || u == "JOIN" || n == "btn-ready")
                    {
                        GameAudioManager.Instance?.PlayButtonConfirm();
                    }
                    else
                    {
                        GameAudioManager.Instance?.PlayButtonClick();
                    }
                });
            }
        }

        var sliders = new List<UnityEngine.UIElements.Slider>();
        root.Query<UnityEngine.UIElements.Slider>().ToList(sliders);
        foreach (var slider in sliders)
        {
            if (slider == null || registeredAudioElements.Contains(slider)) continue;
            registeredAudioElements.Add(slider);
            slider.RegisterValueChangedCallback(evt =>
            {
                GameAudioManager.Instance?.PlaySliderTick();
            });
        }
    }

    /// <summary>
    /// Opens confirmation dialog before closing the game.
    /// </summary>
    public void OnQuitGameClicked()
    {
        if (isQuittingGame)
            return;

        OpenQuitConfirmDialog();
    }

    public void OpenQuitConfirmDialog()
    {
        GameAudioManager.Instance?.PlayCardPop();

        if (currentStep == MenuStep.StartScreen || currentStep == MenuStep.NicknameWindow)
        {
            if (uguiQuitConfirmDialog != null)
            {
                uguiQuitConfirmDialog.SetActive(true);
                var anim = uguiQuitConfirmDialog.GetComponentInChildren<CartoonButtonAnimator>();
                if (anim != null)
                {
                    anim.PlayPopBounce(0.68f, 6.0f, 0f);
                }
            }
        }
        else
        {
            if (toolkitQuitConfirmModal != null)
            {
                toolkitQuitConfirmModal.style.display = DisplayStyle.Flex;
                toolkitQuitConfirmModal.style.opacity = 1f;
            }
        }
    }

    public void OnCancelQuitClicked()
    {
        GameAudioManager.Instance?.PlayButtonCancel();
        CloseQuitConfirmDialog();
    }

    public void CloseQuitConfirmDialog()
    {
        if (uguiQuitConfirmDialog != null)
            uguiQuitConfirmDialog.SetActive(false);

        if (toolkitQuitConfirmModal != null)
            toolkitQuitConfirmModal.style.display = DisplayStyle.None;
    }

    public void OnConfirmQuitClicked()
    {
        if (isQuittingGame)
            return;

        GameAudioManager.Instance?.PlayButtonConfirm();
        CloseQuitConfirmDialog();
        StartCoroutine(QuitGameRoutine());
    }

    public void OnToggleMuteClicked()
    {
        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.ToggleMenuMute();
            bool isMuted = GameAudioManager.Instance.IsMenuMuted;
            if (isMuted)
                GameAudioManager.Instance.PlayButtonCancel();
            else
                GameAudioManager.Instance.PlayButtonConfirm();

            UpdateMuteButtonVisuals(isMuted);
        }
    }

    private void OnAudioMuteStateChanged(bool isMuted)
    {
        UpdateMuteButtonVisuals(isMuted);
    }

    private void OnAudioVolumeChanged(float volume)
    {
        UpdateVolumeVisuals(volume, GameAudioManager.IsMasterMuted);
    }

    private void OnUguiVolumeChanged(float val)
    {
        float vol01 = Mathf.Clamp01(val / 100f);
        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.SetMasterVolumeLevel(vol01);
            GameAudioManager.Instance.PlaySliderTick();
        }
        else
        {
            GameAudioManager.MasterVolume = vol01;
        }
    }

    private void OnToolkitVolumeChanged(ChangeEvent<float> evt)
    {
        float vol01 = Mathf.Clamp01(evt.newValue / 100f);
        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.SetMasterVolumeLevel(vol01);
            GameAudioManager.Instance.PlaySliderTick();
        }
        else
        {
            GameAudioManager.MasterVolume = vol01;
        }
    }

    private void UpdateMuteButtonVisuals(bool isMuted)
    {
        string text = isMuted ? "🔇 MUTADO" : "🔊 SOM";

        if (uguiMuteButton != null)
        {
            var txt = uguiMuteButton.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txt != null)
                txt.text = text;
        }

        if (uguiMuteIconImage != null)
        {
#if UNITY_EDITOR
            if (muteSoundOnSprite == null)
                muteSoundOnSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_sound_on.png");
            if (muteSoundOffSprite == null)
                muteSoundOffSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_sound_off.png");
#endif
            uguiMuteIconImage.sprite = isMuted ? muteSoundOffSprite : muteSoundOnSprite;
            uguiMuteIconImage.color = isMuted
                ? new Color(214f / 255f, 60f / 255f, 42f / 255f, 1f)
                : new Color(82f / 255f, 54f / 255f, 34f / 255f, 1f);
        }

        if (toolkitMuteIcon != null)
        {
            toolkitMuteIcon.EnableInClassList("menu-mute-icon--muted", isMuted);
        }

        if (toolkitMuteButton != null)
        {
            toolkitMuteButton.tooltip = isMuted ? "Desmutar áudio" : "Mutar áudio";
        }

        UpdateVolumeVisuals(GameAudioManager.MasterVolume, isMuted);
    }

    private void UpdateVolumeVisuals(float volume, bool isMuted)
    {
        float displayVol = isMuted ? 0f : (volume * 100f);
        string percentText = isMuted ? "0%" : $"{Mathf.RoundToInt(volume * 100f)}%";

        if (uguiVolumeSlider != null)
        {
            if (Mathf.Abs(uguiVolumeSlider.value - displayVol) > 0.5f)
            {
                uguiVolumeSlider.SetValueWithoutNotify(displayVol);
            }
        }

        if (uguiVolumePercentText != null)
        {
            uguiVolumePercentText.text = percentText;
        }

        if (toolkitVolumeSlider != null)
        {
            if (Mathf.Abs(toolkitVolumeSlider.value - displayVol) > 0.5f)
            {
                toolkitVolumeSlider.SetValueWithoutNotify(displayVol);
            }
        }

        if (toolkitVolumePercentLabel != null)
        {
            toolkitVolumePercentLabel.text = percentText;
        }
    }

    private IEnumerator QuitGameRoutine()
    {
        isQuittingGame = true;

        if (uguiQuitButton != null)
        {
            uguiQuitButton.interactable = false;
            var txt = uguiQuitButton.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txt != null)
                txt.text = "SAINDO...";
        }

        if (toolkitQuitButton != null)
        {
            toolkitQuitButton.SetEnabled(false);
            toolkitQuitButton.text = "SAINDO...";
        }

        try
        {
            PurgeActiveMultiplayerSessions();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LobbyMenuFlowController] Falha ao limpar sessões durante saída: {ex.Message}");
        }

        try
        {
            CancelPendingConnection();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LobbyMenuFlowController] Falha ao cancelar conexão pendente durante saída: {ex.Message}");
        }

        try
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LobbyMenuFlowController] Falha ao encerrar NetworkManager durante saída: {ex.Message}");
        }

        yield return new WaitForSecondsRealtime(0.35f);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
