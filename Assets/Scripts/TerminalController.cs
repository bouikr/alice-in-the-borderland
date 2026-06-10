using UnityEngine;
using TMPro;

public class TerminalController : MonoBehaviour
{
    [Header("UI")]
    public Canvas          terminalCanvas;
    public TextMeshProUGUI codeDisplayText;
    public TextMeshProUGUI inputText;
    public TextMeshProUGUI feedbackText;

    private string generatedCode    = "";
    private string playerInput      = "";
    private bool   isActive         = false;
    private bool   ignoreFirstInput = false;
    private bool   playerInZone     = false;

    void Start()
    {
        GenerateCode();
        if (terminalCanvas  != null) terminalCanvas.enabled = false;
        if (codeDisplayText != null) codeDisplayText.gameObject.SetActive(false);

        // Crée automatiquement la zone trigger autour du terminal
        SphereCollider zone = gameObject.AddComponent<SphereCollider>();
        zone.isTrigger = true;
        zone.radius    = 2f; // distance d'interaction
        Debug.Log($"[Terminal] {gameObject.name} initialisé — code : {generatedCode}");
    }

    void GenerateCode()
    {
        string[] letters = { "A","B","C","D","E","F","G","H","I","J","K","L","M",
                             "N","O","P","Q","R","S","T","U","V","W","X","Y","Z" };
        generatedCode = $"{letters[Random.Range(0,26)]}-{Random.Range(0,10)}-" +
                        $"{letters[Random.Range(0,26)]}-{Random.Range(0,10)}";
    }

    // Détection automatique quand le joueur entre/sort de la zone
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            Debug.Log($"[Terminal] Joueur proche de {gameObject.name} — appuyez E");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            if (isActive) Deactivate();
        }
    }

    void Update()
    {
        // E pour ouvrir le terminal si le joueur est dans la zone
        if (playerInZone && !isActive && !PlayerController.InputBlocked)
        {
            if (Input.GetKeyDown(KeyCode.E))
                ActivateTerminal();
        }
    }

    void ActivateTerminal()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isPlaying) return;

        isActive              = true;
        ignoreFirstInput      = true;
        PlayerController.InputBlocked = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        if (terminalCanvas  != null) terminalCanvas.enabled = true;
        if (codeDisplayText != null)
        {
            codeDisplayText.gameObject.SetActive(true);
            codeDisplayText.text = $"CODE: {generatedCode}";
        }

        playerInput = "";
        if (inputText    != null) inputText.text    = "";
        if (feedbackText != null) feedbackText.text = "";

        Debug.Log($"[Terminal] {gameObject.name} activé — attendu : {generatedCode}");
    }

    void OnGUI()
    {
        if (!isActive) return;

        if (ignoreFirstInput) { ignoreFirstInput = false; return; }

        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        if (e.keyCode == KeyCode.Escape) { Deactivate(); return; }

        if (e.keyCode == KeyCode.Backspace)
        {
            if (playerInput.Length > 0)
            {
                playerInput = playerInput[..^1];
                UpdateDisplay();
            }
            return;
        }

        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            Validate(); return;
        }

        if (e.character != 0)
        {
            char c = char.ToUpper(e.character);
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                playerInput += c;
                UpdateDisplay();
                if (playerInput.Length >= generatedCode.Length)
                    Validate();
            }
        }
    }

    void UpdateDisplay()
    {
        if (inputText != null) inputText.text = playerInput;
    }

    void Validate()
    {
        if (playerInput == generatedCode)
        {
            SetFeedback("VALIDE !", Color.green);
            GameManager.Instance?.OnCodeSuccess();
            Invoke(nameof(Deactivate), 1.5f);
        }
        else
        {
            SetFeedback($"ERREUR — attendu : {generatedCode}", Color.red);
            GameManager.Instance?.OnCodeError();
            playerInput = "";
            Invoke(nameof(ResetInput), 1.5f);
        }
    }

    void ResetInput()
    {
        playerInput = "";
        if (inputText    != null) inputText.text    = "";
        if (feedbackText != null) feedbackText.text = "";
    }

    void SetFeedback(string msg, Color color)
    {
        if (feedbackText != null)
        {
            feedbackText.text  = msg;
            feedbackText.color = color;
        }
    }

    void Deactivate()
    {
        isActive              = false;
        PlayerController.InputBlocked = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (terminalCanvas  != null) terminalCanvas.enabled = false;
        if (codeDisplayText != null) codeDisplayText.gameObject.SetActive(false);
        if (feedbackText    != null) feedbackText.text = "";
        if (inputText       != null) inputText.text    = "";
    }
}