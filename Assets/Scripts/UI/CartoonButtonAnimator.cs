using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reproduces the bouncy overshoot/squash-stretch motion from FCCartoonGUI_UIAnimation Data curves
/// cleanly in runtime without missing external animation package scripts.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CartoonButtonAnimator : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Header("Sprite Swaps (Opcional)")]
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite hoverSprite;
    [SerializeField] private Sprite pressedSprite;

    [Header("FCCartoonGUI Tween")]
    [SerializeField] private bool enableIdlePulse = true;
    [SerializeField] private float idlePulseSpeed = 2.4f;
    [SerializeField] private float idlePulseAmplitude = 0.03f;
    [SerializeField] private float hoverScale = 1.07f;
    [SerializeField] private float pressedScale = 0.92f;
    [SerializeField] private float hoverTiltDegrees = 1.6f;
    [SerializeField] private float springSpeed = 16f;

    private RectTransform rectTransform;
    private Vector3 baseScale = Vector3.one;
    private float currentScale = 1f;
    private float scaleVelocity;
    private float currentAngle;
    private float angleVelocity;

    private bool isHovered;
    private bool isPressed;
    private Selectable selectable;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        baseScale = rectTransform.localScale;
        if (baseScale.sqrMagnitude < 0.0001f)
            baseScale = Vector3.one;
        if (targetImage == null)
            targetImage = GetComponent<Image>();
        selectable = GetComponent<Selectable>();
    }

    private void OnEnable()
    {
        PlayPopBounce(0.72f, 6.5f, 0f);
    }

    public void PlayPopBounce(float startScale = 0.72f, float popImpulse = 6.8f, float startAngle = 0f)
    {
        isHovered = false;
        isPressed = false;
        currentScale = startScale;
        scaleVelocity = popImpulse; // Pop-in bounce matching Panel_Scale / Title_Scale
        currentAngle = startAngle;
        angleVelocity = 0f;
        if (rectTransform != null)
        {
            rectTransform.localScale = baseScale * currentScale;
            rectTransform.localRotation = Quaternion.Euler(0f, 0f, currentAngle);
        }
        ApplyVisuals();
    }

    public void SetIdlePulseEnabled(bool enabled)
    {
        enableIdlePulse = enabled;
    }

    private void Update()
    {
        bool interactable = selectable == null || selectable.interactable;
        float target = 1f;
        float targetAngle = 0f;

        if (interactable)
        {
            if (isPressed)
            {
                target = pressedScale;
                targetAngle = 0f;
            }
            else if (isHovered)
            {
                target = hoverScale;
                targetAngle = hoverTiltDegrees;
            }
            else if (enableIdlePulse)
            {
                target = 1f + Mathf.Sin(Time.unscaledTime * idlePulseSpeed) * idlePulseAmplitude;
            }
        }
        else
        {
            isHovered = false;
            isPressed = false;
        }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        // Underdamped spring gives authentic cartoon overshoot matching FCCartoonGUI curveIn/Out
        float stiffness = springSpeed * springSpeed;
        float damping = springSpeed * 1.15f;

        float accel = (target - currentScale) * stiffness - scaleVelocity * damping;
        scaleVelocity += accel * dt;
        currentScale += scaleVelocity * dt;

        float angAccel = (targetAngle - currentAngle) * stiffness - angleVelocity * damping;
        angleVelocity += angAccel * dt;
        currentAngle += angleVelocity * dt;

        rectTransform.localScale = baseScale * currentScale;
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, currentAngle);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        bool interactable = selectable == null || selectable.interactable;
        if (interactable)
        {
            GameAudioManager.Instance?.PlayButtonHover();
        }

        isHovered = true;
        scaleVelocity += 1.4f;
        ApplyVisuals();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPressed = false;
        ApplyVisuals();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        bool interactable = selectable == null || selectable.interactable;
        if (interactable)
        {
            GameAudioManager.Instance?.PlayButtonClick();
        }

        isPressed = true;
        scaleVelocity -= 2.2f;
        ApplyVisuals();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        scaleVelocity += 3.2f;
        ApplyVisuals();
    }

    public void ConfigureSprites(Image img, Sprite normal, Sprite hover, Sprite pressed)
    {
        targetImage = img;
        normalSprite = normal;
        hoverSprite = hover;
        pressedSprite = pressed;
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        if (targetImage == null) return;
        if (isPressed && pressedSprite != null)
            targetImage.sprite = pressedSprite;
        else if (isHovered && hoverSprite != null)
            targetImage.sprite = hoverSprite;
        else if (normalSprite != null)
            targetImage.sprite = normalSprite;
    }
}
