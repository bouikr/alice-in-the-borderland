using UnityEngine;

public class FlameLight : MonoBehaviour
{
    public Light flameLight;
    public float minIntensity = 2f;
    public float maxIntensity = 4f;
    public float speed = 3f;

    void Update()
    {
        // Lumière qui pulse comme une vraie flamme
        float noise = Mathf.PerlinNoise(Time.time * speed, 0f);
        flameLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, noise);
    }
}