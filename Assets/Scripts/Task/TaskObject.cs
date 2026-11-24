using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Componente que identifica e gerencia um objeto interativo de Tarefa (Task).
/// Garante que todos os Renderers do objeto e de seus filhos recebam a camada de renderização
/// 'Task' (bit 1, valor 2), permitindo que o Free Outline desenhe um contorno cinza
/// quando o objeto estiver no campo de visão direto (sem atravessar paredes ou obstáculos).
/// 
/// Ao remover a tag 'Task' do objeto, o componente é automaticamente destruído, a camada
/// de contorno é removida de todos os Renderers e o objeto deixa de ser considerado uma Task.
/// </summary>
[SelectionBase]
[DisallowMultipleComponent]
[ExecuteAlways]
public class TaskObject : MonoBehaviour
{
    public const string TaskTag = "Task";
    public const int TaskRenderingLayerIndex = 1; // Camada 'Task' no TagManager (bit 1) -> Contorno Cinza Geral
    public const uint TaskRenderingLayerMask = 1u << TaskRenderingLayerIndex; // Valor 2

    public const int TargetTaskRenderingLayerIndex = 2; // Camada 'TargetTask' no TagManager (bit 2) -> Contorno Roxo Objetivo
    public const uint TargetTaskRenderingLayerMask = 1u << TargetTaskRenderingLayerIndex; // Valor 4

    public const int HunterAlertRenderingLayerIndex = 3; // Camada 'HunterAlertTask' no TagManager (bit 3) -> Contorno Vermelho de Alerta
    public const uint HunterAlertRenderingLayerMask = 1u << HunterAlertRenderingLayerIndex; // Valor 8

    public const int HunterAllyRenderingLayerIndex = 4; // Camada 'HunterAlly' no TagManager (bit 4) -> Contorno Fogo Aliado da Morte
    public const uint HunterAllyRenderingLayerMask = 1u << HunterAllyRenderingLayerIndex; // Valor 16

    private static readonly List<TaskObject> allTasks = new List<TaskObject>();
    private static bool s_TasksSorted = false;

    public static IReadOnlyList<TaskObject> AllTasks
    {
        get
        {
            EnsureTasksSorted();
            return allTasks;
        }
    }

