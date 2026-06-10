using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Timer")]
    public TextMeshProUGUI timerText;       // TimerText
    public GameObject      panneauTimer;    // PanneauTimer

    [Header("Progression")]
    public TextMeshProUGUI progressionText; // à créer dans PanneauProgression
    public GameObject      panneauProgression;

    [Header("Vie Allié")]
    public TextMeshProUGUI allieHealthText; // à créer

    [Header("Ecran de fin")]
    public GameObject      panneauFin;     // à créer
    public TextMeshProUGUI finText;

    void Awake() => Instance = this;

    void Start()
    {
        if (panneauFin != null) panneauFin.SetActive(false);
        UpdateProgression(0, 3);
        UpdateAllie(100f);
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isPlaying) return;

        // Chrono
        float t       = GameManager.Instance.GetCurrentTime();
        int   minutes = Mathf.FloorToInt(t / 60f);
        int   seconds = Mathf.FloorToInt(t % 60f);

        if (timerText != null)
        {
            timerText.text = $"{minutes:00}:{seconds:00}";

            // Couleur rouge quand moins de 15 secondes
            timerText.color = t <= 15f ? Color.red : Color.white;
        }
    }

    public void UpdateProgression(int done, int total)
    {
        if (progressionText != null)
            progressionText.text = $"{done}/{total}";
    }

    public void UpdateAllie(float health)
    {
        if (allieHealthText != null)
        {
            allieHealthText.text  = $"ALLIE : {health:0}%";
            allieHealthText.color = health <= 25f ? Color.red :
                                    health <= 50f ? Color.yellow : Color.green;
        }
    }

    public void ShowVictoire()
    {
        if (panneauFin == null) return;
        panneauFin.SetActive(true);
        if (finText != null)
        {
            finText.text  = "VOUS ETES LIBRES !";
            finText.color = Color.green;
        }
    }

    public void ShowDefaiteTemps()
    {
        if (panneauFin == null) return;
        panneauFin.SetActive(true);
        if (finText != null)
        {
            finText.text  = "TEMPS ECOULE.\nVOUS AVEZ ECHOUE.";
            finText.color = Color.red;
        }
    }

    public void ShowDefaiteSacrifice()
    {
        if (panneauFin == null) return;
        panneauFin.SetActive(true);
        if (finText != null)
        {
            finText.text  = "VOTRE ALLIE EST MORT.\nVOUS AVEZ ECHOUE.";
            finText.color = Color.red;
        }
    }
}