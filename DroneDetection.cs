using UnityEngine;

public class DroneDetection : MonoBehaviour
{
    private survive gameManager;

    void Start()
    {
        gameManager = FindFirstObjectByType<survive>();

        if (gameManager == null)
            Debug.LogError("ERREUR : survive introuvable !");
        else
            Debug.Log("OK : survive trouve !");
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