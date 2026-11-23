using System.Collections;
using UnityEngine;

/// <summary>
/// Controla a foice da Morte (Hunter) durante o ataque com o efeito de corte estilizado (Slash).
/// - Substitui completamente o antigo efeito de fogo: o fogo foi removido.
/// - Ao atacar ('E'), a foice (Death_Scythe) surge nas mãos e executa o movimento do golpe (Harvesting).
/// - Durante a descida da foice, o corte amarelo estilizado (Slash) surge acompanhando a trajetória da lâmina.
/// - Ao término do ataque (1.15s), a foice e o corte desaparecem suavemente.
/// </summary>
public class HunterWeaponVisuals : MonoBehaviour
{
    [Header("Arma da Morte")]
    [Tooltip("Objeto da Foice (Death_Scythe)")]
    [SerializeField] private GameObject scytheObject;

    [Tooltip("Duração total da exibição da foice durante o golpe em segundos")]
    [SerializeField] private float attackDuration = 1.15f;

    [Header("Efeito de Corte Estilizado (Slash VFX)")]
    [Tooltip("Sistema de partículas do corte de fogo NamuFX (fireslash)")]
    [SerializeField] private ParticleSystem fireSlashEffect;

    [Tooltip("Componente legado de corte estilizado (slash5-HungNguyen)")]
    [SerializeField] private StylizedSlashEffect slashEffect;

    [Tooltip("Atraso em segundos para o corte disparar, perfeitamente sincronizado com o arco da foice")]
    [SerializeField] private float slashDelay = 0.20f;

    private Coroutine attackRoutine;

    public GameObject ScytheObject => scytheObject;
    public ParticleSystem FireSlashEffect => fireSlashEffect;
    public StylizedSlashEffect SlashEffect => slashEffect;
    public bool IsWeaponVisible => scytheObject != null && scytheObject.activeSelf;

    void Awake()
    {
#if UNITY_EDITOR
        previewSlashInEditor = false;
#endif
        InitializeComponents();
    }

#if UNITY_EDITOR
    [Header("Ferramentas de Ajuste Manual (Editor)")]
    [Tooltip("Marque para visualizar a foice e o corte congelado na Scene View enquanto ajusta posição, rotação e escala")]
    [SerializeField] private bool previewSlashInEditor = false;

    [Tooltip("Momento da simulação do corte para ajuste fino visual (0.01s a 0.40s)")]
    [Range(0.01f, 0.40f)]
    [SerializeField] private float previewSimulationTime = 0.15f;

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            FindReferences();
            UpdateEditorPreview();
        }
    }

    [ContextMenu("Ativar Preview na Scene View")]
    public void EnableEditorPreview()
    {
        previewSlashInEditor = true;
        UpdateEditorPreview();
    }

    [ContextMenu("Desativar Preview na Scene View")]
    public void DisableEditorPreview()
    {
        previewSlashInEditor = false;
        UpdateEditorPreview();
    }

    private void UpdateEditorPreview()
    {
        FindReferences();

        if (scytheObject != null)
        {
            scytheObject.SetActive(previewSlashInEditor);
        }

        if (fireSlashEffect != null)
        {
            fireSlashEffect.gameObject.SetActive(true);
            var pss = fireSlashEffect.GetComponentsInChildren<ParticleSystem>(true);
            if (previewSlashInEditor)
            {
                foreach (var ps in pss)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Simulate(previewSimulationTime, true, true);
                }
            }
            else
            {
                foreach (var ps in pss)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }
    }
#endif

    public void InitializeComponents()
    {
        FindReferences();

        if (fireSlashEffect != null)
        {
            fireSlashEffect.gameObject.SetActive(true);
            fireSlashEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else if (slashEffect != null)
        {
            slashEffect.gameObject.SetActive(true);
            slashEffect.Stop();
        }

        SetWeaponVisible(false);
    }

    public void FindReferences()
    {
        if (scytheObject == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "Death_Scythe")
                {
                    scytheObject = t.gameObject;
                    break;
                }
            }
        }

        if (fireSlashEffect == null)
        {
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps != null && (ps.name == "fireslash" || ps.name == "FireSlash" || ps.name.Contains("fireslash")))
                {
                    fireSlashEffect = ps;
                    break;
                }
            }
        }

        if (slashEffect == null)
        {
            slashEffect = GetComponentInChildren<StylizedSlashEffect>(true);
        }
    }

    void OnDisable()
    {
        StopAttackVisuals();
    }

    /// <summary>
    /// Ativa ou desativa a visibilidade da foice nas mãos da Morte.
    /// </summary>
    public void SetWeaponVisible(bool visible)
    {
        if (scytheObject != null && scytheObject.activeSelf != visible)
        {
            scytheObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Executa o ciclo visual completo do ataque:
    /// 1. Foice aparece na mão do Hunter
    /// 2. Corte estilizado fireslash (NamuFX) dispara acompanhando o arco da lâmina
    /// 3. Foice desaparece ao concluir o movimento
    /// </summary>
    public void PlayAttackVisuals(float customDuration = -1f)
    {
        if (scytheObject == null || (fireSlashEffect == null && slashEffect == null))
        {
            FindReferences();
        }

        float dur = customDuration > 0f ? customDuration : attackDuration;
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        attackRoutine = StartCoroutine(AttackVisualRoutine(dur));
    }

    /// <summary>
    /// Interrompe imediatamente os efeitos visuais (ex: se o Hunter for chutado/derrubado).
    /// </summary>
    public void StopAttackVisuals()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        if (fireSlashEffect != null)
        {
            fireSlashEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else if (slashEffect != null)
        {
            slashEffect.Stop();
        }

        SetWeaponVisible(false);
    }

    private IEnumerator AttackVisualRoutine(float duration)
    {
        // 1. A foice surge nas mãos da Morte
        SetWeaponVisible(true);

        // 2. Aguarda o instante exato em que a foice inicia a descida cortante
        if (slashDelay > 0f)
        {
            yield return new WaitForSeconds(slashDelay);
        }

        // 3. O corte fireslash (NamuFX) surge acompanhando a foice
        if (fireSlashEffect != null)
        {
            fireSlashEffect.gameObject.SetActive(true);
            fireSlashEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            fireSlashEffect.Play(true);
        }
        else if (slashEffect != null)
        {
            slashEffect.gameObject.SetActive(true);
            slashEffect.Play();
        }

        // 4. Aguarda o restante da animação de ataque
        float remaining = Mathf.Max(0f, duration - slashDelay);
        yield return new WaitForSeconds(remaining);

        // 5. A foice desaparece ao concluir o movimento
        SetWeaponVisible(false);
        attackRoutine = null;
    }
}