    /// <summary>
    /// Garante que a lista de tarefas esteja ordenada de forma 100% determinística
    /// por coordenadas (X, Z, Y) e nome no Servidor e em todos os Clientes.
    /// </summary>
    public static void EnsureTasksSorted()
    {
        if (s_TasksSorted && allTasks.Count > 0)
        {
            bool hasNull = false;
            for (int i = 0; i < allTasks.Count; i++)
            {
                if (allTasks[i] == null) { hasNull = true; break; }
            }
            if (!hasNull) return;
        }

        allTasks.Clear();
        var found = Object.FindObjectsByType<TaskObject>(FindObjectsInactive.Exclude);
        for (int i = 0; i < found.Length; i++)
        {
            var t = found[i];
            if (t != null && t.gameObject.CompareTag(TaskTag))
            {
                allTasks.Add(t);
            }
        }

        allTasks.Sort((a, b) =>
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;
            int cx = pa.x.CompareTo(pb.x);
            if (cx != 0) return cx;
            int cz = pa.z.CompareTo(pb.z);
            if (cz != 0) return cz;
            int cy = pa.y.CompareTo(pb.y);
            if (cy != 0) return cy;
            return string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
        });
        for (int i = 0; i < allTasks.Count; i++)
        {
            allTasks[i].TaskIndex = i;
        }
        s_TasksSorted = true;
    }

    [Header("Identificação da Tarefa")]
    [Tooltip("Índice determinístico desta tarefa na partida")]
    [SerializeField] private int taskIndex = -1;
    public int TaskIndex
    {
        get => taskIndex;
        set => taskIndex = value;
    }
    [Tooltip("Nome amigável da tarefa para exibição na interface")]
    [SerializeField] private string taskName;

    [Tooltip("Distância máxima de interação com a tarefa")]
    [SerializeField] private float interactionDistance = 2.5f;

    [Tooltip("Indica se esta tarefa já foi concluída")]
    [SerializeField] private bool isCompleted = false;

    private bool isCleaningUp = false;

    public string TaskName
    {
        get => string.IsNullOrEmpty(taskName) ? gameObject.name : taskName;
        set => taskName = value;
    }

    public float InteractionDistance => interactionDistance;
    public bool IsCompleted
    {
        get => isCompleted;
        set => isCompleted = value;
    }

    private Transform indicatorTarget;
    /// <summary>
    /// Ponto de âncora no mundo para posicionamento de indicadores direcionais e marcadores da tarefa.
    /// Fica posicionado logo acima do objeto para visibilidade ideal.
    /// </summary>
    public Transform IndicatorTarget
    {
        get
        {
            if (indicatorTarget == null)
            {
                var child = transform.Find("IndicatorTarget");
                if (child != null)
                {
                    // Reposiciona para a altura ideal caso esteja deslocado
                    if (child.position.y - transform.position.y > 2.5f || child.position.y - transform.position.y < 0.2f)
                    {
                        child.position = new Vector3(child.position.x, transform.position.y + 1.6f, child.position.z);
                    }
                    indicatorTarget = child;
                }
                else
                {
                    var anchor = new GameObject("IndicatorTarget").transform;
                    var renderers = GetComponentsInChildren<Renderer>(true);
                    Vector3 centerPos = transform.position;
                    if (renderers != null && renderers.Length > 0)
                    {
                        Bounds totalBounds = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++)
                        {
                            totalBounds.Encapsulate(renderers[i].bounds);
                        }
                        centerPos = new Vector3(totalBounds.center.x, totalBounds.min.y, totalBounds.center.z);
                    }
                    anchor.position = centerPos + Vector3.up * 1.6f;
                    anchor.SetParent(transform, true);
                    indicatorTarget = anchor;
                }
            }
            return indicatorTarget;
        }
    }

    private void Awake()
    {
        if (!gameObject.CompareTag(TaskTag))
        {
            HandleUntaggedRemoval();
            return;
        }

        ApplyTaskRenderingLayer();
    }

    private void OnEnable()
    {
        if (!gameObject.CompareTag(TaskTag))
        {
            HandleUntaggedRemoval();
            return;
        }

        if (!allTasks.Contains(this))
        {
            allTasks.Add(this);
            s_TasksSorted = false;
        }
        ApplyTaskRenderingLayer();
    }

    private void OnDisable()
    {
        allTasks.Remove(this);
        s_TasksSorted = false;
        RemoveTaskRenderingLayer();
    }

    private void OnDestroy()
    {
        CleanupTask();
    }

    private void Update()
    {
        // Monitora em tempo real: se a tag foi alterada para Untagged ou outra tag,
        // remove imediatamente o contorno e o componente TaskObject.
        if (!gameObject.CompareTag(TaskTag))
        {
            HandleUntaggedRemoval();
        }
    }

    private void OnValidate()
    {
        if (!gameObject.CompareTag(TaskTag))
        {
            HandleUntaggedRemoval();
            return;
        }

        ApplyTaskRenderingLayer();
    }

    private void Reset()
    {
        if (string.IsNullOrEmpty(taskName))
        {
            taskName = gameObject.name;
        }

        if (gameObject.CompareTag(TaskTag))
        {
            ApplyTaskRenderingLayer();
        }
    }

    /// <summary>
    /// Desfaz as configurações de Task quando a tag é retirada do objeto.
    /// Remove o contorno (renderingLayerMask) e destrói este componente.
    /// </summary>
    private void HandleUntaggedRemoval()
    {
        if (isCleaningUp) return;
        isCleaningUp = true;

        CleanupTask();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && !gameObject.CompareTag(TaskTag))
                {
                    UnityEditor.Undo.DestroyObjectImmediate(this);
                }
            };
            return;
        }
#endif

        if (Application.isPlaying)
        {
            Destroy(this);
        }
    }

    /// <summary>
    /// Limpa o registro estático e remove a camada de contorno dos renderers.
    /// </summary>
    public void CleanupTask()
    {
        allTasks.Remove(this);
        RemoveTaskRenderingLayer();
    }

    /// <summary>
    /// Garante que os Renderers deste objeto possuam a camada de outline ativada.
    /// </summary>
    public void EnsureTaskConfiguration()
    {
        if (gameObject.CompareTag(TaskTag))
        {
            ApplyTaskRenderingLayer();
        }
    }

    /// <summary>
    /// Ativa o bit da camada 'Task' em todos os Renderers do objeto e sub-objetos.
    /// </summary>
    public void ApplyTaskRenderingLayer()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            uint newMask = r.renderingLayerMask | TaskRenderingLayerMask;
            if (r.renderingLayerMask != newMask)
            {
                r.renderingLayerMask = newMask;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(r);
                }
#endif
            }
        }
    }

    /// <summary>
    /// Remove o bit da camada 'Task' de todos os Renderers.
    /// </summary>
    public void RemoveTaskRenderingLayer()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            uint newMask = r.renderingLayerMask & ~(TaskRenderingLayerMask | TargetTaskRenderingLayerMask | HunterAlertRenderingLayerMask);
            if (r.renderingLayerMask != newMask)
            {
                r.renderingLayerMask = newMask;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(r);
                }
