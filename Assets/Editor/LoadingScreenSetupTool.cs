#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class LoadingScreenSetupTool
{
    private const string PrefabDir = "Assets/Prefab";
    private const string ResourcesDir = "Assets/Resources";
    private const string PrefabPath = "Assets/Prefab/GameLoadingScreenCanvas.prefab";
    private const string ResourcesPath = "Assets/Resources/GameLoadingScreenCanvas.prefab";

    [MenuItem("Tools/UI/Create or Update Loading Screen Prefab")]
    public static void CreateLoadingScreenPrefab()
    {
        Debug.Log("[LoadingScreenSetupTool] Gerando prefab GameLoadingScreenCanvas...");

        if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);
        if (!Directory.Exists(ResourcesDir)) Directory.CreateDirectory(ResourcesDir);

        // Carrega fontes e sprites
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        Sprite skullSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/HunterLifeIcon.png");
        Sprite smallSkullSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_icon_skull.png");
        Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_panel_hunter.png");
        Sprite borderSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_border_rounded.png");
        Sprite trackSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_slider_track.png");
        Sprite fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_btn_orange.png");
        Sprite tipsBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cartoon/cartoon_list_bg.png");

        // Cria Root Canvas
        GameObject root = new GameObject("GameLoadingScreenCanvas");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();
        CanvasGroup cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        GameLoadingScreenController controller = root.AddComponent<GameLoadingScreenController>();

        // 1. Backdrop escuro total
        GameObject backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(root.transform, false);
        RectTransform backdropRt = backdrop.AddComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        Image backdropImg = backdrop.AddComponent<Image>();
        backdropImg.color = new Color(0.045f, 0.035f, 0.075f, 0.985f); // Quase preto azulado/gótico

        // 2. Card Central
        GameObject card = new GameObject("CenterCard");
        card.transform.SetParent(root.transform, false);
        RectTransform cardRt = card.AddComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(780f, 510f);
        cardRt.anchoredPosition = Vector2.zero;

        Image cardImg = card.AddComponent<Image>();
        if (panelSprite != null)
        {
            cardImg.sprite = panelSprite;
            cardImg.type = Image.Type.Sliced;
        }
        else
        {
            cardImg.color = new Color(0.12f, 0.09f, 0.18f, 0.95f);
        }

        // Borda decorativa do Card
        if (borderSprite != null)
        {
            GameObject border = new GameObject("Border");
            border.transform.SetParent(card.transform, false);
            RectTransform borderRt = border.AddComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = Vector2.zero;
            borderRt.offsetMax = Vector2.zero;
            Image borderImg = border.AddComponent<Image>();
            borderImg.sprite = borderSprite;
            borderImg.type = Image.Type.Sliced;
            borderImg.color = new Color(1f, 1f, 1f, 0.35f);
        }

        // 3. Caveira da Morte (SkullIcon)
        GameObject skullObj = new GameObject("SkullIcon");
        skullObj.transform.SetParent(card.transform, false);
        RectTransform skullRt = skullObj.AddComponent<RectTransform>();
        skullRt.anchorMin = new Vector2(0.5f, 0.5f);
        skullRt.anchorMax = new Vector2(0.5f, 0.5f);
        skullRt.pivot = new Vector2(0.5f, 0.5f);
        skullRt.sizeDelta = new Vector2(120f, 120f);
        skullRt.anchoredPosition = new Vector2(0f, 155f);

        Image skullImg = skullObj.AddComponent<Image>();
        if (skullSprite != null) skullImg.sprite = skullSprite;
        skullImg.color = Color.white;

        // 4. Título Principal
        GameObject titleObj = new GameObject("TitleLabel");
        titleObj.transform.SetParent(card.transform, false);
        RectTransform titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(700f, 44f);
        titleRt.anchoredPosition = new Vector2(0f, 68f);

        TextMeshProUGUI titleTmp = titleObj.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleTmp.font = fontAsset;
        titleTmp.text = "ENTRANDO EM PENA COVA...";
        titleTmp.fontSize = 28f;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = new Color(1f, 0.94f, 0.82f, 1f); // Dourado claro

        // 5. Subtítulo / Status
        GameObject subObj = new GameObject("SubtitleLabel");
        subObj.transform.SetParent(card.transform, false);
        RectTransform subRt = subObj.AddComponent<RectTransform>();
        subRt.anchorMin = new Vector2(0.5f, 0.5f);
        subRt.anchorMax = new Vector2(0.5f, 0.5f);
        subRt.pivot = new Vector2(0.5f, 0.5f);
        subRt.sizeDelta = new Vector2(700f, 32f);
        subRt.anchoredPosition = new Vector2(0f, 28f);

        TextMeshProUGUI subTmp = subObj.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) subTmp.font = fontAsset;
        subTmp.text = "Carregando cenário e sincronizando jogadores...";
        subTmp.fontSize = 16f;
        subTmp.alignment = TextAlignmentOptions.Center;
        subTmp.color = new Color(0.88f, 0.82f, 0.70f, 1f);

        // 6. Barra de Progresso Container
        GameObject barObj = new GameObject("ProgressBarContainer");
        barObj.transform.SetParent(card.transform, false);
        RectTransform barRt = barObj.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.5f, 0.5f);
        barRt.anchorMax = new Vector2(0.5f, 0.5f);
        barRt.pivot = new Vector2(0.5f, 0.5f);
        barRt.sizeDelta = new Vector2(580f, 38f);
        barRt.anchoredPosition = new Vector2(0f, -32f);

        // Track (Fundo da Barra)
        Image trackImg = barObj.AddComponent<Image>();
        if (trackSprite != null)
        {
            trackImg.sprite = trackSprite;
            trackImg.type = Image.Type.Sliced;
        }
        else
        {
            trackImg.color = new Color(0.08f, 0.06f, 0.12f, 0.9f);
        }

        // Fill (Preenchimento da Barra)
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(barObj.transform, false);
        RectTransform fillRt = fillObj.AddComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0.015f, 0.15f);
        fillRt.anchorMax = new Vector2(0.985f, 0.85f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        Image fillImg = fillObj.AddComponent<Image>();
        if (fillSprite != null)
        {
            fillImg.sprite = fillSprite;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 0.5f;
        }
        else
        {
            fillImg.color = new Color(0.95f, 0.55f, 0.15f, 1f);
        }

        // Texto Percentual
        GameObject percentObj = new GameObject("PercentLabel");
        percentObj.transform.SetParent(barObj.transform, false);
        RectTransform percentRt = percentObj.AddComponent<RectTransform>();
        percentRt.anchorMin = Vector2.zero;
        percentRt.anchorMax = Vector2.one;
        percentRt.offsetMin = Vector2.zero;
        percentRt.offsetMax = Vector2.zero;

        TextMeshProUGUI percentTmp = percentObj.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) percentTmp.font = fontAsset;
        percentTmp.text = "0%";
        percentTmp.fontSize = 17f;
        percentTmp.fontStyle = FontStyles.Bold;
        percentTmp.alignment = TextAlignmentOptions.Center;
        percentTmp.color = Color.white;

        // 7. Container de Dicas de Sobrevivência (Tips)
        GameObject tipsContainer = new GameObject("TipsContainer");
        tipsContainer.transform.SetParent(card.transform, false);
        RectTransform tipsRt = tipsContainer.AddComponent<RectTransform>();
        tipsRt.anchorMin = new Vector2(0.5f, 0.5f);
        tipsRt.anchorMax = new Vector2(0.5f, 0.5f);
        tipsRt.pivot = new Vector2(0.5f, 0.5f);
        tipsRt.sizeDelta = new Vector2(660f, 95f);
        tipsRt.anchoredPosition = new Vector2(0f, -155f);

        CanvasGroup tipsCg = tipsContainer.AddComponent<CanvasGroup>();

        Image tipsBg = tipsContainer.AddComponent<Image>();
        if (tipsBgSprite != null)
        {
            tipsBg.sprite = tipsBgSprite;
            tipsBg.type = Image.Type.Sliced;
            tipsBg.color = new Color(0.10f, 0.08f, 0.14f, 0.85f);
        }
        else
        {
            tipsBg.color = new Color(0f, 0f, 0f, 0.45f);
        }

        // Ícone pequeno da dica
        GameObject tipIconObj = new GameObject("TipIcon");
        tipIconObj.transform.SetParent(tipsContainer.transform, false);
        RectTransform tipIconRt = tipIconObj.AddComponent<RectTransform>();
        tipIconRt.anchorMin = new Vector2(0f, 0.5f);
        tipIconRt.anchorMax = new Vector2(0f, 0.5f);
        tipIconRt.pivot = new Vector2(0.5f, 0.5f);
        tipIconRt.sizeDelta = new Vector2(28f, 28f);
        tipIconRt.anchoredPosition = new Vector2(30f, 0f);

        Image tipIconImg = tipIconObj.AddComponent<Image>();
        if (smallSkullSprite != null)
        {
            tipIconImg.sprite = smallSkullSprite;
            tipIconImg.color = new Color(1f, 0.85f, 0.65f, 1f);
        }
        else if (skullSprite != null)
        {
            tipIconImg.sprite = skullSprite;
            tipIconImg.color = new Color(1f, 0.85f, 0.65f, 1f);
        }

        // Texto da dica
        GameObject tipTextObj = new GameObject("TipLabel");
        tipTextObj.transform.SetParent(tipsContainer.transform, false);
        RectTransform tipTextRt = tipTextObj.AddComponent<RectTransform>();
        tipTextRt.anchorMin = new Vector2(0f, 0f);
        tipTextRt.anchorMax = new Vector2(1f, 1f);
        tipTextRt.offsetMin = new Vector2(55f, 8f);
        tipTextRt.offsetMax = new Vector2(-15f, -8f);

        TextMeshProUGUI tipTmp = tipTextObj.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) tipTmp.font = fontAsset;
        tipTmp.text = "<b>DICA:</b> Fique atento ao bater das asas e aos passos da Morte quando ela estiver por perto.";
        tipTmp.fontSize = 15f;
        tipTmp.alignment = TextAlignmentOptions.MidlineLeft;
        tipTmp.enableWordWrapping = true;
        tipTmp.color = new Color(0.96f, 0.91f, 0.82f, 1f);

        // Vincula as referências ao controller via SerializedObject
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("canvas").objectReferenceValue = canvas;
        so.FindProperty("canvasGroup").objectReferenceValue = cg;
        so.FindProperty("cardContainer").objectReferenceValue = cardRt;
        so.FindProperty("backdropImage").objectReferenceValue = backdropImg;
        so.FindProperty("cardBackgroundImage").objectReferenceValue = cardImg;

        so.FindProperty("skullTransform").objectReferenceValue = skullRt;
        so.FindProperty("skullImage").objectReferenceValue = skullImg;
        so.FindProperty("titleLabel").objectReferenceValue = titleTmp;
        so.FindProperty("subtitleLabel").objectReferenceValue = subTmp;

        so.FindProperty("progressBarRect").objectReferenceValue = barRt;
        so.FindProperty("progressTrackImage").objectReferenceValue = trackImg;
        so.FindProperty("progressFillImage").objectReferenceValue = fillImg;
        so.FindProperty("progressPercentLabel").objectReferenceValue = percentTmp;

        so.FindProperty("tipsCanvasGroup").objectReferenceValue = tipsCg;
        so.FindProperty("tipLabel").objectReferenceValue = tipTmp;
        so.FindProperty("tipIconImage").objectReferenceValue = tipIconImg;
        so.ApplyModifiedProperties();

        // Salva prefab em Assets/Prefab e Assets/Resources
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.SaveAsPrefabAsset(root, ResourcesPath);

        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[LoadingScreenSetupTool] Prefab GameLoadingScreenCanvas gerado com sucesso em '{PrefabPath}' e '{ResourcesPath}'!");
    }

    [MenuItem("Tools/UI/Remove Initial Splash Screen in JoinByBrowsing")]
    public static void RemoveInitialSplashInJoinByBrowsing()
    {
        Debug.Log("[LoadingScreenSetupTool] Removendo InitialGameSplashScreen na cena JoinByBrowsing...");

        var startPageCanvas = GameObject.Find("StartPageCanvas");
        if (startPageCanvas != null)
        {
            Transform existingSplash = startPageCanvas.transform.Find("InitialSplashScreen");
            if (existingSplash != null)
            {
                Object.DestroyImmediate(existingSplash.gameObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(startPageCanvas.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(startPageCanvas.scene);
                Debug.Log("[LoadingScreenSetupTool] InitialSplashScreen removido com sucesso!");
            }
        }
    }
}
#endif
