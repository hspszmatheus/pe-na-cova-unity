using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class PlayerCharacterRandomizer : NetworkBehaviour
{
    public GameObject[] characters;
    public NetworkVariable<int> selectedIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event System.Action<GameObject> OnCharacterVisualsChanged;

    private int pendingInitialIndex = -1;

    public GameObject GetActiveCharacterObject()
    {
        if (characters != null && selectedIndex.Value >= 0 && selectedIndex.Value < characters.Length)
        {
            return characters[selectedIndex.Value];
        }
        return null;
    }

    /// <summary>
    /// Permite ao servidor definir previamente (antes do spawn) ou a qualquer momento qual modelo visual este personagem usará.
    /// </summary>
    public void SetCharacterIndex(int index)
    {
        pendingInitialIndex = index;
        if (IsServer && IsSpawned)
        {
            selectedIndex.Value = index;
            UpdateVisuals(index);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (pendingInitialIndex >= 0)
            {
                selectedIndex.Value = pendingInitialIndex;
            }
            else if (selectedIndex.Value < 0)
            {
                int maxLen = (characters != null && characters.Length > 0) ? characters.Length : 1;
                selectedIndex.Value = Random.Range(0, maxLen);
            }
        }

        selectedIndex.OnValueChanged += (oldValue, newValue) => {
            UpdateVisuals(newValue);
        };

        if (selectedIndex.Value != -1)
        {
            UpdateVisuals(selectedIndex.Value);
        }
    }

    private void UpdateVisuals(int index)
    {
        var playerCtrl = GetComponent<PlayerController>();
        if (playerCtrl != null && (playerCtrl.IsGhost || playerCtrl.IsDead)) return;

        var npcCtrl = GetComponent<NPCController>();
        if (npcCtrl != null && npcCtrl.IsDead) return;

        if (characters == null) return;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] != null)
            {
                characters[i].SetActive(i == index);
                characters[i].transform.localScale = Vector3.one;
                characters[i].transform.localPosition = Vector3.zero;
            }
        }

        Transform visuals = transform.Find("Visuals");
        if (visuals != null)
        {
            visuals.localScale = Vector3.one;
            visuals.localPosition = Vector3.zero;
        }

        if (index < 0 || index >= characters.Length || characters[index] == null) return;

        Animator activeAnimator = characters[index].GetComponent<Animator>();
        
        var animLogic = GetComponent<PlayerAnimationLogic>();
        if (animLogic != null)
        {
            // PlayerAnimationLogic uses GetComponentInChildren<Animator>() which ignores inactive objects
            animLogic.Initialize(); 
        }

        try
        {
            var netAnim = GetComponent<NetworkAnimator>();
            if (netAnim != null && netAnim.Animator != activeAnimator)
            {
                netAnim.Animator = activeAnimator;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlayerCharacterRandomizer] Aviso ao atualizar NetworkAnimator.Animator: {ex.Message}");
        }

        OnCharacterVisualsChanged?.Invoke(characters[index]);
    }
}
