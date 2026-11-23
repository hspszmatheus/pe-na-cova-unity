using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controla o contorno visual e a barreira de bolinhas (LineworkLite + Polka-Dot Shader)
/// em TODA a extensão dos limites do cenário (CityBoundaries), incluindo:
/// 1. As paredes verticais cilíndricas ao redor de toda a cidade (de Y = -1m até Y = 39m).
/// 2. O teto / domo no céu (em Y = 39m, abaixo do limite de voo de 40m).
/// 
/// Regras atendidas:
/// - Bolinhas menores, refinadas e elegantes mapeadas em metros no shader.
/// - Cor cinza prateada padrão idêntica aos contornos de objetos Task (TaskOutline).
/// - Segue por todo o boundary (paredes e teto do céu para voo de fantasma).
/// - Proximidade 3D esférica real (surge suavemente ao se aproximar de paredes ou do céu).
/// - ZTest LEqual: Bloqueado por prédios e paredes (não visível através de obstáculos).
/// - Otimização de ponta: Renderers distantes dormem desativados (0 draws).
/// </summary>
[ExecuteAlways]
public class CityBoundaryVisualController : MonoBehaviour
{
    public const int CityBoundaryRenderingLayerIndex = 5;
    public const uint CityBoundaryRenderingLayerMask = 1u << CityBoundaryRenderingLayerIndex; // Bit 5 = 32u

    private static readonly int BoundaryPlayerPosId = Shader.PropertyToID("_BoundaryLocalPlayerPos");
    private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");

    [Header("Boundary Dimensions (3D Cylinder + Ceiling)")]
    [Tooltip("Centro geométrico do limite circular da cidade.")]
    [SerializeField] private Vector3 boundaryCenter = new Vector3(0.2f, 0.0f, -39.9f);

    [Tooltip("Raio do limite circular da cidade (padrão 74.074m).")]
    [SerializeField] private float boundaryRadius = 74.074f;

    [Tooltip("Altitude base da barreira na parte inferior (abaixo do chão).")]
    [SerializeField] private float barrierBaseY = -1.0f;

    [Tooltip("Altitude do teto no céu (onde o fantasma colide).")]
    [SerializeField] private float ceilingAltitude = 39.0f;

    [Header("Proximity Fade (3D Sphere)")]
    [Tooltip("Distância 3D da barreira/teto em que o contorno começa a surgir suavemente.")]
    [SerializeField] private float startFadeDistance = 15.0f;

    [Tooltip("Distância 3D da barreira/teto em que o contorno atinge 100% de opacidade.")]
    [SerializeField] private float fullAlphaDistance = 3.5f;

    [Tooltip("Raio de ativação de segmentos e blocos de teto próximos ao jogador (metros).")]
    [SerializeField] private float segmentActivationRange = 26.0f;

    [Header("Material")]
    [SerializeField] private Material barrierMaterial;

    [Header("Debug")]
    [SerializeField] private bool previewInEditor = true;

    [System.Serializable]
    public class SegmentVisualData
    {
        public GameObject gameObject;
        public MeshRenderer renderer;
        public Vector3 boundsCenter;
        public Vector3 boundsExtents;
        public bool isCeiling;
    }

    [SerializeField, HideInInspector]
    private List<SegmentVisualData> cachedSegments = new List<SegmentVisualData>();

    private MaterialPropertyBlock propBlock;
    private float lastUpdateTime = 0f;
    private const float UpdateInterval = 0.02f; // ~50Hz
    private bool isSuppressed = false;

    public Vector3 BoundaryCenter => boundaryCenter;
    public float BoundaryRadius => boundaryRadius;
    public float BarrierBaseY => barrierBaseY;
    public float CeilingAltitude => ceilingAltitude;

    /// <summary>
    /// Suprime ou restaura a exibição visual das bolinhas/pontinhos do boundary (ex: durante cutscenes cinematográficas).
    /// </summary>
    public void SetVisualsSuppressed(bool suppressed)
    {
        isSuppressed = suppressed;
        if (suppressed)
        {
            SetAllRenderersEnabled(false);
        }
    }

