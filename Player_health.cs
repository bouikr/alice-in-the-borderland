using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHealth : MonoBehaviour
{
    [Header("Sante")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float regenSpeed = 5f; // regeneration par seconde

    [Header("UI")]
    public Image healthBar;        // Image type Filled
    public Image healthBarBG;      // fond du health bar
    public TextMeshProUGUI healthText; // texte optionnel

    [Header("Couleurs")]
    public Color colorGreen = new Color(0.2f, 0.8f, 0.2f);
    public Color colorOrange = new Color(1f, 0.5f, 0f);
    public Color colorRed = new Color(0.9f, 0.1f, 0.1f);

    [Header("Son")]
    public AudioClip crySound;     // son de cri
    public AudioSource audioSource;

    private survive gameManager;

    void Start()
    {
        gameManager = FindFirstObjectByType<survive>();
        currentHealth = maxHealth;
        UpdateHealthBar();
    }

    void Update()
    {
        // Regeneration automatique
        if (currentHealth < maxHealth)
        {
            currentHealth += regenSpeed * Time.deltaTime;
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            UpdateHealthBar();
        }
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0f);

        // Jouer le son de cri
        if (audioSource != null && crySound != null)
            audioSource.PlayOneShot(crySound);

        UpdateHealthBar();

        // Game Over si sante = 0
        if (currentHealth <= 0f)
        {
            if (gameManager != null)
                gameManager.PlayerCaught();
        }
    }

    void UpdateHealthBar()
    {
        if (healthBar == null) return;

        float ratio = currentHealth / maxHealth;
        healthBar.fillAmount = ratio;

        // Changer couleur selon sante
        if (ratio > 0.5f)
            healthBar.color = Color.Lerp(colorOrange, colorGreen, (ratio - 0.5f) * 2f);
        else
            healthBar.color = Color.Lerp(colorRed, colorOrange, ratio * 2f);

        // Texte optionnel
        if (healthText != null)
            healthText.text = Mathf.CeilToInt(currentHealth) + " / " + maxHealth;
    }
}