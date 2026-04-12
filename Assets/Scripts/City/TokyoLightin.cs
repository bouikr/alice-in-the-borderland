// TokyoLighting.cs
using UnityEngine;
using UnityEngine.Rendering;

public class TokyoLighting : MonoBehaviour
{
    [Header("Lumière directionnelle (lune)")]
    public Light moonLight;

    [Header("Néons")]
    public Color[] neonColors = {
        new Color(1f, 0.1f, 0.3f),   // Rouge néon
        new Color(0.1f, 0.8f, 1f),   // Cyan néon
        new Color(1f, 0.6f, 0f),     // Orange néon
        new Color(0.8f, 0.1f, 1f)    // Violet néon
    };

    void Start()
    {
        SetupNightAtmosphere();
        SpawnNeonLights();
    }

    void SetupNightAtmosphere()
    {
        // Lune douce
        if (moonLight != null)
        {
            moonLight.color = new Color(0.6f, 0.7f, 1f);
            moonLight.intensity = 0.3f;
            moonLight.transform.rotation = Quaternion.Euler(45, -30, 0);
        }

        // Ambient sombre bleuté
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.02f, 0.03f, 0.08f);
        RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.15f);
        RenderSettings.fog = true;
        RenderSettings.fogDensity = 0.015f;
    }

    void SpawnNeonLights()
    {
        // Ajouter des point lights colorés sur les bâtiments
        GameObject[] buildings = GameObject.FindGameObjectsWithTag("Building");

        foreach (GameObject building in buildings)
        {
            if (Random.value > 0.4f)  // 60% des bâtiments ont des néons
            {
                GameObject lightObj = new GameObject("NeonLight");
                Light neon = lightObj.AddComponent<Light>();
                neon.type = LightType.Point;
                neon.color = neonColors[Random.Range(0, neonColors.Length)];
                neon.intensity = Random.Range(2f, 8f);
                neon.range = Random.Range(10f, 25f);

                // Positionner en haut du bâtiment
                float h = building.transform.localScale.y;
                lightObj.transform.position = building.transform.position
                    + new Vector3(Random.Range(-3f, 3f), h * 0.5f, Random.Range(-3f, 3f));
                lightObj.transform.SetParent(building.transform);
            }
        }
    }
}