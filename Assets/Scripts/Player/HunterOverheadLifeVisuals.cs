using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Exibe um contador de vidas flutuante acima da cabeça do Hunter no espaço 3D (World Space Billboard).
/// - Mostra as 3 cabeças do Hunter representando suas vidas (HunterLifeIcon).
/// - Quando o Hunter perde uma vida por matar um NPC errado, a cabeça correspondente é substituída
///   pelo ícone da cabeça rachada (HunterLifeLostIcon) com efeito visual de impacto.
/// - Exibe o nome/título do Hunter acima das vidas para fácil identificação em partidas com múltiplos Hunters.
/// - Segue o osso da cabeça (Head) e mantém alinhamento com a câmera local (Billboard).
/// - Visível para todos os jogadores na partida (sobreviventes, outros hunters e espectadores).
/// </summary>
[RequireComponent(typeof(HunterKnockdownLogic))]
public class HunterOverheadLifeVisuals : MonoBehaviour
{
    [Header("Posicionamento e Escala")]
    [Tooltip("Altura padrão acima do osso da cabeça (garante folga ampla sem colidir com o capuz)")]
    [SerializeField] private float heightAboveHead = 0.65f;
    [Tooltip("Altura padrão caso o osso da cabeça não seja encontrado")]
    [SerializeField] private float fallbackHeight = 2.30f;
    [SerializeField] private Vector3 baseScale = new Vector3(0.9f, 0.9f, 0.9f);

    [Header("Sprites")]
    [SerializeField] private Sprite activeSkullSprite;
    [SerializeField] private Sprite lostSkullSprite;

    private HunterKnockdownLogic knockdownLogic;
    private PlayerController playerController;
    private HunterSpawnIntroSequence hunterSpawnIntro;
    private Transform headBone;

    private GameObject container;
    private GameObject backdropObj;
    private SpriteRenderer backdropRenderer;
    private readonly SpriteRenderer[] skullRenderers = new SpriteRenderer[3];
    private readonly Vector3[] skullOriginalScales = new Vector3[3];
    private TextMesh nameTextMesh;

    private static Sprite cachedBackdropSprite;

    void Awake()
    {
        knockdownLogic = GetComponent<HunterKnockdownLogic>();
        playerController = GetComponent<PlayerController>();
        hunterSpawnIntro = GetComponent<HunterSpawnIntroSequence>();

        // Busca o osso da cabeça para ancoragem precisa
        var anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        }

