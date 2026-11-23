#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Diretor Cinemático e Coreógrafo de Bots do Trailer Oficial de PÉ NA COVA.
/// 100% Baseado nos Sistemas Reais do Jogo:
/// - Exclusivamente UM Hunter durante toda a narrativa.
/// - Estação de Tarefa Real com o contorno/silhueta ROXA original do jogo (TargetTaskRenderingLayerMask).
/// - Animações e movimentações orgânicas usando os parâmetros reais do PlayerAnimator e velocidades corretas.
/// - Reação física autêntica do Chute no Hunter através do próprio HunterKnockdownLogic (arco balístico de pino de boliche, tombo 3D e recuperação).
/// - Sincronização milimétrica com os 67.0s de 'Danse Macabre' (05:15 a 06:22), finalizando no solo de oboé do canto do galo sobre a tela de título.
/// </summary>
public class GameTrailerDirector : MonoBehaviour
{
    private const string FlagFileName = "record_trailer.flag";
    private static string FlagFilePath => @"C:\Users\hspsz\.gemini\antigravity\brain\a9d11376-b75b-4d20-9c18-cabb3e5829bd\scratch\record_trailer.flag";
    private static string OutputFramesDir => @"C:\Users\hspsz\.gemini\antigravity\brain\a9d11376-b75b-4d20-9c18-cabb3e5829bd\scratch\trailer_frames";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CheckAndStartTrailerDirector()
    {
        if (File.Exists(FlagFilePath))
        {
            Debug.Log("[GameTrailerDirector] Flag de gravação detectada! Inicializando Diretor Cinemático Oficial...");
            GameObject directorGo = new GameObject("[GAME_TRAILER_DIRECTOR]");
            directorGo.AddComponent<GameTrailerDirector>();
            DontDestroyOnLoad(directorGo);
        }
    }

    private Camera trailerCamera;
    private RenderTexture renderTexture;
    private Texture2D captureTexture;

    private int currentFrame = 0;
    private const int TotalTargetFrames = 2010; // Exatamente 67.00s a 30fps (05:15 a 06:22)

    // Único Hunter
    private GameObject hunterActor;
    private NavMeshAgent hunterAgent;
    private Animator hunterAnimator;
    private CharacterController hunterController;
    private PlayerAnimationLogic hunterAnimLogic;
    private HunterKnockdownLogic hunterKnockdown;
    private GameObject hunterScythe;
    private GameObject hunterFallingFlames;

    // Sobreviventes
    private List<GameObject> survivorActors = new List<GameObject>();
    private List<NavMeshAgent> survivorAgents = new List<NavMeshAgent>();
    private List<Animator> survivorAnimators = new List<Animator>();
    private List<GameObject> survivorSoulOrbs = new List<GameObject>();

    // Pedestres Civis de Fundo
    private List<GameObject> pedestrianActors = new List<GameObject>();
    private List<NavMeshAgent> pedestrianAgents = new List<NavMeshAgent>();

    // Props e Efeitos
    private TaskObject activeTaskObject;
    private GameObject fireballObj;
    private float cameraTrauma = 0f;

    // Cartela Final de Logo e Wishlist
    private Texture2D titleLogoTex;
    private GUIStyle titleSubtitleStyle;
    private GUIStyle titleWishlistStyle;

    // Coordenadas Chave da Cidade Noturna
    private readonly Vector3 CrashSiteCenter = new Vector3(-80.0f, 0.40f, -30.0f);
    private readonly Vector3 MeteorLandingSpot = new Vector3(-62.0f, 0.15f, -34.8f);
    private readonly Vector3 AvenueCenter = new Vector3(-5.0f, 0.15f, -34.8f);
    private Vector3 realTaskPosition = new Vector3(49.75f, 0.08f, -26.37f);

    private void Awake()
    {
        if (!Directory.Exists(OutputFramesDir))
        {
            Directory.CreateDirectory(OutputFramesDir);
        }

#if UNITY_EDITOR
        Selection.activeGameObject = null;
#endif

        Time.captureFramerate = 30;

        // Desativa cutscenes automáticas de início e elementos de HUD
        var planeCutscene = FindObjectOfType<AirplaneCrashCutsceneController>();
        if (planeCutscene != null) planeCutscene.gameObject.SetActive(false);

        var hud = GameObject.Find("GameHUD");
        if (hud != null) hud.SetActive(false);

        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            canvas.enabled = false;
        }

        foreach (var cam in FindObjectsOfType<Camera>())
        {
            cam.enabled = false;
        }

