using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

/// <summary>
/// Controla o menu retangular de opções/pausa in-game (tema Cartoon),
/// incluindo indicador [ESC] no canto superior esquerdo, mute global
/// no canto superior direito do card, "Sair da Partida" e "Sair do Jogo".
/// </summary>
[DisallowMultipleComponent]
public class InGamePauseMenuController : MonoBehaviour
{
    public const string AudioMutedPrefKey = "EOTWH_AudioMuted";
    private const string LobbySceneName = "JoinByBrowsing";

    private const string RootElementName = "pause-root";
    private const string EscHintButtonName = "esc-hint-button";
    private const string PauseOverlayName = "pause-overlay";
    private const string PauseCardName = "pause-card";
    private const string MuteButtonName = "btn-toggle-mute";
    private const string MuteLabelName = "mute-label";
    private const string LeaveMatchButtonName = "btn-leave-match";
    private const string QuitGameButtonName = "btn-quit-game";
    private const string ToggleHudButtonName = "btn-toggle-hud";

    private const string GameOverOverlayName = "gameover-overlay";
    private const string GameOverCardName = "gameover-card";
    private const string GameOverBadgeName = "gameover-badge";
    private const string GameOverIconName = "gameover-icon";
    private const string GameOverRibbonName = "gameover-header-ribbon";
    private const string GameOverTitleName = "gameover-title";
    private const string GameOverSubtitleName = "gameover-subtitle";
    private const string GameOverDescriptionName = "gameover-description";
    private const string GameOverTimerLabelName = "gameover-timer-label";
    private const string GameOverProgressFillName = "gameover-progress-fill";
    private const string GameOverLeaveButtonName = "btn-gameover-leave";

    private const string OverlayVisibleClass = "pause-overlay--visible";
    private const string MuteButtonMutedClass = "btn-mute--muted";
    private const string ButtonDisabledClass = "pause-btn--disabled";

    private const string GameOverVisibleClass = "gameover-overlay--visible";
    private const string GameOverRibbonVictoryClass = "gameover-ribbon--victory";
    private const string GameOverRibbonDefeatClass = "gameover-ribbon--defeat";
    private const string GameOverBadgeDefeatClass = "gameover-badge--defeat";
    private const string GameOverIconVictoryClass = "gameover-icon--victory";
    private const string GameOverIconDefeatClass = "gameover-icon--defeat";
    private const string GameOverProgressFillDefeatClass = "gameover-progress-fill--defeat";

    public static InGamePauseMenuController Instance { get; private set; }
    public static bool IsMenuOpen { get; set; }
    public static bool IsLeavingMatch { get; set; }
    public static bool IsGameOverVisible { get; set; }

    public static void ResetMenuState()
    {
        IsLeavingMatch = false;
        IsMenuOpen = false;
        IsGameOverVisible = false;
    }

    [Header("Referências")]
    [SerializeField] private UIDocument pauseMenuDocument;

    private VisualElement rootElement;
    private Button escHintButton;
    private VisualElement pauseOverlay;
    private VisualElement pauseCard;
    private Button muteButton;
    private Label muteLabel;
    private Slider volumeSlider;
    private Label volumePercentLabel;
    private Button leaveMatchButton;
    private Button quitGameButton;
    private Button toggleHudButton;

    // Elementos do painel de Fim de Partida (Vitória / Derrota no tema Cartoon)
    private VisualElement gameOverOverlay;
    private VisualElement gameOverCard;
    private VisualElement gameOverBadge;
    private VisualElement gameOverIcon;
    private VisualElement gameOverHeaderRibbon;
    private Label gameOverTitleLabel;
    private Label gameOverSubtitleLabel;
    private Label gameOverDescriptionLabel;
    private Label gameOverTimerLabel;
    private VisualElement gameOverProgressFill;
    private Button gameOverLeaveButton;

    private bool isHudVisible = true;

