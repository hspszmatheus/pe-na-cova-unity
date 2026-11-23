using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gerencia a sequência cinemática de spawn da Morte (Hunter):
/// - Inicia alto nos céus acima do centro da praça.
/// - Desce em alta velocidade durante exatamente 5 segundos de ponta cabeça.
/// - Envolto em chamas intensas com a ponta da bola de fogo virada para baixo e girando horizontalmente em espiral.
/// - Exibe um HUDzinho exclusivo para o Hunter no centro da tela com a contagem regressiva (5, 4, 3, 2, 1) seguido por "COMEÇAR!".
/// - Ao atingir o solo (5s), gera um impacto colossal de fogo e estilhaços no chão e exibe "COMEÇAR!".
/// - Libera os controles para a caçada começar.
/// </summary>
public class HunterSpawnIntroSequence : NetworkBehaviour
{
    [Header("Configurações da Queda")]
    [Tooltip("Duração total da contagem regressiva e queda em segundos")]
    [SerializeField] private float fallDuration = 5.0f;

    [Tooltip("Altura inicial no céu de onde o Hunter despenca")]
    [SerializeField] private float fallHeight = 115.0f;

    [Tooltip("Velocidade da rotação horizontal da bola de fogo em graus por segundo")]
    [SerializeField] private float fireballHorizontalSpinSpeed = 240.0f;

    [Header("Prefabs de Efeitos")]
    [Tooltip("Prefab das chamas que envolvem o Hunter durante a queda")]
    [SerializeField] private GameObject fallingFlamesPrefab;

    [Tooltip("Prefab do impacto no chão (estilhaços, cratera, onda de choque)")]
    [SerializeField] private GameObject groundImpactPrefab;

    [Tooltip("Prefab do estouro de fogo no solo")]
    [SerializeField] private GameObject fireBurstPrefab;

