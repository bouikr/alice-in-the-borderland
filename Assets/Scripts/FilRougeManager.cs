using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class FilRougeManager : MonoBehaviour
{
    [Header("UI — Panneau principal")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI progressionText;
    public Image progressionBar;
    public TextMeshProUGUI situationText;
    public TextMeshProUGUI messageText;

    [Header("UI — Choix")]
    public TextMeshProUGUI titreChoix1;
    public TextMeshProUGUI descChoix1;
    public TextMeshProUGUI titreChoix2;
    public TextMeshProUGUI descChoix2;
    public Button boutonChoix1;
    public Button boutonChoix2;

    [Header("Lumieres")]
    public Light lumiereCentrale;
    public Light lumiereRouge;
    public Light lumiereVerte;

    [Header("Portes")]
    public GameObject porteRouge;
    public GameObject porteVerte;

    [Header("Parametres")]
    public float tempsLimite = 60f;
    public int totalEpreuves = 3;

    private float tempsRestant;
    private int epreuvesReussies = 0;
    private bool jeuActif = true;
    private int bonChoix;
    private int epreuveActuelle = 0;

    private string[] situations = {
        "Tu es dans une piece mysterieuse.\nDeux portes s'offrent a toi.\nUn seul choix te menera a la prochaine epreuve.\n<color=#cc2222>Choisis judicieusement.</color>",
        "La piece se remplit de fumee.\nTu entends des bruits derriere les murs.\nUne seule porte mene a la survie.\n<color=#cc2222>Fais confiance a ton instinct.</color>",
        "C'est la derniere epreuve.\nTon allie t'attend de l'autre cote.\nUne porte vous sauvera tous les deux.\n<color=#cc2222>Ne te trompe pas.</color>"
    };

    private string[] titresC1 = {
        "Entrer maintenant",
        "Prendre le risque",
        "Foncer sans reflechir"
    };

    private string[] descsC1 = {
        "Plus rapide, mais risque.\n30% de chance d'echec.",
        "Danger inconnu derriere.\nMais tu gagnes du temps.",
        "Instinct pur.\n50/50 de survie."
    };

    private string[] titresC2 = {
        "Attendre encore",
        "Observer d'abord",
        "Analyser les indices"
    };

    private string[] descsC2 = {
        "Plus sur, mais le temps passe.\n+30 secondes, 90% de succes.",
        "Plus lent, mais maitrise.\nTu perds 15 secondes.",
        "Tu remarques une marque sur la porte verte.\nC'est un signe."
    };

    private int[] bonsChoix = { 2, 1, 2 };

    void Start()
    {
        tempsRestant = tempsLimite;
        boutonChoix1.onClick.AddListener(() => FaireChoix(1));
        boutonChoix2.onClick.AddListener(() => FaireChoix(2));
        ChargerEpreuve();
        UpdateUI();
    }

    void ChargerEpreuve()
    {
        if (epreuveActuelle >= situations.Length) return;

        situationText.text = situations[epreuveActuelle];
        titreChoix1.text = titresC1[epreuveActuelle];
        descChoix1.text = descsC1[epreuveActuelle];
        titreChoix2.text = titresC2[epreuveActuelle];
        descChoix2.text = descsC2[epreuveActuelle];
        bonChoix = bonsChoix[epreuveActuelle];

        boutonChoix1.interactable = true;
        boutonChoix2.interactable = true;
        messageText.text = "";
    }

    public void FaireChoix(int choix)
    {
        if (!jeuActif) return;

        boutonChoix1.interactable = false;
        boutonChoix2.interactable = false;

        if (choix == bonChoix)
            StartCoroutine(BonneReponse());
        else
            StartCoroutine(MauvaiseReponse());
    }

    IEnumerator BonneReponse()
    {
        epreuvesReussies++;
        messageText.text = "Bonne decision !";
        messageText.color = Color.green;

        if (lumiereCentrale != null) lumiereCentrale.color = Color.green;
        if (lumiereVerte != null) lumiereVerte.intensity = 3f;

        yield return new WaitForSeconds(1.5f);

        if (epreuvesReussies >= totalEpreuves)
        {
            StartCoroutine(Victoire());
        }
        else
        {
            epreuveActuelle++;
            if (lumiereCentrale != null) lumiereCentrale.color = Color.blue;
            if (lumiereVerte != null) lumiereVerte.intensity = 0.5f;
            ChargerEpreuve();
            UpdateUI();
        }
    }

    IEnumerator MauvaiseReponse()
    {
        messageText.text = "Mauvais choix... Tu perds du temps.";
        messageText.color = Color.red;

        if (lumiereCentrale != null) lumiereCentrale.color = Color.red;
        if (lumiereRouge != null) lumiereRouge.intensity = 3f;

        tempsRestant -= 15f;

        yield return new WaitForSeconds(1.5f);

        if (lumiereCentrale != null) lumiereCentrale.color = Color.blue;
        if (lumiereRouge != null) lumiereRouge.intensity = 0.5f;

        boutonChoix1.interactable = true;
        boutonChoix2.interactable = true;
        messageText.text = "";
    }

    IEnumerator Victoire()
    {
        jeuActif = false;
        messageText.text = "Tu as survecu ! La sortie est ouverte...";
        messageText.color = Color.green;

        if (lumiereCentrale != null) lumiereCentrale.color = Color.green;
        if (porteVerte != null) porteVerte.SetActive(false);

        yield return new WaitForSeconds(3f);
        SceneManager.LoadScene("demo_city_night");
    }

    IEnumerator Defaite()
    {
        jeuActif = false;
        messageText.text = "Le temps est ecoule... Tu n'as pas survecu.";
        messageText.color = Color.red;

        if (lumiereCentrale != null) lumiereCentrale.color = Color.black;

        yield return new WaitForSeconds(3f);
        SceneManager.LoadScene("demo_city_night");
    }

    void Update()
    {
        if (!jeuActif) return;

        tempsRestant -= Time.deltaTime;

        float t = 1 - (tempsRestant / tempsLimite);
        if (lumiereCentrale != null)
            lumiereCentrale.color = Color.Lerp(Color.blue, Color.red, t);

        if (tempsRestant <= 0)
        {
            tempsRestant = 0;
            StartCoroutine(Defaite());
        }

        UpdateUI();
    }

    void UpdateUI()
    {
        int mins = Mathf.FloorToInt(tempsRestant / 60);
        int secs = Mathf.FloorToInt(tempsRestant % 60);
        timerText.text = string.Format("{0:00}:{1:00}", mins, secs);
        timerText.color = tempsRestant <= 15 ? Color.red : Color.white;

        progressionText.text = epreuvesReussies + " / " + totalEpreuves + " EPREUVES REUSSIES";

        if (progressionBar != null)
            progressionBar.fillAmount = (float)epreuvesReussies / totalEpreuves;
    }
}