using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sistema de medo por proximidade da Morte (Hunter) para o jogador local vivo:
/// - Calcula a distância até o Hunter ativo mais próximo.
/// - Reproduz batidas cardíacas sincronizadas usando o clipe Heartbeat (fatiado por batida com fallback dinâmico),
///   acelerando a cadência e elevando a intensidade quanto mais próximo o Hunter estiver.
/// - Sincroniza o tremor de câmera (6D shake + micro-inclinação + tremor de enquadramento) e o zoom suave
///   (redução de FOV e leve aproximação de órbita com pulso a cada "lub-dub") por meio do PlayerCameraSetup.
/// </summary>
[DisallowMultipleComponent]
public class HunterProximityFearEffect : MonoBehaviour
{
    private struct BeatSlice
    {
        public AudioClip Clip;
        public float LubPeakOffset;
        public float DubPeakOffset;
    }

    [Header("Áudio do Batimento Cardíaco")]
    [Tooltip("Clipe de áudio Heartbeat localizado em Assets/Audio/heartbeat.mp3")]
    [SerializeField] private AudioClip heartbeatClip;
    [SerializeField, Range(0f, 1f)] private float minHeartbeatVolume = 0.08f;
    [SerializeField, Range(0f, 1f)] private float maxHeartbeatVolume = 0.56f;
    [SerializeField] private float minBeatPitch = 0.94f;
    [SerializeField] private float maxBeatPitch = 1.18f;

    [Header("Raio e Curva de Medo")]
    [Tooltip("Distância em metros onde o coração começa a bater e a câmera inicia tremor/zoom sutis")]
    [SerializeField] private float fearStartDistance = 11.0f;
    [Tooltip("Distância de perigo extremo em metros onde o medo atinge 100%")]
    [SerializeField] private float fearMaxDistance = 2.5f;
    [Tooltip("Expoente da curva de aproximação (valores maiores concentram a intensidade mais perto do Hunter)")]
    [SerializeField] private float distanceCurveExponent = 1.35f;
    [SerializeField] private float fearRampUpSpeed = 5.5f;
    [SerializeField] private float fearDecaySpeed = 3.5f;

    [Header("Cadência Cardíaca (Segundos por Batida)")]
    [Tooltip("Intervalo entre batidas no limite externo do raio de medo (~55 BPM)")]
    [SerializeField] private float slowBeatInterval = 1.10f;
    [Tooltip("Intervalo entre batidas no perigo máximo (~170 BPM)")]
    [SerializeField] private float fastBeatInterval = 0.35f;
    [SerializeField, Range(0f, 0.25f)] private float fearActivationThreshold = 0.03f;

    [Header("Zoom e Tremor de Câmera")]
    [Tooltip("Redução contínua máxima no FOV da câmera (graus) ao se aproximar")]
    [SerializeField] private float maxFearFovReduction = 3.5f;
    [Tooltip("Pulso extra de zoom no FOV (graus) sincronizado com cada lub-dub")]
    [SerializeField] private float maxBeatFovPulse = 0.7f;
    [Tooltip("Aproximação contínua da órbita da câmera (metros) ao se aproximar")]
    [SerializeField] private float maxFearRadiusReduction = 0.05f;
    [Tooltip("Pulso extra de aproximação da órbita sincronizado com cada batida")]
    [SerializeField] private float maxBeatRadiusPulse = 0.0f;
    [Tooltip("Ganho base contínuo de tremor Perlin 6D no medo máximo")]
    [SerializeField] private float maxContinuousShakeAmplitude = 0.012f;
    [Tooltip("Pico extra de tremor Perlin 6D sincronizado com cada lub-dub")]
    [SerializeField] private float maxBeatShakeBurst = 0.020f;
    [SerializeField] private float minShakeFrequency = 0.08f;
    [SerializeField] private float maxShakeFrequency = 0.18f;
    [SerializeField] private float maxContinuousRollDegrees = 0.0f;
    [SerializeField] private float maxBeatRollKickDegrees = 0.0f;
    [SerializeField] private float maxScreenJitterOffset = 0.0f;

    private PlayerController playerController;
    private PlayerCameraSetup cameraSetup;
    private AudioSource heartbeatSource;

    private readonly List<BeatSlice> slicedBeats = new List<BeatSlice>(4);
    private bool useFallbackLoopMode = false;
    private bool beatsInitialized = false;
    private int nextBeatSliceIndex = 0;

