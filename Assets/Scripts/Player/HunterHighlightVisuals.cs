using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Controla o visual de identificação dos Hunters (A Morte) aliados em partidas com múltiplos Hunters:
/// - Orbe de Alma de Fogo (Fire SoulOrb) fixado no peito do Hunter aliado (na altura do coração/peito),
///   com tonalidade de fogo/chamas alaranjadas intensas combinando com a bola de fogo/ataque de fogo.
/// - Visível através de paredes (ZTest Always) exclusivamente para outros Hunters aliados.
/// - Contorno de Fogo (LineworkLite HunterAllyOutline - bit 4 / valor 16) da mesma cor de fogo através de paredes.
/// - Nickname flutuante ("A Morte" / Nickname) com borda e texto em chamas sobre a cabeça, legível através de obstáculos.
/// - OS SOBREVIVENTES NUNCA VÊEM ESSES ELEMENTOS — visibilidade restrita estritamente a Hunters aliados.
/// - O próprio jogador local da Morte não vê seu próprio orbe para não obstruir sua câmera.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class HunterHighlightVisuals : MonoBehaviour
{
    [Header("Fire Soul Orb Configuration")]
    [SerializeField] private GameObject soulOrbPrefab;
    [Tooltip("Offset relativo ao osso do peito (Chest) para centralizar no corpo da Morte")]
    [SerializeField] private Vector3 chestOffset = new Vector3(0f, 0.12f, 0.05f);
    [Tooltip("Escala compacta para o orbe caber perfeitamente no peito")]
    [SerializeField] private float soulOrbScale = 0.28f;
    [Range(0.1f, 1f)]
    [Tooltip("Opacidade da alma de fogo")]
    [SerializeField] private float soulOrbOpacity = 0.85f;

    public const uint FireOutlineMask = 16u; // Bit 4 do LineworkLite (HunterAllyOutline - Fogo / Laranja)

    private PlayerController playerController;
    private HunterKnockdownLogic knockdownLogic;

    private GameObject fireSoulOrbInstance;
    private Transform attachedChestBone;
    private Transform headBone;

    private GameObject nameplateContainer;
    private GameObject pillQuad;
    private TextMesh nameTextMesh;
    private TextMesh shadowTextMesh;

    private Material textMaterial;
    private Material pillMaterial;

    private string currentDisplayedNickname = string.Empty;
    private bool isAllyOutlineActive = false;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        knockdownLogic = GetComponent<HunterKnockdownLogic>();

        // Se este personagem não for a Morte (Hunter), desativa o componente imediatamente
        if (playerController != null && !playerController.IsHunter)
        {
            enabled = false;
            return;
        }

        FindBones();
        InitializeMaterials();
        CreateFireSoulOrb();
        CreateNameplate();
    }

    private void Start()
    {
        if (playerController != null)
        {
            playerController.OnNicknameChanged += UpdateNickname;
            if (!string.IsNullOrEmpty(playerController.Nickname))
            {
                UpdateNickname(playerController.Nickname);
            }
            else
            {
                UpdateNickname("A Morte");
            }
        }
    }

    private void OnDestroy()
    {
        if (playerController != null)
        {
            playerController.OnNicknameChanged -= UpdateNickname;
        }

        if (fireSoulOrbInstance != null) Destroy(fireSoulOrbInstance);
        if (nameplateContainer != null) Destroy(nameplateContainer);

        if (textMaterial != null) Destroy(textMaterial);
        if (pillMaterial != null) Destroy(pillMaterial);

        ApplyAllyOutline(false);
    }

    private void OnDisable()
    {
        if (fireSoulOrbInstance != null && fireSoulOrbInstance.activeSelf)
        {
            fireSoulOrbInstance.SetActive(false);
        }
        if (nameplateContainer != null && nameplateContainer.activeSelf)
        {
            nameplateContainer.SetActive(false);
        }
        ApplyAllyOutline(false);
    }

    private void FindBones()
    {
        var anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            attachedChestBone = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.UpperChest);
            headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        }
    }

    private void InitializeMaterials()
    {
        Shader textShader = Shader.Find("Toon/TextOverlayThroughWalls");
        if (textShader != null)
        {
            textMaterial = new Material(textShader);
            textMaterial.color = Color.white;
        }

        Shader pillShader = Shader.Find("Toon/PillOverlayThroughWalls");
        if (pillShader != null)
        {
            pillMaterial = new Material(pillShader);
            // Fundo escuro cor de brasa / magma com borda de fogo vibrante
            pillMaterial.SetColor("_BaseColor", new Color(0.14f, 0.03f, 0.01f, 0.70f));
            pillMaterial.SetColor("_BorderColor", new Color(1.0f, 0.45f, 0.05f, 0.85f));
            pillMaterial.SetFloat("_BorderThickness", 0.045f);
        }

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (defaultFont != null && defaultFont.material != null && textMaterial != null)
        {
            textMaterial.mainTexture = defaultFont.material.mainTexture;
        }
    }

    public void SetSoulOrbPrefab(GameObject prefab)
    {
        if (prefab == null) return;
        soulOrbPrefab = prefab;
        if (fireSoulOrbInstance != null)
        {
            Destroy(fireSoulOrbInstance);
            fireSoulOrbInstance = null;
        }
        CreateFireSoulOrb();
    }

    private void CreateFireSoulOrb()
    {
        if (soulOrbPrefab == null)
        {
            soulOrbPrefab = Resources.Load<GameObject>("SoulOrb");
        }

#if UNITY_EDITOR
        if (soulOrbPrefab == null)
        {
            soulOrbPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Lamin/SoulOrb.prefab");
        }
        if (soulOrbPrefab == null)
        {
            soulOrbPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/SoulOrb.prefab");
        }
#endif

        if (soulOrbPrefab == null)
        {
            Debug.LogWarning("[HunterHighlightVisuals] SoulOrb prefab não foi encontrado para a Morte aliada.");
            return;
        }

        fireSoulOrbInstance = Instantiate(soulOrbPrefab, transform);
        fireSoulOrbInstance.name = "_HunterFireSoulOrb";
        fireSoulOrbInstance.transform.localScale = Vector3.one * soulOrbScale;

        if (attachedChestBone != null)
        {
            fireSoulOrbInstance.transform.SetParent(attachedChestBone, false);
            fireSoulOrbInstance.transform.localPosition = chestOffset;
            fireSoulOrbInstance.transform.localRotation = Quaternion.identity;
        }
        else
        {
            fireSoulOrbInstance.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            fireSoulOrbInstance.transform.localRotation = Quaternion.identity;
        }

        // Tonalidade de fogo / chamas intensas (laranja vivo com núcleo dourado)
        Color fireColor = new Color(1.0f, 0.45f, 0.05f, soulOrbOpacity);
        Color fireEmission = new Color(1.2f, 0.55f, 0.10f, 1.0f);

        // Converte as cores de todas as partículas para fogo/brasa
        var particleSystems = fireSoulOrbInstance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in particleSystems)
        {
            var main = ps.main;
            main.startColor = fireColor;
        }

        // Configura renderers das partículas para serem visíveis através das paredes e aplica cor de fogo
        var renderers = fireSoulOrbInstance.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var psr in renderers)
        {
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;

            var mats = psr.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    mats[i].SetFloat("_ZTest", (float)CompareFunction.Always);
                    mats[i].renderQueue = (int)RenderQueue.Overlay + 100;

                    if (mats[i].HasProperty("_TintColor"))
                    {
                        mats[i].SetColor("_TintColor", fireColor);
                    }
                    if (mats[i].HasProperty("_Color"))
                    {
                        mats[i].SetColor("_Color", fireColor);
                    }
                    if (mats[i].HasProperty("_BaseColor"))
                    {
                        mats[i].SetColor("_BaseColor", fireColor);
                    }
                    if (mats[i].HasProperty("_EmissionColor"))
                    {
                        mats[i].SetColor("_EmissionColor", fireEmission);
                    }
                }
            }
            psr.materials = mats;
        }

        fireSoulOrbInstance.SetActive(false);
    }

    private void CreateNameplate()
    {
        nameplateContainer = new GameObject("_HunterNameplate");
        nameplateContainer.transform.SetParent(transform, false);
        nameplateContainer.transform.localPosition = new Vector3(0f, 2.50f, 0f);
        nameplateContainer.SetActive(false);

        // Fundo do crachá estilo Overwatch (Pill escuro com borda de fogo brilhante)
        pillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pillQuad.name = "PillBadge";
        pillQuad.transform.SetParent(nameplateContainer.transform, false);
        pillQuad.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        pillQuad.transform.localScale = new Vector3(1.2f, 0.28f, 1f);

        var col = pillQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var pillRenderer = pillQuad.GetComponent<MeshRenderer>();
        if (pillRenderer != null && pillMaterial != null)
        {
            pillRenderer.material = pillMaterial;
            pillRenderer.shadowCastingMode = ShadowCastingMode.Off;
            pillRenderer.receiveShadows = false;
        }

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Texto com sombra escura de brasa para contraste
        GameObject shadowObj = new GameObject("NameShadow");
        shadowObj.transform.SetParent(nameplateContainer.transform, false);
        shadowObj.transform.localPosition = new Vector3(0.012f, -0.012f, -0.005f);

        shadowTextMesh = shadowObj.AddComponent<TextMesh>();
        shadowTextMesh.font = defaultFont;
        shadowTextMesh.fontSize = 28;
        shadowTextMesh.characterSize = 0.035f;
        shadowTextMesh.anchor = TextAnchor.MiddleCenter;
        shadowTextMesh.alignment = TextAlignment.Center;
        shadowTextMesh.fontStyle = FontStyle.Bold;
        shadowTextMesh.color = new Color(0.20f, 0.04f, 0.01f, 0.95f);

        var shadowRenderer = shadowObj.GetComponent<MeshRenderer>();
        if (shadowRenderer != null && textMaterial != null)
        {
            shadowRenderer.material = textMaterial;
            shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
        }

        // Texto principal em tom dourado/fogo radiante
        GameObject textObj = new GameObject("NameText");
        textObj.transform.SetParent(nameplateContainer.transform, false);
        textObj.transform.localPosition = new Vector3(0f, 0f, -0.01f);

        nameTextMesh = textObj.AddComponent<TextMesh>();
        nameTextMesh.font = defaultFont;
        nameTextMesh.fontSize = 28;
        nameTextMesh.characterSize = 0.035f;
        nameTextMesh.anchor = TextAnchor.MiddleCenter;
        nameTextMesh.alignment = TextAlignment.Center;
        nameTextMesh.fontStyle = FontStyle.Bold;
        nameTextMesh.color = new Color(1.0f, 0.80f, 0.25f, 1.0f);

        var textRenderer = textObj.GetComponent<MeshRenderer>();
        if (textRenderer != null && textMaterial != null)
        {
            textRenderer.material = textMaterial;
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }
    }

    public void UpdateNickname(string newNick)
    {
        if (string.IsNullOrEmpty(newNick))
        {
            newNick = "A Morte";
        }

        currentDisplayedNickname = newNick;

        if (nameTextMesh != null) nameTextMesh.text = newNick;
        if (shadowTextMesh != null) shadowTextMesh.text = newNick;

        // Ajusta largura da pílula conforme o tamanho do texto
        if (pillQuad != null)
        {
            float charCount = Mathf.Max(6, newNick.Length);
            float width = Mathf.Clamp(charCount * 0.11f + 0.35f, 1.0f, 2.8f);
            pillQuad.transform.localScale = new Vector3(width, 0.28f, 1f);
        }
    }

    /// <summary>
    /// Aplica ou remove o contorno de fogo (LineworkLite HunterAllyOutline - bit 4 / valor 16)
    /// nos renderers deste Hunter aliado, permitindo que outros Hunters o identifiquem através das paredes.
    /// </summary>
    public void ApplyAllyOutline(bool enable)
    {
        if (isAllyOutlineActive == enable) return;
        isAllyOutlineActive = enable;

        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            if (enable)
            {
                r.renderingLayerMask |= FireOutlineMask;
            }
            else
            {
                r.renderingLayerMask &= ~FireOutlineMask;
            }
        }
    }

    private void LateUpdate()
    {
        // ----------------------------------------------------------------------------------
        // REGRA DE VISIBILIDADE:
        // Visível EXCLUSIVAMENTE para outros Hunters aliados!
        // Sobreviventes NUNCA vêem este visual em hipótese alguma.
        // O próprio jogador local da Morte não vê seu próprio orbe no peito.
        // ----------------------------------------------------------------------------------
        var localPlayer = PlayerController.LocalInstance;

        // Se o jogador local ainda não existe ou NÃO é um Hunter, oculta tudo:
        if (localPlayer == null || !localPlayer.IsHunter)
        {
            if (fireSoulOrbInstance != null && fireSoulOrbInstance.activeSelf) fireSoulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        // O alvo não deve ser o próprio jogador local (não vê o próprio orbe no peito)
        bool isOther = playerController != null && !playerController.IsOwner;
        if (!isOther)
        {
            if (fireSoulOrbInstance != null && fireSoulOrbInstance.activeSelf) fireSoulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        // O alvo deve estar vivo e não atordoado
        bool targetIsAlive = playerController != null && !playerController.IsDead;
        bool isKnockedDown = knockdownLogic != null && knockdownLogic.IsKnockedDown;
        if (!targetIsAlive || isKnockedDown)
        {
            if (fireSoulOrbInstance != null && fireSoulOrbInstance.activeSelf) fireSoulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        // Garante que o FireSoulOrb exista
        if (fireSoulOrbInstance == null)
        {
            CreateFireSoulOrb();
        }

        // Sincronização do nickname se necessário
        if (string.IsNullOrEmpty(currentDisplayedNickname) && playerController != null && !string.IsNullOrEmpty(playerController.Nickname))
        {
            UpdateNickname(playerController.Nickname);
        }

        // Exibe o FireSoulOrb no peito da Morte aliada (visível mesmo por trás de paredes)
        if (fireSoulOrbInstance != null)
        {
            if (!fireSoulOrbInstance.activeSelf)
            {
                fireSoulOrbInstance.SetActive(true);
            }
        }

        // Aplica o contorno de fogo (LineworkLite HunterAllyOutline bit 4 / 16u)
        ApplyAllyOutline(true);

        // Exibe o crachá/nickname de fogo flutuante orientado para a câmera
        if (nameplateContainer != null)
        {
            if (!nameplateContainer.activeSelf)
            {
                nameplateContainer.SetActive(true);
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 headPos = headBone != null ? headBone.position : transform.position + Vector3.up * 2.2f;
                nameplateContainer.transform.position = headPos + Vector3.up * 0.70f;
                nameplateContainer.transform.rotation = cam.transform.rotation;

                float dist = Vector3.Distance(cam.transform.position, nameplateContainer.transform.position);
                float scale = Mathf.Clamp(dist * 0.012f, 0.50f, 1.30f);
                nameplateContainer.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
