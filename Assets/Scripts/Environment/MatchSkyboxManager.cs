using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gerencia a troca aleatória de skybox a cada partida (usando o shader FREE Skybox Extended Shader / Boxophobic).
/// Sincronizado via rede (Netcode for GameObjects) para que todos os jogadores (Host e Clientes) vejam exatamente o mesmo céu e iluminação.
/// </summary>
public class MatchSkyboxManager : NetworkBehaviour
{
    public static MatchSkyboxManager Instance { get; private set; }

    [System.Serializable]
    public class SkyboxPreset
    {
        [Tooltip("Nome descritivo da variação de céu")]
        public string presetName = "Skybox";

        [Tooltip("Material de skybox configurado com o shader Skybox Cubemap Extended ou Cubemap Blend")]
        public Material skyboxMaterial;

        [Header("Iluminação Direcional (Sol / Lua)")]
        public bool adjustLight = true;
        public Color lightColor = new Color(1.0f, 0.95f, 0.84f, 1.0f);
        [Range(0.1f, 3.0f)] public float lightIntensity = 1.0f;
        public Vector3 lightEulerAngles = new Vector3(50f, 330f, 0f);

        [Header("Neblina (Fog)")]
        public bool adjustFog = true;
        public bool enableFog = false;
        public Color fogColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        [Range(0.001f, 0.05f)] public float fogDensity = 0.01f;

        [Header("Iluminação Ambiente (Ambient Light)")]
        public bool adjustAmbient = true;
        public Color ambientSkyColor = new Color(0.21f, 0.23f, 0.26f, 1.0f);
        public Color ambientEquatorColor = new Color(0.11f, 0.12f, 0.14f, 1.0f);
        public Color ambientGroundColor = new Color(0.05f, 0.05f, 0.06f, 1.0f);

        [Header("Postes de Luz da Cidade")]
        [Tooltip("Define se os postes de iluminação pública da cidade devem acender neste preset (ideal para Noite, Pôr do Sol e Alvorada)")]
        public bool enableStreetlights = false;
    }

    [Header("Presets de Skybox")]
    [Tooltip("Lista de variações de Skybox do pacote FREE Skybox Extended Shader")]
    [SerializeField] private SkyboxPreset[] skyboxPresets;

    [Header("Referências da Cena")]
    [Tooltip("Luz direcional da cena (se vazia, busca automaticamente)")]
    [SerializeField] private Light directionalLight;

    [Header("Iluminação dos Postes")]
    [Tooltip("Controla automaticamente as luzes dos postes dependendo do preset")]
    [SerializeField] private bool autoControlStreetlights = true;
    [SerializeField] private List<StreetlightFixture> streetlights = new List<StreetlightFixture>();

    // Sincronização via rede: o Server/Host sorteia o índice e replica a todos os clientes
    private readonly NetworkVariable<int> activeSkyboxIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private int lastAppliedIndex = -1;

    public int CurrentSkyboxIndex => activeSkyboxIndex.Value;
    public SkyboxPreset[] Presets => skyboxPresets;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        FindDirectionalLightIfNeeded();
    }

    private void Start()
    {
        // Se estiver rodando offline/sem rede (ex: teste rápido direto na GameScene no editor)
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            ApplyRandomSkyboxOffline();
        }
    }

    public override void OnNetworkSpawn()
    {
        activeSkyboxIndex.OnValueChanged += OnSkyboxIndexChanged;

        if (IsServer)
        {
            SelectAndApplyRandomSkybox();
        }
        else if (activeSkyboxIndex.Value >= 0)
        {
            ApplyPreset(activeSkyboxIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        activeSkyboxIndex.OnValueChanged -= OnSkyboxIndexChanged;
    }

    private void OnSkyboxIndexChanged(int previousIndex, int newIndex)
    {
        if (newIndex >= 0)
        {
            ApplyPreset(newIndex);
        }
    }

    /// <summary>
    /// Sorteia um novo skybox entre os presets cadastrados (executado apenas pelo Host/Servidor).
    /// </summary>
    public void SelectAndApplyRandomSkybox()
    {
        if (!IsServer) return;
        if (skyboxPresets == null || skyboxPresets.Length == 0) return;

        int newIndex;
        if (skyboxPresets.Length == 1)
        {
            newIndex = 0;
        }
        else
        {
            // Tenta não repetir o mesmo skybox da partida anterior se houver mais de um
            int prev = PlayerPrefs.GetInt("LastPlayedSkyboxIndex", -1);
            do
            {
                newIndex = UnityEngine.Random.Range(0, skyboxPresets.Length);
            } while (newIndex == prev && skyboxPresets.Length > 1);

            PlayerPrefs.SetInt("LastPlayedSkyboxIndex", newIndex);
        }

        activeSkyboxIndex.Value = newIndex;
        ApplyPreset(newIndex);
    }

    private void ApplyRandomSkyboxOffline()
    {
        if (skyboxPresets == null || skyboxPresets.Length == 0) return;
        int rand = UnityEngine.Random.Range(0, skyboxPresets.Length);
        ApplyPreset(rand);
    }

    /// <summary>
    /// Aplica o preset de Skybox especificado e ajusta a iluminação ambiente e neblina.
    /// </summary>
    public void ApplyPreset(int index)
    {
        if (skyboxPresets == null || index < 0 || index >= skyboxPresets.Length)
        {
            Debug.LogWarning($"[MatchSkyboxManager] Índice de preset inválido: {index}");
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

        Debug.Log($"[MatchSkyboxManager] Skybox aplicado: '{preset.presetName}' (Material: {preset.skyboxMaterial?.name}, Postes: {preset.enableStreetlights})");
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

    [ContextMenu("Testar Próximo Skybox")]
    public void TestNextSkybox()
    {
        if (skyboxPresets == null || skyboxPresets.Length == 0) return;
        int next = (lastAppliedIndex + 1) % skyboxPresets.Length;
        ApplyPreset(next);
    }

    [ContextMenu("Sortear Novo Skybox")]
    public void TestRandomSkybox()
    {
        if (skyboxPresets == null || skyboxPresets.Length == 0) return;
        int rand = UnityEngine.Random.Range(0, skyboxPresets.Length);
        ApplyPreset(rand);
    }
}
