using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela de Splash Inicial cinemática com a caveira da Morte e o título 'Pena Cova'.
/// Exibida uma única vez ao iniciar o jogo, realizando uma transição suave para o menu inicial.
/// </summary>
public class InitialGameSplashScreen : MonoBehaviour
{
    private static bool hasShownSplashThisSession = false;

    [Header("Componentes Visuais")]
    [SerializeField] private CanvasGroup splashCanvasGroup;
    [SerializeField] private RectTransform skullTransform;
    [SerializeField] private Image skullImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private TextMeshProUGUI promptText;

    [Header("Configurações de Tempo")]
    [SerializeField] private float displayHoldDuration = 2.2f;
    [SerializeField] private float fadeOutDuration = 0.55f;
    [SerializeField] private float skullPulseFrequency = 2.4f;
    [SerializeField] private float skullPulseAmplitude = 0.045f;

    private bool isSkipped = false;
    private Coroutine splashRoutine;

    public static bool HasShownSplashThisSession => hasShownSplashThisSession;

    private void Awake()
    {
        // Se já exibiu nesta sessão ou se o jogador veio de um retorno de partida, dispensa imediatamente
        if (hasShownSplashThisSession || LobbyMenuFlowController.ForceReturnToRoomBrowser)
        {
            if (splashCanvasGroup != null)
            {
                splashCanvasGroup.alpha = 0f;
                splashCanvasGroup.blocksRaycasts = false;
                splashCanvasGroup.interactable = false;
            }
            gameObject.SetActive(false);
            return;
        }

        if (splashCanvasGroup != null)
        {
            splashCanvasGroup.alpha = 1f;
            splashCanvasGroup.blocksRaycasts = true;
            splashCanvasGroup.interactable = true;
        }
    }

    private void Start()
    {
        if (hasShownSplashThisSession || LobbyMenuFlowController.ForceReturnToRoomBrowser)
        {
            gameObject.SetActive(false);
            return;
        }

        splashRoutine = StartCoroutine(SplashSequenceRoutine());
    }

    private void Update()
    {
        if (hasShownSplashThisSession || !gameObject.activeSelf) return;

        // Animação de pulso e respiração da caveira da Morte
        if (skullTransform != null)
        {
            float scale = 1f + Mathf.Sin(Time.unscaledTime * skullPulseFrequency) * skullPulseAmplitude;
            skullTransform.localScale = new Vector3(scale, scale, 1f);
        }

        // Animação de fade sutil no texto de continuar
        if (promptText != null)
        {
            float promptAlpha = 0.45f + Mathf.PingPong(Time.unscaledTime * 1.5f, 0.55f);
            Color c = promptText.color;
            promptText.color = new Color(c.r, c.g, c.b, promptAlpha);
        }

        // Permite pular imediatamente com qualquer tecla ou clique do mouse
        if (!isSkipped && (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
        {
            isSkipped = true;
            if (splashRoutine != null)
            {
                StopCoroutine(splashRoutine);
                splashRoutine = StartCoroutine(FadeOutAndFinishRoutine());
            }
        }
    }

    private IEnumerator SplashSequenceRoutine()
    {
        float timer = 0f;
        while (timer < displayHoldDuration && !isSkipped)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return FadeOutAndFinishRoutine();
    }

    private IEnumerator FadeOutAndFinishRoutine()
    {
        hasShownSplashThisSession = true;

        if (splashCanvasGroup != null)
        {
            float startAlpha = splashCanvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                splashCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            splashCanvasGroup.alpha = 0f;
            splashCanvasGroup.blocksRaycasts = false;
            splashCanvasGroup.interactable = false;
        }

        gameObject.SetActive(false);
    }
}
