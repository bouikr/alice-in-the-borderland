using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class sirvive : MonoBehaviour
{
    [Header("Drone")]
    public GameObject drone;

    [Header("Temps de survie")]
    public float survivalTime = 60f;
    private float timer = 0f;
    private bool gameFinished = false;

    [Header("UI")]
    public GameObject victoryPanel;
    public GameObject gameOverPanel;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI victoryText;
    public TextMeshProUGUI gameOverText;

    [Header("Scene")]
    public string returnScene = "demo_city_night";

    void Start()
    {
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    void Update()
    {
        if (gameFinished) return;

        timer += Time.deltaTime;

        // Timer affiché
        if (timerText != null)
        {
            float timeLeft = survivalTime - timer;
            timerText.text = "Temps : " + Mathf.CeilToInt(timeLeft) + "s";
            timerText.color = timeLeft <= 10f ? Color.red : Color.white;
        }

        if (timer >= survivalTime)
            Victory();
    }

    public void PlayerCaught()
    {
        if (gameFinished) return;
        GameOver();
    }

    void Victory()
    {
        gameFinished = true;
        if (drone != null) drone.SetActive(false);

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
            if (victoryText != null)
                victoryText.text = "🏆 VOUS AVEZ GAGNE !\nVous avez survecu au drone !";
        }

        Debug.Log("VICTOIRE !");
        Invoke("RetourVille", 4f);
    }

    void GameOver()
    {
        gameFinished = true;
        if (drone != null) drone.SetActive(false);

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
            if (gameOverText != null)
                gameOverText.text = "💀 GAME OVER !\nLe drone vous a attrape !";
        }

        Debug.Log("GAME OVER !");
        Invoke("RetourVille", 4f);
    }

    void RetourVille()
    {
        SceneManager.LoadScene(returnScene);
    }
}