    private static readonly HashSet<HunterSpawnIntroSequence> activeInstances = new HashSet<HunterSpawnIntroSequence>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        activeInstances.Clear();
    }

    /// <summary>
    /// Indica se alguma Morte (Hunter) ainda está executando a queda dos céus no cenário.
    /// </summary>
    public static bool IsAnySpawningIntro
    {
        get
        {
            foreach (var instance in activeInstances)
            {
                if (instance != null && instance.isSpawningIntro)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private bool isSpawningIntro = false;
    public bool IsSpawningIntro => isSpawningIntro;
    public float FallProgress => fallDuration > 0.001f ? Mathf.Clamp01(currentElapsed / fallDuration) : 1f;

    private Vector3 groundPosition;
    private Vector3 skyPosition;
    private Transform visualsTransform;
    private GameObject activeFlamesInstance;
    private CharacterController characterController;
    private PlayerController playerController;

    private float currentElapsed = 0f;
    private float fireballSpinAngle = 0f;
    private float bannerTimer = 0f;
    private bool impactTriggered = false;

    // Cache de estilos para fallback de GUI (caso GameHUD não esteja presente)
    private GUIStyle fallbackCountdownStyle;
    private GUIStyle fallbackShadowStyle;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();

        // Localiza o Transform Visuals que contém a malha e rig do Hunter
        visualsTransform = transform.Find("Visuals");
        if (visualsTransform == null)
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim != null) visualsTransform = anim.transform;
            else visualsTransform = transform;
        }

        activeInstances.Add(this);
        EnsurePrefabsLoaded();
    }

    private void OnEnable()
    {
        activeInstances.Add(this);
        HunterPreMatchVisibility.RefreshAll();
    }

    private void EnsurePrefabsLoaded()
    {
        if (fallingFlamesPrefab == null)
        {
            fallingFlamesPrefab = Resources.Load<GameObject>("FX_Hunter_Falling_Flames");
#if UNITY_EDITOR
            if (fallingFlamesPrefab == null)
            {
                fallingFlamesPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Hunter_Falling_Flames.prefab");
            }
#endif
        }

        if (groundImpactPrefab == null)
        {
            groundImpactPrefab = Resources.Load<GameObject>("FX_Hunter_Spawn_Impact");
#if UNITY_EDITOR
            if (groundImpactPrefab == null)
            {
                groundImpactPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Hunter_Spawn_Impact.prefab");
            }
#endif
        }

        if (fireBurstPrefab == null)
        {
            fireBurstPrefab = Resources.Load<GameObject>("FX_Fire_Hit_02");
#if UNITY_EDITOR
            if (fireBurstPrefab == null)
            {
                fireBurstPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Fire_Hit_02.prefab");
            }
#endif
        }
    }

    public override void OnNetworkSpawn()
    {
        EnsurePrefabsLoaded();

        if (IsServer)
        {
            // O servidor calcula a posição da praça e dispara a introdução sincronizada para todos os clientes
            Vector3 plazaPos = transform.position;
            StartIntroClientRpc(plazaPos);
        }
    }

    [ClientRpc]
    private void StartIntroClientRpc(Vector3 targetGroundPos)
    {
        StartIntro(targetGroundPos);
    }

    public void StartIntro(Vector3 targetGroundPos)
    {
        EnsurePrefabsLoaded();

        groundPosition = targetGroundPos;
        skyPosition = groundPosition + Vector3.up * fallHeight;
        currentElapsed = 0f;
        fireballSpinAngle = 0f;
        bannerTimer = 0f;
        isSpawningIntro = true;
        impactTriggered = false;
        HunterPreMatchVisibility.RefreshAll();

        // Desativa colisor para descer livremente pelo ar sem colidir com geometrias
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.position = skyPosition;

        // Teleporta no ClientNetworkTransform para evitar interpolação com o chão
        var netTransform = GetComponent<ClientNetworkTransform>();
        if (netTransform != null && IsOwner)
        {
            netTransform.Teleport(skyPosition, transform.rotation, transform.localScale);
        }

        // Gira o modelo 180° de ponta cabeça (cabeça voltada para o chão, foice apontada para baixo)
        if (visualsTransform != null && visualsTransform != transform)
        {
            visualsTransform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        }

        // Instancia as chamas envolvendo todo o corpo do Hunter com a ponta virada para baixo
        if (fallingFlamesPrefab != null && activeFlamesInstance == null)
        {
            activeFlamesInstance = Instantiate(fallingFlamesPrefab, transform.position, Quaternion.Euler(90f, 0f, 0f), transform);
            activeFlamesInstance.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            activeFlamesInstance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            activeFlamesInstance.transform.localScale = new Vector3(2.6f, 2.6f, 2.6f);
        }

        StopAllCoroutines();
        GameAudioManager.Instance?.StartMeteorFallLoop(transform);
        StartCoroutine(FallDescentRoutine());
    }

    private IEnumerator FallDescentRoutine()
    {
        currentElapsed = 0f;

        // Executa estritamente durante os 5 segundos completos via Time.deltaTime
        while (currentElapsed < fallDuration)
        {
            currentElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(currentElapsed / fallDuration);
            GameAudioManager.Instance?.UpdateMeteorFallPitch(progress);

            // Curva acelerada da queda atmosférica (começa rápida e acelera intensamente até o chão)
            float curve = Mathf.Pow(progress, 1.45f);
            transform.position = Vector3.Lerp(skyPosition, groundPosition, curve);

            // Mantém o Hunter de ponta cabeça durante toda a descida
            if (visualsTransform != null && visualsTransform != transform)
            {
                visualsTransform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            }

            // A bola de fogo rotaciona horizontalmente enquanto cai com a ponta virada para baixo
            fireballSpinAngle += fireballHorizontalSpinSpeed * Time.deltaTime;
            if (activeFlamesInstance != null)
            {
                activeFlamesInstance.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                // Ponta virada para baixo (Euler 90) + rotação horizontal contínua em torno de Vector3.up (espiral/corkscrew)
                activeFlamesInstance.transform.rotation = Quaternion.AngleAxis(fireballSpinAngle, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            }

            // Atualiza o HUDzinho de contagem regressiva exclusivo no centro da tela da Morte (IsOwner)
            if (IsOwner)
            {
                float remaining = Mathf.Max(0.001f, fallDuration - currentElapsed);
                int number = Mathf.CeilToInt(remaining);
                if (number > 5) number = 5;
                if (number < 1) number = 1;

                float frac = remaining - Mathf.Floor(remaining);
                float pulseScale = Mathf.Lerp(1.20f, 1.0f, frac);

                if (GameHUDController.Instance != null)
                {
                    GameHUDController.Instance.ShowHunterCountdown(number, pulseScale);
                }
            }

            yield return null;
        }

        // Chegada ao solo aos 5.0 segundos exatos
        CompleteLanding();
    }

    private void CompleteLanding()
    {
        if (impactTriggered) return;
        impactTriggered = true;
        isSpawningIntro = false;
        HunterPreMatchVisibility.RefreshAll();

        // Exibe o "COMEÇAR!" logo após o 1 na aterrissagem
        if (IsOwner && GameHUDController.Instance != null)
        {
            GameHUDController.Instance.ShowHunterStartBanner();
        }
        else
        {
            bannerTimer = 1.1f;
        }

        // Posiciona exatamente no solo e desvira imediatamente para a postura em pé
        transform.position = groundPosition;
        if (visualsTransform != null && visualsTransform != transform)
        {
            visualsTransform.localRotation = Quaternion.identity;
        }

        // Reativa colisão e movimentação
        if (characterController != null)
        {
            characterController.enabled = true;
        }

        // Destrói as chamas da queda
        if (activeFlamesInstance != null)
        {
            Destroy(activeFlamesInstance);
            activeFlamesInstance = null;
        }

        // Encontra a superfície real do piso para o impacto ficar perfeitamente colado ao solo (sem flutuar)
        Vector3 exactGroundPoint = groundPosition;
        if (Physics.Raycast(groundPosition + Vector3.up * 1.5f, Vector3.down, out RaycastHit groundHit, 5.0f))
        {
            exactGroundPoint = groundHit.point + Vector3.up * 0.01f;
        }
        else
        {
            exactGroundPoint = groundPosition - Vector3.up * 0.40f;
        }

        GameAudioManager.Instance?.PlayMeteorImpact(exactGroundPoint);

        // 1. Spawna o impacto de solo no centro da praça (estilhaços, cratera, onda de choque) rente ao piso
        if (groundImpactPrefab != null)
        {
            GameObject impactObj = Instantiate(groundImpactPrefab, exactGroundPoint, Quaternion.identity);
            Destroy(impactObj, 4.0f);
        }

        // 2. Spawna o estouro explosivo de fogo rente ao piso
        if (fireBurstPrefab != null)
        {
            GameObject fireObj = Instantiate(fireBurstPrefab, exactGroundPoint + Vector3.up * 0.05f, Quaternion.identity);
            fireObj.transform.localScale = new Vector3(2.4f, 2.4f, 2.4f);
            Destroy(fireObj, 3.5f);
        }

        Debug.Log($"[HunterSpawnIntro] A Morte aterrissou na praça central ({groundPosition}) com impacto de fogo total! Contagem finalizada: COMEÇAR!");
    }

    private void Update()
    {
        if (bannerTimer > 0f)
        {
            bannerTimer -= Time.deltaTime;
        }
    }

    private void OnDisable()
    {
        activeInstances.Remove(this);
        HunterPreMatchVisibility.RefreshAll();
        GameAudioManager.Instance?.StopMeteorFallLoop();
        if (IsOwner && GameHUDController.Instance != null)
        {
            GameHUDController.Instance.HideHunterCountdown();
        }
    }

    public override void OnDestroy()
    {
        activeInstances.Remove(this);
        HunterPreMatchVisibility.RefreshAll();
        if (IsOwner && GameHUDController.Instance != null)
        {
            GameHUDController.Instance.HideHunterCountdown();
        }
        if (activeFlamesInstance != null)
        {
            Destroy(activeFlamesInstance);
            activeFlamesInstance = null;
        }
        base.OnDestroy();
    }

    private void OnGUI()
    {
        // Se o GameHUDController estiver presente, o HUDzinho moderno do UI Toolkit gerencia a exibição
        if (GameHUDController.Instance != null) return;

        // Fallback apenas para cenas de teste isoladas sem o GameHUD principal
        if (!isSpawningIntro && bannerTimer <= 0f) return;
        if (playerController != null && !playerController.IsOwner) return;

        InitStylesIfNeeded();

        float screenW = Screen.width;
        float screenH = Screen.height;

        if (isSpawningIntro)
        {
            float remaining = Mathf.Max(0.001f, fallDuration - currentElapsed);
            int number = Mathf.CeilToInt(remaining);
            if (number > 5) number = 5;
            if (number < 1) number = 1;

            float boxW = 160f;
            float boxH = 160f;
            float boxX = (screenW - boxW) * 0.5f;
            float boxY = (screenH - boxH) * 0.5f;

            string numberText = number.ToString();
            GUI.Label(new Rect(boxX + 3, boxY + 3, boxW, boxH), numberText, fallbackShadowStyle);
            GUI.Label(new Rect(boxX, boxY, boxW, boxH), numberText, fallbackCountdownStyle);
        }
        else if (bannerTimer > 0f)
        {
            float boxW = 320f;
            float boxH = 90f;
            float boxX = (screenW - boxW) * 0.5f;
            float boxY = (screenH - boxH) * 0.5f;

            GUI.Label(new Rect(boxX + 2, boxY + 2, boxW, boxH), "COMEÇAR!", fallbackShadowStyle);
            GUI.Label(new Rect(boxX, boxY, boxW, boxH), "COMEÇAR!", fallbackCountdownStyle);
        }
    }

    private void InitStylesIfNeeded()
    {
        if (fallbackCountdownStyle == null)
        {
            fallbackCountdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 86,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.65f, 0.15f) }
            };

            fallbackShadowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 86,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0f, 0f, 0f, 0.95f) }
            };
        }
    }
}
