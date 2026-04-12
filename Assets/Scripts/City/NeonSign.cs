// NeonSign.cs
using UnityEngine;

public class NeonSign : MonoBehaviour
{
    public float flickerSpeed = 0.1f;
    public float flickerChance = 0.02f;
    private Light neonLight;
    private float baseIntensity;

    void Start()
    {
        neonLight = GetComponent<Light>();
        baseIntensity = neonLight.intensity;
        InvokeRepeating("Flicker", 0, flickerSpeed);
    }

    void Flicker()
    {
        if (Random.value < flickerChance)
        {
            // Clignotement style vieux néon
            neonLight.intensity = Random.value < 0.5f ? 0 : baseIntensity;
            Invoke("RestoreIntensity", Random.Range(0.05f, 0.2f));
        }
    }

    void RestoreIntensity()
    {
        neonLight.intensity = baseIntensity;
    }
}