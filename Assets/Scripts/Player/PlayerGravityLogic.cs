using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerGravityLogic : MonoBehaviour
{
    [SerializeField] public float gravity = -9.81f; // Força padrão da gravidade da Terra

    private float verticalVelocity;
    private CharacterController characterController;

    public void Initialize()
    {
        characterController = GetComponent<CharacterController>();
    }

    public void HandleGravity()
    {
        if (characterController.isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 gravityMove = new Vector3(0, verticalVelocity, 0);
        characterController.Move(gravityMove * Time.deltaTime);
    }
}