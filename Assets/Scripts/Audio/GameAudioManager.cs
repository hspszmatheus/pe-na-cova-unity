using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gerenciador Central de Áudio para Pé na Cova.
/// Suporta:
/// - Música tema do Menu (Tabletop Jazz Cafe - LudoLoon Studio) com transição e fade suave.
/// - Efeitos sonoros táteis de botões e interface (Casual Game SFX Pack - Dustyroom).
/// - Efeitos de combate, habilidades, tasks e queda da morte (RPG Essentials Free - Leohpaz).
/// </summary>
[DisallowMultipleComponent]
public class GameAudioManager : MonoBehaviour
{
    private static GameAudioManager instance;
    public static GameAudioManager Instance
    {
        get
        {
            if (!Application.isPlaying) return instance;

            if (instance == null)
            {
                instance = UnityEngine.Object.FindAnyObjectByType<GameAudioManager>();
                if (instance == null)
                {
                    var go = GameObject.Find("GameAudioManager");
                    if (go != null) instance = go.GetComponent<GameAudioManager>();
                }
                if (instance == null)
                {
                    var prefab = Resources.Load<GameObject>("GameAudioManager");
                    if (prefab != null)
                    {
                        var instantiated = UnityEngine.Object.Instantiate(prefab);
                        instantiated.name = "GameAudioManager";
                        instance = instantiated.GetComponent<GameAudioManager>();
                    }
                    else
                    {
                        var fallbackGo = new GameObject("GameAudioManager");
                        instance = fallbackGo.AddComponent<GameAudioManager>();
                    }
                }
            }
            return instance;
        }
        private set => instance = value;
    }

    [Header("Música de Fundo (Tabletop Jazz Cafe)")]
    [SerializeField] private AudioClip menuMusicClip;
    [SerializeField, Range(0f, 1f)] private float menuMusicVolume = 0.42f;

    [Header("Menu & UI SFX (Dustyroom Casual Game Sounds)")]
    [SerializeField] private AudioClip buttonHoverClip;
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip buttonConfirmClip;
    [SerializeField] private AudioClip buttonCancelClip;
    [SerializeField] private AudioClip sliderTickClip;
    [SerializeField] private AudioClip cardPopClip;

    [Header("Combate & Habilidades (Leohpaz RPG Essentials)")]
    [Tooltip("Foice - Golpe no ar (Swing/Slash)")]
    [SerializeField] private AudioClip scytheSwingClip;
    [Tooltip("Foice - Impacto na carne (Player)")]
    [SerializeField] private AudioClip scytheHitFleshClip;
    [Tooltip("Foice - Impacto pesado (Splash/Heavy)")]
    [SerializeField] private AudioClip scytheHitHeavyClip;

    [Header("Bola de Fogo")]
    [Tooltip("Carregamento/Invocação da bola de fogo na mão")]
    [SerializeField] private AudioClip fireballChargeClip;
    [Tooltip("Explosão da bola de fogo ao atingir alvo")]
    [SerializeField] private AudioClip fireballExplosionClip;

    [Header("Magia da Morte (Revelação de Área)")]
    [Tooltip("Áudio contínuo de canalização/carga de magia escura ao segurar 'E'")]
    [SerializeField] private AudioClip hunterRevealChargeClip;
    [Tooltip("Áudio de liberação e detonação da onda de choque mágica")]
    [SerializeField] private AudioClip hunterRevealReleaseClip;
    [Tooltip("Áudio de ressonância espectral ao detectar sobreviventes")]
    [SerializeField] private AudioClip hunterRevealPulseClip;

    [Header("Queda da Morte do Céu (Meteor Intro)")]
    [Tooltip("Loop de vento e aceleração atmosférica em queda")]
    [SerializeField] private AudioClip meteorWindFallClip;
    [Tooltip("Impacto colossal no solo (Trovão/Cratera)")]
    [SerializeField] private AudioClip meteorGroundImpactClip;
    [Tooltip("Impacto de aterrissagem pesada")]
    [SerializeField] private AudioClip meteorLandingClip;

    [Header("Sobreviventes / Players")]
    [Tooltip("Chute no ar")]
    [SerializeField] private AudioClip playerKickSwingClip;
    [Tooltip("Chute acertando o Hunter")]
    [SerializeField] private AudioClip playerKickHitClip;
    [Tooltip("Morte / Eliminação do jogador")]
    [SerializeField] private AudioClip playerDeathClip;
    [Tooltip("Aura roxa de marcado para a morte")]
    [SerializeField] private AudioClip markedForDeathClip;

