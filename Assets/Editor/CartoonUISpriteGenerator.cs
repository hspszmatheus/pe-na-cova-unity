using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates crisp 9-slice Casual Cartoon GUI sprites modeled after the FCCartoonGUI aesthetic.
/// Kept in Assets/UI/Sprites/Cartoon so original FCCartoonGUI_UIAnimation assets stay untouched.
/// </summary>
public static class CartoonUISpriteGenerator
{
    private const string OutputDir = "Assets/UI/Sprites/Cartoon";

    [MenuItem("Tools/Lobby UI/Generate Cartoon GUI Sprites")]
    public static void GenerateAllSprites()
    {
        if (!Directory.Exists(OutputDir))
            Directory.CreateDirectory(OutputDir);

        // Buttons (128x64, generous pill 9-slice borders)
        var btnBorder = new Vector4(28, 28, 28, 28);

        // Orange primary (FCCartoonGUI big_button_o_40 style: shadow RGBA 0.855, 0.494, 0.169)
        SaveSlicedSprite("cartoon_btn_orange.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(158, 72, 16, 255),
                lip: new Color32(218, 126, 43, 255),
                topGrad: new Color32(255, 193, 62, 255),
                botGrad: new Color32(255, 145, 28, 255),
                shineAlpha: 0.42f, borderThickness: 3.5f, lipHeight: 7f));

