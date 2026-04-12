// BuildingMaterials.cs
using UnityEngine;

public class BuildingMaterials : MonoBehaviour
{
    void Awake()
    {
        // Ces couleurs sont exécutées AVANT le spawner
        CreateAndAssignMaterials();
    }

    void CreateAndAssignMaterials()
    {
        // Palette Tokyo nuit
        Color[] tokyoColors = {
            new Color(0.10f, 0.15f, 0.25f), // Bleu nuit profond
            new Color(0.15f, 0.18f, 0.22f), // Gris ardoise
            new Color(0.08f, 0.12f, 0.20f), // Bleu foncé
            new Color(0.20f, 0.20f, 0.25f), // Gris bleuté
            new Color(0.12f, 0.10f, 0.18f), // Violet sombre
        };

        // Trouver tous les bâtiments dans la scène
        GameObject[] buildings = GameObject.FindGameObjectsWithTag("Untagged");

        foreach (GameObject obj in buildings)
        {
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend == null) continue;

            // Créer un nouveau matériau URP
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            // Choisir une couleur Tokyo aléatoire
            mat.color = tokyoColors[Random.Range(0, tokyoColors.Length)];
            mat.SetFloat("_Metallic", Random.Range(0.5f, 0.9f));
            mat.SetFloat("_Smoothness", Random.Range(0.6f, 0.95f));

            rend.material = mat;
        }
    }
}