        LoadSprites();
        CreateOverheadUI();
    }

    void Start()
    {
        if (knockdownLogic != null)
        {
            knockdownLogic.CurrentLives.OnValueChanged += OnLivesChanged;
            UpdateSkulls(knockdownLogic.CurrentLives.Value, triggerPulse: false);
        }

        if (playerController != null)
        {
            playerController.OnNicknameChanged += UpdateNickname;
            if (!string.IsNullOrEmpty(playerController.Nickname))
            {
                UpdateNickname(playerController.Nickname);
            }
            else
            {
                UpdateNickname(knockdownLogic != null ? knockdownLogic.GetHunterDisplayName() : "A Morte");
            }
        }
    }

    void OnDestroy()
    {
        if (knockdownLogic != null)
        {
            knockdownLogic.CurrentLives.OnValueChanged -= OnLivesChanged;
        }

        if (playerController != null)
        {
            playerController.OnNicknameChanged -= UpdateNickname;
        }

        if (container != null)
        {
            Destroy(container);
        }
    }

    void OnDisable()
    {
        if (container != null && container.activeSelf)
        {
            container.SetActive(false);
        }
    }

    private void LoadSprites()
    {
        if (activeSkullSprite == null)
        {
            activeSkullSprite = Resources.Load<Sprite>("HunterLifeIcon");
#if UNITY_EDITOR
            if (activeSkullSprite == null)
            {
                activeSkullSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/HunterLifeIcon.png");
            }
#endif
        }

        if (lostSkullSprite == null)
        {
            lostSkullSprite = Resources.Load<Sprite>("HunterLifeLostIcon");
#if UNITY_EDITOR
            if (lostSkullSprite == null)
            {
                lostSkullSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/HunterLifeLostIcon.png");
            }
#endif
        }
    }

    private void CreateOverheadUI()
    {
        container = new GameObject("_HunterOverheadLives");
        container.transform.SetParent(transform, false);
        container.transform.localPosition = new Vector3(0f, fallbackHeight, 0f);
        container.SetActive(false); // Inicia oculto até a partida/queda iniciar e o jogo começar oficialmente

        // 1. Fundo estético em formato de cápsula escura translúcida com borda carmesim sutil
        backdropObj = new GameObject("Backdrop");
        backdropObj.transform.SetParent(container.transform, false);
        backdropObj.transform.localPosition = new Vector3(0f, 0f, 0.005f);
        backdropObj.transform.localScale = new Vector3(0.70f, 0.70f, 1f);

        backdropRenderer = backdropObj.AddComponent<SpriteRenderer>();
        backdropRenderer.sprite = GetBackdropSprite();
        backdropRenderer.sortingOrder = 95;

        // 2. As 3 cabeças representando as vidas do Hunter
        for (int i = 0; i < 3; i++)
        {
            var skullObj = new GameObject($"Skull_{i}");
            skullObj.transform.SetParent(container.transform, false);
            skullObj.transform.localPosition = new Vector3((i - 1) * 0.32f, -0.02f, -0.005f);

            Vector3 sScale = Vector3.one * 0.055f;
            skullObj.transform.localScale = sScale;
            skullOriginalScales[i] = sScale;

            var sr = skullObj.AddComponent<SpriteRenderer>();
            sr.sprite = activeSkullSprite;
            sr.sortingOrder = 100;
            skullRenderers[i] = sr;
        }

        // 3. Nome do Hunter acima das cabeças
        var titleObj = new GameObject("NameText");
        titleObj.transform.SetParent(container.transform, false);
        titleObj.transform.localPosition = new Vector3(0f, 0.14f, -0.01f);

        nameTextMesh = titleObj.AddComponent<TextMesh>();
        nameTextMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameTextMesh.fontSize = 26;
        nameTextMesh.characterSize = 0.024f;
        nameTextMesh.anchor = TextAnchor.MiddleCenter;
        nameTextMesh.alignment = TextAlignment.Center;
        nameTextMesh.fontStyle = FontStyle.Bold;
        nameTextMesh.color = new Color(0.95f, 0.90f, 0.95f, 0.95f);

        var mr = titleObj.GetComponent<MeshRenderer>();
        if (nameTextMesh.font != null && nameTextMesh.font.material != null)
        {
            mr.material = nameTextMesh.font.material;
        }
        mr.sortingOrder = 105;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        string initialName = (playerController != null && !string.IsNullOrEmpty(playerController.Nickname))
            ? playerController.Nickname
            : (knockdownLogic != null ? knockdownLogic.GetHunterDisplayName() : "A Morte");
        nameTextMesh.text = initialName;
    }

    private void OnLivesChanged(int oldVal, int newVal)
    {
        UpdateSkulls(newVal, triggerPulse: newVal < oldVal);
    }

    public void EnsureUI()
    {
        if (container == null)
        {
            CreateOverheadUI();
        }
    }

    public void UpdateSkulls(int currentLives, bool triggerPulse = false)
    {
        EnsureUI();
        LoadSprites();

        int safeLives = Mathf.Clamp(currentLives, 0, 3);
        for (int i = 0; i < 3; i++)
        {
            if (skullRenderers[i] == null) continue;

            bool isLost = (i >= safeLives);
            if (isLost)
            {
                skullRenderers[i].sprite = lostSkullSprite != null ? lostSkullSprite : activeSkullSprite;
                skullRenderers[i].color = new Color(1f, 0.88f, 0.88f, 0.95f);

                if (triggerPulse && i == safeLives)
                {
                    StopCoroutine(nameof(PulseSkullRoutine));
                    StartCoroutine(PulseSkullRoutine(i));
                }
            }
            else
            {
                skullRenderers[i].sprite = activeSkullSprite;
                skullRenderers[i].color = Color.white;
            }
        }

        if (nameTextMesh != null)
        {
            if (safeLives <= 0)
            {
                nameTextMesh.text = "ELIMINADO";
                nameTextMesh.color = new Color(1f, 0.25f, 0.25f, 1f);
            }
            else
            {
                string displayName = (playerController != null && !string.IsNullOrEmpty(playerController.Nickname))
                    ? playerController.Nickname
                    : (knockdownLogic != null ? knockdownLogic.GetHunterDisplayName() : "A Morte");
                nameTextMesh.text = displayName;
                nameTextMesh.color = new Color(0.95f, 0.90f, 0.95f, 0.95f);
            }
        }
    }

    private IEnumerator PulseSkullRoutine(int index)
    {
        if (index < 0 || index >= 3 || skullRenderers[index] == null) yield break;

        Transform skullTransform = skullRenderers[index].transform;
        Vector3 orig = skullOriginalScales[index];

        float duration = 0.35f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Efeito de pulso elástico: sobe até 1.4x e depois retorna a 1.0x
            float scaleMult = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.40f;
            if (skullTransform != null)
            {
                skullTransform.localScale = orig * scaleMult;
            }
            yield return null;
        }

        if (skullTransform != null)
        {
            skullTransform.localScale = orig;
        }
    }

    public void UpdateNickname(string newNickname)
    {
        if (nameTextMesh == null) return;
        if (knockdownLogic != null && knockdownLogic.CurrentLives.Value <= 0) return;

        string display = !string.IsNullOrWhiteSpace(newNickname)
            ? newNickname
            : (knockdownLogic != null ? knockdownLogic.GetHunterDisplayName() : "A Morte");
        nameTextMesh.text = display;
    }

    void LateUpdate()
    {
        if (container == null) return;

        // 1. Oculta se o Hunter foi eliminado/morto
        bool isDead = (playerController != null && (playerController.IsDead || playerController.IsGhost)) ||
                      (knockdownLogic != null && knockdownLogic.CurrentLives.Value <= 0);

        if (isDead)
        {
            if (container.activeSelf) container.SetActive(false);
            return;
        }

        // 2. Não aparece enquanto está caindo na bola de fogo (intro de spawn) nem antes do jogo começar oficialmente
        if (hunterSpawnIntro == null)
        {
            hunterSpawnIntro = GetComponent<HunterSpawnIntroSequence>();
        }

        bool isFallingInIntro = (hunterSpawnIntro != null && hunterSpawnIntro.IsSpawningIntro);
        bool isWaitingGameStart = (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive);

        if (isFallingInIntro || isWaitingGameStart)
        {
            if (container.activeSelf) container.SetActive(false);
            return;
        }

        // 3. Visível para todo mundo (tanto sobreviventes quanto hunters) após o jogo começar
        if (!container.activeSelf) container.SetActive(true);

        Camera cam = Camera.main;
        if (cam == null) cam = Camera.current;
        if (cam == null) return;

        // Posição: ancorada com folga confortável acima da cabeça/capuz do Hunter
        Vector3 headPos = (headBone != null) ? headBone.position : transform.position + Vector3.up * 1.8f;
        container.transform.position = headPos + Vector3.up * heightAboveHead;

        // Rotação: Billboard perfeito alinhado à câmera de quem está observando
        container.transform.rotation = cam.transform.rotation;

        // Escala dinâmica suave com a distância para garantir visibilidade nítida
        float dist = Vector3.Distance(cam.transform.position, container.transform.position);
        float distFactor = Mathf.Clamp(dist / 8.5f, 0.85f, 2.2f);
        container.transform.localScale = baseScale * distFactor;
    }

    private static Sprite GetBackdropSprite()
    {
        if (cachedBackdropSprite != null) return cachedBackdropSprite;

        int w = 160;
        int h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = "HunterOverheadBadge_Generated";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color bgColor = new Color(0.06f, 0.05f, 0.09f, 0.85f);
        Color borderColor = new Color(0.85f, 0.22f, 0.22f, 0.90f);
        Color borderGlow = new Color(0.95f, 0.15f, 0.15f, 0.35f);
        float radius = h * 0.45f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x, radius, w - radius);
                float cy = h * 0.5f;
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));

                if (d > radius)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (d > radius - 2.5f)
                {
                    float edgeAlpha = Mathf.Clamp01((radius - d) / 1.5f);
                    tex.SetPixel(x, y, new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * edgeAlpha));
                }
                else if (d > radius - 5.5f)
                {
                    float glowAlpha = Mathf.Clamp01((radius - 2.5f - d) / 3.0f);
                    Color c = Color.Lerp(borderColor, borderGlow, 1f - glowAlpha);
                    tex.SetPixel(x, y, c);
                }
                else
                {
                    tex.SetPixel(x, y, bgColor);
                }
            }
        }
        tex.Apply();

        cachedBackdropSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return cachedBackdropSprite;
    }
}