        SaveSlicedSprite("cartoon_btn_orange_hover.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(170, 80, 18, 255),
                lip: new Color32(228, 138, 52, 255),
                topGrad: new Color32(255, 212, 92, 255),
                botGrad: new Color32(255, 164, 48, 255),
                shineAlpha: 0.52f, borderThickness: 3.5f, lipHeight: 7f));

        SaveSlicedSprite("cartoon_btn_orange_pressed.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(142, 62, 14, 255),
                lip: new Color32(198, 108, 30, 255),
                topGrad: new Color32(236, 152, 34, 255),
                botGrad: new Color32(225, 125, 20, 255),
                shineAlpha: 0.22f, borderThickness: 3.5f, lipHeight: 4f));

        // Green CTA (LogInCanvas green style: shadow 0.373, 0.608, 0.000)
        SaveSlicedSprite("cartoon_btn_green.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(46, 105, 8, 255),
                lip: new Color32(72, 148, 12, 255),
                topGrad: new Color32(138, 224, 48, 255),
                botGrad: new Color32(95, 188, 24, 255),
                shineAlpha: 0.42f, borderThickness: 3.5f, lipHeight: 7f));

        SaveSlicedSprite("cartoon_btn_green_hover.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(52, 118, 10, 255),
                lip: new Color32(84, 166, 18, 255),
                topGrad: new Color32(158, 238, 72, 255),
                botGrad: new Color32(114, 204, 38, 255),
                shineAlpha: 0.52f, borderThickness: 3.5f, lipHeight: 7f));

        // Blue button (FCCartoonGUI big_button_b_43 style: shadow 0.165, 0.424, 0.741)
        SaveSlicedSprite("cartoon_btn_blue.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(26, 72, 138, 255),
                lip: new Color32(42, 108, 189, 255),
                topGrad: new Color32(92, 190, 255, 255),
                botGrad: new Color32(46, 142, 236, 255),
                shineAlpha: 0.42f, borderThickness: 3.5f, lipHeight: 7f));

        SaveSlicedSprite("cartoon_btn_blue_hover.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(30, 82, 152, 255),
                lip: new Color32(54, 124, 205, 255),
                topGrad: new Color32(118, 206, 255, 255),
                botGrad: new Color32(68, 160, 248, 255),
                shineAlpha: 0.52f, borderThickness: 3.5f, lipHeight: 7f));

        // Red danger button (Leave Session)
        SaveSlicedSprite("cartoon_btn_red.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(138, 32, 34, 255),
                lip: new Color32(196, 48, 52, 255),
                topGrad: new Color32(255, 112, 104, 255),
                botGrad: new Color32(232, 68, 68, 255),
                shineAlpha: 0.40f, borderThickness: 3.5f, lipHeight: 7f));

        SaveSlicedSprite("cartoon_btn_red_hover.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(150, 38, 40, 255),
                lip: new Color32(210, 62, 66, 255),
                topGrad: new Color32(255, 134, 126, 255),
                botGrad: new Color32(246, 86, 86, 255),
                shineAlpha: 0.50f, borderThickness: 3.5f, lipHeight: 7f));

        // Disabled button (big_button_inactive_46 style)
        SaveSlicedSprite("cartoon_btn_disabled.png", 128, 64, btnBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 22f,
                border: new Color32(138, 138, 142, 255),
                lip: new Color32(174, 174, 180, 255),
                topGrad: new Color32(216, 216, 222, 255),
                botGrad: new Color32(194, 194, 200, 255),
                shineAlpha: 0.20f, borderThickness: 3.0f, lipHeight: 6f));

        // Cream cartoon modal card (LogInPanel / PausePanel style)
        var panelBorder = new Vector4(30, 30, 30, 30);
        SaveSlicedSprite("cartoon_panel_cream.png", 128, 128, panelBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 26f,
                border: new Color32(84, 56, 36, 255),
                lip: new Color32(216, 194, 162, 255),
                topGrad: new Color32(255, 251, 240, 255),
                botGrad: new Color32(248, 236, 214, 255),
                shineAlpha: 0.50f, borderThickness: 4.5f, lipHeight: 9f));

        // Header ribbons (green + blue)
        var ribbonBorder = new Vector4(26, 22, 26, 22);
        SaveSlicedSprite("cartoon_ribbon_green.png", 128, 64, ribbonBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 18f,
                border: new Color32(46, 102, 10, 255),
                lip: new Color32(72, 144, 14, 255),
                topGrad: new Color32(136, 222, 46, 255),
                botGrad: new Color32(94, 186, 22, 255),
                shineAlpha: 0.42f, borderThickness: 3.5f, lipHeight: 6f));

        SaveSlicedSprite("cartoon_ribbon_blue.png", 128, 64, ribbonBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 18f,
                border: new Color32(26, 72, 138, 255),
                lip: new Color32(42, 108, 189, 255),
                topGrad: new Color32(92, 190, 255, 255),
                botGrad: new Color32(48, 144, 236, 255),
                shineAlpha: 0.42f, borderThickness: 3.5f, lipHeight: 6f));

        // Input fields (recessed warm sand/cream)
        var inputBorder = new Vector4(20, 20, 20, 20);
        SaveSlicedSprite("cartoon_input_bg.png", 128, 64, inputBorder, (x, y, w, h) =>
            DrawRecessedBox(x, y, w, h, 16f,
                border: new Color32(142, 114, 86, 255),
                topShadow: new Color32(214, 197, 170, 255),
                fill: new Color32(246, 237, 220, 255),
                borderThickness: 3.0f));

        SaveSlicedSprite("cartoon_input_focus.png", 128, 64, inputBorder, (x, y, w, h) =>
            DrawRecessedBox(x, y, w, h, 16f,
                border: new Color32(236, 140, 26, 255),
                topShadow: new Color32(234, 214, 182, 255),
                fill: new Color32(255, 249, 236, 255),
                borderThickness: 3.5f));

        // List container + row cards
        var listBorder = new Vector4(22, 22, 22, 22);
        SaveSlicedSprite("cartoon_list_bg.png", 128, 128, listBorder, (x, y, w, h) =>
            DrawRecessedBox(x, y, w, h, 18f,
                border: new Color32(178, 154, 124, 255),
                topShadow: new Color32(222, 208, 184, 255),
                fill: new Color32(242, 232, 212, 255),
                borderThickness: 3.0f));

        var rowBorder = new Vector4(18, 18, 18, 18);
        SaveSlicedSprite("cartoon_list_item.png", 128, 64, rowBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 15f,
                border: new Color32(198, 178, 148, 255),
                lip: new Color32(228, 214, 190, 255),
                topGrad: new Color32(255, 252, 244, 255),
                botGrad: new Color32(250, 242, 228, 255),
                shineAlpha: 0.30f, borderThickness: 2.5f, lipHeight: 4f));

        SaveSlicedSprite("cartoon_list_item_selected.png", 128, 64, rowBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 15f,
                border: new Color32(34, 108, 188, 255),
                lip: new Color32(46, 128, 214, 255),
                topGrad: new Color32(102, 196, 255, 255),
                botGrad: new Color32(62, 158, 244, 255),
                shineAlpha: 0.38f, borderThickness: 2.5f, lipHeight: 4f));

        // Slider track + knob
        SaveSlicedSprite("cartoon_slider_track.png", 128, 32, new Vector4(14, 10, 14, 10), (x, y, w, h) =>
            DrawRecessedBox(x, y, w, h, 12f,
                border: new Color32(132, 104, 78, 255),
                topShadow: new Color32(196, 178, 150, 255),
                fill: new Color32(226, 212, 188, 255),
                borderThickness: 2.5f));

        SaveSlicedSprite("cartoon_slider_knob.png", 48, 48, Vector4.zero, DrawSliderKnob);
        SaveSlicedSprite("cartoon_icon_copy.png", 64, 64, Vector4.zero, DrawCopyIcon);
        SaveSlicedSprite("cartoon_icon_sound_on.png", 64, 64, Vector4.zero, DrawSoundOnIcon);
        SaveSlicedSprite("cartoon_icon_sound_off.png", 64, 64, Vector4.zero, DrawSoundOffIcon);

        // In-Match HUD Panels, Ribbons, Medallions & Icons
        SaveSlicedSprite("cartoon_panel_hunter.png", 128, 128, panelBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 26f,
                border: new Color32(42, 16, 18, 255),
                lip: new Color32(82, 28, 32, 255),
                topGrad: new Color32(66, 36, 40, 255),
                botGrad: new Color32(48, 22, 26, 255),
                shineAlpha: 0.22f, borderThickness: 4.5f, lipHeight: 9f));

        SaveSlicedSprite("cartoon_ribbon_red.png", 128, 64, ribbonBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 18f,
                border: new Color32(124, 24, 28, 255),
                lip: new Color32(182, 42, 46, 255),
                topGrad: new Color32(252, 102, 92, 255),
                botGrad: new Color32(216, 52, 54, 255),
                shineAlpha: 0.42f, borderThickness: 3.5f, lipHeight: 6f));

        SaveSlicedSprite("cartoon_ribbon_orange.png", 128, 64, ribbonBorder, (x, y, w, h) =>
            DrawGlossyBevelBox(x, y, w, h, 18f,
                border: new Color32(148, 66, 14, 255),
                lip: new Color32(210, 118, 36, 255),
                topGrad: new Color32(255, 198, 64, 255),
                botGrad: new Color32(248, 138, 24, 255),
                shineAlpha: 0.45f, borderThickness: 3.5f, lipHeight: 6f));

        SaveSlicedSprite("cartoon_circle_cream.png", 128, 128, Vector4.zero, (x, y, w, h) =>
            DrawGlossyCircle(x, y, w, h,
                border: new Color32(84, 56, 36, 255),
                lip: new Color32(216, 194, 162, 255),
                topGrad: new Color32(255, 251, 240, 255),
                botGrad: new Color32(248, 236, 214, 255),
                shineAlpha: 0.45f, borderThickness: 5.5f, lipThickness: 9f));

        SaveSlicedSprite("cartoon_circle_hunter.png", 128, 128, Vector4.zero, (x, y, w, h) =>
            DrawGlossyCircle(x, y, w, h,
                border: new Color32(42, 16, 18, 255),
                lip: new Color32(118, 34, 38, 255),
                topGrad: new Color32(72, 36, 40, 255),
                botGrad: new Color32(44, 20, 24, 255),
                shineAlpha: 0.28f, borderThickness: 5.5f, lipThickness: 9f));

        SaveSlicedSprite("cartoon_icon_clock.png", 64, 64, Vector4.zero, DrawClockIcon);
        SaveSlicedSprite("cartoon_icon_survivors.png", 64, 64, Vector4.zero, DrawSurvivorsIcon);
        SaveSlicedSprite("cartoon_icon_skull.png", 64, 64, Vector4.zero, DrawSkullIcon);
        SaveSlicedSprite("cartoon_icon_fireball.png", 64, 64, Vector4.zero, DrawFireballIcon);
        SaveSlicedSprite("cartoon_icon_kick.png", 64, 64, Vector4.zero, DrawKickIcon);
        SaveSlicedSprite("cartoon_icon_scythe.png", 64, 64, Vector4.zero, DrawScytheIcon);
        SaveSlicedSprite("cartoon_border_rounded.png", 128, 128, panelBorder, DrawRoundedCardBorder);

        AssetDatabase.Refresh();
        Debug.Log("[CartoonUISpriteGenerator] Generated all cartoon UI sprites in " + OutputDir);
    }

    private delegate Color32 PixelShader(int x, int y, int w, int h);

    private static void SaveSlicedSprite(string fileName, int width, int height, Vector4 border, PixelShader shader)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = shader(x, y, width, height);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        string path = Path.Combine(OutputDir, fileName).Replace("\\", "/");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static float RoundedRectSDF(float px, float py, float cx, float cy, float halfW, float halfH, float radius)
    {
        float dx = Mathf.Abs(px - cx) - (halfW - radius);
        float dy = Mathf.Abs(py - cy) - (halfH - radius);
        float outDist = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
        float inDist = Mathf.Min(Mathf.Max(dx, dy), 0f);
        return outDist + inDist - radius;
    }

    private static Color32 DrawGlossyBevelBox(
        int px, int py, int w, int h, float radius,
        Color32 border, Color32 lip, Color32 topGrad, Color32 botGrad,
        float shineAlpha, float borderThickness, float lipHeight)
    {
        float pad = 2.0f;
        float cx = (w - 1) * 0.5f;
        float cy = (h - 1) * 0.5f;
        float hw = w * 0.5f - pad;
        float hh = h * 0.5f - pad;

        float distOuter = RoundedRectSDF(px, py, cx, cy, hw, hh, radius);
        if (distOuter > 1.0f) return new Color32(0, 0, 0, 0);

        float alpha = Mathf.Clamp01(1.0f - (distOuter + 0.5f));
        if (distOuter <= 0f) alpha = 1f;

        float distInner = distOuter + borderThickness;
        Color col;

        if (distInner > 0f)
        {
            col = border;
        }
        else
        {
            // Inside the bordered box. Note Unity texture y=0 is bottom!
            float innerBottom = pad + borderThickness;
            float innerTop = h - pad - borderThickness;
            float tY = Mathf.InverseLerp(innerBottom, innerTop, py);

            if (py < innerBottom + lipHeight)
            {
                // 3D cartoon bottom bevel lip
                col = lip;
            }
            else
            {
                float faceT = Mathf.InverseLerp(innerBottom + lipHeight, innerTop, py);
                col = Color.Lerp(botGrad, topGrad, faceT);

                // Subtle glossy upper band highlight
                if (faceT > 0.58f && faceT < 0.93f)
                {
                    float shineWeight = Mathf.Sin(Mathf.InverseLerp(0.58f, 0.93f, faceT) * Mathf.PI);
                    col = Color.Lerp(col, Color.white, shineWeight * shineAlpha);
                }
            }
        }

        col.a *= alpha;
        return col;
    }

    private static Color32 DrawRecessedBox(
        int px, int py, int w, int h, float radius,
        Color32 border, Color32 topShadow, Color32 fill, float borderThickness)
    {
        float pad = 1.5f;
        float cx = (w - 1) * 0.5f;
        float cy = (h - 1) * 0.5f;
        float hw = w * 0.5f - pad;
        float hh = h * 0.5f - pad;

        float distOuter = RoundedRectSDF(px, py, cx, cy, hw, hh, radius);
        if (distOuter > 1.0f) return new Color32(0, 0, 0, 0);

        float alpha = distOuter <= 0f ? 1f : Mathf.Clamp01(1.0f - (distOuter + 0.5f));
        float distInner = distOuter + borderThickness;

        Color col;
        if (distInner > 0f)
        {
            col = border;
        }
        else
        {
            float innerTop = h - pad - borderThickness;
            if (py > innerTop - 5f)
            {
                float st = Mathf.InverseLerp(innerTop - 5f, innerTop, py);
                col = Color.Lerp(fill, topShadow, st);
            }
            else
            {
                col = fill;
            }
        }

        col.a *= alpha;
        return col;
    }

    private static Color32 DrawSliderKnob(int px, int py, int w, int h)
    {
        float cx = (w - 1) * 0.5f;
        float cy = (h - 1) * 0.5f;
        float r = w * 0.5f - 3f;
        float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - r;
        if (d > 1f) return new Color32(0, 0, 0, 0);

        float alpha = d <= 0f ? 1f : Mathf.Clamp01(1f - (d + 0.5f));
        Color col;
        if (d > -3.5f)
        {
            col = new Color32(142, 62, 14, 255);
        }
        else
        {
            float t = Mathf.InverseLerp(cy - r + 3.5f, cy + r - 3.5f, py);
            col = Color.Lerp(new Color32(255, 142, 28, 255), new Color32(255, 206, 78, 255), t);
            float shineDist = Vector2.Distance(new Vector2(px, py), new Vector2(cx - 4f, cy + 5f));
            if (shineDist < 5.5f)
            {
                col = Color.Lerp(col, Color.white, (1f - shineDist / 5.5f) * 0.65f);
            }
        }
        col.a *= alpha;
        return col;
    }

    private static Color32 DrawCopyIcon(int px, int py, int w, int h)
    {
        Color32 ink = new Color32(78, 52, 34, 255);
        // Back sheet (top-right offset)
        bool inBackOuter = Mathf.Abs(px - 36f) <= 13f && Mathf.Abs(py - 38f) <= 16f;
        bool inBackInner = Mathf.Abs(px - 36f) <= 8f && Mathf.Abs(py - 38f) <= 11f;
        // Front sheet (bottom-left offset)
        bool inFrontOuter = Mathf.Abs(px - 26f) <= 13f && Mathf.Abs(py - 26f) <= 16f;
        bool inFrontInner = Mathf.Abs(px - 26f) <= 8f && Mathf.Abs(py - 26f) <= 11f;

        if (inFrontOuter && !inFrontInner) return ink;
        if (!inFrontOuter && inBackOuter && !inBackInner) return ink;
        return new Color32(0, 0, 0, 0);
    }

    private static float SegmentSDF(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(0.0001f, Vector2.Dot(ba, ba)));
        return (pa - ba * h).magnitude;
    }

    private static float SpeakerShapeSDF(float px, float py)
    {
        // Rounded rectangular speaker base: x in [10, 20], y in [25, 39]
        float boxSdf = RoundedRectSDF(px, py, 15f, 32f, 5.5f, 7.5f, 2.2f);

        // Flared speaker cone: x in [17, 31], half-height linearly interpolated from 7 to 16
        float coneSdf;
        if (px < 17f)
        {
            coneSdf = 17f - px;
        }
        else if (px > 31f)
        {
            coneSdf = px - 31f;
        }
        else
        {
            float t = Mathf.InverseLerp(17f, 31f, px);
            float halfH = Mathf.Lerp(7.2f, 16.5f, t);
            coneSdf = Mathf.Abs(py - 32f) - halfH;
        }

        // Clamp cone X ends cleanly
        float coneXBounds = Mathf.Abs(px - 24f) - 7f;
        coneSdf = Mathf.Max(coneSdf, coneXBounds) - 1.2f;

        return Mathf.Min(boxSdf, coneSdf);
    }

    private static Color32 ShadeWhiteIconWithShadow(float mainSdf, float shadowSdf)
    {
        float fillAlpha = Mathf.Clamp01(0.8f - mainSdf);
        float shadowAlpha = Mathf.Clamp01(0.8f - shadowSdf) * 0.42f;
        if (fillAlpha <= 0.01f && shadowAlpha <= 0.01f)
            return new Color32(0, 0, 0, 0);

        float outAlpha = fillAlpha + shadowAlpha * (1f - fillAlpha);
        if (outAlpha <= 0.01f)
            return new Color32(0, 0, 0, 0);

        Color shadowCol = new Color(0.06f, 0.14f, 0.28f, 1f);
        Color col = Color.Lerp(shadowCol, Color.white, fillAlpha / outAlpha);
        col.a = outAlpha;
        return col;
    }

    private static float SoundWaveArcSDF(float px, float py, float cx, float cy, float radius, float thickness)
    {
        float dx = px - cx;
        float dy = py - cy;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);
        float angleRad = Mathf.Abs(Mathf.Atan2(dy, Mathf.Max(0.001f, dx)));
        float maxAngle = 44f * Mathf.Deg2Rad;

        if (dx <= 0f || angleRad > maxAngle)
        {
            float endY = Mathf.Sign(dy) * radius * Mathf.Sin(maxAngle);
            float endX = radius * Mathf.Cos(maxAngle);
            return Vector2.Distance(new Vector2(dx, dy), new Vector2(endX, endY)) - thickness;
        }

        return Mathf.Abs(dist - radius) - thickness;
    }

    private static Color32 DrawSoundOnIcon(int px, int py, int w, int h)
    {
        float mainSdf = Mathf.Min(
            SpeakerShapeSDF(px, py),
            Mathf.Min(
                SoundWaveArcSDF(px, py, 29f, 32f, 10.5f, 2.2f),
                SoundWaveArcSDF(px, py, 29f, 32f, 18.0f, 2.2f)));

        float shadowSdf = Mathf.Min(
            SpeakerShapeSDF(px, py + 2f),
            Mathf.Min(
                SoundWaveArcSDF(px, py + 2f, 29f, 32f, 10.5f, 2.2f),
                SoundWaveArcSDF(px, py + 2f, 29f, 32f, 18.0f, 2.2f)));

        return ShadeWhiteIconWithShadow(mainSdf, shadowSdf);
    }

    private static float SoundOffGlyphSDF(float px, float py)
    {
        Vector2 p = new Vector2(px, py);
        float cross1 = SegmentSDF(p, new Vector2(37f, 24f), new Vector2(51f, 40f)) - 2.4f;
        float cross2 = SegmentSDF(p, new Vector2(37f, 40f), new Vector2(51f, 24f)) - 2.4f;
        return Mathf.Min(SpeakerShapeSDF(px, py), Mathf.Min(cross1, cross2));
    }

    private static Color32 DrawSoundOffIcon(int px, int py, int w, int h)
    {
        float mainSdf = SoundOffGlyphSDF(px, py);
        float shadowSdf = SoundOffGlyphSDF(px, py + 2f);
        return ShadeWhiteIconWithShadow(mainSdf, shadowSdf);
    }

    private static Color32 DrawGlossyCircle(
        int px, int py, int w, int h,
        Color32 border, Color32 lip, Color32 topGrad, Color32 botGrad,
        float shineAlpha, float borderThickness, float lipThickness)
    {
        float pad = 3.0f;
        float cx = (w - 1) * 0.5f;
        float cy = (h - 1) * 0.5f;
        float r = Mathf.Min(w, h) * 0.5f - pad;

        float distOuter = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - r;
        if (distOuter > 1.0f) return new Color32(0, 0, 0, 0);

        float alpha = distOuter <= 0f ? 1f : Mathf.Clamp01(1.0f - (distOuter + 0.5f));
        float distInner = distOuter + borderThickness;
        Color col;

        if (distInner > 0f)
        {
            col = border;
        }
        else
        {
            float innerR = r - borderThickness;
            float tY = Mathf.InverseLerp(cy - innerR, cy + innerR, py);
            float lipOffsetDist = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy + lipThickness)) - (innerR - 0.5f);

            if (lipOffsetDist > 0f && py < cy)
            {
                col = lip;
            }
            else
            {
                col = Color.Lerp(botGrad, topGrad, tY);
                if (tY > 0.56f && tY < 0.94f)
                {
                    float shineWeight = Mathf.Sin(Mathf.InverseLerp(0.56f, 0.94f, tY) * Mathf.PI);
                    col = Color.Lerp(col, Color.white, shineWeight * shineAlpha);
                }
            }
        }

        col.a *= alpha;
        return col;
    }

    private static Color32 ShadeShadowedGlyph(float px, float py, System.Func<float, float, float> glyphSdf)
    {
        return ShadeWhiteIconWithShadow(glyphSdf(px, py), glyphSdf(px, py + 2f));
    }

    private static float CircleSDF(float px, float py, float cx, float cy, float radius)
    {
        return Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - radius;
    }

    private static float TaperedQuadCurveSDF(
        Vector2 p, Vector2 a, Vector2 ctrl, Vector2 b,
        float startRadius, float endRadius, int segments = 16)
    {
        float minDist = float.MaxValue;
        Vector2 prev = a;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1f - t;
            Vector2 curr = (u * u) * a + (2f * u * t) * ctrl + (t * t) * b;
            float r = Mathf.Lerp(startRadius, endRadius, t);
            float segDist = SegmentSDF(p, prev, curr) - r;
            if (segDist < minDist) minDist = segDist;
            prev = curr;
        }
        return minDist;
    }

    private static float ClockGlyphSDF(float px, float py)
    {
        Vector2 p = new Vector2(px, py);
        float distFromCenter = Vector2.Distance(p, new Vector2(32f, 29f));
        float ring = Mathf.Abs(distFromCenter - 19.5f) - 3.3f;
        float stem = RoundedRectSDF(px, py, 32f, 52.5f, 3.0f, 3.5f, 1.0f);
        float crown = RoundedRectSDF(px, py, 32f, 55.5f, 6.5f, 2.5f, 1.2f);
        float handUp = SegmentSDF(p, new Vector2(32f, 29f), new Vector2(32f, 41f)) - 2.6f;
        float handRight = SegmentSDF(p, new Vector2(32f, 29f), new Vector2(41f, 29f)) - 2.4f;
        float hub = CircleSDF(px, py, 32f, 29f, 3.5f);
        return Mathf.Min(Mathf.Min(ring, Mathf.Min(stem, crown)), Mathf.Min(hub, Mathf.Min(handUp, handRight)));
    }

    private static Color32 DrawClockIcon(int px, int py, int w, int h) =>
        ShadeShadowedGlyph(px, py, ClockGlyphSDF);

    private static float PersonSilhouetteSDF(float px, float py, float cx, float cy, float scale)
    {
        float head = CircleSDF(px, py, cx, cy + 12f * scale, 7.2f * scale);
        float torso = RoundedRectSDF(px, py, cx, cy - 5f * scale, 11.5f * scale, 8.5f * scale, 7.0f * scale);
        float torsoClip = (cy - 12f * scale) - py;
        float torsoBounded = Mathf.Max(torso, torsoClip);
        return Mathf.Min(head, torsoBounded);
    }

    private static float SurvivorsGlyphSDF(float px, float py)
    {
        float primary = PersonSilhouetteSDF(px, py, 24f, 27f, 1.04f);
        float secondary = PersonSilhouetteSDF(px, py, 42.5f, 25f, 0.86f);
        return Mathf.Min(primary, secondary);
    }

    private static Color32 DrawSurvivorsIcon(int px, int py, int w, int h) =>
        ShadeShadowedGlyph(px, py, SurvivorsGlyphSDF);

    private static float SkullGlyphSDF(float px, float py)
    {
        float cranium = CircleSDF(px, py, 32f, 37f, 16.5f);
        float jaw = RoundedRectSDF(px, py, 32f, 20f, 10.2f, 7.2f, 3.2f);
        float outer = Mathf.Min(cranium, jaw);

        float eyeL = CircleSDF(px, py, 25f, 35.5f, 4.4f);
        float eyeR = CircleSDF(px, py, 39f, 35.5f, 4.4f);
        float nose = CircleSDF(px, py, 32f, 26.5f, 2.3f);
        float toothL = RoundedRectSDF(px, py, 28.5f, 15.5f, 1.1f, 3.2f, 0.5f);
        float toothR = RoundedRectSDF(px, py, 35.5f, 15.5f, 1.1f, 3.2f, 0.5f);
        float cutouts = Mathf.Min(Mathf.Min(eyeL, eyeR), Mathf.Min(nose, Mathf.Min(toothL, toothR)));
        return Mathf.Max(outer, -cutouts);
    }

    private static Color32 DrawSkullIcon(int px, int py, int w, int h) =>
        ShadeShadowedGlyph(px, py, SkullGlyphSDF);

    private static float FireballGlyphSDF(float px, float py)
    {
        Vector2 p = new Vector2(px, py);
        float mainBulb = CircleSDF(px, py, 30f, 24.5f, 13.5f);
        float mainTongue = TaperedQuadCurveSDF(p, new Vector2(28f, 25f), new Vector2(26f, 41f), new Vector2(37f, 55f), 11.5f, 1.4f, 14);
        float sideLick = TaperedQuadCurveSDF(p, new Vector2(36f, 24f), new Vector2(46f, 30f), new Vector2(45f, 44f), 8.2f, 1.1f, 12);
        float outerFlame = Mathf.Min(mainBulb, Mathf.Min(mainTongue, sideLick));

        float innerCore = CircleSDF(px, py, 30f, 21.5f, 5.2f);
        float innerTip = TaperedQuadCurveSDF(p, new Vector2(30f, 21f), new Vector2(30f, 30f), new Vector2(35f, 37f), 4.5f, 0.8f, 10);
        float coreCutout = Mathf.Min(innerCore, innerTip);
        return Mathf.Max(outerFlame, -coreCutout);
    }

    private static Color32 DrawFireballIcon(int px, int py, int w, int h) =>
        ShadeShadowedGlyph(px, py, FireballGlyphSDF);

    private static float KickGlyphSDF(float px, float py)
    {
        Vector2 p = new Vector2(px, py);
        // Chunky cartoon kicking boot + 3 action rays on the right
        float shin = SegmentSDF(p, new Vector2(18f, 42f), new Vector2(26f, 25f)) - 5.4f;
        float sole = SegmentSDF(p, new Vector2(23f, 24f), new Vector2(39f, 21f)) - 5.5f;
        float toe = CircleSDF(px, py, 40f, 23f, 5.4f);
        float boot = Mathf.Min(shin, Mathf.Min(sole, toe));

        float rayTop = SegmentSDF(p, new Vector2(47f, 35f), new Vector2(56f, 40f)) - 2.0f;
        float rayMid = SegmentSDF(p, new Vector2(48f, 27f), new Vector2(58f, 27f)) - 2.1f;
        float rayBot = SegmentSDF(p, new Vector2(47f, 19f), new Vector2(55f, 14f)) - 2.0f;
        return Mathf.Min(boot, Mathf.Min(rayTop, Mathf.Min(rayMid, rayBot)));
    }

    private static Color32 DrawKickIcon(int px, int py, int w, int h) =>
        ShadeShadowedGlyph(px, py, KickGlyphSDF);

    private static float ScytheGlyphSDF(float px, float py)
    {
        Vector2 p = new Vector2(px, py);
        float shaft = SegmentSDF(p, new Vector2(14f, 12f), new Vector2(49f, 48f)) - 3.0f;
        float collar = CircleSDF(px, py, 47f, 46f, 4.6f);
        float bladeBack = TaperedQuadCurveSDF(p, new Vector2(49f, 47f), new Vector2(32f, 55f), new Vector2(14f, 39f), 5.2f, 0.9f, 18);
        return Mathf.Min(Mathf.Min(shaft, collar), bladeBack);
    }

    private static Color32 DrawScytheIcon(int px, int py, int w, int h) =>
        ShadeShadowedGlyph(px, py, ScytheGlyphSDF);

    private static Color32 DrawRoundedCardBorder(int px, int py, int w, int h)
    {
        float pad = 1.5f;
        float cx = (w - 1) * 0.5f;
        float cy = (h - 1) * 0.5f;
        float hw = w * 0.5f - pad;
        float hh = h * 0.5f - pad;

        float distOuter = RoundedRectSDF(px, py, cx, cy, hw, hh, 26f);
        float distInner = distOuter + 5.5f;
        if (distOuter > 1.0f || distInner < -1.0f) return new Color32(0, 0, 0, 0);

        float alphaOuter = distOuter <= 0f ? 1f : Mathf.Clamp01(1.0f - (distOuter + 0.5f));
        float alphaInner = distInner >= 0f ? 1f : Mathf.Clamp01(1.0f + (distInner - 0.5f));
        float alpha = Mathf.Min(alphaOuter, alphaInner);
        return new Color(1f, 1f, 1f, alpha);
    }
}
