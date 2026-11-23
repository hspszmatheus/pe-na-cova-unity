#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class AnimationInspector
{
    [MenuItem("Tools/Test Magic Animation Angles")]
    public static void TestMagicAnimationAngles()
    {
        string fbxPath = "Assets/Animations/Standing 1H Magic Attack 01.fbx";
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("Importer not found for " + fbxPath);
            return;
        }

        // Test orientations: Body Orientation (keepOriginalOrientation = false)
        // and Original Orientation with various offsets
        float[] testAngles = new float[] { -60f, -45f, -30f, 0f, 30f, 45f, 60f };
        string artifactDir = "C:/Users/hspsz/.gemini/antigravity/brain/8073acce-8ef1-4abc-b0fe-a079acb56ed2";

        // 1. Test Body Orientation (keepOriginalOrientation = false)
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }
        var clip = clips[0];
        clip.name = "Standing 1H Magic Attack 01";
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = false; // Body Orientation
        clip.rotationOffset = 0f;
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalPositionXZ = true;
        importer.clipAnimations = new ModelImporterClipAnimation[] { clip };
        importer.SaveAndReimport();

        RenderMagicFrame(System.IO.Path.Combine(artifactDir, "magic_body_orient.png"), 0.40f);

        // 2. Test Original with offsets
        foreach (float angle in testAngles)
        {
            clips = importer.clipAnimations;
            clip = clips[0];
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.rotationOffset = angle;
            importer.clipAnimations = new ModelImporterClipAnimation[] { clip };
            importer.SaveAndReimport();

            RenderMagicFrame(System.IO.Path.Combine(artifactDir, $"magic_orig_offset_{angle:+00;-00;0}.png"), 0.40f);
        }

        Debug.Log("[AnimationInspector] All orientation tests rendered!");
    }

    private static void RenderMagicFrame(string outPath, float normalizedTime)
    {
        string hunterPrefabPath = "Assets/Prefab/Hunter.prefab";
        GameObject hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hunterPrefabPath);
        if (hunterPrefab == null) return;

        GameObject inst = Object.Instantiate(hunterPrefab, Vector3.zero, Quaternion.identity);
        foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) cam.enabled = false;

        Animator anim = inst.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.Play("Hunter Fireball Attack", 0, normalizedTime);
            anim.Update(0f);
        }

        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        lightObj.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        GameObject env = new GameObject("TpsEnv");
        CreateEnvCube(new Vector3(0f, -0.05f, 6f), new Vector3(12f, 0.1f, 18f), new Color(0.24f, 0.27f, 0.32f), env.transform);
        // Target pillar straight forward at Z = 8
        CreateEnvCube(new Vector3(0f, 1.5f, 8f), new Vector3(0.5f, 3f, 0.5f), new Color(0.95f, 0.25f, 0.25f), env.transform);

        GameObject camObj = new GameObject("CaptureCam");
        Camera c = camObj.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
        c.fieldOfView = 48f;
        // View from behind the character looking straight forward along +Z
        camObj.transform.position = new Vector3(0f, 1.7f, -3.0f);
        camObj.transform.rotation = Quaternion.Euler(6f, 0f, 0f);

        int width = 800;
        int height = 600;
        RenderTexture rt = new RenderTexture(width, height, 24);
        c.targetTexture = rt;
        c.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);

        DrawCrosshairOnTexture(tex, width / 2, height / 2);
        tex.Apply();

        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());

        c.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camObj);
        Object.DestroyImmediate(lightObj);
        Object.DestroyImmediate(env);
        Object.DestroyImmediate(inst);
    }

    [MenuItem("Tools/Apply Final Magic Animation Fix")]
    public static void ApplyFinalMagicAnimationFix()
    {
        string fbxPath = "Assets/Animations/Standing 1H Magic Attack 01.fbx";
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        var clip = clips[0];
        clip.name = "Standing 1H Magic Attack 01";
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = true;
        clip.rotationOffset = 25f; // Perfeita orientação frontal para a mão direita e tronco
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalPositionXZ = true;
        importer.clipAnimations = new ModelImporterClipAnimation[] { clip };
        importer.SaveAndReimport();

        string artifactDir = "C:/Users/hspsz/.gemini/antigravity/brain/8073acce-8ef1-4abc-b0fe-a079acb56ed2";

        string hunterPrefabPath = "Assets/Prefab/Hunter.prefab";
        GameObject hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hunterPrefabPath);
        if (hunterPrefab == null) return;

        GameObject inst = Object.Instantiate(hunterPrefab, Vector3.zero, Quaternion.identity);
        foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) cam.enabled = false;

        Animator anim = inst.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.Play("Hunter Fireball Attack", 0, 0.36f); // Momento do disparo do feitiço
            anim.Update(0f);
        }

        // Renderiza a visão final em terceira pessoa com mira (TPS)
        RenderCustomCam(System.IO.Path.Combine(artifactDir, "final_magic_cast_tps.png"),
            new Vector3(0.45f, 1.65f, -2.4f), Quaternion.Euler(6f, -2.5f, 0f));

        // Renderiza a visão traseira central
        RenderCustomCam(System.IO.Path.Combine(artifactDir, "final_magic_cast_back.png"),
            new Vector3(0f, 1.7f, -3.0f), Quaternion.Euler(6f, 0f, 0f));

        // Renderiza a visão superior (top-down)
        RenderCustomCam(System.IO.Path.Combine(artifactDir, "final_magic_cast_top.png"),
            new Vector3(0f, 5.0f, 0.5f), Quaternion.Euler(85f, 0f, 0f));

        Object.DestroyImmediate(inst);

        Debug.Log("[AnimationInspector] Apply Final Magic Animation Fix concluído!");
    }

    private static void RenderCustomCam(string outPath, Vector3 pos, Quaternion rot)
    {
        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        lightObj.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        GameObject env = new GameObject("TpsEnv");
        CreateEnvCube(new Vector3(0f, -0.05f, 6f), new Vector3(12f, 0.1f, 18f), new Color(0.24f, 0.27f, 0.32f), env.transform);
        CreateEnvCube(new Vector3(0f, 1.5f, 8f), new Vector3(0.5f, 3f, 0.5f), new Color(0.95f, 0.25f, 0.25f), env.transform);

        GameObject camObj = new GameObject("CaptureCam");
        Camera c = camObj.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
        c.fieldOfView = 48f;
        camObj.transform.position = pos;
        camObj.transform.rotation = rot;

        int width = 800;
        int height = 600;
        RenderTexture rt = new RenderTexture(width, height, 24);
        c.targetTexture = rt;
        c.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);

        DrawCrosshairOnTexture(tex, width / 2, height / 2);
        tex.Apply();

        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());

        c.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camObj);
        Object.DestroyImmediate(lightObj);
        Object.DestroyImmediate(env);
    }

    private static void RenderCamSnapshot(string outPath)
    {
        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        lightObj.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        GameObject env = new GameObject("TpsEnv");
        CreateEnvCube(new Vector3(0f, -0.05f, 6f), new Vector3(12f, 0.1f, 18f), new Color(0.24f, 0.27f, 0.32f), env.transform);
        CreateEnvCube(new Vector3(0f, 1.5f, 8f), new Vector3(0.5f, 3f, 0.5f), new Color(0.95f, 0.25f, 0.25f), env.transform);

        GameObject camObj = new GameObject("CaptureCam");
        Camera c = camObj.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
        c.fieldOfView = 48f;
        camObj.transform.position = new Vector3(0f, 1.7f, -3.0f);
        camObj.transform.rotation = Quaternion.Euler(6f, 0f, 0f);

        int width = 800;
        int height = 600;
        RenderTexture rt = new RenderTexture(width, height, 24);
        c.targetTexture = rt;
        c.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);

        DrawCrosshairOnTexture(tex, width / 2, height / 2);
        tex.Apply();

        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());

        c.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camObj);
        Object.DestroyImmediate(lightObj);
        Object.DestroyImmediate(env);
    }

    private static void DrawCrosshairOnTexture(Texture2D tex, int cx, int cy)
    {
        Color dotColor = new Color(1f, 0.25f, 0.25f, 1f);
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                tex.SetPixel(cx + x, cy + y, dotColor);
            }
        }
    }

    private static void CreateEnvCube(Vector3 pos, Vector3 scale, Color col, Transform parent)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = pos;
        cube.transform.localScale = scale;
        cube.transform.SetParent(parent);
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = col;
        cube.GetComponent<Renderer>().material = mat;
    }
}
#endif
