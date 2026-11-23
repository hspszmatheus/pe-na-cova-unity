using UnityEngine;

/// <summary>
/// Gentle slow camera breathing sway for the blurred 3D Gas Station menu backdrop.
/// </summary>
public class MenuBackdropCameraDrift : MonoBehaviour
{
    [SerializeField] private Vector3 swayAmplitude = new Vector3(0.55f, 0.18f, 0.30f);
    [SerializeField] private float swaySpeed = 0.22f;
    [SerializeField] private float yawAmplitudeDegrees = 1.2f;

    private Vector3 anchorPosition;
    private Quaternion anchorRotation;

    private void Start()
    {
        anchorPosition = transform.position;
        anchorRotation = transform.rotation;
    }

    private void LateUpdate()
    {
        float t = Time.unscaledTime * swaySpeed;
        Vector3 offset = new Vector3(
            Mathf.Sin(t) * swayAmplitude.x,
            Mathf.Sin(t * 1.4f + 0.8f) * swayAmplitude.y,
            Mathf.Cos(t * 0.85f) * swayAmplitude.z);

        transform.position = anchorPosition + offset;
        transform.rotation = anchorRotation * Quaternion.Euler(
            Mathf.Sin(t * 0.7f) * 0.45f,
            Mathf.Sin(t * 0.65f + 1.1f) * yawAmplitudeDegrees,
            0f);
    }
}
