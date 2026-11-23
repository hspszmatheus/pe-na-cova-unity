using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Renderiza localmente a área circular roxa no chão ao redor da Morte (Hunter)
/// durante a conjuração da habilidade de Revelação (segurar tecla E por 3 segundos).
/// Mostra visualmente o raio exato de 5.5m que será escaneado e emite um pulso ao concluir.
/// </summary>
public class HunterRevealAreaVisual : MonoBehaviour
{
    public static HunterRevealAreaVisual Instance { get; private set; }

    [Header("Configurações do Círculo de Revelação")]
    [SerializeField] private int segments = 64;
    [SerializeField] private float baseLineWidth = 0.26f;
    [SerializeField] private Color borderPulseColor = new Color(0.72f, 0.18f, 1.0f, 0.95f);
    [SerializeField] private Color fillDiscColor = new Color(0.62f, 0.12f, 0.95f, 0.18f);

    private Transform targetHunter;
    private float currentRadius = 5.5f;
    private LineRenderer lineRenderer;
    private Material lineMaterial;

    private MeshFilter fillMeshFilter;
    private MeshRenderer fillMeshRenderer;
    private Mesh fillMesh;
    private Material fillMaterial;

    private Coroutine pulseWaveRoutine = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SetupVisuals();
        SetVisualsEnabled(false);
    }

    private void SetupVisuals()
    {
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

        lineMaterial = CreateTransparentMaterial(borderPulseColor);
        lineRenderer.material = lineMaterial;
        lineRenderer.startColor = borderPulseColor;
        lineRenderer.endColor = borderPulseColor;

        GameObject fillChild = new GameObject("RevealGroundFillDisc");
        fillChild.transform.SetParent(transform, false);

        fillMeshFilter = fillChild.AddComponent<MeshFilter>();
        fillMeshRenderer = fillChild.AddComponent<MeshRenderer>();
        fillMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fillMeshRenderer.receiveShadows = false;

        fillMaterial = CreateTransparentMaterial(fillDiscColor);
        fillMeshRenderer.material = fillMaterial;

        fillMesh = new Mesh { name = "HunterRevealFillMesh" };
        fillMeshFilter.mesh = fillMesh;
    }

    private Material CreateTransparentMaterial(Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 105;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
        mat.color = baseColor;
        return mat;
    }

    private void LateUpdate()
    {
        if (pulseWaveRoutine != null) return;

        if (targetHunter == null || !targetHunter.gameObject.activeInHierarchy)
        {
            SetVisualsEnabled(false);
            return;
        }

        SetVisualsEnabled(true);

        float pulse = baseLineWidth + 0.05f * Mathf.Sin(Time.time * 6.0f);
        lineRenderer.startWidth = pulse;
        lineRenderer.endWidth = pulse;

        Color currentBorderColor = borderPulseColor;
        currentBorderColor.a = 0.80f + 0.20f * Mathf.Sin(Time.time * 6.0f);
        lineRenderer.startColor = currentBorderColor;
        lineRenderer.endColor = currentBorderColor;

        Vector3 center = targetHunter.position;
        RebuildCircleMesh(center, currentRadius);
    }

    private void RebuildCircleMesh(Vector3 center, float radius)
    {
        float angleStep = 360f / segments;
        Vector3[] perimeterPoints = new Vector3[segments];
        Vector3[] meshVertices = new Vector3[segments + 1];
        Color[] meshColors = new Color[segments + 1];
        int[] meshTriangles = new int[segments * 3];

        float centerGroundY = GetGroundHeight(center, center.y);
        meshVertices[0] = new Vector3(center.x, centerGroundY + 0.04f, center.z);
        meshColors[0] = new Color(fillDiscColor.r, fillDiscColor.g, fillDiscColor.b, fillDiscColor.a * 0.8f);

        for (int i = 0; i < segments; i++)
        {
            float rad = (i * angleStep) * Mathf.Deg2Rad;
            Vector3 pt = center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);

            float groundY = GetGroundHeight(pt, center.y);
            pt.y = groundY + 0.06f;

            perimeterPoints[i] = pt;

            meshVertices[i + 1] = new Vector3(pt.x, groundY + 0.04f, pt.z);
            meshColors[i + 1] = fillDiscColor;

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

    public static void Show(Transform hunterTransform, float radius)
    {
        if (hunterTransform == null) return;
        EnsureInstanceExists();

        if (Instance != null)
        {
            if (Instance.pulseWaveRoutine != null)
            {
                Instance.StopCoroutine(Instance.pulseWaveRoutine);
                Instance.pulseWaveRoutine = null;
            }
            Instance.targetHunter = hunterTransform;
            Instance.currentRadius = radius;
            Instance.SetVisualsEnabled(true);
        }
    }

    public static void Hide()
    {
        if (Instance != null)
        {
            if (Instance.pulseWaveRoutine != null)
            {
                Instance.StopCoroutine(Instance.pulseWaveRoutine);
                Instance.pulseWaveRoutine = null;
            }
            Instance.targetHunter = null;
            Instance.SetVisualsEnabled(false);
        }
    }

    public static void TriggerPulseAndHide(Vector3 center, float baseRadius)
    {
        EnsureInstanceExists();
        if (Instance != null)
        {
            Instance.targetHunter = null;
            if (Instance.pulseWaveRoutine != null)
            {
                Instance.StopCoroutine(Instance.pulseWaveRoutine);
            }
            Instance.pulseWaveRoutine = Instance.StartCoroutine(Instance.PulseWaveRoutine(center, baseRadius));
        }
    }

    private IEnumerator PulseWaveRoutine(Vector3 center, float baseRadius)
    {
        SetVisualsEnabled(true);
        float duration = 0.45f;
        float elapsed = 0f;

        Color startBorder = borderPulseColor;
        Color startFill = fillDiscColor;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float currentWaveRadius = Mathf.Lerp(baseRadius, baseRadius * 1.35f, t);
            float alpha = 1f - t;

            if (lineRenderer != null)
            {
                lineRenderer.startWidth = Mathf.Lerp(baseLineWidth * 1.5f, 0.05f, t);
                lineRenderer.endWidth = lineRenderer.startWidth;
                Color bColor = startBorder;
                bColor.a = startBorder.a * alpha;
                lineRenderer.startColor = bColor;
                lineRenderer.endColor = bColor;
            }

            RebuildCircleMesh(center, currentWaveRadius);
            yield return null;
        }

        SetVisualsEnabled(false);
        pulseWaveRoutine = null;
    }

    private static void EnsureInstanceExists()
    {
        if (Instance == null)
        {
            Instance = FindAnyObjectByType<HunterRevealAreaVisual>();
            if (Instance == null)
            {
                var go = new GameObject("HunterRevealAreaVisual");
                Instance = go.AddComponent<HunterRevealAreaVisual>();
            }
        }
    }
}