    private void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        CacheSegmentReferences();
    }

    private void OnEnable()
    {
        if (propBlock == null) propBlock = new MaterialPropertyBlock();
        CacheSegmentReferences();
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            if (isSuppressed || AirplaneCrashCutsceneController.IsAnyCutsceneRunning)
            {
                SetAllRenderersEnabled(false);
                return;
            }

            if (Time.time - lastUpdateTime < UpdateInterval) return;
            lastUpdateTime = Time.time;
            UpdateProximity(GetPlayerPosition());
        }
        else
        {
#if UNITY_EDITOR
            if (previewInEditor)
            {
                Vector3 editorCamPos = GetEditorCameraPosition();
                UpdateProximity(editorCamPos);
            }
#endif
        }
    }

    /// <summary>
    /// Posição mundial 3D do jogador local (suporta fantasma em voo e sobrevivente em terra).
    /// </summary>
    private Vector3 GetPlayerPosition()
    {
        if (AirplaneCrashCutsceneController.IsAnyCutsceneRunning)
        {
            return boundaryCenter;
        }

        if (PlayerController.LocalInstance != null)
        {
            return PlayerController.LocalInstance.transform.position;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            return mainCam.transform.position;
        }

        return boundaryCenter;
    }

#if UNITY_EDITOR
    private Vector3 GetEditorCameraPosition()
    {
        if (UnityEditor.SceneView.lastActiveSceneView != null &&
            UnityEditor.SceneView.lastActiveSceneView.camera != null)
        {
            return UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
        }
        return boundaryCenter;
    }
