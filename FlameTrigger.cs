using UnityEngine;

public class FlameTrigger : MonoBehaviour
{
    public GameObject welcomeText;
    public GameObject choiceCanvas;

    void Start()
    {
        if (welcomeText != null) welcomeText.SetActive(false);
        if (choiceCanvas != null) choiceCanvas.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("Player touche le Flame !");
            if (welcomeText != null) welcomeText.SetActive(true);
            if (choiceCanvas != null) choiceCanvas.SetActive(false);
            Invoke("ShowCards", 20f);
        }
    }

    void ShowCards()
    {
        if (welcomeText != null) welcomeText.SetActive(false);
        if (choiceCanvas != null) choiceCanvas.SetActive(true);

        // Déverrouiller le curseur pour cliquer sur les cartes
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CancelInvoke("ShowCards");
            if (welcomeText != null) welcomeText.SetActive(false);
            if (choiceCanvas != null) choiceCanvas.SetActive(false);

            // Reverrouiller le curseur
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}