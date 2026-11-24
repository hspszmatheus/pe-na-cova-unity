using UnityEngine;

public class PlayerAnimationLogic : MonoBehaviour
{
    private Animator characterAnimator;
    private Unity.Netcode.Components.NetworkAnimator networkAnimator;
    private PlayerController playerController;

    public void Initialize()
    {
        characterAnimator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponent<Unity.Netcode.Components.NetworkAnimator>();
        playerController = GetComponent<PlayerController>();
    }

    public void EnsureActiveAnimator()
    {
        if (characterAnimator == null || !characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator = GetComponentInChildren<Animator>();
        }
    }

    public void HandleAnimation(Vector2 moveInput, bool isRunning, bool isGrounded)
    {
        EnsureActiveAnimator();
        if (characterAnimator == null) return;

        bool isWalking = moveInput.magnitude > 0.1f;
        characterAnimator.SetBool("Walking", isWalking);
        characterAnimator.SetBool("Running", isRunning && isWalking);

        // Avisa o Animator se estamos no chão ou no ar
        characterAnimator.SetBool("IsGrounded", isGrounded);
    }

    // Função que o PlayerController chama para iniciar o pulo
    public void TriggerJumpAnimation()
    {
        EnsureActiveAnimator();
        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Jump");
            characterAnimator.SetTrigger("Jump");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Jump");
                    networkAnimator.SetTrigger("Jump");
                }
            }
            catch {}
        }
    }

    public void TriggerGreetingAnimation()
    {
        if (playerController != null && playerController.IsHunter) return;
        EnsureActiveAnimator();

        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Greeting");
            characterAnimator.SetTrigger("Greeting");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Greeting");
                    networkAnimator.SetTrigger("Greeting");
                }
            }
            catch {}
        }
    }

    public void TriggerDance1Animation()
    {
        if (playerController != null && playerController.IsHunter) return;
        EnsureActiveAnimator();

        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Dance1");
            characterAnimator.SetTrigger("Dance1");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Dance1");
                    networkAnimator.SetTrigger("Dance1");
                }
            }
            catch {}
        }
    }

    public void TriggerDance2Animation()
    {
        if (playerController != null && playerController.IsHunter) return;
        EnsureActiveAnimator();

        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Dance2");
            characterAnimator.SetTrigger("Dance2");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Dance2");
                    networkAnimator.SetTrigger("Dance2");
                }
            }
            catch {}
        }
    }

    /// <summary>
    /// Finaliza com segurança as animações de dança e retorna suavemente para Idle se ainda estiver dançando.
    /// </summary>
    public void StopDanceAnimation()
    {
        EnsureActiveAnimator();
        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Dance1");
            characterAnimator.ResetTrigger("Dance2");
            var state = characterAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Dance1") || state.IsName("Dance2") || state.IsName("Emotes.Dance1") || state.IsName("Emotes.Dance2"))
            {
                characterAnimator.CrossFadeInFixedTime("Idle", 0.25f);
            }
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Dance1");
                    networkAnimator.ResetTrigger("Dance2");
                }
            }
            catch {}
        }
    }

    public void TriggerKickAnimation()
    {
        EnsureActiveAnimator();
        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Kick");
            characterAnimator.SetTrigger("Kick");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Kick");
                    networkAnimator.SetTrigger("Kick");
                }
            }
            catch {}
        }
    }

    /// <summary>
    /// Dispara a animação de interação com Task (baseada no mesmo padrão e estrutura do chute/kick).
    /// </summary>
    public void TriggerTaskInteractAnimation()
    {
        SetTaskInteractionAnimation(true);
    }

    /// <summary>
    /// Inicia ou interrompe a animação contínua de interação com uma Task, sincronizando via Animator e NetworkAnimator.
    /// </summary>
    public void SetTaskInteractionAnimation(bool isInteracting)
    {
        EnsureActiveAnimator();
        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.SetBool("IsInteracting", isInteracting);
            if (isInteracting)
            {
                characterAnimator.ResetTrigger("Interact");
                characterAnimator.SetTrigger("Interact");
            }
            else
            {
                characterAnimator.ResetTrigger("Interact");
                var state = characterAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Task Interact"))
                {
                    characterAnimator.CrossFadeInFixedTime("Idle", 0.20f);
                }
            }
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    if (networkAnimator.Animator != null)
                    {
                        networkAnimator.Animator.SetBool("IsInteracting", isInteracting);
                    }
                    if (isInteracting)
                    {
                        networkAnimator.ResetTrigger("Interact");
                        networkAnimator.SetTrigger("Interact");
                    }
                    else
                    {
                        networkAnimator.ResetTrigger("Interact");
                    }
                }
            }
            catch {}
        }
    }

    public void TriggerAttackAnimation()
    {
        if (characterAnimator == null || !characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator = GetComponentInChildren<Animator>();
        }

        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("Attack");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Attack");
                    networkAnimator.SetTrigger("Attack");
                }
                else if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
                {
                    characterAnimator.SetTrigger("Attack");
                }
            }
            catch
            {
                if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
                {
                    characterAnimator.SetTrigger("Attack");
                }
            }
        }
        else if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.SetTrigger("Attack");
        }
    }

    public void TriggerFireballAnimation()
    {
        if (characterAnimator == null || !characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator = GetComponentInChildren<Animator>();
        }

        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.ResetTrigger("FireballAttack");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("FireballAttack");
                    networkAnimator.SetTrigger("FireballAttack");
                }
                else if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
                {
                    characterAnimator.SetTrigger("FireballAttack");
                }
            }
            catch
            {
                if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
                {
                    characterAnimator.SetTrigger("FireballAttack");
                }
            }
        }
        else if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.SetTrigger("FireballAttack");
        }
    }

    public void ResetFireballTrigger()
    {
        if (characterAnimator == null || !characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator = GetComponentInChildren<Animator>();
        }

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("FireballAttack");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("FireballAttack");
                }
            }
            catch {}
        }
    }

    public void ResetAttackTrigger()
    {
        if (characterAnimator == null || !characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator = GetComponentInChildren<Animator>();
        }

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("Attack");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.ResetTrigger("Attack");
                }
            }
            catch {}
        }
    }

    public void TriggerDeathAnimation()
    {
        if (characterAnimator == null || !characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator = GetComponentInChildren<Animator>();
        }
        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.SetTrigger("Die");
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    networkAnimator.SetTrigger("Die");
                }
            }
            catch {}
        }
    }

    public void SetStiffState(bool stiff)
    {
        if (characterAnimator != null)
        {
            characterAnimator.speed = stiff ? 0f : 1f;
        }
    }

    /// <summary>
    /// Inicia ou encerra a animação de canalização da magia de revelação da Morte (Standing 2H Magic Area Attack 02),
    /// sincronizando localmente no Animator e na rede via NetworkAnimator.
    /// Se isCancelled for true, faz transição imediata para Idle. Se false (conclusão aos 1.5s), permite o término fluido do golpe e transição suave.
    /// </summary>
    public void SetHunterRevealCastAnimation(bool isCasting, bool isCancelled = false)
    {
        EnsureActiveAnimator();
        if (characterAnimator != null && characterAnimator.gameObject.activeInHierarchy)
        {
            characterAnimator.SetBool("IsCastingReveal", isCasting);
            if (isCasting)
            {
                characterAnimator.ResetTrigger("RevealCast");
                characterAnimator.SetTrigger("RevealCast");
            }
            else
            {
                characterAnimator.ResetTrigger("RevealCast");
                if (isCancelled)
                {
                    var state = characterAnimator.GetCurrentAnimatorStateInfo(0);
                    if (state.IsName("Hunter Reveal Cast"))
                    {
                        characterAnimator.CrossFadeInFixedTime("Idle", 0.15f);
                    }
                }
            }
        }

        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            try
            {
                if (networkAnimator.IsOwner || (networkAnimator.IsServer && !networkAnimator.IsClient))
                {
                    if (networkAnimator.Animator != null)
                    {
                        networkAnimator.Animator.SetBool("IsCastingReveal", isCasting);
                    }
                    if (isCasting)
                    {
                        networkAnimator.ResetTrigger("RevealCast");
                        networkAnimator.SetTrigger("RevealCast");
                    }
                    else
                    {
                        networkAnimator.ResetTrigger("RevealCast");
                    }
                }
            }
            catch {}
        }
    }
}