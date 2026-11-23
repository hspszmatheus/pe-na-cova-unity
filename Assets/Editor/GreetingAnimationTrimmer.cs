using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public static class GreetingAnimationTrimmer
{
    public const string ClipPath = "Assets/Hodaart/HodaartLowPolyCharacterCollection3/Animations/Greeting.anim";
    public const string BackupPath = "Assets/Hodaart/HodaartLowPolyCharacterCollection3/Animations/Greeting_Original_Backup.anim";
    public const float DefaultCutTime = 0.35f; // Cuts the dead 350ms pause at the start where character is motionless

    [MenuItem("Tools/Trim Greeting Animation (Cut Start)")]
    public static void TrimGreeting()
    {
        TrimGreetingWithDuration(DefaultCutTime);
    }

    [MenuItem("Tools/Inspect Arm Curves")]
    public static void InspectArmCurves()
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null) return;

        var bindings = AnimationUtility.GetCurveBindings(clip);
        foreach (var b in bindings)
        {
            if (b.propertyName == "RightHandT.y" || b.propertyName == "RightHandT.z" || b.propertyName == "RightHandQ.y")
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                float val0 = curve.Evaluate(0f);
                float val01 = curve.Evaluate(0.1f);
                float val02 = curve.Evaluate(0.2f);
                float val03 = curve.Evaluate(0.3f);
                float val04 = curve.Evaluate(0.4f);
                float val05 = curve.Evaluate(0.5f);
                float val07 = curve.Evaluate(0.7f);
                Debug.Log($"[Trimmed] {b.propertyName}: t0={val0:F3}, t0.1={val01:F3}, t0.2={val02:F3}, t0.3={val03:F3}, t0.4={val04:F3}, t0.5={val05:F3}, t0.7={val07:F3}");
            }
        }
    }

    public static void TrimGreetingWithDuration(float cutTime)
    {
        // 1. Create backup if it doesn't already exist
        if (!File.Exists(BackupPath))
        {
            AssetDatabase.CopyAsset(ClipPath, BackupPath);
            AssetDatabase.Refresh();
            Debug.Log($"[GreetingAnimationTrimmer] Created backup of original Greeting.anim at: {BackupPath}");
        }

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            Debug.LogError($"[GreetingAnimationTrimmer] Greeting clip not found at: {ClipPath}");
            return;
        }

        var bindings = AnimationUtility.GetCurveBindings(clip);
        Debug.Log($"[GreetingAnimationTrimmer] Trimming {bindings.Length} curves by {cutTime:F3}s...");

        int curveCount = 0;
        foreach (var binding in bindings)
        {
            AnimationCurve origCurve = AnimationUtility.GetEditorCurve(clip, binding);
            if (origCurve == null || origCurve.keys.Length == 0) continue;

            List<Keyframe> newKeys = new List<Keyframe>();

            // Calculate value and tangent at cutTime for t = 0
            float startVal = origCurve.Evaluate(cutTime);
            float dt = 0.001f;
            float tangent = (origCurve.Evaluate(cutTime + dt) - origCurve.Evaluate(cutTime - dt)) / (2f * dt);

            // Add starting keyframe at t = 0
            Keyframe firstKey = new Keyframe(0f, startVal, tangent, tangent);
            newKeys.Add(firstKey);

            // Add all subsequent keyframes shifted by cutTime
            foreach (var k in origCurve.keys)
            {
                if (k.time > cutTime + 0.001f)
                {
                    Keyframe shifted = new Keyframe(
                        k.time - cutTime,
                        k.value,
                        k.inTangent,
                        k.outTangent,
                        k.inWeight,
                        k.outWeight
                    );
                    shifted.weightedMode = k.weightedMode;
                    newKeys.Add(shifted);
                }
            }

            AnimationCurve newCurve = new AnimationCurve(newKeys.ToArray());
            newCurve.preWrapMode = origCurve.preWrapMode;
            newCurve.postWrapMode = origCurve.postWrapMode;

            AnimationUtility.SetEditorCurve(clip, binding, newCurve);
            curveCount++;
        }

        // Update settings
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0f;
        settings.stopTime = Mathf.Max(0.1f, settings.stopTime - cutTime);
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();

        Debug.Log($"[GreetingAnimationTrimmer] Successfully trimmed {curveCount} curves! New clip duration: {clip.length:F3}s (was previously {clip.length + cutTime:F3}s).");
    }

    [MenuItem("Tools/Restore Original Greeting Animation")]
    public static void RestoreOriginal()
    {
        if (!File.Exists(BackupPath))
        {
            Debug.LogWarning("[GreetingAnimationTrimmer] No backup found to restore.");
            return;
        }

        AssetDatabase.DeleteAsset(ClipPath);
        AssetDatabase.CopyAsset(BackupPath, ClipPath);
        AssetDatabase.Refresh();
        Debug.Log("[GreetingAnimationTrimmer] Restored original Greeting.anim from backup.");
    }
}
