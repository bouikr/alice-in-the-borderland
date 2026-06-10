using UnityEngine;
using UnityEngine.SceneManagement;

public class ZoneDeclenchement : MonoBehaviour
{
    // Nom exact de ta future scène de puzzle
    public string nomScenePuzzle = "Scene_PuzzleTri"; 

    void OnTriggerStay(Collider other)
    {
        // Si c'est le joueur et qu'il appuie sur E
        if (other.CompareTag("Player") && Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log("Lancement du Puzzle...");
            SceneManager.LoadScene(nomScenePuzzle);
        }
    }
}