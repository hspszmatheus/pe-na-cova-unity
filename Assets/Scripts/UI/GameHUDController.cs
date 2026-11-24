using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UniversityOfGames.ProgressBarToolkit;
using UIImage = UnityEngine.UI.Image;

public class GameHUDController : MonoBehaviour
{
    public static GameHUDController Instance { get; private set; }

    private bool isHudHiddenByPlayer = false;
    public bool IsHudHiddenByPlayer => isHudHiddenByPlayer;
    public bool IsHudVisible => !isHudHiddenByPlayer && (root == null || root.style.display != DisplayStyle.None);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("2D Progress Bar Toolkit (uGUI Canvas)")]
    [SerializeField] private Canvas progressBarsCanvas;

    [Header("Barras Compartilhadas (Normais)")]
    [SerializeField] private GameObject tasksProgressPanel;
    [SerializeField] private TMP_Text tasksTitleLabel;
    [SerializeField] private TMP_Text tasksCounterLabel;
    [SerializeField] private ClassicProgressBar tasksProgressBar;

    [SerializeField] private GameObject staminaContainer;
    [SerializeField] private CanvasGroup staminaCanvasGroup;
    [SerializeField] private ClassicProgressBar staminaProgressBar;

    [Header("Barras do Sobrevivente (Player)")]
    [SerializeField] private GameObject revelationTimerPanel;
    [SerializeField] private UIImage revelationBorderImage;
    [SerializeField] private TMP_Text revelationTitleLabel;
    [SerializeField] private TMP_Text revelationCountdownLabel;
    [SerializeField] private TMP_Text revelationStatusSublabel;
    [SerializeField] private ClassicProgressBar revelationProgressBar;

    [SerializeField] private GameObject kickCooldownPanel;
    [SerializeField] private TMP_Text kickCooldownStatusLabel;
    [SerializeField] private TMP_Text kickCooldownTitleLabel;
    [SerializeField] private CircularProgressBar kickCooldownBar;

    [SerializeField] private GameObject taskInteractionPanel;
    [SerializeField] private TMP_Text taskInteractionTitle;
    [SerializeField] private TMP_Text taskInteractionPercentLabel;
    [SerializeField] private TMP_Text taskInteractionHint;
    [SerializeField] private CircularProgressBar taskInteractionBar;

    [Header("Barras do Hunter (Morte)")]
    [SerializeField] private GameObject fireballCooldownPanel;
    [SerializeField] private TMP_Text fireballCooldownStatusLabel;
    [SerializeField] private TMP_Text fireballCooldownTitleLabel;
    [SerializeField] private CircularProgressBar fireballCooldownBar;

    [Header("Barras da Habilidade de Revelação da Morte (E)")]
    [SerializeField] private GameObject hunterRevealCooldownPanel;
    [SerializeField] private TMP_Text hunterRevealCooldownStatusLabel;
    [SerializeField] private TMP_Text hunterRevealCooldownTitleLabel;
    [SerializeField] private CircularProgressBar hunterRevealCooldownBar;

    [Header("Configurações das Vidas")]
    [SerializeField] private int maxHunterLives = 3;
    [SerializeField] private int currentHunterLives = 3;

    [Header("Ícones Cartoon do HUD")]
    [SerializeField] private Sprite fireballIconSprite;
    [SerializeField] private Sprite kickIconSprite;
    [SerializeField] private Sprite scytheIconSprite;
    [SerializeField] private Sprite killSkullIconSprite;

    private Sprite activeSkullSprite;
    private Sprite lostSkullSprite;

    private VisualElement root;
    private VisualElement topCenterContainer;
    private VisualElement hunterLivesPanel;
    private VisualElement hunterSkullsContainer;
    private VisualElement huntersLifeRoster;
    private VisualElement survivorHudContainer;
    private VisualElement hunterHudContainer;
    private VisualElement hunterCrosshair;
    private VisualElement hunterIntroCountdownPanel;
    private VisualElement hunterCountdownBadge;
    private Label hunterCountdownNumber;

    // Card inferior esquerdo de Controles Cartoon
    private VisualElement roleBadgeCard;
    private Label roleCtrl1Key;
    private Label roleCtrl1Action;
    private Label roleCtrl2Key;
    private Label roleCtrl2Action;
    private Label roleCtrl3Key;
    private Label roleCtrl3Action;

    // Cronômetro da partida (5min) e Contador de Sobreviventes Vivos
    private Label matchTimerLabel;
    private Label alivePlayersLabel;
    private VisualElement matchTimerBadge;
    private VisualElement alivePlayersBadge;

    // Kill Feed (Indicador de Eliminações estilo FPS Online)
    private VisualElement killFeedContainer;

    // Prompt de Proximidade da Task
    private VisualElement taskPromptPanel;
    private Label taskPromptLabel;

    private bool isLocalHunter = false;
    private bool isLocalSpectator = false;
    private bool isStaminaExhausted = false;

    private class HunterData
    {
        public ulong networkId;
        public string displayName;
        public int currentLives = 3;
        public int maxLives = 3;
    }

    private readonly Dictionary<ulong, HunterData> huntersData = new Dictionary<ulong, HunterData>();

    public VisualElement Root => root;
    public VisualElement TopCenterContainer => topCenterContainer;
    public VisualElement SurvivorContainer => survivorHudContainer;
    public VisualElement HunterContainer => hunterHudContainer;
    public VisualElement HunterCrosshair => hunterCrosshair;
    public int CurrentHunterLives => currentHunterLives;
    public int MaxHunterLives => maxHunterLives;

    /// <summary>
    /// Define a visibilidade geral do HUD.
    /// Utilizado para ocultar completamente todos os elementos gráficos durante a conclusão da partida (5s)
    /// ou alternar dinamicamente via tecla de atalho do jogador (tecla P).
    /// </summary>
    public void SetHudVisible(bool visible)
    {
        SetHudVisible(visible, false);
    }

    public void SetHudVisible(bool visible, bool isPlayerToggle)
    {
        if (isPlayerToggle)
        {
            isHudHiddenByPlayer = !visible;
        }
        else if (isHudHiddenByPlayer && visible)
        {
            // O jogador optou por manter o HUD oculto via tecla P; preserva sua escolha
            return;
        }

        if (root == null && uiDocument != null)
        {
            root = uiDocument.rootVisualElement;
        }

        if (root != null)
        {
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (progressBarsCanvas != null)
        {
            progressBarsCanvas.gameObject.SetActive(visible);
        }

        if (InGamePauseMenuController.Instance != null)
        {
            InGamePauseMenuController.Instance.SetHudVisible(visible);
        }
    }

    /// <summary>
    /// Alterna a visibilidade do HUD do jogo (vidas, menus, barras e atalhos) pelo atalho P.
    /// </summary>
    public void ToggleHudVisibility()
    {
        bool newVisible = isHudHiddenByPlayer; // Se estava oculto (true), passa a ser visível (true)
        SetHudVisible(newVisible, isPlayerToggle: true);
        GameAudioManager.Instance?.PlayButtonClick();
        Debug.Log($"[GameHUDController] HUD {(isHudHiddenByPlayer ? "ocultado" : "exibido")} (Atalho P).");
    }

    private void Update()
    {
        if (CheckToggleHudInput())
        {
            ToggleHudVisibility();
        }
    }

    private bool CheckToggleHudInput()
    {
        // Bloqueia caso uma cutscene cinematográfica esteja em execução ou esteja saindo da partida
        if (AirplaneCrashCutsceneController.IsAnyCutsceneRunning) return false;
        if (InGamePauseMenuController.IsLeavingMatch) return false;
        if (InGamePauseMenuController.IsGameOverVisible) return false;
        if (InGamePauseMenuController.IsMenuOpen) return false;

        bool pPressed = false;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame)
        {
            pPressed = true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!pPressed)
        {
            try { pPressed = Input.GetKeyDown(KeyCode.P); } catch { }
        }
#endif
        return pPressed;
    }

    void Awake()
    {
        isHudHiddenByPlayer = false;
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        InitializeUI();
    }

    void OnEnable()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }
        if (uiDocument != null)
        {
            root = uiDocument.rootVisualElement;
        }

        if (root != null)
        {
            topCenterContainer = root.Q<VisualElement>("top-center-container");
            hunterLivesPanel = root.Q<VisualElement>("hunter-lives-panel");
            hunterSkullsContainer = root.Q<VisualElement>("hunter-skulls-container");
            huntersLifeRoster = root.Q<VisualElement>("hunters-life-roster");
            survivorHudContainer = root.Q<VisualElement>("survivor-hud-container");
            hunterHudContainer = root.Q<VisualElement>("hunter-hud-container");
            hunterCrosshair = root.Q<VisualElement>("hunter-crosshair");
            hunterIntroCountdownPanel = root.Q<VisualElement>("hunter-intro-countdown-panel");
            hunterCountdownBadge = root.Q<VisualElement>("hunter-countdown-badge");
            hunterCountdownNumber = root.Q<Label>("hunter-countdown-number");

            roleBadgeCard = root.Q<VisualElement>("role-badge-card");
            roleCtrl1Key = root.Q<Label>("role-ctrl-1-key");
            roleCtrl1Action = root.Q<Label>("role-ctrl-1-action");
            roleCtrl2Key = root.Q<Label>("role-ctrl-2-key");
            roleCtrl2Action = root.Q<Label>("role-ctrl-2-action");
            roleCtrl3Key = root.Q<Label>("role-ctrl-3-key");
            roleCtrl3Action = root.Q<Label>("role-ctrl-3-action");

            matchTimerLabel = root.Q<Label>("match-timer-label");
            alivePlayersLabel = root.Q<Label>("alive-players-label");
            matchTimerBadge = root.Q<VisualElement>("match-timer-badge");
            alivePlayersBadge = root.Q<VisualElement>("alive-players-badge");
            killFeedContainer = root.Q<VisualElement>("kill-feed-container");

            taskPromptPanel = root.Q<VisualElement>("task-prompt-panel");
            taskPromptLabel = root.Q<Label>("task-prompt-label");
        }

        AutoBindToolkitReferences();

        EnsureSpritesLoaded();
        RefreshHunterRosterUI();
    }

    private void AutoBindToolkitReferences()
    {
        if (progressBarsCanvas == null)
        {
            Transform canvasTr = transform.Find("ProgressBarsCanvas");
            if (canvasTr != null)
            {
                progressBarsCanvas = canvasTr.GetComponent<Canvas>();
            }
        }

        if (progressBarsCanvas == null) return;
        Transform rootTr = progressBarsCanvas.transform;

        if (tasksProgressPanel == null) tasksProgressPanel = FindChildGo(rootTr, "TasksProgressPanel");
        if (tasksProgressPanel != null)
        {
            if (tasksTitleLabel == null) tasksTitleLabel = FindChildComponent<TMP_Text>(tasksProgressPanel.transform, "TasksTitleLabel");
            if (tasksCounterLabel == null) tasksCounterLabel = FindChildComponent<TMP_Text>(tasksProgressPanel.transform, "TasksCounterLabel");
            if (tasksProgressBar == null) tasksProgressBar = FindChildComponent<ClassicProgressBar>(tasksProgressPanel.transform, "TasksProgressBar");
        }

        if (staminaContainer == null) staminaContainer = FindChildGo(rootTr, "StaminaContainer");
        if (staminaContainer != null)
        {
            if (staminaCanvasGroup == null) staminaCanvasGroup = staminaContainer.GetComponent<CanvasGroup>();
            if (staminaProgressBar == null) staminaProgressBar = FindChildComponent<ClassicProgressBar>(staminaContainer.transform, "StaminaProgressBar");
        }

        if (revelationTimerPanel == null) revelationTimerPanel = FindChildGo(rootTr, "RevelationTimerPanel");
        if (revelationTimerPanel != null)
        {
            if (revelationBorderImage == null) revelationBorderImage = FindChildComponent<UIImage>(revelationTimerPanel.transform, "Border");
            if (revelationTitleLabel == null) revelationTitleLabel = FindChildComponent<TMP_Text>(revelationTimerPanel.transform, "RevelationTitleLabel");
            if (revelationCountdownLabel == null) revelationCountdownLabel = FindChildComponent<TMP_Text>(revelationTimerPanel.transform, "RevelationCountdownLabel");
            if (revelationStatusSublabel == null) revelationStatusSublabel = FindChildComponent<TMP_Text>(revelationTimerPanel.transform, "RevelationStatusSublabel");
            if (revelationProgressBar == null) revelationProgressBar = FindChildComponent<ClassicProgressBar>(revelationTimerPanel.transform, "RevelationProgressBar");
        }

        if (kickCooldownPanel == null) kickCooldownPanel = FindChildGo(rootTr, "KickCooldownPanel");
        if (kickCooldownPanel != null)
        {
            if (kickCooldownStatusLabel == null) kickCooldownStatusLabel = FindChildComponent<TMP_Text>(kickCooldownPanel.transform, "KickCooldownStatusLabel");
            if (kickCooldownTitleLabel == null) kickCooldownTitleLabel = FindChildComponent<TMP_Text>(kickCooldownPanel.transform, "KickCooldownTitleLabel");
            if (kickCooldownBar == null) kickCooldownBar = FindChildComponent<CircularProgressBar>(kickCooldownPanel.transform, "KickCooldownBar");
        }

        if (taskInteractionPanel == null) taskInteractionPanel = FindChildGo(rootTr, "TaskInteractionPanel");
        if (taskInteractionPanel != null)
        {
            if (taskInteractionTitle == null) taskInteractionTitle = FindChildComponent<TMP_Text>(taskInteractionPanel.transform, "TaskInteractionTitle");
            if (taskInteractionPercentLabel == null) taskInteractionPercentLabel = FindChildComponent<TMP_Text>(taskInteractionPanel.transform, "TaskInteractionPercentLabel");
            if (taskInteractionHint == null) taskInteractionHint = FindChildComponent<TMP_Text>(taskInteractionPanel.transform, "TaskInteractionHint");
            if (taskInteractionBar == null) taskInteractionBar = FindChildComponent<CircularProgressBar>(taskInteractionPanel.transform, "TaskInteractionBar");
        }

        if (fireballCooldownPanel == null) fireballCooldownPanel = FindChildGo(rootTr, "FireballCooldownPanel");
        if (fireballCooldownPanel != null)
        {
            if (fireballCooldownStatusLabel == null) fireballCooldownStatusLabel = FindChildComponent<TMP_Text>(fireballCooldownPanel.transform, "FireballCooldownStatusLabel");
            if (fireballCooldownTitleLabel == null) fireballCooldownTitleLabel = FindChildComponent<TMP_Text>(fireballCooldownPanel.transform, "FireballCooldownTitleLabel");
            if (fireballCooldownBar == null) fireballCooldownBar = FindChildComponent<CircularProgressBar>(fireballCooldownPanel.transform, "FireballCooldownBar");
        }
    }

    private static GameObject FindChildGo(Transform parent, string childName)
    {
        Transform tr = parent.Find(childName);
        return tr != null ? tr.gameObject : null;
    }

    private static T FindChildComponent<T>(Transform parent, string childName) where T : Component
    {
        Transform tr = parent.Find(childName);
        return tr != null ? tr.GetComponent<T>() : null;
    }

    private static void SetBarProgress(SegmentedProgressBar bar, float normalized, bool snapOnDecrease = false, bool snapOnIncrease = false)
    {
        if (bar == null) return;

        float clamped = Mathf.Clamp01(normalized);
        if (!bar.IsBuilt)
        {
            bar.FillAmount = clamped;
            return;
        }

        if (snapOnDecrease && clamped < bar.DisplayedFillAmount - 0.15f)
        {
            bar.SetFillImmediate(clamped);
            return;
        }

        if (snapOnIncrease && clamped > bar.DisplayedFillAmount + 0.15f)
        {
            bar.SetFillImmediate(clamped);
            return;
        }

        if (!Mathf.Approximately(bar.FillAmount, clamped))
        {
            bar.FillAmount = clamped;
        }
    }

    /// <summary>
    /// Atualiza o display da contagem regressiva da partida de 5 minutos no formato MM:SS.
    /// Exibe alerta de cor urgente nos últimos 60 segundos de partida.
    /// </summary>
    public void SetMatchTimer(float secondsRemaining, bool isRunning)
    {
        if (matchTimerLabel == null && root != null)
        {
            matchTimerLabel = root.Q<Label>("match-timer-label");
        }

        if (matchTimerLabel == null) return;

        float clamped = Mathf.Max(0f, secondsRemaining);
        int minutes = Mathf.FloorToInt(clamped / 60f);
        int seconds = Mathf.FloorToInt(clamped % 60f);

        matchTimerLabel.text = $"{minutes:00}:{seconds:00}";

        if (clamped <= 60f && isRunning)
        {
            if (!matchTimerLabel.ClassListContains("match-timer-label--urgent"))
            {
                matchTimerLabel.AddToClassList("match-timer-label--urgent");
            }
        }
        else
        {
            matchTimerLabel.RemoveFromClassList("match-timer-label--urgent");
        }
    }

    /// <summary>
    /// Atualiza a quantidade de sobreviventes vivos exibida ao lado direito do cronômetro da partida.
    /// </summary>
    public void SetAlivePlayersCount(int count)
    {
        if (alivePlayersLabel == null && root != null)
        {
            alivePlayersLabel = root.Q<Label>("alive-players-label");
        }

        if (alivePlayersLabel == null) return;

        int safeCount = Mathf.Max(0, count);
        alivePlayersLabel.text = safeCount == 1 ? "1 VIVO" : $"{safeCount} VIVOS";
    }

    /// <summary>
    /// Define a visibilidade do painel de progresso de tasks no canto superior esquerdo.
    /// Fica oculto durante a descida da Morte dos céus e surge quando a partida começa de verdade.
    /// </summary>
    public void SetTasksPanelVisible(bool visible)
    {
        if (tasksProgressPanel != null)
        {
            tasksProgressPanel.SetActive(visible);
        }
    }

    /// <summary>
    /// Atualiza o display da barra de progresso das Tasks no canto superior esquerdo (Visível para todos).
    /// </summary>
    public void SetTaskProgress(int completed, int total)
    {
        if (tasksCounterLabel == null || tasksProgressBar == null) return;

        int safeTotal = Mathf.Max(1, total);
        int safeCompleted = Mathf.Clamp(completed, 0, safeTotal);

        tasksCounterLabel.text = $"{safeCompleted} / {safeTotal}";

        int desiredSegments = Mathf.Clamp(safeTotal, 1, 12);
        if (tasksProgressBar.NumberOfSegments != desiredSegments)
        {
            tasksProgressBar.NumberOfSegments = desiredSegments;
        }

        float pct = Mathf.Clamp01((float)safeCompleted / safeTotal);
        SetBarProgress(tasksProgressBar, pct);

        bool allDone = safeCompleted >= safeTotal;
        Color targetFill = allDone
            ? new Color(0.05f, 0.58f, 0.53f, 1f)
            : new Color(0.09f, 0.64f, 0.29f, 1f);
        tasksProgressBar.FillColor = targetFill;
        tasksCounterLabel.color = allDone
            ? new Color(0.06f, 0.46f, 0.43f, 1f)
            : new Color(0.08f, 0.50f, 0.24f, 1f);
    }

    /// <summary>
    /// Atualiza a barra circular de progresso de interação contínua enquanto o jogador segura o botão esquerdo na Task.
    /// </summary>
    public void SetTaskInteractionProgress(bool isInteracting, float currentProgress, float duration, string taskName)
    {
        if (taskPromptPanel == null && root != null)
        {
            taskPromptPanel = root.Q<VisualElement>("task-prompt-panel");
        }

        if (taskInteractionPanel == null || taskInteractionBar == null) return;

        if (isInteracting)
        {
            if (!taskInteractionPanel.activeSelf)
            {
                taskInteractionPanel.SetActive(true);
            }
            if (taskPromptPanel != null) taskPromptPanel.style.display = DisplayStyle.None;

            string title = string.IsNullOrWhiteSpace(taskName) ? "REALIZANDO TAREFA..." : $"TAREFA: {taskName.ToUpperInvariant()}";
            if (taskInteractionTitle != null) taskInteractionTitle.text = title;

            float safeDuration = Mathf.Max(0.1f, duration);
            float pct = Mathf.Clamp01(currentProgress / safeDuration);
            SetBarProgress(taskInteractionBar, pct, snapOnDecrease: true);

            if (taskInteractionPercentLabel != null)
            {
                taskInteractionPercentLabel.text = $"{Mathf.RoundToInt(pct * 100f)}%";
            }

            if (taskInteractionHint != null)
            {
                taskInteractionHint.text = $"SEGURE O BOTÃO ESQUERDO DO MOUSE ({currentProgress:F1}s / {safeDuration:F1}s)";
            }
        }
        else
        {
            if (taskInteractionPanel.activeSelf)
            {
                taskInteractionPanel.SetActive(false);
            }
            SetBarProgress(taskInteractionBar, 0f, snapOnDecrease: true);
        }
    }

    /// <summary>
    /// Exibe no HUD o nome da tarefa objetivo atribuída exclusivamente para este jogador sobrevivente.
    /// </summary>
    public void SetTargetTaskObjective(string targetTaskName)
    {
        if (tasksTitleLabel != null)
        {
            if (string.IsNullOrEmpty(targetTaskName))
            {
                tasksTitleLabel.text = "TAREFAS DA EQUIPE";
            }
            else if (targetTaskName.Contains("<color="))
            {
                tasksTitleLabel.text = $"TAREFAS DA EQUIPE\n<size=11.5><b>{targetTaskName}</b></size>";
            }
            else
            {
                tasksTitleLabel.text = $"TAREFAS DA EQUIPE\n<size=11.5><b><color=#7B5D45>SEU ALVO:</color> <color=#15803D>{targetTaskName.ToUpperInvariant()}</color></b></size>";
            }
        }
    }

    /// <summary>
    /// Exibe ou oculta o prompt contextual de proximidade de uma tarefa.
    /// </summary>
    public void SetTaskPrompt(bool isVisible, string taskName, bool alreadyCompleted, bool isTarget = true)
    {
        if (taskPromptPanel == null && root != null)
        {
            taskPromptPanel = root.Q<VisualElement>("task-prompt-panel");
            taskPromptLabel = root.Q<Label>("task-prompt-label");
        }

        if (taskPromptPanel == null || taskPromptLabel == null) return;

        if (isVisible)
        {
            taskPromptPanel.style.display = DisplayStyle.Flex;

            if (!isTarget)
            {
                taskPromptLabel.text = $"[BLOQUEADO] NÃO É SEU OBJETIVO: {taskName}";
                if (!taskPromptLabel.ClassListContains("task-prompt-label--completed"))
                {
                    taskPromptLabel.AddToClassList("task-prompt-label--completed");
                }
            }
            else if (alreadyCompleted)
            {
                if (taskName == "Dançar" || taskName == "Dançar Próximo à Morte")
                {
                    taskPromptLabel.text = "TAREFA CONCLUÍDA: DANÇOU PARA A MORTE!";
                }
                else
                {
                    taskPromptLabel.text = $"TAREFA CONCLUÍDA: {taskName}";
                }
                if (!taskPromptLabel.ClassListContains("task-prompt-label--completed"))
                {
                    taskPromptLabel.AddToClassList("task-prompt-label--completed");
                }
            }
            else
            {
                if (taskName == "A Morte" || taskName == "Hunter" || taskName == "Chutar a Morte")
                {
                    taskPromptLabel.text = "[E] CHUTAR A MORTE!";
                }
                else if (taskName == "Dançar" || taskName == "Dançar Próximo à Morte")
                {
                    taskPromptLabel.text = "[2] OU [3] DANÇAR PARA A MORTE!";
                }
                else if (taskName == "Aproxime-se para Dançar")
                {
                    taskPromptLabel.text = "APROXIME-SE DA MORTE PARA DANÇAR [2 OU 3]";
                }
                else
                {
                    taskPromptLabel.text = $"[SEGURE BOTÃO ESQUERDO] INTERAGIR: {taskName.ToUpperInvariant()}";
                }
                taskPromptLabel.RemoveFromClassList("task-prompt-label--completed");
            }
        }
        else
        {
            taskPromptPanel.style.display = DisplayStyle.None;
        }
    }

    private void EnsureSpritesLoaded()
    {
        if (activeSkullSprite == null)
        {
            activeSkullSprite = Resources.Load<Sprite>("HunterLifeIcon");
#if UNITY_EDITOR
            if (activeSkullSprite == null)
            {
                activeSkullSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/HunterLifeIcon.png");
            }
#endif
        }

        if (lostSkullSprite == null)
        {
            lostSkullSprite = Resources.Load<Sprite>("HunterLifeLostIcon");
#if UNITY_EDITOR
            if (lostSkullSprite == null)
            {
                lostSkullSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/HunterLifeLostIcon.png");
            }
#endif
        }

#if UNITY_EDITOR
        if (fireballIconSprite == null) fireballIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_fireball.png");
        if (kickIconSprite == null) kickIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_kick.png");
        if (scytheIconSprite == null) scytheIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_scythe.png");
        if (killSkullIconSprite == null) killSkullIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_skull.png");
#endif
    }

    /// <summary>
    /// Reconstrói visualmente o painel de vidas dos Hunters no topo da tela.
    /// Para 1 Hunter: exibe o card único com "VIDA DO HUNTER" e suas 3 cabeças.
    /// Para múltiplos Hunters: exibe os cards com nomes e divisores entre eles.
    /// As vidas ativas utilizam HunterLifeIcon e as vidas perdidas utilizam HunterLifeLostIcon.
    /// </summary>
    public void RefreshHunterRosterUI()
    {
        if (root == null)
        {
            InitializeUI();
        }

        if (huntersLifeRoster == null && root != null)
        {
            huntersLifeRoster = root.Q<VisualElement>("hunters-life-roster");
        }

        if (huntersLifeRoster == null) return;

        EnsureSpritesLoaded();
        huntersLifeRoster.Clear();

        if (huntersData.Count == 0)
        {
            var defaultCard = CreateHunterCardElement("VIDA DO HUNTER", currentHunterLives, maxHunterLives);
            huntersLifeRoster.Add(defaultCard);
            return;
        }

        bool isMultiHunter = (huntersData.Count > 1);
        int cardIdx = 0;
        foreach (var kvp in huntersData)
        {
            var data = kvp.Value;
            if (cardIdx > 0)
            {
                var divider = new VisualElement();
                divider.AddToClassList("hunter-card-divider");
                huntersLifeRoster.Add(divider);
            }

            string title = isMultiHunter ? $"HUNTER: {data.displayName}" : "VIDA DO HUNTER";
            var card = CreateHunterCardElement(title, data.currentLives, data.maxLives);
            huntersLifeRoster.Add(card);

            cardIdx++;
        }
    }

    private VisualElement CreateHunterCardElement(string titleText, int currentLives, int maxLives)
    {
        var card = new VisualElement();
        card.AddToClassList("hunter-life-card");

        var title = new Label(titleText);
        title.AddToClassList("hunter-card-title");
        card.Add(title);

        var skullsBox = new VisualElement();
        skullsBox.AddToClassList("hunter-skulls-container");

        for (int i = 0; i < maxLives; i++)
        {
            var skull = new VisualElement();
            skull.AddToClassList("hunter-skull-icon");

            bool isLost = (i >= currentLives);
            if (isLost)
            {
                skull.AddToClassList("hunter-skull-icon--lost");
                if (lostSkullSprite != null)
                {
                    skull.style.backgroundImage = new StyleBackground(lostSkullSprite);
                }
            }
            else
            {
                skull.RemoveFromClassList("hunter-skull-icon--lost");
                if (activeSkullSprite != null)
                {
                    skull.style.backgroundImage = new StyleBackground(activeSkullSprite);
                }
            }

            skullsBox.Add(skull);
        }
        card.Add(skullsBox);

        var status = new Label("ELIMINADO");
        status.AddToClassList("hunter-card-status");
        if (currentLives <= 0)
        {
            status.AddToClassList("hunter-card-status--visible");
        }
        card.Add(status);

        return card;
    }

    /// <summary>
    /// Atualiza o número de vidas restantes do Hunter (compatibilidade e testes).
    /// Visível tanto para o Hunter quanto para os sobreviventes.
    /// </summary>
    public void SetHunterLives(int lives, int max = 3)
    {
        maxHunterLives = Mathf.Max(1, max);
        currentHunterLives = Mathf.Clamp(lives, 0, maxHunterLives);

        if (huntersData.Count == 0)
        {
            UpdateHunterLives(0, "A Morte", currentHunterLives, maxHunterLives);
        }
        else
        {
            foreach (var key in new List<ulong>(huntersData.Keys))
            {
                huntersData[key].currentLives = currentHunterLives;
                huntersData[key].maxLives = maxHunterLives;
            }
            RefreshHunterRosterUI();
        }
    }

    /// <summary>
    /// Atualiza ou registra as 3 vidas de um Hunter específico no HUD.
    /// Adapta a interface automaticamente para 1, 2 ou 3 Hunters na partida.
    /// </summary>
    public void UpdateHunterLives(ulong hunterNetId, string hunterName, int lives, int max = 3)
    {
        if (root == null)
        {
            InitializeUI();
        }
        if (huntersLifeRoster == null && root != null)
        {
            huntersLifeRoster = root.Q<VisualElement>("hunters-life-roster");
        }
        if (huntersLifeRoster == null) return;

        int safeMax = Mathf.Max(1, max);
        int safeLives = Mathf.Clamp(lives, 0, safeMax);
        string safeName = string.IsNullOrWhiteSpace(hunterName) ? "A Morte" : hunterName;

        // Se tínhamos registro provisório com ID 0 e recebemos o ID de rede real, substitui a chave provisória
        if (hunterNetId != 0 && huntersData.ContainsKey(0))
        {
            huntersData.Remove(0);
        }

        // Se hunterNetId == 0 mas já temos 1 Hunter registrado, atualiza esse hunter existente
        if (hunterNetId == 0 && huntersData.Count == 1)
        {
            foreach (var kvp in huntersData)
            {
                hunterNetId = kvp.Key;
                break;
            }
        }

        if (huntersData.TryGetValue(hunterNetId, out var existing))
        {
            existing.displayName = safeName;
            existing.currentLives = safeLives;
            existing.maxLives = safeMax;
        }
        else
        {
            huntersData[hunterNetId] = new HunterData
            {
                networkId = hunterNetId,
                displayName = safeName,
                currentLives = safeLives,
                maxLives = safeMax
            };
        }

        if (huntersData.Count == 1)
        {
            currentHunterLives = safeLives;
            maxHunterLives = safeMax;
        }

        RefreshHunterRosterUI();
    }

    public void UnregisterHunter(ulong hunterNetId)
    {
        if (huntersData.Remove(hunterNetId))
        {
            RefreshHunterRosterUI();
        }
    }

    public bool IsLocalHunter { get; private set; }

    /// <summary>
    /// Configura quais contêineres de HUD são exibidos dependendo do papel do jogador local.
    /// A vida do Hunter (topCenterContainer) permanece SEMPRE visível para ambos.
    /// </summary>
    public void SetLocalRole(bool isHunter)
    {
        IsLocalHunter = isHunter;
        isLocalHunter = isHunter;
        if (isHunter)
        {
            isLocalSpectator = false;
        }
        if (root == null)
        {
            InitializeUI();
        }

        if (topCenterContainer != null)
        {
            topCenterContainer.style.display = DisplayStyle.Flex;
        }

        bool matchActive = GameSpawner.Instance == null || GameSpawner.Instance.IsMatchActive || GameSpawner.Instance.IsGameOver;
        if (tasksProgressPanel != null)
        {
            tasksProgressPanel.SetActive(matchActive);
        }

        if (staminaContainer != null)
        {
            staminaContainer.SetActive(true);
        }

        if (survivorHudContainer != null)
        {
            survivorHudContainer.style.display = isHunter ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (hunterHudContainer != null)
        {
            hunterHudContainer.style.display = isHunter ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!isHunter && hunterIntroCountdownPanel != null)
        {
            hunterIntroCountdownPanel.style.display = DisplayStyle.None;
        }

        if (kickCooldownPanel != null)
        {
            kickCooldownPanel.SetActive(!isHunter && !isLocalSpectator);
        }

        if (fireballCooldownPanel != null)
        {
            fireballCooldownPanel.SetActive(isHunter);
        }

        if (hunterRevealCooldownPanel != null)
        {
            hunterRevealCooldownPanel.SetActive(isHunter);
        }

        if (revelationTimerPanel != null)
        {
            revelationTimerPanel.SetActive(!isHunter && !isLocalSpectator);
        }

        if ((isHunter || isLocalSpectator) && taskInteractionPanel != null)
        {
            taskInteractionPanel.SetActive(false);
        }

        ApplyRoleThemeAndBadge(isHunter, !isHunter && isLocalSpectator);

        Debug.Log($"[GameHUDController] HUD configurado para papel: {(isHunter ? "HUNTER (Morte)" : (isLocalSpectator ? "FANTASMA (Espectador)" : "SURVIVOR (Player)"))}");
    }

    /// <summary>
    /// Alterna o visual de espectador (Fantasma) no HUD de sobrevivente quando o jogador é eliminado.
    /// </summary>
    public void SetSpectatorMode(bool isSpectator)
    {
        isLocalSpectator = isSpectator && !isLocalHunter;
        if (root == null)
        {
            InitializeUI();
        }

        if (kickCooldownPanel != null && !isLocalHunter)
        {
            kickCooldownPanel.SetActive(!isLocalSpectator);
        }

        if (revelationTimerPanel != null && isLocalSpectator)
        {
            revelationTimerPanel.SetActive(false);
        }

        if (isLocalSpectator && taskPromptPanel != null)
        {
            taskPromptPanel.style.display = DisplayStyle.None;
        }

        ApplyRoleThemeAndBadge(isLocalHunter, isLocalSpectator);
    }

    private void ApplyRoleThemeAndBadge(bool isHunter, bool isSpectator)
    {
        if (hunterLivesPanel != null)
        {
            hunterLivesPanel.EnableInClassList("hunter-lives-panel--hunter", isHunter);
        }

        if (roleBadgeCard != null)
        {
            roleBadgeCard.EnableInClassList("role-badge-card--hunter", isHunter);
        }

        if (isHunter)
        {
            ApplyRoleBadgeControls(("ESQ", "Foice"), ("E (2.5s)", "Revelar"), ("DIR", "Fogo"));
        }
        else if (isSpectator)
        {
            ApplyRoleBadgeControls(("WASD", "Voar"), ("ESPAÇO", "Subir"), ("CTRL", "Descer"));
        }
        else
        {
            ApplyRoleBadgeControls(("ESQ", "Tarefa"), ("E", "Chute"), ("SHIFT", "Correr"));
        }
    }

    private void ApplyRoleBadgeControls((string key, string action) c1, (string key, string action) c2, (string key, string action) c3)
    {
        if (roleCtrl1Key != null) roleCtrl1Key.text = c1.key;
        if (roleCtrl1Action != null) roleCtrl1Action.text = c1.action;
        if (roleCtrl2Key != null) roleCtrl2Key.text = c2.key;
        if (roleCtrl2Action != null) roleCtrl2Action.text = c2.action;
        if (roleCtrl3Key != null) roleCtrl3Key.text = c3.key;
        if (roleCtrl3Action != null) roleCtrl3Action.text = c3.action;
    }

    /// <summary>
    /// Atualiza o elemento de UI do Sobrevivente que cai com o tempo indicando quantos segundos faltam para ser revelado.
    /// </summary>
    public void SetSurvivorRevelationTimer(float remainingSeconds, float totalTimeout, bool isMarkedForDeath, bool hasActiveTask)
    {
        if (revelationTimerPanel == null || revelationCountdownLabel == null || revelationProgressBar == null) return;

        if (isLocalHunter)
        {
            if (revelationTimerPanel.activeSelf) revelationTimerPanel.SetActive(false);
            return;
        }

        if (!revelationTimerPanel.activeSelf) revelationTimerPanel.SetActive(true);

        if (isMarkedForDeath)
        {
            // O jogador foi penalizado por inatividade e está com a aura e localização reveladas ao Hunter!
            if (revelationTitleLabel != null) revelationTitleLabel.text = "VOCÊ ESTÁ REVELADO!";
            revelationCountdownLabel.text = "00:00";
            revelationCountdownLabel.color = new Color(0.86f, 0.15f, 0.15f, 1f);
            SetBarProgress(revelationProgressBar, 0f, snapOnDecrease: true);
            revelationProgressBar.FillColor = new Color(0.86f, 0.15f, 0.15f, 1f);
            if (revelationBorderImage != null) revelationBorderImage.color = new Color(0.86f, 0.15f, 0.15f, 0.85f);

            if (revelationStatusSublabel != null)
            {
                revelationStatusSublabel.text = "FAÇA A SUA TASK PARA REMOVER A REVELAÇÃO!";
                revelationStatusSublabel.color = new Color(0.72f, 0.11f, 0.11f, 1f);
            }
            return;
        }

        if (!hasActiveTask)
        {
            // O jogador concluiu uma task e está aguardando os 10s para receber a próxima
            if (revelationTitleLabel != null) revelationTitleLabel.text = "PRÓXIMA TASK";
            revelationCountdownLabel.text = "EM 10s";
            revelationCountdownLabel.color = new Color(0.08f, 0.50f, 0.24f, 1f);
            SetBarProgress(revelationProgressBar, 1f, snapOnIncrease: true);
            revelationProgressBar.FillColor = new Color(0.13f, 0.77f, 0.37f, 1f);
            if (revelationBorderImage != null) revelationBorderImage.color = new Color(0.13f, 0.77f, 0.37f, 0.75f);

            if (revelationStatusSublabel != null)
            {
                revelationStatusSublabel.text = "Aguardando novo objetivo...";
                revelationStatusSublabel.color = new Color(0.43f, 0.31f, 0.21f, 1f);
            }
            return;
        }

        // Estado ativo com task atribuída: o tempo e a barra caem até 0
        float safeTotal = Mathf.Max(1f, totalTimeout);
        float safeRemaining = Mathf.Clamp(remainingSeconds, 0f, safeTotal);
        float pct = safeRemaining / safeTotal;

        if (revelationTitleLabel != null) revelationTitleLabel.text = "TEMPO ATÉ REVELAÇÃO";

        int minutes = Mathf.FloorToInt(safeRemaining / 60f);
        int seconds = Mathf.FloorToInt(safeRemaining % 60f);
        revelationCountdownLabel.text = $"{minutes:00}:{seconds:00}";

        SetBarProgress(revelationProgressBar, pct, snapOnIncrease: true);

        // Cores dinâmicas de urgência contrastadas sobre o cartão creme Cartoon
        if (safeRemaining <= 15f)
        {
            revelationCountdownLabel.color = new Color(0.86f, 0.15f, 0.15f, 1f);
            revelationProgressBar.FillColor = new Color(0.86f, 0.15f, 0.15f, 1f);
            if (revelationBorderImage != null) revelationBorderImage.color = new Color(0.86f, 0.15f, 0.15f, 0.85f);
            if (revelationStatusSublabel != null)
            {
                revelationStatusSublabel.text = "PERIGO IMINENTE! Conclua a task agora!";
                revelationStatusSublabel.color = new Color(0.72f, 0.11f, 0.11f, 1f);
            }
        }
        else if (safeRemaining <= 35f)
        {
            revelationCountdownLabel.color = new Color(0.85f, 0.47f, 0.02f, 1f);
            revelationProgressBar.FillColor = new Color(0.96f, 0.62f, 0.04f, 1f);
            if (revelationBorderImage != null) revelationBorderImage.color = new Color(0.85f, 0.47f, 0.02f, 0.70f);
            if (revelationStatusSublabel != null)
            {
                revelationStatusSublabel.text = "Apresse-se para não ser revelado";
                revelationStatusSublabel.color = new Color(0.71f, 0.33f, 0.04f, 1f);
            }
        }
        else
        {
            revelationCountdownLabel.color = new Color(0.49f, 0.23f, 0.93f, 1f);
            revelationProgressBar.FillColor = new Color(0.55f, 0.36f, 0.96f, 1f);
            if (revelationBorderImage != null) revelationBorderImage.color = new Color(0.49f, 0.23f, 0.93f, 0.50f);
            if (revelationStatusSublabel != null)
            {
                revelationStatusSublabel.text = "Faça sua task para não ser detectado";
                revelationStatusSublabel.color = new Color(0.43f, 0.31f, 0.21f, 1f);
            }
        }
    }

    /// <summary>
    /// Oculta o painel de revelação (ex: ao morrer ou quando vira fantasma).
    /// </summary>
    public void HideSurvivorRevelationTimer()
    {
        if (revelationTimerPanel != null && revelationTimerPanel.activeSelf)
        {
            revelationTimerPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Exibe o HUDzinho de contagem regressiva exclusivo do Hunter no meio da tela.
    /// </summary>
    public void ShowHunterCountdown(int number, float pulseScale = 1.0f)
    {
        if (root == null || hunterIntroCountdownPanel == null)
        {
            InitializeUI();
        }

        if (hunterIntroCountdownPanel != null)
        {
            hunterIntroCountdownPanel.style.display = DisplayStyle.Flex;
        }

        // Durante a contagem, oculta a mira para foco absoluto no HUDzinho central
        if (hunterCrosshair != null)
        {
            hunterCrosshair.style.display = DisplayStyle.None;
        }

        if (hunterCountdownBadge != null)
        {
            hunterCountdownBadge.RemoveFromClassList("hunter-countdown-badge--start");
            hunterCountdownBadge.style.scale = new Scale(new Vector2(pulseScale, pulseScale));
        }

        if (hunterCountdownNumber != null)
        {
            hunterCountdownNumber.RemoveFromClassList("hunter-countdown-number--start");
            hunterCountdownNumber.text = number.ToString();
        }
    }

    /// <summary>
    /// Exibe a mensagem de impacto e início "COMEÇAR!" logo após o 1 na aterrissagem.
    /// </summary>
    public void ShowHunterStartBanner()
    {
        if (root == null || hunterIntroCountdownPanel == null)
        {
            InitializeUI();
        }

        if (hunterIntroCountdownPanel != null)
        {
            hunterIntroCountdownPanel.style.display = DisplayStyle.Flex;
        }

        if (hunterCrosshair != null)
        {
            hunterCrosshair.style.display = DisplayStyle.None;
        }

        if (hunterCountdownBadge != null)
        {
            if (!hunterCountdownBadge.ClassListContains("hunter-countdown-badge--start"))
            {
                hunterCountdownBadge.AddToClassList("hunter-countdown-badge--start");
            }
            hunterCountdownBadge.style.scale = new Scale(new Vector2(1.12f, 1.12f));
        }

        if (hunterCountdownNumber != null)
        {
            if (!hunterCountdownNumber.ClassListContains("hunter-countdown-number--start"))
            {
                hunterCountdownNumber.AddToClassList("hunter-countdown-number--start");
            }
            hunterCountdownNumber.text = "COMEÇAR!";
        }

        StopCoroutine(nameof(HideCountdownDelayedRoutine));
        StartCoroutine(nameof(HideCountdownDelayedRoutine));
    }

    private System.Collections.IEnumerator HideCountdownDelayedRoutine()
    {
        yield return new WaitForSeconds(1.1f);
        HideHunterCountdown();
    }

    /// <summary>
    /// Oculta o HUDzinho de contagem regressiva e restaura a mira padrão do Hunter.
    /// </summary>
    public void HideHunterCountdown()
    {
        if (hunterIntroCountdownPanel != null)
        {
            hunterIntroCountdownPanel.style.display = DisplayStyle.None;
        }

        if (hunterCrosshair != null)
        {
            hunterCrosshair.style.display = DisplayStyle.Flex;
        }

        if (hunterCountdownBadge != null)
        {
            hunterCountdownBadge.RemoveFromClassList("hunter-countdown-badge--start");
            hunterCountdownBadge.style.scale = new Scale(Vector2.one);
        }

        if (hunterCountdownNumber != null)
        {
            hunterCountdownNumber.RemoveFromClassList("hunter-countdown-number--start");
        }
    }

    /// <summary>
    /// Alterna o estilo visual da mira do Hunter entre modo normal e modo de pontaria (Aim Mode).
    /// </summary>
    public void SetAiming(bool isAiming)
    {
        if (root == null)
        {
            InitializeUI();
        }

        if (hunterCrosshair == null && root != null)
        {
            hunterCrosshair = root.Q<VisualElement>("hunter-crosshair");
        }

        if (hunterCrosshair != null)
        {
            if (isAiming)
            {
                if (!hunterCrosshair.ClassListContains("hunter-crosshair--aiming"))
                {
                    hunterCrosshair.AddToClassList("hunter-crosshair--aiming");
                }
            }
            else
            {
                hunterCrosshair.RemoveFromClassList("hunter-crosshair--aiming");
            }
        }
    }

    private void UpdateSkullVisuals()
    {
        RefreshHunterRosterUI();
    }

    /// <summary>
    /// Atualiza visualmente a barra de stamina normal (ClassicProgressBar) do jogador local (tanto Sobrevivente quanto Hunter).
    /// </summary>
    public void SetStamina(float current, float max)
    {
        if (staminaProgressBar == null) return;

        float pct = Mathf.Clamp01(current / Mathf.Max(1f, max));
        SetBarProgress(staminaProgressBar, pct);

        if (pct <= 0.01f)
        {
            isStaminaExhausted = true;
        }
        else if (pct >= 0.20f)
        {
            isStaminaExhausted = false;
        }

        if (isStaminaExhausted)
        {
            if (staminaProgressBar.UseFillGradient)
            {
                staminaProgressBar.UseFillGradient = false;
            }
            staminaProgressBar.FillColor = new Color(0.91f, 0.30f, 0.24f, 1f);
        }
        else if (!staminaProgressBar.UseFillGradient)
        {
            staminaProgressBar.UseFillGradient = true;
        }

        // A barra fica 100% visível quando não está cheia, e desaparece suavemente quando cheia
        if (staminaCanvasGroup != null)
        {
            float targetAlpha = (pct >= 0.99f) ? 0f : 1f;
            staminaCanvasGroup.alpha = Mathf.MoveTowards(staminaCanvasGroup.alpha, targetAlpha, Time.deltaTime * 4f);
        }
    }

    /// <summary>
    /// Atualiza o indicador circular de tempo de recarga da bola de fogo da Morte (Hunter).
    /// </summary>
    public void SetFireballCooldown(float elapsedSinceFire, float totalCooldown)
    {
        if (fireballCooldownBar == null || fireballCooldownStatusLabel == null) return;

        if (elapsedSinceFire >= totalCooldown)
        {
            SetBarProgress(fireballCooldownBar, 1f);
            fireballCooldownBar.UseFillGradient = false;
            fireballCooldownBar.FillColor = new Color(1f, 0.72f, 0.20f, 1f);
            fireballCooldownStatusLabel.text = "PRONTA";
            fireballCooldownStatusLabel.color = new Color(0.99f, 0.83f, 0.30f, 1f);
            if (fireballCooldownTitleLabel != null)
            {
                fireballCooldownTitleLabel.color = Color.white;
            }
        }
        else
        {
            float pct = Mathf.Clamp01(elapsedSinceFire / Mathf.Max(0.01f, totalCooldown));
            SetBarProgress(fireballCooldownBar, pct, snapOnDecrease: true);
            if (!fireballCooldownBar.UseFillGradient)
            {
                fireballCooldownBar.UseFillGradient = true;
            }

            float remaining = Mathf.Max(0f, totalCooldown - elapsedSinceFire);
            fireballCooldownStatusLabel.text = $"{remaining:F1}s";
            fireballCooldownStatusLabel.color = new Color(1f, 0.96f, 0.84f, 1f);
            if (fireballCooldownTitleLabel != null)
            {
                fireballCooldownTitleLabel.color = new Color(1f, 0.94f, 0.88f, 0.95f);
            }
        }
    }

    private void EnsureHunterRevealCooldownUI()
    {
        if (hunterRevealCooldownPanel != null) return;
        if (fireballCooldownPanel == null) return;

        hunterRevealCooldownPanel = Instantiate(fireballCooldownPanel, fireballCooldownPanel.transform.parent);
        hunterRevealCooldownPanel.name = "HunterRevealCooldownPanel";

        RectTransform fbRt = fireballCooldownPanel.GetComponent<RectTransform>();
        RectTransform rvRt = hunterRevealCooldownPanel.GetComponent<RectTransform>();

        if (fbRt != null && rvRt != null)
        {
            Vector2 basePos = fbRt.anchoredPosition;
            fbRt.anchoredPosition = new Vector2(85f, basePos.y);
            rvRt.anchoredPosition = new Vector2(-85f, basePos.y);
        }

        hunterRevealCooldownStatusLabel = FindChildComponent<TMP_Text>(hunterRevealCooldownPanel.transform, "FireballCooldownStatusLabel");
        if (hunterRevealCooldownStatusLabel != null)
        {
            hunterRevealCooldownStatusLabel.gameObject.name = "HunterRevealCooldownStatusLabel";
        }

        hunterRevealCooldownTitleLabel = FindChildComponent<TMP_Text>(hunterRevealCooldownPanel.transform, "FireballCooldownTitleLabel");
        if (hunterRevealCooldownTitleLabel != null)
        {
            hunterRevealCooldownTitleLabel.gameObject.name = "HunterRevealCooldownTitleLabel";
            hunterRevealCooldownTitleLabel.text = "REVELAR [E]";
        }

        hunterRevealCooldownBar = FindChildComponent<CircularProgressBar>(hunterRevealCooldownPanel.transform, "FireballCooldownBar");
        if (hunterRevealCooldownBar != null)
        {
            hunterRevealCooldownBar.gameObject.name = "HunterRevealCooldownBar";
        }

        hunterRevealCooldownPanel.SetActive(isLocalHunter);
    }

    /// <summary>
    /// Atualiza o indicador circular de tempo de recarga da habilidade de revelação (E) da Morte (Hunter).
    /// </summary>
    public void SetHunterRevealCooldown(float elapsedSinceReveal, float totalCooldown)
    {
        EnsureHunterRevealCooldownUI();
        if (hunterRevealCooldownBar == null || hunterRevealCooldownStatusLabel == null) return;

        if (elapsedSinceReveal >= totalCooldown)
        {
            SetBarProgress(hunterRevealCooldownBar, 1f);
            hunterRevealCooldownBar.UseFillGradient = false;
            hunterRevealCooldownBar.FillColor = new Color(0.85f, 0.28f, 1.0f, 1f);
            hunterRevealCooldownStatusLabel.text = "PRONTA";
            hunterRevealCooldownStatusLabel.color = new Color(0.92f, 0.50f, 1.0f, 1f);
            if (hunterRevealCooldownTitleLabel != null)
            {
                hunterRevealCooldownTitleLabel.color = Color.white;
            }
        }
        else
        {
            float pct = Mathf.Clamp01(elapsedSinceReveal / Mathf.Max(0.01f, totalCooldown));
            SetBarProgress(hunterRevealCooldownBar, pct, snapOnDecrease: true);
            if (!hunterRevealCooldownBar.UseFillGradient)
            {
                hunterRevealCooldownBar.UseFillGradient = true;
            }

            float remaining = Mathf.Max(0f, totalCooldown - elapsedSinceReveal);
            hunterRevealCooldownStatusLabel.text = $"{remaining:F1}s";
            hunterRevealCooldownStatusLabel.color = new Color(1f, 0.92f, 0.98f, 1f);
            if (hunterRevealCooldownTitleLabel != null)
            {
                hunterRevealCooldownTitleLabel.color = new Color(0.92f, 0.88f, 0.98f, 0.95f);
            }
        }
    }

    /// <summary>
    /// Atualiza a barra circular central de progresso durante a conjuração da habilidade do Hunter (segurar [E] por 3s).
    /// </summary>
    public void SetHunterAbilityCastingProgress(bool isCasting, float currentProgress, float duration, string abilityName)
    {
        if (taskInteractionPanel == null || taskInteractionBar == null) return;

        if (isCasting)
        {
            if (!taskInteractionPanel.activeSelf)
            {
                taskInteractionPanel.SetActive(true);
            }
            if (taskPromptPanel != null) taskPromptPanel.style.display = DisplayStyle.None;

            if (taskInteractionTitle != null)
            {
                taskInteractionTitle.text = string.IsNullOrWhiteSpace(abilityName) ? "CONJURANDO..." : abilityName.ToUpperInvariant();
            }

            float safeDuration = Mathf.Max(0.1f, duration);
            float pct = Mathf.Clamp01(currentProgress / safeDuration);
            SetBarProgress(taskInteractionBar, pct, snapOnDecrease: true);

            if (taskInteractionPercentLabel != null)
            {
                taskInteractionPercentLabel.text = $"{Mathf.RoundToInt(pct * 100f)}%";
            }

            if (taskInteractionHint != null)
            {
                taskInteractionHint.text = $"SEGURE [E] PARA CONJURAR ({currentProgress:F1}s / {safeDuration:F1}s)";
            }
        }
        else
        {
            if (taskInteractionPanel.activeSelf)
            {
                taskInteractionPanel.SetActive(false);
            }
            SetBarProgress(taskInteractionBar, 0f, snapOnDecrease: true);
        }
    }

    private Coroutine screenNoticeRoutine = null;

    /// <summary>
    /// Exibe um aviso temporário de destaque na tela (ex: sobrevivente detectado ou alerta de revelação).
    /// </summary>
    public void ShowScreenNotice(string message, float duration = 2.5f)
    {
        if (screenNoticeRoutine != null) StopCoroutine(screenNoticeRoutine);
        screenNoticeRoutine = StartCoroutine(ScreenNoticeRoutine(message, duration));
    }

    private System.Collections.IEnumerator ScreenNoticeRoutine(string message, float duration)
    {
        if (taskPromptPanel != null && taskPromptLabel != null)
        {
            taskPromptPanel.style.display = DisplayStyle.Flex;
            taskPromptLabel.text = message;
            yield return new WaitForSeconds(duration);
            taskPromptPanel.style.display = DisplayStyle.None;
        }
        screenNoticeRoutine = null;
    }

    /// <summary>
    /// Atualiza o indicador circular de tempo de recarga (cooldown) do chute do sobrevivente (15s).
    /// </summary>
    public void SetKickCooldown(float elapsedSinceKick, float totalCooldown)
    {
        if (kickCooldownBar == null || kickCooldownStatusLabel == null) return;

        if (elapsedSinceKick >= totalCooldown)
        {
            SetBarProgress(kickCooldownBar, 1f);
            kickCooldownBar.FillColor = new Color(0.13f, 0.77f, 0.37f, 1f);
            kickCooldownStatusLabel.text = "PRONTO";
            kickCooldownStatusLabel.color = new Color(0.08f, 0.50f, 0.24f, 1f);
            if (kickCooldownTitleLabel != null)
            {
                kickCooldownTitleLabel.color = Color.white;
            }
        }
        else
        {
            float pct = Mathf.Clamp01(elapsedSinceKick / Mathf.Max(0.01f, totalCooldown));
            SetBarProgress(kickCooldownBar, pct, snapOnDecrease: true);
            kickCooldownBar.FillColor = new Color(0.08f, 0.52f, 0.38f, 0.95f);

            float remaining = Mathf.Max(0f, totalCooldown - elapsedSinceKick);
            kickCooldownStatusLabel.text = $"{remaining:F1}s";
            kickCooldownStatusLabel.color = new Color(0.24f, 0.14f, 0.08f, 1f);
            if (kickCooldownTitleLabel != null)
            {
                kickCooldownTitleLabel.color = new Color(1f, 0.98f, 0.94f, 0.95f);
            }
        }
    }

    /// <summary>
    /// Adiciona uma nova notificação de eliminação estilo FPS no Kill Feed (canto superior direito).
    /// </summary>
    /// <param name="killerName">Nome de quem eliminou</param>
    /// <param name="victimName">Nome de quem foi eliminado</param>
    /// <param name="isHunterKill">True se o Hunter eliminou um sobrevivente; False se sobreviventes venceram o Hunter</param>
    /// <param name="weaponType">Tipo da arma/método ("scythe", "fireball", "kick")</param>
    public void AddKillFeedEntry(string killerName, string victimName, bool isHunterKill, string weaponType = "scythe")
    {
        if (root == null)
        {
            InitializeUI();
        }
        if (killFeedContainer == null && root != null)
        {
            killFeedContainer = root.Q<VisualElement>("kill-feed-container");
        }
        if (killFeedContainer == null) return;

        // Limita a quantidade máxima de notificações ativas (máx 5) para não sobrecarregar a UI
        while (killFeedContainer.childCount >= 5)
        {
            var oldest = killFeedContainer.ElementAt(0);
            if (oldest != null)
            {
                killFeedContainer.Remove(oldest);
            }
            else
            {
                break;
            }
        }

        var entry = new VisualElement();
        entry.AddToClassList("kill-feed-entry");
        if (isHunterKill)
        {
            entry.AddToClassList("kill-feed-entry--hunter-kill");
        }
        else
        {
            entry.AddToClassList("kill-feed-entry--survivor-kill");
        }

        // 1. Killer Label
        string displayKiller = string.IsNullOrWhiteSpace(killerName) ? (isHunterKill ? "A Morte" : "Sobrevivente") : killerName;
        var killerLabel = new Label(displayKiller);
        killerLabel.AddToClassList("kill-feed-killer");
        if (!isHunterKill)
        {
            killerLabel.AddToClassList("kill-feed-killer--survivor");
        }
        entry.Add(killerLabel);

        // 2. Weapon Badge com Ícone vetorial Cartoon correspondente
        EnsureSpritesLoaded();
        var weaponBadge = new VisualElement();
        weaponBadge.AddToClassList("kill-feed-weapon-badge");

        Sprite weaponSprite = weaponType?.ToLowerInvariant() switch
        {
            "fireball" => fireballIconSprite,
            "kick" => kickIconSprite,
            "scythe" => scytheIconSprite,
            _ => scytheIconSprite
        };
        var weaponIcon = new VisualElement();
        weaponIcon.AddToClassList("kill-feed-weapon-icon");
        if (weaponSprite != null)
        {
            weaponIcon.style.backgroundImage = new StyleBackground(weaponSprite);
        }
        weaponBadge.Add(weaponIcon);
        entry.Add(weaponBadge);

        // 3. Victim Label
        string displayVictim = string.IsNullOrWhiteSpace(victimName) ? "Jogador" : victimName;
        var victimLabel = new Label(displayVictim);
        victimLabel.AddToClassList("kill-feed-victim");
        if (!isHunterKill)
        {
            victimLabel.AddToClassList("kill-feed-victim--hunter");
        }
        entry.Add(victimLabel);

        // 4. Skull icon badge Cartoon de confirmação de eliminação
        var skullBadge = new VisualElement();
        skullBadge.AddToClassList("kill-feed-skull-badge");
        if (killSkullIconSprite != null)
        {
            skullBadge.style.backgroundImage = new StyleBackground(killSkullIconSprite);
        }
        entry.Add(skullBadge);

        killFeedContainer.Add(entry);

        StartCoroutine(KillFeedEntryLifecycleRoutine(entry));
    }

    private System.Collections.IEnumerator KillFeedEntryLifecycleRoutine(VisualElement entry)
    {
        // Aguarda o próximo frame para engatilhar a transição de slide-in suave
        yield return null;
        if (entry == null || entry.parent == null) yield break;

        entry.AddToClassList("kill-feed-entry--visible");

        // Tempo de exibição ativa no feed (5 segundos)
        yield return new WaitForSeconds(5.0f);

        if (entry == null || entry.parent == null) yield break;

        // Dispara transição suave de fadeout
        entry.RemoveFromClassList("kill-feed-entry--visible");
        entry.AddToClassList("kill-feed-entry--fadeout");

        // Aguarda término da transição de opacidade
        yield return new WaitForSeconds(0.45f);

        if (entry != null && entry.parent != null)
        {
            entry.parent.Remove(entry);
        }
    }

    [ContextMenu("Test Take Damage")]
    public void TestTakeDamage()
    {
        SetHunterLives(currentHunterLives - 1, maxHunterLives);
    }

    [ContextMenu("Test Wrong NPC Kill (Take Life)")]
    public void TestWrongNpcKill()
    {
        SetHunterLives(currentHunterLives - 1, maxHunterLives);
    }

    [ContextMenu("Test Multi-Hunter HUD (2 Hunters)")]
    public void TestMultiHunter2()
    {
        UpdateHunterLives(101, "Lucas", 2, 3);
        UpdateHunterLives(102, "Pedro", 3, 3);
    }

    [ContextMenu("Test Multi-Hunter HUD (3 Hunters)")]
    public void TestMultiHunter3()
    {
        UpdateHunterLives(101, "Lucas", 2, 3);
        UpdateHunterLives(102, "Pedro", 1, 3);
        UpdateHunterLives(103, "A Morte", 0, 3);
    }

    [ContextMenu("Test Reset Lives")]
    public void TestResetLives()
    {
        huntersData.Clear();
        SetHunterLives(maxHunterLives, maxHunterLives);
    }

    [ContextMenu("Test KillFeed Scythe")]
    public void TestKillFeedScythe()
    {
        AddKillFeedEntry("A Morte", "Player 2", isHunterKill: true, "scythe");
    }

    [ContextMenu("Test KillFeed Fireball")]
    public void TestKillFeedFireball()
    {
        AddKillFeedEntry("A Morte", "Player 3", isHunterKill: true, "fireball");
    }

    [ContextMenu("Test KillFeed Kick")]
    public void TestKillFeedKick()
    {
        AddKillFeedEntry("Lucas", "A Morte", isHunterKill: false, "kick");
    }
}
