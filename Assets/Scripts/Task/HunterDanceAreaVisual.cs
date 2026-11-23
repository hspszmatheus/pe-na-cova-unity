using System;
using UnityEngine;

/// <summary>
/// Renderiza localmente uma área circular roxa no chão ao redor da Morte (Hunter).
/// Indica visualmente o raio exato (3.5m) em que o sobrevivente precisa entrar para executar
/// a dança (teclas 2 ou 3) e concluir a tarefa "Dançar Próximo à Morte".
/// Utiliza um anel perimetral brilhante (LineRenderer) e um preenchimento translúcido no chão (Mesh).
/// </summary>
public class HunterDanceAreaVisual : MonoBehaviour
{
    public static HunterDanceAreaVisual Instance { get; private set; }

    /// <summary>
    /// Raio exato da área circular de dança ao redor do Hunter (em metros).
    /// </summary>
    public const float DanceRadius = 3.5f;

    [Header("Configurações do Círculo")]
    [SerializeField] private int segments = 64;
    [SerializeField] private float baseLineWidth = 0.22f;
    [SerializeField] private Color borderPulseColor = new Color(0.88f, 0.25f, 1.0f, 0.95f);
    [SerializeField] private Color fillDiscColor = new Color(0.75f, 0.15f, 0.95f, 0.18f);

    private Transform targetHunter;
    private LineRenderer lineRenderer;
    private Material lineMaterial;

