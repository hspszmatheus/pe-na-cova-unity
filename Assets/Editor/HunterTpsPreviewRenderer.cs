#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class HunterTpsPreviewRenderer
{
    [MenuItem("Tools/Render Hunter TPS Aim Comparison")]
    public static void RenderHunterTpsAimComparison()
    {
        string artifactDir = "C:/Users/hspsz/.gemini/antigravity/brain/8073acce-8ef1-4abc-b0fe-a079acb56ed2";
        CaptureHunterTpsState("Assets/Prefab/Hunter.prefab", System.IO.Path.Combine(artifactDir, "hunter_normal_centered.png"), isAiming: false, shoulderOffset: 0f);
        CaptureHunterTpsState("Assets/Prefab/Hunter.prefab", System.IO.Path.Combine(artifactDir, "hunter_aim_offset_0.55.png"), isAiming: true, shoulderOffset: 0.55f);
        CaptureHunterTpsState("Assets/Prefab/Hunter.prefab", System.IO.Path.Combine(artifactDir, "hunter_aim_offset_0.72.png"), isAiming: true, shoulderOffset: 0.72f);
        CaptureHunterTpsState("Assets/Prefab/Hunter.prefab", System.IO.Path.Combine(artifactDir, "hunter_aim_offset_0.85.png"), isAiming: true, shoulderOffset: 0.85f);
        Debug.Log("[HunterTpsPreviewRenderer] Previews TPS do Hunter gerados com sucesso!");
    }

    private static void CaptureHunterTpsState(string prefabPath, string outPath, bool isAiming, float shoulderOffset = 0.72f)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[HunterTpsPreviewRenderer] Prefab não encontrado em {prefabPath}");
            return;
        }

        GameObject inst = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) cam.enabled = false;

        // Atualizar o Animator para o personagem sair do T-pose e ficar na pose real de jogo
        Animator anim = inst.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.Update(0.5f);
        }

        // Fundo e alvo para demonstrar a linha de visao desobstruida do TPS
        GameObject env = new GameObject("TpsEnv");
        CreateEnvCube(new Vector3(0f, -0.05f, 8f), new Vector3(20f, 0.1f, 25f), new Color(0.24f, 0.27f, 0.32f), env.transform);
        // Alvo inimigo ao centro (para demonstrar a mira perfeitamente alinhada)
        CreateEnvCube(new Vector3(0f, 1.5f, 12f), new Vector3(0.6f, 3f, 0.6f), new Color(0.95f, 0.25f, 0.25f), env.transform);
        CreateEnvCube(new Vector3(0f, 3.2f, 12f), new Vector3(0.5f, 0.5f, 0.5f), new Color(1f, 0.8f, 0.2f), env.transform);
        // Pilares de referencia espacial
        CreateEnvCube(new Vector3(-4.5f, 2f, 6f), new Vector3(0.8f, 4f, 0.8f), new Color(0.35f, 0.55f, 0.75f), env.transform);
        CreateEnvCube(new Vector3(4.5f, 2f, 6f), new Vector3(0.8f, 4f, 0.8f), new Color(0.75f, 0.65f, 0.35f), env.transform);
        CreateEnvCube(new Vector3(0f, 4f, 18f), new Vector3(20f, 8f, 0.5f), new Color(0.18f, 0.20f, 0.24f), env.transform);

        GameObject lightObj = new GameObject("PreviewLight");
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        GameObject camObj = new GameObject("TpsCaptureCam");
        Camera c = camObj.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);

        if (isAiming)
        {
            // TPS Over-The-Shoulder:
            // Camera deslocada sobre o ombro direito (+shoulderOffset em X), mais proxima e com zoom
            c.fieldOfView = 48f;
            // A camera aponta para a frente (+Z) mirando no horizonte/alvo central
            // Com um leve pitch de 5 graus para enquadramento equilibrado
            camObj.transform.position = new Vector3(shoulderOffset, 1.62f, -2.40f);
            camObj.transform.rotation = Quaternion.Euler(5.0f, 0f, 0f);
        }
        else
        {
            // Modo Normal (Centralizado estilo Genshin Impact):
            c.fieldOfView = 65f;
            camObj.transform.position = new Vector3(0f, 2.15f, -3.52f);
            camObj.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        }

        // Render em 16:9 (1280x720) como padrão em telas modernas de gameplay
        int width = 1280;
        int height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24);
        c.targetTexture = rt;
        c.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);

        // Se estiver mirando, desenhar o reticulo de mira (crosshair) exatamente no centro da tela
        if (isAiming)
        {
            DrawCrosshairOnTexture(tex, width / 2, height / 2);
        }

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

    private static void DrawCrosshairOnTexture(Texture2D tex, int cx, int cy)
    {
        Color crossColor = new Color(1f, 0.95f, 0.85f, 1f);
        Color dotColor = new Color(1f, 0.25f, 0.25f, 1f);
        Color shadowColor = new Color(0f, 0f, 0f, 0.6f);

        // Ponto central (Dot)
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                tex.SetPixel(cx + x, cy + y, dotColor);
            }
        }

        // Reticulo de 4 linhas (Norte, Sul, Leste, Oeste) com gap de 8px e tamanho 12px
        int gap = 8;
        int len = 12;
        int thickness = 2;

        // Linha Oeste (-X)
        DrawLineThick(tex, cx - gap - len, cy, cx - gap, cy, thickness, crossColor, shadowColor);
        // Linha Leste (+X)
        DrawLineThick(tex, cx + gap, cy, cx + gap + len, cy, thickness, crossColor, shadowColor);
        // Linha Sul (-Y)
        DrawLineThick(tex, cx, cy - gap - len, cx, cy - gap, thickness, crossColor, shadowColor);
        // Linha Norte (+Y)
        DrawLineThick(tex, cx, cy + gap, cx, cy + gap + len, thickness, crossColor, shadowColor);
    }

    private static void DrawLineThick(Texture2D tex, int x0, int y0, int x1, int y1, int thickness, Color col, Color shadow)
    {
        for (int t = -thickness / 2; t <= thickness / 2; t++)
        {
            if (y0 == y1) // Linha horizontal
            {
                for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++)
                {
                    tex.SetPixel(x, y0 + t + 1, shadow);
                    tex.SetPixel(x, y0 + t, col);
                }
            }
            else // Linha vertical
            {
                for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++)
                {
                    tex.SetPixel(x0 + t + 1, y, shadow);
                    tex.SetPixel(x0 + t, y, col);
                }
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
