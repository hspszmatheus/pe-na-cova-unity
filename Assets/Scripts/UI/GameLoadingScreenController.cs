using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controlador global e persistente da Tela de Carregamento (Loading Screen) do jogo Pena Cova.
/// Apresenta o design estilizado em cartoon com a icônica cabeça de caveira da Morte,
/// barra de progresso animada e rotação de dicas dinâmicas de sobrevivência.
/// </summary>
[DisallowMultipleComponent]
public class GameLoadingScreenController : MonoBehaviour
{
    public static GameLoadingScreenController Instance { get; private set; }

    [Header("Estrutura Principal")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform cardContainer;
    [SerializeField] private Image backdropImage;
    [SerializeField] private Image cardBackgroundImage;

    [Header("Elementos de Destaque")]
    [SerializeField] private RectTransform skullTransform;
    [SerializeField] private Image skullImage;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI subtitleLabel;

    [Header("Barra de Progresso")]
    [SerializeField] private RectTransform progressBarRect;
    [SerializeField] private Image progressTrackImage;
    [SerializeField] private Image progressFillImage;
    [SerializeField] private TextMeshProUGUI progressPercentLabel;

    [Header("Dicas de Sobrevivência (Lore/Tips)")]
    [SerializeField] private CanvasGroup tipsCanvasGroup;
    [SerializeField] private TextMeshProUGUI tipLabel;
    [SerializeField] private Image tipIconImage;

    [Header("Configurações de Animação")]
    [SerializeField] private float skullBobAmplitude = 5.0f;
    [SerializeField] private float skullBobFrequency = 2.8f;
    [SerializeField] private float skullBreathAmplitude = 0.04f;
    [SerializeField] private float defaultFadeDuration = 0.45f;
    [SerializeField] private float tipCycleInterval = 3.8f;

    [Header("Timeout & Proteção contra Travamento")]
    [Tooltip("Tempo limite máximo (em segundos) para qualquer carregamento antes de dar time out e retornar à escolha de salas")]
    [SerializeField] private float defaultLoadingTimeoutSeconds = 15.0f;

    private Coroutine watchdogRoutine;
    private Coroutine abortRoutine;
    private bool isAbortingToLobby = false;
    private bool isNetworkDisconnectHooked = false;

    private readonly string[] survivalTips = new string[]
    {
        "Fique atento ao som das asas e aos passos pesados da Morte quando ela estiver por perto.",
        "Conclua suas tarefas para ajudar os sobreviventes a escaparem de Pena Cova.",
        "Não fique parado! Se demorar muito para fazer tarefas, sua alma será revelada à Morte.",
        "Sobreviventes podem desferir um chute para atordoar a Morte temporariamente (botão direito ou 'E').",
        "A Morte possui 3x mais fôlego que os sobreviventes para perseguir suas presas pela cidade.",
        "Tente se misturar aos 80 NPCs caminhando no mesmo ritmo pela calçada para despistar o caçador.",
        "Os destroços do avião marcam o local do impacto inicial onde a Morte chegou.",
        "Tarefas concluídas emitem um aviso e deixam um rastro temporário que a Morte pode notar.",
        "Fique de olho nas rotas de fuga ao realizar tarefas em becos estreitos.",
        "Jogadores eliminados continuam no modo espectador para acompanhar o desfecho da partida."
    };

    private Vector2 skullBasePos;
    private bool skullBasePosCaptured = false;
    private float targetProgress = 0f;
    private float displayedProgress = 0f;
    private float minDisplayTimer = 0f;
    private bool isVisible = false;
    private bool autoDismissOnSceneLoad = true;
    private Coroutine fadeRoutine;
    private Coroutine tipsCycleRoutine;
    private int currentTipIndex = 0;

    public bool IsVisible => isVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureInitializedBeforeSceneLoad()
    {
        if (Instance == null)
        {
            var prefab = Resources.Load<GameObject>("GameLoadingScreenCanvas");
            if (prefab != null)
            {
                var go = Instantiate(prefab);
                go.name = "GameLoadingScreenCanvas";
                DontDestroyOnLoad(go);
            }
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (skullTransform != null && !skullBasePosCaptured)
        {
            skullBasePos = skullTransform.anchoredPosition;
            skullBasePosCaptured = true;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (canvas != null)
        {
            canvas.sortingOrder = 9999;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureNetworkDisconnectHooked();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnhookNetworkDisconnect();
    }

    private void Start()
    {
        // Se ainda não capturou a posição base do crânio, captura no Start
        if (skullTransform != null && !skullBasePosCaptured)
        {
            skullBasePos = skullTransform.anchoredPosition;
            skullBasePosCaptured = true;
        }

        EnsureNetworkDisconnectHooked();
    }

    private void Update()
    {
        EnsureNetworkDisconnectHooked();

        if (!isVisible && (canvasGroup == null || canvasGroup.alpha <= 0.001f))
            return;

        // Se o jogador pressionar ESC durante um carregamento demorado, aborta e volta para a escolha de salas
        bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) escPressed = true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape)) escPressed = true;
#endif
        if (isVisible && !isAbortingToLobby && escPressed)
        {
            Debug.LogWarning("[GameLoadingScreenController] Jogador pressionou ESC para cancelar carregamento. Retornando ao navegador de salas...");
            AbortLoadingAndReturnToRoomBrowser("Carregamento cancelado.");
            return;
        }

        float dt = Time.unscaledDeltaTime;

        // 1. Animação de flutuação e respiração da caveira da Morte
        if (skullTransform != null && skullBasePosCaptured)
        {
            float bobY = Mathf.Sin(Time.unscaledTime * skullBobFrequency) * skullBobAmplitude;
            skullTransform.anchoredPosition = skullBasePos + new Vector2(0f, bobY);

            float scalePulse = 1f + Mathf.Sin(Time.unscaledTime * (skullBobFrequency * 0.9f)) * skullBreathAmplitude;
            skullTransform.localScale = new Vector3(scalePulse, scalePulse, 1f);
        }

        // 2. Interpolação suave do progresso da barra
        if (displayedProgress < targetProgress)
        {
            displayedProgress = Mathf.MoveTowards(displayedProgress, targetProgress, 1.4f * dt);
        }
        else if (displayedProgress > targetProgress && targetProgress >= 0.99f)
        {
            displayedProgress = targetProgress;
        }

        UpdateProgressBarVisuals(displayedProgress);

        // 3. Contagem do tempo mínimo de exibição
        if (minDisplayTimer > 0f)
        {
            minDisplayTimer -= dt;
        }
    }

    private void UpdateProgressBarVisuals(float progress)
    {
        float clamped = Mathf.Clamp01(progress);

        if (progressFillImage != null)
        {
            progressFillImage.fillAmount = clamped;
        }

        if (progressPercentLabel != null)
        {
            progressPercentLabel.text = $"{Mathf.RoundToInt(clamped * 100f)}%";
        }
    }

    /// <summary>
    /// Exibe a tela de carregamento com título e subtítulo customizados e watchdog de timeout.
    /// </summary>
    public void Show(string title = "CARREGANDO...", string subtitle = "Aguarde um momento...", float minDisplayTime = 1.8f, bool autoDismiss = true, float timeoutSeconds = -1f)
    {
        isAbortingToLobby = false;
        if (titleLabel != null) titleLabel.text = title;
        if (subtitleLabel != null) subtitleLabel.text = subtitle;

        minDisplayTimer = Mathf.Max(0.5f, minDisplayTime);
        autoDismissOnSceneLoad = autoDismiss;
        targetProgress = 0.15f;
        displayedProgress = 0f;
        UpdateProgressBarVisuals(0f);

        isVisible = true;

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeCanvasGroupRoutine(1f, defaultFadeDuration));

        if (tipsCycleRoutine != null) StopCoroutine(tipsCycleRoutine);
        tipsCycleRoutine = StartCoroutine(TipsCycleRoutine());

        // Inicia o Watchdog de timeout para qualquer carregamento que exceder o tempo limite
        if (watchdogRoutine != null) StopCoroutine(watchdogRoutine);
        float effectiveTimeout = timeoutSeconds > 0f ? timeoutSeconds : defaultLoadingTimeoutSeconds;
        watchdogRoutine = StartCoroutine(LoadingWatchdogRoutine(effectiveTimeout));
    }

