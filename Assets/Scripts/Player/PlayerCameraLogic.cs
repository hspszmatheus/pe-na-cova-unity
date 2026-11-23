using UnityEngine;

public class PlayerCameraLogic : MonoBehaviour
{
    private Transform cameraTransform;

    // Inicializado pelo Controller
    public void Initialize()
    {
        cameraTransform = GetComponentInChildren<Camera>(true).transform;
    }

    public Vector3 GetForwardDirection()
    {
        if (cameraTransform == null) return Vector3.forward;
        return Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
    }

    public Vector3 GetRightDirection()
    {
        if (cameraTransform == null) return Vector3.right;
        return Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
    }
}