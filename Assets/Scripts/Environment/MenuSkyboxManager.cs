using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gerencia a troca dinâmica e aleatória de skybox no Menu Inicial e Saguão (JoinByBrowsing.unity).
/// Sorteia entre os presets de Dia, Noite, Pôr do Sol, Alvorada e Céu Limpo (pacote FREE Skybox Extended Shader),
/// ajustando a luz direcional, neblina, iluminação ambiente e acendendo os postes de luz da cidade (StreetlightFixture) à noite.
/// </summary>
public class MenuSkyboxManager : MonoBehaviour
{
    public static MenuSkyboxManager Instance { get; private set; }

    private const string PrefKeyLastMenuSkybox = "LastMenuSkyboxIndex";

    [Header("Presets de Skybox")]
    [Tooltip("Lista de presets de iluminação e céu (Dia, Noite, Pôr do Sol, etc.)")]
    [SerializeField] private MatchSkyboxManager.SkyboxPreset[] skyboxPresets;

    [Header("Referências da Cena")]
    [Tooltip("Luz direcional da cena do menu")]
    [SerializeField] private Light directionalLight;

    [Header("Iluminação dos Postes")]
    [Tooltip("Controla automaticamente as luzes dos postes dependendo do preset sorteado")]
    [SerializeField] private bool autoControlStreetlights = true;
    [SerializeField] private List<StreetlightFixture> streetlights = new List<StreetlightFixture>();

    [Header("Comportamento")]
    [Tooltip("Sorteia um novo céu aleatoriamente ao iniciar a cena")]
    [SerializeField] private bool randomizeOnStart = true;

    private int lastAppliedIndex = -1;

    public int CurrentPresetIndex => lastAppliedIndex;
    public MatchSkyboxManager.SkyboxPreset[] Presets => skyboxPresets;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        FindDirectionalLightIfNeeded();
        FindStreetlightsIfNeeded();
    }

    private void Start()
    {
        if (randomizeOnStart)
        {
            SelectAndApplyRandomSkybox();
        }
    }

    /// <summary>
    /// Sorteia e aplica um preset de céu aleatório, evitando repetir o último exibido se houver mais de um.
    /// </summary>
    public void SelectAndApplyRandomSkybox()
    {
        if (skyboxPresets == null || skyboxPresets.Length == 0)
            return;

        int newIndex;
        if (skyboxPresets.Length == 1)
        {
            newIndex = 0;
        }
        else
        {
            int prev = PlayerPrefs.GetInt(PrefKeyLastMenuSkybox, -1);
            int attempts = 0;
            do
            {
                newIndex = UnityEngine.Random.Range(0, skyboxPresets.Length);
                attempts++;
            } while (newIndex == prev && skyboxPresets.Length > 1 && attempts < 10);

            PlayerPrefs.SetInt(PrefKeyLastMenuSkybox, newIndex);
            PlayerPrefs.Save();
        }

        ApplyPreset(newIndex);
    }

    /// <summary>
    /// Aplica o preset de Skybox especificado e ajusta sol/lua, neblina, luz ambiente e postes de luz.
    /// </summary>
    public void ApplyPreset(int index)
    {
        if (skyboxPresets == null || index < 0 || index >= skyboxPresets.Length)
        {
            Debug.LogWarning($"[MenuSkyboxManager] Índice de preset inválido: {index}");
            return;
        }

        var preset = skyboxPresets[index];
        lastAppliedIndex = index;

        FindDirectionalLightIfNeeded();

        // 1. Aplica o Material de Skybox
        if (preset.skyboxMaterial != null)
        {
            RenderSettings.skybox = preset.skyboxMaterial;
        }

        // 2. Ajusta a Luz Direcional
        if (preset.adjustLight && directionalLight != null)
        {
            directionalLight.color = preset.lightColor;
            directionalLight.intensity = preset.lightIntensity;
            if (preset.lightEulerAngles != Vector3.zero)
            {
                directionalLight.transform.rotation = Quaternion.Euler(preset.lightEulerAngles);
            }
        }

        // 3. Ajusta a Neblina (Fog)
        if (preset.adjustFog)
        {
            RenderSettings.fog = preset.enableFog;
            if (preset.enableFog)
            {
                RenderSettings.fogColor = preset.fogColor;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = preset.fogDensity;
            }
        }

        // 4. Ajusta a Iluminação Ambiente
        if (preset.adjustAmbient)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = preset.ambientSkyColor;
            RenderSettings.ambientEquatorColor = preset.ambientEquatorColor;
            RenderSettings.ambientGroundColor = preset.ambientGroundColor;
        }

        // 5. Controla os Postes de Iluminação Pública
        if (autoControlStreetlights)
        {
            SetStreetlightsActive(preset.enableStreetlights);
        }

        // 6. Atualiza o Environment e Reflection Probes em tempo real
        DynamicGI.UpdateEnvironment();

        Debug.Log($"[MenuSkyboxManager] Skybox do Menu aplicado: '{preset.presetName}' (Material: {preset.skyboxMaterial?.name}, Postes: {preset.enableStreetlights})");
    }

    public void SetStreetlightsActive(bool active)
    {
        if (streetlights == null || streetlights.Count == 0)
        {
            FindStreetlightsIfNeeded();
        }

        for (int i = 0; i < streetlights.Count; i++)
        {
            if (streetlights[i] != null)
            {
                streetlights[i].SetLightState(active);
            }
        }
    }

    public void FindStreetlightsIfNeeded()
    {
        if (streetlights == null) streetlights = new List<StreetlightFixture>();
        streetlights.RemoveAll(s => s == null);

        if (streetlights.Count == 0)
        {
            var found = FindObjectsByType<StreetlightFixture>();
            streetlights.AddRange(found);
        }
    }

    private void FindDirectionalLightIfNeeded()
    {
        if (directionalLight == null)
        {
            var lights = FindObjectsByType<Light>();
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    directionalLight = l;
                    break;
                }
            }
        }
    }

    [ContextMenu("Sortear Novo Skybox")]
    public void TestRandomSkybox()
    {
        SelectAndApplyRandomSkybox();
    }

    [ContextMenu("Testar Próximo Skybox")]
    public void TestNextSkybox()
    {
        if (skyboxPresets == null || skyboxPresets.Length == 0) return;
        int next = (lastAppliedIndex + 1) % skyboxPresets.Length;
        ApplyPreset(next);
    }

    [ContextMenu("Ligar Todos os Postes")]
    public void TurnOnAllStreetlights()
    {
        SetStreetlightsActive(true);
    }

    [ContextMenu("Desligar Todos os Postes")]
    public void TurnOffAllStreetlights()
    {
        SetStreetlightsActive(false);
    }
}
