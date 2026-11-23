#if UNITY_EDITOR
using System.IO;
using TMPro;
using TargetIndicators;
using TargetIndicators.Samples;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class TargetIndicatorSetupTool
{
    private const string SourcePrefabPath = "Assets/Target Indicators/Samples/Core Assets/Prefabs/Visual Indicator.prefab";
    private const string TargetIndicatorPrefabPath = "Assets/Prefab/TaskVisualIndicator.prefab";
    private const string HunterIndicatorPrefabPath = "Assets/Prefab/HunterAlertVisualIndicator.prefab";
    private const string ResourcesHunterIndicatorPath = "Assets/Resources/HunterAlertVisualIndicator.prefab";
    private const string CanvasPrefabPath = "Assets/Prefab/TaskIndicatorCanvas.prefab";
    private const string ResourcesCanvasPath = "Assets/Resources/TaskIndicatorCanvas.prefab";
    private const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";
    private const string HunterPrefabPath = "Assets/Prefab/Hunter.prefab";
    private const string ResourcesHunterPrefabPath = "Assets/Resources/Hunter.prefab";

    [MenuItem("Tools/Tasks/Setup Target Indicators for Tasks")]
    public static void SetupTargetIndicators()
    {
        Debug.Log("[TargetIndicatorSetupTool] Iniciando configuração do Target Indicators para Tasks e Hunter...");

        // 1. Garante que os recursos essenciais do TextMeshPro estejam disponíveis
        EnsureTextMeshProEssentials();

        // 2. Cria ou atualiza o Prefab Variant TaskVisualIndicator em tom roxo (Sobreviventes)
        CreateTaskVisualIndicatorPrefab();

        // 3. Cria ou atualiza o Prefab Variant HunterAlertVisualIndicator em tom vermelho (Hunters)
        CreateHunterAlertVisualIndicatorPrefab();

        // 4. Cria o Canvas e Managers do Target Indicators
        CreateTaskIndicatorCanvasPrefab();

        // 5. Adiciona o PlayerTaskIndicatorController ao Player.prefab e Hunter.prefab se necessário
        AddControllerToPlayerPrefab();
        AddControllerToHunterPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TargetIndicatorSetupTool] Configuração concluída com sucesso!");
    }

    private static void EnsureTextMeshProEssentials()
    {
        // Se a pasta TextMesh Pro não existir, importa o pacote de recursos essenciais do cache do UGUI
        if (!Directory.Exists("Assets/TextMesh Pro"))
        {
            string packagePath = "Library/PackageCache/com.unity.ugui@67707a67a4ab/Package Resources/TMP Essential Resources.unitypackage";
            if (File.Exists(packagePath))
            {
                Debug.Log("[TargetIndicatorSetupTool] Importando TMP Essential Resources...");
                AssetDatabase.ImportPackage(packagePath, false);
            }
        }
    }

    private static void CreateTaskVisualIndicatorPrefab()
    {
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (sourcePrefab == null)
        {
            Debug.LogError($"[TargetIndicatorSetupTool] Prefab de origem não encontrado em: {SourcePrefabPath}");
            return;
        }

        // Instancia o prefab de origem para criar a variante
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        instance.name = "TaskVisualIndicator";

        // Configura o componente VisualIndicator
        var vi = instance.GetComponent<VisualIndicator>();
        if (vi != null)
        {
            vi.CoreContentVisibility = IndicatorVisibility.Always;
            vi.RotationContentVisibility = IndicatorVisibility.OutsideBoundary;
            vi.DistanceLabelVisibility = DistanceLabelVisibility.Always;
            vi.DistanceLabelFadeTime = 0.1f;
        }

        Color vibrantPurple = new Color(0.85f, 0.28f, 1.0f, 1.0f);
        Color darkPurple = new Color(0.18f, 0.05f, 0.25f, 0.88f);

        // Ajusta cores dos elementos
        var arrowImage = instance.transform.Find("Offset/Rotation Content/Arrow")?.GetComponent<Image>();
        if (arrowImage != null)
        {
            arrowImage.color = vibrantPurple;
        }

        var outlineImage = instance.transform.Find("Offset/Core Content/Outline")?.GetComponent<Image>();
        if (outlineImage != null)
        {
            outlineImage.color = vibrantPurple;
        }

        var bgImage = instance.transform.Find("Offset/Core Content/Background")?.GetComponent<Image>();
        if (bgImage != null)
        {
            bgImage.color = darkPurple;
        }

        var iconImage = instance.transform.Find("Offset/Core Content/Icon")?.GetComponent<Image>();
        if (iconImage != null)
        {
            iconImage.color = Color.white;
        }

        var distanceLabel = instance.transform.Find("Offset/Distance Label")?.GetComponent<TextMeshProUGUI>();
        if (distanceLabel != null)
        {
            distanceLabel.color = Color.white;
            distanceLabel.fontSize = 14f;
            distanceLabel.fontStyle = FontStyles.Bold;
        }

        // Salva como prefab asset
        if (!Directory.Exists("Assets/Prefab")) Directory.CreateDirectory("Assets/Prefab");
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, TargetIndicatorPrefabPath);
        Object.DestroyImmediate(instance);

        Debug.Log($"[TargetIndicatorSetupTool] TaskVisualIndicator.prefab salvo em: {TargetIndicatorPrefabPath}");
    }

    private static void CreateHunterAlertVisualIndicatorPrefab()
    {
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (sourcePrefab == null)
        {
            Debug.LogError($"[TargetIndicatorSetupTool] Prefab de origem não encontrado em: {SourcePrefabPath}");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        instance.name = "HunterAlertVisualIndicator";

        var vi = instance.GetComponent<VisualIndicator>();
        if (vi != null)
        {
            vi.CoreContentVisibility = IndicatorVisibility.Always;
            vi.RotationContentVisibility = IndicatorVisibility.OutsideBoundary;
            vi.DistanceLabelVisibility = DistanceLabelVisibility.Always;
            vi.DistanceLabelFadeTime = 0.1f;
        }

        Color alertRed = new Color(1.0f, 0.15f, 0.15f, 1.0f);
        Color darkRed = new Color(0.28f, 0.04f, 0.04f, 0.92f);

        var arrowImage = instance.transform.Find("Offset/Rotation Content/Arrow")?.GetComponent<Image>();
        if (arrowImage != null)
        {
            arrowImage.color = alertRed;
        }

        var outlineImage = instance.transform.Find("Offset/Core Content/Outline")?.GetComponent<Image>();
        if (outlineImage != null)
        {
            outlineImage.color = alertRed;
        }

        var bgImage = instance.transform.Find("Offset/Core Content/Background")?.GetComponent<Image>();
        if (bgImage != null)
        {
            bgImage.color = darkRed;
        }

        var iconImage = instance.transform.Find("Offset/Core Content/Icon")?.GetComponent<Image>();
        if (iconImage != null)
        {
            iconImage.color = new Color(1.0f, 0.92f, 0.92f, 1.0f);
        }

        var distanceLabel = instance.transform.Find("Offset/Distance Label")?.GetComponent<TextMeshProUGUI>();
        if (distanceLabel != null)
        {
            distanceLabel.color = Color.white;
            distanceLabel.fontSize = 14f;
            distanceLabel.fontStyle = FontStyles.Bold;
        }

        if (!Directory.Exists("Assets/Prefab")) Directory.CreateDirectory("Assets/Prefab");
        PrefabUtility.SaveAsPrefabAsset(instance, HunterIndicatorPrefabPath);

        if (!Directory.Exists("Assets/Resources")) Directory.CreateDirectory("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(instance, ResourcesHunterIndicatorPath);

        Object.DestroyImmediate(instance);
        Debug.Log($"[TargetIndicatorSetupTool] HunterAlertVisualIndicator.prefab salvo em {HunterIndicatorPrefabPath} e {ResourcesHunterIndicatorPath}");
    }

    private static void CreateTaskIndicatorCanvasPrefab()
    {
        var visualIndicatorPrefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(TargetIndicatorPrefabPath);
        if (visualIndicatorPrefabObj == null)
        {
            Debug.LogError($"[TargetIndicatorSetupTool] TaskVisualIndicator.prefab não encontrado em {TargetIndicatorPrefabPath}");
            return;
        }
        var visualIndicatorPrefab = visualIndicatorPrefabObj.GetComponent<VisualIndicator>();

        // Cria o GameObject raiz do Canvas
        GameObject canvasRoot = new GameObject("TaskIndicatorCanvas");
        Canvas canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRoot.AddComponent<GraphicRaycaster>();

        // Cria o Target Indicators Manager
        GameObject timObj = new GameObject("Target Indicators Manager");
        timObj.transform.SetParent(canvasRoot.transform, false);
        var tim = timObj.AddComponent<TargetIndicatorManager>();
        tim.enabled = false; // Desativa o Update padrão para ser controlado estritamente via GetChanges() no LateUpdate
        tim.BoundaryType = BoundaryType.Padded;
        tim.BoundaryShape = BoundaryShape.Rectangle;
        tim.LeftPadding = 70f;
        tim.RightPadding = 70f;
        tim.TopPadding = 110f; // Espaço para a barra de tarefas e vidas no topo do HUD
        tim.BottomPadding = 70f;

        // Cria o Content RectTransform
        GameObject contentObj = new GameObject("Visual Indicators Content");
        contentObj.transform.SetParent(canvasRoot.transform, false);
        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        // Cria o Visual Indicator Manager
        GameObject vimObj = new GameObject("Visual Indicator Manager");
        vimObj.transform.SetParent(canvasRoot.transform, false);
        var vim = vimObj.AddComponent<VisualIndicatorManager>();
        vim.DefaultVisualIndicatorPrefab = visualIndicatorPrefab;
        vim.AddIndicatorMode = AddIndicatorMode.Auto;

        // Configura as referências serializadas no VisualIndicatorManager
        var vimSo = new SerializedObject(vim);
        vimSo.FindProperty("_defaultVisualIndicatorPrefab").objectReferenceValue = visualIndicatorPrefab;
        vimSo.FindProperty("_targetIndicatorManager").objectReferenceValue = tim;
        vimSo.FindProperty("_canvas").objectReferenceValue = canvas;
        vimSo.FindProperty("_content").objectReferenceValue = contentRect;
        vimSo.FindProperty("_syncBoundaryScaleWithCanvas").boolValue = true;
        vimSo.ApplyModifiedProperties();

        // Salva em Assets/Prefab
        PrefabUtility.SaveAsPrefabAsset(canvasRoot, CanvasPrefabPath);

        // Salva também em Assets/Resources para carregamento limpo em runtime
        if (!Directory.Exists("Assets/Resources")) Directory.CreateDirectory("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(canvasRoot, ResourcesCanvasPath);

        Object.DestroyImmediate(canvasRoot);
        Debug.Log($"[TargetIndicatorSetupTool] TaskIndicatorCanvas.prefab salvo em {CanvasPrefabPath} e {ResourcesCanvasPath}");
    }

    private static void AddControllerToPlayerPrefab()
    {
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError($"[TargetIndicatorSetupTool] Player prefab não encontrado em {PlayerPrefabPath}");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (contents.GetComponent<PlayerTaskIndicatorController>() == null)
        {
            contents.AddComponent<PlayerTaskIndicatorController>();
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            Debug.Log("[TargetIndicatorSetupTool] PlayerTaskIndicatorController adicionado ao Player.prefab!");
        }
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void AddControllerToHunterPrefab()
    {
        string[] hunterPaths = new string[] { HunterPrefabPath, ResourcesHunterPrefabPath };
        foreach (var path in hunterPaths)
        {
            if (!File.Exists(path)) continue;

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            if (contents != null)
            {
                if (contents.GetComponent<PlayerTaskIndicatorController>() == null)
                {
                    contents.AddComponent<PlayerTaskIndicatorController>();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    Debug.Log($"[TargetIndicatorSetupTool] PlayerTaskIndicatorController adicionado a {path}!");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
#endif