    /// <summary>
    /// Atualiza o progresso visual da barra de carregamento (de 0.0 a 1.0).
    /// </summary>
    public void SetProgress(float progress, string statusSubtitle = null)
    {
        targetProgress = Mathf.Clamp01(progress);
        if (!string.IsNullOrEmpty(statusSubtitle) && subtitleLabel != null)
        {
            subtitleLabel.text = statusSubtitle;
        }
    }

    /// <summary>
    /// Atualiza o texto e a porcentagem de sincronização da tela de carregamento entre os jogadores conectados.
    /// </summary>
    public void SetSyncStatus(string title, string subtitle, float progress)
    {
        if (!string.IsNullOrEmpty(title) && titleLabel != null)
        {
            titleLabel.text = title;
        }
        if (!string.IsNullOrEmpty(subtitle) && subtitleLabel != null)
        {
            subtitleLabel.text = subtitle;
        }
        targetProgress = Mathf.Clamp01(progress);
    }

    /// <summary>
    /// Oculta suavemente a tela de carregamento.
    /// </summary>
    public void Hide(float fadeDuration = -1f, bool ignoreMinDisplayTime = false)
    {
        if (watchdogRoutine != null)
        {
            StopCoroutine(watchdogRoutine);
            watchdogRoutine = null;
        }

        if (!isVisible) return;
        isVisible = false;
        isAbortingToLobby = false;

        if (ignoreMinDisplayTime)
        {
            minDisplayTimer = 0f;
        }

        float duration = fadeDuration > 0f ? fadeDuration : defaultFadeDuration;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(HideSequenceRoutine(duration));
    }

