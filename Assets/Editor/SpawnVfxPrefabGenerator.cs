#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SpawnVfxPrefabGenerator
{
    private static readonly string LogPrefix = "[SpawnVfxPrefabGenerator]";

    [InitializeOnLoadMethod]
    private static void AutoRunOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists("Assets/Prefab/FX/FX_Hunter_Falling_Flames.prefab") ||
                !File.Exists("Assets/Prefab/FX/FX_Hunter_Spawn_Impact.prefab") ||
                !File.Exists("Assets/Prefab/FX/FX_Player_Spawn.prefab"))
            {
                GenerateAllPrefabs();
            }
        };
    }

    [MenuItem("Tools/Generate Spawn VFX Prefabs")]
    public static void GenerateAllPrefabs()
    {
        EnsureDirectoriesExist();
        GenerateHunterFallingFlames();
        GenerateHunterSpawnImpact();
        GeneratePlayerSpawn();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"{LogPrefix} Todos os prefabs de Spawn VFX foram gerados com sucesso!");
    }

    private static void EnsureDirectoriesExist()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefab/FX"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefab"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefab");
            }
            AssetDatabase.CreateFolder("Assets/Prefab", "FX");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Prefab/Lamin"))
        {
            AssetDatabase.CreateFolder("Assets/Prefab", "Lamin");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
    }

    private static void GenerateHunterFallingFlames()
    {
        string sourcePath = "Assets/Prefab/FX/HunterFireball.prefab";
        string targetFx = "Assets/Prefab/FX/FX_Hunter_Falling_Flames.prefab";
        string targetLamin = "Assets/Prefab/Lamin/FX_Hunter_Falling_Flames.prefab";
        string targetResources = "Assets/Resources/FX_Hunter_Falling_Flames.prefab";

        GameObject sourceObj = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (sourceObj == null)
        {
            Debug.LogError($"{LogPrefix} Fonte nÃ£o encontrada: {sourcePath}");
            return;
        }

        GameObject instance = Object.Instantiate(sourceObj);
        instance.name = "FX_Hunter_Falling_Flames";

        // Remove scripts de projÃ©til e colisores
        var projectileComp = instance.GetComponent<HunterFireballProjectile>();
        if (projectileComp != null) Object.DestroyImmediate(projectileComp);

        var sphereCol = instance.GetComponent<SphereCollider>();
        if (sphereCol != null) Object.DestroyImmediate(sphereCol);

        var cols = instance.GetComponentsInChildren<Collider>();
        foreach (var c in cols) Object.DestroyImmediate(c);

        // Aumenta escala para envolver todo o corpo do Hunter (2.2x)
        instance.transform.localScale = new Vector3(2.2f, 2.2f, 2.2f);

        // Garante que todas as partÃ­culas fiquem em looping e ativas durante a queda
        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in particles)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        // Salva prefabs
        PrefabUtility.SaveAsPrefabAsset(instance, targetFx);
        PrefabUtility.SaveAsPrefabAsset(instance, targetLamin);
        PrefabUtility.SaveAsPrefabAsset(instance, targetResources);

        Object.DestroyImmediate(instance);
        Debug.Log($"{LogPrefix} FX_Hunter_Falling_Flames gerado com sucesso em {targetFx}");
    }

    private static void GenerateHunterSpawnImpact()
    {
        string weaponFxPath = "Assets/Eric VFX Studio/Free Game VFX/Prefab/FX_Weapon Effect.prefab";
        string fireHitPath = "Assets/Prefab/FX/FX_Fire_Hit_02.prefab";
        string targetFx = "Assets/Prefab/FX/FX_Hunter_Spawn_Impact.prefab";
        string targetLamin = "Assets/Prefab/Lamin/FX_Hunter_Spawn_Impact.prefab";
        string targetResources = "Assets/Resources/FX_Hunter_Spawn_Impact.prefab";

        GameObject weaponFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(weaponFxPath);
        GameObject fireHitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fireHitPath);

        if (weaponFxPrefab == null || fireHitPrefab == null)
        {
            Debug.LogError($"{LogPrefix} Fontes para HunterSpawnImpact nÃ£o encontradas!");
            return;
        }

        // Cria raiz do impacto
        GameObject root = new GameObject("FX_Hunter_Spawn_Impact");

        // 1. Instancia componentes de solo do FX_Weapon Effect (cratera, pedras, poeira, onda de choque)
        GameObject weaponInstance = Object.Instantiate(weaponFxPrefab, root.transform);
        weaponInstance.name = "GroundCraterEffects";
        weaponInstance.transform.localPosition = Vector3.zero;
        weaponInstance.transform.localRotation = Quaternion.identity;

        // Remove o objeto Weapon (o pique caindo) pois quem cai Ã© o prÃ³prio Hunter!
        Transform weaponChild = weaponInstance.transform.Find("Weapon");
        if (weaponChild != null)
        {
            Object.DestroyImmediate(weaponChild.gameObject);
        }

        // Remove o Animator do FX_Weapon Effect para que o impacto ocorra imediatamente em y=0
        var anim = weaponInstance.GetComponent<Animator>();
        if (anim != null) Object.DestroyImmediate(anim);

        // 2. Instancia a explosÃ£o de fogo do FX_Fire_Hit_02
        GameObject fireInstance = Object.Instantiate(fireHitPrefab, root.transform);
        fireInstance.name = "FireExplosionBurst";
        fireInstance.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        fireInstance.transform.localRotation = Quaternion.identity;
        fireInstance.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

        // Configura autodestruiÃ§Ã£o
        var psList = root.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in psList)
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = true;
        }

        PrefabUtility.SaveAsPrefabAsset(root, targetFx);
        PrefabUtility.SaveAsPrefabAsset(root, targetLamin);
        PrefabUtility.SaveAsPrefabAsset(root, targetResources);

        Object.DestroyImmediate(root);
        Debug.Log($"{LogPrefix} FX_Hunter_Spawn_Impact gerado com sucesso em {targetFx}");
    }

    private static void GeneratePlayerSpawn()
    {
        string purpleHitPath = "Assets/Eric VFX Studio/Free Game VFX/Prefab/FX_Purple_Hit_02.prefab";
        string targetFx = "Assets/Prefab/FX/FX_Player_Spawn.prefab";
        string targetLamin = "Assets/Prefab/Lamin/FX_Player_Spawn.prefab";
        string targetResources = "Assets/Resources/FX_Player_Spawn.prefab";

        GameObject purpleHitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(purpleHitPath);
        if (purpleHitPrefab == null)
        {
            Debug.LogError($"{LogPrefix} FX_Purple_Hit_02 nÃ£o encontrado!");
            return;
        }

        GameObject instance = Object.Instantiate(purpleHitPrefab);
        instance.name = "FX_Player_Spawn";

        // Ajusta parÃ¢metros para ser um surgimento suave e discreto (materializaÃ§Ã£o da alma)
        // Sem alterar a malha!
        var allPS = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in allPS)
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = true;
            main.duration = 1.0f;
            main.startLifetime = Mathf.Clamp(main.startLifetime.constant * 0.7f, 0.4f, 1.2f);
            main.startSpeed = Mathf.Clamp(main.startSpeed.constant * 0.4f, 0.2f, 1.5f);
            main.simulationSpeed = 1.1f;

            var emission = ps.emission;
            // Reduz explosÃ£o brusca para um efeito mais calmo de surgimento
            if (ps.name.Contains("hit") || ps.name.Contains("Ray"))
            {
                var col = main.startColor;
                Color c = col.color;
                c.a = 0.45f;
                col.color = c;
                main.startColor = col;
            }
        }

        instance.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);

        PrefabUtility.SaveAsPrefabAsset(instance, targetFx);
        PrefabUtility.SaveAsPrefabAsset(instance, targetLamin);
        PrefabUtility.SaveAsPrefabAsset(instance, targetResources);

        Object.DestroyImmediate(instance);
        Debug.Log($"{LogPrefix} FX_Player_Spawn gerado com sucesso em {targetFx}");
    }
}
#endif
