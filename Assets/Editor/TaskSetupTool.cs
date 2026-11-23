using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using LineworkLite.FreeOutline;
using LineworkLite.Common.Utils;

[InitializeOnLoad]
public static class TaskSetupTool
{
    private const string SettingsPath = "Assets/Settings/TaskOutlineSettings.asset";
    private const string RendererPath = "Assets/Settings/PC_Renderer.asset";

    private static bool isProcessing = false;

    static TaskSetupTool()
    {
        // Monitora mudanças na hierarquia do Editor:
        // - Se o usuário colocar a tag 'Task' em qualquer objeto, ele automaticamente herda o componente TaskObject e o outline cinza.
        // - Se o usuário retirar a tag 'Task', o componente e o outline cinza são imediatamente removidos!
        EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
        EditorApplication.hierarchyChanged += HandleHierarchyChanged;
    }

    private static void HandleHierarchyChanged()
    {
        if (Application.isPlaying || isProcessing) return;

        try
        {
            isProcessing = true;
            bool sceneDirty = false;

            // 1. Remove TaskObject e a camada de renderização de qualquer objeto que NÃO tenha mais a tag "Task"
            var allTaskComponents = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Include);
            foreach (var task in allTaskComponents)
            {
                if (task == null) continue;
                if (!task.gameObject.CompareTag(TaskObject.TaskTag))
                {
                    task.RemoveTaskRenderingLayer();
                    EditorUtility.SetDirty(task.gameObject);
                    Undo.DestroyObjectImmediate(task);
                    sceneDirty = true;
                }
            }

            // 2. Adiciona TaskObject e rendering layer a qualquer objeto que tenha a tag "Task"
            var taskObjs = GameObject.FindGameObjectsWithTag(TaskObject.TaskTag);
            foreach (var go in taskObjs)
            {
                if (go == null) continue;
                if (!go.TryGetComponent<TaskObject>(out var task))
                {
                    task = Undo.AddComponent<TaskObject>(go);
                    sceneDirty = true;
                }
                task.EnsureTaskConfiguration();
            }

            if (sceneDirty && !Application.isPlaying)
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                }
            }
        }
        catch
        {
            // Ignora exceções transitórias durante deleção ou troca de cena
        }
        finally
        {
            isProcessing = false;
        }
    }

    [MenuItem("Tools/Tasks/Sync All Task Objects and Outlines")]
    public static void SyncAllTaskObjectsAndOutlines()
    {
        HandleHierarchyChanged();
        Debug.Log("[TaskSetupTool] Sincronização de objetos Task concluída.");
    }

    [MenuItem("Tools/Tasks/Setup Task Outlines and Objects")]
    public static void SetupTaskOutlinesAndObjects()
    {
        // 1. Configura o asset TaskOutlineSettings
        var settings = AssetDatabase.LoadAssetAtPath<FreeOutlineSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<FreeOutlineSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        var sso = new SerializedObject(settings);
        sso.FindProperty("showInSceneView").boolValue = true;
        sso.FindProperty("injectionPoint").intValue = (int)InjectionPoint.AfterRenderingPostProcessing;
        sso.ApplyModifiedProperties();

        // Limpa sub-assets antigos
        var oldOutlines = settings.Outlines;
        if (oldOutlines != null)
        {
            foreach (var o in oldOutlines)
            {
                if (o != null) Object.DestroyImmediate(o, true);
            }
            settings.Outlines.Clear();
        }

        // 2a. Cria o contorno CINZA para todas as tasks gerais do cenário
        var outline = ScriptableObject.CreateInstance<Outline>();
        outline.name = "TaskOutline";
        outline.color = new Color(0.72f, 0.72f, 0.75f, 0.95f); // Cinza visível
        outline.occludedColor = new Color(0.72f, 0.72f, 0.75f, 0.95f); // Cinza idêntico através de paredes
        outline.occlusion = Occlusion.WhenNotOccluded; // Visível apenas em linha de visão direta (não visível através de paredes/objetos)
        outline.enableOcclusion = false;
        outline.maskingStrategy = MaskingStrategy.Stencil;
        outline.blendMode = BlendingMode.Alpha;
        outline.extrusionMethod = ExtrusionMethod.WorldSpaceNormalVector; // Extrusão em 3D World Space para diminuir proporcionalmente com a distância!
        outline.scaling = Scaling.ConstantScreenSize;
        outline.width = 2.0f; // ~3cm em espaço 3D - sutil e limpo de longe, nítido e definido de perto
        outline.minWidth = 0.0f;
        outline.renderQueue = OutlineRenderQueue.Opaque;
        outline.layerMask = ~0;
        outline.RenderingLayer = (RenderingLayerMask)TaskObject.TaskRenderingLayerMask; // Bit 1 = Layer 'Task' (2)

        AssetDatabase.AddObjectToAsset(outline, settings);
        settings.Outlines.Add(outline);

        // 2b. Cria o contorno ROXO exclusivo para a task objetivo individual do jogador
        var targetOutline = ScriptableObject.CreateInstance<Outline>();
        targetOutline.name = "TargetTaskOutline";
        targetOutline.color = new Color(0.85f, 0.28f, 1.0f, 1.0f); // Roxo vibrante/brilhante
        targetOutline.occludedColor = new Color(0.85f, 0.28f, 1.0f, 1.0f); // Roxo visível através de paredes
        targetOutline.occlusion = Occlusion.Always;
        targetOutline.enableOcclusion = false;
        targetOutline.maskingStrategy = MaskingStrategy.Stencil;
        targetOutline.blendMode = BlendingMode.Alpha;
        targetOutline.extrusionMethod = ExtrusionMethod.WorldSpaceNormalVector;
        targetOutline.scaling = Scaling.ConstantScreenSize;
        targetOutline.width = 2.4f; // Levemente mais destacado para guiar o jogador com clareza
        targetOutline.minWidth = 0.0f;
        targetOutline.renderQueue = OutlineRenderQueue.Opaque;
        targetOutline.layerMask = ~0;
        targetOutline.RenderingLayer = (RenderingLayerMask)TaskObject.TargetTaskRenderingLayerMask; // Bit 2 = Layer 'TargetTask' (4)

        AssetDatabase.AddObjectToAsset(targetOutline, settings);
        settings.Outlines.Add(targetOutline);

        // Aplica modificações também via SerializedObject para garantir persistência no arquivo YAML
        sso.Update();
        var outlinesProp = sso.FindProperty("outlines");
        outlinesProp.ClearArray();
        outlinesProp.arraySize = 2;
        outlinesProp.GetArrayElementAtIndex(0).objectReferenceValue = outline;
        outlinesProp.GetArrayElementAtIndex(1).objectReferenceValue = targetOutline;
        sso.ApplyModifiedProperties();

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[TaskSetupTool] TaskOutlineSettings configurado com sucesso em: " + SettingsPath);

        // 3. Adiciona FreeOutline no PC_Renderer.asset se não existir
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData != null)
        {
            var rso = new SerializedObject(rendererData);
            var featuresProp = rso.FindProperty("m_RendererFeatures");

            FreeOutline taskFeature = null;
            for (int i = 0; i < featuresProp.arraySize; i++)
            {
                var f = featuresProp.GetArrayElementAtIndex(i).objectReferenceValue as FreeOutline;
                if (f != null)
                {
                    taskFeature = f;
                    break;
                }
            }

            if (taskFeature == null)
            {
                taskFeature = ScriptableObject.CreateInstance<FreeOutline>();
                taskFeature.name = "FreeOutline_Tasks";
                AssetDatabase.AddObjectToAsset(taskFeature, rendererData);

                featuresProp.arraySize++;
                featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = taskFeature;
            }

            var fso = new SerializedObject(taskFeature);
            fso.FindProperty("settings").objectReferenceValue = settings;
            fso.ApplyModifiedProperties();

            rso.ApplyModifiedProperties();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            Debug.Log("[TaskSetupTool] FreeOutline adicionado ao PC_Renderer.asset apontando para TaskOutlineSettings!");
        }

        // 4. Configura todos os objetos da GameScene marcados com tag Task e remove de quem não tiver a tag
        var gameScene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
        
        // Remove de qualquer objeto sem tag Task
        var allTaskComps = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Include);
        int cleaned = 0;
        foreach (var task in allTaskComps)
        {
            if (task == null) continue;
            if (!task.gameObject.CompareTag(TaskObject.TaskTag))
            {
                task.RemoveTaskRenderingLayer();
                Undo.DestroyObjectImmediate(task);
                cleaned++;
            }
        }

        // Adiciona aos objetos com tag Task
        var taskObjs = GameObject.FindGameObjectsWithTag(TaskObject.TaskTag);
        int count = 0;
        foreach (var go in taskObjs)
        {
            var taskComp = TaskObject.Register(go);
            EditorUtility.SetDirty(go);
            count++;
        }

        // 5. Garante que o TaskManager esteja adicionado ao GameObject GameSpawner na GameScene
        var spawnerGo = GameObject.Find("GameSpawner");
        if (spawnerGo != null)
        {
            if (!spawnerGo.TryGetComponent<TaskManager>(out var taskMgr))
            {
                taskMgr = Undo.AddComponent<TaskManager>(spawnerGo);
                EditorUtility.SetDirty(spawnerGo);
                Debug.Log("[TaskSetupTool] TaskManager adicionado com sucesso ao GameObject GameSpawner!");
            }
        }

        EditorSceneManager.MarkSceneDirty(gameScene);
        EditorSceneManager.SaveScene(gameScene);
        Debug.Log($"[TaskSetupTool] {count} objetos marcados com tag 'Task' configurados na GameScene.unity! ({cleaned} limpos)");
    }

    [MenuItem("Tools/Tasks/Render Outline Comparison Previews")]
    public static void RenderOutlineComparisonPreviews()
    {
        string artifactDir = "C:/Users/hspsz/.gemini/antigravity/brain/b13a7445-c510-4b50-be98-d6c792ca05a1";

        var activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.path.Contains("GameScene"))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        }

        // Encontra uma Task para teste
        TaskObject target = null;
        var allTasks = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Exclude);
        foreach (var t in allTasks)
        {
            if (t != null && t.gameObject.name.Contains("Car"))
            {
                target = t;
                break;
            }
        }
        if (target == null && allTasks.Length > 0)
        {
            target = allTasks[0];
        }

        if (target == null)
        {
            Debug.LogError("[TaskSetupTool] Nenhuma Task encontrada para gerar previews!");
            return;
        }

        var settings = AssetDatabase.LoadAssetAtPath<FreeOutlineSettings>(SettingsPath);
        if (settings == null || settings.Outlines.Count == 0)
        {
            SetupTaskOutlinesAndObjects();
            settings = AssetDatabase.LoadAssetAtPath<FreeOutlineSettings>(SettingsPath);
        }
        var outline = settings.Outlines[0];

        // 1. Captura com configuração ANTIGA (ClipSpace, width 12 - causava balão/poluição)
        outline.extrusionMethod = ExtrusionMethod.ClipSpaceNormalVector;
        outline.width = 12.0f;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        CaptureScenePreview(target, 4.5f, Path.Combine(artifactDir, "preview_outline_old_near_4m.png"), false);
        CaptureScenePreview(target, 16.0f, Path.Combine(artifactDir, "preview_outline_old_med_16m.png"), false);
        CaptureScenePreview(target, 42.0f, Path.Combine(artifactDir, "preview_outline_old_far_42m.png"), false);

        // 2. Captura com configuração NOVA (WorldSpace Normal Vector, width 2.0 - mantém proporção perfeita e encolhe com perspectiva)
        outline.extrusionMethod = ExtrusionMethod.WorldSpaceNormalVector;
        outline.width = 2.0f;
        outline.scaling = Scaling.ConstantScreenSize;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        CaptureScenePreview(target, 4.5f, Path.Combine(artifactDir, "preview_outline_new_near_4m.png"), false);
        CaptureScenePreview(target, 16.0f, Path.Combine(artifactDir, "preview_outline_new_med_16m.png"), false);
        CaptureScenePreview(target, 42.0f, Path.Combine(artifactDir, "preview_outline_new_far_42m.png"), false);
        // Também captura através de obstáculo (parede) para demonstrar a visibilidade through-walls perfeita e limpa
        CaptureScenePreview(target, 8.0f, Path.Combine(artifactDir, "preview_outline_new_through_wall.png"), true);

        Debug.Log("[TaskSetupTool] Previews comparativos de contorno de Task gerados com sucesso!");
    }

    private static void CaptureScenePreview(TaskObject target, float distance, string outputPath, bool spawnWall)
    {
        var bounds = GetObjectBounds(target.gameObject);
        Vector3 targetCenter = bounds.center;

        GameObject camObj = new GameObject("OutlinePreviewCam");
        Camera cam = camObj.AddComponent<Camera>();
        var camData = camObj.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;

        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 150f;

        Vector3 dir = (new Vector3(1.2f, 0.5f, -1.5f)).normalized;
        Vector3 camPos = targetCenter + dir * distance;
        camObj.transform.position = camPos;
        camObj.transform.LookAt(targetCenter + Vector3.up * 0.2f);

        GameObject wall = null;
        if (spawnWall)
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TempPreviewWall";
            wall.transform.position = targetCenter + dir * (distance * 0.5f);
            wall.transform.localScale = new Vector3(4f, 4f, 0.3f);
            wall.transform.LookAt(targetCenter);
            var mr = wall.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.2f, 0.22f, 0.26f, 1f);
                mr.material = mat;
            }
        }

        int w = 1280;
        int h = 720;
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        cam.targetTexture = rt;

        RenderTexture.active = rt;
        cam.Render();

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        File.WriteAllBytes(outputPath, tex.EncodeToPNG());

        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        if (wall != null) Object.DestroyImmediate(wall);
        Object.DestroyImmediate(camObj);
    }

    private static Bounds GetObjectBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
    }
}
