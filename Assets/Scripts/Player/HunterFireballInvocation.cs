using UnityEngine;

/// <summary>
/// Controla o efeito visual da invocação mágica da bola de fogo na palma da mão da Morte (Hunter).
/// Mescla as chamas estilizadas do VFX_Fire 1 com o núcleo de energia concentrada e iluminação dinâmica.
/// </summary>
public class HunterFireballInvocation : MonoBehaviour
{
    [Header("Configurações de Invocação")]
    [Tooltip("Duração do carregamento/invocação na mão")]
    [SerializeField] private float duration = 0.65f;
    [Tooltip("Escala inicial ao começar a invocar")]
    [SerializeField] private Vector3 startScale = new Vector3(0.15f, 0.15f, 0.15f);
    [Tooltip("Escala máxima atingida no pico da invocação")]
    [SerializeField] private Vector3 maxScale = new Vector3(0.39f, 0.39f, 0.39f);

    [Header("Iluminação Dinâmica")]
    [SerializeField] private Light fireLight;
    [SerializeField] private float maxLightIntensity = 2.5f;
    [SerializeField] private float lightRange = 3.6f;

    [Header("Sistemas de Partículas")]
    [SerializeField] private ParticleSystem[] fireSystems;

    private float elapsed = 0f;
    private bool isReleased = false;

    void Awake()
    {
        if (fireLight == null) fireLight = GetComponentInChildren<Light>();
        if (fireSystems == null || fireSystems.Length == 0) fireSystems = GetComponentsInChildren<ParticleSystem>();

        transform.localScale = startScale;
        if (fireLight != null)
        {
            fireLight.intensity = 0.2f;
            fireLight.range = lightRange;
        }

        GameAudioManager.Instance?.PlayFireballCharge(transform);
    }

    void Update()
    {
        if (isReleased) return;

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);

        // Curva suave de expansão da energia ígnea na palma da mão
        float scaleT = Mathf.SmoothStep(0f, 1f, progress);
        transform.localScale = Vector3.Lerp(startScale, maxScale, scaleT);

        // Tremulação orgânica da luz de fogo
        if (fireLight != null)
        {
            float flicker = Mathf.Sin(Time.time * 28f) * 0.25f + Mathf.Cos(Time.time * 43f) * 0.15f;
            fireLight.intensity = Mathf.Lerp(0.3f, maxLightIntensity, progress) + flicker;
        }
    }

    /// <summary>
    /// Chamado no momento do disparo para fazer a transição orgânica para a bola de fogo em voo.
    /// </summary>
    public void ReleaseAndDestroy()
    {
        isReleased = true;

        if (fireSystems != null)
        {
            foreach (var ps in fireSystems)
            {
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }
            }
        }

        if (fireLight != null)
        {
            fireLight.enabled = false;
        }

        Destroy(gameObject, 0.45f);
    }
}
