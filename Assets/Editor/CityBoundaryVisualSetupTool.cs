#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LineworkLite.FreeOutline;

public static class CityBoundaryVisualSetupTool
{
    private const string MaterialPath = "Assets/Materials/CityBoundaryDottedBarrier.mat";
    private const string SettingsPath = "Assets/Settings/TaskOutlineSettings.asset";
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("Tools/Setup City Boundary Visuals")]
    public static void SetupCityBoundaryVisuals()
    {
        // 0. Garante que a cena ativa seja a GameScene
        if (SceneManager.GetActiveScene().path != GameScenePath)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(GameScenePath);
            }
        }

        // 1. Localiza o objeto Enviroment e CityBoundaries
        GameObject env = GameObject.Find("Enviroment");
        if (env == null)
        {
            Debug.LogError("[CityBoundaryVisualSetup] Objeto 'Enviroment' não encontrado na cena!");
            return;
        }

        Transform boundariesTrans = env.transform.Find("CityBoundaries");
        if (boundariesTrans == null)
        {
            Debug.LogWarning("[CityBoundaryVisualSetup] 'CityBoundaries' não encontrado. Criando limites físicos primeiro...");
            CityBoundaryTool.CreateBoundariesFromReference();
            boundariesTrans = env.transform.Find("CityBoundaries");
            if (boundariesTrans == null)
            {
                Debug.LogError("[CityBoundaryVisualSetup] Falha ao criar/localizar CityBoundaries.");
                return;
            }
        }

        // 2. Carrega o material do shader de bolinhas
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Debug.LogError($"[CityBoundaryVisualSetup] Material '{MaterialPath}' não encontrado!");
            return;
        }

        // 3. Adiciona ou obtém o componente CityBoundaryVisualController
        CityBoundaryVisualController controller = boundariesTrans.GetComponent<CityBoundaryVisualController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<CityBoundaryVisualController>(boundariesTrans.gameObject);
        }

        // 4. Constrói os painéis visuais para toda a extensão 3D (paredes de 40m + teto no céu)
        controller.BuildAllVisualBoundaries(mat);

        // 5. Valida a configuração do LineworkLite (TaskOutlineSettings.asset)
        FreeOutlineSettings settings = AssetDatabase.LoadAssetAtPath<FreeOutlineSettings>(SettingsPath);
        if (settings != null)
        {
            bool hasBoundaryOutline = false;
            foreach (var outline in settings.Outlines)
            {
                if (outline != null && (outline.name.Contains("Boundary") || outline.RenderingLayer == (RenderingLayerMask)32))
                {
                    hasBoundaryOutline = true;
                    // Garante que a oclusão seja WhenNotOccluded (não visível através de paredes)
                    outline.occlusion = LineworkLite.Common.Utils.Occlusion.WhenNotOccluded;
                    outline.renderQueue = LineworkLite.Common.Utils.OutlineRenderQueue.OpaqueAndTransparent;
                    // Cor cinza prateada padrão idêntica aos contornos de Task
                    outline.color = new Color(0.72f, 0.72f, 0.78f, 0.95f);
                    outline.occludedColor = new Color(0.72f, 0.72f, 0.78f, 0.95f);
                    outline.width = 2.4f;
                    EditorUtility.SetDirty(outline);
                    break;
                }
            }

            if (!hasBoundaryOutline)
            {
                Debug.LogWarning("[CityBoundaryVisualSetup] Outline de Boundary não encontrado na lista do settings. Verifique TaskOutlineSettings.asset.");
            }
            else
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log("[CityBoundaryVisualSetup] ✅ LineworkLite TaskOutlineSettings validado com CityBoundaryOutline (Bit 5 / 32, ZTest WhenNotOccluded, Estilo Cinza Task)!");
            }
        }

        // 6. Salva a cena ativa
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[CityBoundaryVisualSetup] ✅ Configuração 3D dos limites da cidade (paredes de 40m + teto do céu + bolinhas cinzas finas) concluída e salva com sucesso!");
    }
}
#endif
