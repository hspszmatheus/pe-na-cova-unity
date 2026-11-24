using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Controla o visual de identificação dos sobreviventes aliados:
/// - Orbe de Alma (SoulOrb) roxo fixado no peito do sobrevivente (na altura do coração), visível através das paredes (ZTest Always).
/// - Segue o osso do peito (Chest/HumanBodyBones) do modelo ativo para sincronização total com animações de corrida e respiração.
/// - Nome flutuando sobre a cabeça em roxo radiante combinando com o efeito visual, legível através de obstáculos.
/// - A MORTE (HUNTER) NUNCA VÊ ESSES ELEMENTOS — visibilidade restrita exclusivamente a sobreviventes aliados.
/// - O próprio jogador local não vê seu próprio orbe no peito para não obstruir sua visão/câmera.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class SurvivorHighlightVisuals : MonoBehaviour
{
    [Header("Soul Orb Configuration")]
    [SerializeField] private GameObject soulOrbPrefab;
    [Tooltip("Offset relativo ao osso do peito (Chest) para centralizar no meio do corpo do peito")]
    [SerializeField] private Vector3 chestOffset = new Vector3(0f, 0.10f, 0.00f);
    [Tooltip("Escala compacta para o orbe caber perfeitamente no peito como um coração")]
    [SerializeField] private float soulOrbScale = 0.25f;
    [Range(0.1f, 1f)]
    [Tooltip("Opacidade da alma (70% padrão conforme solicitado, 100% = totalmente visível)")]
    [SerializeField] private float soulOrbOpacity = 0.70f;

    private PlayerController playerController;
    private PlayerCharacterRandomizer randomizer;

    private GameObject soulOrbInstance;
    private GameObject currentAttachedCharacter;
    private GameObject nameplateContainer;
    private GameObject pillQuad;
    private TextMesh nameTextMesh;
    private TextMesh shadowTextMesh;

    private Material textMaterial;
    private Material pillMaterial;

    private string currentDisplayedNickname = string.Empty;
    private bool isAllyOutlineActive = false;

    public void Awake()
    {
        playerController = GetComponent<PlayerController>();
        randomizer = GetComponent<PlayerCharacterRandomizer>();

        // Se este personagem for a Morte (Hunter), desativa o componente imediatamente
        if (playerController != null && playerController.IsHunter)
        {
            enabled = false;
            return;
        }

        InitializeMaterials();
        CreateSoulOrb();
        CreateNameplate();
    }

    private void Start()
    {
        if (randomizer != null)
        {
            randomizer.OnCharacterVisualsChanged += OnCharacterVisualsChanged;
            var activeChar = randomizer.GetActiveCharacterObject();
            if (activeChar != null)
            {
                AttachSoulOrbToChest(activeChar);
            }
        }

        if (playerController != null)
        {
            playerController.OnNicknameChanged += UpdateNickname;
            if (!string.IsNullOrEmpty(playerController.Nickname))
            {
                UpdateNickname(playerController.Nickname);
            }
        }
    }

    private void OnDestroy()
    {
        if (randomizer != null)
        {
            randomizer.OnCharacterVisualsChanged -= OnCharacterVisualsChanged;
        }

        if (playerController != null)
        {
            playerController.OnNicknameChanged -= UpdateNickname;
        }

        if (soulOrbInstance != null) Destroy(soulOrbInstance);
        if (nameplateContainer != null) Destroy(nameplateContainer);

        if (textMaterial != null) Destroy(textMaterial);
        if (pillMaterial != null) Destroy(pillMaterial);

        ApplyAllyOutline(false);
    }

    private void OnDisable()
    {
        if (soulOrbInstance != null && soulOrbInstance.activeSelf)
        {
            soulOrbInstance.SetActive(false);
        }
        if (nameplateContainer != null && nameplateContainer.activeSelf)
        {
            nameplateContainer.SetActive(false);
        }
        ApplyAllyOutline(false);
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
            pillMaterial.SetColor("_BaseColor", new Color(0.08f, 0.02f, 0.12f, 0.65f));
            pillMaterial.SetColor("_BorderColor", new Color(0.85f, 0.35f, 1.0f, 0.75f));
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
        if (soulOrbInstance != null)
        {
            Destroy(soulOrbInstance);
            soulOrbInstance = null;
        }
        CreateSoulOrb();

        if (randomizer != null)
        {
            var activeChar = randomizer.GetActiveCharacterObject();
            if (activeChar != null)
            {
                AttachSoulOrbToChest(activeChar);
            }
        }
    }

    private void CreateSoulOrb()
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
#endif

        if (soulOrbPrefab == null)
        {
            Debug.LogWarning("[SurvivorHighlightVisuals] SoulOrb prefab não foi encontrado em Resources ou Assets/Prefab/Lamin/SoulOrb.prefab.");
            return;
        }

        soulOrbInstance = Instantiate(soulOrbPrefab, transform);
        soulOrbInstance.name = "_SurvivorSoulOrb";
        soulOrbInstance.transform.localPosition = new Vector3(0f, 1.19f, 0f);
        soulOrbInstance.transform.localRotation = Quaternion.identity;
        soulOrbInstance.transform.localScale = Vector3.one * soulOrbScale;

        // Configura renderers das partículas para serem visíveis através das paredes e aplica opacidade
        var renderers = soulOrbInstance.GetComponentsInChildren<ParticleSystemRenderer>(true);
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
                        Color c = mats[i].GetColor("_TintColor");
                        mats[i].SetColor("_TintColor", new Color(c.r * soulOrbOpacity, c.g * soulOrbOpacity, c.b * soulOrbOpacity, c.a * soulOrbOpacity));
                    }
                }
            }
            psr.materials = mats;
        }

        soulOrbInstance.SetActive(false);
    }

    private void OnCharacterVisualsChanged(GameObject newCharacterModel)
    {
        AttachSoulOrbToChest(newCharacterModel);
        if (isAllyOutlineActive)
        {
            isAllyOutlineActive = false;
            ApplyAllyOutline(true);
        }
    }

    /// <summary>
    /// Aplica ou remove o contorno roxo (LineworkLite TargetTaskOutline - bit 2 / valor 4) nos renderers deste aliado,
    /// permitindo que sobreviventes aliados o identifiquem através das paredes com contorno e SoulOrb.
    /// </summary>
    public void ApplyAllyOutline(bool enable)
    {
        // Se este sobrevivente estiver marcado para a Morte (revelado pelo PlayerController), o contorno roxo
        // permanece ativo mesmo se o sistema de aliados tentar desativá-lo
        if (!enable && playerController != null && playerController.IsMarkedForDeath)
        {
            return;
        }

        if (isAllyOutlineActive == enable) return;
        isAllyOutlineActive = enable;

        const uint purpleOutlineMask = 4u; // Bit 2 do LineworkLite (TargetTaskOutline - Roxo vibrante)
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            if (enable)
            {
                r.renderingLayerMask |= purpleOutlineMask;
            }
            else
            {
                r.renderingLayerMask &= ~purpleOutlineMask;
            }
        }
    }

    public Vector3 GetChestWorldPosition()
    {
        if (soulOrbInstance != null && soulOrbInstance.transform.parent != null)
        {
            return soulOrbInstance.transform.position;
        }

        Transform chestBone = null;
        if (randomizer != null)
        {
            var activeChar = randomizer.GetActiveCharacterObject();
            if (activeChar != null)
            {
                var anim = activeChar.GetComponent<Animator>();
                if (anim != null && anim.isHuman)
                {
                    chestBone = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.UpperChest);
                }
            }
        }

        if (chestBone != null)
        {
            return chestBone.TransformPoint(chestOffset);
        }

        return transform.position + Vector3.up * 1.19f;
    }

    private void AttachSoulOrbToChest(GameObject characterObj)
    {
        if (soulOrbInstance == null) return;
        currentAttachedCharacter = characterObj;

        Transform chestBone = null;
        if (characterObj != null)
        {
            var anim = characterObj.GetComponent<Animator>();
            if (anim != null && anim.isHuman)
            {
                chestBone = anim.GetBoneTransform(HumanBodyBones.Chest);
                if (chestBone == null)
                {
                    chestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                }
            }
        }

        if (chestBone != null)
        {
            soulOrbInstance.transform.SetParent(chestBone, false);
            soulOrbInstance.transform.localPosition = chestOffset;
            soulOrbInstance.transform.localRotation = Quaternion.identity;
            soulOrbInstance.transform.localScale = Vector3.one * soulOrbScale;
        }
        else
        {
            soulOrbInstance.transform.SetParent(transform, false);
            soulOrbInstance.transform.localPosition = new Vector3(0f, 1.19f, 0f);
            soulOrbInstance.transform.localRotation = Quaternion.identity;
            soulOrbInstance.transform.localScale = Vector3.one * soulOrbScale;
        }
    }

    private void CreateNameplate()
    {
        nameplateContainer = new GameObject("_SurvivorNameplate");
        nameplateContainer.transform.SetParent(transform, false);
        nameplateContainer.transform.localPosition = new Vector3(0f, 2.15f, 0f);
        nameplateContainer.SetActive(false);

        // Fundo do crachá estilo Overwatch (Pill escuro translúcido com borda roxa brilhante)
        pillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pillQuad.name = "PillBadge";
        pillQuad.transform.SetParent(nameplateContainer.transform, false);
        pillQuad.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        pillQuad.transform.localScale = new Vector3(1.1f, 0.25f, 1f);

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

        // Texto com sombra escura para contraste e legibilidade
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
        shadowTextMesh.color = new Color(0.12f, 0.02f, 0.20f, 0.90f);

        var shadowRenderer = shadowObj.GetComponent<MeshRenderer>();
        if (shadowRenderer != null && textMaterial != null)
        {
            shadowRenderer.material = textMaterial;
            shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
        }

        // Texto principal em tom roxo/lilás radiante combinando com o SoulOrb
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
        nameTextMesh.color = new Color(0.94f, 0.65f, 1.0f, 1.0f);

        var textRenderer = textObj.GetComponent<MeshRenderer>();
        if (textRenderer != null && textMaterial != null)
        {
            textRenderer.material = textMaterial;
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }
    }

    public void UpdateNickname(string newName)
    {
        if (string.IsNullOrEmpty(newName)) newName = "Sobrevivente";
        currentDisplayedNickname = newName;

        string formattedText = $"▼ {newName}";
        if (nameTextMesh != null) nameTextMesh.text = formattedText;
        if (shadowTextMesh != null) shadowTextMesh.text = formattedText;

        if (pillQuad != null)
        {
            float width = Mathf.Clamp(formattedText.Length * 0.072f + 0.25f, 0.70f, 2.1f);
            pillQuad.transform.localScale = new Vector3(width, 0.25f, 1f);
        }
    }

    private void LateUpdate()
    {
        // ----------------------------------------------------------------------------------
        // REGRA DE VISIBILIDADE:
        // Durante a partida: A Morte (Hunter) NUNCA vê a alma ou o nickname dos sobreviventes.
        // Conclusão da partida (5s): A aura roxa e o nickname se tornam visíveis para o Hunter também,
        // permitindo ver a localização de todos os sobreviventes que não conseguiu matar!
        // ----------------------------------------------------------------------------------
        if (GameHUDController.Instance != null && (GameHUDController.Instance.IsHudHiddenByPlayer || !GameHUDController.Instance.IsHudVisible))
        {
            if (soulOrbInstance != null && soulOrbInstance.activeSelf) soulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        var localPlayer = PlayerController.LocalInstance;
        bool isGameOver = GameSpawner.Instance != null && GameSpawner.Instance.IsGameOver;

        // Se o jogador local ainda não existe:
        if (localPlayer == null)
        {
            if (soulOrbInstance != null && soulOrbInstance.activeSelf) soulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        // Durante o andamento da partida, bloqueia a visão do Hunter,
        // EXCETO se este sobrevivente estiver MARCADO PARA A MORTE (inatividade de task) OU REVELADO PELA HABILIDADE (E) DO HUNTER
        bool isMarkedForDeath = playerController != null && (playerController.IsMarkedForDeath || playerController.IsTemporarilyRevealed);
        if (!isGameOver && !isMarkedForDeath)
        {
            if (localPlayer.IsHunter)
            {
                if (soulOrbInstance != null && soulOrbInstance.activeSelf) soulOrbInstance.SetActive(false);
                if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
                ApplyAllyOutline(false);
                return;
            }
        }

        // O alvo deve estar vivo e não ser um fantasma
        bool targetIsAlive = playerController != null && !playerController.IsDead && !playerController.IsGhost;
        if (!targetIsAlive)
        {
            if (soulOrbInstance != null && soulOrbInstance.activeSelf) soulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        // O alvo não deve ser o próprio jogador local (não vê o próprio orbe no peito para não obstruir sua visão)
        bool isOther = playerController != null && !playerController.IsOwner;
        if (!isOther)
        {
            if (soulOrbInstance != null && soulOrbInstance.activeSelf) soulOrbInstance.SetActive(false);
            if (nameplateContainer != null && nameplateContainer.activeSelf) nameplateContainer.SetActive(false);
            ApplyAllyOutline(false);
            return;
        }

        // Garante que o SoulOrb exista e esteja ancorado ao osso do peito do modelo ativo
        if (soulOrbInstance == null)
        {
            CreateSoulOrb();
        }

        // Garante que o SoulOrb esteja ancorado ao osso do peito do modelo ativo
        if (currentAttachedCharacter == null && randomizer != null)
        {
            var activeChar = randomizer.GetActiveCharacterObject();
            if (activeChar != null)
            {
                AttachSoulOrbToChest(activeChar);
            }
        }

        // Sincronização do nickname se necessário
        if (string.IsNullOrEmpty(currentDisplayedNickname) && !string.IsNullOrEmpty(playerController.Nickname))
        {
            UpdateNickname(playerController.Nickname);
        }

        // Exibe o SoulOrb no peito do aliado (visível mesmo por trás de paredes)
        if (soulOrbInstance != null)
        {
            if (!soulOrbInstance.activeSelf)
            {
                soulOrbInstance.SetActive(true);
            }
        }

        // Aplica o contorno roxo (LineworkLite TargetTaskOutline) no aliado sobrevivente através das paredes
        ApplyAllyOutline(true);

        // Exibe o crachá/nickname roxo flutuante orientado para a câmera
        if (nameplateContainer != null)
        {
            if (!nameplateContainer.activeSelf)
            {
                nameplateContainer.SetActive(true);
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                nameplateContainer.transform.position = transform.position + Vector3.up * 2.15f;
                nameplateContainer.transform.rotation = cam.transform.rotation;

                float dist = Vector3.Distance(cam.transform.position, nameplateContainer.transform.position);
                float scale = Mathf.Clamp(dist * 0.012f, 0.50f, 1.30f);
                nameplateContainer.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