    private readonly List<CinemachineInputAxisController> disabledCameraControllers = new List<CinemachineInputAxisController>();

    /// <summary>
    /// Trava e oculta o cursor do mouse durante a gameplay ativa.
    /// </summary>
    public static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Destrava e exibe o cursor do mouse para menus, pausa e interações de UI.
    /// </summary>
    public static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitSceneCursorListener()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedForCursor;
    }

    private static void OnSceneLoadedForCursor(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name == "JoinByBrowsing")
        {
            UnlockCursor();
        }
        else if (scene.name == "GameScene")
        {
            LockCursor();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyPersistedMuteStateOnLoad()
    {
        ApplyGlobalAudioVolume(LoadMutedPreference());
    }

    public static bool IsMuted => LoadMutedPreference();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InGamePauseMenuController] Instância duplicada detectada; mantendo a primeira.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        IsMenuOpen = false;
        IsLeavingMatch = false;
        IsGameOverVisible = false;

        if (pauseMenuDocument == null)
        {
            pauseMenuDocument = GetComponent<UIDocument>();
        }

        EnsureEventSystemExists();
        ApplyGlobalAudioVolume(LoadMutedPreference());
    }

    private void OnEnable()
    {
        GameAudioManager.OnMasterVolumeChanged += OnGlobalAudioVolumeChanged;
        GameAudioManager.OnMasterMuteChanged += OnGlobalAudioMuteChanged;
        BindUI();
        RefreshMuteButtonVisual();
        ApplyOverlayState(open: false);
    }

    private void Start()
    {
        if (!IsMenuOpen && !IsGameOverVisible && !IsLeavingMatch)
        {
            LockCursor();
        }
        else
        {
            UnlockCursor();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            if (!IsMenuOpen && !IsGameOverVisible && !IsLeavingMatch)
            {
                LockCursor();
            }
            else
            {
                UnlockCursor();
            }
        }
    }

    private void OnDisable()
    {
        GameAudioManager.OnMasterVolumeChanged -= OnGlobalAudioVolumeChanged;
        GameAudioManager.OnMasterMuteChanged -= OnGlobalAudioMuteChanged;
        UnbindUI();
        RestoreGameplayInputState(keepCursorUnlocked: IsLeavingMatch);
        IsMenuOpen = false;
    }

    private void OnGlobalAudioVolumeChanged(float vol)
    {
        RefreshMuteButtonVisual();
    }

    private void OnGlobalAudioMuteChanged(bool muted)
    {
        RefreshMuteButtonVisual();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsMenuOpen = false;
        }
    }

    private void Update()
    {
        if (IsLeavingMatch || AirplaneCrashCutsceneController.IsAnyCutsceneRunning)
        {
            return;
        }

        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver)
        {
            if (IsMenuOpen)
            {
                CloseMenu();
            }
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            ToggleMenu();
            return;
        }

        if (IsMenuOpen && keyboard != null && keyboard.pKey.wasPressedThisFrame)
        {
            HandleToggleHudClicked();
            return;
        }

        // Durante a gameplay ativa com o menu fechado, garante que o cursor permaneça travado e oculto ao clicar no jogo
        if (!IsMenuOpen && !IsGameOverVisible && !IsLeavingMatch)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            {
                if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
                {
                    LockCursor();
                }
            }
        }
    }

    /// <summary>
    /// Alterna entre abrir e fechar o menu de pausa in-game.
    /// </summary>
    public void ToggleMenu()
    {
        if (IsLeavingMatch || AirplaneCrashCutsceneController.IsAnyCutsceneRunning)
        {
            return;
        }

        if (IsMenuOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    /// <summary>
    /// Abre o menu de pausa, libera o cursor e congela controle de câmera local.
    /// </summary>
    public void OpenMenu()
    {
        if (IsLeavingMatch || AirplaneCrashCutsceneController.IsAnyCutsceneRunning)
        {
            return;
        }

        if (GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver)
        {
            return;
        }

        if (IsMenuOpen)
        {
            return;
        }

        if (rootElement != null)
        {
            rootElement.style.display = DisplayStyle.Flex;
        }

        IsMenuOpen = true;
        if (toggleHudButton != null)
        {
            bool isHidden = GameHUDController.Instance != null && GameHUDController.Instance.IsHudHiddenByPlayer;
            toggleHudButton.text = isHidden ? "EXIBIR HUD (P)" : "OCULTAR HUD (P)";
        }
        GameAudioManager.Instance?.PlayPauseMenuOpen();
        SaveAndUnlockCursor();
        DisableLocalCameraControllers();
        PlayerController.LocalInstance?.CancelTaskInteraction();
        ApplyOverlayState(open: true);
    }

    /// <summary>
    /// Fecha o menu de pausa e restaura câmera e cursor do jogador.
    /// </summary>
    public void CloseMenu()
    {
        if (!IsMenuOpen && (pauseOverlay == null || !pauseOverlay.ClassListContains(OverlayVisibleClass)))
        {
            return;
        }

        IsMenuOpen = false;
        GameAudioManager.Instance?.PlayPauseMenuClose();
        ApplyOverlayState(open: false);

        if (rootElement != null)
        {
            rootElement.style.display = (isHudVisible || IsGameOverVisible) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        RestoreGameplayInputState(keepCursorUnlocked: IsLeavingMatch);
    }

    /// <summary>
    /// Sincroniza visibilidade do indicador [ESC] e fecha o menu caso o HUD seja ocultado (ex: Game Over).
    /// </summary>
    public void SetHudVisible(bool visible)
    {
        isHudVisible = visible;

        if (!visible && IsMenuOpen)
        {
            CloseMenu();
        }

        if (escHintButton != null)
        {
            escHintButton.style.display = (visible && !IsMenuOpen && !IsGameOverVisible) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (rootElement != null)
        {
            rootElement.style.display = (visible || IsGameOverVisible) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>
    /// Exibe o painel de Fim de Jogo (Vitória ou Derrota) com o visual e fontes padronizados com o menu.
    /// </summary>
    public void ShowGameOver(bool isVictory, string title, string subtitle, string description, float duration)
    {
        IsGameOverVisible = true;

        if (gameOverOverlay == null || rootElement == null)
        {
            BindUI();
        }

        if (IsMenuOpen)
        {
            CloseMenu();
        }

        if (escHintButton != null)
        {
            escHintButton.style.display = DisplayStyle.None;
        }

        if (rootElement != null)
        {
            rootElement.style.display = DisplayStyle.Flex;
        }

        SaveAndUnlockCursor();
        DisableLocalCameraControllers();
        PlayerController.LocalInstance?.CancelTaskInteraction();

        if (gameOverTitleLabel != null) gameOverTitleLabel.text = title;
        if (gameOverSubtitleLabel != null) gameOverSubtitleLabel.text = subtitle;
        if (gameOverDescriptionLabel != null) gameOverDescriptionLabel.text = description;

        if (gameOverHeaderRibbon != null)
        {
            gameOverHeaderRibbon.EnableInClassList(GameOverRibbonVictoryClass, isVictory);
            gameOverHeaderRibbon.EnableInClassList(GameOverRibbonDefeatClass, !isVictory);
        }

        if (gameOverBadge != null)
        {
            gameOverBadge.EnableInClassList(GameOverBadgeDefeatClass, !isVictory);
        }

        if (gameOverIcon != null)
        {
            gameOverIcon.EnableInClassList(GameOverIconVictoryClass, isVictory);
            gameOverIcon.EnableInClassList(GameOverIconDefeatClass, !isVictory);
        }

        if (gameOverProgressFill != null)
        {
            gameOverProgressFill.EnableInClassList(GameOverProgressFillDefeatClass, !isVictory);
        }

        UpdateGameOverCountdown(duration, duration);

        if (gameOverOverlay != null)
        {
            gameOverOverlay.AddToClassList(GameOverVisibleClass);
        }

        if (gameOverCard != null)
        {
            gameOverCard.transform.scale = Vector3.one * 0.76f;
            StartCoroutine(AnimateGameOverCardEntranceRoutine());
        }
    }

    private IEnumerator AnimateGameOverCardEntranceRoutine()
    {
        if (gameOverCard == null) yield break;

        float duration = 0.28f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            float scale = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            float currentScale = Mathf.LerpUnclamped(0.76f, 1f, scale);
            gameOverCard.transform.scale = Vector3.one * currentScale;
            yield return null;
        }
        gameOverCard.transform.scale = Vector3.one;
    }

    /// <summary>
    /// Atualiza em tempo real o contador e barra de progresso da tela de vitória/derrota.
    /// </summary>
    public void UpdateGameOverCountdown(float remainingSeconds, float totalDuration)
    {
        if (gameOverTimerLabel == null && pauseMenuDocument != null && pauseMenuDocument.rootVisualElement != null)
        {
            BindUI();
        }

        if (gameOverTimerLabel != null)
        {
            int secs = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            gameOverTimerLabel.text = $"Retornando ao saguão em {secs}s...";
        }

        if (gameOverProgressFill != null)
        {
            float ratio = Mathf.Clamp01(remainingSeconds / Mathf.Max(0.1f, totalDuration));
            gameOverProgressFill.style.width = new Length(ratio * 100f, LengthUnit.Percent);
        }
    }

    /// <summary>
    /// Alterna o estado de som mutado/ativo em toda a aplicação.
    /// </summary>
    public void ToggleMute()
    {
        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.ToggleAudioMute();
            if (GameAudioManager.IsMasterMuted)
                GameAudioManager.Instance.PlayButtonCancel();
            else
                GameAudioManager.Instance.PlayButtonConfirm();
        }
        else
        {
            bool nextMuted = !LoadMutedPreference();
            SetMuted(nextMuted);
        }
        RefreshMuteButtonVisual();
    }

    /// <summary>
    /// Define explicitamente se o som do jogo está mutado e persiste em PlayerPrefs.
    /// </summary>
    public void SetMuted(bool muted)
    {
        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.SetAudioMute(muted);
        }
        else
        {
            PlayerPrefs.SetInt(AudioMutedPrefKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyGlobalAudioVolume(muted);
        }
        RefreshMuteButtonVisual();
    }

    /// <summary>
    /// Dispara a saída voluntária da partida atual de volta para JoinByBrowsing.
    /// </summary>
    public void TriggerLeaveMatch()
    {
        if (IsLeavingMatch)
        {
            return;
        }

        StartCoroutine(LeaveMatchRoutine());
    }

    /// <summary>
    /// Fecha a partida/rede e encerra a aplicação (ou sai do Play Mode no Editor).
    /// </summary>
    public void TriggerQuitGame()
    {
        if (IsLeavingMatch)
        {
            return;
        }

        IsLeavingMatch = true;
        SetActionButtonsInteractable(false, leavingLabel: null);
        StartCoroutine(QuitGameRoutine());
    }

    private void BindUI()
    {
        if (pauseMenuDocument == null)
        {
            pauseMenuDocument = GetComponent<UIDocument>();
        }

        if (pauseMenuDocument == null)
        {
            Debug.LogError("[InGamePauseMenuController] UIDocument não encontrado.");
            return;
        }

        VisualElement docRoot = pauseMenuDocument.rootVisualElement;
        if (docRoot == null)
        {
            return;
        }

        rootElement = docRoot.Q<VisualElement>(RootElementName);
        escHintButton = docRoot.Q<Button>(EscHintButtonName);
        pauseOverlay = docRoot.Q<VisualElement>(PauseOverlayName);
        pauseCard = docRoot.Q<VisualElement>(PauseCardName);
        muteButton = docRoot.Q<Button>(MuteButtonName);
        muteLabel = docRoot.Q<Label>(MuteLabelName);
        volumeSlider = docRoot.Q<Slider>("pause-volume-slider");
        volumePercentLabel = docRoot.Q<Label>("pause-volume-percent");
        leaveMatchButton = docRoot.Q<Button>(LeaveMatchButtonName);
        quitGameButton = docRoot.Q<Button>(QuitGameButtonName);
        toggleHudButton = docRoot.Q<Button>(ToggleHudButtonName);

        // GameOver UI Toolkit elements
        gameOverOverlay = docRoot.Q<VisualElement>(GameOverOverlayName);
        gameOverCard = docRoot.Q<VisualElement>(GameOverCardName);
        gameOverBadge = docRoot.Q<VisualElement>(GameOverBadgeName);
        gameOverIcon = docRoot.Q<VisualElement>(GameOverIconName);
        gameOverHeaderRibbon = docRoot.Q<VisualElement>(GameOverRibbonName);
        gameOverTitleLabel = docRoot.Q<Label>(GameOverTitleName);
        gameOverSubtitleLabel = docRoot.Q<Label>(GameOverSubtitleName);
        gameOverDescriptionLabel = docRoot.Q<Label>(GameOverDescriptionName);
        gameOverTimerLabel = docRoot.Q<Label>(GameOverTimerLabelName);
        gameOverProgressFill = docRoot.Q<VisualElement>(GameOverProgressFillName);
        gameOverLeaveButton = docRoot.Q<Button>(GameOverLeaveButtonName);

        if (escHintButton != null)
        {
            escHintButton.clicked += HandleEscHintClicked;
        }

        if (muteButton != null)
        {
            muteButton.clicked += ToggleMute;
        }

        if (volumeSlider != null)
        {
            volumeSlider.RegisterValueChangedCallback(OnVolumeSliderChanged);
        }

        if (leaveMatchButton != null)
        {
            leaveMatchButton.clicked += TriggerLeaveMatch;
        }

        if (quitGameButton != null)
        {
            quitGameButton.clicked += TriggerQuitGame;
        }

        if (toggleHudButton != null)
        {
            toggleHudButton.clicked += HandleToggleHudClicked;
        }

        if (gameOverLeaveButton != null)
        {
            gameOverLeaveButton.clicked += TriggerLeaveMatch;
        }

        if (pauseOverlay != null)
        {
            pauseOverlay.RegisterCallback<PointerDownEvent>(HandleBackdropPointerDown);
        }

        if (pauseCard != null)
        {
            pauseCard.RegisterCallback<PointerDownEvent>(StopPointerPropagation);
        }

        if (rootElement != null)
        {
            rootElement.style.display = isHudVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void UnbindUI()
    {
        if (escHintButton != null)
        {
            escHintButton.clicked -= HandleEscHintClicked;
        }

        if (muteButton != null)
        {
            muteButton.clicked -= ToggleMute;
        }

        if (volumeSlider != null)
        {
            volumeSlider.UnregisterValueChangedCallback(OnVolumeSliderChanged);
        }

        if (leaveMatchButton != null)
        {
            leaveMatchButton.clicked -= TriggerLeaveMatch;
        }

        if (quitGameButton != null)
        {
            quitGameButton.clicked -= TriggerQuitGame;
        }

        if (toggleHudButton != null)
        {
            toggleHudButton.clicked -= HandleToggleHudClicked;
        }

        if (gameOverLeaveButton != null)
        {
            gameOverLeaveButton.clicked -= TriggerLeaveMatch;
        }

        if (pauseOverlay != null)
        {
            pauseOverlay.UnregisterCallback<PointerDownEvent>(HandleBackdropPointerDown);
        }

        if (pauseCard != null)
        {
            pauseCard.UnregisterCallback<PointerDownEvent>(StopPointerPropagation);
        }
    }

    private void HandleToggleHudClicked()
    {
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.ToggleHudVisibility();
        }
        CloseMenu();
    }

    private void HandleEscHintClicked()
    {
        if (!IsMenuOpen)
        {
            OpenMenu();
        }
    }

    private void HandleBackdropPointerDown(PointerDownEvent evt)
    {
        if (evt.target == pauseOverlay && IsMenuOpen && !IsLeavingMatch)
        {
            CloseMenu();
        }
    }

    private static void StopPointerPropagation(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private void ApplyOverlayState(bool open)
    {
        if (pauseOverlay != null)
        {
            pauseOverlay.EnableInClassList(OverlayVisibleClass, open);
        }

        if (escHintButton != null)
        {
            escHintButton.style.display = (open || !isHudVisible || IsGameOverVisible) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private void OnVolumeSliderChanged(ChangeEvent<float> evt)
    {
        float newVolume01 = Mathf.Clamp01(evt.newValue / 100f);
        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.SetMasterVolumeLevel(newVolume01);
            GameAudioManager.Instance.PlaySliderTick();
        }
        else
        {
            PlayerPrefs.SetFloat(GameAudioManager.MasterVolumePrefKey, newVolume01);
            if (newVolume01 > 0.01f && PlayerPrefs.GetInt(AudioMutedPrefKey, 0) == 1)
            {
                PlayerPrefs.SetInt(AudioMutedPrefKey, 0);
            }
            PlayerPrefs.Save();
            AudioListener.volume = PlayerPrefs.GetInt(AudioMutedPrefKey, 0) == 1 ? 0f : newVolume01;
        }

        RefreshMuteButtonVisual();
    }

    private void RefreshMuteButtonVisual()
    {
        bool muted = GameAudioManager.IsMasterMuted;
        float currentVol = GameAudioManager.MasterVolume;

        if (muteButton != null)
        {
            muteButton.EnableInClassList(MuteButtonMutedClass, muted);
        }

        if (muteLabel != null)
        {
            muteLabel.text = muted ? "MUDO" : "SOM";
        }

        if (volumeSlider != null)
        {
            float targetVal = muted ? 0f : (currentVol * 100f);
            if (Mathf.Abs(volumeSlider.value - targetVal) > 0.5f)
            {
                volumeSlider.SetValueWithoutNotify(targetVal);
            }
        }

        if (volumePercentLabel != null)
        {
            int pct = muted ? 0 : Mathf.RoundToInt(currentVol * 100f);
            volumePercentLabel.text = $"{pct}%";
        }
    }

    private void SetActionButtonsInteractable(bool interactable, string leavingLabel)
    {
        if (leaveMatchButton != null)
        {
            leaveMatchButton.SetEnabled(interactable);
            leaveMatchButton.EnableInClassList(ButtonDisabledClass, !interactable);
            if (!string.IsNullOrEmpty(leavingLabel))
            {
                leaveMatchButton.text = leavingLabel;
            }
        }

        if (quitGameButton != null)
        {
            quitGameButton.SetEnabled(interactable);
            quitGameButton.EnableInClassList(ButtonDisabledClass, !interactable);
        }

        if (gameOverLeaveButton != null)
        {
            gameOverLeaveButton.SetEnabled(interactable);
            gameOverLeaveButton.EnableInClassList(ButtonDisabledClass, !interactable);
            if (!string.IsNullOrEmpty(leavingLabel))
            {
                gameOverLeaveButton.text = leavingLabel;
            }
        }
    }

    private IEnumerator LeaveMatchRoutine()
    {
        IsLeavingMatch = true;
        SetActionButtonsInteractable(false, leavingLabel: "SAINDO...");

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerController.LocalInstance?.CancelTaskInteraction();

        // Exibe a tela de carregamento animada com a caveira da Morte ao retornar ao saguão
        if (GameLoadingScreenController.Instance != null)
        {
            GameLoadingScreenController.Instance.Show("RETORNANDO AO SAGUÃO...", "Desconectando da partida...", minDisplayTime: 1.5f, autoDismiss: true);
        }

        // Sinaliza para a cena de saguão (JoinByBrowsing) abrir direto no menu de escolher sala
        LobbyMenuFlowController.ForceReturnToRoomBrowser = true;

        yield return LeaveActiveMultiplayerSessionsCoroutine(timeoutSeconds: 3f);
        yield return ShutdownNetworkManagerCoroutine(timeoutSeconds: 2f);

        IsMenuOpen = false;
        SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
    }

    private IEnumerator QuitGameRoutine()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        yield return LeaveActiveMultiplayerSessionsCoroutine(timeoutSeconds: 2f);
        yield return ShutdownNetworkManagerCoroutine(timeoutSeconds: 1.5f);

        IsMenuOpen = false;
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public static IEnumerator LeaveActiveMultiplayerSessionsCoroutine(float timeoutSeconds)
    {
        IMultiplayerService service = null;
        try
        {
            service = MultiplayerService.Instance;
        }
        catch (Exception)
        {
            service = null;
        }

        if (service?.Sessions == null || service.Sessions.Count == 0)
        {
            yield break;
        }

        var leaveTasks = new List<Task>(service.Sessions.Count);
        foreach (ISession session in new List<ISession>(service.Sessions.Values))
        {
            if (session == null)
            {
                continue;
            }

            try
            {
                if (session.IsHost)
                {
                    leaveTasks.Add(session.AsHost().DeleteAsync());
                }
                else
                {
                    leaveTasks.Add(session.LeaveAsync());
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InGamePauseMenuController] Erro ao iniciar saída da sessão: {ex.Message}");
            }
        }

        if (leaveTasks.Count == 0)
        {
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool allDone = true;
            for (int i = 0; i < leaveTasks.Count; i++)
            {
                if (!leaveTasks[i].IsCompleted)
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                break;
            }

            yield return null;
        }
    }

    public static IEnumerator ShutdownNetworkManagerCoroutine(float timeoutSeconds)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
        {
            yield break;
        }

        nm.ConnectionApprovalCallback = null;
        if (nm.NetworkConfig != null)
        {
            nm.NetworkConfig.ConnectionApproval = false;
        }

        if (nm.IsListening || nm.IsClient || nm.IsServer)
        {
            nm.Shutdown();
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (nm != null && nm.ShutdownInProgress && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private void SaveAndUnlockCursor()
    {
        UnlockCursor();
    }

    private void RestoreGameplayInputState(bool keepCursorUnlocked)
    {
        RestoreLocalCameraControllers();

        if (keepCursorUnlocked)
        {
            UnlockCursor();
            return;
        }

        LockCursor();
    }

    private void DisableLocalCameraControllers()
    {
        disabledCameraControllers.Clear();
        CinemachineInputAxisController[] controllers = FindObjectsByType<CinemachineInputAxisController>(
            FindObjectsInactive.Exclude);

        for (int i = 0; i < controllers.Length; i++)
        {
            CinemachineInputAxisController controller = controllers[i];
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
                disabledCameraControllers.Add(controller);
            }
        }
    }

    private void RestoreLocalCameraControllers()
    {
        for (int i = 0; i < disabledCameraControllers.Count; i++)
        {
            CinemachineInputAxisController controller = disabledCameraControllers[i];
            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        disabledCameraControllers.Clear();
    }

    private static bool LoadMutedPreference()
    {
        return GameAudioManager.IsMasterMuted;
    }

    private static void ApplyGlobalAudioVolume(bool muted)
    {
        GameAudioManager.ApplyGlobalAudioSettings();
    }

    private static void EnsureEventSystemExists()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        EventSystem existing = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
            }
            return;
        }

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        InputSystemUIInputModule uiModule = eventSystemGo.AddComponent<InputSystemUIInputModule>();
        uiModule.AssignDefaultActions();
    }
}
