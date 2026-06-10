using UnityEngine;

public class GameProgressManager : MonoBehaviour
{
    public GameObject winPanel;
    public GameObject gameOverPanel;

    void Start()
    {
        if (GlobalGameManager.Instance == null)
            return;

        CheckGameState();
    }

    public void CheckGameState()
    {
        if (GlobalGameManager.Instance.lives <= 0)
        {
            gameOverPanel.SetActive(true);
            return;
        }

        if (GlobalGameManager.Instance.droneWon &&
            GlobalGameManager.Instance.diamondWon)
        {
            winPanel.SetActive(true);
        }
    }
}