    private MeshFilter fillMeshFilter;
    private MeshRenderer fillMeshRenderer;
    private Mesh fillMesh;
    private Material fillMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SetupVisuals();
    }

    private void SetupVisuals()
    {
        // 1. Configura o LineRenderer para a borda perimétrica
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.positionCount = segments;
        lineRenderer.startWidth = baseLineWidth;
        lineRenderer.endWidth = baseLineWidth;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

        // Material com suporte URP transparente
        lineMaterial = CreateTransparentMaterial(borderPulseColor);
        lineRenderer.material = lineMaterial;
        lineRenderer.startColor = borderPulseColor;
        lineRenderer.endColor = borderPulseColor;

        // 2. Configura o Mesh para o preenchimento suave no chão
        GameObject fillChild = new GameObject("GroundFillDisc");
        fillChild.transform.SetParent(transform, false);

        fillMeshFilter = fillChild.AddComponent<MeshFilter>();
        fillMeshRenderer = fillChild.AddComponent<MeshRenderer>();
        fillMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fillMeshRenderer.receiveShadows = false;

        fillMaterial = CreateTransparentMaterial(fillDiscColor);
        fillMeshRenderer.material = fillMaterial;

        fillMesh = new Mesh { name = "DanceAreaFillMesh" };
        fillMeshFilter.mesh = fillMesh;
    }

    private Material CreateTransparentMaterial(Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0);     // Alpha
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 100;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
        mat.color = baseColor;
        return mat;
    }

    private void LateUpdate()
    {
        if (targetHunter == null || !targetHunter.gameObject.activeInHierarchy)
        {
            SetVisualsEnabled(false);
            return;
        }

        SetVisualsEnabled(true);

        // Suave pulsação visual na espessura e no brilho
        float pulse = baseLineWidth + 0.04f * Mathf.Sin(Time.time * 4.0f);
        lineRenderer.startWidth = pulse;
        lineRenderer.endWidth = pulse;

        Color currentBorderColor = borderPulseColor;
        currentBorderColor.a = 0.85f + 0.15f * Mathf.Sin(Time.time * 4.0f);
        lineRenderer.startColor = currentBorderColor;
        lineRenderer.endColor = currentBorderColor;

        Vector3 center = targetHunter.position;
        float angleStep = 360f / segments;

        // Vértices do chão para a linha e para o disco de preenchimento
        Vector3[] perimeterPoints = new Vector3[segments];
        Vector3[] meshVertices = new Vector3[segments + 1];
        Color[] meshColors = new Color[segments + 1];
        int[] meshTriangles = new int[segments * 3];

        // Centro do disco
        float centerGroundY = GetGroundHeight(center, center.y);
        meshVertices[0] = new Vector3(center.x, centerGroundY + 0.035f, center.z);
        meshColors[0] = new Color(fillDiscColor.r, fillDiscColor.g, fillDiscColor.b, fillDiscColor.a * 0.7f);

        for (int i = 0; i < segments; i++)
        {
            float rad = (i * angleStep) * Mathf.Deg2Rad;
            Vector3 pt = center + new Vector3(Mathf.Cos(rad) * DanceRadius, 0f, Mathf.Sin(rad) * DanceRadius);

            float groundY = GetGroundHeight(pt, center.y);
            pt.y = groundY + 0.055f; // Leve elevação para sobrepor chão e calçadas perfeitamente

            perimeterPoints[i] = pt;

            // Vértice da malha do chão
            meshVertices[i + 1] = new Vector3(pt.x, groundY + 0.035f, pt.z);
            meshColors[i + 1] = fillDiscColor;

            // Triângulos conectando o centro à borda
            int nextIdx = (i + 1) % segments;
            meshTriangles[i * 3] = 0;
            meshTriangles[i * 3 + 1] = i + 1;
            meshTriangles[i * 3 + 2] = nextIdx + 1;
        }

        lineRenderer.SetPositions(perimeterPoints);

        if (fillMesh != null)
        {
            fillMesh.Clear();
            fillMesh.vertices = meshVertices;
            fillMesh.colors = meshColors;
            fillMesh.triangles = meshTriangles;
            fillMesh.RecalculateBounds();
        }
    }

    private float GetGroundHeight(Vector3 pos, float defaultY)
    {
        RaycastHit[] hits = Physics.RaycastAll(pos + Vector3.up * 2.5f, Vector3.down, 6.0f, ~0, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            float bestDist = float.MaxValue;
            float groundY = defaultY;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (col == null || col.isTrigger) continue;
                if (col.CompareTag("Player")) continue;
                if (col.GetComponentInParent<PlayerController>() != null) continue;
                if (col.GetComponentInParent<NPCController>() != null) continue;

                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    groundY = hits[i].point.y;
                    found = true;
                }
            }

            if (found) return groundY;
        }
        return defaultY;
    }

    private void SetVisualsEnabled(bool isEnabled)
    {
        if (lineRenderer != null && lineRenderer.enabled != isEnabled)
        {
            lineRenderer.enabled = isEnabled;
        }
        if (fillMeshRenderer != null && fillMeshRenderer.enabled != isEnabled)
        {
            fillMeshRenderer.enabled = isEnabled;
        }
    }

    /// <summary>
    /// Exibe a área circular roxa no chão ao redor do Hunter informado.
    /// </summary>
    public static void Show(Transform hunterTransform)
    {
        if (hunterTransform == null) return;

        if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
        {
            Hide();
            return;
        }

        if (Instance == null)
        {
            var go = new GameObject("HunterDanceAreaVisual");
            Instance = go.AddComponent<HunterDanceAreaVisual>();
        }

        Instance.targetHunter = hunterTransform;
        Instance.gameObject.SetActive(true);
        Instance.SetVisualsEnabled(true);
    }

    /// <summary>
    /// Oculta e desativa o círculo do chão.
    /// </summary>
    public static void Hide()
    {
        if (Instance != null)
        {
            Instance.targetHunter = null;
            Instance.SetVisualsEnabled(false);
            Instance.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Verifica se a posição informada (ex: jogador sobrevivente) está dentro do raio de dança da Morte.
    /// </summary>
    public static bool IsPlayerInsideDanceArea(Vector3 playerPos, Transform hunterTransform)
    {
        if (hunterTransform == null) return false;

        Vector3 diff = playerPos - hunterTransform.position;
        diff.y = 0f;
        // Margem de 0.25m para tolerância e suavidade na borda
        return diff.magnitude <= (DanceRadius + 0.25f);
    }

    private void OnDestroy()
    {
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }
        if (fillMaterial != null)
        {
            Destroy(fillMaterial);
            fillMaterial = null;
        }
        if (fillMesh != null)
        {
            Destroy(fillMesh);
            fillMesh = null;
        }
    }
}
