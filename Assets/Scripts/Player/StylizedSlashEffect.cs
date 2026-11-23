using UnityEngine;

/// <summary>
/// Executa a animação estilizada de corte (Slash VFX) da Morte (Hunter).
/// Utiliza a malha curva e as texturas estilizadas do pacote slash5-HungNguyen,
/// animando o corte precisamente ao longo da trajetória da foice.
/// </summary>
public class StylizedSlashEffect : MonoBehaviour
{
    [Header("Renderização")]
    [SerializeField] private MeshRenderer meshRenderer;

    [Header("Animação do Corte")]
    [Tooltip("Duração do corte visual em segundos")]
    [SerializeField] private float duration = 0.28f;

    [Tooltip("Curva de avanço da lâmina do corte (0 a 1)")]
    [SerializeField] private AnimationCurve progressCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 2.6f, 2.6f),
        new Keyframe(0.45f, 0.75f, 1.0f, 1.0f),
        new Keyframe(1.0f, 1.0f, 0.2f, 0.2f)
    );

    [Tooltip("Curva de opacidade do corte ao longo da animação")]
    [SerializeField] private AnimationCurve opacityCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.08f, 1f),
        new Keyframe(0.65f, 1f),
        new Keyframe(1.0f, 0f)
    );

    private MaterialPropertyBlock propBlock;
    private float timer = -1f;
    private bool isPlaying = false;

    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int OpacityId = Shader.PropertyToID("_SlashOpacity");

    public bool IsPlaying => isPlaying;

    void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }
        if (propBlock == null)
        {
            propBlock = new MaterialPropertyBlock();
        }
        if (meshRenderer != null && !isPlaying)
        {
            meshRenderer.enabled = false;
        }
    }

    /// <summary>
    /// Define manualmente o progresso e opacidade do corte (útil para testes ou capturas).
    /// </summary>
    public void SetProgress(float progress, float opacity)
    {
        Initialize();
        if (meshRenderer != null)
        {
            meshRenderer.enabled = opacity > 0.001f;
            meshRenderer.GetPropertyBlock(propBlock);
            propBlock.SetFloat(ProgressId, progress);
            propBlock.SetFloat(OpacityId, opacity);
            meshRenderer.SetPropertyBlock(propBlock);
        }
    }

    /// <summary>
    /// Dispara a animação do corte.
    /// </summary>
    public void Play()
    {
        Initialize();
        gameObject.SetActive(true);
        enabled = true;

        timer = 0f;
        isPlaying = true;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }

        UpdateMaterial(0f, 0.5f);
    }

    /// <summary>
    /// Interrompe o corte imediatamente (ex: ao ser atingido).
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
        timer = -1f;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }

        UpdateMaterial(1f, 0f);
    }

    void Update()
    {
        if (!isPlaying) return;

        timer += Time.deltaTime;
        float normTime = Mathf.Clamp01(timer / duration);

        float progress = progressCurve != null ? progressCurve.Evaluate(normTime) : normTime;
        float opacity = opacityCurve != null ? opacityCurve.Evaluate(normTime) : (1f - normTime);

        UpdateMaterial(progress, opacity);

        if (timer >= duration)
        {
            Stop();
        }
    }

    private void UpdateMaterial(float progress, float opacity)
    {
        if (meshRenderer == null) return;
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        meshRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat(ProgressId, progress);
        propBlock.SetFloat(OpacityId, opacity);
        meshRenderer.SetPropertyBlock(propBlock);
    }
}
