using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuButtons : MonoBehaviour
{
    public void BackToMenu()
    {
        Time.timeScale = 1f;

        GlobalGameManager.Instance.lives = 2;
        GlobalGameManager.Instance.droneWon = false;
        GlobalGameManager.Instance.diamondWon = false;

        SceneManager.LoadScene(1);
    }
}