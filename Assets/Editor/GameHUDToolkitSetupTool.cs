using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UniversityOfGames.ProgressBarToolkit;

/// <summary>
/// Utilitário de Editor para configurar e mesclar as barras de progresso do Player e do Hunter
/// no GameHUD usando o pacote 2D Progress Bar Toolkit (ClassicProgressBar + CircularProgressBar).
/// </summary>
[InitializeOnLoad]
public static class GameHUDToolkitSetupTool
{
    private const string GameHudPrefabPath = "Assets/Prefab/GameHUD.prefab";
    private const string ToolkitPrefabsRoot = "Assets/2D Progress Bar Toolkit/Prefabs/";
    private const string SpritesFolder = "Assets/UI/Sprites";
    private const string CartoonSpritesFolder = "Assets/UI/Sprites/Cartoon";
    private const string TmpFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string PauseMenuUxmlPath = "Assets/UI/UXML/InGamePauseMenu.uxml";
    private const string PausePanelSettingsPath = "Assets/UI/InGamePausePanelSettings.asset";
    private const string PauseMenuPrefabPath = "Assets/Prefab/InGamePauseMenu.prefab";

    private sealed class CartoonHudSprites
    {
        public Sprite CardBg;
        public Sprite CardBorder;
        public Sprite RoundedBorder;
        public Sprite CircleBg;
        public Sprite PanelCream;
        public Sprite TrackSand;
        public Sprite CircleCream;
        public Sprite CircleHunter;
        public Sprite BtnGreen;
        public Sprite BtnRed;
        public Sprite IconFireball;
        public Sprite IconKick;
        public Sprite IconScythe;
        public Sprite IconSkull;
    }

    static GameHUDToolkitSetupTool()
    {
        EditorApplication.delayCall += EnsureGameHudSetupIfMissing;
    }