    private IEnumerator HideSequenceRoutine(float fadeDuration)
    {
        // Garante que o progresso alcance 100% antes de desaparecer
        targetProgress = 1.0f;
        displayedProgress = 1.0f;
        UpdateProgressBarVisuals(1.0f);

        // Aguarda expirar o tempo mínimo de visualização (se não foi cancelado por sincronização)
        while (minDisplayTimer > 0f)
        {
            yield return null;
        }

        yield return FadeCanvasGroupRoutine(0f, fadeDuration);

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (tipsCycleRoutine != null)
        {
            StopCoroutine(tipsCycleRoutine);
            tipsCycleRoutine = null;
        }
    }

    private IEnumerator FadeCanvasGroupRoutine(float targetAlpha, float duration)
    {
        if (canvasGroup == null) yield break;

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / Mathf.Max(0.01f, duration));
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    private IEnumerator TipsCycleRoutine()
    {
        if (survivalTips == null || survivalTips.Length == 0) yield break;

        currentTipIndex = Random.Range(0, survivalTips.Length);
        SetCurrentTip(survivalTips[currentTipIndex]);

        while (isVisible)
        {
            yield return new WaitForSecondsRealtime(tipCycleInterval);
            if (!isVisible) break;

            // Fade out da dica anterior
            if (tipsCanvasGroup != null)
            {
                float t = 0f;
                while (t < 0.25f)
                {
                    t += Time.unscaledDeltaTime;
                    tipsCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t / 0.25f);
                    yield return null;
                }
                tipsCanvasGroup.alpha = 0f;
            }

            // Seleciona nova dica aleatória diferente da anterior
            int nextIndex;
            do
            {
                nextIndex = Random.Range(0, survivalTips.Length);
            } while (survivalTips.Length > 1 && nextIndex == currentTipIndex);

            currentTipIndex = nextIndex;
            SetCurrentTip(survivalTips[currentTipIndex]);

            // Fade in da nova dica
            if (tipsCanvasGroup != null)
            {
                float t = 0f;
                while (t < 0.3f)
                {
                    t += Time.unscaledDeltaTime;
                    tipsCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t / 0.3f);
                    yield return null;
                }
                tipsCanvasGroup.alpha = 1f;
            }
        }
    }

    private void SetCurrentTip(string tip)
    {
        if (tipLabel != null)
        {
            tipLabel.text = $"<b>DICA:</b> {tip}";
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (isVisible)
        {
            // Se a cena carregada for a de escolha de salas (JoinByBrowsing), SEMPRE descarta a tela de carregamento!
            if (scene.name == "JoinByBrowsing" || scene.name.Contains("Join") || scene.name.Contains("Menu"))
            {
                autoDismissOnSceneLoad = true;
                isAbortingToLobby = false;
                targetProgress = 1.0f;
                StartCoroutine(AutoDismissAfterSceneLoadRoutine());
                return;
            }

            if (autoDismissOnSceneLoad)
            {
                // Se o descarte automático estiver habilitado (ex: retorno ao Lobby), avança para 100% e oculta suavemente
                targetProgress = 1.0f;
                StartCoroutine(AutoDismissAfterSceneLoadRoutine());
            }
        }
    }

    private IEnumerator AutoDismissAfterSceneLoadRoutine()
    {
        yield return new WaitForSecondsRealtime(0.4f);
        Hide(defaultFadeDuration);
    }

    #region Watchdog, Timeout e Aborto Seguro de Carregamento

    private void EnsureNetworkDisconnectHooked()
    {
        if (!isNetworkDisconnectHooked && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleNetworkManagerDisconnect;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleNetworkManagerDisconnect;
            isNetworkDisconnectHooked = true;
        }
    }

    private void UnhookNetworkDisconnect()
    {
        if (isNetworkDisconnectHooked && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleNetworkManagerDisconnect;
            isNetworkDisconnectHooked = false;
        }
    }

    private void HandleNetworkManagerDisconnect(ulong clientId)
    {
        if (!isVisible || isAbortingToLobby) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // Se este cliente local foi desconectado do servidor enquanto estava em tela de carregamento
        if (!nm.IsServer && (clientId == nm.LocalClientId || !nm.IsConnectedClient))
        {
            Debug.LogWarning("[GameLoadingScreenController] Desconexão de rede detectada durante carregamento! Retornando ao navegador de salas...");
            AbortLoadingAndReturnToRoomBrowser("Desconectado da partida.");
        }
    }

    private IEnumerator LoadingWatchdogRoutine(float timeoutSeconds)
    {
        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
            if (!isVisible || isAbortingToLobby) yield break;
        }

        if (isVisible && !isAbortingToLobby)
        {
            Debug.LogWarning($"[GameLoadingScreenController] TEMPO LIMITE DE CARREGAMENTO ESGOTADO ({timeoutSeconds:F1}s)! Retornando à escolha de salas...");
            AbortLoadingAndReturnToRoomBrowser("Tempo limite esgotado.");
        }
    }

    /// <summary>
    /// Cancela imediatamente qualquer carregamento travado ou demorado e retorna os jogadores
    /// com segurança para a tela de escolha das salas (JoinByBrowsing).
    /// </summary>
    public void AbortLoadingAndReturnToRoomBrowser(string reason = null)
    {
        if (isAbortingToLobby) return;
        isAbortingToLobby = true;

        if (watchdogRoutine != null)
        {
            StopCoroutine(watchdogRoutine);
            watchdogRoutine = null;
        }

        if (abortRoutine != null) StopCoroutine(abortRoutine);
        abortRoutine = StartCoroutine(AbortAndReturnRoutine(reason));
    }

    private IEnumerator AbortAndReturnRoutine(string reason)
    {
        Debug.LogWarning($"[GameLoadingScreenController] Abortando carregamento. Motivo: {reason ?? "Timeout"}. Retornando à escolha de salas...");

        if (subtitleLabel != null)
        {
            subtitleLabel.text = !string.IsNullOrEmpty(reason)
                ? $"{reason} Retornando à escolha de salas..."
                : "Retornando à escolha de salas...";
        }

        targetProgress = 1.0f;
        displayedProgress = 1.0f;
        UpdateProgressBarVisuals(1.0f);

        // 1. Libera cursor e restaura timescale
        Time.timeScale = 1.0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 2. Reseta estados de pause/task
        InGamePauseMenuController.ResetMenuState();
        PlayerController.LocalInstance?.CancelTaskInteraction();

        // 3. Força flag de retorno ao navegador de salas em JoinByBrowsing
        LobbyMenuFlowController.ForceReturnToRoomBrowser = true;

        // 4. Sai de sessões multiplayer UGS ativas com timeout de segurança
        yield return InGamePauseMenuController.LeaveActiveMultiplayerSessionsCoroutine(2.0f);

        // 5. Desliga o NetworkManager se estiver ativo
        yield return InGamePauseMenuController.ShutdownNetworkManagerCoroutine(1.5f);

        // 6. Se não estiver em JoinByBrowsing, carrega a cena de salas
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "JoinByBrowsing")
        {
            autoDismissOnSceneLoad = true;
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("JoinByBrowsing", LoadSceneMode.Single);
            if (asyncLoad != null)
            {
                float loadWait = 0f;
                while (!asyncLoad.isDone && loadWait < 5.0f)
                {
                    loadWait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }
        else
        {
            // Se já estiver em JoinByBrowsing, volta diretamente ao navegador de salas
            LobbyMenuFlowController.Instance?.ExitSoloConnectedRoomToBrowser();
        }

        yield return new WaitForSecondsRealtime(0.25f);
        Hide(fadeDuration: 0.35f, ignoreMinDisplayTime: true);
        abortRoutine = null;
        isAbortingToLobby = false;
    }

    #endregion
}
