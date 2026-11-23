using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TargetIndicators;
using TargetIndicators.Samples;

/// <summary>
/// Controla o sistema de indicadores direcionais e visuais para o jogador local utilizando o pacote Target Indicators.
/// 
/// Regras de Visibilidade e Isolamento:
/// - Sobrevivente Local (IsOwner && !IsHunter):
///     * Recebe um direcional e marcador ROXO exclusivo apontando para a sua task atribuída.
///     * Nunca vê tarefas ou direcionais de outros jogadores sobreviventes.
///     * Quando conclui a tarefa, o indicador é limpo imediatamente até a próxima ser liberada em 10s.
/// - Hunter Local (IsOwner && IsHunter):
///     * Não possui tasks normais de sobrevivente.
///     * 3.35s após qualquer sobrevivente concluir uma tarefa, recebe um alerta VERMELHO exclusivo:
///       o objeto da task ganha contorno vermelho e um direcional/marcador vermelho aponta para o local.
///     * O alerta dura exatamente 10.0s e então desaparece, restaurando a task ao contorno normal cinza.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class PlayerTaskIndicatorController : MonoBehaviour
{
    private PlayerController playerController;
    private GameObject spawnedCanvasObject;
    private TargetIndicatorManager indicatorManager;
    private VisualIndicatorManager visualManager;
    private TargetIndicatorId currentTrackedTargetId;
    private bool isTracking = false;
    private bool isInitialized = false;

    // Rastreamento da Morte (Hunter) para sobrevivente com a task de chutar o Hunter
    private Transform trackedHunterTransform = null;

    // Alertas ativos para o Hunter (taskIndex -> TargetIndicatorId)
    private readonly Dictionary<int, TargetIndicatorId> activeHunterAlertIds = new Dictionary<int, TargetIndicatorId>();
    private readonly Dictionary<int, Coroutine> activeHunterAlertCoroutines = new Dictionary<int, Coroutine>();

    // Alertas ativos para o Hunter contra jogadores marcados por inatividade (clientId -> TargetIndicatorId)
    private readonly Dictionary<ulong, TargetIndicatorId> activeMarkedPlayerIndicatorIds = new Dictionary<ulong, TargetIndicatorId>();
    private readonly Dictionary<ulong, Coroutine> activeMarkedPlayerCoroutines = new Dictionary<ulong, Coroutine>();
    private readonly Dictionary<ulong, PlayerController> activeMarkedPlayers = new Dictionary<ulong, PlayerController>();

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    private void Start()
    {
        if (AirplaneCrashCutsceneController.Instance != null)
        {
            AirplaneCrashCutsceneController.Instance.OnCutsceneFinished += HandleCutsceneFinished;
        }
    }

    private void HandleCutsceneFinished()
    {
        if (indicatorManager != null)
        {
            var cam = GetActiveGameplayCamera();
            if (cam != null)
            {
                indicatorManager.Camera = cam;
            }
        }

        if (playerController != null && playerController.IsOwner && !playerController.IsHunter)
        {
            if (playerController.CurrentAssignedTaskIndex >= 0)
            {
                UpdateTargetTask(playerController.CurrentAssignedTaskIndex);
            }
        }
    }

    /// <summary>
    /// Retorna dinamicamente a câmera ativa de gameplay do jogador local,
    /// garantindo que câmeras cinemáticas/cutscene desativadas nunca sejam utilizadas.
    /// </summary>
    public Camera GetActiveGameplayCamera()
    {
        if (playerController != null)
        {
            var camSetup = playerController.GetComponent<PlayerCameraSetup>();
            if (camSetup != null && camSetup.UnityCamera != null && camSetup.UnityCamera.isActiveAndEnabled)
            {
                return camSetup.UnityCamera;
            }
        }

        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled && main.name != "CutsceneCamera")
        {
            return main;
        }

        var allCameras = Camera.allCameras;
        for (int i = 0; i < allCameras.Length; i++)
        {
            var c = allCameras[i];
            if (c != null && c.isActiveAndEnabled && c.name != "CutsceneCamera")
            {
                return c;
            }
        }
        return null;
    }

    /// <summary>
    /// Inicializa o sistema de indicadores visuais para o jogador local (Sobrevivente ou Hunter).
    /// </summary>
    public void InitializeForLocalPlayer()
    {
        if (playerController == null || !playerController.IsOwner)
        {
            enabled = false;
            return;
        }

        if (!isInitialized)
        {
            EnsureCanvasCreated();
            isInitialized = true;
        }

        if (playerController.IsHunter)
        {
            // Hunter fica pronto para receber alertas vermelhos de tasks concluídas
            ClearTarget();
        }
        else
        {
            // Sobrevivente rastreia sua task objetivo atribuída em roxo
            UpdateTargetTask(playerController.CurrentAssignedTaskIndex);
        }
    }

    private void EnsureCanvasCreated()
    {
        if (spawnedCanvasObject != null) return;

        // Carrega o prefab do canvas configurado
        GameObject canvasPrefab = Resources.Load<GameObject>("TaskIndicatorCanvas");
#if UNITY_EDITOR
        if (canvasPrefab == null)
        {
            canvasPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/TaskIndicatorCanvas.prefab");
        }
#endif

        if (canvasPrefab != null)
        {
            spawnedCanvasObject = Instantiate(canvasPrefab);
            spawnedCanvasObject.name = playerController.IsHunter 
                ? "LocalHunterIndicatorCanvas" 
                : "LocalTaskIndicatorCanvas";

            indicatorManager = spawnedCanvasObject.GetComponentInChildren<TargetIndicatorManager>(true);
            visualManager = spawnedCanvasObject.GetComponentInChildren<VisualIndicatorManager>(true);

            if (indicatorManager != null)
            {
                indicatorManager.enabled = false;
                var cam = GetActiveGameplayCamera();
                if (cam != null) indicatorManager.Camera = cam;
            }

            ConfigureVisualIndicatorPrefab();
        }
        else
        {
            Debug.LogWarning("[PlayerTaskIndicatorController] Prefab 'TaskIndicatorCanvas' não encontrado em Resources ou Assets/Prefab!");
        }
    }

    private void ConfigureVisualIndicatorPrefab()
    {
        if (visualManager == null) return;

        if (playerController != null && playerController.IsHunter)
        {
            SetRedVisualIndicatorPrefab();
        }
        else
        {
            SetPurpleVisualIndicatorPrefab();
        }
    }

    private void SetPurpleVisualIndicatorPrefab()
    {
        if (visualManager == null) return;
        VisualIndicator taskPrefab = Resources.Load<VisualIndicator>("TaskVisualIndicator");
#if UNITY_EDITOR
        if (taskPrefab == null)
        {
            taskPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualIndicator>("Assets/Prefab/TaskVisualIndicator.prefab");
        }
#endif
        if (taskPrefab != null)
        {
            visualManager.DefaultVisualIndicatorPrefab = taskPrefab;
        }
    }

    private void SetRedVisualIndicatorPrefab()
    {
        if (visualManager == null) return;
        VisualIndicator hunterPrefab = Resources.Load<VisualIndicator>("HunterAlertVisualIndicator");
#if UNITY_EDITOR
        if (hunterPrefab == null)
        {
            hunterPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualIndicator>("Assets/Prefab/HunterAlertVisualIndicator.prefab");
        }
#endif
        if (hunterPrefab != null)
        {
            visualManager.DefaultVisualIndicatorPrefab = hunterPrefab;
        }
    }

    private void TintVisualIndicator(TargetIndicatorId indicatorId, Color color)
    {
        if (spawnedCanvasObject == null) return;
        var visualIndicators = spawnedCanvasObject.GetComponentsInChildren<VisualIndicator>(true);
        foreach (var vi in visualIndicators)
        {
            if (vi != null && vi.TargetIndicatorId.Equals(indicatorId))
            {
                var arrowImage = vi.transform.Find("Offset/Rotation Content/Arrow")?.GetComponent<UnityEngine.UI.Image>();
                if (arrowImage != null) arrowImage.color = color;

                var outlineImage = vi.transform.Find("Offset/Core Content/Outline")?.GetComponent<UnityEngine.UI.Image>();
                if (outlineImage != null) outlineImage.color = color;
            }
        }
    }

    #region Sobrevivente (Survivor) Task Tracking

    /// <summary>
    /// Atualiza o alvo rastreado pelo Target Indicators para a task atribuída ao sobrevivente.
    /// Suporta tanto tasks de objeto quanto a task de chutar o Hunter (Morte).
    /// </summary>
    public void UpdateTargetTask(int taskIndex)
    {
        if (playerController == null || !playerController.IsOwner || playerController.IsHunter) return;

        EnsureCanvasCreated();
        if (indicatorManager == null) return;

        ClearTarget();

        if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
        {
            return;
        }

        if (taskIndex == TaskManager.HunterKickTaskIndex || taskIndex == TaskManager.HunterDanceTaskIndex)
        {
            var hunter = PlayerController.GetClosestHunterPlayer(playerController.transform.position);
            if (hunter != null)
            {
                UpdateTargetHunter(hunter.transform);
            }
            return;
        }

        if (taskIndex < 0) return;

        TaskObject.EnsureTasksSorted();
        var allTasks = TaskObject.AllTasks;
        if (taskIndex >= allTasks.Count) return;

        var targetTask = allTasks[taskIndex];
        if (targetTask == null) return;

        SetPurpleVisualIndicatorPrefab();

        Transform targetAnchor = targetTask.IndicatorTarget;
        if (targetAnchor != null)
        {
            if (indicatorManager.TryAddTarget(targetAnchor, out var indicator))
            {
                currentTrackedTargetId = indicator.Id;
                isTracking = true;

                var cam = GetActiveGameplayCamera();
                if (cam != null) indicatorManager.Camera = cam;
                indicatorManager.GetChanges();

                Debug.Log($"[PlayerTaskIndicatorController] Direcional ativado para a task #{taskIndex} ('{targetTask.TaskName}')");
            }
        }
    }

    /// <summary>
    /// Ativa o indicador direcional roxo apontando continuamente para a posição da Morte (Hunter).
    /// </summary>
    public void UpdateTargetHunter(Transform hunterTransform)
    {
        if (playerController == null || !playerController.IsOwner || playerController.IsHunter) return;

        EnsureCanvasCreated();
        if (indicatorManager == null) return;

        ClearTarget();

        if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
        {
            return;
        }

        if (hunterTransform == null) return;

        trackedHunterTransform = hunterTransform;
        SetPurpleVisualIndicatorPrefab();

        Transform targetAnchor = hunterTransform.Find("IndicatorTarget");
        if (targetAnchor == null)
        {
            var child = new GameObject("IndicatorTarget").transform;
            child.SetParent(hunterTransform, false);
            child.localPosition = new Vector3(0f, 1.4f, 0f);
            targetAnchor = child;
        }

        if (indicatorManager.TryAddTarget(targetAnchor, out var indicator))
        {
            currentTrackedTargetId = indicator.Id;
            isTracking = true;

            var cam = GetActiveGameplayCamera();
            if (cam != null) indicatorManager.Camera = cam;
            indicatorManager.GetChanges();

            Debug.Log($"[PlayerTaskIndicatorController] Direcional ativado para a Morte (Hunter: '{hunterTransform.name}')!");
        }
    }

    /// <summary>
    /// Limpa o alvo atualmente rastreado pelo sobrevivente.
    /// </summary>
    public void ClearTarget()
    {
        if (playerController == null || playerController.CurrentAssignedTaskIndex != TaskManager.HunterDanceTaskIndex)
        {
            HunterDanceAreaVisual.Hide();
        }
        if (trackedHunterTransform != null)
        {
            var hunter = trackedHunterTransform.GetComponent<PlayerController>();
            if (hunter != null)
            {
                PlayerController.SetHunterTaskTargetVisual(hunter, false);
            }
            trackedHunterTransform = null;
        }

        if (indicatorManager != null && playerController != null && !playerController.IsHunter)
        {
            if (isTracking)
            {
                indicatorManager.TryRemoveTarget(currentTrackedTargetId);
                isTracking = false;
            }
            indicatorManager.RemoveAllTargets();
            indicatorManager.GetChanges();
        }

        if (playerController != null && !playerController.IsHunter)
        {
            CleanupAllVisualIndicators();
        }
    }

    #endregion

    #region Hunter Alert Tracking

    /// <summary>
    /// Dispara o alerta de task concluída para o Hunter local:
    /// - Ativa o contorno vermelho na task.
    /// - Adiciona um direcional vermelho apontando para a task no Target Indicators.
    /// - Inicia a contagem regressiva de duração (10.0s).
    /// </summary>
    public void TriggerHunterAlert(int taskIndex, float durationSeconds)
    {
        if (playerController == null || !playerController.IsOwner || !playerController.IsHunter) return;

        EnsureCanvasCreated();
        if (indicatorManager == null) return;

        TaskObject.EnsureTasksSorted();
        var allTasks = TaskObject.AllTasks;
        if (taskIndex < 0 || taskIndex >= allTasks.Count) return;

        var targetTask = allTasks[taskIndex];
        if (targetTask == null) return;

        // Se já havia um alerta correndo nesta mesma task, reinicia o timer
        if (activeHunterAlertCoroutines.TryGetValue(taskIndex, out var existingRoutine) && existingRoutine != null)
        {
            StopCoroutine(existingRoutine);
            activeHunterAlertCoroutines.Remove(taskIndex);
        }

        // Garante que o prefab de alerta vermelho do Hunter esteja ativo no VisualIndicatorManager
        SetRedVisualIndicatorPrefab();

        // Ativa o contorno vermelho no objeto da task
        targetTask.SetAsHunterAlert(true);

        // Toca o alerta sonoro para o Hunter indicando a task revelada em vermelho
        GameAudioManager.Instance?.PlayHunterTaskAlert(targetTask.transform.position);

        // Se ainda não estiver rastreando este target no indicatorManager, adiciona
        if (!activeHunterAlertIds.ContainsKey(taskIndex))
        {
            Transform targetAnchor = targetTask.IndicatorTarget;
            if (targetAnchor != null && indicatorManager.TryAddTarget(targetAnchor, out var indicator))
            {
                activeHunterAlertIds[taskIndex] = indicator.Id;
                isTracking = true;

                var cam = GetActiveGameplayCamera();
                if (cam != null) indicatorManager.Camera = cam;
                indicatorManager.GetChanges();

                Debug.Log($"[PlayerTaskIndicatorController] HUNTER ALERTA: Task #{taskIndex} ('{targetTask.TaskName}') marcada em VERMELHO com direcional por {durationSeconds}s!");
            }
        }

        // Inicia a contagem regressiva de encerramento do alerta para esta task
        Coroutine routine = StartCoroutine(HunterAlertCountdownRoutine(taskIndex, durationSeconds));
        activeHunterAlertCoroutines[taskIndex] = routine;
    }

    private IEnumerator HunterAlertCountdownRoutine(int taskIndex, float duration)
    {
        yield return new WaitForSeconds(duration);
        EndHunterAlert(taskIndex);
    }

    /// <summary>
    /// Encerra o alerta de uma task para o Hunter, removendo o contorno vermelho e o direcional.
    /// </summary>
    public void EndHunterAlert(int taskIndex)
    {
        if (activeHunterAlertCoroutines.TryGetValue(taskIndex, out var routine) && routine != null)
        {
            StopCoroutine(routine);
            activeHunterAlertCoroutines.Remove(taskIndex);
        }

        TaskObject.EnsureTasksSorted();
        var allTasks = TaskObject.AllTasks;
        if (taskIndex >= 0 && taskIndex < allTasks.Count)
        {
            var task = allTasks[taskIndex];
            if (task != null)
            {
                task.SetAsHunterAlert(false);
            }
        }

        if (activeHunterAlertIds.TryGetValue(taskIndex, out var indicatorId))
        {
            if (indicatorManager != null)
            {
                indicatorManager.TryRemoveTarget(indicatorId);
                indicatorManager.GetChanges();
            }
            CleanupVisualIndicator(indicatorId);
            activeHunterAlertIds.Remove(taskIndex);
        }

        if (activeHunterAlertIds.Count == 0 && activeMarkedPlayerIndicatorIds.Count == 0)
        {
            if (playerController != null && playerController.IsHunter)
            {
                isTracking = false;
            }
            if (indicatorManager != null)
            {
                indicatorManager.RemoveAllTargets();
                indicatorManager.GetChanges();
            }
            CleanupAllVisualIndicators();
        }

        Debug.Log($"[PlayerTaskIndicatorController] HUNTER ALERTA: Alerta da Task #{taskIndex} encerrado. Contorno voltou ao normal.");
    }

    /// <summary>
    /// Dispara o alerta de um jogador marcado para a Morte (inatividade de task) para o Hunter local:
    /// - Adiciona um direcional ROXO vibrante apontando para a posição do jogador fujão.
    /// - Inicia a contagem regressiva de duração ou persiste até que o jogador conclua uma task.
    /// </summary>
    public void TriggerHunterMarkedPlayerAlert(PlayerController targetPlayer, float durationSeconds)
    {
        if (playerController == null || !playerController.IsOwner || !playerController.IsHunter) return;
        if (targetPlayer == null) return;

        ulong clientId = targetPlayer.OwnerClientId;
        activeMarkedPlayers[clientId] = targetPlayer;
        EnsureCanvasCreated();
        if (indicatorManager == null) return;

        // Se já havia um alerta correndo para este mesmo jogador, reinicia o timer
        if (activeMarkedPlayerCoroutines.TryGetValue(clientId, out var existingRoutine) && existingRoutine != null)
        {
            StopCoroutine(existingRoutine);
            activeMarkedPlayerCoroutines.Remove(clientId);
        }

        // Para o alerta de jogador revelado, o indicador direcional DEVE ser ROXO vibrante
        SetPurpleVisualIndicatorPrefab();

        // Obtém ou cria a âncora de alvo no jogador (posicionada confortavelmente alta bem acima da cabeça)
        Transform anchor = targetPlayer.transform.Find("IndicatorTarget");
        if (anchor == null)
        {
            var anchorGo = new GameObject("IndicatorTarget");
            anchorGo.transform.SetParent(targetPlayer.transform, false);
            anchor = anchorGo.transform;
        }

        Vector3 basePos = targetPlayer.transform.position;
        Vector3 topOfHeadPos = basePos + Vector3.up * 1.80f;
        var anim = targetPlayer.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            var headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null)
            {
                topOfHeadPos = headBone.position + Vector3.up * 0.15f;
            }
        }
        // Exatamente 0.90m acima do topo da cabeça do personagem
        float desiredY = topOfHeadPos.y + 0.90f;
        anchor.position = new Vector3(basePos.x, desiredY, basePos.z);

        if (!activeMarkedPlayerIndicatorIds.ContainsKey(clientId))
        {
            if (indicatorManager.TryAddTarget(anchor, out var indicator))
            {
                activeMarkedPlayerIndicatorIds[clientId] = indicator.Id;
                isTracking = true;

                var cam = GetActiveGameplayCamera();
                if (cam != null) indicatorManager.Camera = cam;
                indicatorManager.GetChanges();

                TintVisualIndicator(indicator.Id, new Color(0.85f, 0.28f, 1.0f, 1.0f));
                string durationMsg = durationSeconds > 0f ? $"por {durationSeconds:F1}s" : "até que complete uma task";
                Debug.Log($"[PlayerTaskIndicatorController] HUNTER ALERTA DE JOGADOR: Player '{targetPlayer.Nickname}' (Client {clientId}) marcado com direcional ROXO {durationMsg}!");
            }
        }

        // Se durationSeconds > 0, encerra automaticamente após o tempo; caso contrário (0 ou negativo), persiste até o jogador concluir uma task
        if (durationSeconds > 0f)
        {
            Coroutine routine = StartCoroutine(MarkedPlayerAlertCountdownRoutine(clientId, durationSeconds));
            activeMarkedPlayerCoroutines[clientId] = routine;
        }
    }

    private IEnumerator MarkedPlayerAlertCountdownRoutine(ulong clientId, float duration)
    {
        yield return new WaitForSeconds(duration);
        EndHunterMarkedPlayerAlert(clientId);
    }

    /// <summary>
    /// Encerra o alerta de um jogador marcado para a Morte para o Hunter local.
    /// </summary>
    public void EndHunterMarkedPlayerAlert(ulong clientId)
    {
        if (activeMarkedPlayerCoroutines.TryGetValue(clientId, out var routine) && routine != null)
        {
            StopCoroutine(routine);
            activeMarkedPlayerCoroutines.Remove(clientId);
        }

        activeMarkedPlayers.Remove(clientId);

        if (activeMarkedPlayerIndicatorIds.TryGetValue(clientId, out var indicatorId))
        {
            if (indicatorManager != null)
            {
                indicatorManager.TryRemoveTarget(indicatorId);
                indicatorManager.GetChanges();
            }
            CleanupVisualIndicator(indicatorId);
            activeMarkedPlayerIndicatorIds.Remove(clientId);
        }

        if (activeHunterAlertIds.Count == 0 && activeMarkedPlayerIndicatorIds.Count == 0)
        {
            if (playerController != null && playerController.IsHunter)
            {
                isTracking = false;
            }
            if (indicatorManager != null)
            {
                indicatorManager.RemoveAllTargets();
                indicatorManager.GetChanges();
            }
            CleanupAllVisualIndicators();
        }

        Debug.Log($"[PlayerTaskIndicatorController] HUNTER ALERTA DE JOGADOR: Alerta do Client {clientId} encerrado.");
    }

    /// <summary>
    /// Encerra todos os alertas ativos do Hunter (tasks e jogadores marcados).
    /// </summary>
    public void ClearAllHunterAlerts()
    {
        foreach (var routine in activeHunterAlertCoroutines.Values)
        {
            if (routine != null) StopCoroutine(routine);
        }
        activeHunterAlertCoroutines.Clear();

        foreach (var routine in activeMarkedPlayerCoroutines.Values)
        {
            if (routine != null) StopCoroutine(routine);
        }
        activeMarkedPlayerCoroutines.Clear();

        activeMarkedPlayers.Clear();

        if (indicatorManager != null)
        {
            indicatorManager.RemoveAllTargets();
            indicatorManager.GetChanges();
        }
        CleanupAllVisualIndicators();
        activeHunterAlertIds.Clear();
        activeMarkedPlayerIndicatorIds.Clear();

        TaskObject.ClearAllHunterAlerts();

        if (playerController != null && playerController.IsHunter)
        {
            isTracking = false;
        }
    }

    private void CleanupVisualIndicator(TargetIndicatorId indicatorId)
    {
        if (visualManager != null)
        {
            visualManager.RemoveTargetIndicator(indicatorId);
        }

        if (spawnedCanvasObject != null)
        {
            var visualIndicators = spawnedCanvasObject.GetComponentsInChildren<VisualIndicator>(true);
            foreach (var vi in visualIndicators)
            {
                if (vi != null && vi.TargetIndicatorId.Equals(indicatorId))
                {
                    vi.gameObject.SetActive(false);
                    Destroy(vi.gameObject);
                }
            }
        }
    }

    private void CleanupAllVisualIndicators()
    {
        if (spawnedCanvasObject != null)
        {
            var visualIndicators = spawnedCanvasObject.GetComponentsInChildren<VisualIndicator>(true);
            for (int i = 0; i < visualIndicators.Length; i++)
            {
                var vi = visualIndicators[i];
                if (vi != null)
                {
                    if (visualManager != null)
                    {
                        visualManager.RemoveTargetIndicator(vi.TargetIndicatorId);
                    }
                    if (vi != null && vi.gameObject != null)
                    {
                        vi.gameObject.SetActive(false);
                        Destroy(vi.gameObject);
                    }
                }
            }
        }
    }

    #endregion

    private void LateUpdate()
    {
        // 1. Para o Hunter local: varre e sincroniza todos os sobreviventes marcados para a morte e atualiza suas âncoras no mundo
        if (playerController != null && playerController.IsHunter)
        {
            if (PlayerController.ActivePlayers != null)
            {
                for (int i = 0; i < PlayerController.ActivePlayers.Count; i++)
                {
                    var p = PlayerController.ActivePlayers[i];
                    if (p == null || p.IsHunter) continue;

                    bool shouldTrack = p.IsMarkedForDeath && !p.IsDead && !p.IsGhost;
                    bool isTrackingMarked = activeMarkedPlayerIndicatorIds.ContainsKey(p.OwnerClientId);

                    if (shouldTrack && !isTrackingMarked)
                    {
                        TriggerHunterMarkedPlayerAlert(p, 0f);
                    }
                    else if (!shouldTrack && isTrackingMarked)
                    {
                        EndHunterMarkedPlayerAlert(p.OwnerClientId);
                    }
                }
            }

            // Atualiza continuamente a posição no mundo de cada âncora de jogador marcado
            if (activeMarkedPlayers.Count > 0)
            {
                foreach (var kvp in activeMarkedPlayers)
                {
                    var markedPlayer = kvp.Value;
                    if (markedPlayer == null || !markedPlayer.gameObject.activeInHierarchy) continue;

                    Transform anchor = markedPlayer.transform.Find("IndicatorTarget");
                    if (anchor != null)
                    {
                        Vector3 basePos = markedPlayer.transform.position;
                        Vector3 topOfHeadPos = basePos + Vector3.up * 1.80f;
                        var anim = markedPlayer.GetComponentInChildren<Animator>();
                        if (anim != null && anim.isHuman)
                        {
                            var headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                            if (headBone != null)
                            {
                                topOfHeadPos = headBone.position + Vector3.up * 0.15f;
                            }
                        }

                        // Exatamente 0.90m acima do topo da cabeça do personagem
                        float desiredY = topOfHeadPos.y + 0.90f;
                        anchor.position = new Vector3(basePos.x, desiredY, basePos.z);
                    }
                }
            }
        }

        // 2. Para o Sobrevivente local: se a task atribuída for Chutar a Morte ou Dançar Próximo à Morte, sincroniza a âncora e o rastreamento do Hunter
        if (playerController != null && playerController.IsOwner && !playerController.IsHunter)
        {
            if (GameSpawner.Instance != null && !GameSpawner.Instance.IsMatchActive)
            {
                if (isTracking) ClearTarget();
                return;
            }

            if (playerController.CurrentAssignedTaskIndex == TaskManager.HunterKickTaskIndex ||
                playerController.CurrentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex)
            {
                var hunter = PlayerController.GetClosestHunterPlayer(playerController.transform.position);
                if (hunter != null && hunter.gameObject.activeInHierarchy && !hunter.IsDead)
                {
                    if (trackedHunterTransform != hunter.transform || !isTracking)
                    {
                        UpdateTargetHunter(hunter.transform);
                        PlayerController.SetHunterTaskTargetVisual(hunter, true);
                    }
                    else
                    {
                        // Garante que a silhueta roxa continue ativa no Hunter
                        PlayerController.SetHunterTaskTargetVisual(hunter, true);

                        // Atualiza dinamicamente a posição da âncora no mundo acima da cabeça do Hunter
                        Transform anchor = hunter.transform.Find("IndicatorTarget");
                        if (anchor != null)
                        {
                            Vector3 basePos = hunter.transform.position;
                            Vector3 topOfHeadPos = basePos + Vector3.up * 1.80f;
                            var anim = hunter.GetComponentInChildren<Animator>();
                            if (anim != null && anim.isHuman)
                            {
                                var headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                                if (headBone != null)
                                {
                                    topOfHeadPos = headBone.position + Vector3.up * 0.15f;
                                }
                            }

                            float desiredY = topOfHeadPos.y + 0.90f;
                            anchor.position = new Vector3(basePos.x, desiredY, basePos.z);
                        }
                    }

                    // Se a tarefa atual for Dançar Próximo à Morte, garante que o círculo roxo no chão esteja visível e rastreando o Hunter
                    if (playerController.CurrentAssignedTaskIndex == TaskManager.HunterDanceTaskIndex)
                    {
                        HunterDanceAreaVisual.Show(hunter.transform);
                    }
                }
                else if (isTracking && trackedHunterTransform != null)
                {
                    ClearTarget();
                }
            }
        }

        // Sincronização contínua após a Cinemachine (order 0) e PlayerCameraSetup (order 100)
        if (indicatorManager != null)
        {
            if (indicatorManager.enabled)
            {
                indicatorManager.enabled = false;
            }

            var activeCam = GetActiveGameplayCamera();
            if (activeCam != null && indicatorManager.Camera != activeCam)
            {
                indicatorManager.Camera = activeCam;
            }

            if (indicatorManager.Camera != null && indicatorManager.Camera.isActiveAndEnabled)
            {
                indicatorManager.GetChanges();
            }

            // Garante que o indicador dos jogadores marcados continue com o tom roxo vibrante
            if (activeMarkedPlayerIndicatorIds.Count > 0)
            {
                foreach (var id in activeMarkedPlayerIndicatorIds.Values)
                {
                    TintVisualIndicator(id, new Color(0.85f, 0.28f, 1.0f, 1.0f));
                }
            }

            // Se não estiver rastreando nada ativamente, garante que nenhum indicador fique órfão ou preso no canto da tela
            if (!isTracking)
            {
                CleanupAllVisualIndicators();
            }
        }
    }

    private void OnDisable()
    {
        if (playerController != null && playerController.IsHunter)
        {
            ClearAllHunterAlerts();
        }
        else
        {
            ClearTarget();
        }

        if (spawnedCanvasObject != null)
        {
            spawnedCanvasObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (spawnedCanvasObject != null && playerController != null && playerController.IsOwner)
        {
            spawnedCanvasObject.SetActive(true);
            if (!playerController.IsHunter)
            {
                UpdateTargetTask(playerController.CurrentAssignedTaskIndex);
            }
        }
    }

    private void OnDestroy()
    {
        if (AirplaneCrashCutsceneController.Instance != null)
        {
            AirplaneCrashCutsceneController.Instance.OnCutsceneFinished -= HandleCutsceneFinished;
        }

        if (playerController != null && playerController.IsHunter)
        {
            ClearAllHunterAlerts();
        }
        else
        {
            ClearTarget();
        }

        if (spawnedCanvasObject != null)
        {
            Destroy(spawnedCanvasObject);
            spawnedCanvasObject = null;
        }
    }
}
