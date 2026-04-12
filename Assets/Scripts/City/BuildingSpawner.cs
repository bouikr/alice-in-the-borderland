


// BuildingSpawner.cs
using UnityEngine;

public class BuildingSpawner : MonoBehaviour
{
    [Header("Grille de la ville")]
    public int gridX = 10;
    public int gridZ = 10;
    public float blockSize = 20f;
    public float streetWidth = 6f;

    [Header("Bâtiments")]
    public GameObject[] buildingPrefabs;
    public float minHeight = 5f;
    public float maxHeight = 60f;

    void Start()
    {
        GenerateCity();
    }

    void GenerateCity()
    {
        float spacing = blockSize + streetWidth;

        for (int x = 0; x < gridX; x++)
        {
            for (int z = 0; z < gridZ; z++)
            {
                // Position avec offset aléatoire (réalisme)
                Vector3 pos = new Vector3(
                    x * spacing + Random.Range(-2f, 2f),
                    0,
                    z * spacing + Random.Range(-2f, 2f)
                );

                SpawnBuilding(pos);
            }
        }
    }

    void SpawnBuilding(Vector3 position)
    {
        // Choisir un prefab aléatoire
        int idx = Random.Range(0, buildingPrefabs.Length);
        GameObject building = Instantiate(buildingPrefabs[idx], position, Quaternion.identity);
        building.transform.SetParent(transform);

        // Hauteur aléatoire (style Tokyo = grands écarts)
        float height = Random.Range(minHeight, maxHeight);
        float baseScale = building.transform.localScale.y;
        building.transform.localScale = new Vector3(
            Random.Range(8f, 15f),
            height,
            Random.Range(8f, 15f)
        );

        // Rotation par paliers de 90°
        building.transform.rotation = Quaternion.Euler(
            0, Random.Range(0, 4) * 90f, 0
        );
    }
}
