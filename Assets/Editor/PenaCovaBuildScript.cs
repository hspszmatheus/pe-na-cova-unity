using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PenaCovaBuildScript
{
    private const string GameName = "Pena Cova";
    private const string IconPath = "Assets/UI/Sprites/HunterLifeIcon.png";
    private const string DesktopFolder = "C:/Users/hspsz/Desktop/Pena Cova";
    private const string ExePath = "C:/Users/hspsz/Desktop/Pena Cova/Pena Cova.exe";

    [MenuItem("Build/Configure Pena Cova Settings")]
    public static void ConfigureSettings()
    {
        PlayerSettings.productName = GameName;

        // Carrega o ícone da cabeça da morte
        Texture2D iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (iconTex != null)
        {
            TextureImporter importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            }

            // Standalone Windows exige exatamente 8 ícones (para os tamanhos 16, 32, 48, 64, 128, 256, etc.)
            Texture2D[] standaloneIcons = new Texture2D[8];
            for (int i = 0; i < 8; i++)
            {
                standaloneIcons[i] = iconTex;
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, standaloneIcons, IconKind.Application);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new Texture2D[] { iconTex }, IconKind.Application);
#pragma warning disable CS0618
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, standaloneIcons);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new Texture2D[] { iconTex });
#pragma warning restore CS0618

            Debug.Log($"[PenaCovaBuildScript] Ícone da cabeça da morte '{IconPath}' aplicado com sucesso ao jogo '{GameName}'!");
        }
        else
        {
            Debug.LogWarning($"[PenaCovaBuildScript] Ícone não encontrado em '{IconPath}'.");
        }

        // Desativação total de todas as splash screens (nativa da Unity e personalizada), a pedido do usuário
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];
        try
        {
            PlayerSettings.SplashScreen.showUnityLogo = false;
        }
        catch { }

        Debug.Log("[PenaCovaBuildScript] Splash screens (nativa da Unity e personalizadas) totalmente desativadas!");

        AssetDatabase.SaveAssets();
    }

    [MenuItem("Build/Build Pena Cova")]
    public static void BuildGame()
    {
        ConfigureSettings();

        if (!Directory.Exists(DesktopFolder))
        {
            Directory.CreateDirectory(DesktopFolder);
        }

        string[] scenes = new string[]
        {
            "Assets/Scenes/JoinByBrowsing.unity",
            "Assets/Scenes/GameScene.unity"
        };

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = ExePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log($"[PenaCovaBuildScript] Iniciando Build de '{GameName}' em '{ExePath}'...");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[PenaCovaBuildScript] BUILD SUCESSO! Tamanho: {summary.totalSize / (1024 * 1024)} MB, Tempo: {summary.totalTime.TotalSeconds:F1}s.");
        }
        else if (summary.result == BuildResult.Failed)
        {
            Debug.LogError($"[PenaCovaBuildScript] BUILD FALHOU com {summary.totalErrors} erros.");
        }
        else
        {
            Debug.LogWarning($"[PenaCovaBuildScript] Build finalizado com resultado: {summary.result}");
        }
    }
}
