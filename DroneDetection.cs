using UnityEngine;

public class DroneDetection : MonoBehaviour
{
    private sirvive gameManager;

    void Start()
    {
        gameManager = FindFirstObjectByType<sirvive>();

        if (gameManager == null)
            Debug.LogError("ERREUR : sirvive introuvable !");
        else
            Debug.Log("OK : sirvive trouve !");
    }

    // Détecte via Trigger
    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Trigger touche : " + other.name + " tag : " + other.tag);

        if (other.CompareTag("Player"))
            TriggerGameOver();
    }

    // Détecte via Collision normale
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Collision touche : " + collision.gameObject.name);

        if (collision.gameObject.CompareTag("Player"))
            TriggerGameOver();
    }

    void TriggerGameOver()
    {
        Debug.Log("GAME OVER DECLENCHE !");

        if (gameManager != null)
            gameManager.PlayerCaught();
        else
            Debug.LogError("gameManager est NULL !");
    }
}