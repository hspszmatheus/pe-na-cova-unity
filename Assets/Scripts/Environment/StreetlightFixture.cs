using UnityEngine;

/// <summary>
/// Controla as fontes de luz de um poste de iluminação pública (Spotlight para o chão e Pointlight para o brilho da lâmpada).
/// </summary>
public class StreetlightFixture : MonoBehaviour
{
    [Tooltip("Luz cônica direcionada para o chão / rua")]
    [SerializeField] private Light spotLight;

    [Tooltip("Luz pontual suave para fazer a cúpula da lâmpada brilhar visivelmente")]
    [SerializeField] private Light pointGlow;

    public Light SpotLight => spotLight;
    public Light PointGlow => pointGlow;

    private void Awake()
    {
        ResolveLightsIfNeeded();
    }

    public void ResolveLightsIfNeeded()
    {
        if (spotLight == null)
        {
            Transform spotT = transform.Find("Streetlight_Spot");
            if (spotT != null) spotLight = spotT.GetComponent<Light>();
        }

        if (pointGlow == null)
        {
            Transform glowT = transform.Find("Streetlight_BulbGlow");
            if (glowT != null) pointGlow = glowT.GetComponent<Light>();
        }
    }

    public void SetLightState(bool active)
    {
        ResolveLightsIfNeeded();

        if (spotLight != null)
        {
            spotLight.enabled = active;
        }

        if (pointGlow != null)
        {
            pointGlow.enabled = active;
        }
    }

    public void SetLightIntensityMultiplier(float multiplier)
    {
        ResolveLightsIfNeeded();

        if (spotLight != null)
        {
            spotLight.intensity *= multiplier;
        }

        if (pointGlow != null)
        {
            pointGlow.intensity *= multiplier;
        }
    }
}
