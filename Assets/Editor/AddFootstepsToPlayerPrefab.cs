#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class AddFootstepsToPlayerPrefab
{
    [MenuItem("Tools/Update Player Prefab")]
    public static void UpdatePrefab()
    {
        string prefabPath = "Assets/Prefab/Player.prefab";
        GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("Não foi possível carregar Player.prefab");
            return;
        }

        // ── AudioSource ──────────────────────────────────────────
        AudioSource audioSource = prefab.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = prefab.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 1.5f;
        audioSource.maxDistance = 25f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        // ── AudioLowPassFilter (Filtro para suavizar estalos secos) ──
        AudioLowPassFilter lowPass = prefab.GetComponent<AudioLowPassFilter>();
        if (lowPass == null)
            lowPass = prefab.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 2800f;
        lowPass.lowpassResonanceQ = 1f;

        // ── PlayerFootstepLogic ──────────────────────────────────
        PlayerFootstepLogic footstepLogic = prefab.GetComponent<PlayerFootstepLogic>();
        if (footstepLogic == null)
            footstepLogic = prefab.AddComponent<PlayerFootstepLogic>();

        // Carregar variações de clips para evitar repetição mecânica
        string basePath = "Assets/The_Sound_Guild_FREE_PACK_Footsteps_Volume_02/WAV - 44100 Hz - 16 Bit/";

        List<AudioClip> streetClipsList = new List<AudioClip>();
        for (int i = 1; i <= 5; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{basePath}Footstep_Shoe_On_Street_0{i}.wav");
            if (clip != null) streetClipsList.Add(clip);
        }

        List<AudioClip> grassClipsList = new List<AudioClip>();
        for (int i = 1; i <= 5; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{basePath}Foostep_Grass_0{i}.wav");
            if (clip != null) grassClipsList.Add(clip);
        }

        // Montar propriedades via SerializedObject
        SerializedObject so = new SerializedObject(footstepLogic);

        SerializedProperty surfacesProp = so.FindProperty("surfaces");
        surfacesProp.arraySize = 2;

        // Superfície 0: Rua (Tag "Street") com variações
        SerializedProperty streetEntry = surfacesProp.GetArrayElementAtIndex(0);
        streetEntry.FindPropertyRelative("surfaceTag").stringValue = "Street";
        SerializedProperty streetClips = streetEntry.FindPropertyRelative("clips");
        streetClips.arraySize = streetClipsList.Count;
        for (int i = 0; i < streetClipsList.Count; i++)
        {
            streetClips.GetArrayElementAtIndex(i).objectReferenceValue = streetClipsList[i];
        }

        // Superfície 1: Grama (Tag "Grass") com variações
        SerializedProperty grassEntry = surfacesProp.GetArrayElementAtIndex(1);
        grassEntry.FindPropertyRelative("surfaceTag").stringValue = "Grass";
        SerializedProperty grassClips = grassEntry.FindPropertyRelative("clips");
        grassClips.arraySize = grassClipsList.Count;
        for (int i = 0; i < grassClipsList.Count; i++)
        {
            grassClips.GetArrayElementAtIndex(i).objectReferenceValue = grassClipsList[i];
        }

        // defaultClips: vazio
        SerializedProperty defaultClipsProp = so.FindProperty("defaultClips");
        defaultClipsProp.arraySize = 0;

        // Configurações acústicas suaves e discretas
        so.FindProperty("stepVolume").floatValue = 0.03f;
        so.FindProperty("volumeVariation").floatValue = 0.005f;
        so.FindProperty("walkPitch").floatValue = 0.88f;
        so.FindProperty("runPitch").floatValue = 1.02f;
        so.FindProperty("pitchJitter").floatValue = 0.02f;
        so.FindProperty("useLowPassFilter").boolValue = true;
        so.FindProperty("lowPassCutoff").floatValue = 2000f;
        so.FindProperty("minStepCooldown").floatValue = 0.22f;
        so.FindProperty("stopGracePeriod").floatValue = 0.12f;
        so.FindProperty("minAirTimeToLand").floatValue = 0.12f;
        so.FindProperty("landVolumeMultiplier").floatValue = 1.15f;
        so.FindProperty("landPitch").floatValue = 0.82f;

        lowPass.cutoffFrequency = 2000f;

        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        Debug.Log($"[Footsteps] Player.prefab atualizado com sucesso! Volume super discreto (0.03 = 3%), tom macio (0.88), filtro low-pass (2000Hz) e {streetClipsList.Count} variações de rua + {grassClipsList.Count} variações de grama.");

        PrefabUtility.UnloadPrefabContents(prefab);
    }
}
#endif