#endif

    /// <summary>
    /// Atualiza visibilidade, shaders e LineworkLite renderingLayerMask com base na proximidade 3D.
    /// </summary>
    public void UpdateProximity(Vector3 playerPos)
    {
        if (cachedSegments == null || cachedSegments.Count == 0)
        {
            CacheSegmentReferences();
            if (cachedSegments == null || cachedSegments.Count == 0) return;
        }

        // Informa ao shader a posição 3D do jogador para a esfera de luz/fade de proximidade
        Shader.SetGlobalVector(BoundaryPlayerPosId, new Vector4(playerPos.x, playerPos.y, playerPos.z, 1.0f));

        // 1. Distância radial às paredes verticais
        Vector2 playerXZ = new Vector2(playerPos.x, playerPos.z);
        Vector2 centerXZ = new Vector2(boundaryCenter.x, boundaryCenter.z);
        float distFromCenter = Vector2.Distance(playerXZ, centerXZ);
        float distToWall = Mathf.Abs(distFromCenter - boundaryRadius);

        // 2. Distância vertical ao teto no céu
        float distToCeiling = Mathf.Abs(playerPos.y - ceilingAltitude);

        // 3. Menor distância a qualquer parte do limite
        float closestLimitDist = Mathf.Min(distToWall, distToCeiling);

        // Se o player estiver longe de qualquer fronteira (no meio da cidade a baixa altitude):
        // Desativa tudo para 0 draw calls na GPU!
        if (closestLimitDist > startFadeDistance + 5.0f)
        {
            SetAllRenderersEnabled(false);
            return;
        }

        float range = Mathf.Max(0.01f, startFadeDistance - fullAlphaDistance);

        // Ativa e modula apenas os segmentos ou blocos de teto próximos à posição 3D do player
        for (int i = 0; i < cachedSegments.Count; i++)
        {
            var seg = cachedSegments[i];
            if (seg == null || seg.renderer == null) continue;

            // Calcula distância 3D ao bounding box do segmento
            float distToSeg = DistanceToBox(playerPos, seg.boundsCenter, seg.boundsExtents);

            if (distToSeg <= segmentActivationRange)
            {
                if (!seg.renderer.enabled) seg.renderer.enabled = true;

                // Garante a camada de contorno do LineworkLite (bit 5 = 32u)
                if ((seg.renderer.renderingLayerMask & CityBoundaryRenderingLayerMask) == 0)
                {
                    seg.renderer.renderingLayerMask |= CityBoundaryRenderingLayerMask;
                }

                // Calcula alpha proporcional à aproximação daquele ponto
                float segAlpha = 1.0f - Mathf.Clamp01((distToSeg - fullAlphaDistance) / range);

                seg.renderer.GetPropertyBlock(propBlock);
                propBlock.SetFloat(AlphaMultiplierId, Mathf.Max(0.1f, segAlpha));
                seg.renderer.SetPropertyBlock(propBlock);
            }
            else
            {
                if (seg.renderer.enabled)
                {
                    seg.renderer.enabled = false;
                }
            }
        }
    }

    private static float DistanceToBox(Vector3 point, Vector3 center, Vector3 extents)
    {
        Vector3 d = Vector3.Max(Vector3.zero, new Vector3(
            Mathf.Abs(point.x - center.x) - extents.x,
            Mathf.Abs(point.y - center.y) - extents.y,
            Mathf.Abs(point.z - center.z) - extents.z
        ));
        return d.magnitude;
    }

    private void SetAllRenderersEnabled(bool enable)
    {
        for (int i = 0; i < cachedSegments.Count; i++)
        {
            var seg = cachedSegments[i];
            if (seg != null && seg.renderer != null && seg.renderer.enabled != enable)
            {
                seg.renderer.enabled = enable;
            }
        }
    }

    /// <summary>
    /// Encontra e armazena em cache todos os segmentos visuais (paredes e teto).
    /// </summary>
    public void CacheSegmentReferences()
    {
        cachedSegments.Clear();
        var allRenderers = GetComponentsInChildren<MeshRenderer>(true);

        foreach (var r in allRenderers)
        {
            if (r.gameObject.name.Contains("Visual_Barrier") || r.gameObject.name.Contains("Visual_Ceiling"))
            {
                Bounds b = r.bounds;
                cachedSegments.Add(new SegmentVisualData
                {
                    gameObject = r.gameObject,
                    renderer = r,
                    boundsCenter = b.center,
                    boundsExtents = b.extents,
                    isCeiling = r.gameObject.name.Contains("Ceiling")
                });

                r.renderingLayerMask |= CityBoundaryRenderingLayerMask;
            }
        }
    }

    /// <summary>
    /// Constrói a barreira 3D completa da cidade:
    /// 1. Paredes verticais de altura total (de Y=-1m até Y=39m) em 64 segmentos.
    /// 2. Teto do céu em grade de painéis cobrindo o círculo em Y=39m.
    /// </summary>
    public void BuildAllVisualBoundaries(Material mat)
    {
        barrierMaterial = mat;

        // 1. Limpa visuais anteriores em paredes e teto
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            for (int c = child.childCount - 1; c >= 0; c--)
            {
                Transform sub = child.GetChild(c);
                if (sub.name.Contains("Visual_Barrier") || sub.name.Contains("Visual_Ceiling"))
                {
                    DestroyImmediate(sub.gameObject);
                }
            }
        }

        Transform ceilingObj = transform.Find("Boundary_Ceiling");
        if (ceilingObj != null)
        {
            for (int c = ceilingObj.childCount - 1; c >= 0; c--)
            {
                Transform sub = ceilingObj.GetChild(c);
                if (sub.name.Contains("Visual_"))
                {
                    DestroyImmediate(sub.gameObject);
                }
            }
        }

        cachedSegments.Clear();

        // 2. Constrói as paredes verticais de altura total (64 segmentos, altura 40m)
        BuildFullHeightWallSegments();

        // 3. Constrói o teto do céu cobrindo o círculo da cidade
        BuildCeilingTiles();

        Debug.Log($"[CityBoundaryVisualController] ✅ Criados {cachedSegments.Count} painéis 3D (paredes de 40m + teto no céu) com contorno cinza e bolinhas finas!");
    }

    private const float DotGridSpacingMeters = 0.45f;

    private void BuildFullHeightWallSegments()
    {
        int segments = 64;
        float angleStep = (Mathf.PI * 2f) / segments;
        // Largura cordal tangencial exata para encontro borda-a-borda sem sobreposição nem frestas
        float panelWidth = 2f * boundaryRadius * Mathf.Tan(Mathf.PI / segments);
        float wallTotalHeight = ceilingAltitude - barrierBaseY; // ex: 39 - (-1) = 40m
        float wallCenterY = barrierBaseY + (wallTotalHeight * 0.5f);

        // Múltiplo inteiro do espaçamento de bolinhas para que todas as 64 emendas verticais caiam exatamente entre colunas
        int dotColsPerPanel = Mathf.Max(1, Mathf.RoundToInt(panelWidth / DotGridSpacingMeters));
        float uvWidth = dotColsPerPanel * DotGridSpacingMeters;
        int dotRowsPerWall = Mathf.Max(1, Mathf.RoundToInt(wallTotalHeight / DotGridSpacingMeters));
        float uvHeight = dotRowsPerWall * DotGridSpacingMeters;

        Mesh sharedWallMesh = CreateMetersPanelMesh(panelWidth, wallTotalHeight, uvWidth, uvHeight);
        sharedWallMesh.name = $"BoundaryWall_Shared_{panelWidth:F2}x{wallTotalHeight:F1}m";

        for (int i = 0; i < segments; i++)
        {
            string segName = $"Boundary_Segment_{i:D2}";
            Transform segTrans = transform.Find(segName);
            if (segTrans == null) continue;

            float theta = i * angleStep;
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);

            Vector3 barrierPos = new Vector3(
                boundaryCenter.x + cos * boundaryRadius,
                wallCenterY,
                boundaryCenter.z + sin * boundaryRadius
            );

            Vector3 inwardDir = new Vector3(-cos, 0f, -sin);
            Quaternion barrierRot = Quaternion.LookRotation(inwardDir, Vector3.up);

            GameObject visualObj = new GameObject("Visual_Barrier");
            visualObj.transform.SetParent(segTrans, true);
            visualObj.transform.position = barrierPos;
            visualObj.transform.rotation = barrierRot;
            visualObj.transform.localScale = Vector3.one;

            MeshFilter mf = visualObj.AddComponent<MeshFilter>();
            mf.sharedMesh = sharedWallMesh;

            MeshRenderer mr = visualObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = barrierMaterial;
            mr.renderingLayerMask = CityBoundaryRenderingLayerMask; // Bit 5 = 32u
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.enabled = false;

            cachedSegments.Add(new SegmentVisualData
            {
                gameObject = visualObj,
                renderer = mr,
                boundsCenter = barrierPos,
                boundsExtents = new Vector3(panelWidth * 0.5f, wallTotalHeight * 0.5f, 1.5f),
                isCeiling = false
            });
        }
    }

    private void BuildCeilingTiles()
    {
        Transform ceilingRoot = transform.Find("Boundary_Ceiling");
        if (ceilingRoot == null)
        {
            // Cria container para o teto se não existir
            GameObject cObj = new GameObject("Boundary_Ceiling");
            cObj.transform.SetParent(transform);
            cObj.transform.position = new Vector3(boundaryCenter.x, ceilingAltitude, boundaryCenter.z);
            ceilingRoot = cObj.transform;
        }

        // Grade 8x8 com tamanho exato múltiplo de DotGridSpacingMeters (42 * 0.45m = 18.90m, total 151.20m) sem sobreposição
        int gridSize = 8;
        int dotsPerTile = Mathf.CeilToInt((boundaryRadius * 2f) / (gridSize * DotGridSpacingMeters));
        float tileSize = dotsPerTile * DotGridSpacingMeters;
        float totalSpan = tileSize * gridSize;
        float halfSpan = totalSpan * 0.5f;

        Mesh sharedCeilingMesh = CreateMetersPanelMesh(tileSize, tileSize, tileSize, tileSize);
        sharedCeilingMesh.name = $"BoundaryCeiling_Shared_{tileSize:F2}x{tileSize:F2}m";

        for (int gx = 0; gx < gridSize; gx++)
        {
            for (int gz = 0; gz < gridSize; gz++)
            {
                float localX = -halfSpan + (gx + 0.5f) * tileSize;
                float localZ = -halfSpan + (gz + 0.5f) * tileSize;

                float distFromCenter = Mathf.Sqrt(localX * localX + localZ * localZ);

                // Descarta azulejos nos 4 cantos externos ao círculo de raio 74m
                if (distFromCenter > boundaryRadius + (tileSize * 0.55f))
                    continue;

                Vector3 worldTilePos = new Vector3(
                    boundaryCenter.x + localX,
                    ceilingAltitude,
                    boundaryCenter.z + localZ
                );

                GameObject ceilingTile = new GameObject($"Visual_Ceiling_{gx}_{gz}");
                ceilingTile.transform.SetParent(ceilingRoot, true);
                ceilingTile.transform.position = worldTilePos;
                ceilingTile.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Face apontada para baixo (olhando do chão pro céu)
                ceilingTile.transform.localScale = Vector3.one;

                MeshFilter mf = ceilingTile.AddComponent<MeshFilter>();
                mf.sharedMesh = sharedCeilingMesh;

                MeshRenderer mr = ceilingTile.AddComponent<MeshRenderer>();
                mr.sharedMaterial = barrierMaterial;
                mr.renderingLayerMask = CityBoundaryRenderingLayerMask; // Bit 5 = 32u
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                mr.enabled = false;

                cachedSegments.Add(new SegmentVisualData
                {
                    gameObject = ceilingTile,
                    renderer = mr,
                    boundsCenter = worldTilePos,
                    boundsExtents = new Vector3(tileSize * 0.5f, 1.5f, tileSize * 0.5f),
                    isCeiling = true
                });
            }
        }
    }

    /// <summary>
    /// Gera um painel plano com UVs mapeadas diretamente em METROS mundiais,
    /// garantindo que as bolinhas tenham exatamente o mesmo tamanho e espaçamento em qualquer objeto.
    /// </summary>
    private static Mesh CreateMetersPanelMesh(float widthInMeters, float heightInMeters, float uvWidthInMeters, float uvHeightInMeters)
    {
        Mesh mesh = new Mesh();
        mesh.name = $"BoundaryPanel_{widthInMeters:F1}x{heightInMeters:F1}m";

        float halfW = widthInMeters * 0.5f;
        float halfH = heightInMeters * 0.5f;

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-halfW, -halfH, 0f),
            new Vector3( halfW, -halfH, 0f),
            new Vector3( halfW,  halfH, 0f),
            new Vector3(-halfW,  halfH, 0f)
        };

        // UV em metros alinhada ao espaçamento das bolinhas para grade uniforme
        Vector2[] uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(uvWidthInMeters, 0f),
            new Vector2(uvWidthInMeters, uvHeightInMeters),
            new Vector2(0f, uvHeightInMeters)
        };

        Vector3[] normals = new Vector3[]
        {
            Vector3.back,
            Vector3.back,
            Vector3.back,
            Vector3.back
        };

        Vector4[] tangents = new Vector4[]
        {
            new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f)
        };

        int[] triangles = new int[]
        {
            0, 2, 1,
            0, 3, 2,
            0, 1, 2,
            0, 2, 3
        };

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    private void OnDrawGizmosSelected()
    {
        // 1. Círculo na base
        Gizmos.color = new Color(0.72f, 0.72f, 0.78f, 0.8f);
        int steps = 64;
        Vector3 prevBase = boundaryCenter + new Vector3(boundaryRadius, barrierBaseY, 0f);
        Vector3 prevTop = boundaryCenter + new Vector3(boundaryRadius, ceilingAltitude, 0f);

        for (int i = 1; i <= steps; i++)
        {
            float theta = i * (Mathf.PI * 2f / steps);
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);

            Vector3 nextBase = boundaryCenter + new Vector3(cos * boundaryRadius, barrierBaseY, sin * boundaryRadius);
            Vector3 nextTop = boundaryCenter + new Vector3(cos * boundaryRadius, ceilingAltitude, sin * boundaryRadius);

            Gizmos.DrawLine(prevBase, nextBase);
            Gizmos.DrawLine(prevTop, nextTop);

            if (i % 8 == 0)
            {
                Gizmos.DrawLine(nextBase, nextTop);
            }

            prevBase = nextBase;
            prevTop = nextTop;
        }
    }
}