    [Header("Tasks")]
    [Tooltip("Início da interação com a task")]
    [SerializeField] private AudioClip taskStartClip;
    [Tooltip("Conclusão com sucesso da task")]
    [SerializeField] private AudioClip taskCompleteClip;
    [Tooltip("Acionamento sonoro do Hunter ao detectar interação com task por NPC")]
    [SerializeField] private AudioClip hunterTaskAlertClip;

    [Header("Pausa")]
    [SerializeField] private AudioClip pauseOpenClip;
    [SerializeField] private AudioClip pauseCloseClip;

    // Fontes de Áudio 2D internas
    private AudioSource musicSource;
    private AudioSource uiSource;
    private AudioSource meteorLoopSource;

    private Coroutine musicFadeCoroutine;
    private float lastHoverSoundTime = -1f;
    private float lastSliderSoundTime = -1f;
    private float lastHunterTaskAlertTime = -1f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureAudioSourcesCreated();
        EnsureClipsLoaded();
        ApplyGlobalAudioSettings();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        StopHunterRevealCharge();
        StopMeteorFallLoop();
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    private void Start()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "JoinByBrowsing" || currentScene.Contains("Menu"))
        {
            PlayMenuMusic();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StopHunterRevealCharge();
        StopMeteorFallLoop();

        if (scene.name == "JoinByBrowsing" || scene.name.Contains("Menu"))
        {
            PlayMenuMusic();
        }
        else if (scene.name == "GameScene")
        {
            StopMenuMusic(1.2f);
        }
    }

    private void EnsureAudioSourcesCreated()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.volume = menuMusicVolume;
            musicSource.mute = IsMasterMuted;
        }

        if (uiSource == null)
        {
            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.loop = false;
            uiSource.spatialBlend = 0f;
            uiSource.volume = 0.85f;
        }

        if (meteorLoopSource == null)
        {
            meteorLoopSource = gameObject.AddComponent<AudioSource>();
            meteorLoopSource.playOnAwake = false;
            meteorLoopSource.loop = true;
            meteorLoopSource.spatialBlend = 0f;
            meteorLoopSource.volume = 0.75f;
        }
    }

    public void EnsureClipsLoaded()
    {
#if UNITY_EDITOR
        if (menuMusicClip == null)
            menuMusicClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/LudoLoon Studio/Tabletop Jazz Cafe/Tabletop Jazz Cafe.wav");

        // Menu SFX
        if (buttonHoverClip == null)
            buttonHoverClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-01.wav");
        if (buttonClickClip == null)
            buttonClickClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-03.wav");
        if (buttonConfirmClip == null)
            buttonConfirmClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-26.wav");
        if (buttonCancelClip == null)
            buttonCancelClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-07.wav");
        if (sliderTickClip == null)
            sliderTickClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-21.wav");
        if (cardPopClip == null)
            cardPopClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-42.wav");

        // Combate & RPG
        if (scytheSwingClip == null)
            scytheSwingClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_Battle_SFX/22_Slash_04.wav");
        if (scytheHitFleshClip == null)
            scytheHitFleshClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_Battle_SFX/15_Impact_flesh_02.wav");
        if (scytheHitHeavyClip == null)
            scytheHitHeavyClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_Battle_SFX/77_flesh_02.wav");

        if (fireballChargeClip == null)
            fireballChargeClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/45_Charge_05.wav");
        if (fireballExplosionClip == null)
            fireballExplosionClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/04_Fire_explosion_04_medium.wav");

        if (hunterRevealChargeClip == null)
            hunterRevealChargeClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Buffs_Heals_SFX/39_Absorb_04.wav");
        if (hunterRevealReleaseClip == null)
            hunterRevealReleaseClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/30_Earth_02.wav");
        if (hunterRevealPulseClip == null)
            hunterRevealPulseClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Buffs_Heals_SFX/21_Debuff_01.wav");

        if (meteorWindFallClip == null)
            meteorWindFallClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/25_Wind_01.wav");
        if (meteorGroundImpactClip == null)
            meteorGroundImpactClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Atk_Magic_SFX/18_Thunder_02.wav");
        if (meteorLandingClip == null)
            meteorLandingClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/12_Player_Movement_SFX/45_Landing_01.wav");

        if (playerKickSwingClip == null)
            playerKickSwingClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/12_Player_Movement_SFX/56_Attack_03.wav");
        if (playerKickHitClip == null)
            playerKickHitClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/12_Player_Movement_SFX/61_Hit_03.wav");
        if (playerDeathClip == null)
            playerDeathClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_Battle_SFX/69_Enemy_death_01.wav");
        if (markedForDeathClip == null)
            markedForDeathClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Buffs_Heals_SFX/21_Debuff_01.wav");

        if (taskStartClip == null)
            taskStartClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_UI_Menu_SFX/051_use_item_01.wav");
        if (taskCompleteClip == null)
            taskCompleteClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/8_Buffs_Heals_SFX/02_Heal_02.wav");
        if (hunterTaskAlertClip == null)
            hunterTaskAlertClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-28.wav");
        if (hunterTaskAlertClip == null)
            hunterTaskAlertClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual Game Sounds U6/CasualGameSounds/DM-CGS-28.wav");

        if (pauseOpenClip == null)
            pauseOpenClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_UI_Menu_SFX/092_Pause_04.wav");
        if (pauseCloseClip == null)
            pauseCloseClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Leohpaz/RPG_Essentials_Free/10_UI_Menu_SFX/098_Unpause_04.wav");
#endif
    }

    #region Sistema Global de Volume & Mute (Menu e Jogo)

    public const string MasterVolumePrefKey = "EOTWH_MasterVolume";
    public const string AudioMutedPrefKey = "EOTWH_AudioMuted";

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterVolumePrefKey, 1.0f);
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterVolumePrefKey, clamped);
            PlayerPrefs.Save();
            ApplyGlobalAudioSettings();
            OnMasterVolumeChanged?.Invoke(clamped);
        }
    }

    public static bool IsMasterMuted
    {
        get => PlayerPrefs.GetInt(AudioMutedPrefKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(AudioMutedPrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyGlobalAudioSettings();
            OnMasterMuteChanged?.Invoke(value);
        }
    }

    public bool IsMenuMuted => IsMasterMuted;

    public static event System.Action<float> OnMasterVolumeChanged;
    public static event System.Action<bool> OnMasterMuteChanged;

    public event System.Action<bool> OnMenuMuteStateChanged
    {
        add => OnMasterMuteChanged += value;
        remove => OnMasterMuteChanged -= value;
    }

    public static void ApplyGlobalAudioSettings()
    {
        bool muted = IsMasterMuted;
        float vol = MasterVolume;
        AudioListener.volume = muted ? 0f : vol;

        if (Instance != null && Instance.musicSource != null)
        {
            Instance.musicSource.mute = muted;
        }
    }

    public void SetMasterVolumeLevel(float volume)
    {
        if (volume > 0.01f && IsMasterMuted)
        {
            IsMasterMuted = false;
        }
        MasterVolume = volume;
    }

    public void ToggleMenuMute()
    {
        ToggleAudioMute();
    }

    public void SetMenuMute(bool muted)
    {
        SetAudioMute(muted);
    }

    public void ToggleAudioMute()
    {
        IsMasterMuted = !IsMasterMuted;
    }

    public void SetAudioMute(bool muted)
    {
        IsMasterMuted = muted;
    }

    #endregion

    #region Música de Fundo (Tabletop Jazz Cafe)

    public void PlayMenuMusic(float fadeInDuration = 1.0f)
    {
        EnsureAudioSourcesCreated();
        EnsureClipsLoaded();
        ApplyGlobalAudioSettings();

        if (menuMusicClip == null) return;

        if (musicSource != null)
        {
            musicSource.mute = IsMenuMuted;
        }

        if (musicSource.isPlaying && musicSource.clip == menuMusicClip)
        {
            return;
        }

        if (musicFadeCoroutine != null) StopCoroutine(musicFadeCoroutine);
        musicFadeCoroutine = StartCoroutine(FadeMusicRoutine(menuMusicClip, menuMusicVolume, fadeInDuration));
    }

    public void StopMenuMusic(float fadeOutDuration = 1.0f)
    {
        if (musicSource == null || !musicSource.isPlaying) return;

        if (musicFadeCoroutine != null) StopCoroutine(musicFadeCoroutine);
        musicFadeCoroutine = StartCoroutine(FadeOutMusicRoutine(fadeOutDuration));
    }

    private IEnumerator FadeMusicRoutine(AudioClip targetClip, float targetVolume, float duration)
    {
        musicSource.clip = targetClip;
        musicSource.volume = 0f;
        musicSource.mute = IsMenuMuted;
        musicSource.Play();

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(0f, targetVolume, t / duration);
            yield return null;
        }
        musicSource.volume = targetVolume;
        musicFadeCoroutine = null;
    }

    private IEnumerator FadeOutMusicRoutine(float duration)
    {
        float startVol = musicSource.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(startVol, 0f, t / duration);
            yield return null;
        }
        musicSource.Stop();
        musicSource.volume = startVol;
        musicFadeCoroutine = null;
    }

    #endregion

    #region Efeitos Sonoros de Interface (Casual Game SFX Pack)

    public void PlayButtonHover()
    {
        if (Time.unscaledTime - lastHoverSoundTime < 0.05f) return;
        lastHoverSoundTime = Time.unscaledTime;
        Play2D(buttonHoverClip, 0.35f, Random.Range(0.96f, 1.04f));
    }

    public void PlayButtonClick()
    {
        Play2D(buttonClickClip, 0.70f, Random.Range(0.95f, 1.05f));
    }

    public void PlayButtonConfirm()
    {
        Play2D(buttonConfirmClip, 0.75f, 1.0f);
    }

    public void PlayButtonCancel()
    {
        Play2D(buttonCancelClip, 0.70f, 1.0f);
    }

    public void PlaySliderTick()
    {
        if (Time.unscaledTime - lastSliderSoundTime < 0.07f) return;
        lastSliderSoundTime = Time.unscaledTime;
        Play2D(sliderTickClip, 0.30f, Random.Range(0.92f, 1.08f));
    }

    public void PlayCardPop()
    {
        Play2D(cardPopClip, 0.65f, Random.Range(0.97f, 1.03f));
    }

    public void PlayPauseMenuOpen()
    {
        Play2D(pauseOpenClip, 0.75f, 1.0f);
    }

    public void PlayPauseMenuClose()
    {
        Play2D(pauseCloseClip, 0.70f, 1.0f);
    }

    private void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        EnsureAudioSourcesCreated();
        uiSource.pitch = pitch;
        uiSource.PlayOneShot(clip, volume);
    }

    #endregion

    #region Efeitos de Combate & Habilidades (RPG Essentials Free)

    public void PlayHunterScytheSwing(Vector3 position)
    {
        PlaySpatialSound(scytheSwingClip, position, volume: 0.85f, minDistance: 2.5f, maxDistance: 32f, pitch: Random.Range(0.94f, 1.06f));
    }

    public void PlayHunterScytheHit(Vector3 position, bool isPlayer)
    {
        AudioClip clip = isPlayer ? scytheHitHeavyClip : scytheHitFleshClip;
        PlaySpatialSound(clip, position, volume: 0.95f, minDistance: 3f, maxDistance: 35f, pitch: Random.Range(0.95f, 1.05f));
    }

    public void PlayFireballCharge(Transform parentTransform)
    {
        if (fireballChargeClip == null || parentTransform == null) return;
        PlayAttachedSound(fireballChargeClip, parentTransform, volume: 0.75f, minDistance: 2f, maxDistance: 28f);
    }

    public void PlayFireballExplosion(Vector3 position)
    {
        PlaySpatialSound(fireballExplosionClip, position, volume: 0.95f, minDistance: 4f, maxDistance: 45f, pitch: Random.Range(0.92f, 1.05f));
    }

    #region Magia de Revelação da Morte (Hunter Reveal)

    private AudioSource hunterRevealLoopSource;

    /// <summary>
    /// Inicia o áudio de canalização/carga mágica enquanto a Morte segura a tecla 'E'.
    /// Nunca fica em loop para evitar que o áudio continue se repetindo indefinidamente.
    /// </summary>
    public void StartHunterRevealCharge(Transform parentTransform)
    {
        EnsureClipsLoaded();
        if (hunterRevealChargeClip == null) return;

        if (hunterRevealLoopSource == null)
        {
            var go = new GameObject("HunterRevealChargeAudio");
            go.transform.SetParent(transform, false);
            hunterRevealLoopSource = go.AddComponent<AudioSource>();
            hunterRevealLoopSource.spatialBlend = 0.80f;
            hunterRevealLoopSource.minDistance = 2.5f;
            hunterRevealLoopSource.maxDistance = 28f;
            hunterRevealLoopSource.rolloffMode = AudioRolloffMode.Linear;
            hunterRevealLoopSource.dopplerLevel = 0f;
            hunterRevealLoopSource.playOnAwake = false;
        }

        if (parentTransform != null)
        {
            hunterRevealLoopSource.transform.position = parentTransform.position;
        }

        hunterRevealLoopSource.Stop();
        hunterRevealLoopSource.clip = hunterRevealChargeClip;
        hunterRevealLoopSource.volume = 0.70f;
        hunterRevealLoopSource.pitch = 1.0f;
        hunterRevealLoopSource.loop = false; // Áudio de canalização para os 1.5s de conjuração rápida
        hunterRevealLoopSource.Play();
    }

    /// <summary>
    /// Modula a frequência e volume do feitiço conforme a barra progride (0 a 1 em 1.5s).
    /// </summary>
    public void UpdateHunterRevealChargePitch(float progress, Vector3? position = null)
    {
        if (hunterRevealLoopSource != null)
        {
            if (position.HasValue)
            {
                hunterRevealLoopSource.transform.position = position.Value;
            }

            if (hunterRevealLoopSource.isPlaying)
            {
                float p = Mathf.Clamp01(progress);
                // Pitch acelera de 1.0f até 1.45f criando a tensão crescente da conjuração rápida
                hunterRevealLoopSource.pitch = Mathf.Lerp(1.0f, 1.45f, p);
                // Volume cresce até 1.0f no clímax exato aos 1.5 segundos
                hunterRevealLoopSource.volume = Mathf.Lerp(0.70f, 1.0f, p);
            }
        }
    }

    /// <summary>
    /// Interrompe imediatamente e limpa o áudio de carga mágica se o feitiço for solto ou cancelado.
    /// </summary>
    public void StopHunterRevealCharge()
    {
        if (hunterRevealLoopSource != null)
        {
            hunterRevealLoopSource.Stop();
            hunterRevealLoopSource.clip = null;
        }
    }

    /// <summary>
    /// Emite a explosão e detonação da onda de choque mágica aos 1.5 segundos exatos, no clímax do golpe da Morte.
    /// </summary>
    public void PlayHunterRevealRelease(Vector3 position)
    {
        StopHunterRevealCharge();
        EnsureClipsLoaded();

        if (hunterRevealReleaseClip != null)
        {
            PlaySpatialSound(hunterRevealReleaseClip, position, volume: 1.0f, minDistance: 5f, maxDistance: 45f, pitch: 1.0f);
        }
    }

    /// <summary>
    /// Efeito sonoro espectral quando um sobrevivente é detectado e revelado pela magia.
    /// </summary>
    public void PlayHunterRevealSurvivorPing(Vector3 position)
    {
        EnsureClipsLoaded();
        AudioClip clip = hunterRevealPulseClip != null ? hunterRevealPulseClip : markedForDeathClip;
        if (clip != null)
        {
            PlaySpatialSound(clip, position, volume: 0.90f, minDistance: 3f, maxDistance: 35f, pitch: 1.20f);
        }
    }

    #endregion

    public void StartMeteorFallLoop(Transform hunterTransform)
    {
        EnsureAudioSourcesCreated();
        if (meteorWindFallClip == null) return;

        meteorLoopSource.clip = meteorWindFallClip;
        meteorLoopSource.volume = 0.65f;
        meteorLoopSource.pitch = 0.85f;
        meteorLoopSource.Play();
    }

    public void UpdateMeteorFallPitch(float progress)
    {
        if (meteorLoopSource != null && meteorLoopSource.isPlaying)
        {
            meteorLoopSource.pitch = Mathf.Lerp(0.85f, 1.30f, progress);
            meteorLoopSource.volume = Mathf.Lerp(0.50f, 0.90f, progress);
        }
    }

    public void StopMeteorFallLoop()
    {
        if (meteorLoopSource != null && meteorLoopSource.isPlaying)
        {
            meteorLoopSource.Stop();
        }
    }

    public void PlayMeteorImpact(Vector3 position)
    {
        StopMeteorFallLoop();
        // Dispara impacto com trovão e aterrissagem estrondosa
        PlaySpatialSound(meteorGroundImpactClip, position, volume: 1.0f, minDistance: 8f, maxDistance: 60f, pitch: 0.95f);
        PlaySpatialSound(fireballExplosionClip, position, volume: 0.90f, minDistance: 6f, maxDistance: 50f, pitch: 0.85f);
        PlaySpatialSound(meteorLandingClip, position, volume: 0.85f, minDistance: 5f, maxDistance: 40f, pitch: 0.90f);
    }

    public void PlayPlayerKick(Vector3 position)
    {
        PlaySpatialSound(playerKickSwingClip, position, volume: 0.80f, minDistance: 2f, maxDistance: 25f, pitch: Random.Range(0.95f, 1.08f));
    }

    public void PlayPlayerKickHit(Vector3 position)
    {
        PlaySpatialSound(playerKickHitClip, position, volume: 0.90f, minDistance: 2.5f, maxDistance: 30f, pitch: Random.Range(0.92f, 1.04f));
    }

    public void PlayPlayerDeath(Vector3 position)
    {
        PlaySpatialSound(playerDeathClip, position, volume: 0.95f, minDistance: 4f, maxDistance: 40f, pitch: Random.Range(0.95f, 1.05f));
    }

    public void PlayMarkedForDeath(Vector3 position)
    {
        PlaySpatialSound(markedForDeathClip, position, volume: 0.90f, minDistance: 3f, maxDistance: 35f);
    }

    /// <summary>
    /// Som de início ou progresso de interação com task. Emitido espacialmente no cenário para que todos próximos ouçam.
    /// </summary>
    public void PlayTaskStart(Vector3 position)
    {
        EnsureClipsLoaded();
        if (taskStartClip == null) return;
        PlaySpatialSound(taskStartClip, position, volume: 0.70f, minDistance: 1.5f, maxDistance: 18f, pitch: Random.Range(0.96f, 1.04f));
    }

    /// <summary>
    /// Acionamento sonoro específico para o Hunter quando uma task concluída é revelada em vermelho no mapa.
    /// Emite um sinal sonoro 2D claro para o Hunter e áudio espacial 3D na posição da task para orientação direcional.
    /// </summary>
    public void PlayHunterTaskAlert(Vector3 position)
    {
        EnsureClipsLoaded();
        AudioClip clip = hunterTaskAlertClip != null ? hunterTaskAlertClip : taskCompleteClip;
        if (clip == null) return;

        if (Time.unscaledTime - lastHunterTaskAlertTime > 0.25f)
        {
            lastHunterTaskAlertTime = Time.unscaledTime;
            Play2D(clip, volume: 0.90f, pitch: 1.0f);
        }

        PlaySpatialSound(clip, position, volume: 0.85f, minDistance: 6f, maxDistance: 70f, pitch: 0.95f);
    }

    /// <summary>
    /// Som de conclusão de task. Executado exclusivamente em 2D para o jogador local que realizou a tarefa.
    /// Não propaga pelo mundo em 3D para evitar poluição sonora e não revelar a conclusão aos outros.
    /// </summary>
    public void PlayTaskComplete(Vector3 position = default)
    {
        EnsureClipsLoaded();
        if (taskCompleteClip == null) return;
        Play2D(taskCompleteClip, volume: 0.90f, pitch: 1.0f);
    }

    #endregion

    #region Helpers de Áudio 3D Espacial

    /// <summary>
    /// Instancia um emissor de áudio 3D temporário no mundo com curva de atenuação suave.
    /// </summary>
    public static void PlaySpatialSound(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 2f, float maxDistance = 35f, float pitch = 1f)
    {
        if (clip == null) return;

        var go = new GameObject($"SpatialSFX_{clip.name}");
        go.transform.position = position;

        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.spatialBlend = 1f; // 100% 3D
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        source.playOnAwake = false;

        source.Play();
        Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.15f);
    }

    public static void PlayAttachedSound(AudioClip clip, Transform parent, float volume = 1f, float minDistance = 2f, float maxDistance = 28f)
    {
        if (clip == null || parent == null) return;

        var go = new GameObject($"AttachedSFX_{clip.name}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        source.playOnAwake = false;

        source.Play();
        Destroy(go, clip.length + 0.15f);
    }

    #endregion
}
