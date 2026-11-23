using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oculta visualmente sobreviventes e NPCs na tela dos Hunters enquanto a partida não começa
/// oficialmente (isto é, enquanto a Morte ainda estiver caindo dos céus e/ou o cronômetro
/// da partida ainda não tiver começado a rodar).
/// Preserva os Animators, física, colisores e movimentação ativos em segundo plano e nunca oculta
/// o próprio Hunter nem afeta a visão dos jogadores sobreviventes.
/// </summary>
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public class HunterPreMatchVisibility : MonoBehaviour
{
    private static readonly List<HunterPreMatchVisibility> activeInstances = new List<HunterPreMatchVisibility>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        activeInstances.Clear();
    }

    /// <summary>
    /// Garante a criação ou obtenção do componente de visibilidade pré-partida no objeto alvo.
    /// </summary>
    public static void EnsureAttached(GameObject target)
    {
        if (target == null) return;
        if (target.GetComponent<HunterPreMatchVisibility>() == null)
        {
            target.AddComponent<HunterPreMatchVisibility>();
        }
    }

    /// <summary>
    /// Reavalia imediatamente a visibilidade de todos os sobreviventes e NPCs registrados.
    /// </summary>
    public static void RefreshAll()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            var instance = activeInstances[i];
            if (instance == null)
            {
                activeInstances.RemoveAt(i);
                continue;
            }
            instance.RefreshVisibility();
        }
    }

    /// <summary>
    /// Retorna true apenas quando o jogador local é um Hunter e o jogo ainda não começou
    /// (cronômetro ainda não iniciou ou alguma Morte ainda não terminou de cair do céu).
    /// </summary>
    public static bool ShouldHideTargetsFromLocalClient()
    {
        if (!PlayerController.IsLocalPlayerHunter())
        {
            return false;
        }

        bool waitingMatchTimer = GameSpawner.Instance != null && !GameSpawner.Instance.HasMatchStarted;
        bool hunterStillFalling = HunterSpawnIntroSequence.IsAnySpawningIntro;
        return waitingMatchTimer || hunterStillFalling;
    }

    private PlayerController playerController;
    private PlayerCharacterRandomizer characterRandomizer;
    private readonly List<Renderer> cachedRenderers = new List<Renderer>();
    private readonly HashSet<Renderer> hiddenRenderers = new HashSet<Renderer>();
    private bool renderersDirty = true;
    private bool isHiding = false;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        characterRandomizer = GetComponent<PlayerCharacterRandomizer>();
        renderersDirty = true;
    }

    private void OnEnable()
    {
        if (!activeInstances.Contains(this))
        {
            activeInstances.Add(this);
        }

        if (characterRandomizer == null)
        {
            characterRandomizer = GetComponent<PlayerCharacterRandomizer>();
        }
        if (characterRandomizer != null)
        {
            characterRandomizer.OnCharacterVisualsChanged -= HandleCharacterVisualsChanged;
            characterRandomizer.OnCharacterVisualsChanged += HandleCharacterVisualsChanged;
        }

        renderersDirty = true;
        RefreshVisibility();
    }

    private void Start()
    {
        renderersDirty = true;
        RefreshVisibility();
    }

    private void OnTransformChildrenChanged()
    {
        renderersDirty = true;
        if (isHiding || ShouldHideTargetsFromLocalClient())
        {
            RefreshVisibility();
        }
    }

    private void LateUpdate()
    {
        RefreshVisibility();
    }

    private void OnDisable()
    {
        if (characterRandomizer != null)
        {
            characterRandomizer.OnCharacterVisualsChanged -= HandleCharacterVisualsChanged;
        }

        activeInstances.Remove(this);
        RestoreHiddenRenderers();
    }

    private void OnDestroy()
    {
        activeInstances.Remove(this);
        RestoreHiddenRenderers();
    }

    private void HandleCharacterVisualsChanged(GameObject _)
    {
        renderersDirty = true;
        RefreshVisibility();
    }

    public void RefreshVisibility()
    {
        if (playerController != null && playerController.IsHunter)
        {
            if (isHiding)
            {
                RestoreHiddenRenderers();
            }
            return;
        }

        if (ShouldHideTargetsFromLocalClient())
        {
            ApplyHiddenState();
        }
        else if (isHiding)
        {
            RestoreHiddenRenderers();
        }
    }

    private void ApplyHiddenState()
    {
        if (renderersDirty)
        {
            GetComponentsInChildren(true, cachedRenderers);
            renderersDirty = false;
        }

        for (int i = 0; i < cachedRenderers.Count; i++)
        {
            Renderer r = cachedRenderers[i];
            if (r != null && r.enabled && r.gameObject.activeInHierarchy)
            {
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
        }

        isHiding = true;
    }

    private void RestoreHiddenRenderers()
    {
        if (hiddenRenderers.Count > 0)
        {
            foreach (var r in hiddenRenderers)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }
            hiddenRenderers.Clear();
        }

        isHiding = false;
    }
}
