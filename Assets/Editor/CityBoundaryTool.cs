#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using System.Collections.Generic;

public static class CityBoundaryTool
{
    private const string ReferenciaName = "referencia";

    public static readonly Vector3 DefaultCenter = new Vector3(0.2f, 0.0f, -39.9f);
    public const float DefaultRadius = 74.074f;

    public static bool TryGetReferenceData(out Vector3 center, out float radius, out GameObject refObj)
    {
        center = DefaultCenter;
        radius = DefaultRadius;
        refObj = null;

        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        foreach (var go in allObjects)
        {
            if (go.name.ToLower().Contains(ReferenciaName))
            {
                refObj = go;
                center = go.transform.position;
                radius = Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z) * 0.5f;
                Debug.Log($"[CityBoundary] Objeto de referência encontrado: '{go.name}' em {center}, Raio={radius:F3}m (Diâmetro={radius * 2f:F2}m)");
                return true;
            }
        }

        Debug.Log("[CityBoundary] Objeto de referência não presente; utilizando valores fixados da referência: Centro=(0.20, 0.00, -39.90), Raio=74.07m");
        return false;
    }

    [MenuItem("Tools/Analyze Current NavMesh")]
    public static void AnalyzeCurrentNavMesh()
    {
        TryGetReferenceData(out Vector3 center, out float radius, out _);
        var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();

        int totalVerts = tri.vertices.Length;
        int totalTris = tri.indices.Length / 3;

        int vertsInside = 0;
        int vertsOutside = 0;
        float maxDist = 0f;

        Vector3 minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var v in tri.vertices)
        {
            minBounds = Vector3.Min(minBounds, v);
            maxBounds = Vector3.Max(maxBounds, v);

            float dist = Vector2.Distance(new Vector2(v.x, v.z), new Vector2(center.x, center.z));
            if (dist > maxDist) maxDist = dist;

            if (dist <= radius) vertsInside++;
            else vertsOutside++;
        }

        Debug.Log($"[NavMeshAnalysis] Total Verts: {totalVerts}, Total Tris: {totalTris}");
        Debug.Log($"[NavMeshAnalysis] Min Bounds: {minBounds}, Max Bounds: {maxBounds}");
        Debug.Log($"[NavMeshAnalysis] Verts Dentro R={radius:F1}m: {vertsInside} ({(vertsInside * 100f / Mathf.Max(1, totalVerts)):F1}%), Fora: {vertsOutside} ({(vertsOutside * 100f / Mathf.Max(1, totalVerts)):F1}%), Distância Máxima: {maxDist:F1}m");

        // Detalhar onde estão os vértices de fora
        int countSampled = 0;
        foreach (var v in tri.vertices)
        {
            float dist = Vector2.Distance(new Vector2(v.x, v.z), new Vector2(center.x, center.z));
            if (dist > radius + 2.0f && countSampled < 8)
            {
                countSampled++;
                // Raycast para ver que objeto está exatamente nessa coordenada
                if (Physics.Raycast(new Vector3(v.x, 30f, v.z), Vector3.down, out RaycastHit hit, 50f))
                {
                    Debug.Log($"[NavMeshOutside] Vértice fora em ({v.x:F1}, {v.y:F2}, {v.z:F1}) dist={dist:F1}m - Colidiu com: '{hit.collider.name}' (Parent: {(hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "null")}), Tipo={hit.collider.GetType().Name}");
                }
                else
                {
                    Debug.Log($"[NavMeshOutside] Vértice fora em ({v.x:F1}, {v.y:F2}, {v.z:F1}) dist={dist:F1}m - Nenhum collider abaixo");
                }
            }
        }
    }

    [MenuItem("Tools/Bake City NavMesh")]
    public static void BakeNavMesh()
    {
        // 1. Limpa qualquer NavMesh legado baked na cena para eliminar o azul espalhado no terreno
        UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
        Debug.Log("[CityBoundary] NavMesh legado da cena limpo com sucesso.");

        // 2. Garante que os limites físicos circulares e a contenção estejam atualizados
        CreateBoundariesFromReference();

        // 3. Localiza ou adiciona o componente NavMeshSurface no Enviroment
        GameObject env = GameObject.Find("Enviroment");
        if (env == null)
        {
            Debug.LogError("[CityBoundary] Objeto 'Enviroment' não encontrado na cena!");
            return;
        }

        NavMeshSurface surface = env.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = env.AddComponent<NavMeshSurface>();
        }

        surface.enabled = true;

        TryGetReferenceData(out Vector3 center, out float radius, out _);

        // 4. Configura o volume exato da cidade
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(center.x, 10.0f, center.z);
        surface.size = new Vector3(radius * 2.1f, 30.0f, radius * 2.1f);
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        // 5. Constrói o NavMesh restrito à área circular da cidade
        surface.BuildNavMesh();
        surface.RemoveData();
        surface.AddData();

        // 6. Persiste o NavMeshData como asset do projeto
        if (surface.navMeshData != null)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            string scenePath = activeScene.path;
            string sceneDir = System.IO.Path.GetDirectoryName(scenePath);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            string folderPath = System.IO.Path.Combine(sceneDir, sceneName).Replace('\\', '/');

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(sceneDir.Replace('\\', '/'), sceneName);
            }

            string assetPath = $"{folderPath}/NavMesh-City.asset";
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            }
            else
            {
                EditorUtility.CopySerialized(surface.navMeshData, existing);
                surface.navMeshData = existing;
            }

            EditorUtility.SetDirty(surface);
            AssetDatabase.SaveAssets();
        }

        // 7. Salva a cena ativa
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[CityBoundary] ✅ NavMesh reconfigurado, assado e salvo exclusivamente dentro da área da cidade!");
        AnalyzeCurrentNavMesh();
    }

    [MenuItem("Tools/Create City Invisible Boundaries")]
    public static void CreateBoundariesFromReference()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject env = GameObject.Find("Enviroment");
        if (env == null)
        {
            Debug.LogError("[CityBoundary] Objeto 'Enviroment' não encontrado na cena ativa!");
            return;
        }

        TryGetReferenceData(out Vector3 center, out float radius, out GameObject refObj);

        // Remove o GameObject de referência da cena se ainda existir
        if (refObj != null)
        {
            Undo.DestroyObjectImmediate(refObj);
            Debug.Log("[CityBoundary] Objeto de referência 'referencia ' removido da cena com sucesso.");
        }

        // Remove limites anteriores se existirem
        Transform existing = env.transform.Find("CityBoundaries");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        // Cria container CityBoundaries
        GameObject boundaryRoot = new GameObject("CityBoundaries");
        Undo.RegisterCreatedObjectUndo(boundaryRoot, "Create City Boundaries");
        boundaryRoot.transform.SetParent(env.transform);
        boundaryRoot.transform.localPosition = Vector3.zero;
        boundaryRoot.transform.localRotation = Quaternion.identity;
        boundaryRoot.transform.localScale = Vector3.one;

        // Dimensões dos segmentos de parede circular
        int segments = 64; // 64 paredes tangenciais cobrem a circunferência com precisão suave
        float wallThickness = 80.0f; // Espessura radial gigante de 80m para fora (impede qualquer vazamento de NavMesh fora da cidade)
        float wallHeight = 150.0f; // Altura gigante de 150m (de Y = -20m até Y = +130m) para conter fantasmas e física
        float wallWidth = 16.0f; // Sobreposição tangencial suave entre segmentos vizinhos (zero vãos até R=150m)

        // O centro da parede fica em R + wallThickness/2 para que a face interna da parede fique exatamente no raio R
        float wallRadialCenter = radius + (wallThickness * 0.5f);

        for (int i = 0; i < segments; i++)
        {
            float theta = i * (Mathf.PI * 2f / segments);
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);

            Vector3 wallPos = new Vector3(
                center.x + cos * wallRadialCenter,
                10.0f, // Centralizado na altura navegável do terreno/cidade
                center.z + sin * wallRadialCenter
            );

            // Orientação: forward aponta para fora (radial), right aponta tangencial
            Quaternion wallRot = Quaternion.LookRotation(new Vector3(cos, 0f, sin), Vector3.up);

            GameObject wall = new GameObject($"Boundary_Segment_{i:D2}");
            Undo.RegisterCreatedObjectUndo(wall, $"Create Boundary_Segment_{i}");
            wall.transform.SetParent(boundaryRoot.transform);
            wall.transform.position = wallPos;
            wall.transform.rotation = wallRot;
            wall.transform.localScale = Vector3.one;

            // Colisor físico gigante de 150m de altura
            BoxCollider col = wall.AddComponent<BoxCollider>();
            col.size = new Vector3(wallWidth, wallHeight, wallThickness);
            col.center = new Vector3(0f, 45.0f, 0f); // Centro do colisor físico em Y = 10 + 45 = 55m (de Y = -20m até +130m)
            col.isTrigger = false;

            // Modificador de NavMesh com prioridade máxima Not Walkable em todo o volume externo
            NavMeshModifierVolume modVol = wall.AddComponent<NavMeshModifierVolume>();
            modVol.size = new Vector3(wallWidth * 1.05f, 40.0f, wallThickness);
            modVol.center = Vector3.zero; // Cobre de Y = -10m até Y = +30m (todo o terreno externo)
            modVol.area = 1; // 1 = Not Walkable
        }

        // Teto invisível de contenção física (impede que fantasmas voem infinitamente para cima)
        GameObject ceiling = new GameObject("Boundary_Ceiling");
        Undo.RegisterCreatedObjectUndo(ceiling, "Create Boundary_Ceiling");
        ceiling.transform.SetParent(boundaryRoot.transform);
        ceiling.transform.position = new Vector3(center.x, 40.0f, center.z);
        ceiling.transform.rotation = Quaternion.identity;
        ceiling.transform.localScale = Vector3.one;

        BoxCollider ceilCol = ceiling.AddComponent<BoxCollider>();
        ceilCol.size = new Vector3(radius * 2.2f, 4.0f, radius * 2.2f);
        ceilCol.center = Vector3.zero;
        ceilCol.isTrigger = false;
        var ceilMod = ceiling.AddComponent<NavMeshModifier>();
        ceilMod.ignoreFromBuild = true;

        // Chão invisível de contenção sob a cidade (evita queda no vazio ou atravessar o piso para baixo)
        GameObject floor = new GameObject("Boundary_Floor");
        Undo.RegisterCreatedObjectUndo(floor, "Create Boundary_Floor");
        floor.transform.SetParent(boundaryRoot.transform);
        floor.transform.position = new Vector3(center.x, -5.0f, center.z);
        floor.transform.rotation = Quaternion.identity;
        floor.transform.localScale = Vector3.one;

        BoxCollider floorCol = floor.AddComponent<BoxCollider>();
        floorCol.size = new Vector3(radius * 2.2f, 4.0f, radius * 2.2f);
        floorCol.center = Vector3.zero;
        floorCol.isTrigger = false;
        var floorMod = floor.AddComponent<NavMeshModifier>();
        floorMod.ignoreFromBuild = true;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[CityBoundary] ✅ Novos limites circulares criados com sucesso! Raio={radius:F2}m, EspessuraExterna={wallThickness}m.");

        // Atualiza e cria automaticamente os visuais dos limites (contorno LineworkLite + bolinhas)
        CityBoundaryVisualSetupTool.SetupCityBoundaryVisuals();
    }

    [MenuItem("Tools/Sync Player Prefab Ghost Flight Bounds")]
    public static void SyncPlayerPrefabFlightBounds()
    {
        string prefabPath = "Assets/Prefab/Player.prefab";
        GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefab != null)
        {
            var moveLogic = prefab.GetComponent<PlayerMovementLogic>();
            if (moveLogic != null)
            {
                moveLogic.ghostFlightCenter = new Vector2(DefaultCenter.x, DefaultCenter.z);
                moveLogic.maxGhostFlightRadius = 73.5f;
                moveLogic.minGhostFlightAltitude = -0.2f;
                moveLogic.maxGhostFlightAltitude = 38.0f;
                Debug.Log("[CityBoundaryTool] PlayerMovementLogic configurado no Player.prefab com limites circulares de voo do fantasma!");
            }

            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    [MenuItem("Tools/Inspect City Tasks and Nodes")]
    public static void InspectCityObjects()
    {
        TryGetReferenceData(out Vector3 center, out float radius, out _);

        // Verifica nós de pedestres
        int totalNodes = 0;
        int nodesInside = 0;
        int nodesOutside = 0;
        var networkObj = GameObject.Find("CityPedestrianNetwork");
        if (networkObj != null)
        {
            var nodes = networkObj.GetComponentsInChildren<PedestrianNode>();
            totalNodes = nodes.Length;
            foreach (var n in nodes)
            {
                float dist = Vector2.Distance(new Vector2(n.Position.x, n.Position.z), new Vector2(center.x, center.z));
                if (dist <= radius) nodesInside++;
                else
                {
                    nodesOutside++;
                    Debug.LogWarning($"[CityInspect] Nó fora do limite: '{n.name}' a {dist:F1}m do centro (Raio máximo={radius:F1}m)");
                }
            }
        }
        Debug.Log($"[CityInspect] PedestrianNetwork: Total={totalNodes}, Dentro={nodesInside}, Fora={nodesOutside}");

        // Verifica Tasks
        int totalTasks = 0;
        int tasksInside = 0;
        int tasksOutside = 0;
        int reachableTasks = 0;
        int unreachableTasks = 0;
        var tasks = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Include);
        totalTasks = tasks.Length;
        foreach (var t in tasks)
        {
            Vector3 pos = t.transform.position;
            float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(center.x, center.z));
            if (dist <= radius) tasksInside++;
            else
            {
                tasksOutside++;
                Debug.LogWarning($"[CityInspect] Task fora do limite: '{t.name}' a {dist:F1}m do centro (Raio={radius:F1}m)");
            }

            // Checa alcance no NavMesh
            float interactDist = t.InteractionDistance;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, interactDist + 0.8f, UnityEngine.AI.NavMesh.AllAreas))
            {
                reachableTasks++;
            }
            else
            {
                unreachableTasks++;
                Debug.LogWarning($"[CityInspect] Task possivelmente inalcançável: '{t.name}' em {pos} (sem NavMesh num raio de {interactDist + 0.8f:F1}m)");
            }
        }
        Debug.Log($"[CityInspect] Tasks: Total={totalTasks}, Dentro={tasksInside}, Fora={tasksOutside}, Alcancáveis={reachableTasks}, Inalcançáveis={unreachableTasks}");
    }

    [MenuItem("Tools/Scan City Scene Objects")]
    public static void ScanCitySceneObjects()
    {
        TryGetReferenceData(out Vector3 center, out float radius, out _);
        
        GameObject env = GameObject.Find("Enviroment");
        if (env != null)
        {
            Debug.Log($"[CityScan] Enviroment Pos={env.transform.position}, LossyScale={env.transform.lossyScale}, Rot={env.transform.rotation.eulerAngles}");
            var surface = env.GetComponent<NavMeshSurface>();
            if (surface != null)
            {
                Debug.Log($"[CityScan] NavMeshSurface: center={surface.center}, size={surface.size}, collectObjects={surface.collectObjects}, defaultArea={surface.defaultArea}, layerMask={surface.layerMask.value}, navMeshData={(surface.navMeshData != null ? surface.navMeshData.name : "null")}");
            }
            Debug.Log($"[CityScan] Estrutura de 'Enviroment': {env.transform.childCount} filhos diretos:");
            for (int i = 0; i < env.transform.childCount; i++)
            {
                var child = env.transform.GetChild(i);
                Debug.Log($"[CityScan] Enviroment Filho #{i}: '{child.name}' com {child.childCount} sub-filhos, Pos={child.position}");
            }
        }
    }

    [MenuItem("Tools/Save Scene And Project")]
    public static void SaveSceneAndProject()
    {
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorApplication.ExecuteMenuItem("File/Save Project");
        Debug.Log("[CityBoundary] Cena e Projeto salvos com sucesso.");
    }

    [MenuItem("Tools/Test City Random Spawn")]
    public static void TestSpawn()
    {
        TryGetReferenceData(out Vector3 center, out float radius, out _);
        float safeRadius = radius - 5.0f;

        int successes = 0;
        int trials = 20;

        for (int i = 0; i < trials; i++)
        {
            if (TryFindClearCitySpawnCircular(center, safeRadius, out Vector3 spawnPos))
            {
                successes++;
                float dist = Vector2.Distance(new Vector2(spawnPos.x, spawnPos.z), new Vector2(center.x, center.z));
                if (successes <= 5)
                {
                    Debug.Log($"[CitySpawnTest] Spawn #{successes} válido: ({spawnPos.x:F1}, {spawnPos.y:F2}, {spawnPos.z:F1}) - dist do centro={dist:F1}m (máx {safeRadius:F1}m)");
                }
            }
        }
        Debug.Log($"[CitySpawnTest] Resultado dos testes circulares: {successes}/{trials} spawns bem-sucedidos.");
    }

    public static bool TryFindClearCitySpawnCircular(Vector3 center, float maxRadius, out Vector3 position)
    {
        position = Vector3.zero;

        for (int attempt = 0; attempt < 60; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Mathf.Sqrt(Random.Range(0f, 1f)) * maxRadius;
            float rx = center.x + Mathf.Cos(angle) * dist;
            float rz = center.z + Mathf.Sin(angle) * dist;

            Vector3 rayOrigin = new Vector3(rx, 35f, rz);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 45f))
            {
                if (hit.point.y < -0.3f || hit.point.y > 1.2f)
                    continue;

                string hitName = hit.collider.gameObject.name.ToLower();
                string hitParent = hit.collider.transform.parent != null ? hit.collider.transform.parent.name.ToLower() : "";
                if (hitName.Contains("car") || hitParent.Contains("vehicle") || hitName.Contains("building") || hitParent.Contains("building") || hitName.Contains("tree"))
                    continue;

                Vector3 feetPos = hit.point + Vector3.up * 0.05f;
                Vector3 capBottom = feetPos + Vector3.up * 0.5f;
                Vector3 capTop = feetPos + Vector3.up * 1.5f;

                Collider[] overlaps = Physics.OverlapCapsule(capBottom, capTop, 0.45f);
                bool blocked = false;

                foreach (var col in overlaps)
                {
                    if (col == hit.collider) continue;
                    if (col is TerrainCollider) continue;
                    if (col.isTrigger) continue;

                    string colName = col.gameObject.name.ToLower();

                    if (col.CompareTag("Street") || col.CompareTag("Grass") || colName.Contains("road") || colName.Contains("pavement") || colName.Contains("roundabout") || colName.Contains("parking"))
                        continue;

                    blocked = true;
                    break;
                }

                if (!blocked)
                {
                    position = feetPos;
                    return true;
                }
            }
        }

        return false;
    }
}
#endif