    private static void EnsureGameHudSetupIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GameHudPrefabPath);
        if (prefabAsset == null) return;

        Transform existingCanvas = prefabAsset.transform.Find("ProgressBarsCanvas");
        GameHUDController controller = prefabAsset.GetComponent<GameHUDController>();
        bool needsSetup = existingCanvas == null || controller == null;

        if (!needsSetup)
        {
            SerializedObject so = new SerializedObject(controller);
            SerializedProperty canvasProp = so.FindProperty("progressBarsCanvas");
            SerializedProperty fireballProp = so.FindProperty("fireballIconSprite");
            if (canvasProp == null || canvasProp.objectReferenceValue == null ||
                fireballProp == null || fireballProp.objectReferenceValue == null)
            {
                needsSetup = true;
            }
        }

        if (!needsSetup)
        {
            GameObject pausePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuPrefabPath);
            Transform tasksPanel = existingCanvas != null ? existingCanvas.Find("TasksProgressPanel") : null;
            Image tasksImg = tasksPanel != null ? tasksPanel.GetComponent<Image>() : null;
            if (pausePrefabAsset == null ||
                (tasksPanel is RectTransform tasksRt && tasksRt.anchoredPosition.y < -30f) ||
                tasksImg == null || tasksImg.sprite == null || tasksImg.sprite.name != "cartoon_panel_cream")
            {
                needsSetup = true;
            }
        }

        if (needsSetup)
        {
            SetupGameHudProgressBars();
        }
    }

    private static CartoonHudSprites LoadHudSprites()
    {
        EnsureUiSpritesExist(
            out Sprite cardBgSprite,
            out Sprite cardBorderSprite,
            out Sprite circleBgSprite);

        if (AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_border_rounded.png") == null)
        {
            CartoonUISpriteGenerator.GenerateAllSprites();
        }

        return new CartoonHudSprites
        {
            CardBg = cardBgSprite,
            CardBorder = cardBorderSprite,
            RoundedBorder = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_border_rounded.png"),
            CircleBg = circleBgSprite,
            PanelCream = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_panel_cream.png"),
            TrackSand = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_slider_track.png"),
            CircleCream = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_circle_cream.png"),
            CircleHunter = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_circle_hunter.png"),
            BtnGreen = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_btn_green.png"),
            BtnRed = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_btn_red.png"),
            IconFireball = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_icon_fireball.png"),
            IconKick = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_icon_kick.png"),
            IconScythe = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_icon_scythe.png"),
            IconSkull = AssetDatabase.LoadAssetAtPath<Sprite>($"{CartoonSpritesFolder}/cartoon_icon_skull.png"),
        };
    }

    [MenuItem("Tools/End Of The World Hotel/Setup GameHUD Progress Bars (2D Toolkit)")]
    public static void SetupGameHudProgressBars()
    {
        CartoonHudSprites sprites = LoadHudSprites();

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
        if (fontAsset == null)
        {
            Debug.LogError($"[GameHUDToolkitSetupTool] TMP Font não encontrada em '{TmpFontPath}'.");
            return;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(GameHudPrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[GameHUDToolkitSetupTool] Não foi possível carregar '{GameHudPrefabPath}'.");
            return;
        }

        try
        {
            GameHUDController hudController = prefabRoot.GetComponent<GameHUDController>();
            if (hudController == null)
            {
                Debug.LogError("[GameHUDToolkitSetupTool] GameHUDController não encontrado em GameHUD.prefab.");
                return;
            }

            // Remove instância anterior para garantir execução idempotente
            Transform existingCanvas = prefabRoot.transform.Find("ProgressBarsCanvas");
            if (existingCanvas != null)
            {
                Object.DestroyImmediate(existingCanvas.gameObject);
            }

            // 1. Canvas overlay alinhado 1:1 com GameHUDPanelSettings (1920x1080, Match 0.5, SortingOrder > 10)
            GameObject canvasGo = CreateUiObject("ProgressBarsCanvas", prefabRoot.transform);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 2. Painel Top-Left: Tarefas Globais (Barra Normal Segmentada - Compartilhada)
            BuildTasksProgressPanel(
                canvasGo.transform, fontAsset, sprites,
                out GameObject tasksPanel, out TMP_Text tasksTitle, out TMP_Text tasksCounter, out ClassicProgressBar tasksBar);

            // 3. Painel Top-Left (abaixo de Tarefas): Tempo até Revelação (Barra Normal Segmentada - Player)
            BuildRevelationTimerPanel(
                canvasGo.transform, fontAsset, sprites,
                out GameObject revelationPanel, out Image revelationBorder,
                out TMP_Text revelationTitle, out TMP_Text revelationCountdown,
                out TMP_Text revelationSublabel, out ClassicProgressBar revelationBar);

            // 4. Painel Bottom-Center: Barra de Stamina (Barra Normal com Gradiente - Compartilhada)
            BuildStaminaPanel(
                canvasGo.transform, sprites,
                out GameObject staminaPanel, out CanvasGroup staminaCg, out ClassicProgressBar staminaBar);

            // 5. Painel Bottom-Center (acima da Stamina): Cooldown do Chute (Barra Circular 360° - Player)
            BuildKickCooldownPanel(
                canvasGo.transform, fontAsset, sprites,
                out GameObject kickPanel, out TMP_Text kickStatus, out TMP_Text kickTitle, out CircularProgressBar kickBar);

            // 6. Painel Bottom-Center (acima da Stamina): Cooldown da Bola de Fogo (Barra Circular 270° - Hunter)
            BuildFireballCooldownPanel(
                canvasGo.transform, fontAsset, sprites,
                out GameObject fireballPanel, out TMP_Text fireballStatus, out TMP_Text fireballTitle, out CircularProgressBar fireballBar);

            // 7. Painel Lower-Center: Progresso de Interação da Task (Barra Circular Sólida - Player)
            BuildTaskInteractionPanel(
                canvasGo.transform, fontAsset, sprites,
                out GameObject interactionPanel, out TMP_Text interactionTitle,
                out TMP_Text interactionPercent, out TMP_Text interactionHint, out CircularProgressBar interactionBar);

            // 8. Vincula todas as referências e sprites Cartoon no GameHUDController
            SerializedObject controllerSo = new SerializedObject(hudController);
            controllerSo.FindProperty("progressBarsCanvas").objectReferenceValue = canvas;

            controllerSo.FindProperty("fireballIconSprite").objectReferenceValue = sprites.IconFireball;
            controllerSo.FindProperty("kickIconSprite").objectReferenceValue = sprites.IconKick;
            controllerSo.FindProperty("scytheIconSprite").objectReferenceValue = sprites.IconScythe;
            controllerSo.FindProperty("killSkullIconSprite").objectReferenceValue = sprites.IconSkull;

            controllerSo.FindProperty("tasksProgressPanel").objectReferenceValue = tasksPanel;
            controllerSo.FindProperty("tasksTitleLabel").objectReferenceValue = tasksTitle;
            controllerSo.FindProperty("tasksCounterLabel").objectReferenceValue = tasksCounter;
            controllerSo.FindProperty("tasksProgressBar").objectReferenceValue = tasksBar;

            controllerSo.FindProperty("staminaContainer").objectReferenceValue = staminaPanel;
            controllerSo.FindProperty("staminaCanvasGroup").objectReferenceValue = staminaCg;
            controllerSo.FindProperty("staminaProgressBar").objectReferenceValue = staminaBar;

            controllerSo.FindProperty("revelationTimerPanel").objectReferenceValue = revelationPanel;
            controllerSo.FindProperty("revelationBorderImage").objectReferenceValue = revelationBorder;
            controllerSo.FindProperty("revelationTitleLabel").objectReferenceValue = revelationTitle;
            controllerSo.FindProperty("revelationCountdownLabel").objectReferenceValue = revelationCountdown;
            controllerSo.FindProperty("revelationStatusSublabel").objectReferenceValue = revelationSublabel;
            controllerSo.FindProperty("revelationProgressBar").objectReferenceValue = revelationBar;

            controllerSo.FindProperty("kickCooldownPanel").objectReferenceValue = kickPanel;
            controllerSo.FindProperty("kickCooldownStatusLabel").objectReferenceValue = kickStatus;
            controllerSo.FindProperty("kickCooldownTitleLabel").objectReferenceValue = kickTitle;
            controllerSo.FindProperty("kickCooldownBar").objectReferenceValue = kickBar;

            controllerSo.FindProperty("taskInteractionPanel").objectReferenceValue = interactionPanel;
            controllerSo.FindProperty("taskInteractionTitle").objectReferenceValue = interactionTitle;
            controllerSo.FindProperty("taskInteractionPercentLabel").objectReferenceValue = interactionPercent;
            controllerSo.FindProperty("taskInteractionHint").objectReferenceValue = interactionHint;
            controllerSo.FindProperty("taskInteractionBar").objectReferenceValue = interactionBar;

            controllerSo.FindProperty("fireballCooldownPanel").objectReferenceValue = fireballPanel;
            controllerSo.FindProperty("fireballCooldownStatusLabel").objectReferenceValue = fireballStatus;
            controllerSo.FindProperty("fireballCooldownTitleLabel").objectReferenceValue = fireballTitle;
            controllerSo.FindProperty("fireballCooldownBar").objectReferenceValue = fireballBar;

            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            BuildPauseMenuChild(prefabRoot.transform);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, GameHudPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameHUDToolkitSetupTool] GameHUD.prefab atualizado com sucesso com 2D Progress Bar Toolkit (barras circulares e normais)!");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void BuildTasksProgressPanel(
        Transform parent,
        TMP_FontAsset font,
        CartoonHudSprites sprites,
        out GameObject panelGo,
        out TMP_Text titleLabel,
        out TMP_Text counterLabel,
        out ClassicProgressBar bar)
    {
        panelGo = CreateCardPanel(
            "TasksProgressPanel",
            parent,
            anchorMin: new Vector2(0f, 1f),
            anchorMax: new Vector2(0f, 1f),
            pivot: new Vector2(0f, 1f),
            anchoredPos: new Vector2(24f, -24f),
            size: new Vector2(286f, 94f),
            bgColor: Color.white,
            borderColor: Color.clear,
            cardBg: sprites.PanelCream,
            cardBorder: sprites.RoundedBorder != null ? sprites.RoundedBorder : sprites.CardBorder,
            out _,
            ppuMultiplier: 1.35f);

        titleLabel = CreateTmpLabel(
            "TasksTitleLabel", panelGo.transform, font,
            text: "TAREFAS GLOBAIS",
            fontSize: 13f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.24f, 0.14f, 0.08f, 1f),
            alignment: TextAlignmentOptions.TopLeft,
            anchorMin: new Vector2(0f, 1f),
            anchorMax: new Vector2(1f, 1f),
            pivot: new Vector2(0f, 1f),
            anchoredPos: new Vector2(18f, -11f),
            size: new Vector2(-88f, 44f));
        titleLabel.textWrappingMode = TextWrappingModes.Normal;

        counterLabel = CreateTmpLabel(
            "TasksCounterLabel", panelGo.transform, font,
            text: "0 / 3",
            fontSize: 14.5f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.08f, 0.50f, 0.24f, 1f),
            alignment: TextAlignmentOptions.TopRight,
            anchorMin: new Vector2(1f, 1f),
            anchorMax: new Vector2(1f, 1f),
            pivot: new Vector2(1f, 1f),
            anchoredPos: new Vector2(-18f, -10f),
            size: new Vector2(72f, 22f));

        bar = InstantiateToolkitBar<ClassicProgressBar>(
            "ClassicProgressBar.prefab",
            "TasksProgressBar",
            panelGo.transform,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 16f),
            size: new Vector2(244f, 13f),
            zRotation: 0f);

        ConfigureBar(
            bar,
            mainColor: new Color(0.84f, 0.76f, 0.64f, 0.90f),
            fillColor: new Color(0.09f, 0.64f, 0.29f, 1f),
            useGradient: false,
            gradient: null,
            segments: 3,
            notch: 5f,
            fillAmount: 0f,
            smoothingSpeed: 6f);
    }

    private static void BuildRevelationTimerPanel(
        Transform parent,
        TMP_FontAsset font,
        CartoonHudSprites sprites,
        out GameObject panelGo,
        out Image borderImage,
        out TMP_Text titleLabel,
        out TMP_Text countdownLabel,
        out TMP_Text sublabel,
        out ClassicProgressBar bar)
    {
        panelGo = CreateCardPanel(
            "RevelationTimerPanel",
            parent,
            anchorMin: new Vector2(0f, 1f),
            anchorMax: new Vector2(0f, 1f),
            pivot: new Vector2(0f, 1f),
            anchoredPos: new Vector2(24f, -128f),
            size: new Vector2(286f, 88f),
            bgColor: Color.white,
            borderColor: new Color(0.49f, 0.23f, 0.93f, 0.50f),
            cardBg: sprites.PanelCream,
            cardBorder: sprites.RoundedBorder != null ? sprites.RoundedBorder : sprites.CardBorder,
            out borderImage,
            ppuMultiplier: 1.35f);

        titleLabel = CreateTmpLabel(
            "RevelationTitleLabel", panelGo.transform, font,
            text: "TEMPO ATÉ REVELAÇÃO",
            fontSize: 11f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.24f, 0.14f, 0.08f, 1f),
            alignment: TextAlignmentOptions.MidlineLeft,
            anchorMin: new Vector2(0f, 1f),
            anchorMax: new Vector2(1f, 1f),
            pivot: new Vector2(0f, 1f),
            anchoredPos: new Vector2(18f, -10f),
            size: new Vector2(-94f, 20f));

        countdownLabel = CreateTmpLabel(
            "RevelationCountdownLabel", panelGo.transform, font,
            text: "01:25",
            fontSize: 13.5f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.49f, 0.23f, 0.93f, 1f),
            alignment: TextAlignmentOptions.MidlineRight,
            anchorMin: new Vector2(1f, 1f),
            anchorMax: new Vector2(1f, 1f),
            pivot: new Vector2(1f, 1f),
            anchoredPos: new Vector2(-18f, -10f),
            size: new Vector2(76f, 20f));

        bar = InstantiateToolkitBar<ClassicProgressBar>(
            "ClassicProgressBar.prefab",
            "RevelationProgressBar",
            panelGo.transform,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, -1f),
            size: new Vector2(244f, 10f),
            zRotation: 0f);

        ConfigureBar(
            bar,
            mainColor: new Color(0.84f, 0.76f, 0.64f, 0.90f),
            fillColor: new Color(0.55f, 0.36f, 0.96f, 1f),
            useGradient: false,
            gradient: null,
            segments: 10,
            notch: 4f,
            fillAmount: 1f,
            smoothingSpeed: 8f);

        sublabel = CreateTmpLabel(
            "RevelationStatusSublabel", panelGo.transform, font,
            text: "Faça sua task para não ser detectado",
            fontSize: 9.5f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.43f, 0.31f, 0.21f, 1f),
            alignment: TextAlignmentOptions.Center,
            anchorMin: new Vector2(0f, 0f),
            anchorMax: new Vector2(1f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 10f),
            size: new Vector2(-28f, 18f));
    }

    private static void BuildStaminaPanel(
        Transform parent,
        CartoonHudSprites sprites,
        out GameObject panelGo,
        out CanvasGroup canvasGroup,
        out ClassicProgressBar bar)
    {
        panelGo = CreateCardPanel(
            "StaminaContainer",
            parent,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 22f),
            size: new Vector2(276f, 24f),
            bgColor: Color.white,
            borderColor: Color.clear,
            cardBg: sprites.TrackSand,
            cardBorder: sprites.CardBorder,
            out _,
            ppuMultiplier: 1.7f);

        canvasGroup = panelGo.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        bar = InstantiateToolkitBar<ClassicProgressBar>(
            "ClassicProgressBar_Gradient.prefab",
            "StaminaProgressBar",
            panelGo.transform,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: Vector2.zero,
            size: new Vector2(254f, 11f),
            zRotation: 0f);

        Gradient staminaGrad = CreateGradient(
            new Color(0.906f, 0.298f, 0.235f, 1f),
            new Color(0.953f, 0.612f, 0.071f, 1f),
            new Color(0.130f, 0.770f, 0.370f, 1f));

        ConfigureBar(
            bar,
            mainColor: new Color(0.62f, 0.52f, 0.40f, 0.45f),
            fillColor: new Color(0.130f, 0.770f, 0.370f, 1f),
            useGradient: true,
            gradient: staminaGrad,
            segments: 12,
            notch: 3f,
            fillAmount: 1f,
            smoothingSpeed: 12f);
    }

    private static void BuildKickCooldownPanel(
        Transform parent,
        TMP_FontAsset font,
        CartoonHudSprites sprites,
        out GameObject panelGo,
        out TMP_Text statusLabel,
        out TMP_Text titleLabel,
        out CircularProgressBar bar)
    {
        panelGo = CreateUiObject("KickCooldownPanel", parent);
        SetRect(
            panelGo,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 54f),
            size: new Vector2(124f, 112f));

        CreateCartoonMedallionBackdrop(panelGo.transform, sprites.CircleCream, new Vector2(0f, -1f), 86f);

        bar = InstantiateToolkitBar<CircularProgressBar>(
            "CircularProgressBar_360.prefab",
            "KickCooldownBar",
            panelGo.transform,
            anchorMin: new Vector2(0.5f, 1f),
            anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 1f),
            anchoredPos: new Vector2(0f, -7f),
            size: new Vector2(74f, 74f),
            zRotation: 0f);

        ConfigureBar(
            bar,
            mainColor: new Color(0.78f, 0.70f, 0.58f, 0.55f),
            fillColor: new Color(0.13f, 0.77f, 0.37f, 1f),
            useGradient: false,
            gradient: null,
            segments: 10,
            notch: 5f,
            fillAmount: 1f,
            smoothingSpeed: 12f,
            startAngle: 0f,
            endAngle: 360f);

        statusLabel = CreateTmpLabel(
            "KickCooldownStatusLabel", panelGo.transform, font,
            text: "PRONTO",
            fontSize: 11f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.08f, 0.50f, 0.24f, 1f),
            alignment: TextAlignmentOptions.Center,
            anchorMin: new Vector2(0.5f, 1f),
            anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, -44f),
            size: new Vector2(68f, 24f));

        titleLabel = CreateCartoonPillTag(
            panelGo.transform,
            tagObjectName: "KickTitleTag",
            pillSprite: sprites.BtnGreen,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 0f),
            size: new Vector2(118f, 26f),
            ppuMultiplier: 2.35f,
            font: font,
            labelName: "KickCooldownTitleLabel",
            text: "CHUTE [E]",
            fontSize: 10.5f,
            textColor: Color.white);
    }

    private static void BuildFireballCooldownPanel(
        Transform parent,
        TMP_FontAsset font,
        CartoonHudSprites sprites,
        out GameObject panelGo,
        out TMP_Text statusLabel,
        out TMP_Text titleLabel,
        out CircularProgressBar bar)
    {
        panelGo = CreateUiObject("FireballCooldownPanel", parent);
        SetRect(
            panelGo,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 54f),
            size: new Vector2(146f, 114f));

        CreateCartoonMedallionBackdrop(panelGo.transform, sprites.CircleHunter, new Vector2(0f, -1f), 88f);

        // Rotacionado 180° em Z para deixar o arco aberto de 90° voltado para baixo (preenchimento estilo mostrador)
        bar = InstantiateToolkitBar<CircularProgressBar>(
            "CircularProgressBar_270.prefab",
            "FireballCooldownBar",
            panelGo.transform,
            anchorMin: new Vector2(0.5f, 1f),
            anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, -45f),
            size: new Vector2(78f, 78f),
            zRotation: 180f);

        Gradient fireGrad = CreateGradient(
            new Color(0.95f, 0.25f, 0.12f, 1f),
            new Color(1f, 0.55f, 0.08f, 1f),
            new Color(1f, 0.78f, 0.25f, 1f));

        ConfigureBar(
            bar,
            mainColor: new Color(0.24f, 0.10f, 0.11f, 0.85f),
            fillColor: new Color(1f, 0.72f, 0.20f, 1f),
            useGradient: true,
            gradient: fireGrad,
            segments: 8,
            notch: 5f,
            fillAmount: 1f,
            smoothingSpeed: 12f,
            startAngle: 45f,
            endAngle: 315f);

        statusLabel = CreateTmpLabel(
            "FireballCooldownStatusLabel", panelGo.transform, font,
            text: "PRONTA",
            fontSize: 11f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.99f, 0.83f, 0.30f, 1f),
            alignment: TextAlignmentOptions.Center,
            anchorMin: new Vector2(0.5f, 1f),
            anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, -45f),
            size: new Vector2(70f, 24f));

        titleLabel = CreateCartoonPillTag(
            panelGo.transform,
            tagObjectName: "FireballTitleTag",
            pillSprite: sprites.BtnRed,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 0f),
            size: new Vector2(150f, 26f),
            ppuMultiplier: 2.35f,
            font: font,
            labelName: "FireballCooldownTitleLabel",
            text: "BOLA DE FOGO",
            fontSize: 10f,
            textColor: Color.white);

        // Oculto até SetLocalRole(true)
        panelGo.SetActive(false);
    }

    private static void BuildTaskInteractionPanel(
        Transform parent,
        TMP_FontAsset font,
        CartoonHudSprites sprites,
        out GameObject panelGo,
        out TMP_Text titleLabel,
        out TMP_Text percentLabel,
        out TMP_Text hintLabel,
        out CircularProgressBar bar)
    {
        panelGo = CreateUiObject("TaskInteractionPanel", parent);
        SetRect(
            panelGo,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 176f),
            size: new Vector2(440f, 168f));

        titleLabel = CreateCartoonPillTag(
            panelGo.transform,
            tagObjectName: "TaskTitlePill",
            pillSprite: sprites.PanelCream,
            anchorMin: new Vector2(0.5f, 1f),
            anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 1f),
            anchoredPos: new Vector2(0f, 0f),
            size: new Vector2(360f, 30f),
            ppuMultiplier: 2.2f,
            font: font,
            labelName: "TaskInteractionTitle",
            text: "INTERAGINDO COM A TASK",
            fontSize: 12f,
            textColor: new Color(0.08f, 0.50f, 0.24f, 1f));

        GameObject backdropGo = CreateUiObject("Backdrop", panelGo.transform);
        SetRect(
            backdropGo,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 1f), new Vector2(104f, 104f));
        Image backdropImg = backdropGo.AddComponent<Image>();
        backdropImg.sprite = sprites.CircleCream;
        backdropImg.color = Color.white;
        backdropImg.raycastTarget = false;

        bar = InstantiateToolkitBar<CircularProgressBar>(
            "CircularProgressBar_Solid.prefab",
            "TaskInteractionBar",
            panelGo.transform,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, 1f),
            size: new Vector2(90f, 90f),
            zRotation: 0f);

        Gradient taskGrad = CreateGradient(
            new Color(0.130f, 0.770f, 0.370f, 1f),
            new Color(0.080f, 0.720f, 0.520f, 1f),
            new Color(0.050f, 0.580f, 0.530f, 1f));

        ConfigureBar(
            bar,
            mainColor: new Color(0.78f, 0.70f, 0.58f, 0.55f),
            fillColor: new Color(0.130f, 0.770f, 0.370f, 1f),
            useGradient: true,
            gradient: taskGrad,
            segments: 1,
            notch: 0f,
            fillAmount: 0f,
            smoothingSpeed: 0f,
            startAngle: 0f,
            endAngle: 360f);

        percentLabel = CreateTmpLabel(
            "TaskInteractionPercentLabel", panelGo.transform, font,
            text: "0%",
            fontSize: 18f,
            fontStyle: FontStyles.Bold,
            color: new Color(0.08f, 0.50f, 0.24f, 1f),
            alignment: TextAlignmentOptions.Center,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, 1f),
            size: new Vector2(82f, 30f));

        hintLabel = CreateCartoonPillTag(
            panelGo.transform,
            tagObjectName: "TaskHintPill",
            pillSprite: sprites.PanelCream,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f),
            anchoredPos: new Vector2(0f, 0f),
            size: new Vector2(390f, 26f),
            ppuMultiplier: 2.4f,
            font: font,
            labelName: "TaskInteractionHint",
            text: "SEGURE O BOTÃO ESQUERDO DO MOUSE",
            fontSize: 10.5f,
            textColor: new Color(0.36f, 0.23f, 0.14f, 1f));

        panelGo.SetActive(false);
    }

    private static void CreateCartoonMedallionBackdrop(Transform parent, Sprite sprite, Vector2 anchoredPos, float size)
    {
        GameObject backdropGo = CreateUiObject("Backdrop", parent);
        SetRect(
            backdropGo,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            anchoredPos, new Vector2(size, size));
        Image backdropImg = backdropGo.AddComponent<Image>();
        backdropImg.sprite = sprite;
        backdropImg.color = Color.white;
        backdropImg.raycastTarget = false;
    }

    private static TMP_Text CreateCartoonPillTag(
        Transform parent,
        string tagObjectName,
        Sprite pillSprite,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size,
        float ppuMultiplier,
        TMP_FontAsset font,
        string labelName,
        string text,
        float fontSize,
        Color textColor)
    {
        GameObject tagGo = CreateUiObject(tagObjectName, parent);
        SetRect(tagGo, anchorMin, anchorMax, pivot, anchoredPos, size);
        Image pillImg = tagGo.AddComponent<Image>();
        pillImg.sprite = pillSprite;
        pillImg.type = Image.Type.Sliced;
        pillImg.pixelsPerUnitMultiplier = ppuMultiplier;
        pillImg.color = Color.white;
        pillImg.raycastTarget = false;

        return CreateTmpLabel(
            labelName, tagGo.transform, font,
            text: text,
            fontSize: fontSize,
            fontStyle: FontStyles.Bold,
            color: textColor,
            alignment: TextAlignmentOptions.Center,
            anchorMin: Vector2.zero,
            anchorMax: Vector2.one,
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: new Vector2(0f, 1f),
            size: new Vector2(-14f, -4f));
    }

    private static T InstantiateToolkitBar<T>(
        string prefabName,
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size,
        float zRotation) where T : SegmentedProgressBar
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ToolkitPrefabsRoot + prefabName);
        if (asset == null)
        {
            Debug.LogError($"[GameHUDToolkitSetupTool] Prefab não encontrado: {ToolkitPrefabsRoot + prefabName}");
            return null;
        }

        GameObject instance = Object.Instantiate(asset, parent, false);
        instance.name = objectName;

        RectTransform rect = instance.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.Euler(0f, 0f, zRotation);

        return instance.GetComponent<T>();
    }

    private static void ConfigureBar(
        SegmentedProgressBar bar,
        Color mainColor,
        Color fillColor,
        bool useGradient,
        Gradient gradient,
        int segments,
        float notch,
        float fillAmount,
        float smoothingSpeed,
        float startAngle = 0f,
        float endAngle = 360f)
    {
        if (bar == null) return;

        SerializedObject so = new SerializedObject(bar);
        so.FindProperty("m_MainColor").colorValue = mainColor;
        so.FindProperty("m_FillColor").colorValue = fillColor;
        so.FindProperty("m_UseFillGradient").boolValue = useGradient;
        if (gradient != null)
        {
            so.FindProperty("m_FillGradient").gradientValue = gradient;
        }
        so.FindProperty("m_NumberOfSegments").intValue = Mathf.Max(1, segments);
        so.FindProperty("m_SizeOfNotch").floatValue = Mathf.Max(0f, notch);
        so.FindProperty("m_FillAmount").floatValue = Mathf.Clamp01(fillAmount);
        so.FindProperty("m_FillMode").enumValueIndex = (int)SegmentedProgressBar.FillMode.Continuous;
        so.FindProperty("m_SmoothingSpeed").floatValue = Mathf.Max(0f, smoothingSpeed);

        if (bar is CircularProgressBar)
        {
            so.FindProperty("m_StartAngle").floatValue = startAngle;
            so.FindProperty("m_EndAngle").floatValue = endAngle;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateCardPanel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size,
        Color bgColor,
        Color borderColor,
        Sprite cardBg,
        Sprite cardBorder,
        out Image borderImage,
        float ppuMultiplier = 1f)
    {
        GameObject go = CreateUiObject(name, parent);
        SetRect(go, anchorMin, anchorMax, pivot, anchoredPos, size);

        Image bgImg = go.AddComponent<Image>();
        bgImg.sprite = cardBg;
        bgImg.type = Image.Type.Sliced;
        bgImg.pixelsPerUnitMultiplier = ppuMultiplier;
        bgImg.color = bgColor;
        bgImg.raycastTarget = false;

        GameObject borderGo = CreateUiObject("Border", go.transform);
        SetRect(borderGo, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        borderImage = borderGo.AddComponent<Image>();
        borderImage.sprite = cardBorder;
        borderImage.type = Image.Type.Sliced;
        borderImage.pixelsPerUnitMultiplier = ppuMultiplier;
        borderImage.color = borderColor;
        borderImage.raycastTarget = false;

        return go;
    }

    private static TMP_Text CreateTmpLabel(
        string name,
        Transform parent,
        TMP_FontAsset font,
        string text,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        TextAlignmentOptions alignment,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size)
    {
        GameObject go = CreateUiObject(name, parent);
        SetRect(go, anchorMin, anchorMax, pivot, anchoredPos, size);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        tmp.richText = true;
        return tmp;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static RectTransform SetRect(
        GameObject go,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Gradient CreateGradient(Color start, Color mid, Color end)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(mid, 0.5f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(end.a, 1f)
            });
        return g;
    }

    private static void EnsureUiSpritesExist(
        out Sprite cardBgSprite,
        out Sprite cardBorderSprite,
        out Sprite circleBgSprite)
    {
        if (!Directory.Exists(SpritesFolder))
        {
            Directory.CreateDirectory(SpritesFolder);
        }

        string cardBgPath = $"{SpritesFolder}/HudCardBg.png";
        string cardBorderPath = $"{SpritesFolder}/HudCardBorder.png";
        string circleBgPath = $"{SpritesFolder}/HudCircleBg.png";

        GenerateRoundedRectPng(cardBgPath, 64, 64, 14f, 0f, fillInside: true);
        GenerateRoundedRectPng(cardBorderPath, 64, 64, 14f, 2f, fillInside: false);
        GenerateCirclePng(circleBgPath, 128);

        AssetDatabase.Refresh();

        ConfigureSpriteImporter(cardBgPath, new Vector4(18f, 18f, 18f, 18f));
        ConfigureSpriteImporter(cardBorderPath, new Vector4(18f, 18f, 18f, 18f));
        ConfigureSpriteImporter(circleBgPath, Vector4.zero);

        cardBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(cardBgPath);
        cardBorderSprite = AssetDatabase.LoadAssetAtPath<Sprite>(cardBorderPath);
        circleBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(circleBgPath);
    }

    private static void ConfigureSpriteImporter(string assetPath, Vector4 border)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        bool changed = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }
        if (importer.spriteBorder != border)
        {
            importer.spriteBorder = border;
            changed = true;
        }
        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }
        if (importer.filterMode != FilterMode.Bilinear)
        {
            importer.filterMode = FilterMode.Bilinear;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static void GenerateRoundedRectPng(string filePath, int width, int height, float radius, float strokeWidth, bool fillInside)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        Vector2 innerHalf = new Vector2(halfW - radius, halfH - radius);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - halfW, y + 0.5f - halfH);
                Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - innerHalf;
                float sdf = (new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f))).magnitude
                            + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;

                float alpha;
                if (fillInside)
                {
                    alpha = Mathf.Clamp01(0.5f - sdf);
                }
                else
                {
                    float outerAlpha = Mathf.Clamp01(0.5f - sdf);
                    float innerAlpha = Mathf.Clamp01(0.5f - (sdf + strokeWidth));
                    alpha = Mathf.Clamp01(outerAlpha - innerAlpha);
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        File.WriteAllBytes(filePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static void GenerateCirclePng(string filePath, int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size * 0.5f;
        float radius = center - 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                float alpha = Mathf.Clamp01(0.5f + radius - dist);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        File.WriteAllBytes(filePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static void BuildPauseMenuChild(Transform prefabRoot)
    {
        Transform existingPauseUi = prefabRoot.Find("PauseMenuUI");
        if (existingPauseUi != null)
        {
            Object.DestroyImmediate(existingPauseUi.gameObject);
        }

        EnsureInGamePauseMenuPrefab();
    }

    public static GameObject EnsureInGamePauseMenuPrefab()
    {
        UnityEngine.UIElements.PanelSettings panelSettings =
            AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(PausePanelSettingsPath);
        UnityEngine.UIElements.VisualTreeAsset visualTree =
            AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(PauseMenuUxmlPath);

        if (panelSettings == null || visualTree == null)
        {
            Debug.LogWarning(
                $"[GameHUDToolkitSetupTool] Assets do InGamePauseMenu ausentes (panelSettings={panelSettings != null}, visualTree={visualTree != null}).");
            return null;
        }

        GameObject pauseGo = new GameObject("InGamePauseMenu");
        try
        {
            UnityEngine.UIElements.UIDocument pauseDoc = pauseGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            pauseDoc.panelSettings = panelSettings;
            pauseDoc.visualTreeAsset = visualTree;
            pauseDoc.sortingOrder = 100f;

            InGamePauseMenuController pauseController = pauseGo.AddComponent<InGamePauseMenuController>();
            SerializedObject pauseSo = new SerializedObject(pauseController);
            pauseSo.FindProperty("pauseMenuDocument").objectReferenceValue = pauseDoc;
            pauseSo.ApplyModifiedPropertiesWithoutUndo();

            return PrefabUtility.SaveAsPrefabAsset(pauseGo, PauseMenuPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(pauseGo);
        }
    }
}