#endif
            }
        }
    }

    /// <summary>
    /// Define se esta tarefa é o objetivo atual do jogador local nesta máquina.
    /// Quando verdadeiro, ativa o contorno ROXO exclusivo (TargetTaskRenderingLayerMask).
    /// Quando falso, restaura o contorno cinza padrão (TaskRenderingLayerMask).
    /// Como a renderingLayerMask é local no cliente, apenas este jogador vê o contorno roxo.
    /// </summary>
    public void SetAsLocalTarget(bool isTarget)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            uint mask = r.renderingLayerMask;
            if (isTarget)
            {
                // Remove cinza/alerta e adiciona roxo
                mask &= ~(TaskRenderingLayerMask | HunterAlertRenderingLayerMask);
                mask |= TargetTaskRenderingLayerMask;
            }
            else
            {
                // Remove roxo e restaura cinza
                mask &= ~TargetTaskRenderingLayerMask;
                mask |= TaskRenderingLayerMask;
            }

            if (r.renderingLayerMask != mask)
            {
                r.renderingLayerMask = mask;
            }
        }
    }

    /// <summary>
    /// Define se esta tarefa está em estado de ALERTA para os Hunters nesta máquina (local no cliente).
    /// Quando verdadeiro, ativa o contorno VERMELHO exclusivo (HunterAlertRenderingLayerMask).
    /// Quando falso, restaura o contorno cinza padrão (TaskRenderingLayerMask).
    /// </summary>
    public void SetAsHunterAlert(bool isAlert)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            uint mask = r.renderingLayerMask;
            if (isAlert)
            {
                // Remove cinza/roxo e adiciona vermelho de alerta para o Hunter
                mask &= ~(TaskRenderingLayerMask | TargetTaskRenderingLayerMask);
                mask |= HunterAlertRenderingLayerMask;
            }
            else
            {
                // Remove vermelho de alerta e restaura cinza padrão
                mask &= ~HunterAlertRenderingLayerMask;
                mask |= TaskRenderingLayerMask;
            }

            if (r.renderingLayerMask != mask)
            {
                r.renderingLayerMask = mask;
            }
        }
    }

    /// <summary>
    /// Remove o contorno de alerta vermelho de todas as tarefas locais.
    /// </summary>
    public static void ClearAllHunterAlerts()
    {
        foreach (var task in allTasks)
        {
            if (task != null)
            {
                task.SetAsHunterAlert(false);
            }
        }
    }

    /// <summary>
    /// Remove o contorno roxo de todas as tarefas locais, restaurando o contorno cinza.
    /// </summary>
    public static void ClearAllLocalTargets()
    {
        foreach (var task in allTasks)
        {
            if (task != null)
            {
                task.SetAsLocalTarget(false);
            }
        }
    }

    /// <summary>
    /// Registra e configura um GameObject como objeto de Task.
    /// </summary>
    public static TaskObject Register(GameObject go)
    {
        if (go == null) return null;

        if (!go.CompareTag(TaskTag))
        {
            try
            {
                go.tag = TaskTag;
            }
            catch
            {
                // Tag pode não estar cadastrada no TagManager
            }
        }

        if (!go.TryGetComponent<TaskObject>(out var task))
        {
            task = go.AddComponent<TaskObject>();
        }
        task.ApplyTaskRenderingLayer();
        return task;
    }

    /// <summary>
    /// Remove as configurações de Task de um GameObject.
    /// </summary>
    public static void Unregister(GameObject go)
    {
        if (go == null) return;

        if (go.TryGetComponent<TaskObject>(out var task))
        {
            task.CleanupTask();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.DestroyObjectImmediate(task);
                return;
            }
#endif
            Destroy(task);
        }
        else
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.renderingLayerMask &= ~TaskRenderingLayerMask;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(r);
                }
#endif
            }
        }
    }

    /// <summary>
    /// Inicialização automática ao carregar cenas no jogo: localiza qualquer objeto com a tag 'Task'
    /// e garante que herde o TaskObject e as configurações de contorno.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoRegisterAllSceneTasks()
    {
        GameObject[] taskObjects = null;
        try
        {
            taskObjects = GameObject.FindGameObjectsWithTag(TaskTag);
        }
        catch
        {
            // Tag pode não existir em cenas de inicialização rápida
        }

        if (taskObjects != null)
        {
            for (int i = 0; i < taskObjects.Length; i++)
            {
                Register(taskObjects[i]);
            }
        }
    }
}