    private float currentFearIntensity = 0f;
    private float currentClosestHunterDistance = float.PositiveInfinity;
    private float nextBeatTimer = 0f;
    private bool isBeatPulseActive = false;
    private float activeBeatElapsed = 0f;
    private float activeBeatDuration = 0.45f;
    private float activeBeatPitch = 1f;
    private float activeBeatStrength = 0f;
    private float activeLubOffset = 0.05f;
    private float activeDubOffset = 0.26f;
    private float activeBeatSign = 1f;
    private float lastBeatTriggeredAt = -10f;
    private int beatCount = 0;
    private int fallbackScanCountdown = 0;
    private PlayerController[] cachedFallbackPlayers;

    public float CurrentFearIntensity => currentFearIntensity;
    public float ClosestHunterDistance => currentClosestHunterDistance;
    public float CurrentBeatInterval => Mathf.Lerp(slowBeatInterval, fastBeatInterval, Mathf.Pow(currentFearIntensity, 0.85f));
    public int BeatsTriggeredCount => beatCount;

    public void Initialize()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (cameraSetup == null) cameraSetup = GetComponent<PlayerCameraSetup>();

        if (ShouldPrepareLocalAudio())
        {
            EnsureLocalAudioPrepared();
        }
    }

    private bool ShouldPrepareLocalAudio()
    {
        if (playerController == null) return true;
        if (playerController.IsHunter) return false;
        return playerController.IsOwner || !playerController.IsSpawned;
    }

    private void EnsureLocalAudioPrepared()
    {
        EnsureHeartbeatClipLoaded();
        EnsureAudioSourceCreated();
        if (!beatsInitialized && heartbeatClip != null)
        {
            BuildSlicedHeartbeats();
        }
    }

    private void Awake()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (cameraSetup == null) cameraSetup = GetComponent<PlayerCameraSetup>();
    }

    private void OnEnable()
    {
        ResetFearState();
    }

    private void OnDisable()
    {
        ResetFearState();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < slicedBeats.Count; i++)
        {
            if (slicedBeats[i].Clip != null)
            {
                Destroy(slicedBeats[i].Clip);
            }
        }
        slicedBeats.Clear();
    }

    private void EnsureHeartbeatClipLoaded()
    {
        if (heartbeatClip == null)
        {
            heartbeatClip = Resources.Load<AudioClip>("heartbeat");
#if UNITY_EDITOR
            if (heartbeatClip == null)
            {
                heartbeatClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/heartbeat.mp3");
            }
#endif
        }

        if (heartbeatClip != null && heartbeatClip.loadState == AudioDataLoadState.Unloaded)
        {
            heartbeatClip.LoadAudioData();
        }
    }

    private void EnsureAudioSourceCreated()
    {
        if (heartbeatSource != null) return;

        Transform existing = transform.Find("HeartbeatFearAudio");
        GameObject audioObj = existing != null ? existing.gameObject : new GameObject("HeartbeatFearAudio");
        if (existing == null)
        {
            audioObj.transform.SetParent(transform, false);
            audioObj.transform.localPosition = Vector3.zero;
        }

        heartbeatSource = audioObj.GetComponent<AudioSource>();
        if (heartbeatSource == null)
        {
            heartbeatSource = audioObj.AddComponent<AudioSource>();
        }

        heartbeatSource.playOnAwake = false;
        heartbeatSource.loop = false;
        heartbeatSource.spatialBlend = 0f; // Áudio 2D interno do jogador local (simétrico e limpo)
        heartbeatSource.dopplerLevel = 0f;
        heartbeatSource.bypassEffects = true;
        heartbeatSource.bypassListenerEffects = true;
        heartbeatSource.bypassReverbZones = true;
        heartbeatSource.priority = 32;
    }

    private void BuildSlicedHeartbeats()
    {
        beatsInitialized = true;
        slicedBeats.Clear();
        useFallbackLoopMode = false;

        if (heartbeatClip == null)
        {
            return;
        }

        if (heartbeatClip.loadState != AudioDataLoadState.Loaded)
        {
            heartbeatClip.LoadAudioData();
        }

        int totalSamples = heartbeatClip.samples;
        int channels = heartbeatClip.channels;
        int freq = heartbeatClip.frequency;
        if (totalSamples <= 0 || channels <= 0 || freq <= 0)
        {
            useFallbackLoopMode = true;
            return;
        }

        float[] allSamples = new float[totalSamples * channels];
        if (!heartbeatClip.GetData(allSamples, 0))
        {
            // Caso o clip esteja em Streaming e não permita leitura direta de amostras
            useFallbackLoopMode = true;
            return;
        }

        // Analisa envelope a cada 10ms para detectar cada ciclo "lub-dub" separado por silêncio (> 0.24s)
        const float bucketSeconds = 0.01f;
        int bucketSamples = Mathf.Max(1, Mathf.RoundToInt(freq * bucketSeconds));
        int numBuckets = Mathf.CeilToInt((float)totalSamples / bucketSamples);
        float[] bucketPeaks = new float[numBuckets];

        for (int b = 0; b < numBuckets; b++)
        {
            int s0 = b * bucketSamples;
            int s1 = Mathf.Min(totalSamples, s0 + bucketSamples);
            float peak = 0f;
            for (int s = s0; s < s1; s++)
            {
                int baseIdx = s * channels;
                for (int c = 0; c < channels; c++)
                {
                    float v = Mathf.Abs(allSamples[baseIdx + c]);
                    if (v > peak) peak = v;
                }
            }
            bucketPeaks[b] = peak;
        }

        const float activeThreshold = 0.06f;
        const float maxInternalGapSeconds = 0.24f;
        int maxInternalGapBuckets = Mathf.RoundToInt(maxInternalGapSeconds / bucketSeconds);

        int idx = 0;
        while (idx < numBuckets)
        {
            while (idx < numBuckets && bucketPeaks[idx] < activeThreshold) idx++;
            if (idx >= numBuckets) break;

            int segStartBucket = idx;
            int segEndBucket = idx;
            int silentRun = 0;
            idx++;

            while (idx < numBuckets)
            {
                if (bucketPeaks[idx] >= activeThreshold)
                {
                    segEndBucket = idx;
                    silentRun = 0;
                }
                else
                {
                    silentRun++;
                    if (silentRun > maxInternalGapBuckets)
                    {
                        break;
                    }
                }
                idx++;
            }

            float startSec = Mathf.Max(0f, segStartBucket * bucketSeconds - 0.02f);
            float endSec = Mathf.Min(heartbeatClip.length, (segEndBucket + 1) * bucketSeconds + 0.05f);
            if (endSec - startSec >= 0.16f)
            {
                int startSample = Mathf.Clamp(Mathf.RoundToInt(startSec * freq), 0, totalSamples - 1);
                int endSample = Mathf.Clamp(Mathf.RoundToInt(endSec * freq), startSample + 1, totalSamples);
                int sliceLen = endSample - startSample;

                float[] sliceData = new float[sliceLen * channels];
                Array.Copy(allSamples, startSample * channels, sliceData, 0, sliceLen * channels);

                ApplyEdgeFades(sliceData, sliceLen, channels, freq, fadeInSec: 0.01f, fadeOutSec: 0.02f);
                FindLubDubOffsets(bucketPeaks, segStartBucket, segEndBucket, bucketSeconds, startSec, out float lubOff, out float dubOff);

                AudioClip subClip = AudioClip.Create($"HeartbeatBeat_{slicedBeats.Count}", sliceLen, channels, freq, false);
                subClip.SetData(sliceData, 0);

                slicedBeats.Add(new BeatSlice
                {
                    Clip = subClip,
                    LubPeakOffset = lubOff,
                    DubPeakOffset = dubOff
                });
            }
        }

        if (slicedBeats.Count == 0)
        {
            useFallbackLoopMode = true;
        }
    }

    private static void ApplyEdgeFades(float[] data, int sampleCount, int channels, int frequency, float fadeInSec, float fadeOutSec)
    {
        int fadeInSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(fadeInSec * frequency));
        int fadeOutSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(fadeOutSec * frequency));

        for (int s = 0; s < fadeInSamples; s++)
        {
            float gain = 0.5f * (1f - Mathf.Cos(Mathf.PI * s / Mathf.Max(1, fadeInSamples)));
            int baseIdx = s * channels;
            for (int c = 0; c < channels; c++)
            {
                data[baseIdx + c] *= gain;
            }
        }

        for (int s = 0; s < fadeOutSamples; s++)
        {
            float gain = 0.5f * (1f - Mathf.Cos(Mathf.PI * s / Mathf.Max(1, fadeOutSamples)));
            int samplePos = sampleCount - 1 - s;
            int baseIdx = samplePos * channels;
            for (int c = 0; c < channels; c++)
            {
                data[baseIdx + c] *= gain;
            }
        }
    }

    private static void FindLubDubOffsets(
        float[] bucketPeaks,
        int startBucket,
        int endBucket,
        float bucketSeconds,
        float sliceStartSec,
        out float lubOffset,
        out float dubOffset)
    {
        int lubBucket = startBucket;
        float lubVal = -1f;
        for (int b = startBucket; b <= endBucket; b++)
        {
            if (bucketPeaks[b] > lubVal)
            {
                lubVal = bucketPeaks[b];
                lubBucket = b;
            }
        }

        // Procura segundo pico ("DUB") separado pelo menos 100ms do "LUB"
        int minSeparationBuckets = Mathf.Max(1, Mathf.RoundToInt(0.10f / bucketSeconds));
        int dubBucket = -1;
        float dubVal = 0.06f;
        for (int b = startBucket; b <= endBucket; b++)
        {
            if (Mathf.Abs(b - lubBucket) < minSeparationBuckets) continue;
            if (bucketPeaks[b] > dubVal)
            {
                dubVal = bucketPeaks[b];
                dubBucket = b;
            }
        }

        if (dubBucket < 0)
        {
            lubOffset = Mathf.Max(0.02f, lubBucket * bucketSeconds - sliceStartSec);
            dubOffset = lubOffset + 0.20f;
            return;
        }

        int first = Mathf.Min(lubBucket, dubBucket);
        int second = Mathf.Max(lubBucket, dubBucket);
        lubOffset = Mathf.Max(0.02f, first * bucketSeconds - sliceStartSec);
        dubOffset = Mathf.Max(lubOffset + 0.08f, second * bucketSeconds - sliceStartSec);
    }

    private void Update()
    {
        if (!CanLocalPlayerExperienceFear())
        {
            if (currentFearIntensity > 0.001f || isBeatPulseActive || (heartbeatSource != null && heartbeatSource.isPlaying))
            {
                ResetFearState();
            }
            return;
        }

        if (!beatsInitialized || heartbeatSource == null)
        {
            EnsureLocalAudioPrepared();
        }

        float closestDist = FindClosestActiveHunterDistance();
        currentClosestHunterDistance = closestDist;

        float rawProximity = float.IsPositiveInfinity(closestDist)
            ? 0f
            : Mathf.Clamp01(Mathf.InverseLerp(fearStartDistance, fearMaxDistance, closestDist));
        float targetFear = rawProximity > 0f ? Mathf.Pow(rawProximity, distanceCurveExponent) : 0f;

        float blendSpeed = targetFear > currentFearIntensity ? fearRampUpSpeed : fearDecaySpeed;
        currentFearIntensity = Mathf.MoveTowards(currentFearIntensity, targetFear, blendSpeed * Time.deltaTime);
        if (currentFearIntensity < 0.001f && targetFear <= 0f)
        {
            currentFearIntensity = 0f;
        }

        UpdateHeartbeatAudioAndPulse();
        PushCameraEffects();
    }

    private bool CanLocalPlayerExperienceFear()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (cameraSetup == null) cameraSetup = GetComponent<PlayerCameraSetup>();
        if (playerController == null || cameraSetup == null) return false;

        if (!playerController.IsOwner) return false;
        if (playerController.IsHunter || playerController.IsDead || playerController.IsGhost || playerController.IsDying) return false;

        if (GameSpawner.Instance != null)
        {
            if (GameSpawner.Instance.IsGameOver || !GameSpawner.Instance.HasMatchStarted)
            {
                return false;
            }
        }

        return true;
    }

    private float FindClosestActiveHunterDistance()
    {
        float minDistance = float.PositiveInfinity;
        Vector3 myPos = transform.position;

        var activePlayers = PlayerController.ActivePlayers;
        if (activePlayers != null && activePlayers.Count > 0)
        {
            for (int i = 0; i < activePlayers.Count; i++)
            {
                float d = EvaluateHunterDistance(activePlayers[i], myPos);
                if (d < minDistance) minDistance = d;
            }
            return minDistance;
        }

        // Fallback leve para cenas de teste sem Netcode/OnNetworkSpawn
        if (fallbackScanCountdown <= 0)
        {
            cachedFallbackPlayers = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
            fallbackScanCountdown = 15;
        }
        else
        {
            fallbackScanCountdown--;
        }

        if (cachedFallbackPlayers != null)
        {
            for (int i = 0; i < cachedFallbackPlayers.Length; i++)
            {
                float d = EvaluateHunterDistance(cachedFallbackPlayers[i], myPos);
                if (d < minDistance) minDistance = d;
            }
        }

        return minDistance;
    }

    private float EvaluateHunterDistance(PlayerController candidate, Vector3 myPos)
    {
        if (candidate == null || candidate == playerController) return float.PositiveInfinity;
        if (!candidate.IsHunter || candidate.IsDead || candidate.IsGhost || candidate.IsDying) return float.PositiveInfinity;
        if (candidate.HunterSpawnIntro != null && candidate.HunterSpawnIntro.IsSpawningIntro) return float.PositiveInfinity;

        var kd = candidate.KnockdownLogic;
        if (kd != null && (kd.IsKnockedDown || kd.CurrentLives.Value <= 0))
        {
            return float.PositiveInfinity;
        }

        return Vector3.Distance(myPos, candidate.transform.position);
    }

    private void UpdateHeartbeatAudioAndPulse()
    {
        bool isMenuOpen = InGamePauseMenuController.IsMenuOpen;
        float volumeScale = isMenuOpen ? 0.55f : 1f;

        if (useFallbackLoopMode)
        {
            UpdateFallbackLoopAudio(volumeScale);
            return;
        }

        if (currentFearIntensity >= fearActivationThreshold && slicedBeats.Count > 0)
        {
            float dynamicInterval = CurrentBeatInterval;
            // Se o Hunter aproximar-se subitamente, encurta o timer pendente na hora
            if (nextBeatTimer > dynamicInterval)
            {
                nextBeatTimer = dynamicInterval;
            }

            nextBeatTimer -= Time.deltaTime;
            float minGap = Mathf.Max(0.22f, fastBeatInterval * 0.8f);
            if (nextBeatTimer <= 0f && Time.unscaledTime - lastBeatTriggeredAt >= minGap)
            {
                TriggerNextSlicedBeat(dynamicInterval, volumeScale);
            }
        }
        else
        {
            nextBeatTimer = 0f;
        }

        if (isBeatPulseActive)
        {
            activeBeatElapsed += Time.deltaTime;
            if (activeBeatElapsed >= activeBeatDuration)
            {
                isBeatPulseActive = false;
            }
        }
    }

    private void TriggerNextSlicedBeat(float dynamicInterval, float volumeScale)
    {
        BeatSlice slice = slicedBeats[nextBeatSliceIndex];
        nextBeatSliceIndex = (nextBeatSliceIndex + 1) % slicedBeats.Count;

        float pitch = Mathf.Lerp(minBeatPitch, maxBeatPitch, currentFearIntensity);
        float vol = Mathf.Lerp(minHeartbeatVolume, maxHeartbeatVolume, Mathf.Pow(currentFearIntensity, 0.75f)) * volumeScale;

        if (heartbeatSource != null && slice.Clip != null)
        {
            heartbeatSource.pitch = pitch;
            heartbeatSource.PlayOneShot(slice.Clip, vol);
        }

        isBeatPulseActive = true;
        activeBeatElapsed = 0f;
        activeBeatPitch = Mathf.Max(0.5f, pitch);
        activeBeatStrength = currentFearIntensity;
        activeLubOffset = slice.LubPeakOffset;
        activeDubOffset = slice.DubPeakOffset;
        activeBeatDuration = Mathf.Max(0.32f, (activeDubOffset + 0.16f) / activeBeatPitch);
        activeBeatSign = (beatCount % 2 == 0) ? 1f : -1f;
        beatCount++;
        lastBeatTriggeredAt = Time.unscaledTime;

        nextBeatTimer = dynamicInterval;
    }

    private void UpdateFallbackLoopAudio(float volumeScale)
    {
        if (heartbeatSource == null || heartbeatClip == null) return;

        if (currentFearIntensity < fearActivationThreshold)
        {
            if (heartbeatSource.isPlaying)
            {
                heartbeatSource.Stop();
            }
            heartbeatSource.loop = false;
            isBeatPulseActive = false;
            return;
        }

        if (heartbeatSource.clip != heartbeatClip)
        {
            heartbeatSource.clip = heartbeatClip;
        }
        heartbeatSource.loop = true;
        heartbeatSource.pitch = Mathf.Lerp(0.78f, 1.85f, Mathf.Pow(currentFearIntensity, 0.85f));
        heartbeatSource.volume = Mathf.Lerp(minHeartbeatVolume, maxHeartbeatVolume, Mathf.Pow(currentFearIntensity, 0.75f)) * volumeScale;

        if (!heartbeatSource.isPlaying)
        {
            heartbeatSource.Play();
        }

        // Sincroniza pulso visual usando o ciclo aproximado do clipe (4 batidas em heartbeat.mp3)
        float normalizedPhase = (heartbeatSource.time / Mathf.Max(0.1f, heartbeatClip.length)) * 4f;
        float subPhase = normalizedPhase - Mathf.Floor(normalizedPhase);
        activeBeatElapsed = subPhase * 0.65f;
        activeBeatPitch = 1f;
        activeLubOffset = 0.05f;
        activeDubOffset = 0.25f;
        activeBeatDuration = 0.65f;
        activeBeatStrength = currentFearIntensity;
        isBeatPulseActive = true;
    }

    private float EvaluateBeatPulseEnvelope()
    {
        if (!isBeatPulseActive) return 0f;

        float t = activeBeatElapsed * activeBeatPitch;
        float lub = EvalBellBump(t, activeLubOffset, attackSec: 0.032f, decaySec: 0.105f) * 0.85f;
        float dub = EvalBellBump(t, activeDubOffset, attackSec: 0.042f, decaySec: 0.135f);
        return Mathf.Clamp01(Mathf.Max(lub, dub)) * activeBeatStrength;
    }

    private static float EvalBellBump(float t, float peakTime, float attackSec, float decaySec)
    {
        float delta = t - peakTime;
        if (delta < 0f)
        {
            float n = 1f - (-delta / Mathf.Max(0.005f, attackSec));
            return n > 0f ? n * n : 0f;
        }
        float d = 1f - (delta / Mathf.Max(0.005f, decaySec));
        return d > 0f ? d * d : 0f;
    }

    private void PushCameraEffects()
    {
        if (cameraSetup == null) return;

        float beatPulse = EvaluateBeatPulseEnvelope();
        if (currentFearIntensity <= 0.001f && beatPulse <= 0.001f)
        {
            cameraSetup.ResetFearCameraState();
            return;
        }

        float visualScale = InGamePauseMenuController.IsMenuOpen ? 0.25f : 1f;

        float fovDelta = (maxFearFovReduction * currentFearIntensity + maxBeatFovPulse * beatPulse) * visualScale;
        float radiusDelta = (maxFearRadiusReduction * currentFearIntensity + maxBeatRadiusPulse * beatPulse) * visualScale;

        float shakeAmp = (maxContinuousShakeAmplitude * Mathf.Pow(currentFearIntensity, 1.25f) + maxBeatShakeBurst * beatPulse) * visualScale;
        float shakeFreq = Mathf.Lerp(minShakeFrequency, maxShakeFrequency, currentFearIntensity);

        float rollDeg = (maxContinuousRollDegrees > 0f || maxBeatRollKickDegrees > 0f)
            ? (Mathf.Sin(Time.time * 21f) * maxContinuousRollDegrees * currentFearIntensity
               + activeBeatSign * maxBeatRollKickDegrees * beatPulse) * visualScale
            : 0f;

        Vector2 jitter = Vector2.zero;
        if (maxScreenJitterOffset > 0.00001f)
        {
            float jitterAmp = maxScreenJitterOffset * (0.45f * currentFearIntensity + 0.85f * beatPulse) * visualScale;
            float jx = (Mathf.PerlinNoise(Time.time * 28f, 1.37f) - 0.5f) * 2f * jitterAmp;
            float jy = (Mathf.PerlinNoise(5.91f, Time.time * 31f) - 0.5f) * 2f * jitterAmp;
            jitter = new Vector2(jx, jy);
        }

        cameraSetup.SetFearCameraState(fovDelta, radiusDelta, shakeAmp, shakeFreq, rollDeg, jitter);
    }

    public void ResetFearState()
    {
        currentFearIntensity = 0f;
        currentClosestHunterDistance = float.PositiveInfinity;
        nextBeatTimer = 0f;
        isBeatPulseActive = false;
        activeBeatElapsed = 0f;

        if (heartbeatSource != null && heartbeatSource.isPlaying)
        {
            heartbeatSource.Stop();
            heartbeatSource.loop = false;
        }

        if (cameraSetup != null)
        {
            cameraSetup.ResetFearCameraState();
        }
    }
}
