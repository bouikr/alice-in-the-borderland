using UnityEngine;
using UnityEngine.UI;

public class HeartsUI : MonoBehaviour
{
    public Image heart1;
    public Image heart2;

    void Update()
    {
        if (GlobalGameManager.Instance == null)
            return;

        int lives = GlobalGameManager.Instance.lives;

        heart1.enabled = lives >= 1;
        heart2.enabled = lives >= 2;
    }
}