        // Desativa linhas de boundary
        foreach (var b in FindObjectsOfType<CityBoundaryVisualController>(true))
        {
            b.gameObject.SetActive(false);
        }

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go != null && (go.name.StartsWith("Boundary") || go.name.StartsWith("CityBoundary")))
            {
                go.SetActive(false);
            }
        }

        // Iluminação noturna com postes acesos
        var skyboxManager = FindObjectOfType<MatchSkyboxManager>();
        if (skyboxManager != null)
        {
            skyboxManager.enabled = false;
            skyboxManager.ApplyPreset(1); // Noite Estrelada
        }

        foreach (var sl in FindObjectsOfType<StreetlightFixture>(true))
        {
            if (sl != null) sl.SetLightState(true);
        }

        // Configura Câmera Cinemática 1080p
        GameObject camGo = new GameObject("TrailerCinematicCamera");
        trailerCamera = camGo.AddComponent<Camera>();
        trailerCamera.tag = "MainCamera";
        trailerCamera.clearFlags = CameraClearFlags.Skybox;
        trailerCamera.fieldOfView = 50f;
        trailerCamera.nearClipPlane = 0.2f;
        trailerCamera.farClipPlane = 600f;
        trailerCamera.cullingMask = ~(1 << 5); // Exclui UI padrão
        camGo.AddComponent<AudioListener>();

        renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        renderTexture.antiAliasing = 4;
        trailerCamera.targetTexture = renderTexture;

        captureTexture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);

        // Carrega logo para a cartela de encerramento
        titleLogoTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Sprites/Cartoon/pe_na_cova_title_logo.png");

        SetupActorsAndEnvironment();

        Debug.Log($"[GameTrailerDirector] Gravando {TotalTargetFrames} quadros orgânicos (67.0s) em: {OutputFramesDir}");
    }

    private void SetupActorsAndEnvironment()
    {
        RuntimeAnimatorController animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/PlayerAnimator.controller");

        // 1. Localiza a estação real de Tarefa (Gas Pump) e ativa a Silhueta Roxa do jogo!
        var gasPump = GameObject.Find("Gas_Pump_1A (10)") ?? GameObject.Find("Gas_Pump_1A (7)") ?? GameObject.Find("Gas_Pump_1A (12)");
        if (gasPump != null)
        {
            activeTaskObject = gasPump.GetComponent<TaskObject>();
            if (activeTaskObject == null) activeTaskObject = gasPump.AddComponent<TaskObject>();
            realTaskPosition = gasPump.transform.position;

            // Ativa o contorno ROXO oficial das Tasks de Sobreviventes!
            activeTaskObject.SetAsLocalTarget(true);
            Debug.Log($"[GameTrailerDirector] Task Real configurada em {realTaskPosition} com silhueta roxa ativada!");
        }

        // 2. Instancia o Único Hunter (Ceifador)
        GameObject hunterPrefab = Resources.Load<GameObject>("Hunter");
        if (hunterPrefab == null)
        {
            hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Hunter.prefab");
        }

        if (hunterPrefab != null)
        {
            hunterActor = new GameObject("Trailer_Hunter");
            Transform hunterVisuals = hunterPrefab.transform.Find("Visuals");
            if (hunterVisuals != null)
            {
                GameObject vis = Instantiate(hunterVisuals.gameObject, hunterActor.transform);
                vis.SetActive(true);
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = Quaternion.identity;

                hunterAnimator = vis.GetComponent<Animator>();
                if (hunterAnimator == null) hunterAnimator = vis.GetComponentInChildren<Animator>();
                if (hunterAnimator != null && animController != null)
                {
                    hunterAnimator.runtimeAnimatorController = animController;
                    hunterAnimator.applyRootMotion = false;
                }

                foreach (var t in vis.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Death_Scythe")
                    {
                        hunterScythe = t.gameObject;
                        hunterScythe.SetActive(false);

                        // Efeito de fogo na foice para o ato final
                        GameObject fireScythePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/ScytheFireFX.prefab");
                        if (fireScythePrefab != null)
                        {
                            GameObject fireFx = Instantiate(fireScythePrefab, hunterScythe.transform);
                            fireFx.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                        }
                        break;
                    }
                }
            }

            // Componentes de Movimentação e Animação
            hunterController = hunterActor.AddComponent<CharacterController>();
            hunterController.height = 1.8f;
            hunterController.radius = 0.45f;
            hunterController.center = new Vector3(0f, 0.9f, 0f);

            hunterAnimLogic = hunterActor.AddComponent<PlayerAnimationLogic>();
            hunterAnimLogic.Initialize();

            // Componente de Física do Chute oficial do jogo
            hunterKnockdown = hunterActor.AddComponent<HunterKnockdownLogic>();
            hunterKnockdown.Initialize();

            hunterAgent = hunterActor.AddComponent<NavMeshAgent>();
            hunterAgent.speed = 1.8f;
            hunterAgent.acceleration = 14f;
            hunterAgent.stoppingDistance = 0.5f;
            hunterAgent.radius = 0.45f;
            hunterAgent.height = 1.8f;
            hunterAgent.updateRotation = true;

            // Inicializa no céu para a descida triunfal
            hunterAgent.enabled = false;
            hunterActor.transform.position = MeteorLandingSpot + new Vector3(0f, 95f, 0f);

            GameObject flamesPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Hunter_Falling_Flames.prefab");
            if (flamesPrefab != null)
            {
                hunterFallingFlames = Instantiate(flamesPrefab, hunterActor.transform);
                hunterFallingFlames.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                hunterFallingFlames.SetActive(true);
            }
        }

        // 3. Spawna 3 Sobreviventes com modelos e animações oficiais
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        GameObject soulOrbPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/SoulOrb.prefab");

        int[] survivorModelIndices = { 0, 4, 7 }; // Character 01, Character 05, Character 08
        Vector3[] initialSurvivorPositions = {
            realTaskPosition + new Vector3(-0.95f, 0f, 0.2f),  // Trabalhando na Task
            realTaskPosition + new Vector3(-5.2f, 0f, -4.5f),  // De vigia na calçada
            realTaskPosition + new Vector3(-11.5f, 0f, -2.0f)  // Recuado atento
        };

        for (int i = 0; i < survivorModelIndices.Length; i++)
        {
            GameObject survivor = new GameObject($"Trailer_Survivor_{i}");
            var capCol = survivor.AddComponent<CapsuleCollider>();
            capCol.height = 1.8f;
            capCol.radius = 0.4f;
            capCol.center = new Vector3(0f, 0.9f, 0f);

            NavMeshAgent agent = survivor.AddComponent<NavMeshAgent>();
            agent.speed = 1.6f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.5f;
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.updateRotation = true;

            if (playerPrefab != null)
            {
                Transform visualsGroup = playerPrefab.transform.Find("Visuals");
                if (visualsGroup != null && visualsGroup.childCount > 0)
                {
                    int mIdx = survivorModelIndices[i] % visualsGroup.childCount;
                    GameObject vis = Instantiate(visualsGroup.GetChild(mIdx).gameObject, survivor.transform);
                    vis.SetActive(true);
                    vis.transform.localPosition = Vector3.zero;
                    vis.transform.localRotation = Quaternion.identity;

                    Animator anim = vis.GetComponent<Animator>();
                    if (anim == null) anim = vis.GetComponentInChildren<Animator>();
                    if (anim != null && animController != null)
                    {
                        anim.runtimeAnimatorController = animController;
                        anim.applyRootMotion = false;
                        SetActorAnimState(anim, false, false);
                    }

                    if (soulOrbPrefab != null)
                    {
                        GameObject orb = Instantiate(soulOrbPrefab, survivor.transform);
                        orb.transform.localPosition = new Vector3(0f, 1.2f, 0.15f);
                        orb.transform.localScale = Vector3.one * 0.28f;
                        orb.SetActive(false);
                        survivorSoulOrbs.Add(orb);
                    }

                    survivorActors.Add(survivor);
                    survivorAgents.Add(agent);
                    survivorAnimators.Add(anim);
                }
            }

            if (NavMesh.SamplePosition(initialSurvivorPositions[i], out NavMeshHit sHit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(sHit.position);
            }
            else
            {
                survivor.transform.position = initialSurvivorPositions[i];
            }
        }

        // 4. Dois Pedestres Civis caminhando na calçada ao longe
        for (int i = 0; i < 2; i++)
        {
            Vector3 npcStart = realTaskPosition + new Vector3(-20f + i * 15f, 0f, -12f);
            GameObject npc = new GameObject($"Trailer_Pedestrian_{i}");
            var nAgent = npc.AddComponent<NavMeshAgent>();
            nAgent.speed = 1.3f;
            nAgent.radius = 0.4f;
            nAgent.height = 1.8f;
            nAgent.updateRotation = true;

            if (playerPrefab != null)
            {
                Transform visualsGroup = playerPrefab.transform.Find("Visuals");
                if (visualsGroup != null && visualsGroup.childCount > 0)
                {
                    int mIdx = (i + 2) % visualsGroup.childCount;
                    GameObject vis = Instantiate(visualsGroup.GetChild(mIdx).gameObject, npc.transform);
                    vis.SetActive(true);
                    vis.transform.localPosition = Vector3.zero;
                    vis.transform.localRotation = Quaternion.identity;

                    Animator anim = vis.GetComponent<Animator>();
                    if (anim == null) anim = vis.GetComponentInChildren<Animator>();
                    if (anim != null && animController != null)
                    {
                        anim.runtimeAnimatorController = animController;
                        anim.applyRootMotion = false;
                        SetActorAnimState(anim, true, false);
                    }
                }
            }

            if (NavMesh.SamplePosition(npcStart, out NavMeshHit nHit, 5f, NavMesh.AllAreas))
            {
                nAgent.Warp(nHit.position);
                nAgent.SetDestination(npcStart + new Vector3(25f, 0f, 0f));
            }

            pedestrianActors.Add(npc);
            pedestrianAgents.Add(nAgent);
        }

        // 5. Projétil da Bola de Fogo
        GameObject fbPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/HunterFireball.prefab");
        if (fbPrefab != null)
        {
            fireballObj = Instantiate(fbPrefab);
            fireballObj.SetActive(false);
        }

        // 6. Luz de Destaque para o Duelo Noturno na Avenida
        GameObject sdLightGo = new GameObject("ShowdownCinematicLight");
        sdLightGo.transform.position = new Vector3(-7.5f, 5.0f, -34.8f);
        Light sdLight = sdLightGo.AddComponent<Light>();
        sdLight.type = LightType.Point;
        sdLight.color = new Color(1.0f, 0.94f, 0.82f);
        sdLight.range = 18.0f;
        sdLight.intensity = 3.5f;
    }

    /// <summary>
    /// Aplica os estados corretos no Animator conforme a lógica real de PlayerAnimationLogic:
    /// Para Correr: Walking = true, Running = true, IsGrounded = true.
    /// Para Andar: Walking = true, Running = false, IsGrounded = true.
    /// Para Parar: Walking = false, Running = false, IsGrounded = true.
    /// </summary>
    private static void SetActorAnimState(Animator anim, bool isWalking, bool isRunning)
    {
        if (anim == null) return;
        anim.SetBool("Walking", isWalking);
        anim.SetBool("Running", isRunning && isWalking);
        anim.SetBool("IsGrounded", true);
    }

    private void LateUpdate()
    {
        if (currentFrame >= TotalTargetFrames)
        {
            FinishTrailerCapture();
            return;
        }

        UpdateCinematicTimeline(currentFrame);

        trailerCamera.Render();

        RenderTexture.active = renderTexture;
        captureTexture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        captureTexture.Apply();
        RenderTexture.active = null;

        // Se estiver no Ato 7 (canto do galo no oboé), desenha o logo e wishlist sobre a textura capturada
        if (currentFrame >= 1831)
        {
            DrawTitleLogoOverlay(currentFrame);
        }

        byte[] jpgBytes = captureTexture.EncodeToJPG(92);
        string frameFileName = Path.Combine(OutputFramesDir, $"frame_{currentFrame:D5}.jpg");
        File.WriteAllBytes(frameFileName, jpgBytes);

        currentFrame++;
    }

    private void DrawTitleLogoOverlay(int frame)
    {
        try
        {
            // 1831 (61.0s) a 2010 (67.0s)
            // Fade in suave entre 1831 e 1860 (quando soa o canto do galo no oboé)
            float alpha = Mathf.Clamp01((frame - 1831) / 28f);

            // Fade out sutil nos últimos 15 quadros (66.5s a 67.0s)
            if (frame > 1995)
            {
                alpha *= Mathf.Clamp01((2010 - frame) / 15f);
            }

            if (alpha <= 0.01f || titleLogoTex == null) return;

            // Aplica vinheta preta transparente no fundo
            Color[] pixels = captureTexture.GetPixels();
            float bgDarken = alpha * 0.75f;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.Lerp(pixels[i], Color.black, bgDarken);
            }

            // Blit do logo centralizado se legível
            int logoW = 860;
            int logoH = (int)(logoW * ((float)titleLogoTex.height / Mathf.Max(1, titleLogoTex.width)));
            int startX = (1920 - logoW) / 2;
            int startY = 1080 / 2 - logoH / 2 + 50;

            Color[] logoPixels = titleLogoTex.GetPixels();
            int origW = titleLogoTex.width;
            int origH = titleLogoTex.height;

            for (int y = 0; y < logoH; y++)
            {
                int texY = (int)((y / (float)logoH) * origH);
                int destY = startY + y;
                if (destY < 0 || destY >= 1080) continue;

                for (int x = 0; x < logoW; x++)
                {
                    int texX = (int)((x / (float)logoW) * origW);
                    int destX = startX + x;
                    if (destX < 0 || destX >= 1920) continue;

                    Color src = logoPixels[texY * origW + texX];
                    float srcA = src.a * alpha;
                    if (srcA > 0.01f)
                    {
                        int pIdx = destY * 1920 + destX;
                        pixels[pIdx] = Color.Lerp(pixels[pIdx], new Color(src.r, src.g, src.b, 1f), srcA);
                    }
                }
            }

            captureTexture.SetPixels(pixels);
            captureTexture.Apply();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameTrailerDirector] Overlay do logo no frame {frame}: {ex.Message}");
        }
    }

    private void UpdateCinematicTimeline(int frame)
    {
        float dt = 1f / 30f;

        // =========================================================================
        // ATO 1: O DESASTRE & A CHEGADA DO CEIFADOR (0 - 240 | 0.0s - 8.0s)
        // Danse Macabre: Início da valsa misteriosa e violinos soturnos
        // =========================================================================
        if (frame < 240)
        {
            if (frame < 120)
            {
                // Drone cinemático sobrevoando a fumaça e destroços do avião
                float t = frame / 120f;
                Vector3 camStart = CrashSiteCenter + new Vector3(-14.0f, 18.0f, 16.0f);
                Vector3 camEnd = CrashSiteCenter + new Vector3(9.0f, 4.2f, -1.0f);
                Vector3 camPos = Vector3.Lerp(camStart, camEnd, SmoothStep(t));
                Vector3 lookTarget = CrashSiteCenter + new Vector3(0f, 1.5f, 0f);

                PositionCamera(camPos, lookTarget, Mathf.Lerp(52f, 48f, t));
            }
            else if (frame < 180)
            {
                // Meteoro flamejante desce em velocidade terminal cortando a noite
                float dropT = (frame - 120) / 60f;
                float dropY = Mathf.Lerp(95f, 0.15f, Mathf.Pow(dropT, 2.4f));

                if (hunterActor != null)
                {
                    hunterActor.transform.position = MeteorLandingSpot + new Vector3(0f, dropY, 0f);
                    hunterActor.transform.Rotate(0f, 360f * dt, 0f);
                }

                Vector3 camPos = MeteorLandingSpot + new Vector3(-11.5f, 1.8f, -11.0f);
                Vector3 lookTarget = hunterActor != null ? hunterActor.transform.position : MeteorLandingSpot;
                PositionCamera(camPos, lookTarget, 52f);
            }
            else
            {
                // IMPACTO RETUMBANTE NO ASFALTO!
                if (frame == 180)
                {
                    if (hunterFallingFlames != null) hunterFallingFlames.SetActive(false);

                    GameObject impactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Hunter_Spawn_Impact.prefab");
                    if (impactPrefab != null)
                    {
                        GameObject imp = Instantiate(impactPrefab, MeteorLandingSpot, Quaternion.identity);
                        Destroy(imp, 5f);
                    }
                    cameraTrauma = 1.0f;

                    if (hunterActor != null)
                    {
                        hunterActor.transform.position = MeteorLandingSpot;
                        hunterActor.transform.rotation = Quaternion.Euler(0f, 215f, 0f);
                        SetActorAnimState(hunterAnimator, false, false);
                    }
                }

                // Câmera heroica contra-plongée (ângulo baixo) contemplando o Ceifador
                float t = (frame - 180) / 60f;
                Vector3 camPos = MeteorLandingSpot + Vector3.Lerp(new Vector3(-8.5f, 1.35f, -8.0f), new Vector3(-6.8f, 1.25f, -6.5f), SmoothStep(t));
                Vector3 lookTarget = MeteorLandingSpot + new Vector3(0f, 1.55f, 0f);
                PositionCamera(camPos, lookTarget, 46f);
            }
        }
        // =========================================================================
        // ATO 2: SOBREVIVENTES & ESTAÇÃO REAL COM SILHUETA ROXA (240 - 540 | 8.0s - 18.0s)
        // Danse Macabre: Valsa ganha ritmo e instrumentos de sopro
        // =========================================================================
        else if (frame < 540)
        {
            if (frame == 240)
            {
                // Configura Survivor 0 realizando a Task Real com contorno roxo ativado
                if (survivorActors.Count > 0 && survivorActors[0] != null)
                {
                    Vector3 repairSpot = realTaskPosition + new Vector3(-0.95f, 0.08f, 0.2f);
                    if (survivorAgents[0] != null) survivorAgents[0].Warp(repairSpot);
                    survivorActors[0].transform.rotation = Quaternion.Euler(0f, 85f, 0f);

                    if (survivorAnimators[0] != null)
                    {
                        SetActorAnimState(survivorAnimators[0], false, false);
                        survivorAnimators[0].SetBool("IsInteracting", true);
                        survivorAnimators[0].ResetTrigger("Interact");
                        survivorAnimators[0].SetTrigger("Interact");
                    }
                }

                // Survivor 1 de vigia atento na esquina
                if (survivorActors.Count > 1 && survivorActors[1] != null)
                {
                    Vector3 guardSpot = realTaskPosition + new Vector3(-4.8f, 0.08f, -3.8f);
                    if (survivorAgents[1] != null) survivorAgents[1].Warp(guardSpot);
                    survivorActors[1].transform.rotation = Quaternion.Euler(0f, 245f, 0f);
                    SetActorAnimState(survivorAnimators[1], false, false);
                }

                // Survivor 2 recuado vigiando o acesso
                if (survivorActors.Count > 2 && survivorActors[2] != null)
                {
                    Vector3 backSpot = realTaskPosition + new Vector3(-7.2f, 0.08f, 1.8f);
                    if (survivorAgents[2] != null) survivorAgents[2].Warp(backSpot);
                    survivorActors[2].transform.rotation = Quaternion.Euler(0f, 120f, 0f);
                    SetActorAnimState(survivorAnimators[2], false, false);
                }
            }

            if (frame < 430)
            {
                // Travelling lento enquadrando a bomba de gasolina brilhando em roxo e o survivor consertando
                float t = (frame - 240) / 190f;
                Vector3 camPos = realTaskPosition + Vector3.Lerp(new Vector3(-2.8f, 1.25f, -2.4f), new Vector3(-1.8f, 1.15f, -1.6f), SmoothStep(t));
                Vector3 lookTarget = realTaskPosition + new Vector3(0.1f, 0.85f, 0.1f);
                PositionCamera(camPos, lookTarget, 44f);
            }
            else
            {
                // Conclusão da tarefa: o sobrevivente se ergue e se reúne com os aliados
                if (frame == 430)
                {
                    if (survivorAnimators[0] != null)
                    {
                        survivorAnimators[0].SetBool("IsInteracting", false);
                        SetActorAnimState(survivorAnimators[0], true, false);
                    }
                    if (survivorAgents[0] != null)
                    {
                        survivorAgents[0].speed = 1.6f;
                        survivorAgents[0].SetDestination(realTaskPosition + new Vector3(-4.2f, 0.08f, -3.2f));
                    }
                }

                float t = (frame - 430) / 110f;
                Vector3 camPos = realTaskPosition + Vector3.Lerp(new Vector3(-6.5f, 1.55f, -6.0f), new Vector3(-5.2f, 1.45f, -4.5f), SmoothStep(t));
                Vector3 lookTarget = realTaskPosition + new Vector3(-3.5f, 1.0f, -2.5f);
                PositionCamera(camPos, lookTarget, 48f);
            }
        }
        // =========================================================================
        // ATO 3: A PATRULHA & O RADAR ROXO DE REVELAÇÃO (540 - 840 | 18.0s - 28.0s)
        // Danse Macabre: Tensão orquestral crescente, cordas graves
        // =========================================================================
        else if (frame < 840)
        {
            Vector3 scanCenter = AvenueCenter + new Vector3(0f, 0f, 0f);

            if (frame == 540)
            {
                // Posiciona o Hunter caminhando imponente pelo centro da avenida
                if (hunterActor != null && hunterAgent != null)
                {
                    hunterAgent.enabled = true;
                    if (NavMesh.SamplePosition(scanCenter + new Vector3(-15f, 0.08f, 0f), out NavMeshHit hHit, 5f, NavMesh.AllAreas))
                    {
                        hunterAgent.Warp(hHit.position);
                    }
                    hunterAgent.speed = 1.8f;
                    hunterAgent.SetDestination(scanCenter);
                    SetActorAnimState(hunterAnimator, true, false);
                }

                // Sobreviventes se escondendo atrás dos carros estacionados no asfalto
                if (survivorActors.Count > 0 && survivorActors[0] != null && survivorAgents[0] != null)
                {
                    survivorAgents[0].Warp(scanCenter + new Vector3(8.5f, 0.08f, 2.0f));
                    survivorActors[0].transform.rotation = Quaternion.Euler(0f, 210f, 0f);
                    SetActorAnimState(survivorAnimators[0], false, false);
                }

                if (survivorActors.Count > 1 && survivorActors[1] != null && survivorAgents[1] != null)
                {
                    survivorAgents[1].Warp(scanCenter + new Vector3(10.2f, 0.08f, 2.4f));
                    survivorActors[1].transform.rotation = Quaternion.Euler(0f, 200f, 0f);
                    SetActorAnimState(survivorAnimators[1], false, false);
                }
            }

            if (frame < 640)
            {
                // Marcha imponente sob os postes de luz
                float t = (frame - 540) / 100f;
                Vector3 hPos = hunterActor != null ? hunterActor.transform.position : scanCenter;
                Vector3 camPos = hPos + new Vector3(7.5f, 1.45f, -3.2f);
                Vector3 lookTarget = hPos + new Vector3(0f, 1.55f, 0f);
                PositionCamera(camPos, lookTarget, 46f);
            }
            else if (frame < 740)
            {
                // Hunter para e canaliza a magia com o anel roxo oficial de 16.5m expandindo no asfalto!
                if (frame == 640)
                {
                    if (hunterAgent != null)
                    {
                        hunterAgent.isStopped = true;
                        hunterAgent.velocity = Vector3.zero;
                    }
                    if (hunterAnimator != null)
                    {
                        hunterAnimator.SetBool("Walking", false);
                        hunterAnimator.SetBool("Running", false);
                        hunterAnimator.SetBool("IsCastingReveal", true);
                        hunterAnimator.ResetTrigger("RevealCast");
                        hunterAnimator.SetTrigger("RevealCast");
                    }

                    HunterRevealAreaVisual.Show(hunterActor.transform, 16.5f);
                }

                float t = (frame - 640) / 100f;
                float angle = -35f + t * 45f;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 hPos = hunterActor != null ? hunterActor.transform.position : scanCenter;
                Vector3 camPos = hPos + new Vector3(Mathf.Cos(rad) * 6.8f, 1.25f, Mathf.Sin(rad) * 6.8f);
                Vector3 lookTarget = hPos + new Vector3(0f, 1.55f, 0f);
                PositionCamera(camPos, lookTarget, 48f);
            }
            else
            {
                // PULSO DE REVELAÇÃO: Os orbes de alma brilham no peito dos sobreviventes!
                if (frame == 740)
                {
                    Vector3 hPos = hunterActor != null ? hunterActor.transform.position : scanCenter;
                    HunterRevealAreaVisual.TriggerPulseAndHide(hPos, 16.5f);

                    foreach (var orb in survivorSoulOrbs)
                    {
                        if (orb != null) orb.SetActive(true);
                    }
                }

                float t = (frame - 740) / 100f;
                Vector3 survPos = survivorActors.Count > 0 && survivorActors[0] != null ? survivorActors[0].transform.position : scanCenter;
                Vector3 camPos = survPos + Vector3.Lerp(new Vector3(2.5f, 1.20f, -1.8f), new Vector3(1.8f, 1.15f, -1.4f), SmoothStep(t));
                Vector3 lookTarget = survPos + new Vector3(-0.3f, 1.10f, 0.2f);
                PositionCamera(camPos, lookTarget, 44f);
            }
        }
        // =========================================================================
        // ATO 4: O DISPARO DA BOLA DE FOGO & EVASÃO LIMPA (840 - 1140 | 28.0s - 38.0s)
        // Danse Macabre: Crescendo explosivo e metais vibrantes
        // =========================================================================
        else if (frame < 1140)
        {
            Vector3 combatSpot = AvenueCenter + new Vector3(4.0f, 0.15f, 0f);

            if (frame == 840)
            {
                if (hunterAnimator != null)
                {
                    hunterAnimator.SetBool("IsCastingReveal", false);
                    hunterAnimator.ResetTrigger("FireballAttack");
                    hunterAnimator.SetTrigger("FireballAttack");
                }

                if (hunterActor != null)
                {
                    hunterActor.transform.rotation = Quaternion.Euler(0f, 65f, 0f);
                }
            }

            // O projétil flamejante viaja cortando a avenida
            if (frame >= 870 && frame < 970)
            {
                float fbT = (frame - 870) / 100f;
                Vector3 hPos = hunterActor != null ? hunterActor.transform.position : combatSpot;
                Vector3 targetPos = combatSpot + new Vector3(12.5f, 0.4f, 3.2f);

                if (fireballObj != null)
                {
                    if (!fireballObj.activeSelf) fireballObj.SetActive(true);
                    Vector3 currentBallPos = Vector3.Lerp(hPos + new Vector3(0.8f, 1.6f, 0.4f), targetPos, fbT);
                    fireballObj.transform.position = currentBallPos;

                    Vector3 camPos = currentBallPos + new Vector3(-3.8f, 0.75f, -3.2f);
                    Vector3 lookTarget = currentBallPos + new Vector3(3.5f, 0f, 0.5f);
                    PositionCamera(camPos, lookTarget, 50f);
                }
            }
            else if (frame >= 970)
            {
                // IMPACTO EXPLOSIVO!
                if (frame == 970)
                {
                    if (fireballObj != null) fireballObj.SetActive(false);
                    cameraTrauma = 1.0f;

                    GameObject hitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/FX/FX_Fire_Hit_02.prefab");
                    if (hitPrefab != null)
                    {
                        GameObject hitFx = Instantiate(hitPrefab, combatSpot + new Vector3(12.5f, 0.4f, 3.2f), Quaternion.identity);
                        Destroy(hitFx, 4f);
                    }

                    // Sobreviventes arrancam em corrida limpa e orgânica (Walking = true, Running = true, speed = 7.2f)
                    for (int i = 0; i < survivorAnimators.Count; i++)
                    {
                        if (survivorAnimators[i] != null)
                        {
                            SetActorAnimState(survivorAnimators[i], true, true);
                        }
                        if (survivorAgents[i] != null)
                        {
                            survivorAgents[i].speed = 7.2f;
                            survivorAgents[i].SetDestination(combatSpot + new Vector3(25f + i * 2.5f, 0f, -2.0f));
                        }
                    }
                }

                float t = (frame - 970) / 170f;
                Vector3 hitPoint = combatSpot + new Vector3(12.5f, 0.4f, 3.2f);
                Vector3 camPos = hitPoint + Vector3.Lerp(new Vector3(-5.5f, 2.2f, -5.0f), new Vector3(-3.5f, 1.8f, -4.0f), SmoothStep(t));
                Vector3 lookTarget = hitPoint + new Vector3(2.0f, 0.8f, -1.0f);
                PositionCamera(camPos, lookTarget, 48f);
            }
            else
            {
                Vector3 hPos = hunterActor != null ? hunterActor.transform.position : combatSpot;
                Vector3 camPos = hPos + new Vector3(-4.5f, 1.35f, -3.8f);
                PositionCamera(camPos, hPos + new Vector3(0f, 1.55f, 0f), 46f);
            }
        }
        // =========================================================================
        // ATO 5: O DEBOCHE (DANÇA 360°) & VOADORA COM FÍSICA REAL DO JOGO! (1140 - 1440 | 38.0s - 48.0s)
        // Danse Macabre: Valsa vibrante e cômica
        // =========================================================================
        else if (frame < 1440)
        {
            Vector3 showdownSpot = new Vector3(-7.5f, 0.15f, -36.0f);

            if (frame == 1140)
            {
                foreach (var orb in survivorSoulOrbs)
                {
                    if (orb != null) orb.SetActive(false);
                }

                // Posiciona o Hunter encurralando na avenida
                if (hunterActor != null && hunterAgent != null)
                {
                    hunterAgent.Warp(showdownSpot);
                    hunterActor.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
                    SetActorAnimState(hunterAnimator, false, false);
                }

                // Survivor 0 começa a dançar na cara da Morte!
                if (survivorActors.Count > 0 && survivorActors[0] != null && survivorAgents[0] != null)
                {
                    survivorAgents[0].Warp(showdownSpot + new Vector3(2.6f, 0f, -0.8f));
                    survivorActors[0].transform.rotation = Quaternion.Euler(0f, 290f, 0f);
                    if (survivorAnimators[0] != null)
                    {
                        SetActorAnimState(survivorAnimators[0], false, false);
                        survivorAnimators[0].ResetTrigger("Dance1");
                        survivorAnimators[0].SetTrigger("Dance1");
                    }
                }

                // Survivor 1 também dança provocando
                if (survivorActors.Count > 1 && survivorActors[1] != null && survivorAgents[1] != null)
                {
                    survivorAgents[1].Warp(showdownSpot + new Vector3(2.4f, 0f, 0.8f));
                    survivorActors[1].transform.rotation = Quaternion.Euler(0f, 250f, 0f);
                    if (survivorAnimators[1] != null)
                    {
                        SetActorAnimState(survivorAnimators[1], false, false);
                        survivorAnimators[1].ResetTrigger("Dance2");
                        survivorAnimators[1].SetTrigger("Dance2");
                    }
                }

                // Survivor 2 recuado se preparando para a voadora furtiva
                if (survivorActors.Count > 2 && survivorActors[2] != null && survivorAgents[2] != null)
                {
                    survivorAgents[2].Warp(showdownSpot + new Vector3(-13.0f, 0f, 0f));
                    survivorActors[2].transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    SetActorAnimState(survivorAnimators[2], false, false);
                }
            }

            if (frame < 1260)
            {
                // Órbita fluida mostrando o deboche e a reação atônita da Morte pelo asfalto aberto
                float t = (frame - 1140) / 120f;
                float camAngle = 45f + t * 90f;
                float rad = camAngle * Mathf.Deg2Rad;
                Vector3 camOffset = new Vector3(Mathf.Cos(rad) * 4.8f, 1.4f, Mathf.Sin(rad) * 4.8f);

                float headWobble = Mathf.Sin((frame - 1140) * 0.08f) * 15f;
                if (hunterActor != null)
                {
                    hunterActor.transform.rotation = Quaternion.Euler(0f, 270f + headWobble, 0f);
                }

                PositionCamera(showdownSpot + camOffset, showdownSpot + new Vector3(0.8f, 1.25f, 0f), 48f);
            }
            else if (frame < 1315)
            {
                // SURGIMENTO DA VOADORA! Survivor 2 arranca em disparada e salta no ar!
                if (frame == 1260 && survivorAnimators.Count > 2 && survivorAnimators[2] != null)
                {
                    SetActorAnimState(survivorAnimators[2], true, true);
                    if (survivorAgents[2] != null)
                    {
                        survivorAgents[2].speed = 7.5f;
                        survivorAgents[2].SetDestination(showdownSpot + new Vector3(-0.5f, 0f, 0f));
                    }
                }

                if (frame == 1290 && survivorAnimators.Count > 2 && survivorAnimators[2] != null)
                {
                    if (survivorAgents[2] != null) survivorAgents[2].enabled = false;
                    survivorAnimators[2].ResetTrigger("Kick");
                    survivorAnimators[2].SetTrigger("Kick");
                }

                if (frame >= 1290 && survivorActors.Count > 2 && survivorActors[2] != null)
                {
                    float leapProgress = (frame - 1290) / 25f;
                    float kickerX = Mathf.Lerp(showdownSpot.x - 5.5f, showdownSpot.x - 0.3f, leapProgress);
                    float kickerY = Mathf.Sin(leapProgress * Mathf.PI) * 1.35f + 0.12f;
                    survivorActors[2].transform.position = new Vector3(kickerX, kickerY, showdownSpot.z);
                    survivorActors[2].transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                }

                Vector3 camPos = showdownSpot + new Vector3(-3.2f, 1.35f, 3.8f);
                Vector3 lookTarget = showdownSpot + new Vector3(0f, 1.2f, 0f);
                PositionCamera(camPos, lookTarget, 46f);
            }
            else
            {
                // IMPACTO DO CHUTE: Executa o sistema REAL do jogo (HunterKnockdownLogic.ApplyKick)!
                if (frame == 1315)
                {
                    cameraTrauma = 1.0f;

                    GameObject hitPurple = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Lamin/FX_Purple_Hit_02.prefab");
                    if (hitPurple != null)
                    {
                        GameObject pFx = Instantiate(hitPurple, showdownSpot + new Vector3(0f, 1.2f, 0f), Quaternion.identity);
                        Destroy(pFx, 4f);
                    }

                    // Aplica física de lançamento e tombo 3D do pino de boliche diretamente via código oficial!
                    if (hunterKnockdown != null)
                    {
                        if (hunterAgent != null) hunterAgent.enabled = false;
                        hunterKnockdown.ApplyKick(new Vector3(1.0f, 0.15f, 0f));
                    }
                }

                // Posiciona o chutador aterrissando com firmeza no chão
                if (survivorActors.Count > 2 && survivorActors[2] != null)
                {
                    survivorActors[2].transform.position = showdownSpot + new Vector3(1.2f, 0.12f, 0f);
                    survivorActors[2].transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    if (frame == 1335 && survivorAnimators[2] != null)
                    {
                        SetActorAnimState(survivorAnimators[2], false, false);
                    }
                }

                // Auto-recuperação do Hunter: fica em pé e reativa a navegação
                if (frame == 1410)
                {
                    if (hunterKnockdown != null)
                    {
                        hunterKnockdown.ApplyRecover();
                    }
                }

                float t = (frame - 1315) / 125f;
                Vector3 camPos = (hunterActor != null ? hunterActor.transform.position : showdownSpot) + new Vector3(3.8f, 1.40f, 3.2f);
                Vector3 lookTarget = hunterActor != null ? hunterActor.transform.position + Vector3.up * 0.8f : showdownSpot;
                PositionCamera(camPos, lookTarget, 48f);
            }
        }
        // =========================================================================
        // ATO 6: A GRANDE PERSEGUIÇÃO NA AVENIDA ILUMINADA (1440 - 1830 | 48.0s - 61.0s)
        // Danse Macabre: Clímax máximo da orquestra, fortíssimo, valsa enlouquecida!
        // =========================================================================
        else if (frame < 1830)
        {
            Vector3 chaseStart = new Vector3(-6.0f, 0.15f, -36.0f);

            if (frame == 1440)
            {
                if (hunterScythe != null) hunterScythe.SetActive(true); // Empunha a foice com fogo!

                if (hunterActor != null)
                {
                    hunterActor.transform.position = chaseStart + new Vector3(-6f, 0.15f, 0f);
                    hunterActor.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    if (hunterAgent != null)
                    {
                        hunterAgent.enabled = true;
                        hunterAgent.Warp(hunterActor.transform.position);
                        hunterAgent.speed = 8.2f; // Perseguição veloz
                        hunterAgent.SetDestination(chaseStart + new Vector3(65f, 0f, 0f));
                    }
                    if (hunterAnimator != null)
                    {
                        SetActorAnimState(hunterAnimator, true, true);
                    }
                }

                // Todos os 3 sobreviventes em disparada pela pista de asfalto livre de postes e arbustos!
                for (int i = 0; i < survivorActors.Count; i++)
                {
                    if (survivorActors[i] != null && survivorAgents[i] != null)
                    {
                        survivorAgents[i].enabled = true;
                        Vector3 sStart = chaseStart + new Vector3(6f + i * 2.4f, 0f, (i - 1f) * 0.75f);
                        survivorAgents[i].Warp(sStart);
                        survivorAgents[i].speed = 7.4f;
                        survivorAgents[i].SetDestination(sStart + new Vector3(65f, 0f, 0f));

                        if (survivorAnimators[i] != null)
                        {
                            SetActorAnimState(survivorAnimators[i], true, true);
                        }
                    }
                }
            }

            if (frame < 1620)
            {
                // Primeira metade da caçada: over-the-shoulder logo atrás do Hunter e da foice em chamas!
                Vector3 hPos = hunterActor != null ? hunterActor.transform.position : chaseStart;
                Vector3 camPos = hPos + new Vector3(-2.8f, 1.70f, 1.35f);
                Vector3 lookTarget = hPos + new Vector3(8.5f, 1.15f, -0.4f);
                PositionCamera(camPos, lookTarget, 48f);
            }
            else
            {
                // Segunda metade da caçada: tracking frontal dinâmico correndo à frente dos sobreviventes
                Vector3 leadPos = survivorActors.Count > 0 && survivorActors[0] != null ? survivorActors[0].transform.position : chaseStart;
                Vector3 camPos = leadPos + new Vector3(6.8f, 1.35f, 1.20f);
                Vector3 lookTarget = leadPos + new Vector3(-4.0f, 1.25f, -0.6f);
                float bob = Mathf.Sin((frame - 1620) * 0.32f) * 0.04f;
                PositionCamera(camPos + new Vector3(0f, bob, 0f), lookTarget, 48f);
            }
        }
        // =========================================================================
        // ATO 7: O CORTE NO SILÊNCIO & O CANTO DO GALO NO OBOÉ (1830 - 2010 | 61.0s - 67.0s)
        // Danse Macabre: A orquestra silencia subitamente. O solo de oboé canta a alvorada!
        // =========================================================================
        else
        {
            float t = (frame - 1830) / 180f;
            Vector3 focusPoint = new Vector3(38.0f, 1.2f, -34.8f);

            // Grua ascendente revelando a cidade sob a noite estrelada
            float camHeight = Mathf.Lerp(1.8f, 16.0f, SmoothStep(t));
            float camDistZ = Mathf.Lerp(-3.5f, -14.0f, SmoothStep(t));
            float camDistX = Mathf.Lerp(4.0f, -10.0f, SmoothStep(t));

            Vector3 camPos = focusPoint + new Vector3(camDistX, camHeight, camDistZ);
            Vector3 lookTarget = focusPoint + new Vector3(6.0f, 0.5f, 0f);

            PositionCamera(camPos, lookTarget, Mathf.Lerp(48f, 56f, SmoothStep(t)));
        }
    }

    private void PositionCamera(Vector3 position, Vector3 lookTarget, float fov)
    {
        if (trailerCamera == null) return;

        Vector3 shakeOffset = Vector3.zero;
        if (cameraTrauma > 0f)
        {
            float shakeAmt = Mathf.Pow(cameraTrauma, 2f) * 0.45f;
            shakeOffset = new Vector3(
                (Mathf.PerlinNoise(Time.time * 30f, 0f) - 0.5f) * shakeAmt,
                (Mathf.PerlinNoise(0f, Time.time * 30f) - 0.5f) * shakeAmt,
                0f
            );
            cameraTrauma = Mathf.MoveTowards(cameraTrauma, 0f, (1f / 30f) * 1.8f);
        }

        trailerCamera.transform.position = position + shakeOffset;
        trailerCamera.fieldOfView = fov;

        Vector3 dir = (lookTarget - position).normalized;
        if (dir != Vector3.zero)
        {
            trailerCamera.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    private float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void FinishTrailerCapture()
    {
        Debug.Log($"[GameTrailerDirector] GRAVAÇÃO CINEMÁTICA CONCLUÍDA COM SUCESSO! {TotalTargetFrames} quadros salvos.");
        Time.captureFramerate = 0;

        if (renderTexture != null) renderTexture.Release();

        if (File.Exists(FlagFilePath))
        {
            try { File.Delete(FlagFilePath); } catch { }
        }

#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#endif
    }
}
#endif
