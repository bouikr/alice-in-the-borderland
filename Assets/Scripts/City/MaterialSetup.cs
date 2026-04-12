using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class MaterialSetup : MonoBehaviour
{
    [ContextMenu("Apply Tokyo Building Material")]
    void ApplyMaterial()
    {
        // Vérifie si URP Lit est disponible
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("Shader URP Lit non trouvé ! Vérifie que URP est installé");
            return;
        }

        Material glassMat = new Material(shader);
        glassMat.color = new Color(0.1f, 0.15f, 0.25f, 0.7f); // Bleu nuit
        glassMat.SetFloat("_Metallic", 0.8f);
        glassMat.SetFloat("_Smoothness", 0.9f);
        glassMat.SetFloat("_Surface", 1); // Transparent
        glassMat.renderQueue = 3000;

        // Applique au MeshRenderer du GameObject
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.material = glassMat;
        }
        else
        {
            Debug.LogError("Pas de MeshRenderer sur cet objet !");
        }
    }
}