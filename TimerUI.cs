using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimerUI : MonoBehaviour
{
    public Image circleTimer;        // Image type "Filled" pour le cercle
    public TextMeshProUGUI timerText; // Texte au centre
    public float totalTime = 60f;

    private float currentTime;
    private bool isRunning = false;
    private bool choiceMade = false;

    void OnEnable()
    {
        currentTime = totalTime;
        isRunning = true;
        choiceMade = false;

        if (circleTimer != null) circleTimer.fillAmount = 1f;
        if (timerText != null) timerText.color = Color.white;
    }

    void Update()
    {
        if (!isRunning || choiceMade) return;

        currentTime -= Time.deltaTime;
        currentTime = Mathf.Max(currentTime, 0f);

        // Mettre à jour le cercle
        if (circleTimer != null)
            circleTimer.fillAmount = currentTime / totalTime;

        // Mettre à jour le texte
        if (timerText != null)
            timerText.text = Mathf.CeilToInt(currentTime).ToString();

        // Rouge quand moins de 10s
        if (currentTime <= 10f)
        {
            if (circleTimer != null) circleTimer.color = Color.red;
            if (timerText != null) timerText.color = Color.red;
        }
        else if (currentTime <= 30f)
        {
            if (circleTimer != null) circleTimer.color = Color.yellow;
        }

        if (currentTime <= 0f)
        {
            isRunning = false;
            TimeOut();
        }
    }

    public void StopTimer()
    {
        choiceMade = true;
        isRunning = false;
    }

    void TimeOut()
    {
        Debug.Log("TIME OUT !");
        // game over ici
    }
}