using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class survive : MonoBehaviour
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
        if (victoryPanel != null)
            victoryPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    void Update()
    {
        if (gameFinished)
            return;

        timer += Time.deltaTime;

        // Affichage du temps restant
        if (timerText != null)
        {
            float timeLeft = survivalTime - timer;

            timerText.text = "Temps : " + Mathf.CeilToInt(timeLeft) + "s";

            if (timeLeft <= 10f)
                timerText.color = Color.red;
            else
                timerText.color = Color.white;
        }

        // Victoire
        if (timer >= survivalTime)
        {
            Victory();
        }
    }

    // Appelé quand le drone attrape le joueur
    public void PlayerCaught()
    {
        if (gameFinished)
            return;

        GameOver();
    }

    void Victory()
    {
        gameFinished = true;

        // Sauvegarde la victoire du mini-jeu Drone
        if (GlobalGameManager.Instance != null)
        {
            GlobalGameManager.Instance.droneWon = true;
        }

        if (drone != null)
            drone.SetActive(false);

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);

            if (victoryText != null)
            {
                victoryText.text =
                    "🏆 VOUS AVEZ GAGNE !\nVous avez survecu au drone !";
            }
        }

        Debug.Log("VICTOIRE DRONE");

        Invoke(nameof(RetourVille), 4f);
    }

    void GameOver()
    {
        gameFinished = true;

        // Perdre une vie
        if (GlobalGameManager.Instance != null)
        {
            GlobalGameManager.Instance.lives--;
        }

        if (drone != null)
            drone.SetActive(false);

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);

            if (gameOverText != null)
            {
                gameOverText.text =
                    "💀 GAME OVER !\nLe drone vous a attrape !";
            }
        }

        Debug.Log("DEFAITE DRONE");

        Invoke(nameof(RetourVille), 4f);
    }

    void RetourVille()
    {
        SceneManager.LoadScene(returnScene);
    }
}