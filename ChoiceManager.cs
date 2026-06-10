using UnityEngine;
using UnityEngine.SceneManagement;

public class ChoiceManager : MonoBehaviour
{
    public GameObject choiceCanvas;
    public TimerUI timerUI;
    private bool choiceMade = false;

    void OnEnable()
    {
        choiceMade = false;
    }

    public void ResetChoice()
    {
        choiceMade = false;
    }

    public void ChoosePhysical()
    {
        if (choiceMade) return;
        choiceMade = true;
        if (timerUI != null) timerUI.StopTimer();
        if (choiceCanvas != null) choiceCanvas.SetActive(false);
        SceneManager.LoadScene(2); // Carte physique → scène index 2
    }

    public void ChooseMoral()
    {
        if (choiceMade) return;
        choiceMade = true;
        if (timerUI != null) timerUI.StopTimer();
        if (choiceCanvas != null) choiceCanvas.SetActive(false);
        SceneManager.LoadScene(3); // Carte morale → DiamondGame scène index 3
    }
}