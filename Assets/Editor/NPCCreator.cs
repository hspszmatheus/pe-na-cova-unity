#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public static class NPCCreator
{
    [MenuItem("Tools/Generate NPC Prefab From Player")]
    public static void GenerateNPCPrefab()
    {
        string playerPrefabPath = "Assets/Prefab/Player.prefab";
        string npcPrefabPath = "Assets/Prefab/NPC.prefab";

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError($"[NPCCreator] Prefab do Player não encontrado em: {playerPrefabPath}");
            return;
        }

        // Instancia uma cópia temporária na cena
        GameObject npcInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        npcInstance.name = "NPC";

        // Desconecta o link do prefab original para que possamos salvar como um novo prefab independente
        PrefabUtility.UnpackPrefabInstance(npcInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // 1. Remove câmeras filhas que são específicas do jogador local
        Transform camChild = npcInstance.transform.Find("Camera");
        if (camChild != null) Object.DestroyImmediate(camChild.gameObject);

        Transform localCamChild = npcInstance.transform.Find("LocalPlayerCamera");
        if (localCamChild != null) Object.DestroyImmediate(localCamChild.gameObject);

        Transform freelookCamChild = npcInstance.transform.Find("FreeLook Camera");
        if (freelookCamChild != null) Object.DestroyImmediate(freelookCamChild.gameObject);

        // 2. Remove componentes específicos do controle do jogador humano
        PlayerController playerController = npcInstance.GetComponent<PlayerController>();
        if (playerController != null) Object.DestroyImmediate(playerController);

        PlayerMovementLogic movementLogic = npcInstance.GetComponent<PlayerMovementLogic>();
        if (movementLogic != null) Object.DestroyImmediate(movementLogic);

        PlayerCameraLogic cameraLogic = npcInstance.GetComponent<PlayerCameraLogic>();
        if (cameraLogic != null) Object.DestroyImmediate(cameraLogic);

        PlayerCameraSetup cameraSetup = npcInstance.GetComponent<PlayerCameraSetup>();
        if (cameraSetup != null) Object.DestroyImmediate(cameraSetup);

        PlayerInput playerInput = npcInstance.GetComponent<PlayerInput>();
        if (playerInput != null) Object.DestroyImmediate(playerInput);

        // Remove CharacterController para evitar conflito com NavMeshAgent
        CharacterController charController = npcInstance.GetComponent<CharacterController>();
        if (charController != null) Object.DestroyImmediate(charController);

        // 3. Adiciona CapsuleCollider com dimensões idênticas ao jogador
        CapsuleCollider collider = npcInstance.GetComponent<CapsuleCollider>();
        if (collider == null) collider = npcInstance.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 0.9f, 0f);
        collider.radius = 0.45f;
        collider.height = 1.8f;

        // Obtém a velocidade exata de caminhada do Player
        PlayerMovementLogic playerMove = playerPrefab.GetComponent<PlayerMovementLogic>();
        float targetWalkSpeed = (playerMove != null && playerMove.walkSpeed > 0.1f) ? playerMove.walkSpeed : 1.5f;

        // 4. Adiciona NavMeshAgent
        NavMeshAgent agent = npcInstance.GetComponent<NavMeshAgent>();
        if (agent == null) agent = npcInstance.AddComponent<NavMeshAgent>();
        agent.speed = targetWalkSpeed;
        agent.angularSpeed = 240f;
        agent.acceleration = 8f;
        agent.stoppingDistance = 0.5f;
        agent.radius = 0.45f;
        agent.height = 1.8f;
        agent.baseOffset = 0f;
        agent.updateRotation = false; // Rotação suave controlada manualmente idêntica ao Player
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        // 5. Adiciona NPCController
        NPCController npcController = npcInstance.GetComponent<NPCController>();
        if (npcController == null) npcController = npcInstance.AddComponent<NPCController>();
        npcController.walkSpeed = targetWalkSpeed;

        // 6. Salva como novo Prefab independente
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(npcInstance, npcPrefabPath);
        Object.DestroyImmediate(npcInstance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[NPCCreator] Prefab NPC criado com sucesso em: {npcPrefabPath}!");

        // 7. Registra no DefaultNetworkPrefabs
        RegisterInNetworkPrefabs(savedPrefab);
    }

    private static void RegisterInNetworkPrefabs(GameObject prefab)
    {
        string networkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        NetworkPrefabsList prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(networkPrefabsPath);
        if (prefabsList == null)
        {
            Debug.LogWarning("[NPCCreator] DefaultNetworkPrefabs.asset não encontrado.");
            return;
        }

        // Verifica se já está na lista
        bool alreadyContains = false;
        foreach (var item in prefabsList.PrefabList)
        {
            if (item.Prefab == prefab)
            {
                alreadyContains = true;
                break;
            }
        }

        if (!alreadyContains)
        {
            NetworkPrefab newEntry = new NetworkPrefab();
            newEntry.Prefab = prefab;
            prefabsList.Add(newEntry);
            EditorUtility.SetDirty(prefabsList);
            AssetDatabase.SaveAssets();
            Debug.Log($"[NPCCreator] NPC.prefab registrado com sucesso no NetworkPrefabsList!");
        }
        else
        {
            Debug.Log($"[NPCCreator] NPC.prefab já constava no NetworkPrefabsList.");
        }
    }

    [MenuItem("Tools/Test Spawn 5 NPCs in Scene")]
    public static void TestSpawnNPCs()
    {
        string npcPrefabPath = "Assets/Prefab/NPC.prefab";
        GameObject npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(npcPrefabPath);
        if (npcPrefab == null)
        {
            Debug.LogError("[NPCCreator] NPC.prefab não encontrado.");
            return;
        }

        GameObject parent = GameObject.Find("Test_NPC_Preview");
        if (parent != null) Object.DestroyImmediate(parent);
        parent = new GameObject("Test_NPC_Preview");

        int spawned = 0;
        for (int i = 0; i < 5; i++)
        {
            Vector3 candidate = new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-90f, 10f));
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, NavMesh.AllAreas))
            {
                GameObject npc = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab);
                npc.name = $"Test_NPC_{i + 1}";
                npc.transform.position = hit.position;
                npc.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                npc.transform.SetParent(parent.transform);

                // Ativa um dos 10 personagens aleatoriamente para visualização no Editor
                PlayerCharacterRandomizer randomizer = npc.GetComponent<PlayerCharacterRandomizer>();
                if (randomizer != null && randomizer.characters != null && randomizer.characters.Length > 0)
                {
                    int chosen = Random.Range(0, randomizer.characters.Length);
                    for (int c = 0; c < randomizer.characters.Length; c++)
                    {
                        if (randomizer.characters[c] != null)
                            randomizer.characters[c].SetActive(c == chosen);
                    }
                }

                spawned++;
            }
        }

        Debug.Log($"[NPCCreator] {spawned} NPCs de teste criados em 'Test_NPC_Preview' para conferência visual no Editor!");
    }

    [MenuItem("Tools/Clear Test NPCs from Scene")]
    public static void ClearTestNPCs()
    {
        GameObject parent = GameObject.Find("Test_NPC_Preview");
        if (parent != null)
        {
            Object.DestroyImmediate(parent);
            Debug.Log("[NPCCreator] Objeto 'Test_NPC_Preview' removido da cena com sucesso.");
        }
        else
        {
            Debug.Log("[NPCCreator] Nenhum 'Test_NPC_Preview' encontrado para remover.");
        }
    }

    [MenuItem("Tools/Generate City Pedestrian Network")]
    public static void GeneratePedestrianNetwork()
    {
        // 1. Encontra ou cria o objeto CityPedestrianNetwork na cena ativa
        GameObject networkObj = GameObject.Find("CityPedestrianNetwork");
        if (networkObj != null) Object.DestroyImmediate(networkObj);
        networkObj = new GameObject("CityPedestrianNetwork");

        CityPedestrianNetwork network = networkObj.AddComponent<CityPedestrianNetwork>();

        // 2. Determina o centro e raio a partir do objeto de referência na cena
        Vector2 center = new Vector2(0.2f, -39.9f);
        float radius = 74.074f;

        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        foreach (var go in allObjects)
        {
            if (go.name.ToLower().Contains("referencia"))
            {
                center = new Vector2(go.transform.position.x, go.transform.position.z);
                radius = Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z) * 0.5f;
                Debug.Log($"[NPCCreator] Usando objeto de referência '{go.name}' para malha de pedestres: Centro=({center.x:F2}, {center.y:F2}), Raio={radius:F2}m");
                break;
            }
        }

        // Margem de segurança de 4.5m dentro das paredes do limite circular (raio máx ~69.5m)
        float maxNodeRadius = radius - 4.5f;

        List<Vector3> candidatePositions = new List<Vector3>();

        // 3. Amostragem explícita de Pontos de Interesse (Tasks, Posto, Prédios, Outdoors, Árvores)
        // 3.1 Tasks do mapa
        var tasks = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Include);
        foreach (var task in tasks)
        {
            if (task == null) continue;
            Vector3 tPos = task.transform.position;
            if (Vector2.Distance(new Vector2(tPos.x, tPos.z), center) > maxNodeRadius) continue;

            if (NavMesh.SamplePosition(tPos, out NavMeshHit tHit, 3.5f, NavMesh.AllAreas))
            {
                candidatePositions.Add(tHit.position);
            }
        }

        // 3.2 Props e Edifícios do cenário
        foreach (var go in allObjects)
        {
            if (go == null || !go.activeInHierarchy) continue;
            Vector3 goPos = go.transform.position;
            if (Vector2.Distance(new Vector2(goPos.x, goPos.z), center) > maxNodeRadius) continue;

            string goName = go.name.ToLower();
            if (goName.Contains("bilboard") || goName.Contains("streetsign") || goName.Contains("gas_pump") || goName.Contains("gas_station"))
            {
                if (NavMesh.SamplePosition(goPos, out NavMeshHit propHit, 4.0f, NavMesh.AllAreas))
                {
                    candidatePositions.Add(propHit.position);
                }
            }
            else if (goName.StartsWith("building_"))
            {
                // Amostra 4 cantos ao redor do edifício
                Vector3[] offsets = new Vector3[]
                {
                    new Vector3(4.5f, 0, 0),
                    new Vector3(-4.5f, 0, 0),
                    new Vector3(0, 0, 4.5f),
                    new Vector3(0, 0, -4.5f)
                };
                foreach (var off in offsets)
                {
                    Vector3 samplePt = goPos + off;
                    if (Vector2.Distance(new Vector2(samplePt.x, samplePt.z), center) <= maxNodeRadius)
                    {
                        if (NavMesh.SamplePosition(samplePt, out NavMeshHit bHit, 3.5f, NavMesh.AllAreas))
                        {
                            candidatePositions.Add(bHit.position);
                        }
                    }
                }
            }
        }

        // 3.3 Grade densa e refinada pela cidade (passo de 5.2m ao invés de 12m)
        float step = 5.2f;
        float minX = center.x - maxNodeRadius;
        float maxX = center.x + maxNodeRadius;
        float minZ = center.y - maxNodeRadius;
        float maxZ = center.y + maxNodeRadius;

        for (float x = minX; x <= maxX; x += step)
        {
            for (float z = minZ; z <= maxZ; z += step)
            {
                Vector2 pt = new Vector2(x, z);
                if (Vector2.Distance(pt, center) > maxNodeRadius)
                    continue;

                Vector3 candidate = new Vector3(x, 0f, z);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
                {
                    candidatePositions.Add(hit.position);
                }
            }
        }

        // 4. Filtragem, Desobstrução física e Criação dos Nós
        List<PedestrianNode> createdNodes = new List<PedestrianNode>();
        Dictionary<PedestrianNodeType, int> typeCounts = new Dictionary<PedestrianNodeType, int>();

        const float minNodeSpacing = 4.2f;

        foreach (var rawPos in candidatePositions)
        {
            // Validação de altitude (nível da rua, não telhado)
            if (rawPos.y < -0.3f || rawPos.y > 1.3f) continue;

            // Restrição rigorosa ao raio circular
            if (Vector2.Distance(new Vector2(rawPos.x, rawPos.z), center) > maxNodeRadius)
                continue;

            // Verificação de espaçamento com nós já criados
            bool tooClose = false;
            foreach (var existing in createdNodes)
            {
                if (Vector3.Distance(existing.Position, rawPos) < minNodeSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Verificação de desobstrução física (ignora colisão com solo/rua, mas checa paredes/carros/árvores)
            Vector3 capBottom = rawPos + Vector3.up * 0.45f;
            Vector3 capTop = rawPos + Vector3.up * 1.45f;
            Collider[] overlaps = Physics.OverlapCapsule(capBottom, capTop, 0.40f);
            bool blocked = false;
            foreach (var col in overlaps)
            {
                if (col is TerrainCollider || col.isTrigger) continue;
                string colName = col.gameObject.name.ToLower();
                if (col.CompareTag("Street") || col.CompareTag("Grass") || colName.Contains("road") || colName.Contains("pavement") || colName.Contains("roundabout"))
                    continue;

                blocked = true;
                break;
            }
            if (blocked) continue;

            // 5. Classificação Semântica Contextual respeitando objetos do cenário
            ClassifyNodeContext(rawPos, out PedestrianNodeType nodeType, out string locName, out Vector3 lookDir, out bool isPause, out float pauseChance, out float minPause, out float maxPause);

            GameObject nodeObj = new GameObject($"PedestrianNode_{createdNodes.Count + 1}_{nodeType}");
            nodeObj.transform.position = rawPos;
            nodeObj.transform.SetParent(networkObj.transform);

            PedestrianNode node = nodeObj.AddComponent<PedestrianNode>();
            node.nodeType = nodeType;
            node.locationName = locName;
            node.preferredLookDirection = lookDir;
            node.isPausePoint = isPause;
            node.pauseChance = pauseChance;
            node.minPauseDuration = minPause;
            node.maxPauseDuration = maxPause;

            createdNodes.Add(node);

            if (!typeCounts.ContainsKey(nodeType)) typeCounts[nodeType] = 0;
            typeCounts[nodeType]++;
        }

        // 6. Conexão bidirecional inteligente pelo NavMesh
        NavMeshPath path = new NavMeshPath();
        for (int i = 0; i < createdNodes.Count; i++)
        {
            var nodeA = createdNodes[i];
            for (int j = i + 1; j < createdNodes.Count; j++)
            {
                var nodeB = createdNodes[j];
                float dist = Vector3.Distance(nodeA.Position, nodeB.Position);

                // Conecta nós com distância navegável entre 3.8m e 15.0m
                if (dist >= 3.8f && dist <= 15.0f)
                {
                    if (nodeA.neighbors.Count >= 5 && nodeB.neighbors.Count >= 5) continue;

                    if (NavMesh.CalculatePath(nodeA.Position, nodeB.Position, NavMesh.AllAreas, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete && path.corners.Length <= 4)
                        {
                            bool pathInsideCircle = true;
                            foreach (var corner in path.corners)
                            {
                                if (Vector2.Distance(new Vector2(corner.x, corner.z), center) > radius - 1.5f)
                                {
                                    pathInsideCircle = false;
                                    break;
                                }
                            }

                            if (pathInsideCircle)
                            {
                                if (!nodeA.neighbors.Contains(nodeB)) nodeA.neighbors.Add(nodeB);
                                if (!nodeB.neighbors.Contains(nodeA)) nodeB.neighbors.Add(nodeA);
                            }
                        }
                    }
                }
            }
        }

        // 7. Garante que nenhum nó fique isolado
        for (int i = 0; i < createdNodes.Count; i++)
        {
            var nodeA = createdNodes[i];
            if (nodeA.neighbors.Count == 0)
            {
                PedestrianNode closest = null;
                float closestDist = float.MaxValue;
                for (int j = 0; j < createdNodes.Count; j++)
                {
                    if (i == j) continue;
                    var nodeB = createdNodes[j];
                    float d = Vector3.Distance(nodeA.Position, nodeB.Position);
                    if (d < closestDist && d <= 26.0f)
                    {
                        if (NavMesh.CalculatePath(nodeA.Position, nodeB.Position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                        {
                            closestDist = d;
                            closest = nodeB;
                        }
                    }
                }

                if (closest != null)
                {
                    nodeA.neighbors.Add(closest);
                    if (!closest.neighbors.Contains(nodeA)) closest.neighbors.Add(nodeA);
                }
            }
        }

        network.RefreshNodes();
        EditorUtility.SetDirty(networkObj);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(networkObj.scene);

        Debug.Log($"[NPCCreator] Rede de pedestres gerada com sucesso! Total de {createdNodes.Count} nós (anteriormente 47). Distribuição por variedade:");
        foreach (var kvp in typeCounts)
        {
            Debug.Log($"[NPCCreator] - {kvp.Key}: {kvp.Value} locais");
        }
    }

    private static void ClassifyNodeContext(Vector3 pos, out PedestrianNodeType type, out string locName, out Vector3 lookDir, out bool isPause, out float pauseChance, out float minPause, out float maxPause)
    {
        lookDir = Vector3.zero;
        isPause = true;
        pauseChance = 0.35f;
        minPause = 2.0f;
        maxPause = 5.0f;

        // 1. Checa proximidade de Tarefas
        var allTasks = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Include);
        foreach (var taskObj in allTasks)
        {
            if (taskObj == null || !taskObj.gameObject.CompareTag(TaskObject.TaskTag)) continue;
            float dist = Vector3.Distance(pos, taskObj.transform.position);
            if (dist <= Mathf.Max(3.5f, taskObj.InteractionDistance * 1.15f))
            {
                type = PedestrianNodeType.TaskSpot;
                locName = $"Ponto de Tarefa ({taskObj.name})";
                lookDir = taskObj.transform.position - pos;
                lookDir.y = 0;
                pauseChance = 0.65f;
                minPause = 3.5f;
                maxPause = 6.0f;
                return;
            }
        }

        // 2. Checa objetos do cenário em um raio de 4.5m
        Collider[] nearby = Physics.OverlapSphere(pos, 4.5f);
        GameObject closestGas = null;
        GameObject closestBuilding = null;
        GameObject closestCar = null;
        GameObject closestSign = null;
        GameObject closestTree = null;
        float minGasDist = float.MaxValue;
        float minBuildDist = float.MaxValue;
        float minCarDist = float.MaxValue;
        float minSignDist = float.MaxValue;
        float minTreeDist = float.MaxValue;

        foreach (var c in nearby)
        {
            if (c.isTrigger) continue;
            string n = c.gameObject.name.ToLower();
            string p = c.transform.parent != null ? c.transform.parent.name.ToLower() : "";
            float d = Vector3.Distance(pos, c.transform.position);

            if (n.Contains("gas_pump") || n.Contains("gas_station") || n.Contains("gas_tank") || p.Contains("gas"))
            {
                if (d < minGasDist) { minGasDist = d; closestGas = c.gameObject; }
            }
            else if (n.Contains("building") || p.Contains("building"))
            {
                if (d < minBuildDist) { minBuildDist = d; closestBuilding = c.gameObject; }
            }
            else if (n.Contains("car") || p.Contains("car") || n.Contains("parking") || p.Contains("parking"))
            {
                if (d < minCarDist) { minCarDist = d; closestCar = c.gameObject; }
            }
            else if (n.Contains("bilboard") || n.Contains("streetsign") || n.Contains("streetlight"))
            {
                if (d < minSignDist) { minSignDist = d; closestSign = c.gameObject; }
            }
            else if (n.Contains("tree") || p.Contains("tree") || n.Contains("rock"))
            {
                if (d < minTreeDist) { minTreeDist = d; closestTree = c.gameObject; }
            }
        }

        // Prioridade 1: Posto de Gasolina
        if (closestGas != null && minGasDist <= 4.8f)
        {
            type = PedestrianNodeType.GasStation;
            locName = "Posto de Combustível";
            lookDir = closestGas.transform.position - pos;
            lookDir.y = 0;
            pauseChance = 0.45f;
            minPause = 2.5f;
            maxPause = 6.0f;
            return;
        }

        // Prioridade 2: Outdoor / Placa de Trânsito / Iluminação
        if (closestSign != null && minSignDist <= 3.2f)
        {
            type = PedestrianNodeType.ScenicPoint;
            locName = closestSign.name.ToLower().Contains("bilboard") ? "Painel Publicitário (Billboard)" : "Ponto Cênico / Placa Urbana";
            lookDir = closestSign.transform.position - pos;
            lookDir.y = 0;
            pauseChance = 0.55f;
            minPause = 3.0f;
            maxPause = 6.5f;
            return;
        }

        // Prioridade 3: Estacionamento de Veículos
        if (closestCar != null && minCarDist <= 4.2f && closestBuilding == null)
        {
            type = PedestrianNodeType.ParkingLot;
            locName = "Pátio de Estacionamento";
            pauseChance = 0.30f;
            minPause = 1.5f;
            maxPause = 4.0f;
            return;
        }

        // Prioridade 4: Fachada Comercial ou Beco
        if (closestBuilding != null && minBuildDist <= 4.2f)
        {
            // Checa se há múltiplos prédios em lados opostos (beco)
            int oppositeBuildings = 0;
            foreach (var c in nearby)
            {
                if (c.gameObject != closestBuilding && (c.gameObject.name.ToLower().Contains("building") || (c.transform.parent != null && c.transform.parent.name.ToLower().Contains("building"))))
                {
                    oppositeBuildings++;
                }
            }

            if (oppositeBuildings >= 1 && minBuildDist <= 3.2f)
            {
                type = PedestrianNodeType.Alleyway;
                locName = "Beco entre Edifícios";
                pauseChance = 0.20f;
                minPause = 1.0f;
                maxPause = 3.0f;
                return;
            }

            type = PedestrianNodeType.StoreFront;
            locName = "Fachada Comercial / Vitrine";
            lookDir = closestBuilding.transform.position - pos;
            lookDir.y = 0;
            pauseChance = 0.50f;
            minPause = 3.0f;
            maxPause = 7.0f;
            return;
        }

        // Prioridade 5: Parque / Praça / Área Verde
        if (closestTree != null && minTreeDist <= 4.0f)
        {
            type = PedestrianNodeType.ParkGarden;
            locName = "Praça / Área Verde";
            pauseChance = 0.55f;
            minPause = 3.5f;
            maxPause = 8.0f;
            return;
        }

        // Prioridade 6: Travessia de Rua (checa se o solo abaixo é Road ou Roundabout)
        if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.0f))
        {
            string hitName = hit.collider.gameObject.name.ToLower();
            string hitParent = hit.collider.transform.parent != null ? hit.collider.transform.parent.name.ToLower() : "";
            if (hit.collider.CompareTag("Street") || hitName.Contains("road") || hitName.Contains("roundabout") || hitParent.Contains("road"))
            {
                type = PedestrianNodeType.StreetCrossing;
                locName = "Travessia de Rua / Faixa";
                pauseChance = 0.60f;
                minPause = 1.5f;
                maxPause = 2.8f;
                return;
            }
        }

        // Padrão: Calçada Urbana
        type = PedestrianNodeType.Sidewalk;
        locName = "Calçada Urbana";
        pauseChance = 0.35f;
        minPause = 2.0f;
        maxPause = 5.0f;
    }
}
#endif
