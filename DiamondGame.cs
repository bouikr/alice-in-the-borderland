using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class DiamondGame : MonoBehaviour
{
    // ── Static API ────────────────────────────────────────────────────────────
    public static int  ForcedDifficulty = 1;
    public static bool GameCompleted;
    public static bool GameFailed;
    public static int  FinalScore;
    public static bool IsActive;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const int   ReferenceWidth   = 1920;
    private const int   ReferenceHeight  = 1080;
    private const int   CodeLength       = 5;
    private const int   DigitCount       = 10;
    private const int   MaxHistory       = 10;
    private const int   BgCardCount      = 18;
    private const int   ParticlePoolSize = 120;
    private const int   HexGridCols      = 24;
    private const int   HexGridRows      = 14;
    private const int   DataStreamCols   = 28;
    private const int   DataStreamRows   = 26;
    private const int   ArcCount         = 8;
    private const int   RainCount        = 80;
    private const float AutosaveSeconds  = 60f;
    private const string PrefPrefix      = "DiamondGame.";

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color Bg          = Hex("#020001");
    private static readonly Color Primary     = Hex("#D90429");
    private static readonly Color Secondary   = Hex("#EF233C");
    private static readonly Color DiamondRed  = Hex("#FF3333");
    private static readonly Color SafeGreen   = Hex("#00FF88");
    private static readonly Color Warning     = Hex("#F59E0B");
    private static readonly Color TextWhite   = Hex("#F0F4FF");
    private static readonly Color Panel       = Hex("#0C0003").WithA(0.97f);
    private static readonly Color Dark        = Hex("#060002");
    private static readonly Color Dim         = Hex("#7A4A52");
    private static readonly Color BlackMark   = Hex("#0E080E");
    private static readonly Color Pink        = Hex("#FF80AA");
    private static readonly Color ColdWhite   = Hex("#C8DCF0");
    private static readonly Color GlassRed    = Hex("#1E0008").WithA(0.72f);
    private static readonly Color NeonCyan    = Hex("#00FFFF");
    private static readonly Color NeonGold    = Hex("#FFD700");
    private static readonly Color DeepBlood   = Hex("#5C0010");

    // ── Enums ─────────────────────────────────────────────────────────────────
    private enum GameScreen { Boot, Intro, Playing, Pause, Win, Lose, Eliminated, Stats, Settings }
    private enum GameMode   { Beginner, Challenger, DeathGame }
    private enum GameState  { Boot, CardReveal, Rules, Playing, Paused, Win, Lose, Eliminated, Stats }
    private enum Mark       { Void, Signal, Locked }

    // ── Inner types ───────────────────────────────────────────────────────────
    [Serializable] private sealed class HistoryEntry
    {
        public string date, difficulty, result;
        public int score, attempts, hints;
        public float time;
    }
    [Serializable] private sealed class HistorySave { public List<HistoryEntry> entries = new(); }

    private sealed class DifficultyConfig
    {
        public GameMode mode;
        public string   title;
        public int      seconds, attempts, lives;
        public float    multiplier;
        public Color    color;
    }

    private sealed class DigitBox
    {
        public RectTransform       root;
        public Image               image;
        public Image               glowRing;
        public Image               scanShimmer;
        public Image               topBevel;          // NEW: glass bevel top edge
        public Image               specularStripe;    // NEW: diagonal specular
        public TextMeshProUGUI     label;
        public TextMeshProUGUI     mark;
        public TextMeshProUGUI     cursor;
    }

    private sealed class AttemptRow
    {
        public RectTransform             root;
        public TextMeshProUGUI           index;
        public readonly List<DigitBox>   boxes = new();
    }

    private sealed class StateMachine
    {
        private readonly DiamondGame owner;
        public GameState Current { get; private set; }
        public StateMachine(DiamondGame o) { owner = o; Current = GameState.Boot; }
        public void Transition(GameState next) { Current = next; owner.RefreshHud(); }
    }

    private sealed class ObjectPool<T> where T : Component
    {
        private readonly Stack<T>  inactive = new();
        private readonly Func<T>   factory;
        public ObjectPool(Func<T> factory, int warmCount)
        {
            this.factory = factory;
            for (int i = 0; i < warmCount; i++) { T item = factory(); item.gameObject.SetActive(false); inactive.Push(item); }
        }
        public T Get()    { T item = inactive.Count > 0 ? inactive.Pop() : factory(); item.gameObject.SetActive(true); return item; }
        public void Release(T item) { if (item == null) return; item.gameObject.SetActive(false); inactive.Push(item); }
    }

    // ── Component References ──────────────────────────────────────────────────
    private Canvas        canvas;
    private RectTransform root, safe, backgroundLayer, screenLayer, fxLayer, modalLayer, postFxLayer;
    private RectTransform farPlane, midPlane;         // NEW: z-plane groups
    private CanvasGroup   farPlaneGroup, midPlaneGroup;
    private CanvasGroup   screenFade;
    [Header("Optional Audio")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioClip digitReveal;
    [SerializeField] private AudioClip correctSound;
    [SerializeField] private AudioClip winJingle;
    [SerializeField] private AudioClip loseSound;
    [SerializeField] private AudioClip wrongSound;
    [SerializeField] private AudioClip uiClickSound;
    [SerializeField] private AudioClip ambientMusic;
    private StateMachine  stateMachine;
    private ObjectPool<Image> particlePool;
    private Sprite        circleSprite, squareSprite, diamondSprite;

    // ── Screen registry ───────────────────────────────────────────────────────
    private readonly Dictionary<GameScreen, RectTransform> screens = new();

    // ── Game State ────────────────────────────────────────────────────────────
    private readonly List<int>              secret      = new();
    private readonly List<int>              input       = new();
    private readonly List<DigitBox>         inputBoxes  = new();
    private readonly List<AttemptRow>       attemptRows = new();
    private readonly Dictionary<int, Button>  digitButtons      = new();
    private readonly Dictionary<int, Image>   digitButtonImages = new();
    private readonly Dictionary<int, Mark>    knownDigits       = new();
    private readonly Dictionary<int, int>     digitPressCounts  = new(); // NEW: wear tracking
    private readonly List<string>             intelLog          = new();
    private readonly List<HistoryEntry>       history           = new();

    // ── Background Visual Lists ───────────────────────────────────────────────
    private readonly List<Image>              noisePixels        = new();
    private readonly List<RectTransform>      fallingCards       = new();
    private readonly List<RectTransform>      lightBeams         = new();
    private readonly List<RectTransform>      atmosphereMotes    = new();
    private readonly List<RectTransform>      glitchBars         = new();
    private readonly List<RectTransform>      fogBanks           = new();
    private readonly List<TextMeshProUGUI>    surveillanceLabels = new();
    private readonly List<TextMeshProUGUI>    coordinateLabels   = new();
    private readonly List<TextMeshProUGUI>    ghostDiamonds      = new();
    private readonly List<RectTransform>      suitRibbons        = new();
    private readonly List<RectTransform>      hexCells           = new();
    private readonly List<TextMeshProUGUI>    dataStreamCells    = new();
    private readonly List<RectTransform>      arcTendrils        = new();
    private readonly List<RectTransform>      windowLights       = new();
    private readonly List<RectTransform>      radarBlips         = new();
    private readonly List<Image>              flickerBorders     = new();
    private readonly List<TextMeshProUGUI>    scalableText       = new();
    private readonly List<TextMeshProUGUI>    neonSigns          = new();
    private readonly List<RectTransform>      rainDrops          = new();  // NEW
    private readonly List<RectTransform>      reflectionPuddles  = new();  // NEW
    private readonly List<Image>              hexCellImages      = new();  // NEW: for hue shifts
    private readonly List<Image>              fogImages          = new();   // NEW: for hue shifts

    // ── Post-FX references ────────────────────────────────────────────────────
    private Image vignetteInner, vignetteOuter, distortionWash;
    private Image chromaR, chromaG, chromaB;
    private Image filmGrainOverlay;
    private Image scanlineOverlay;
    private Image letterboxTop, letterboxBottom;
    private RectTransform radarArm;

    // ── HUD references ────────────────────────────────────────────────────────
    private Image              timerRing, timerCorona;
    private Image              pressureFill;
    private TextMeshProUGUI    timerText, livesText, scoreText, attemptCounterText;
    private TextMeshProUGUI    statusText, warningText, dossierText, intelLogText, usedDigitsText;
    private Button             confirmButton;
    private Image              statusUnderline;          // NEW: sweep underline
    private RectTransform      spectatorLabel;           // NEW

    // ── Runtime State ─────────────────────────────────────────────────────────
    private DifficultyConfig selected;
    private GameScreen        currentScreen;
    private System.Random     rng                 = new();
    private Coroutine         timerRoutine, statusRoutine, cursorRoutine;
    private float             timeLeft, nextAutosave, nextHeartbeat;
    private float             nextSurveillancePulse, nextCoordinatePulse;
    private float             nextArcSpawn, nextSpectatorUpdate;
    private float             backgroundSpeed     = 1f;
    private float             lastScoreTweenValue;
    private float             chromaIntensity     = 0f;
    private float             grainIntensity      = 0.3f;
    private float             tensionFloat        = 0f;
    private int               currentAttempt, lives, score, shownScore;
    private int               lastBaseScore, lastTimeBonus, lastFinalScore, lastTensionLevel;
    private int               hintPenalty, hintsUsedThisGame;
    private int               bestScore, gamesPlayed, gamesWon, gamesLost, totalHintsUsed, survivalStreak;
    private bool              acceptingInput;
    private bool              soundEnabled = true, musicEnabled = true, shakeEnabled = true;
    private bool              reducedMotion, colorblind, largeText;
    private bool              whisperUsed, visionUsed, revelationUsed;
    private bool              attemptCounterShaken = false;  // NEW
    private int               lastTickSecond       = -1;     // NEW: for discrete tick
    private string            systemLine = "> SYSTEM: INITIALIZING...";
    private int               spectatorCount       = 847;    // NEW

    // ─────────────────────────────────────────────────────────────────────────
    // LAUNCH API
    // ─────────────────────────────────────────────────────────────────────────
    public static void LaunchDiamondGame()
    {
        GameCompleted = false; GameFailed = false; FinalScore = 0; IsActive = true;
        DiamondGame existing = FindFirstObjectByType<DiamondGame>();
        if (existing != null) { existing.gameObject.SetActive(true); existing.RestartToIntro(); return; }
        DontDestroyOnLoad(new GameObject("DIAMOND GAME Runtime").AddComponent<DiamondGame>().gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MONOBEHAVIOUR
    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        DOTween.Init(false, true, LogBehaviour.ErrorsOnly);
        Application.targetFrameRate = 60;
        IsActive = true;
        stateMachine = new StateMachine(this);
        ApplyForcedDifficulty();
        LoadData();
        EnsureEventSystem();
        EnsureAudio();
        BuildSprites();
        BuildCanvas();
        BuildBackground();
        BuildPostFxLayer();
        WarmPool();
        BuildScreens();
        Show(GameScreen.Boot, false);
    }

    private void Start() => StartCoroutine(BootSequence());

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        AnimateBackground(dt);
        AnimateNoise(dt);
        AnimatePostFx(dt);
        AnimateDataStream(dt);
        AnimateRadar(dt);
        AnimateHexGrid(dt);
        AnimateArcTendrils(dt);
        AnimateWindowLights(dt);
        AnimateLetterbox(dt);
        AnimateRain(dt);            // NEW
        AnimateReflections(dt);     // NEW
        AnimateSpectator(dt);       // NEW
        HandleKeyboard();
        UpdateMusicPulse();
        AnimateCursorBlinks(dt);

        if (currentScreen == GameScreen.Playing && acceptingInput)
        {
            UpdateDangerFeedback();
            UpdateTensionFloat(dt);
            if (Time.unscaledTime >= nextAutosave) { SaveData(); nextAutosave = Time.unscaledTime + AutosaveSeconds; }
        }
    }

    private void OnApplicationPause(bool p) { if (p) SaveData(); }
    private void OnDisable()  => SaveData();
    private void OnDestroy()  { DOTween.Kill(this); IsActive = false; }

    // ─────────────────────────────────────────────────────────────────────────
    // SETUP
    // ─────────────────────────────────────────────────────────────────────────
    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            DontDestroyOnLoad(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)));
    }

    private void EnsureAudio()
    {
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false; sfxSource.spatialBlend = 0f;
        if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true; musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f; musicSource.volume = 0.08f;
        if (ambientMusic != null) { musicSource.clip = ambientMusic; if (musicEnabled && !musicSource.isPlaying) musicSource.Play(); }
    }

    private void BuildSprites()
    {
        circleSprite  = CreateCircleSprite(128);
        squareSprite  = CreateSquareSprite(32);
        diamondSprite = CreateDiamondSprite(64);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CANVAS + LAYER STACK
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildCanvas()
    {
        GameObject go = new("DIAMOND GAME Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        

        root = go.GetComponent<RectTransform>();
        root.Fill();
        MakeImage("Background", root, Bg).rectTransform.Fill();

        backgroundLayer = Rect("Background Effects", root); backgroundLayer.Fill();

        // NEW: Z-plane sub-groups
        farPlane  = Rect("Far Plane",  backgroundLayer); farPlane.Fill();
        midPlane  = Rect("Mid Plane",  backgroundLayer); midPlane.Fill();
        farPlaneGroup  = farPlane.gameObject.AddComponent<CanvasGroup>();  farPlaneGroup.alpha  = 0.55f;
        midPlaneGroup  = midPlane.gameObject.AddComponent<CanvasGroup>(); midPlaneGroup.alpha = 0.80f;

        screenLayer     = Rect("Screens",            root); screenLayer.Fill();
        fxLayer         = Rect("FX",                 root); fxLayer.Fill();
        postFxLayer     = Rect("PostFX",             root); postFxLayer.Fill();
        modalLayer      = Rect("Modals",             root); modalLayer.Fill();

        safe = Rect("Safe Area", screenLayer);
        safe.Fill();
        safe.offsetMin = new Vector2(34, 28);
        safe.offsetMax = new Vector2(-34, -28);

        screenFade = screenLayer.gameObject.AddComponent<CanvasGroup>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BACKGROUND — ALL LAYERS (using Z-planes)
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildBackground()
    {
        // FAR PLANE (depth 0.55 alpha)
        BuildHexGrid();
        BuildFogLayer();
        BuildDataStream();
        BuildRainLayer();             // NEW
        BuildWetFloorReflection();    // NEW

        // MID PLANE (depth 0.80 alpha)
        BuildTokyoSilhouetteV3();
        BuildRedLightBeams();
        BuildRadarSystem();
        BuildScanlines();
        BuildSuitRibbons();
        BuildGhostDiamonds();
        BuildSurveillanceOverlay();
        BuildCoordinateOverlay();
        BuildGlitchBars();
        BuildAtmosphereMotes();
        BuildNeonSigns();
        BuildFallingCards();
        BuildSpectatorRim();          // NEW
        BuildCrtCorners();
    }

    // ── Hex Grid (far plane) ──────────────────────────────────────────────────
    private void BuildHexGrid()
    {
        RectTransform layer = Rect("Hex Grid", farPlane); layer.Fill();
        float hexW = ReferenceWidth  / (float)HexGridCols;
        float hexH = ReferenceHeight / (float)HexGridRows;
        for (int row = 0; row < HexGridRows; row++)
        for (int col = 0; col < HexGridCols; col++)
        {
            float xOff  = (row % 2 == 0) ? 0f : hexW * 0.5f;
            float alpha = UnityEngine.Random.Range(0.012f, 0.045f);
            Image cell  = MakeImage("Hex", layer, Primary.WithA(alpha));
            cell.sprite = diamondSprite;
            RectTransform rt = cell.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(col * hexW + xOff, row * hexH);
            rt.sizeDelta = new Vector2(hexW * 0.88f, hexH * 0.88f);
            hexCells.Add(rt);
            hexCellImages.Add(cell);
        }
    }

    // ── Matrix Data Stream (far plane) ────────────────────────────────────────
    private void BuildDataStream()
    {
        RectTransform layer = Rect("Data Stream", farPlane); layer.Fill();
        float colW = ReferenceWidth / (float)DataStreamCols;
        string glyphs = "0123456789ABCDEFX♦♠♣♥⬡";
        for (int col = 0; col < DataStreamCols; col++)
        for (int row = 0; row < DataStreamRows; row++)
        {
            float fadeT = row / (float)DataStreamRows;
            float alpha = Mathf.Lerp(0.25f, 0.02f, fadeT) * UnityEngine.Random.Range(0.4f, 1f);
            Color c     = col % 7 == 0 ? TextWhite.WithA(alpha) : Primary.WithA(alpha * 0.7f);
            string glyph = glyphs[UnityEngine.Random.Range(0, glyphs.Length)].ToString();
            TextMeshProUGUI label = TMP("DS", layer, glyph, 18, c, TextAlignmentOptions.Center);
            label.characterSpacing = -5f;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(col * colW + colW * 0.5f, -row * (ReferenceHeight / (float)DataStreamRows));
            rt.sizeDelta = new Vector2(colW, 28f);
            dataStreamCells.Add(label);
        }
    }

    // ── NEW: Rain Layer (far plane) ───────────────────────────────────────────
    private void BuildRainLayer()
    {
        RectTransform layer = Rect("Rain", farPlane); layer.Fill();
        for (int i = 0; i < RainCount; i++)
        {
            float alpha  = UnityEngine.Random.Range(0.04f, 0.16f);
            float height = UnityEngine.Random.Range(18f, 48f);
            Image drop   = MakeImage("Rain Drop", layer, ColdWhite.WithA(alpha));
            RectTransform rt = drop.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.Range(0f, 1.3f));
            rt.sizeDelta  = new Vector2(UnityEngine.Random.Range(1f, 2f), height);
            rt.pivot      = new Vector2(0.5f, 1f);
            rainDrops.Add(rt);
        }
    }

    // ── NEW: Wet Floor Reflection (far plane) ─────────────────────────────────
    private void BuildWetFloorReflection()
    {
        RectTransform layer = Rect("Wet Floor", farPlane); layer.Fill();

        // Mirrored city silhouette base
        Image refl = MakeImage("Reflection Base", layer, Primary.WithA(0.06f));
        refl.rectTransform.anchorMin = Vector2.zero;
        refl.rectTransform.anchorMax = new Vector2(1f, 0.22f);
        refl.rectTransform.offsetMin = refl.rectTransform.offsetMax = Vector2.zero;
        fogImages.Add(refl);

        // Reflection highlight band
        Image band = MakeImage("Reflection Band", layer, TextWhite.WithA(0.025f));
        RectTransform brt = band.rectTransform;
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot     = new Vector2(0.5f, 0f);
        brt.sizeDelta = new Vector2(0f, 3f);

        // Animated puddle ripple circles
        for (int i = 0; i < 8; i++)
        {
            Image ripple = MakeImage("Puddle Ripple", layer, TextWhite.WithA(0f));
            ripple.sprite = circleSprite;
            RectTransform rrt = ripple.rectTransform;
            float x = UnityEngine.Random.Range(0.08f, 0.92f);
            rrt.anchorMin = rrt.anchorMax = new Vector2(x, UnityEngine.Random.Range(0.02f, 0.12f));
            rrt.sizeDelta = new Vector2(12f, 8f);
            reflectionPuddles.Add(rrt);
        }
        StartCoroutine(AnimatePuddleRipples());
    }

    private IEnumerator AnimatePuddleRipples()
    {
        while (true)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(1.2f, 3.8f));
            if (reflectionPuddles.Count == 0) continue;
            RectTransform rt  = reflectionPuddles[UnityEngine.Random.Range(0, reflectionPuddles.Count)];
            Image img = rt.GetComponent<Image>();
            if (img == null) continue;
            rt.sizeDelta = new Vector2(12f, 8f);
            img.color    = TextWhite.WithA(0.22f);
            rt.DOSizeDelta(new Vector2(80f, 30f), 0.9f).SetEase(Ease.OutCubic).SetId(this);
            img.DOFade(0f, 0.9f).SetId(this);
        }
    }

    // ── Fog Layer (far plane) ─────────────────────────────────────────────────
    private void BuildFogLayer()
    {
        RectTransform layer = Rect("Cinematic Fog", farPlane); layer.Fill();
        for (int i = 0; i < 12; i++)
        {
            Image fog = MakeImage("Fog Bank", layer, (i % 2 == 0 ? Primary : TextWhite).WithA(UnityEngine.Random.Range(0.014f, 0.038f)));
            fog.sprite = circleSprite;
            RectTransform rt = fog.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.Range(0.05f, 0.88f));
            rt.sizeDelta  = new Vector2(UnityEngine.Random.Range(480f, 1100f), UnityEngine.Random.Range(100f, 280f));
            fogBanks.Add(rt);
            fogImages.Add(fog);
        }
    }

    // ── Radar System (mid plane) ──────────────────────────────────────────────
    private void BuildRadarSystem()
    {
        RectTransform layer = Rect("Radar", midPlane); layer.Fill();
        float cx = ReferenceWidth * 0.87f, cy = ReferenceHeight * 0.72f, radius = 180f;
        for (int i = 3; i >= 1; i--)
        {
            Image ring = MakeImage("Radar Ring", layer, Primary.WithA(i == 3 ? 0.12f : 0.07f));
            ring.sprite = circleSprite;
            ring.type   = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillAmount = 1f;
            RectTransform rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(cx / ReferenceWidth, cy / ReferenceHeight);
            rt.sizeDelta = Vector2.one * (radius * 2f / i);
        }
        for (int i = 0; i < 2; i++)
        {
            RectTransform line = MakeImage("Radar Cross", layer, Primary.WithA(0.1f)).rectTransform;
            line.anchorMin = line.anchorMax = new Vector2(cx / ReferenceWidth, cy / ReferenceHeight);
            line.sizeDelta = i == 0 ? new Vector2(radius * 2f + 10f, 1.5f) : new Vector2(1.5f, radius * 2f + 10f);
        }
        Image arm = MakeImage("Radar Arm", layer, Primary.WithA(0.5f));
        arm.sprite = squareSprite;
        radarArm   = arm.rectTransform;
        radarArm.anchorMin = radarArm.anchorMax = new Vector2(cx / ReferenceWidth, cy / ReferenceHeight);
        radarArm.pivot     = new Vector2(0f, 0.5f);
        radarArm.sizeDelta = new Vector2(radius, 2f);
        for (int t = 1; t <= 6; t++)
        {
            Image trail = MakeImage("Radar Trail", layer, Primary.WithA(0.04f * (7 - t)));
            RectTransform trt = trail.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(cx / ReferenceWidth, cy / ReferenceHeight);
            trt.pivot      = new Vector2(0f, 0.5f);
            trt.sizeDelta  = new Vector2(radius * (1f - t * 0.05f), 2f + t);
        }
        for (int b = 0; b < 6; b++)
        {
            float angle = UnityEngine.Random.Range(0f, 360f);
            float dist  = UnityEngine.Random.Range(40f, radius - 20f);
            float bx    = cx + Mathf.Cos(angle * Mathf.Deg2Rad) * dist;
            float by    = cy + Mathf.Sin(angle * Mathf.Deg2Rad) * dist;
            Image blip  = MakeImage("Radar Blip", layer, SafeGreen.WithA(0.6f));
            blip.sprite  = circleSprite;
            RectTransform rt = blip.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(bx / ReferenceWidth, by / ReferenceHeight);
            rt.sizeDelta = new Vector2(10f, 10f);
            radarBlips.Add(rt);
            rt.DOScale(1.5f, UnityEngine.Random.Range(0.6f, 1.4f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
            blip.DOFade(0.2f, UnityEngine.Random.Range(0.4f, 1.1f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
        }
    }

    // ── Scanlines (mid plane) ─────────────────────────────────────────────────
    private void BuildScanlines()
    {
        RectTransform layer = Rect("Scanlines", midPlane); layer.Fill();
        int count = 54;
        for (int i = 0; i < count; i++)
        {
            Image line = MakeImage("SL", layer, TextWhite.WithA(i % 3 == 0 ? 0.025f : 0.012f));
            RectTransform rt = line.rectTransform;
            rt.anchorMin = new Vector2(0f, (float)i / count);
            rt.anchorMax = new Vector2(1f, (float)i / count);
            rt.sizeDelta = new Vector2(0f, 1f);
        }
    }

    // ── UPGRADED: Tokyo Silhouette V3 (mid plane) ─────────────────────────────
    private void BuildTokyoSilhouetteV3()
    {
        RectTransform skyline = Rect("Abandoned Tokyo Skyline V3", midPlane);
        skyline.anchorMin = new Vector2(0f, 0f);
        skyline.anchorMax = new Vector2(1f, 0f);
        skyline.pivot     = new Vector2(0.5f, 0f);
        skyline.sizeDelta = new Vector2(0f, 280f);

        for (int i = 0; i < 40; i++)
        {
            float width  = UnityEngine.Random.Range(28f, 82f);
            float height = UnityEngine.Random.Range(60f, 260f);
            float depth  = UnityEngine.Random.Range(0f, 1f);
            Color bldColor = Color.Lerp(Color.black, DeepBlood, depth * 0.3f).WithA(UnityEngine.Random.Range(0.55f, 0.88f));
            Image building = MakeImage("Building", skyline, bldColor);
            RectTransform rt = building.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(i / 39f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-18f, 18f), 0f);

            // NEW: near-light specular top-edge on buildings
            Image bevelTop = MakeImage("Building Bevel", rt, TextWhite.WithA(0.04f));
            bevelTop.rectTransform.anchorMin = new Vector2(0f, 1f);
            bevelTop.rectTransform.anchorMax = Vector2.one;
            bevelTop.rectTransform.pivot     = new Vector2(0.5f, 1f);
            bevelTop.rectTransform.sizeDelta = new Vector2(0f, 1f);

            int winCount = UnityEngine.Random.Range(3, 12);
            for (int w = 0; w < winCount; w++)
            {
                bool neon    = UnityEngine.Random.value < 0.12f;
                bool flicker = UnityEngine.Random.value < 0.08f;
                Color wc     = neon ? Primary.WithA(UnityEngine.Random.Range(0.55f, 0.9f))
                                    : TextWhite.WithA(UnityEngine.Random.Range(0.06f, 0.22f));
                Image win = MakeImage("Window", rt, wc);
                RectTransform wrt = win.rectTransform;
                wrt.anchorMin = wrt.anchorMax = new Vector2(UnityEngine.Random.Range(0.15f, 0.85f), UnityEngine.Random.Range(0.18f, 0.88f));
                wrt.sizeDelta = new Vector2(UnityEngine.Random.Range(5f, 11f), UnityEngine.Random.Range(7f, 14f));
                windowLights.Add(wrt);
                if (flicker)
                    win.DOFade(UnityEngine.Random.Range(0.02f, 0.08f), UnityEngine.Random.Range(0.12f, 0.65f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
            }
            if (UnityEngine.Random.value < 0.25f)
            {
                Image ant = MakeImage("Antenna", rt, Primary.WithA(0.45f));
                ant.rectTransform.anchorMin = ant.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                ant.rectTransform.pivot     = new Vector2(0.5f, 0f);
                ant.rectTransform.sizeDelta = new Vector2(2f, UnityEngine.Random.Range(18f, 42f));
                Image tip = MakeImage("Ant Tip", ant.rectTransform, Primary.WithA(0.9f));
                tip.sprite = circleSprite;
                tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                tip.rectTransform.pivot      = new Vector2(0.5f, 1f);
                tip.rectTransform.sizeDelta  = new Vector2(6f, 6f);
                tip.DOFade(0.1f, UnityEngine.Random.Range(0.4f, 1.2f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
            }
        }
        Image groundGlow = MakeImage("Ground Glow", skyline, Primary.WithA(0.06f));
        groundGlow.sprite = circleSprite;
        groundGlow.rectTransform.anchorMin = groundGlow.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        groundGlow.rectTransform.sizeDelta  = new Vector2(ReferenceWidth, 80f);
        groundGlow.rectTransform.DOSizeDelta(new Vector2(ReferenceWidth * 1.1f, 90f), 2.8f).SetLoops(-1, LoopType.Yoyo).SetId(this);
        fogImages.Add(groundGlow);
    }

    // ── Red Light Beams (mid plane) ───────────────────────────────────────────
    private void BuildRedLightBeams()
    {
        RectTransform layer = Rect("Red Search Lights", midPlane); layer.Fill();
        for (int i = 0; i < 8; i++)
        {
            RectTransform beam = MakeImage("Light Beam", layer, Primary.WithA(UnityEngine.Random.Range(0.018f, 0.045f))).rectTransform;
            beam.anchorMin = beam.anchorMax = new Vector2(UnityEngine.Random.Range(0.05f, 0.95f), 0.5f);
            beam.sizeDelta  = new Vector2(UnityEngine.Random.Range(10f, 22f), ReferenceHeight * 1.8f);
            beam.anchoredPosition = new Vector2(0f, UnityEngine.Random.Range(-90f, 130f));
            beam.localRotation    = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-30f, 30f));
            lightBeams.Add(beam);
        }
    }

    // ── Suit Ribbons (mid plane) ──────────────────────────────────────────────
    private void BuildSuitRibbons()
    {
        RectTransform layer = Rect("Suit Ribbons", midPlane); layer.Fill();
        string content = "♦  ♣  ♥  ♠  ♦  ♣  ♥  ♠  ♦  ♣  ♥  ♠";
        for (int i = 0; i < 4; i++)
        {
            TextMeshProUGUI ribbon = TMP("Suit Ribbon", layer, content, 24, Primary.WithA(0.08f + i * 0.02f), TextAlignmentOptions.Center);
            ribbon.textWrappingMode = TextWrappingModes.NoWrap;
            ribbon.characterSpacing = 8f;
            RectTransform rt = ribbon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.18f + i * 0.22f);
            rt.sizeDelta  = new Vector2(1800f, 44f);
            rt.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? -4f : 4f);
            suitRibbons.Add(rt);
        }
    }

    // ── Ghost Diamonds (mid plane) ────────────────────────────────────────────
    private void BuildGhostDiamonds()
    {
        RectTransform layer = Rect("Ghost Diamonds", midPlane); layer.Fill();
        for (int i = 0; i < 9; i++)
        {
            TextMeshProUGUI d = TMP("Ghost Diamond", layer, "♦", UnityEngine.Random.Range(90, 260), Primary.WithA(0.025f), TextAlignmentOptions.Center);
            RectTransform rt  = d.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.Range(0.06f, 0.94f), UnityEngine.Random.Range(0.1f, 0.9f));
            rt.sizeDelta  = new Vector2(280f, 280f);
            rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-22f, 22f));
            d.DOFade(UnityEngine.Random.Range(0.012f, 0.065f), UnityEngine.Random.Range(1.2f, 3.0f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
            rt.DOScale(UnityEngine.Random.Range(1.06f, 1.24f), UnityEngine.Random.Range(2.0f, 4.5f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
            ghostDiamonds.Add(d);
        }
    }

    // ── Surveillance Overlay (mid plane) ─────────────────────────────────────
    private void BuildSurveillanceOverlay()
    {
        RectTransform layer = Rect("Surveillance Overlay", midPlane); layer.Fill();
        string[] labels = { "CAMERA 01", "CAMERA 02", "CAMERA 03", "TOP SECRET",
            "SUBJECT DETECTED", "PLAYER STATUS: ALIVE", "SURVIVAL RATE: 12%",
            "YOU ARE PLAYER #180", "PLAYER #177 - ELIMINATED",
            "PLAYER #178 - ELIMINATED", "PLAYER #179 - ELIMINATED" };
        for (int i = 0; i < labels.Length; i++)
        {
            bool isCamera = i < 3;
            TextMeshProUGUI label = TMP("Surv " + i, layer, labels[i], isCamera ? 16 : 19,
                (isCamera ? TextWhite : Primary).WithA(0f), TextAlignmentOptions.Left);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.characterSpacing = 4f;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.Range(0.04f, 0.75f), UnityEngine.Random.Range(0.12f, 0.92f));
            rt.sizeDelta  = new Vector2(380f, 32f);
            surveillanceLabels.Add(label);
        }
        for (int i = 0; i < 4; i++)
        {
            RectTransform ret = Rect("Camera Reticle", layer);
            ret.anchorMin = ret.anchorMax = new Vector2(i % 2 == 0 ? 0.06f : 0.94f, i < 2 ? 0.85f : 0.14f);
            ret.sizeDelta = new Vector2(88f, 66f);
            AddEdge(ret, "Top",    Primary.WithA(0.18f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f));
            AddEdge(ret, "Bottom", Primary.WithA(0.18f), Vector2.zero,        new Vector2(1f, 0f), new Vector2(0f, 2f));
            AddEdge(ret, "Left",   Primary.WithA(0.18f), Vector2.zero,        new Vector2(0f, 1f), new Vector2(2f, 0f));
            AddEdge(ret, "Right",  Primary.WithA(0.18f), new Vector2(1f, 0f), Vector2.one,         new Vector2(2f, 0f));
            ret.DOScale(1.05f, 1.2f + i * 0.15f).SetLoops(-1, LoopType.Yoyo).SetId(this);
        }
    }

    // ── Coordinate Overlay (mid plane) ────────────────────────────────────────
    private void BuildCoordinateOverlay()
    {
        RectTransform layer = Rect("Coordinate Overlay", midPlane); layer.Fill();
        for (int i = 0; i < 14; i++)
        {
            TextMeshProUGUI label = TMP("Coord " + i, layer, RandomCoordinate(), 13, TextWhite.WithA(0f), TextAlignmentOptions.Left);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.Range(0.03f, 0.84f), UnityEngine.Random.Range(0.06f, 0.94f));
            rt.sizeDelta  = new Vector2(280f, 22f);
            coordinateLabels.Add(label);
        }
    }

    // ── Glitch Bars (mid plane) ───────────────────────────────────────────────
    private void BuildGlitchBars()
    {
        RectTransform layer = Rect("Glitch Bars", midPlane); layer.Fill();
        for (int i = 0; i < 12; i++)
        {
            RectTransform bar = MakeImage("Glitch Bar", layer, Primary.WithA(0f)).rectTransform;
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, UnityEngine.Random.Range(0.1f, 0.96f));
            bar.sizeDelta = new Vector2(UnityEngine.Random.Range(120f, 820f), UnityEngine.Random.Range(2f, 11f));
            bar.anchoredPosition = new Vector2(UnityEngine.Random.Range(-640f, 640f), 0f);
            glitchBars.Add(bar);
        }
    }

    // ── Atmosphere Motes (mid plane) ──────────────────────────────────────────
    private void BuildAtmosphereMotes()
    {
        RectTransform layer = Rect("Dust And Ash", midPlane); layer.Fill();
        for (int i = 0; i < 64; i++)
        {
            Color c = i % 5 == 0 ? Pink.WithA(0.18f) : (i % 3 == 0 ? NeonGold.WithA(0.08f) : Primary.WithA(0.12f));
            Image mote = MakeImage("Mote", layer, c);
            if (i % 5 == 0) mote.sprite = diamondSprite;
            RectTransform rt = mote.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
            float sz = UnityEngine.Random.Range(2f, 8f);
            rt.sizeDelta = new Vector2(sz, sz);
            atmosphereMotes.Add(rt);
        }
    }

    // ── Neon Signs (mid plane) ────────────────────────────────────────────────
    private void BuildNeonSigns()
    {
        RectTransform layer = Rect("Neon Signs", midPlane); layer.Fill();
        (string text, Color color, Vector2 pos, float size)[] signs =
        {
            ("SURVIVE OR DIE",      Primary,              new Vector2(0.12f, 0.62f), 22f),
            ("GAME ARENA",          NeonCyan.WithA(0.65f),new Vector2(0.82f, 0.55f), 26f),
            ("PLAYER #180",         SafeGreen,            new Vector2(0.08f, 0.35f), 19f),
            ("CODE:  _ _ _ _ _",    Warning,              new Vector2(0.88f, 0.78f), 18f),
            ("ELIMINATION ZONE",    Primary,              new Vector2(0.5f,  0.88f), 20f),
        };
        foreach (var sign in signs)
        {
            TextMeshProUGUI label = TMP("Neon " + sign.text, layer, sign.text, (int)sign.size, sign.color.WithA(0.0f), TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.characterSpacing = 4f;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = sign.pos;
            rt.sizeDelta  = new Vector2(460f, 40f);
            float delay  = UnityEngine.Random.Range(2f, 9f);
            float alpha  = sign.color.a;
            DOTween.Sequence().SetId(this)
                .AppendInterval(delay)
                .Append(label.DOFade(alpha, 0.04f))
                .Append(label.DOFade(0f, 0.03f))
                .Append(label.DOFade(alpha, 0.08f))
                .Append(label.DOFade(alpha * 0.4f, 0.05f))
                .Append(label.DOFade(alpha, 0.12f))
                .SetLoops(-1, LoopType.Restart);
            neonSigns.Add(label);
        }
    }

    // ── Falling Cards (mid plane) ─────────────────────────────────────────────
    private void BuildFallingCards()
    {
        RectTransform layer = Rect("Falling Cards", midPlane); layer.Fill();
        string[] suits = { "♦", "♣", "♥", "♠" };
        for (int i = 0; i < BgCardCount; i++)
        {
            RectTransform card = PanelBox("Falling Card", layer, Dark.WithA(0.28f), Primary.WithA(0.1f));
            card.sizeDelta = new Vector2(UnityEngine.Random.Range(65f, 115f), UnityEngine.Random.Range(90f, 148f));
            card.anchorMin = card.anchorMax = new Vector2(UnityEngine.Random.value, 1f);
            card.anchoredPosition = new Vector2(0f, UnityEngine.Random.Range(-ReferenceHeight, ReferenceHeight));
            card.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-22f, 22f));
            TMP("Suit", card, suits[UnityEngine.Random.Range(0, suits.Length)], 42, Primary.WithA(0.2f), TextAlignmentOptions.Center).rectTransform.Fill();
            Image sheen = MakeImage("Card Sheen", card, TextWhite.WithA(0.04f));
            sheen.rectTransform.anchorMin = sheen.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sheen.rectTransform.sizeDelta = new Vector2(14f, 200f);
            sheen.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -25f);
            sheen.rectTransform.anchoredPosition = new Vector2(-120f, 0f);
            sheen.rectTransform.DOAnchorPosX(120f, UnityEngine.Random.Range(1.8f, 3.4f)).SetLoops(-1, LoopType.Restart).SetEase(Ease.InOutSine).SetId(this);
            fallingCards.Add(card);
        }
    }

    // ── NEW: Spectator Rim (mid plane) ────────────────────────────────────────
    private void BuildSpectatorRim()
    {
        RectTransform layer = Rect("Spectator Rim", midPlane); layer.Fill();

        // Observer silhouettes in upper corners
        string[] silhouettes = { "●", "●", "●", "●", "●", "●" };
        for (int side = 0; side < 2; side++)
        {
            for (int j = 0; j < 3; j++)
            {
                float x     = side == 0 ? 0.04f + j * 0.025f : 0.96f - j * 0.025f;
                Image sil   = MakeImage("Observer", layer, Dim.WithA(UnityEngine.Random.Range(0.08f, 0.18f)));
                sil.sprite  = circleSprite;
                RectTransform rt = sil.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(x, 0.94f);
                rt.sizeDelta  = new Vector2(UnityEngine.Random.Range(8f, 16f), UnityEngine.Random.Range(14f, 24f));
                sil.DOFade(UnityEngine.Random.Range(0.04f, 0.12f), UnityEngine.Random.Range(1.8f, 4.2f)).SetLoops(-1, LoopType.Yoyo).SetId(this);
            }
        }
    }

    // ── CRT Corners (mid plane) ───────────────────────────────────────────────
    private void BuildCrtCorners()
    {
        RectTransform layer = Rect("CRT BG", midPlane); layer.Fill();
        Color c = Color.black.WithA(0.22f);
        Vector2[] corners = { new Vector2(0f, 1f), Vector2.one, Vector2.zero, new Vector2(1f, 0f) };
        foreach (var corner in corners)
        {
            Image img = MakeImage("CRT Corner", layer, c);
            img.sprite = circleSprite;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.sizeDelta  = new Vector2(600f, 600f);
            rt.anchoredPosition = new Vector2((corner.x == 0f ? -1f : 1f) * 280f, (corner.y == 0f ? -1f : 1f) * 220f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST-FX LAYER
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildPostFxLayer()
    {
        chromaR = MakeImage("ChromaR", postFxLayer, new Color(1f, 0f, 0f, 0f));
        chromaR.rectTransform.Fill();
        chromaG = MakeImage("ChromaG", postFxLayer, new Color(0f, 1f, 0f, 0f));
        chromaG.rectTransform.Fill();
        chromaB = MakeImage("ChromaB", postFxLayer, new Color(0f, 0f, 1f, 0f));
        chromaB.rectTransform.Fill();

        filmGrainOverlay = MakeImage("Film Grain", postFxLayer, TextWhite.WithA(0f));
        filmGrainOverlay.rectTransform.Fill();

        scanlineOverlay = MakeImage("Scanline Overlay", postFxLayer, Color.black.WithA(0.08f));
        scanlineOverlay.rectTransform.Fill();

        vignetteInner = MakeImage("Vignette Inner", postFxLayer, Primary.WithA(0f));
        vignetteInner.sprite = circleSprite;
        vignetteInner.type   = Image.Type.Filled;
        vignetteInner.rectTransform.Fill();
        vignetteInner.rectTransform.offsetMin = new Vector2(-80f, -60f);
        vignetteInner.rectTransform.offsetMax = new Vector2(80f, 60f);

        vignetteOuter = MakeImage("Vignette Outer", postFxLayer, Color.black.WithA(0.35f));
        vignetteOuter.sprite = circleSprite;
        vignetteOuter.rectTransform.Fill();
        vignetteOuter.rectTransform.offsetMin = new Vector2(-40f, -30f);
        vignetteOuter.rectTransform.offsetMax = new Vector2(40f, 30f);
        vignetteOuter.color = Color.black.WithA(0.35f);

        distortionWash = MakeImage("Signal Distortion Wash", postFxLayer, TextWhite.WithA(0f));
        distortionWash.rectTransform.Fill();

        letterboxTop = MakeImage("Letterbox Top", postFxLayer, Color.black.WithA(1f));
        RectTransform lbT = letterboxTop.rectTransform;
        lbT.anchorMin = new Vector2(0f, 1f); lbT.anchorMax = Vector2.one;
        lbT.pivot = new Vector2(0.5f, 1f); lbT.sizeDelta = new Vector2(0f, 0f);

        letterboxBottom = MakeImage("Letterbox Bottom", postFxLayer, Color.black.WithA(1f));
        RectTransform lbB = letterboxBottom.rectTransform;
        lbB.anchorMin = Vector2.zero; lbB.anchorMax = new Vector2(1f, 0f);
        lbB.pivot = new Vector2(0.5f, 0f); lbB.sizeDelta = new Vector2(0f, 0f);

        foreach (Image img in new[] { chromaR, chromaG, chromaB, filmGrainOverlay,
            scanlineOverlay, vignetteInner, vignetteOuter, distortionWash,
            letterboxTop, letterboxBottom })
            img.raycastTarget = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ANIMATION LOOPS
    // ─────────────────────────────────────────────────────────────────────────

    private void AnimateBackground(float dt)
    {
        for (int i = 0; i < fogBanks.Count; i++)
        {
            RectTransform fog = fogBanks[i];
            fog.anchoredPosition += new Vector2(dt * (7f + i * 1.8f) * (i % 2 == 0 ? 1f : -1f),
                                                Mathf.Sin(Time.unscaledTime * 0.2f + i) * 0.06f);
            if (Mathf.Abs(fog.anchoredPosition.x) > ReferenceWidth * 0.68f)
                fog.anchoredPosition = new Vector2(-Mathf.Sign(fog.anchoredPosition.x) * ReferenceWidth * 0.55f, fog.anchoredPosition.y);
        }
        for (int i = 0; i < fallingCards.Count; i++)
        {
            RectTransform card = fallingCards[i];
            card.anchoredPosition += new Vector2(Mathf.Sin(Time.unscaledTime + i) * 0.14f,
                                                 -dt * backgroundSpeed * (28f + i * 3f));
            card.Rotate(0f, 0f, dt * (3.5f + i * 0.5f));
            if (card.anchoredPosition.y < -ReferenceHeight - 160f)
            {
                card.anchorMin = card.anchorMax = new Vector2(UnityEngine.Random.value, 1f);
                card.anchoredPosition = new Vector2(0f, 180f);
            }
        }
        for (int i = 0; i < lightBeams.Count; i++)
        {
            float angle = Mathf.Sin(Time.unscaledTime * (0.16f + i * 0.022f) + i) * 36f;
            lightBeams[i].localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        for (int i = 0; i < suitRibbons.Count; i++)
        {
            RectTransform rt = suitRibbons[i];
            float drift = Mathf.Repeat(Time.unscaledTime * (16f + i * 5f), 440f);
            rt.anchoredPosition = new Vector2(i % 2 == 0 ? drift - 220f : 220f - drift, 0f);
        }
        for (int i = 0; i < atmosphereMotes.Count; i++)
        {
            RectTransform m = atmosphereMotes[i];
            m.anchoredPosition += new Vector2(Mathf.Sin(Time.unscaledTime + i) * 0.2f,
                                              -dt * (7f + i % 7) * backgroundSpeed);
            if (m.anchoredPosition.y < -ReferenceHeight * 0.6f)
            {
                m.anchorMin = m.anchorMax = new Vector2(UnityEngine.Random.value, 1f);
                m.anchoredPosition = new Vector2(UnityEngine.Random.Range(-ReferenceWidth * 0.45f, ReferenceWidth * 0.45f), 40f);
            }
        }
        AnimateSurveillance();
    }

    // ── NEW: Rain animation ────────────────────────────────────────────────────
    private void AnimateRain(float dt)
    {
        float speed = (320f + tensionFloat * 140f) * backgroundSpeed;
        for (int i = 0; i < rainDrops.Count; i++)
        {
            RectTransform rt = rainDrops[i];
            rt.anchoredPosition += new Vector2(dt * 12f, -dt * speed); // slight sideways drift
            if (rt.anchoredPosition.y < -ReferenceHeight - 60f)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.Range(1.0f, 1.4f));
                rt.anchoredPosition = Vector2.zero;
                Image img = rt.GetComponent<Image>();
                if (img != null) img.color = ColdWhite.WithA(UnityEngine.Random.Range(0.04f, 0.16f));
            }
        }
    }

    // ── NEW: Reflection shimmer ────────────────────────────────────────────────
    private float _reflTimer;
    private void AnimateReflections(float dt)
    {
        _reflTimer += dt;
        if (_reflTimer < 0.15f) return;
        _reflTimer = 0f;
        foreach (Image img in fogImages)
        {
            if (img == null) continue;
            float pulse = Mathf.Sin(Time.unscaledTime * 0.4f) * 0.012f;
            Color c = img.color;
            img.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a + pulse));
        }
    }

    // ── NEW: Spectator count update ────────────────────────────────────────────
    private void AnimateSpectator(float dt)
    {
        if (Time.unscaledTime < nextSpectatorUpdate) return;
        nextSpectatorUpdate = Time.unscaledTime + UnityEngine.Random.Range(8f, 22f);
        spectatorCount += UnityEngine.Random.Range(-12, 18);
        spectatorCount  = Mathf.Clamp(spectatorCount, 600, 1200);
        if (spectatorLabel != null)
        {
            TextMeshProUGUI t = spectatorLabel.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = "SPECTATORS: " + spectatorCount.ToString("N0").Replace(",", " ");
        }
    }

    private float _dsTimer;
    private void AnimateDataStream(float dt)
    {
        _dsTimer += dt;
        if (_dsTimer < 0.06f) return;
        _dsTimer = 0f;
        string glyphs = "0123456789ABCDEF♦♠♣♥";
        int updateCount = reducedMotion ? 4 : 14;
        for (int i = 0; i < updateCount; i++)
        {
            if (dataStreamCells.Count == 0) break;
            int idx = UnityEngine.Random.Range(0, dataStreamCells.Count);
            TextMeshProUGUI cell = dataStreamCells[idx];
            cell.text = glyphs[UnityEngine.Random.Range(0, glyphs.Length)].ToString();
            cell.rectTransform.anchoredPosition += new Vector2(0f, -22f * backgroundSpeed);
            if (cell.rectTransform.anchoredPosition.y < -ReferenceHeight - 50f)
                cell.rectTransform.anchoredPosition = new Vector2(cell.rectTransform.anchoredPosition.x, 40f);
        }
    }

    private void AnimateRadar(float dt)
    {
        if (radarArm == null) return;
        float speed = 30f + tensionFloat * 45f;
        radarArm.Rotate(0f, 0f, -speed * dt);
    }

    private float _hexTimer;
    private void AnimateHexGrid(float dt)
    {
        _hexTimer += dt;
        if (_hexTimer < 0.08f) return;
        _hexTimer = 0f;
        int updateCount = reducedMotion ? 3 : 10;
        for (int i = 0; i < updateCount; i++)
        {
            if (hexCells.Count == 0) break;
            int idx = UnityEngine.Random.Range(0, hexCells.Count);
            Image img = hexCellImages.Count > idx ? hexCellImages[idx] : hexCells[idx].GetComponent<Image>();
            if (img == null) continue;
            float peak = Mathf.Lerp(0.05f, 0.22f, tensionFloat);
            img.DOFade(UnityEngine.Random.Range(0.01f, peak), UnityEngine.Random.Range(0.15f, 0.55f)).SetId(this);
        }
    }

    private void AnimateArcTendrils(float dt)
    {
        if (tensionFloat < 0.55f) return;
        if (Time.unscaledTime < nextArcSpawn) return;
        nextArcSpawn = Time.unscaledTime + Mathf.Lerp(1.2f, 0.25f, tensionFloat);
        if (arcTendrils.Count >= ArcCount) return;
        SpawnArcTendril();
    }

    private void SpawnArcTendril()
    {
        RectTransform arc = MakeImage("Arc Tendril", midPlane, Primary.WithA(0.55f)).rectTransform;
        arc.anchorMin = arc.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.Range(0.2f, 0.9f));
        arc.sizeDelta = new Vector2(UnityEngine.Random.Range(60f, 280f), 2f);
        arc.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-60f, 60f));
        arcTendrils.Add(arc);
        Image img = arc.GetComponent<Image>();
        img.DOFade(0f, UnityEngine.Random.Range(0.18f, 0.5f)).SetId(this).OnComplete(() =>
        {
            arcTendrils.Remove(arc);
            if (arc != null) Destroy(arc.gameObject);
        });
    }

    private float _winTimer;
    private void AnimateWindowLights(float dt)
    {
        _winTimer += dt;
        if (_winTimer < 0.22f) return;
        _winTimer = 0f;
        int count = UnityEngine.Random.Range(1, 4);
        for (int i = 0; i < count && windowLights.Count > 0; i++)
        {
            int idx = UnityEngine.Random.Range(0, windowLights.Count);
            Image img = windowLights[idx].GetComponent<Image>();
            if (img == null) continue;
            img.DOFade(UnityEngine.Random.Range(0.03f, 0.22f), UnityEngine.Random.Range(0.08f, 0.35f)).SetId(this);
        }
    }

    private void AnimateLetterbox(float dt)
    {
        if (letterboxTop == null || letterboxBottom == null) return;
        float target = (currentScreen == GameScreen.Playing || currentScreen == GameScreen.Pause) ? 0f : 54f;
        float cur    = letterboxTop.rectTransform.sizeDelta.y;
        float newH   = Mathf.MoveTowards(cur, target, dt * 90f);
        letterboxTop.rectTransform.sizeDelta    = new Vector2(0f, newH);
        letterboxBottom.rectTransform.sizeDelta = new Vector2(0f, newH);
    }

    private float _grainTimer;
    private void AnimatePostFx(float dt)
    {
        _grainTimer += dt;
        if (_grainTimer > 0.033f)
        {
            _grainTimer = 0f;
            if (filmGrainOverlay != null)
                filmGrainOverlay.color = TextWhite.WithA(UnityEngine.Random.Range(0f, grainIntensity * 0.045f));
        }
        if (chromaR != null)
        {
            float ca = chromaIntensity * 0.014f;
            chromaR.color = new Color(1f, 0f, 0f, ca);
            chromaR.rectTransform.offsetMin = new Vector2(-chromaIntensity * 5f, 0f);
            chromaR.rectTransform.offsetMax = new Vector2(-chromaIntensity * 5f, 0f);
            chromaG.color = new Color(0f, 1f, 0f, ca * 0.5f);
            chromaG.rectTransform.offsetMin = chromaG.rectTransform.offsetMax = Vector2.zero;
            chromaB.color = new Color(0f, 0f, 1f, ca);
            chromaB.rectTransform.offsetMin = new Vector2(chromaIntensity * 5f, 0f);
            chromaB.rectTransform.offsetMax = new Vector2(chromaIntensity * 5f, 0f);
        }
        if (vignetteInner != null && currentScreen == GameScreen.Playing)
        {
            float breath = Mathf.Sin(Time.unscaledTime * 1.6f) * 0.04f;
            float vAlpha = tensionFloat * 0.55f + breath;
            vignetteInner.color = Primary.WithA(Mathf.Clamp01(vAlpha));
        }
    }

    private void AnimateNoise(float dt)
    {
        if (Time.frameCount % 3 != 0) return;
        foreach (Image img in noisePixels)
            img.enabled = UnityEngine.Random.value > (0.62f - tensionFloat * 0.18f);
        if (UnityEngine.Random.value < 0.18f + tensionFloat * 0.22f)
        {
            foreach (RectTransform bar in glitchBars)
            {
                Image img = bar.GetComponent<Image>();
                float peak = Mathf.Lerp(0.04f, 0.26f, tensionFloat);
                img.color = Primary.WithA(UnityEngine.Random.Range(0.02f, peak));
                bar.anchoredPosition = new Vector2(UnityEngine.Random.Range(-760f, 760f), bar.anchoredPosition.y);
                bar.sizeDelta = new Vector2(UnityEngine.Random.Range(140f, 960f), UnityEngine.Random.Range(2f, 13f));
                img.DOFade(0f, 0.08f + tensionFloat * 0.06f).SetId(this);
            }
        }
        if (distortionWash != null && currentScreen == GameScreen.Playing)
        {
            float pressure = Mathf.Max(tensionFloat, Mathf.InverseLerp(10f, 0f, timeLeft));
            distortionWash.color = TextWhite.WithA(UnityEngine.Random.Range(0f, 0.03f * pressure));
        }
    }

    private void UpdateTensionFloat(float dt)
    {
        float target = Mathf.Clamp01((float)currentAttempt / Mathf.Max(1, selected.attempts - 1));
        float timerP = Mathf.InverseLerp(selected.seconds, 0f, timeLeft);
        target = Mathf.Max(target, timeLeft <= 12f ? timerP : 0f);
        tensionFloat    = Mathf.MoveTowards(tensionFloat, target, dt * 0.35f);
        chromaIntensity = tensionFloat;
        grainIntensity  = 0.3f + tensionFloat * 0.7f;
        backgroundSpeed = Mathf.Lerp(1f, 2.4f, tensionFloat);
    }

    private void AnimateSurveillance()
    {
        if (Time.unscaledTime >= nextSurveillancePulse && surveillanceLabels.Count > 0)
        {
            TextMeshProUGUI label = surveillanceLabels[UnityEngine.Random.Range(0, surveillanceLabels.Count)];
            label.rectTransform.anchorMin = label.rectTransform.anchorMax =
                new Vector2(UnityEngine.Random.Range(0.03f, 0.74f), UnityEngine.Random.Range(0.1f, 0.92f));
            Color baseC = label.text.Contains("ALIVE") ? SafeGreen
                        : label.text.Contains("ELIMINATED") ? Primary : TextWhite;
            label.color = baseC.WithA(UnityEngine.Random.Range(0.28f, 0.52f));
            label.DOFade(0f, UnityEngine.Random.Range(0.5f, 1.4f)).SetEase(Ease.InCubic).SetId(this);
            nextSurveillancePulse = Time.unscaledTime + UnityEngine.Random.Range(0.38f, tensionFloat >= 0.7f ? 0.9f : 2.6f);
        }
        if (Time.unscaledTime >= nextCoordinatePulse && coordinateLabels.Count > 0)
        {
            foreach (TextMeshProUGUI label in coordinateLabels.OrderBy(_ => UnityEngine.Random.value).Take(4))
            {
                label.text  = RandomCoordinate();
                label.color = TextWhite.WithA(UnityEngine.Random.Range(0.05f, 0.18f));
                label.DOFade(0f, UnityEngine.Random.Range(0.3f, 0.85f)).SetId(this);
            }
            nextCoordinatePulse = Time.unscaledTime + UnityEngine.Random.Range(0.6f, 1.7f);
        }
    }

    private float _cursorTimer;
    private void AnimateCursorBlinks(float dt)
    {
        _cursorTimer += dt;
        if (_cursorTimer < 0.52f) return;
        _cursorTimer = 0f;
        foreach (DigitBox box in inputBoxes)
        {
            if (box.cursor == null) continue;
            bool empty = box.label.text == "_" || box.label.text == string.Empty;
            if (!empty) { box.cursor.gameObject.SetActive(false); continue; }
            box.cursor.gameObject.SetActive(!box.cursor.gameObject.activeSelf);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCREEN BUILDS
    // ─────────────────────────────────────────────────────────────────────────
    private void WarmPool()
    {
        particlePool = new ObjectPool<Image>(() =>
        {
            Image img = MakeImage("Pooled Particle", fxLayer, Primary);
            img.raycastTarget = false;
            return img;
        }, ParticlePoolSize);
    }

    private void BuildScreens()
    {
        screens[GameScreen.Boot]       = BuildBootScreen();
        screens[GameScreen.Intro]      = BuildIntroScreen();
        screens[GameScreen.Playing]    = BuildGameScreen();
        screens[GameScreen.Pause]      = BuildPauseScreen();
        screens[GameScreen.Win]        = BuildResultScreen(true, false);
        screens[GameScreen.Lose]       = BuildResultScreen(false, false);
        screens[GameScreen.Eliminated] = BuildResultScreen(false, true);
        screens[GameScreen.Stats]      = BuildStatsScreen();
        screens[GameScreen.Settings]   = BuildSettingsScreen();
    }

    // ── UPGRADED: Boot Screen ─────────────────────────────────────────────────
    private RectTransform BuildBootScreen()
    {
        RectTransform s = ScreenRoot("Boot");

        // Full black overlay that fades out as boot progresses
        Image blackout = MakeImage("Boot Blackout", s, Color.black);
        blackout.rectTransform.Fill();

        // Diamond corona rings (glow behind diamond)
        for (int r = 3; r >= 1; r--)
        {
            Image corona = MakeImage("Diamond Corona " + r, s, Primary.WithA(0.03f / r));
            corona.sprite = circleSprite;
            corona.rectTransform.SetBox(0.5f, 0.62f, 160f * r, 160f * r);
            corona.rectTransform.DOScale(1.4f, 1.8f + r * 0.4f).SetLoops(-1, LoopType.Yoyo).SetId(this);
        }

        // Diamond with bloom
        TextMeshProUGUI diamond = TMP("Diamond", s, "♦", 190, Primary, TextAlignmentOptions.Center);
        diamond.rectTransform.SetBox(0.5f, 0.62f, 280f, 220f);
        diamond.characterSpacing = -10f;
        TextMeshProUGUI diamondBloom = TMP("Diamond Bloom", s, "♦", 240, Primary.WithA(0.08f), TextAlignmentOptions.Center);
        diamondBloom.rectTransform.SetBox(0.5f, 0.62f, 350f, 280f);

        // Boot text
        TextMeshProUGUI boot = TMP("Boot Text", s, string.Empty, 26, TextWhite, TextAlignmentOptions.TopLeft);
        boot.characterSpacing = 2f;
        boot.rectTransform.SetBox(0.5f, 0.34f, 880f, 270f);

        // Progress bar
        RectTransform bar = PanelBox("Boot Progress", s, Dark, Primary);
        bar.SetBox(0.5f, 0.16f, 740f, 22f);
        Image fill = MakeImage("Fill", bar, Primary);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        Image fillGlow = MakeImage("Fill Glow", bar, Primary.WithA(0.35f));
        fillGlow.rectTransform.anchorMin = Vector2.zero;
        fillGlow.rectTransform.anchorMax = new Vector2(0f, 1f);
        fillGlow.rectTransform.sizeDelta = new Vector2(20f, 4f);

        // NEW: percentage readout
        TextMeshProUGUI pct = TMP("Boot Pct", bar, "0%", 14, Dim, TextAlignmentOptions.Right);
        pct.rectTransform.Fill();
        pct.rectTransform.offsetMax = new Vector2(-6f, 0f);

        // NEW: terminal cursor
        TextMeshProUGUI termCursor = TMP("Term Cursor", s, "|", 26, Primary.WithA(0.8f), TextAlignmentOptions.Left);
        termCursor.rectTransform.SetBox(0.5f, 0.34f, 880f, 270f);
        termCursor.DOFade(0f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetId(this);

        TMP("Version", s, "DIAMOND PROTOCOL v3.0 // TOP SECRET", 14, Dim, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.08f, 660f, 24f);
        return s;
    }

    // ── UPGRADED: Intro Screen ────────────────────────────────────────────────
    private RectTransform BuildIntroScreen()
    {
        RectTransform s = ScreenRoot("Intro");
        MakeImage("Blackout", s, Color.black).rectTransform.Fill();
        BuildStageFrame(s, Primary.WithA(0.24f), "BORDERLAND GAME ARENA");

        Image redWash = MakeImage("Intro Red Wash", s, DeepBlood.WithA(0.18f));
        redWash.rectTransform.anchorMin = Vector2.zero;
        redWash.rectTransform.anchorMax = Vector2.one;
        redWash.rectTransform.offsetMin = redWash.rectTransform.offsetMax = Vector2.zero;

        RectTransform leftPanel = PanelBox("Selection Panel", s, GlassRed.WithA(0.42f), Primary.WithA(0.24f));leftPanel.SetBox(0.25f, 0.48f, 580f, 720f);
        leftPanel.SetBox(0.28f, 0.46f, 520f, 640f);
        AddAnimatedBorder(leftPanel, Primary.WithA(0.55f));

        // NEW: per-difficulty breathing effect
        for (int d = 0; d < 3; d++)
        {
            float beatPeriod = 2.0f + d * 0.5f;
            Image diffGlow = MakeImage("Diff Glow " + d, leftPanel, Primary.WithA(0.05f + d * 0.02f));
            diffGlow.rectTransform.anchorMin = new Vector2(0f, 0.72f - d * 0.24f);
            diffGlow.rectTransform.anchorMax = new Vector2(1f, 0.94f - d * 0.24f);
            diffGlow.rectTransform.offsetMin = diffGlow.rectTransform.offsetMax = Vector2.zero;
            diffGlow.DOFade(0.12f + d * 0.03f, beatPeriod).SetLoops(-1, LoopType.Yoyo).SetId(this);
        }

        RectTransform intelPanel = PanelBox("Intro Intel Panel", s, Dark.WithA(0.78f), Secondary.WithA(0.36f));intelPanel.SetBox(0.66f, 0.48f, 840f, 660f);
        intelPanel.SetBox(0.64f, 0.45f, 790f, 560f);
        AddAnimatedBorder(intelPanel, Secondary.WithA(0.55f));

        TextMeshProUGUI selectedText = TMP("Selected Text", s, string.Empty, 44, TextWhite, TextAlignmentOptions.Center);
        selectedText.fontStyle = FontStyles.Bold;
        selectedText.characterSpacing = 8f;
        selectedText.rectTransform.SetBox(0.5f, 0.84f, 1120f, 86f);
        // NEW: title breathing
        selectedText.rectTransform.DOScale(1.003f, 4f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetId(this);

        RectTransform card = PanelBox("Reveal Card", s, Dark.WithA(0.98f), Primary);
        card.SetBox(0.5f, 0.48f, 370f, 530f);
        AddAnimatedBorder(card, Primary);
        AddCardPips(card, Primary.WithA(0.24f));
        TMP("Card Pattern", card, "♦\n♦  ♦\n♦", 72, Primary.WithA(0.26f), TextAlignmentOptions.Center).rectTransform.Fill();
        TMP("Card Face", card, string.Empty, 52, TextWhite, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.55f, 320f, 220f);
        TextMeshProUGUI announcement = TMP("Announcement", s, string.Empty, 36, TextWhite, TextAlignmentOptions.Center);
        announcement.fontStyle = FontStyles.Bold;
        announcement.characterSpacing = 3f;
        announcement.rectTransform.SetBox(0.64f, 0.55f, 720f, 220f);

        RectTransform borderTop    = MakeImage("Closing Border Top",    s, Primary.WithA(0f)).rectTransform;
        RectTransform borderBottom = MakeImage("Closing Border Bottom", s, Primary.WithA(0f)).rectTransform;
        borderTop.anchorMin = new Vector2(0f, 1f); borderTop.anchorMax    = Vector2.one;      borderTop.sizeDelta    = new Vector2(0f, 0f);
        borderBottom.anchorMin = Vector2.zero;      borderBottom.anchorMax = new Vector2(1f, 0f); borderBottom.sizeDelta = new Vector2(0f, 0f);

        TMP("Rules Caption", s, "SURVIVAL CONTRACT", 18, Secondary.WithA(0.78f), TextAlignmentOptions.Left)
            .rectTransform.SetBox(0.64f, 0.36f, 720f, 32f);

        RectTransform ruleRail = MakeImage("Rule Rail", s, Primary.WithA(0.62f)).rectTransform;
        ruleRail.anchorMin = ruleRail.anchorMax = new Vector2(0.435f, 0.255f);
        ruleRail.sizeDelta = new Vector2(4f, 210f);

        TextMeshProUGUI rules = TMP("Reveal Rules", s, string.Empty, 28, 
            TextWhite, TextAlignmentOptions.TopLeft);
        rules.characterSpacing = 1.5f;
        rules.lineSpacing = 12f;
        rules.rectTransform.SetBox(0.66f, 0.22f, 740f, 280f);
        Button start = AddButton(s, "I HAVE NO CHOICE", 0f, -408f, 340f, 68f, Primary, StartNewGame);
        start.gameObject.SetActive(false);
        AddButton(s, "STATS",    -730f, -432f, 165f, 54f, Secondary, () => Show(GameScreen.Stats));
        AddButton(s, "SETTINGS",  730f, -432f, 195f, 54f, Secondary, () => Show(GameScreen.Settings));
        return s;
    }

    // ── UPGRADED: Game Screen ─────────────────────────────────────────────────
    private RectTransform BuildGameScreen()
    {
        timerRing = timerCorona = pressureFill = null;
        timerText = livesText = scoreText = attemptCounterText = null;
        statusText = warningText = dossierText = intelLogText = usedDigitsText = null;
        confirmButton = null; statusUnderline = null; spectatorLabel = null;

        RectTransform s = ScreenRoot("Game");
        BuildStageFrame(s, Primary.WithA(0.18f), "DIAMOND PROTOCOL LIVE");

        // ── HUD Bar ───────────────────────────────────────────────────────────
        RectTransform hud = PanelBox("HUD", s, Panel, Primary);
        hud.SetBox(0.5f, 0.92f, 1720f, 116f);
        // NEW: near-light specular on HUD top-left edge
        Image hudSpecLeft = MakeImage("HUD Specular Left", hud, TextWhite.WithA(0.06f));
        hudSpecLeft.rectTransform.anchorMin = Vector2.zero;
        hudSpecLeft.rectTransform.anchorMax = new Vector2(0f, 1f);
        hudSpecLeft.rectTransform.pivot     = new Vector2(0f, 0.5f);
        hudSpecLeft.rectTransform.sizeDelta = new Vector2(1f, 0f);
        Image hudSpecTop = MakeImage("HUD Specular Top", hud, TextWhite.WithA(0.08f));
        hudSpecTop.rectTransform.anchorMin = new Vector2(0f, 1f);
        hudSpecTop.rectTransform.anchorMax = Vector2.one;
        hudSpecTop.rectTransform.pivot     = new Vector2(0.5f, 1f);
        hudSpecTop.rectTransform.sizeDelta = new Vector2(0f, 1f);

        TMP("Hud Title",      hud, "♦ DIAMOND GAME", 30,  TextWhite, TextAlignmentOptions.Left).rectTransform.SetBox(0.12f, 0.58f, 340f, 58f);
        TMP("Timer Label",    hud, "TIMER",           16,  Dim,       TextAlignmentOptions.Center).rectTransform.SetBox(0.43f, 0.84f, 160f, 28f);

        // NEW: Correct score hierarchy — 48px Bold NeonGold
        TMP("Score Label",    hud, "SCORE",           11,  Dim,       TextAlignmentOptions.Center).rectTransform.SetBox(0.86f, 0.85f, 160f, 22f);
        TMP("Pressure Label", hud, "PRESSURE",        14,  Dim,       TextAlignmentOptions.Center).rectTransform.SetBox(0.535f, 0.78f, 160f, 26f);

        // NEW: Spectator count
        spectatorLabel = Rect("Spectator Label Rt", hud);
        spectatorLabel.SetBox(0.17f, 0.25f, 220f, 26f);
        TextMeshProUGUI specT = TMP("Spectator Count", spectatorLabel, "SPECTATORS: 847", 13, Dim, TextAlignmentOptions.Left);
        specT.characterSpacing = 1f;

        // Pressure meter
        RectTransform pressureBox = PanelBox("Pressure Meter", hud, Dark.WithA(0.85f), Primary.WithA(0.45f));
        pressureBox.SetBox(0.535f, 0.38f, 195f, 16f);
        pressureFill = MakeImage("Pressure Fill", pressureBox, Primary.WithA(0.75f));
        pressureFill.rectTransform.anchorMin = Vector2.zero;
        pressureFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        pressureFill.rectTransform.offsetMin = pressureFill.rectTransform.offsetMax = Vector2.zero;
        Image pressureGlow = MakeImage("Pressure Glow", pressureBox, Primary.WithA(0f));
        pressureGlow.rectTransform.anchorMin = new Vector2(0f, 0f);
        pressureGlow.rectTransform.anchorMax = new Vector2(0f, 1f);
        pressureGlow.rectTransform.sizeDelta = new Vector2(8f, 0f);

        // Timer ring + corona
        Image ringBg = MakeImage("Timer Ring Bg", hud, Dark.WithA(0.95f));
        ringBg.sprite = circleSprite; ringBg.rectTransform.SetBox(0.43f, 0.45f, 90f, 90f);

        timerCorona = MakeImage("Timer Corona", hud, SafeGreen.WithA(0.12f));
        timerCorona.sprite = circleSprite;
        timerCorona.rectTransform.SetBox(0.43f, 0.45f, 118f, 118f);

        timerRing = MakeImage("Timer Ring", hud, SafeGreen);
        timerRing.sprite = circleSprite; timerRing.type = Image.Type.Filled;
        timerRing.fillMethod = Image.FillMethod.Radial360;
        timerRing.fillOrigin = (int)Image.Origin360.Top;
        timerRing.fillClockwise = false; timerRing.fillAmount = 1f;
        timerRing.rectTransform.SetBox(0.43f, 0.45f, 90f, 90f);

        Image ringCore = MakeImage("Timer Ring Core", hud, Panel);
        ringCore.sprite = circleSprite; ringCore.rectTransform.SetBox(0.43f, 0.45f, 60f, 60f);

        timerText = TMP("Timer", hud, "30", 28, SafeGreen, TextAlignmentOptions.Center);
        timerText.fontStyle = FontStyles.Bold; timerText.characterSpacing = -2f;
        timerText.rectTransform.SetBox(0.43f, 0.45f, 90f, 50f);

        livesText = TMP("Lives", hud, "♦♦♦", 34, Primary, TextAlignmentOptions.Center);
        livesText.rectTransform.SetBox(0.64f, 0.48f, 235f, 60f);

        // NEW: 48px Bold NeonGold score
        scoreText = TMP("Score", hud, "0", 48, NeonGold, TextAlignmentOptions.Center);
        scoreText.fontStyle = FontStyles.Bold;
        scoreText.rectTransform.SetBox(0.86f, 0.42f, 185f, 64f);

        // NEW: Attempts counter — will get permanent scale on last 2
        attemptCounterText = TMP("Attempt Counter", hud, "[0/" + selected.attempts + "]", 26, Warning, TextAlignmentOptions.Center);
        attemptCounterText.rectTransform.SetBox(0.28f, 0.48f, 175f, 48f);

        RectTransform diffBadge = PanelBox("Difficulty Badge", hud, selected.color.WithA(0.12f), selected.color);
        diffBadge.SetBox(0.94f, 0.5f, 135f, 58f);
        TMP("Diff Label", diffBadge, selected.title.Split(' ')[0], 18, selected.color, TextAlignmentOptions.Center).rectTransform.Fill();

        // ── Left Panel: Attempt History ───────────────────────────────────────
        RectTransform left = PanelBox("Attempt History", s, Panel, Primary);
        left.SetBox(0.29f, 0.49f, 800f, 745f);
        BuildPanelHeaderStrip(left, "SYSTEM MEMORY // ATTEMPTS", Primary);
        TMP("History Title", left, "ATTEMPT HISTORY", 25, TextWhite, TextAlignmentOptions.Left).rectTransform.SetBox(0.25f, 0.94f, 340f, 40f);
        // NEW: specular edges on left panel
        AddPanelSpecular(left);

        // NEW: Ghost attempt rows from previous players
        BuildGhostAttempts(left);

        for (int i = 0; i < selected.attempts; i++)
        {
            AttemptRow row = new AttemptRow();
            row.root = Rect("Attempt Row " + (i + 1), left);
            row.root.anchorMin = row.root.anchorMax = new Vector2(0.5f, 0.84f - i * 0.104f);
            row.root.sizeDelta = new Vector2(710f, 68f);
            Image rowGlow = MakeImage("Row Active Glow", row.root, Primary.WithA(0.03f));
            rowGlow.rectTransform.Fill();
            Image rowLine = MakeImage("Row Sep", row.root, Primary.WithA(0.06f));
            rowLine.rectTransform.anchorMin = Vector2.zero;
            rowLine.rectTransform.anchorMax = new Vector2(1f, 0f);
            rowLine.rectTransform.sizeDelta  = new Vector2(0f, 1f);
            row.index = TMP("Index", row.root, "[" + (i + 1).ToString("00") + "]", 21, Dim, TextAlignmentOptions.Center);
            row.index.rectTransform.SetBox(0.08f, 0.5f, 92f, 44f);
            for (int d = 0; d < CodeLength; d++)
            {
                DigitBox box = MakeDigitBoxV3(row.root, new Vector2(-210f + d * 105f, 0f), 58f);
                row.boxes.Add(box);
            }
            attemptRows.Add(row);
        }

        // ── Right Panel: Input Terminal ───────────────────────────────────────
        RectTransform right = PanelBox("Input Terminal", s, Panel, Primary);
        right.SetBox(0.73f, 0.49f, 750f, 745f);
        BuildPanelHeaderStrip(right, "CIPHER INPUT // PLAYER VS SYSTEM", Secondary);
        TMP("Terminal Title", right, "CIPHER TERMINAL v3.0 >", 25, TextWhite, TextAlignmentOptions.Left).rectTransform.SetBox(0.34f, 0.94f, 460f, 40f);
        // NEW: specular edges on right panel
        AddPanelSpecular(right);

        dossierText = TMP("Dossier Text", right, "ACCESS LEVEL: RESTRICTED\nAUTHORIZATION: PLAYER #180\nOBJECTIVE: FIND 5 UNIQUE DIGITS", 15, ColdWhite, TextAlignmentOptions.Left);
        dossierText.characterSpacing = 1f;
        dossierText.rectTransform.SetBox(0.70f, 0.925f, 270f, 76f);

        RectTransform inputPanel = PanelBox("Input Code", right, Dark, Secondary);
        inputPanel.SetBox(0.5f, 0.78f, 520f, 112f);

        for (int i = 0; i < CodeLength; i++)
        {
            DigitBox box = MakeDigitBoxV3(inputPanel, new Vector2(-200f + i * 100f, 0f), 72f);
            inputBoxes.Add(box);
        }

        BuildNumpad(right);

        usedDigitsText = TMP("Used Digits", right, "USED DIGITS: NONE", 15, Dim, TextAlignmentOptions.Center);
        usedDigitsText.rectTransform.SetBox(0.5f, 0.252f, 570f, 32f);

        intelLogText = TMP("Intel Log", right, "INTEL LOG\n- Awaiting first pattern.", 16, ColdWhite.WithA(0.78f), TextAlignmentOptions.TopLeft);
        intelLogText.rectTransform.SetBox(0.5f, 0.172f, 590f, 70f);

        AddButton(right, "WHISPER -200",    -235f, -324f, 195f, 48f, Secondary, UseWhisper);
        AddButton(right, "VISION -300",        0f, -324f, 175f, 48f, Warning,   UseVision);
        AddButton(right, "REVELATION -500",  238f, -324f, 215f, 48f, Primary,   UseRevelation);

        // ── Status Bar ────────────────────────────────────────────────────────
        RectTransform status = PanelBox("System Status", s, Dark.WithA(0.92f), Secondary);
        status.SetBox(0.5f, 0.062f, 1720f, 70f);
        BuildMicroTicks(status, 24, Secondary.WithA(0.28f));
        statusText = TMP("System Line", status, systemLine, 23, TextWhite, TextAlignmentOptions.Left);
        statusText.characterSpacing = 1.5f;
        statusText.rectTransform.SetBox(0.5f, 0.5f, 1620f, 46f);
        // NEW: status sweep underline
        statusUnderline = MakeImage("Status Underline", status, Secondary.WithA(0f));
        statusUnderline.rectTransform.anchorMin = new Vector2(0f, 0f);
        statusUnderline.rectTransform.anchorMax = new Vector2(0f, 0f);
        statusUnderline.rectTransform.pivot     = new Vector2(0f, 0f);
        statusUnderline.rectTransform.sizeDelta = new Vector2(0f, 2f);
        statusUnderline.rectTransform.anchoredPosition = new Vector2(50f, 6f);

        warningText = TMP("Warning Text", s, string.Empty, 52, Primary, TextAlignmentOptions.Center);
        warningText.fontStyle = FontStyles.Bold; warningText.characterSpacing = 6f;
        warningText.rectTransform.SetBox(0.5f, 0.52f, 920f, 96f);
        warningText.gameObject.SetActive(false);

        AddButton(s, "PAUSE", 808f, 476f, 148f, 50f, Secondary, PauseGame);
        return s;
    }

    // ── NEW: Ghost Attempts from previous players ─────────────────────────────
    private void BuildGhostAttempts(RectTransform parent)
    {
        System.Random ghostRng = new System.Random(42); // deterministic
        for (int i = 0; i < selected.attempts; i++)
        {
            for (int d = 0; d < CodeLength; d++)
            {
                int ghostDigit = ghostRng.Next(0, 10);
                float alpha    = UnityEngine.Random.Range(0.018f, 0.042f);
                TextMeshProUGUI ghost = TMP("Ghost " + i + "," + d, parent, ghostDigit.ToString(), 22, Dim.WithA(alpha), TextAlignmentOptions.Center);
                ghost.fontStyle = FontStyles.Bold;
                RectTransform rt = ghost.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.84f - i * 0.104f);
                rt.sizeDelta  = new Vector2(48f, 48f);
                rt.anchoredPosition = new Vector2(-210f + d * 105f, 0f);
                // subtle slow drift
                rt.DOAnchorPosY(rt.anchoredPosition.y + UnityEngine.Random.Range(-2f, 2f), UnityEngine.Random.Range(3f, 6f))
                  .SetLoops(-1, LoopType.Yoyo).SetId(this);
            }
        }
    }

    // ── NEW: Panel specular highlights ────────────────────────────────────────
    private void AddPanelSpecular(RectTransform panel)
    {
        // Top edge highlight
        Image top = MakeImage("Panel Specular Top", panel, TextWhite.WithA(0.07f));
        top.rectTransform.anchorMin = new Vector2(0f, 1f);
        top.rectTransform.anchorMax = Vector2.one;
        top.rectTransform.pivot     = new Vector2(0.5f, 1f);
        top.rectTransform.sizeDelta = new Vector2(0f, 1f);
        // Left edge highlight
        Image left = MakeImage("Panel Specular Left", panel, TextWhite.WithA(0.05f));
        left.rectTransform.anchorMin = Vector2.zero;
        left.rectTransform.anchorMax = new Vector2(0f, 1f);
        left.rectTransform.pivot     = new Vector2(0f, 0.5f);
        left.rectTransform.sizeDelta = new Vector2(1f, 0f);
        // Slow breathing panel glow — panel edge color cycle
        Image glow = MakeImage("Panel Breathing Glow", panel, Primary.WithA(0f));
        glow.rectTransform.Fill();
        glow.raycastTarget = false;
        DOTween.Sequence().SetId(this)
            .Append(glow.DOFade(0.028f, 4f))
            .Append(glow.DOColor(Secondary.WithA(0.028f), 2f))
            .Append(glow.DOFade(0f, 2f))
            .SetLoops(-1);
    }

    // ── UPGRADED: Digit Box V3 with glass-metal material ─────────────────────
    private DigitBox MakeDigitBoxV3(RectTransform parent, Vector2 pos, float size)
    {
        // Outer glow ring
        Image glowRing = MakeImage("Glow Ring", parent, Primary.WithA(0f));
        glowRing.sprite = circleSprite;
        RectTransform grt = glowRing.rectTransform;
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
        grt.sizeDelta  = new Vector2(size + 22f, size + 22f);
        grt.anchoredPosition = pos;

        // Main box
        RectTransform rt = PanelBox("Digit Box", parent, Dark, Primary.WithA(0.6f));
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta  = new Vector2(size, size);
        rt.anchoredPosition = pos;

        // NEW: Top bevel (glass highlight)
        Image topBevel = MakeImage("Top Bevel", rt, TextWhite.WithA(0.10f));
        topBevel.rectTransform.anchorMin = new Vector2(0f, 1f);
        topBevel.rectTransform.anchorMax = Vector2.one;
        topBevel.rectTransform.pivot     = new Vector2(0.5f, 1f);
        topBevel.rectTransform.sizeDelta = new Vector2(0f, 2f);

        // NEW: Bottom/right inset shadow (depth simulation)
        Image shadowBottom = MakeImage("Shadow Bottom", rt, Color.black.WithA(0.22f));
        shadowBottom.rectTransform.anchorMin = Vector2.zero;
        shadowBottom.rectTransform.anchorMax = new Vector2(1f, 0f);
        shadowBottom.rectTransform.pivot     = new Vector2(0.5f, 0f);
        shadowBottom.rectTransform.sizeDelta = new Vector2(0f, 2f);
        Image shadowRight = MakeImage("Shadow Right", rt, Color.black.WithA(0.18f));
        shadowRight.rectTransform.anchorMin = new Vector2(1f, 0f);
        shadowRight.rectTransform.anchorMax = Vector2.one;
        shadowRight.rectTransform.pivot     = new Vector2(1f, 0.5f);
        shadowRight.rectTransform.sizeDelta = new Vector2(2f, 0f);

        // NEW: Diagonal specular stripe
        Image specular = MakeImage("Specular", rt, TextWhite.WithA(0.04f));
        specular.rectTransform.anchorMin = specular.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        specular.rectTransform.sizeDelta  = new Vector2(6f, size * 1.4f);
        specular.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 32f);
        specular.rectTransform.anchoredPosition = new Vector2(-size * 0.5f, 0f);
        specular.rectTransform.DOAnchorPosX(size * 0.5f, 8f).SetLoops(-1, LoopType.Restart).SetEase(Ease.InOutSine).SetId(this);

        // Scanline shimmer
        Image shimmer = MakeImage("Shimmer", rt, TextWhite.WithA(0.04f));
        shimmer.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        shimmer.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        shimmer.rectTransform.sizeDelta  = new Vector2(0f, 3f);
        shimmer.rectTransform.DOAnchorPosY(size * 0.5f, 1.1f).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetId(this);
        shimmer.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f);

        // NEW: empty box drip (vertical gradient sweep, slow 8s)
        Image drip = MakeImage("Box Drip", rt, TextWhite.WithA(0.025f));
        drip.rectTransform.anchorMin = new Vector2(0f, 1f);
        drip.rectTransform.anchorMax = Vector2.one;
        drip.rectTransform.pivot     = new Vector2(0.5f, 1f);
        drip.rectTransform.sizeDelta = new Vector2(0f, 4f);
        drip.rectTransform.DOAnchorPosY(-size, 8f).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetId(this);
        drip.DOFade(0f, 8f).SetLoops(-1, LoopType.Restart).SetId(this);

        TextMeshProUGUI label = TMP("Digit", rt, "_", Mathf.RoundToInt(size * 0.46f), Dim, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold; label.Fill();

        TextMeshProUGUI mark = TMP("Mark", rt, string.Empty, Mathf.RoundToInt(size * 0.22f), Primary, TextAlignmentOptions.TopRight);
        mark.Fill();

        TextMeshProUGUI cursor = TMP("Cursor", rt, "|", Mathf.RoundToInt(size * 0.38f), Primary.WithA(0.7f), TextAlignmentOptions.Center);
        cursor.rectTransform.SetBox(0.5f, 0.42f, size, size * 0.5f);
        cursor.gameObject.SetActive(false);

        return new DigitBox
        {
            root = rt, image = rt.GetComponent<Image>(),
            glowRing = glowRing, scanShimmer = shimmer,
            topBevel = topBevel, specularStripe = specular,
            label = label, mark = mark, cursor = cursor
        };
    }

    private void BuildNumpad(RectTransform parent)
    {
        int[] digits = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        for (int i = 0; i < digits.Length; i++)
        {
            int digit = digits[i];
            float x = -150f + (i % 3) * 150f;
            float y =   90f - (i / 3)  * 72f;
            Button b = AddButton(parent, digit.ToString(), x, y, 108f, 56f, Secondary, () => PressDigit(digit));
            digitButtons[digit]      = b;
            digitButtonImages[digit] = b.GetComponent<Image>();
        }
        Button del     = AddButton(parent, "DEL",     -150f, -128f, 108f, 56f, Warning,   DeleteDigit);
        Button zero    = AddButton(parent, "0",           0f, -128f, 108f, 56f, Secondary, () => PressDigit(0));
        Button confirm = AddButton(parent, "CONFIRM",  150f, -128f, 132f, 56f, SafeGreen,  ConfirmGuess);
        digitButtons[0]      = zero;
        digitButtonImages[0] = zero.GetComponent<Image>();
        del.name     = "Delete Button";
        confirm.name = "Confirm Button";
        confirmButton = confirm;
        confirm.interactable = false;
        confirm.GetComponent<Image>().color = BlackMark.WithA(0.38f);
    }

    private RectTransform BuildPauseScreen()
    {
        RectTransform s = ScreenRoot("Pause");
        MakeImage("Shade", s, Color.black.WithA(0.88f)).rectTransform.Fill();
        RectTransform p = PanelBox("Pause Panel", s, Panel, Primary);
        p.SetBox(0.5f, 0.5f, 780f, 520f);
        TMP("Title", p, "♦ PAUSED", 56, TextWhite, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.78f, 580f, 82f);
        TMP("Pause Calm",    p, "GAME PAUSED - TAKE A BREATH", 23, Dim,       TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.64f, 610f, 50f);
        TMP("Pause Summary", p, string.Empty,                  23, TextWhite, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.49f, 650f, 90f);
        AddButton(p, "RESUME",       0f,   42f, 290f, 64f, SafeGreen,  ResumeGame);
        AddButton(p, "RESTART",      0f, -42f,  290f, 64f, Warning,    StartNewGame);
        AddButton(p, "EXIT TO CITY", 0f, -128f, 290f, 64f, Secondary,  RestartToIntro);
        return s;
    }

    private RectTransform BuildResultScreen(bool win, bool eliminated)
    {
        RectTransform s = ScreenRoot(win ? "Win" : eliminated ? "Eliminated" : "Lose");
        BuildStageFrame(s, (win ? SafeGreen : Primary).WithA(0.2f), win ? "SURVIVAL VERDICT" : "BORDERLAND JUDGEMENT");
        Color border = win ? SafeGreen : Primary;
        RectTransform p = PanelBox("Result Panel", s, Panel, border);
        p.SetBox(0.5f, 0.52f, 1020f, 660f);
        AddAnimatedBorder(p, border);
        AddPanelSpecular(p);

        TextMeshProUGUI trophy = TMP("Symbol", p, eliminated ? "✕" : "♦", 116, win ? NeonGold : border, TextAlignmentOptions.Center);
        trophy.rectTransform.SetBox(0.5f, 0.82f, 210f, 148f);
        trophy.transform.DOScale(1.09f, 0.62f).SetLoops(-1, LoopType.Yoyo).SetId(this);
        TextMeshProUGUI bloom = TMP("Symbol Bloom", p, eliminated ? "✕" : "♦", 150, (win ? NeonGold : border).WithA(0.06f), TextAlignmentOptions.Center);
        bloom.rectTransform.SetBox(0.5f, 0.82f, 270f, 185f);

        TMP("Title", p, win ? "PLAYER CLEARED" : eliminated ? "ELIMINATION" : "ATTEMPT FAILED", 54, win ? SafeGreen : Primary, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.66f, 860f, 84f);
        TMP("Result Body", p, "", 28, TextWhite, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.42f, 840f, 186f);
        AddButton(p, "PLAY AGAIN", -195f, -208f, 245f, 64f, win ? SafeGreen : Primary, StartNewGame);
        AddButton(p, "MAIN CARD",   195f, -208f, 245f, 64f, Secondary, RestartToIntro);
        return s;
    }

    private RectTransform BuildStatsScreen()
    {
        RectTransform s = ScreenRoot("Stats");
        BuildStageFrame(s, Primary.WithA(0.14f), "ARCHIVE ACCESS");
        RectTransform p = PanelBox("Stats Panel", s, Panel, Primary);
        p.SetBox(0.5f, 0.52f, 1220f, 770f);
        AddPanelSpecular(p);
        TMP("Title", p, "SURVIVAL ARCHIVES", 46, TextWhite, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.91f, 720f, 72f);
        TMP("Stats Body", p, "", 23, TextWhite, TextAlignmentOptions.TopLeft).rectTransform.SetBox(0.5f, 0.49f, 1080f, 570f);
        AddButton(p, "BACK", 0f, -332f, 225f, 60f, Secondary, () => Show(GameScreen.Intro));
        return s;
    }

    private RectTransform BuildSettingsScreen()
    {
        RectTransform s = ScreenRoot("Settings");
        BuildStageFrame(s, Primary.WithA(0.14f), "SYSTEM CONFIGURATION");
        RectTransform p = PanelBox("Settings Panel", s, Panel, Primary);
        p.SetBox(0.5f, 0.52f, 980f, 700f);
        AddPanelSpecular(p);
        TMP("Title", p, "SYSTEM SETTINGS", 46, TextWhite, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.9f, 720f, 72f);
        AddButton(p, "SOUND",      -215f,  162f, 255f, 60f, Secondary, () => ToggleSetting(ref soundEnabled));
        AddButton(p, "MUSIC",       215f,  162f, 255f, 60f, Secondary, ToggleMusic);
        AddButton(p, "SHAKE",      -215f,   72f, 255f, 60f, Secondary, () => ToggleSetting(ref shakeEnabled));
        AddButton(p, "REDUCED FX",  215f,   72f, 255f, 60f, Secondary, () => ToggleSetting(ref reducedMotion));
        AddButton(p, "COLORBLIND", -215f,  -18f, 255f, 60f, Secondary, () => ToggleSetting(ref colorblind));
        AddButton(p, "LARGE TEXT",  215f,  -18f, 255f, 60f, Secondary, () => ToggleLargeText());
        AddButton(p, "RESET DATA",    0f, -148f, 285f, 64f, Primary,   ResetAllData);
        AddButton(p, "BACK",          0f, -264f, 225f, 60f, Secondary, () => Show(GameScreen.Intro));
        TMP("Settings Body", p, "", 21, Dim, TextAlignmentOptions.Center).rectTransform.SetBox(0.5f, 0.26f, 800f, 78f);
        return s;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BOOT SEQUENCE
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator BootSequence()
    {
        ApplyForcedDifficulty();
        stateMachine.Transition(GameState.Boot);

        RectTransform bootS = screens[GameScreen.Boot];
        Image blackout = bootS.Find("Boot Blackout") as RectTransform != null
            ? (bootS.Find("Boot Blackout") as RectTransform).GetComponent<Image>() : null;
        TextMeshProUGUI bootText = FindText(bootS, "Boot Text");
        TextMeshProUGUI pct      = FindText(bootS, "Boot Pct");
        Image fill = bootS.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Fill");
        RectTransform diamond = bootS.Find("Diamond") as RectTransform;

        if (diamond != null)
        {
            diamond.DOScale(1.22f, 0.38f).SetLoops(6, LoopType.Yoyo).SetId(this);
            diamond.GetComponent<TextMeshProUGUI>()?.DOColor(Primary.WithA(0.8f), 0.2f).SetLoops(14, LoopType.Yoyo).SetId(this);
        }

        // NEW: per-line state colors
        Color[] lineColors = { Warning, TextWhite, SafeGreen, Warning, Primary, TextWhite };
        string[] lines = {
            "BORDERLAND GAME SYSTEM",
            "INITIALIZING GAME PROTOCOL...",
            "CARD: " + CardLabel(),
            "CATEGORY: INTELLIGENCE",
            "PLAYERS: 1 / LIVES AT STAKE: " + selected.lives,
            "GOOD LUCK. YOU WILL NEED IT."
        };
        for (int i = 0; i < lines.Length; i++)
        {
            bootText.color = lineColors[i];
            yield return TypeLine(bootText, lines[i] + "\n", 0.016f);
            if (fill != null)
            {
                float prog = (i + 1f) / lines.Length;
                fill.rectTransform.anchorMax = new Vector2(prog, 1f);
                if (pct != null) pct.text = Mathf.RoundToInt(prog * 100f) + "%";
            }
            // NEW: background comes online progressively
            if (blackout != null && i == 1)
                blackout.DOFade(0f, 1.2f).SetId(this);
            PlayTone(155f + i * 48f, 0.04f, 0.03f, 0f);
        }
        Flash(TextWhite, 0.2f);
        yield return new WaitForSeconds(0.3f);
        Show(GameScreen.Intro);
        yield return CardRevealSequence();
    }

    private IEnumerator CardRevealSequence()
    {
        ApplyForcedDifficulty();
        stateMachine.Transition(GameState.CardReveal);
        RectTransform intro         = screens[GameScreen.Intro];
        TextMeshProUGUI selectedLabel = FindText(intro, "Selected Text");
        RectTransform card          = intro.Find("Reveal Card") as RectTransform;
        TextMeshProUGUI face        = FindText(intro, "Card Face");
        TextMeshProUGUI announcement = FindText(intro, "Announcement");
        TextMeshProUGUI rules       = FindText(intro, "Reveal Rules");
        RectTransform top           = intro.Find("Closing Border Top") as RectTransform;
        RectTransform bottom        = intro.Find("Closing Border Bottom") as RectTransform;
        Button start                = intro.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Button I HAVE NO CHOICE");

        if (selectedLabel == null || card == null || face == null || announcement == null || rules == null || start == null)
        { if (start != null) start.gameObject.SetActive(true); yield break; }

        selectedLabel.text = string.Empty; announcement.text = string.Empty;
        rules.text = string.Empty; face.text = string.Empty;
        card.localScale = Vector3.one;
        start.gameObject.SetActive(false);

        // NEW: card slides in from top-right like being dealt
        card.anchoredPosition = new Vector2(480f, 280f);
        card.localRotation    = Quaternion.Euler(0f, 0f, 15f);
        card.DOAnchorPos(Vector2.zero, 0.55f).SetEase(Ease.OutCubic).SetId(this);
        card.DOLocalRotate(Vector3.zero, 0.55f).SetEase(Ease.OutCubic).SetId(this);

        PlayTone(72f, 0.16f, 0.09f, -0.15f);
        yield return new WaitForSeconds(0.55f);
        PlayTone(72f, 0.16f, 0.09f,  0.15f);

        // NEW: word-by-word "YOU HAVE BEEN SELECTED" with dramatic pauses
        string[] words = { "YOU", " HAVE", " BEEN", " SELECTED" };
        float[] pauses = { 0.38f, 0.28f, 0.14f, 0f };
        foreach (var pair in words.Zip(pauses, (w, p) => (w, p)))
        {
            selectedLabel.text += pair.w;
            yield return new WaitForSeconds(reducedMotion ? 0.04f : 0.025f);
            if (pair.p > 0f) yield return new WaitForSeconds(reducedMotion ? 0.05f : pair.p);
        }

        yield return RgbSplitEffect(selectedLabel.rectTransform, 0.35f);
        yield return new WaitForSeconds(reducedMotion ? 0.12f : 0.5f);

        face.text = "♦ BACK";
        PlayCardFlip();
        yield return FlipCard(card, () => face.text = CardShortLabel());
        yield return new WaitForSeconds(reducedMotion ? 0.08f : 0.4f);

        if (top != null)
        {
            top.GetComponent<Image>().DOFade(0.24f, 0.3f).SetId(this);
            top.DOSizeDelta(new Vector2(0f, 88f), 0.6f).SetEase(Ease.OutCubic).SetId(this);
        }
        if (bottom != null)
        {
            bottom.GetComponent<Image>().DOFade(0.24f, 0.3f).SetId(this);
            bottom.DOSizeDelta(new Vector2(0f, 88f), 0.6f).SetEase(Ease.OutCubic).SetId(this);
        }
        card.DOAnchorPos(new Vector2(-420f, -18f), 0.55f).SetEase(Ease.InOutCubic).SetId(this);
        card.DOScale(0.72f, 0.55f).SetEase(Ease.InOutCubic).SetId(this);

        PlayHorrorSting();
        yield return TypeOverwrite(announcement, AnnouncementText(), 0.016f);
        yield return new WaitForSeconds(reducedMotion ? 0.08f : 0.42f);

        stateMachine.Transition(GameState.Rules);

        // NEW: rule lines with their mark colors
        (string line, Color color)[] ruleLines = {
            ("♦ GREEN = correct position",   SafeGreen),
            ("◆ ORANGE = wrong position",    Warning),
            ("■ DARK = not in code",         Dim),
            ("YOU HAVE " + selected.seconds + " SECONDS PER ATTEMPT", TextWhite),
            ("FAILURE MEANS ELIMINATION",    Primary),
            ("ARE YOU READY?",               SafeGreen),
        };
        foreach (var (line, col) in ruleLines)
        {
            Color prev = rules.color;
            rules.color = col;
            yield return TypeLine(rules, line + "\n", 0.016f);
            PlayTone(line.Contains("READY") ? 175f : 255f, 0.04f, 0.032f, 0f);
        }
        rules.DOColor(Primary, 0.42f).SetLoops(6, LoopType.Yoyo).SetId(this);
        start.gameObject.SetActive(true);
        start.transform.localScale = Vector3.one * 0.68f;
        start.transform.DOScale(1f, 0.42f).SetEase(Ease.OutBack).SetId(this);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GAMEPLAY
    // ─────────────────────────────────────────────────────────────────────────
    private void StartNewGame()
    {
        ApplyForcedDifficulty();
        RebuildGameplayScreen();
        secret.Clear(); input.Clear(); knownDigits.Clear(); digitPressCounts.Clear();
        secret.AddRange(Enumerable.Range(0, DigitCount).OrderBy(_ => rng.Next()).Take(CodeLength));
        currentAttempt = 0; lives = selected.lives; score = 0;
        shownScore = 0; lastScoreTweenValue = 0f;
        lastBaseScore = lastTimeBonus = lastFinalScore = lastTensionLevel = 0;
        hintPenalty = hintsUsedThisGame = 0;
        whisperUsed = visionUsed = revelationUsed = false;
        acceptingInput = true; timeLeft = selected.seconds;
        nextAutosave = Time.unscaledTime + AutosaveSeconds;
        tensionFloat = 0f; chromaIntensity = 0f;
        attemptCounterShaken = false;
        lastTickSecond = -1;
        intelLog.Clear();
        SetStatus("> SYSTEM: Analyze. Deduce. Survive.");
        stateMachine.Transition(GameState.Playing);
        Show(GameScreen.Playing);
        ApplyTension(true);
        RefreshInput(); RefreshHud();
        StartAttemptTimer(true);
        StartCoroutine(PlayEntryTicker());
        if (!PlaySfx(digitReveal, 0.6f)) PlayTone(190f, 0.14f, 0.055f, 0f);
    }

    private IEnumerator PlayEntryTicker()
    {
        RectTransform overlay = CinematicOverlay("Entry Ticker", Color.clear);
        TextMeshProUGUI ticker = TMP("Ticker", overlay, "PLAYER #180 HAS ENTERED THE ARENA", 38, TextWhite.WithA(0f), TextAlignmentOptions.Center);
        ticker.characterSpacing = 6f;
        ticker.rectTransform.SetBox(0.5f, 0.5f, 1200f, 70f);
        ticker.DOFade(1f, 0.3f).SetId(this);
        RectTransform line1 = MakeImage("Ticker Line Top",    overlay, Primary.WithA(0f)).rectTransform;
        RectTransform line2 = MakeImage("Ticker Line Bottom", overlay, Primary.WithA(0f)).rectTransform;
        line1.anchorMin = line1.anchorMax = new Vector2(0.5f, 0.56f); line1.sizeDelta = new Vector2(0f, 2f);
        line2.anchorMin = line2.anchorMax = new Vector2(0.5f, 0.44f); line2.sizeDelta = new Vector2(0f, 2f);
        line1.GetComponent<Image>().DOFade(0.55f, 0.2f).SetId(this);
        line2.GetComponent<Image>().DOFade(0.55f, 0.2f).SetId(this);
        line1.DOSizeDelta(new Vector2(1400f, 2f), 0.4f).SetEase(Ease.OutCubic).SetId(this);
        line2.DOSizeDelta(new Vector2(1400f, 2f), 0.4f).SetEase(Ease.OutCubic).SetId(this);
        yield return new WaitForSeconds(1.8f);
        ticker.DOFade(0f, 0.35f).SetId(this);
        line1.GetComponent<Image>().DOFade(0f, 0.35f).SetId(this);
        line2.GetComponent<Image>().DOFade(0f, 0.35f).SetId(this);
        yield return new WaitForSeconds(0.4f);
        Destroy(overlay.gameObject);
    }

    private void RebuildGameplayScreen()
    {
        attemptRows.Clear(); inputBoxes.Clear();
        digitButtons.Clear(); digitButtonImages.Clear();
        if (screens.TryGetValue(GameScreen.Playing, out RectTransform old))
        {
            Destroy(old.gameObject);
            screens[GameScreen.Playing] = BuildGameScreen();
            screens[GameScreen.Playing].gameObject.SetActive(false);
        }
    }

    private void RestartToIntro()
    {
        acceptingInput = false;
        if (timerRoutine != null) StopCoroutine(timerRoutine);
        // NEW: decelerate background on exit
        DOTween.To(() => backgroundSpeed, v => backgroundSpeed = v, 1f, 0.5f).SetId(this);
        tensionFloat = 0f; chromaIntensity = 0f;
        stateMachine.Transition(GameState.CardReveal);
        Show(GameScreen.Intro);
    }

    private void StartAttemptTimer(bool resetTime)
    {
        if (timerRoutine != null) StopCoroutine(timerRoutine);
        if (resetTime) timeLeft = selected.seconds;
        timerRoutine = StartCoroutine(TimerRoutine());
    }

    private IEnumerator TimerRoutine()
    {
        while (timeLeft > 0f && acceptingInput && currentScreen == GameScreen.Playing)
        {
            timeLeft -= Time.deltaTime;
            // NEW: discrete tick detection
            int currentSecond = Mathf.CeilToInt(timeLeft);
            if (currentSecond != lastTickSecond && currentSecond >= 0)
            {
                lastTickSecond = currentSecond;
                DoTimerTick(currentSecond);
            }
            RefreshHud();
            yield return null;
        }
        if (acceptingInput && currentScreen == GameScreen.Playing)
        {
            SetStatus("> TIMER EXPIRED - ELIMINATING...");
            LoseLifeOrEliminate();
        }
    }

    // NEW: physical discrete tick
    private void DoTimerTick(int secondsLeft)
    {
        if (timerRing == null) return;
        // Snap fill to integer second
        timerRing.fillAmount = selected.seconds > 0 ? secondsLeft / (float)selected.seconds : 0f;
        // Punch scale
        int ticks = secondsLeft <= 5 ? 3 : secondsLeft <= 10 ? 2 : 1;
        for (int t = 0; t < ticks; t++)
        {
            float delay = t * 0.12f;
            timerRing.transform.DOScale(1.08f, 0.06f).SetDelay(delay).SetLoops(2, LoopType.Yoyo).SetEase(Ease.OutCubic).SetId(this);
            if (timerCorona != null)
                timerCorona.transform.DOScale(1.18f, 0.06f).SetDelay(delay).SetLoops(2, LoopType.Yoyo).SetId(this);
        }
        // Tick tone
        float freq = secondsLeft <= 5 ? 880f : secondsLeft <= 10 ? 660f : 440f;
        PlayTone(freq, 0.025f, 0.03f, 0f);
    }

    private void PressDigit(int digit)
    {
        if (!acceptingInput || input.Count >= CodeLength || input.Contains(digit)) return;
        input.Add(digit);
        // NEW: track wear
        digitPressCounts.TryGetValue(digit, out int presses);
        digitPressCounts[digit] = presses + 1;
        ApplyButtonWear(digit);

        RefreshInput();
        if (inputBoxes.Count >= input.Count)
        {
            RectTransform slot = inputBoxes[input.Count - 1].root;
            slot.DOKill(); slot.localScale = Vector3.one * 0.78f;
            slot.DOScale(1f, 0.24f).SetEase(Ease.OutBack).SetId(this);
            if (inputBoxes[input.Count - 1].glowRing != null)
            {
                Image glow = inputBoxes[input.Count - 1].glowRing;
                glow.DOKill();
                glow.color = Secondary.WithA(0.55f);
                glow.DOFade(0f, 0.4f).SetId(this);
            }
        }
        // NEW: dim unused numpad keys on full input
        if (input.Count == CodeLength) DimUnusedKeys();
        PulseButton(digit);
        if (!PlaySfx(digitReveal, 0.55f)) PlayTone(DigitFrequency(digit), 0.055f, 0.045f, DigitPan(digit));
    }

    // NEW: key wear visual
    private void ApplyButtonWear(int digit)
    {
        if (!digitButtonImages.TryGetValue(digit, out Image img)) return;
        digitPressCounts.TryGetValue(digit, out int presses);
        if (presses >= 5)
        {
            // Worn: dark with bright edge
            img.DOColor(Secondary.WithA(0.22f), 0.2f).SetId(this);
            RectTransform wearEdge = img.transform.Find("Wear Edge") as RectTransform;
            if (wearEdge == null)
            {
                Image we = MakeImage("Wear Edge", img.rectTransform, TextWhite.WithA(0.14f));
                we.rectTransform.anchorMin = new Vector2(0f, 1f); we.rectTransform.anchorMax = Vector2.one;
                we.rectTransform.pivot     = new Vector2(0.5f, 1f); we.rectTransform.sizeDelta = new Vector2(0f, 2f);
            }
        }
        else if (presses >= 3)
            img.DOColor(Secondary.WithA(0.08f), 0.2f).SetId(this);
    }

    // NEW: dim keys not in current input
    private void DimUnusedKeys()
    {
        for (int d = 0; d <= 9; d++)
        {
            if (digitButtonImages.TryGetValue(d, out Image img))
            {
                bool used = input.Contains(d);
                img.DOColor(used ? img.color : img.color.WithA(0.3f), 0.15f).SetId(this);
            }
        }
    }

    private void RestoreKeyAlpha()
    {
        for (int d = 0; d <= 9; d++)
        {
            if (digitButtonImages.TryGetValue(d, out Image img))
                img.DOColor(img.color.WithA(0.13f), 0.18f).SetId(this);
        }
    }

    private void DeleteDigit()
    {
        if (!acceptingInput || input.Count == 0) return;
        input.RemoveAt(input.Count - 1);
        RefreshInput();
        RestoreKeyAlpha();
        foreach (DigitBox box in inputBoxes) Shake(box.root, 9f);
        if (!PlaySfx(uiClickSound, 0.55f)) PlayTone(108f, 0.05f, 0.04f, 0f);
    }

    private void ConfirmGuess()
    {
        if (!acceptingInput || input.Count != CodeLength) return;
        acceptingInput = false;
        if (timerRoutine != null) StopCoroutine(timerRoutine);
        StartCoroutine(ResolveGuess(input.ToList()));
    }

    private IEnumerator ResolveGuess(List<int> guess)
    {
        AttemptRow row = attemptRows[Mathf.Clamp(currentAttempt, 0, attemptRows.Count - 1)];
        row.root.anchoredPosition += new Vector2(-90f, 0f);
        row.root.DOAnchorPosX(row.root.anchoredPosition.x + 90f, 0.28f).SetEase(Ease.OutCubic).SetId(this);

        int locked = 0, signal = 0;
        for (int i = 0; i < CodeLength; i++)
        {
            Mark mark = Mark.Void;
            if (secret[i] == guess[i])          { mark = Mark.Locked; locked++; }
            else if (secret.Contains(guess[i])) { mark = Mark.Signal; signal++; }
            knownDigits[guess[i]] = StrongerMark(knownDigits.ContainsKey(guess[i]) ? knownDigits[guess[i]] : Mark.Void, mark);
            RevealBoxV3(row.boxes[i], guess[i], mark, i * 0.12f);
            AddScore(mark == Mark.Locked ? 40 : mark == Mark.Signal ? 15 : 0);
            yield return new WaitForSeconds(reducedMotion ? 0.02f : 0.14f);
        }
        currentAttempt++;
        RefreshKnownKeys();
        UpdateIntelLog(locked, signal, guess);

        if (locked == CodeLength) { yield return WinSequence(); yield break; }

        if      (locked == 4)              SetStatus("> 4 LOCKED - ONE MORE DIGIT");
        else if (locked > 0 || signal > 0) SetStatus("> NEURAL MATCH DETECTED - KEEP GOING");
        else                               SetStatus("> PATTERN MISMATCH - RECALIBRATE");

        // NEW: attempt counter trauma on last 2 attempts
        if (!attemptCounterShaken && selected.attempts - currentAttempt <= 2)
        {
            attemptCounterShaken = true;
            if (attemptCounterText != null)
            {
                attemptCounterText.rectTransform.DOShakeAnchorPos(0.3f, 8f, 18).SetId(this);
                attemptCounterText.rectTransform.DOScale(1.08f, 0.2f).SetId(this); // permanent scale-up
                attemptCounterText.color = Primary;
            }
        }

        input.Clear(); RestoreKeyAlpha(); RefreshInput();
        if (currentAttempt >= selected.attempts) { LoseLifeOrEliminate(); yield break; }
        acceptingInput = true;
        StartAttemptTimer(true);
        ApplyTension(true);
        RefreshHud();
    }

    // NEW: update intel log with dimming old entries
    private void UpdateIntelLog(int locked, int signal, List<int> guess)
    {
        string entry = "[" + currentAttempt.ToString("00") + "] " + string.Join("", guess) + " → " + locked + "♦ " + signal + "◆";
        intelLog.Insert(0, entry);
        if (intelLog.Count > 4) intelLog.RemoveAt(intelLog.Count - 1);
        if (intelLogText == null) return;
        intelLogText.text = "INTEL LOG\n";
        for (int i = 0; i < intelLog.Count; i++)
        {
            float alpha = Mathf.Lerp(0.85f, 0.3f, i / (float)Mathf.Max(1, intelLog.Count - 1));
            // TMP doesn't support per-line color easily without rich text, so we use a simple fade
            intelLogText.text += (i == 0 ? "<color=#C8DCF0>" : "<color=#7A4A52>") + intelLog[i] + "</color>\n";
        }
        intelLogText.richText = true;
    }

    private IEnumerator WinSequence()
    {
        acceptingInput = false;
        if (timerRoutine != null) StopCoroutine(timerRoutine);
        int baseScore  = (selected.attempts - currentAttempt + 1) * 150;
        int timeBonus  = Mathf.RoundToInt(Mathf.Clamp01(timeLeft / selected.seconds) * 300f);
        int final      = Mathf.Max(0, Mathf.RoundToInt((score + baseScore + timeBonus) * selected.multiplier) - hintPenalty);
        lastBaseScore = baseScore; lastTimeBonus = timeBonus; lastFinalScore = final;
        AddScore(final - score);
        FinalScore = final; GameCompleted = true; GameFailed = false; IsActive = false;

        // NEW: background shifts to victory green palette
        StartCoroutine(ShiftBackgroundHue(SafeGreen, 1.5f));

        RectTransform overlay = CinematicOverlay("Win Cinematic", Color.black.WithA(0.22f));
        TextMeshProUGUI title  = TMP("Cin Title",  overlay, string.Empty, 68, SafeGreen, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.rectTransform.SetBox(0.5f, 0.66f, 1000f, 126f);
        TextMeshProUGUI detail = TMP("Cin Detail", overlay, string.Empty, 36, TextWhite, TextAlignmentOptions.Center);
        detail.rectTransform.SetBox(0.5f, 0.42f, 920f, 175f);

        for (int i = 0; i < 3; i++) { Flash(TextWhite, 0.07f); yield return new WaitForSeconds(0.11f); }

        // NEW: holographic card scan on win
        yield return StartCoroutine(HolographicScan(overlay));

        Image wave = MakeImage("Green Wave", overlay, SafeGreen.WithA(0.3f));
        wave.rectTransform.anchorMin = wave.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        wave.rectTransform.sizeDelta = new Vector2(180f, ReferenceHeight);
        wave.rectTransform.anchoredPosition = new Vector2(-140f, 0f);
        wave.rectTransform.DOAnchorPosX(ReferenceWidth + 140f, 0.42f).SetEase(Ease.InOutCubic).SetId(this);

        if (!PlaySfx(winJingle, 0.9f)) PlayMelody(new[] { 262f, 294f, 330f, 349f, 392f, 440f, 523f }, 0.07f, 0.06f);
        yield return new WaitForSeconds(0.42f);
        Flash(TextWhite, 0.2f);
        SpawnLightRays(overlay, SafeGreen, 12);
        yield return TypeOverwrite(title, "CODE CRACKED", 0.022f);
        yield return TypeOverwrite(detail, "SECRET CODE: " + string.Join("  ", secret), 0.028f);
        AnimateScoreText(detail, 0, final, "FINAL SCORE: ");
        SpawnVictoryRain(overlay, SafeGreen, 40);

        // NEW: post-win attempt history replay
        yield return StartCoroutine(ReplayAttemptHistory());

        yield return new WaitForSeconds(0.65f);
        title.text  = "SURVIVAL CONFIRMED"; title.color = TextWhite;
        SpawnBurst(Pink, 32, new Vector2(0f, 500f), true);
        SpawnDiamondFragments(SafeGreen, 42, Vector2.zero);
        yield return new WaitForSeconds(0.42f);
        yield return FlipCard(title.rectTransform, () => title.text = CardShortLabel() + " DIAMOND — GAME CLEARED");
        yield return new WaitForSeconds(0.22f);
        Destroy(overlay.gameObject);

        gamesPlayed++; gamesWon++; survivalStreak++;
        bestScore = Mathf.Max(bestScore, final);
        totalHintsUsed += hintsUsedThisGame;
        AddHistory("CLEARED", final);
        SaveData();
        stateMachine.Transition(GameState.Win);
        Show(GameScreen.Win);
        RefreshResultScreen(GameScreen.Win);

        // Retour automatique à la scène principale après victoire
        StartCoroutine(ReturnToMainScene(3.5f));
    }

    // NEW: holographic card scan effect
    private IEnumerator HolographicScan(RectTransform parent)
    {
        Image scanner = MakeImage("Holo Scan", parent, TextWhite.WithA(0.12f));
        scanner.rectTransform.anchorMin = new Vector2(0f, 1f);
        scanner.rectTransform.anchorMax = Vector2.one;
        scanner.rectTransform.pivot     = new Vector2(0.5f, 1f);
        scanner.rectTransform.sizeDelta = new Vector2(0f, 8f);
        scanner.rectTransform.anchoredPosition = new Vector2(0f, 0f);
        scanner.rectTransform.DOAnchorPosY(-ReferenceHeight, 0.6f).SetEase(Ease.Linear).SetId(this);
        scanner.DOFade(0f, 0.6f).SetId(this);
        yield return new WaitForSeconds(0.65f);
        if (scanner != null) Destroy(scanner.gameObject);
    }

    // NEW: replay attempt rows after win
    private IEnumerator ReplayAttemptHistory()
    {
        if (!screens.TryGetValue(GameScreen.Playing, out RectTransform ps)) yield break;
        for (int i = 0; i < Mathf.Min(currentAttempt, attemptRows.Count); i++)
        {
            AttemptRow row = attemptRows[i];
            Image rowGlow = row.root.Find("Row Active Glow") as RectTransform != null
                ? (row.root.Find("Row Active Glow") as RectTransform).GetComponent<Image>() : null;
            if (rowGlow != null)
            {
                rowGlow.DOColor(SafeGreen.WithA(0.18f), 0.12f).SetId(this);
                rowGlow.DOFade(0.03f, 0.3f).SetDelay(0.14f).SetId(this);
            }
            yield return new WaitForSeconds(reducedMotion ? 0.04f : 0.15f);
        }
    }

    // NEW: shift background hue toward a target color
    private IEnumerator ShiftBackgroundHue(Color targetColor, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            foreach (Image img in hexCellImages)
            {
                if (img == null) continue;
                img.color = Color.Lerp(img.color, targetColor.WithA(img.color.a * 0.7f), t * 0.04f);
            }
            foreach (Image img in fogImages)
            {
                if (img == null) continue;
                img.color = Color.Lerp(img.color, targetColor.WithA(img.color.a), t * 0.02f);
            }
            yield return null;
        }
    }

    // NEW: progressive corruption on elimination
    private IEnumerator CorruptionSequence()
    {
        // Background first
        yield return ShiftBackgroundHue(Color.gray, 0.8f);
        yield return new WaitForSeconds(0.1f);
        // Then HUD
        if (screens.TryGetValue(GameScreen.Playing, out RectTransform ps))
        {
            CanvasGroup cg = ps.GetComponent<CanvasGroup>();
            if (cg == null) cg = ps.gameObject.AddComponent<CanvasGroup>();
            cg.DOFade(0.3f, 0.3f).SetId(this);
            yield return new WaitForSeconds(0.15f);
        }
    }

    private void LoseLifeOrEliminate() { acceptingInput = false; if (timerRoutine != null) StopCoroutine(timerRoutine); StartCoroutine(LoseSequence()); }

    private IEnumerator LoseSequence()
    {
        lives--;
        bool eliminated = lives <= 0;

        if (eliminated)
            StartCoroutine(CorruptionSequence());

        RectTransform overlay = CinematicOverlay("Lose Cinematic", Color.black.WithA(eliminated ? 0.92f : 0.28f));
        TextMeshProUGUI title  = TMP("Lose Title",  overlay, string.Empty, 72, Primary, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 6f;
        title.rectTransform.SetBox(0.5f, 0.62f, 1020f, 126f);
        TextMeshProUGUI detail = TMP("Lose Detail", overlay, string.Empty, 34, TextWhite, TextAlignmentOptions.Center);
        detail.rectTransform.SetBox(0.5f, 0.42f, 1000f, 175f);

        // NEW: radial elimination vignette from input center
        if (eliminated)
        {
            Image radVig = MakeImage("Elimination Vignette", overlay, Color.black.WithA(0f));
            radVig.sprite = circleSprite;
            radVig.rectTransform.anchorMin = radVig.rectTransform.anchorMax = new Vector2(0.5f, 0.55f);
            radVig.rectTransform.sizeDelta = new Vector2(60f, 60f);
            // Invert: scale outward while alpha fades in
            radVig.DOFade(0.88f, 0.4f).SetId(this);
            radVig.rectTransform.DOSizeDelta(new Vector2(2400f, 1800f), 0.4f).SetEase(Ease.OutCubic).SetId(this);
        }

        // NEW: lives diamond shatter on life loss
        if (!eliminated && livesText != null)
            StartCoroutine(ShatterLifeDiamond());

        int flashes = eliminated ? 6 : 2;
        for (int i = 0; i < flashes; i++)
        {
            Flash(Primary, 0.07f);
            if (!PlaySfx(loseSound, 0.65f)) PlayTone(90f, 0.055f, 0.08f, 0f);
            yield return new WaitForSeconds(0.1f);
        }

        if (eliminated)
        {
            ScreenGlitch(1.0f, Primary);
            SpawnStaticNoise(overlay);
            title.text = "✕";
            title.transform.localScale = Vector3.zero;
            title.transform.DOScale(2.2f, 0.34f).SetEase(Ease.OutBack).SetId(this);
            yield return new WaitForSeconds(0.38f);
            yield return TypeOverwrite(title, "ELIMINATION", 0.022f);
            yield return TypeOverwrite(detail, "YOU HAVE BEEN ELIMINATED FROM BORDERLAND\nTHE CODE WAS " + string.Join("", secret), 0.022f);
            SpawnSignalLoss(overlay);
        }
        else
        {
            yield return TypeOverwrite(title, "ATTEMPT FAILED", 0.022f);
            yield return TypeOverwrite(detail, "THE CODE WAS: " + string.Join("", secret) + "\nLIVES REMAINING: " + LifeSymbols(), 0.022f);
        }

        Shake(screens[GameScreen.Playing], eliminated ? 46f : 28f);
        SpawnBurst(Primary, eliminated ? 40 : 20, new Vector2(0f, 120f), true);
        if (!PlaySfx(loseSound, 0.9f)) PlayMelody(new[] { 392f, 330f, 294f, 220f }, 0.09f, 0.06f);
        yield return new WaitForSeconds(reducedMotion ? 0.08f : 0.52f);
        Destroy(overlay.gameObject);

        gamesPlayed++; gamesLost++; survivalStreak = 0;
        totalHintsUsed += hintsUsedThisGame;
        AddHistory(eliminated ? "ELIMINATED" : "FAILED", score);
        SaveData();

        if (eliminated)
        {
            GameFailed = true; GameCompleted = false; IsActive = false;
            stateMachine.Transition(GameState.Eliminated);
            Show(GameScreen.Eliminated);
            RefreshResultScreen(GameScreen.Eliminated);
        }
        else
        {
            stateMachine.Transition(GameState.Lose);
            Show(GameScreen.Lose);
            RefreshResultScreen(GameScreen.Lose);
        }

        // Retour automatique à la scène principale après défaite
        StartCoroutine(ReturnToMainScene(4.0f));
    }

    // NEW: life diamond shatter
    private IEnumerator ShatterLifeDiamond()
    {
        if (livesText == null) yield break;
        livesText.transform.DOScale(1.38f, 0.1f).SetLoops(2, LoopType.Yoyo).SetId(this);
        Vector2 pos = ToRootPosition(livesText.rectTransform);
        SpawnBurst(Primary, 8, pos, false);
        SpawnDiamondFragments(Primary, 6, pos);
        yield return new WaitForSeconds(0.22f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HINTS
    // ─────────────────────────────────────────────────────────────────────────
    private void UseWhisper()
    {
        if (!acceptingInput || whisperUsed) return;
        whisperUsed = true; hintsUsedThisGame++; hintPenalty += 200;
        int digit = secret[rng.Next(secret.Count)];
        SetStatus("> WHISPER: Digit " + digit + " exists inside the code.");
        AddScore(-Mathf.Min(score, 200));
        PlayTone(740f, 0.12f, 0.045f, 0f);
        RefreshHud();
    }
    private void UseVision()
    {
        if (!acceptingInput || visionUsed) return;
        visionUsed = true; hintsUsedThisGame++; hintPenalty += 300;
        List<int> absent = Enumerable.Range(0, DigitCount).Where(d => !secret.Contains(d)).OrderBy(_ => rng.Next()).Take(3).ToList();
        foreach (int d in absent) knownDigits[d] = Mark.Void;
        SetStatus("> VISION: Void digits identified: " + string.Join(", ", absent));
        AddScore(-Mathf.Min(score, 300)); RefreshKnownKeys(); RefreshHud();
    }
    private void UseRevelation()
    {
        if (!acceptingInput || revelationUsed) return;
        revelationUsed = true; hintsUsedThisGame++; hintPenalty += 500;
        int index = rng.Next(secret.Count);
        SetStatus("> REVELATION: Position " + (index + 1) + " contains digit " + secret[index] + ".");
        AddScore(-Mathf.Min(score, 500));
        PlayTone(880f, 0.16f, 0.05f, 0f); RefreshHud();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAUSE / RESUME
    // ─────────────────────────────────────────────────────────────────────────
    private void PauseGame()
    {
        if (currentScreen != GameScreen.Playing) return;
        acceptingInput = false;
        if (timerRoutine != null) StopCoroutine(timerRoutine);
        // NEW: decelerate background
        DOTween.To(() => backgroundSpeed, v => backgroundSpeed = v, 0.15f, 0.5f).SetId(this);
        if (musicSource != null) musicSource.volume = 0.02f;
        stateMachine.Transition(GameState.Paused);
        Show(GameScreen.Pause);
        RefreshPauseScreen();
    }
    private void ResumeGame()
    {
        acceptingInput = true;
        // NEW: reaccelerate background to tension level
        float targetSpeed = Mathf.Lerp(1f, 2.4f, tensionFloat);
        DOTween.To(() => backgroundSpeed, v => backgroundSpeed = v, targetSpeed, 0.5f).SetId(this);
        if (musicSource != null) musicSource.volume = 0.08f;
        stateMachine.Transition(GameState.Playing);
        Show(GameScreen.Playing);
        StartAttemptTimer(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KEYBOARD
    // ─────────────────────────────────────────────────────────────────────────
    private void HandleKeyboard()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame)
        { if (currentScreen == GameScreen.Playing) PauseGame(); else if (currentScreen == GameScreen.Pause) ResumeGame(); }
        if (currentScreen != GameScreen.Playing || !acceptingInput) return;
        Key[] digitKeys = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        for (int i = 0; i <= 9; i++) if (kb[digitKeys[i]].wasPressedThisFrame) PressDigit(i);
        if (kb.backspaceKey.wasPressedThisFrame) DeleteDigit();
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) ConfirmGuess();
        if (kb.hKey.wasPressedThisFrame) { if (!whisperUsed) UseWhisper(); else if (!visionUsed) UseVision(); else UseRevelation(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REFRESH FUNCTIONS
    // ─────────────────────────────────────────────────────────────────────────
    private void RefreshInput()
    {
        for (int i = 0; i < inputBoxes.Count; i++)
        {
            bool filled = i < input.Count;
            inputBoxes[i].label.text  = filled ? input[i].ToString() : "_";
            inputBoxes[i].label.color = filled ? TextWhite : Dim;
            inputBoxes[i].image.color = filled ? Dark.WithA(0.96f) : Dark.WithA(0.72f);
            if (inputBoxes[i].cursor != null)
                inputBoxes[i].cursor.gameObject.SetActive(!filled);
        }
        if (confirmButton != null)
        {
            bool ready = input.Count == CodeLength;
            confirmButton.interactable = ready;
            Image img = confirmButton.GetComponent<Image>();
            img.DOKill();
            img.DOColor((ready ? SafeGreen : BlackMark).WithA(0.38f), 0.16f).SetId(this);
            if (ready && !reducedMotion)
            {
                // NEW: lock-in slide-down-then-spring
                RectTransform crt = confirmButton.GetComponent<RectTransform>();
                Vector2 origPos = crt.anchoredPosition;
                DOTween.Sequence().SetId(this)
                    .Append(crt.DOAnchorPosY(origPos.y - 3f, 0.06f).SetEase(Ease.InCubic))
                    .Append(crt.DOAnchorPosY(origPos.y, 0.24f).SetEase(Ease.OutBack));
                img.transform.DOScale(1.05f, 0.3f).SetLoops(4, LoopType.Yoyo).SetId(this);
            }
        }
    }

    private void RefreshHud()
    {
        if (!screens.ContainsKey(GameScreen.Playing)) return;
        TextMeshProUGUI[] labels = screens[GameScreen.Playing].GetComponentsInChildren<TextMeshProUGUI>(true);
        SetText(labels, "Timer",           Mathf.CeilToInt(timeLeft).ToString());
        SetText(labels, "Lives",           LifeSymbols());
        SetText(labels, "Score",           Mathf.Max(0, shownScore).ToString());
        SetText(labels, "Attempt Counter", "[" + Mathf.Min(currentAttempt + 1, selected.attempts) + "/" + selected.attempts + "]");
        if (statusText == null) SetText(labels, "System Line", systemLine);

        float ratio      = selected.seconds <= 0 ? 0f : Mathf.Clamp01(timeLeft / selected.seconds);
        Color timerColor = ratio > 0.5f ? SafeGreen : ratio > 0.25f ? Warning : Primary;

        if (timerRing != null)
        {
            // Discrete tick handles fillAmount; only color here
            timerRing.color = timerColor;
            if (timeLeft <= 10f && currentScreen == GameScreen.Playing && acceptingInput)
                timerRing.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 13f) * 0.055f);
            else
                timerRing.transform.localScale = Vector3.one;
        }
        if (timerCorona != null)
        {
            // NEW: throb even at full health (subtle 6% baseline)
            float basePulse  = 0.06f + Mathf.Sin(Time.unscaledTime * 2f) * 0.02f;
            float coronaPulse = timeLeft <= 10f ? 0.18f + Mathf.Sin(Time.unscaledTime * 9f) * 0.1f : basePulse;
            timerCorona.color = timerColor.WithA(coronaPulse);
            timerCorona.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.04f);
        }
        if (pressureFill != null && selected.attempts > 1)
        {
            float p = (float)currentAttempt / (selected.attempts - 1);
            pressureFill.rectTransform.anchorMax = new Vector2(p, 1f);
            pressureFill.color = Color.Lerp(SafeGreen, Primary, p).WithA(0.75f);
        }
        TextMeshProUGUI timer = timerText != null ? timerText : labels.FirstOrDefault(t => t.name == "Timer");
        if (timer != null) timer.color = timerColor;
        if (livesText != null && currentScreen == GameScreen.Playing)
            livesText.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 6.28f) * 0.032f);
    }

    private void RefreshKnownKeys()
    {
        foreach (KeyValuePair<int, Image> pair in digitButtonImages)
        {
            if (!knownDigits.TryGetValue(pair.Key, out Mark mark))
            {
                pair.Value.color = Secondary.WithA(0.13f);
                SetKeyStrike(pair.Value.rectTransform, false);
                continue;
            }
            pair.Value.DOColor(MarkColor(mark).WithA(mark == Mark.Void ? 0.22f : 0.34f), 0.22f).SetId(this);
            SetKeyStrike(pair.Value.rectTransform, mark == Mark.Void);
        }
    }

    private void SetKeyStrike(RectTransform key, bool visible)
    {
        if (key == null) return;
        RectTransform strike = key.Find("Strike") as RectTransform;
        if (strike == null)
        {
            strike = MakeImage("Strike", key, Primary.WithA(0.78f)).rectTransform;
            strike.anchorMin = strike.anchorMax = new Vector2(0.5f, 0.5f);
            strike.sizeDelta = new Vector2(74f, 3f);
            strike.localRotation = Quaternion.Euler(0f, 0f, -12f);
        }
        strike.gameObject.SetActive(visible);
    }

    private void RefreshResultScreen(GameScreen screen)
    {
        RectTransform s = screens[screen];
        TextMeshProUGUI body = s.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Result Body");
        string code = string.Join("", secret);
        if (screen == GameScreen.Win)
        {
            bool record = FinalScore >= bestScore && gamesWon > 0;
            body.color  = TextWhite;
            body.text   = CardShortLabel() + " DIAMOND CARD — GAME CLEARED\n"
                        + "BASE SCORE:      " + lastBaseScore + " pts\n"
                        + "TIME BONUS:     +" + lastTimeBonus + " pts\n"
                        + "DIFFICULTY:      x" + selected.multiplier.ToString("0.0") + "\n"
                        + "HINT PENALTIES: -" + hintPenalty + " pts\n"
                        + "FINAL SCORE:     " + FinalScore + " pts\n"
                        + "RANK: " + RankName(FinalScore) + (record ? "\n★ NEW RECORD!" : "");
        }
        else if (screen == GameScreen.Eliminated)
            body.text = "YOU HAVE BEEN ELIMINATED FROM BORDERLAND\nTHE CODE WAS: " + code + "\nBETTER LUCK IN YOUR NEXT LIFE";
        else
            body.text = "Remaining lives: " + LifeSymbols() + "\nThe code was: " + code + "\nScore: " + score + "\nTry again before the system closes in.";
    }

    private void RefreshStatsScreen()
    {
        RectTransform s = screens[GameScreen.Stats];
        TextMeshProUGUI body = s.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Stats Body");
        float rate = gamesPlayed == 0 ? 0f : gamesWon * 100f / gamesPlayed;
        string text = "Games played:  " + gamesPlayed + "\nGames cleared: " + gamesWon
            + "\nGames failed:  " + gamesLost + "\nClear rate:    " + rate.ToString("0.0") + "%"
            + "\nBest score:    " + bestScore + "\nCurrent rank:  " + RankName(bestScore)
            + "\nTotal hints:   " + totalHintsUsed + "\nSurvival streak: " + survivalStreak
            + "\n\nLAST 10 GAMES\n";
        foreach (HistoryEntry h in history.Take(MaxHistory))
            text += h.date + " | " + h.difficulty + " | " + h.result + " | score " + h.score + "\n";
        body.text = text;
    }

    private void RefreshSettingsScreen()
    {
        RectTransform s = screens[GameScreen.Settings];
        TextMeshProUGUI body = s.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Settings Body");
        body.text = "Sound: " + OnOff(soundEnabled) + "    Music: " + OnOff(musicEnabled) + "    Shake: " + OnOff(shakeEnabled)
                  + "\nReduced FX: " + OnOff(reducedMotion) + "    Colorblind: " + OnOff(colorblind) + "    Large text: " + OnOff(largeText);
    }

    private void RefreshPauseScreen()
    {
        if (!screens.TryGetValue(GameScreen.Pause, out RectTransform pause)) return;
        TextMeshProUGUI summary = FindText(pause, "Pause Summary");
        if (summary != null)
            summary.text = "Card: " + CardShortLabel() + "   Attempts: " + Mathf.Min(currentAttempt + 1, selected.attempts) + "/" + selected.attempts
                         + "\nInput: " + (input.Count == 0 ? "-----" : string.Join("", input))
                         + "\nLives: " + LifeSymbols() + "   Score: " + score;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SHOW / SCREEN TRANSITIONS
    // ─────────────────────────────────────────────────────────────────────────
    private void Show(GameScreen next, bool animated = true)
    {
        currentScreen = next;
        foreach (KeyValuePair<GameScreen, RectTransform> pair in screens)
            pair.Value.gameObject.SetActive(pair.Key == next);
        if (next == GameScreen.Stats)    RefreshStatsScreen();
        if (next == GameScreen.Settings) RefreshSettingsScreen();
        RefreshHud();
        if (!animated || reducedMotion) return;
        screenFade.alpha = 0.1f;
        screenFade.DOFade(1f, 0.2f).SetId(this);
        ScreenGlitch(0.16f, next == GameScreen.Win ? SafeGreen : Primary);
        StartCoroutine(RgbSplitOnScreen(0.25f));
    }

    private IEnumerator RgbSplitOnScreen(float duration)
    {
        if (chromaR == null) yield break;
        float peak = 0.025f;
        chromaR.color = new Color(1f, 0f, 0f, peak);
        chromaB.color = new Color(0f, 0f, 1f, peak);
        chromaR.rectTransform.offsetMin = new Vector2(-8f, 0f);
        chromaB.rectTransform.offsetMin = new Vector2( 8f, 0f);
        yield return new WaitForSeconds(duration);
        chromaR.color = new Color(1f, 0f, 0f, 0f);
        chromaB.color = new Color(0f, 0f, 1f, 0f);
        chromaR.rectTransform.offsetMin = chromaR.rectTransform.offsetMax = Vector2.zero;
        chromaB.rectTransform.offsetMin = chromaB.rectTransform.offsetMax = Vector2.zero;
    }

    private IEnumerator RgbSplitEffect(RectTransform target, float duration)
    {
        if (target == null) yield break;
        target.DOAnchorPosX(target.anchoredPosition.x + 6f, 0.04f).SetLoops(4, LoopType.Yoyo).SetId(this);
        yield return new WaitForSeconds(duration);
    }

    private void ScreenGlitch(float duration, Color color)
    {
        int bars = 10;
        for (int i = 0; i < bars; i++)
        {
            Image img = MakeImage("Transition Glitch", fxLayer, color.WithA(UnityEngine.Random.Range(0.06f, 0.24f)));
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, UnityEngine.Random.Range(0.08f, 0.94f));
            rt.sizeDelta = new Vector2(UnityEngine.Random.Range(200f, 1200f), UnityEngine.Random.Range(3f, 18f));
            rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-500f, 500f), 0f);
            rt.DOAnchorPosX(rt.anchoredPosition.x + UnityEngine.Random.Range(-240f, 240f), duration).SetEase(Ease.OutCubic).SetId(this);
            img.DOFade(0f, duration).SetId(this).OnComplete(() => Destroy(img.gameObject));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REVEAL BOX V3 — full material response
    // ─────────────────────────────────────────────────────────────────────────
    private void RevealBoxV3(DigitBox box, int digit, Mark mark, float delay)
    {
        Sequence seq = DOTween.Sequence().SetId(this).SetDelay(delay);
        seq.Append(box.root.DOScaleX(0f, 0.11f).SetEase(Ease.InQuad));
        seq.AppendCallback(() =>
        {
            box.label.text  = digit.ToString();
            box.label.color = TextWhite;
            box.mark.text   = MarkSymbol(mark);
            box.mark.color  = MarkColor(mark);
            box.image.color = MarkColor(mark).WithA(mark == Mark.Void ? 0.24f : 0.44f);
            if (box.cursor != null) box.cursor.gameObject.SetActive(false);

            // Top bevel color response
            if (box.topBevel != null)
                box.topBevel.DOColor(mark == Mark.Locked ? SafeGreen.WithA(0.25f) : TextWhite.WithA(0.10f), 0.22f).SetId(this);

            // Glow ring
            if (box.glowRing != null)
            {
                Color gc = MarkColor(mark);
                box.glowRing.DOKill();
                box.glowRing.color = gc.WithA(mark == Mark.Locked ? 0.8f : mark == Mark.Signal ? 0.5f : 0.15f);
                box.glowRing.DOFade(0f, 0.55f).SetId(this);
                box.glowRing.transform.DOScale(mark == Mark.Locked ? 1.8f : 1.4f, 0.45f).SetEase(Ease.OutCubic).SetId(this);
            }

            if (mark == Mark.Locked)
            {
                // Ripple ring
                Image ripple = MakeImage("Ripple", fxLayer, SafeGreen.WithA(0.55f));
                ripple.sprite = circleSprite;
                Vector2 worldPos = ToRootPosition(box.root);
                ripple.rectTransform.anchorMin = ripple.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                ripple.rectTransform.anchoredPosition = worldPos;
                ripple.rectTransform.sizeDelta = new Vector2(60f, 60f);
                ripple.rectTransform.DOSizeDelta(new Vector2(140f, 140f), 0.5f).SetEase(Ease.OutCubic).SetId(this);
                ripple.DOFade(0f, 0.5f).SetId(this).OnComplete(() => Destroy(ripple.gameObject));
                // NEW: subliminal screen flash on LOCKED
                Flash(SafeGreen.WithA(0.06f), 0.08f);
                box.root.DOScale(1.14f, 0.15f).SetLoops(2, LoopType.Yoyo).SetId(this);
                SpawnBurst(SafeGreen, 9, ToRootPosition(box.root), false);
                if (!PlaySfx(correctSound, 0.75f)) PlayTone(880f, 0.12f, 0.055f, 0f);
            }
            else if (mark == Mark.Signal)
            {
                box.image.DOColor(Warning.WithA(0.68f), 0.07f).SetLoops(7, LoopType.Yoyo).SetId(this);
                if (!PlaySfx(digitReveal, 0.55f)) PlayTone(520f, 0.12f, 0.04f, 0f);
            }
            else if (!PlaySfx(wrongSound, 0.65f)) PlayTone(148f, 0.1f, 0.04f, 0f);
        });
        seq.Append(box.root.DOScaleX(1f, 0.16f).SetEase(Ease.OutBack));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DANGER FEEDBACK
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateDangerFeedback()
    {
        float timerRatio = selected.seconds <= 0 ? 0f : Mathf.Clamp01(timeLeft / selected.seconds);
        if (timerRatio <= 0.5f && timerRatio > 0.25f && Time.frameCount % 88 == 0)
            ScreenGlitch(0.07f, Warning.WithA(0.5f));
        else if (timerRatio <= 0.25f && timerRatio > 0.1f && Time.frameCount % 52 == 0)
        {
            ScreenGlitch(0.11f, Primary.WithA(0.62f));
            if (vignetteInner != null) vignetteInner.DOColor(Primary.WithA(Mathf.Max(0.38f, vignetteInner.color.a)), 0.1f).SetLoops(2, LoopType.Yoyo).SetId(this);
        }
        else if (timerRatio <= 0.1f && timerRatio > 0.05f && Time.frameCount % 28 == 0)
        {
            Flash(Primary.WithA(0.18f), 0.06f);
            ScreenGlitch(0.14f, Primary);
        }
        else if (timerRatio <= 0.05f && Time.frameCount % 16 == 0)
        {
            Flash(Primary.WithA(0.38f), 0.07f);
            SpawnBurst(Primary, 4, new Vector2(UnityEngine.Random.Range(-720f, 720f), UnityEngine.Random.Range(-380f, 380f)), false);
        }

        bool nearDeath = lives <= 1 && timeLeft <= 10f;
        if (nearDeath)
        {
            if (vignetteInner != null) vignetteInner.color = Primary.WithA(0.75f + Mathf.Sin(Time.unscaledTime * 7.5f) * 0.08f);
            if (screenLayer != null && !reducedMotion)
                screenLayer.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 5.8f) * 0.007f);
            if (warningText != null) { warningText.gameObject.SetActive(true); warningText.text = "NEAR DEATH MODE"; }
        }
        else
        {
            if (screenLayer != null) screenLayer.localScale = Vector3.one;
        }

        if (Time.unscaledTime >= nextHeartbeat)
        {
            float atP  = selected.attempts <= 1 ? 0f : currentAttempt / (float)(selected.attempts - 1);
            float tiP  = Mathf.InverseLerp(selected.seconds, 0f, timeLeft);
            float pressure = Mathf.Max(atP, timeLeft <= 10f ? tiP : 0f);
            PlayTone(Mathf.Lerp(70f, 108f, pressure), 0.045f, Mathf.Lerp(0.032f, 0.09f, pressure), 0f);
            if (timerCorona != null && !reducedMotion)
            {
                timerCorona.DOKill();
                timerCorona.color = timerRing != null ? timerRing.color.WithA(0.55f) : Primary.WithA(0.55f);
                timerCorona.DOFade(0.04f, Mathf.Lerp(1.1f, 0.24f, pressure)).SetId(this);
            }
            nextHeartbeat = Time.unscaledTime + Mathf.Lerp(1.05f, 0.2f, pressure);
        }
        if (timeLeft <= 5f && Time.frameCount % 16 == 0) Flash(Primary.WithA(0.38f), 0.07f);
        if (lastTensionLevel >= 5 && screens.ContainsKey(GameScreen.Playing))
            Shake(screens[GameScreen.Playing], 2.5f);
    }

    private void UpdateMusicPulse()
    {
        if (!musicEnabled || currentScreen != GameScreen.Playing || Time.frameCount % 34 != 0) return;
        float urgency = Mathf.InverseLerp(selected.seconds, 0f, timeLeft);
        PlayTone(Mathf.Lerp(92f, 148f, urgency), 0.033f, 0.017f, -0.25f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TENSION SYSTEM
    // ─────────────────────────────────────────────────────────────────────────
    private void ApplyTension(bool announce)
    {
        int level = Mathf.Clamp(currentAttempt + 1, 1, 5);
        if (level == lastTensionLevel && !announce) return;
        lastTensionLevel = level;
        float alpha = level == 1 ? 0.08f : level == 2 ? 0.18f : level == 3 ? 0.32f : level == 4 ? 0.48f : 0.62f;
        backgroundSpeed = level == 1 ? 1f : level == 2 ? 1.25f : level == 3 ? 1.5f : level == 4 ? 1.85f : 2.3f;
        if (vignetteInner != null) vignetteInner.DOColor(Primary.WithA(alpha), 0.32f).SetId(this);
        if (warningText != null)
        {
            bool show = level >= 4;
            warningText.gameObject.SetActive(show);
            warningText.text = level >= 5 ? "ELIMINATION IMMINENT" : "FINAL ATTEMPTS";
            if (show) { warningText.DOKill(); warningText.color = Primary; warningText.transform.localScale = Vector3.one; warningText.transform.DOScale(1.09f, 0.32f).SetLoops(7, LoopType.Yoyo).SetId(this); }
        }
        if (level >= 3)
        {
            foreach (Image img in flickerBorders)
                if (img != null) { img.DOKill(); img.DOColor(Primary.WithA(level >= 4 ? 0.32f : 0.52f), 0.1f).SetLoops(7, LoopType.Yoyo).SetId(this); }
        }
        if (!announce) return;
        if      (level == 2) SetStatus("> WARNING: Pattern analysis ongoing");
        else if (level == 3) SetStatus("> ALERT: Time is running out");
        else if (level == 4) SetStatus("> CRITICAL: One wrong move and you're done");
        else if (level == 5) SetStatus("> FINAL ATTEMPT - SURVIVE OR BE ELIMINATED");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCORE + HISTORY
    // ─────────────────────────────────────────────────────────────────────────
    private void AddScore(int amount)
    {
        score = Mathf.Max(0, score + amount);
        DOTween.To(() => lastScoreTweenValue, v =>
        {
            lastScoreTweenValue = v;
            shownScore = Mathf.RoundToInt(v);
            RefreshHud();
        }, score, 0.32f).SetEase(Ease.OutCubic).SetId(this);

        // NEW: score flash on increment
        if (amount > 0 && scoreText != null)
        {
            scoreText.DOKill(false);
            scoreText.color = Color.white;
            scoreText.DOColor(NeonGold, 0.08f).SetId(this);
        }
    }

    private void AddHistory(string result, int finalScore)
    {
        history.Insert(0, new HistoryEntry { date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), difficulty = selected.title, result = result, score = finalScore, attempts = currentAttempt, time = selected.seconds - timeLeft, hints = hintsUsedThisGame });
        while (history.Count > MaxHistory) history.RemoveAt(history.Count - 1);
    }
    // ─────────────────────────────────────────────────────────────────────────
    // RETURN TO MAIN SCENE
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator ReturnToMainScene(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Fade out
        Image fadeOut = MakeImage("Scene Fade Out", modalLayer, Color.black.WithA(0f));
        fadeOut.rectTransform.Fill();
        fadeOut.raycastTarget = true;
        fadeOut.DOFade(1f, 0.8f).SetId(this);
        yield return new WaitForSeconds(0.9f);

        // Reset static state
        IsActive = false;
        DOTween.KillAll();

        // Retour à la scène principale (Build Index 2)
        SceneManager.LoadScene(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAVE / LOAD / SETTINGS
    // ─────────────────────────────────────────────────────────────────────────
    private void LoadData()
    {
        try
        {
            bestScore      = PlayerPrefs.GetInt(PrefPrefix + "BestScore",  0);
            gamesPlayed    = PlayerPrefs.GetInt(PrefPrefix + "GamesPlayed",0);
            gamesWon       = PlayerPrefs.GetInt(PrefPrefix + "GamesWon",   0);
            gamesLost      = PlayerPrefs.GetInt(PrefPrefix + "GamesLost",  0);
            totalHintsUsed = PlayerPrefs.GetInt(PrefPrefix + "Hints",      0);
            survivalStreak = PlayerPrefs.GetInt(PrefPrefix + "Streak",     0);
            soundEnabled   = PlayerPrefs.GetInt(PrefPrefix + "Sound",   1) == 1;
            musicEnabled   = PlayerPrefs.GetInt(PrefPrefix + "Music",   1) == 1;
            shakeEnabled   = PlayerPrefs.GetInt(PrefPrefix + "Shake",   1) == 1;
            reducedMotion  = PlayerPrefs.GetInt(PrefPrefix + "Reduced", 0) == 1;
            colorblind     = PlayerPrefs.GetInt(PrefPrefix + "Colorblind", 0) == 1;
            largeText      = PlayerPrefs.GetInt(PrefPrefix + "LargeText",  0) == 1;
            ApplyForcedDifficulty();
            string json = PlayerPrefs.GetString(PrefPrefix + "History", string.Empty);
            if (!string.IsNullOrEmpty(json)) { HistorySave save = JsonUtility.FromJson<HistorySave>(json); if (save?.entries != null) history.AddRange(save.entries); }
        }
        catch (Exception ex) { Debug.LogWarning("DiamondGame save recovered: " + ex.Message); history.Clear(); }
    }

    private void SaveData()
    {
        try
        {
            PlayerPrefs.SetInt(PrefPrefix + "BestScore",  bestScore);
            PlayerPrefs.SetInt(PrefPrefix + "GamesPlayed", gamesPlayed);
            PlayerPrefs.SetInt(PrefPrefix + "GamesWon",   gamesWon);
            PlayerPrefs.SetInt(PrefPrefix + "GamesLost",  gamesLost);
            PlayerPrefs.SetInt(PrefPrefix + "Hints",      totalHintsUsed);
            PlayerPrefs.SetInt(PrefPrefix + "Streak",     survivalStreak);
            PlayerPrefs.SetInt(PrefPrefix + "Sound",   soundEnabled  ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "Music",   musicEnabled  ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "Shake",   shakeEnabled  ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "Reduced", reducedMotion ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "Colorblind", colorblind ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "LargeText",  largeText  ? 1 : 0);
            PlayerPrefs.SetString(PrefPrefix + "History", JsonUtility.ToJson(new HistorySave { entries = history }));
            PlayerPrefs.Save();
        }
        catch (Exception ex) { Debug.LogWarning("DiamondGame save failed: " + ex.Message); }
    }

    private void ResetAllData()
    {
        bestScore = gamesPlayed = gamesWon = gamesLost = totalHintsUsed = survivalStreak = 0;
        history.Clear(); SaveData(); RefreshSettingsScreen();
        systemLine = "> SYSTEM: Archives erased.";
        PlayTone(88f, 0.12f, 0.04f, 0f);
    }

    private void ToggleSetting(ref bool setting) { setting = !setting; SaveData(); RefreshSettingsScreen(); }
    private void ToggleMusic()
    {
        musicEnabled = !musicEnabled; SyncAmbientMusic(); SaveData(); RefreshSettingsScreen();
    }
    private void SyncAmbientMusic()
    {
        if (musicSource == null) return;
        if (ambientMusic != null && musicSource.clip != ambientMusic) musicSource.clip = ambientMusic;
        if (musicEnabled && ambientMusic != null) { if (!musicSource.isPlaying) musicSource.Play(); }
        else if (musicSource.isPlaying) musicSource.Stop();
    }
    private void ToggleLargeText()
    {
        largeText = !largeText;
        foreach (TextMeshProUGUI t in scalableText) t.fontSize *= largeText ? 1.12f : 1f / 1.12f;
        SaveData(); RefreshSettingsScreen();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PARTICLES + FX
    // ─────────────────────────────────────────────────────────────────────────
    private void SpawnBurst(Color color, int count, Vector2 center, bool fall)
    {
        for (int i = 0; i < count; i++)
        {
            Image p   = particlePool.Get();
            p.sprite  = UnityEngine.Random.value < 0.3f ? diamondSprite : null;
            p.color   = color.WithA(UnityEngine.Random.Range(0.45f, 0.95f));
            RectTransform rt = p.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * UnityEngine.Random.Range(7f, 18f);
            rt.anchoredPosition = center + UnityEngine.Random.insideUnitCircle * 32f;
            Vector2 target = center + (fall ? new Vector2(UnityEngine.Random.Range(-680f, 680f), UnityEngine.Random.Range(-680f, -280f)) : UnityEngine.Random.insideUnitCircle * 170f);
            rt.DOAnchorPos(target, UnityEngine.Random.Range(0.5f, 1.3f)).SetEase(Ease.OutCubic).SetId(this);
            p.DOFade(0f, 1.1f).SetId(this).OnComplete(() => particlePool.Release(p));
        }
    }

    private void SpawnVictoryRain(RectTransform parent, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            TextMeshProUGUI frag = TMP("VRain", parent, "♦", UnityEngine.Random.Range(16, 36), color.WithA(0.8f), TextAlignmentOptions.Center);
            RectTransform rt    = frag.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.value, 1.1f);
            rt.sizeDelta  = new Vector2(34f, 34f);
            rt.DOAnchorPosY(-ReferenceHeight * 0.1f - UnityEngine.Random.Range(0f, ReferenceHeight), UnityEngine.Random.Range(0.8f, 2.2f)).SetEase(Ease.InCubic).SetDelay(UnityEngine.Random.Range(0f, 0.6f)).SetId(this);
            frag.DOFade(0f, 1.8f).SetDelay(0.3f).SetId(this).OnComplete(() => Destroy(frag.gameObject));
        }
    }

    private void SpawnStaticNoise(RectTransform parent)
    {
        if (reducedMotion) return;
        for (int y = 0; y < 18; y++)
        for (int x = 0; x < 28; x++)
        {
            Image px = MakeImage("Static", parent, UnityEngine.Random.value < 0.5f ? TextWhite.WithA(0.28f) : Primary.WithA(0.22f));
            RectTransform rt = px.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2((x + 0.5f) / 28f, (y + 0.5f) / 18f);
            rt.sizeDelta  = new Vector2(12f, 8f);
            px.DOFade(0f, UnityEngine.Random.Range(0.35f, 1.0f)).SetDelay(UnityEngine.Random.Range(0f, 0.4f)).SetId(this).OnComplete(() => Destroy(px.gameObject));
        }
    }

    private void Flash(Color color, float duration)
    {
        Image img = MakeImage("Flash", fxLayer, color);
        img.rectTransform.Fill();
        img.DOFade(0f, duration).SetId(this).OnComplete(() => Destroy(img.gameObject));
    }

    private void Shake(RectTransform target, float strength)
    {
        if (!shakeEnabled || reducedMotion || target == null) return;
        target.DOShakeAnchorPos(0.22f, strength, 16, 80f).SetId(this);
    }

    private void SpawnLightRays(RectTransform parent, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            RectTransform ray = MakeImage("Victory Ray", parent, color.WithA(0.07f)).rectTransform;
            ray.anchorMin = ray.anchorMax = new Vector2(0.5f, 0.5f);
            ray.sizeDelta = new Vector2(UnityEngine.Random.Range(8f, 22f), ReferenceHeight * 1.4f);
            ray.anchoredPosition = Vector2.zero;
            ray.localRotation    = Quaternion.Euler(0f, 0f, i * (360f / count));
            ray.localScale       = new Vector3(1f, 0f, 1f);
            ray.DOScaleY(1f, 0.4f).SetEase(Ease.OutCubic).SetId(this);
            ray.GetComponent<Image>().DOFade(0f, 1.2f).SetDelay(0.32f).SetId(this).OnComplete(() => Destroy(ray.gameObject));
        }
    }

    private void SpawnDiamondFragments(Color color, int count, Vector2 center)
    {
        for (int i = 0; i < count; i++)
        {
            TextMeshProUGUI frag = TMP("Frag", fxLayer, "♦", UnityEngine.Random.Range(16, 44), color.WithA(UnityEngine.Random.Range(0.4f, 0.9f)), TextAlignmentOptions.Center);
            RectTransform rt    = frag.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta  = new Vector2(54f, 54f);
            rt.anchoredPosition = center + UnityEngine.Random.insideUnitCircle * 65f;
            rt.localRotation    = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            Vector2 target = center + UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(220f, 800f);
            rt.DOAnchorPos(target, UnityEngine.Random.Range(0.7f, 1.5f)).SetEase(Ease.OutCubic).SetId(this);
            rt.DORotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-240f, 240f)), 1.3f, RotateMode.FastBeyond360).SetId(this);
            frag.DOFade(0f, 1.4f).SetId(this).OnComplete(() => Destroy(frag.gameObject));
        }
    }

    private void SpawnSignalLoss(RectTransform parent)
    {
        TextMeshProUGUI signal = TMP("Signal Loss", parent, "SIGNAL LOST\nTRANSMISSION TERMINATED", 42, Primary, TextAlignmentOptions.Center);
        signal.fontStyle = FontStyles.Bold; signal.characterSpacing = 5f;
        signal.rectTransform.SetBox(0.5f, 0.2f, 920f, 128f);
        StartGlitch(signal, signal.text, 0.04f, 0.2f);
        for (int i = 0; i < 32; i++)
        {
            RectTransform stripe = MakeImage("Stripe", parent, i % 2 == 0 ? TextWhite.WithA(0.16f) : Primary.WithA(0.16f)).rectTransform;
            stripe.anchorMin = stripe.anchorMax = new Vector2(0.5f, i / 31f);
            stripe.sizeDelta  = new Vector2(ReferenceWidth, UnityEngine.Random.Range(3f, 20f));
            stripe.DOScaleX(UnityEngine.Random.Range(0.08f, 0.88f), UnityEngine.Random.Range(0.16f, 0.52f)).SetLoops(4, LoopType.Yoyo).SetId(this);
            stripe.GetComponent<Image>().DOFade(0f, 1.1f).SetDelay(0.22f).SetId(this).OnComplete(() => Destroy(stripe.gameObject));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIO
    // ─────────────────────────────────────────────────────────────────────────
    private bool PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (!soundEnabled || sfxSource == null || clip == null) return false;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        return true;
    }

    private void PlayTone(float frequency, float duration, float volume, float pan)
    {
        if (!soundEnabled || sfxSource == null) return;
        int sampleRate = AudioSettings.outputSampleRate;
        int samples    = Mathf.Max(64, Mathf.CeilToInt(sampleRate * duration));
        AudioClip clip = AudioClip.Create("DiamondTone", samples, 2, sampleRate, false);
        float[] data   = new float[samples * 2];
        float left     = Mathf.Clamp01(1f - Mathf.Max(0f, pan));
        float right    = Mathf.Clamp01(1f + Mathf.Min(0f, pan));
        for (int i = 0; i < samples; i++)
        {
            float t    = i / (float)sampleRate;
            float env  = 1f - i / (float)samples;
            float wave = Mathf.Sin(2f * Mathf.PI * frequency * t) * volume * env;
            data[i * 2]     = wave * left;
            data[i * 2 + 1] = wave * right;
        }
        clip.SetData(data, 0);
        sfxSource.PlayOneShot(clip);
    }

    private void PlayMelody(float[] notes, float duration, float volume) => StartCoroutine(MelodyRoutine(notes, duration, volume));
    private IEnumerator MelodyRoutine(float[] notes, float duration, float volume)
    {
        foreach (float note in notes) { PlayTone(note, duration, volume, 0f); yield return new WaitForSeconds(duration * 0.84f); }
    }
    private void PlayCardFlip() { if (PlaySfx(digitReveal, 0.85f)) return; PlayTone(208f, 0.033f, 0.044f, -0.2f); PlayTone(416f, 0.05f, 0.038f, 0.2f); }
    private void PlayHorrorSting() { if (PlaySfx(wrongSound, 0.75f)) return; PlayTone(52f, 0.18f, 0.08f, 0f); PlayTone(108f, 0.16f, 0.05f, 0f); }

    // ─────────────────────────────────────────────────────────────────────────
    // BUTTONS + UI HELPERS
    // ─────────────────────────────────────────────────────────────────────────
    private void PulseButton(int digit)
    {
        if (!digitButtons.TryGetValue(digit, out Button b)) return;
        RectTransform rt = b.transform as RectTransform;
        if (rt == null) return;
        rt.DOKill(); rt.localScale = Vector3.one * 0.84f;
        rt.DOScale(1f, 0.24f).SetEase(Ease.OutBack).SetId(this);
        Image glow = MakeImage("Key Glow", fxLayer, Secondary.WithA(0.5f));
        glow.sprite = circleSprite;
        Vector2 pos = ToRootPosition(rt);
        glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        glow.rectTransform.anchoredPosition = pos;
        glow.rectTransform.sizeDelta = new Vector2(55f, 55f);
        glow.rectTransform.DOSizeDelta(new Vector2(95f, 95f), 0.35f).SetEase(Ease.OutCubic).SetId(this);
        glow.DOFade(0f, 0.35f).SetId(this).OnComplete(() => Destroy(glow.gameObject));
        SpawnBurst(Secondary, 4, pos, false);
    }

    private Button AddButton(Transform parent, string label, float x, float y, float w, float h, Color color, Action click)
    {
        RectTransform rt = PanelBox("Button " + label, parent, color.WithA(0.12f), color);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta  = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = rt.GetComponent<Image>();

        Image lip = MakeImage("Button Bottom Lip", rt, Color.black.WithA(0.3f));
        lip.rectTransform.anchorMin = Vector2.zero;
        lip.rectTransform.anchorMax = new Vector2(1f, 0f);
        lip.rectTransform.pivot     = new Vector2(0.5f, 0f);
        lip.rectTransform.sizeDelta = new Vector2(0f, 7f);

        Image shine = MakeImage("Button Top Shine", rt, TextWhite.WithA(0.04f));
        shine.rectTransform.anchorMin = new Vector2(0f, 1f);
        shine.rectTransform.anchorMax = Vector2.one;
        shine.rectTransform.pivot     = new Vector2(0.5f, 1f);
        shine.rectTransform.sizeDelta = new Vector2(0f, 16f);

        ColorBlock cb = button.colors;
        cb.normalColor      = color.WithA(0.12f);
        cb.highlightedColor = color.WithA(0.3f);
        cb.pressedColor     = color.WithA(0.52f);
        cb.selectedColor    = color.WithA(0.24f);
        cb.disabledColor    = Dark.WithA(0.38f);
        button.colors = cb;

        button.onClick.AddListener(() =>
        {
            if (!PlaySfx(uiClickSound, 0.6f)) PlayTone(415f, 0.033f, 0.024f, 0f);
            rt.DOKill(); rt.localScale = Vector3.one * 0.91f;
            rt.DOScale(1f, 0.24f).SetEase(Ease.OutBack).SetId(this);
            Image bImg = rt.GetComponent<Image>();
            bImg.DOKill(); bImg.DOColor(color.WithA(0.45f), 0.06f).SetLoops(2, LoopType.Yoyo).SetId(this);
            click?.Invoke();
        });

        EventTrigger trigger = rt.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ => rt.DOAnchorPosY(y + 3f, 0.1f).SetId("hover_" + rt.GetInstanceID()));
        trigger.triggers.Add(enterEntry);
        EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => rt.DOAnchorPosY(y, 0.1f).SetId("hover_" + rt.GetInstanceID()));
        trigger.triggers.Add(exitEntry);

        TextMeshProUGUI text = TMP("Label", rt, label, 19, color == SafeGreen ? SafeGreen : TextWhite, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold; text.characterSpacing = 2f; text.Fill();
        return button;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STAGE FRAME + PANEL HELPERS
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildStageFrame(RectTransform parent, Color color, string label)
    {
        RectTransform top = MakeImage("Arena Top Rail", parent, color).rectTransform;
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one; top.pivot = new Vector2(0.5f, 1f); top.sizeDelta = new Vector2(0f, 8f);
        RectTransform bottom = MakeImage("Arena Bottom Rail", parent, color.WithA(color.a * 0.7f)).rectTransform;
        bottom.anchorMin = Vector2.zero; bottom.anchorMax = new Vector2(1f, 0f); bottom.pivot = new Vector2(0.5f, 0f); bottom.sizeDelta = new Vector2(0f, 8f);
        RectTransform left = MakeImage("Arena Left Rail", parent, color.WithA(color.a * 0.8f)).rectTransform;
        left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0f, 1f); left.pivot = new Vector2(0f, 0.5f); left.sizeDelta = new Vector2(8f, 0f);
        RectTransform right = MakeImage("Arena Right Rail", parent, color.WithA(color.a * 0.8f)).rectTransform;
        right.anchorMin = new Vector2(1f, 0f); right.anchorMax = Vector2.one; right.pivot = new Vector2(1f, 0.5f); right.sizeDelta = new Vector2(8f, 0f);

        TextMeshProUGUI tag = TMP("Arena Label", parent, label, 17, color.WithA(0.72f), TextAlignmentOptions.Center);
        tag.textWrappingMode = TextWrappingModes.NoWrap; tag.characterSpacing = 12f;
        tag.rectTransform.SetBox(0.5f, 0.985f, 540f, 26f);

        for (int i = 0; i < 12; i++)
        {
            Image lamp = MakeImage("Alarm Lamp", parent, (i % 2 == 0 ? Primary : Warning).WithA(0.25f));
            lamp.sprite = circleSprite;
            RectTransform rt = lamp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(i / 11f, 0.985f);
            rt.sizeDelta  = new Vector2(12f, 12f);
            rt.DOScale(1.4f, 0.48f + i * 0.028f).SetLoops(-1, LoopType.Yoyo).SetId(this);
            lamp.DOFade(i % 2 == 0 ? 0.05f : 0.12f, 0.52f + i * 0.03f).SetLoops(-1, LoopType.Yoyo).SetId(this);
        }
    }

    private void BuildPanelHeaderStrip(RectTransform parent, string text, Color color)
    {
        RectTransform strip = MakeImage("Panel Header Strip", parent, color.WithA(0.07f)).rectTransform;
        strip.anchorMin = new Vector2(0f, 1f); strip.anchorMax = new Vector2(1f, 1f); strip.pivot = new Vector2(0.5f, 1f); strip.sizeDelta = new Vector2(0f, 46f);
        BuildMicroTicks(strip, 20, color.WithA(0.28f));
        TextMeshProUGUI lbl = TMP("Panel Strip Label", strip, text, 14, color.WithA(0.58f), TextAlignmentOptions.Right);
        lbl.textWrappingMode = TextWrappingModes.NoWrap; lbl.characterSpacing = 3f;
        lbl.rectTransform.Fill(); lbl.rectTransform.offsetMax = new Vector2(-18f, 0f);
    }

    private void BuildMicroTicks(RectTransform parent, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            Image tick = MakeImage("Micro Tick", parent, color);
            RectTransform rt = tick.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2((i + 0.5f) / count, 0.5f);
            rt.sizeDelta  = new Vector2(2f, i % 4 == 0 ? 20f : i % 2 == 0 ? 12f : 7f);
        }
    }

    private void AddCardPips(RectTransform card, Color color)
    {
        Vector2[] positions = { new Vector2(0.18f, 0.86f), new Vector2(0.82f, 0.86f), new Vector2(0.18f, 0.14f), new Vector2(0.82f, 0.14f) };
        foreach (Vector2 pos in positions)
        {
            TextMeshProUGUI pip = TMP("Card Pip", card, "♦", 30, color, TextAlignmentOptions.Center);
            pip.rectTransform.SetBox(pos.x, pos.y, 44f, 44f);
        }
        Image shine = MakeImage("Card Shine", card, TextWhite.WithA(0.05f)).rectTransform.GetComponent<Image>();
        shine.rectTransform.anchorMin = shine.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        shine.rectTransform.sizeDelta = new Vector2(82f, 640f);
        shine.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -22f);
        shine.rectTransform.anchoredPosition = new Vector2(-280f, 0f);
        shine.rectTransform.DOAnchorPosX(280f, 2.5f).SetLoops(-1, LoopType.Restart).SetEase(Ease.InOutSine).SetId(this);
    }

    private void AddPanelCorners(RectTransform parent, Color color)
    {
        Vector2[] anchors = { new Vector2(0f, 1f), Vector2.one, Vector2.zero, new Vector2(1f, 0f) };
        foreach (Vector2 anchor in anchors)
        {
            Image h = MakeImage("Corner H", parent, color.WithA(0.85f));
            RectTransform hrt = h.rectTransform;
            hrt.anchorMin = hrt.anchorMax = anchor; hrt.pivot = anchor;
            hrt.sizeDelta = new Vector2(36f, 3f);
            hrt.anchoredPosition = new Vector2(anchor.x == 0f ? 9f : -9f, anchor.y == 0f ? 9f : -9f);
            flickerBorders.Add(h);
            Image v = MakeImage("Corner V", parent, color.WithA(0.85f));
            RectTransform vrt = v.rectTransform;
            vrt.anchorMin = vrt.anchorMax = anchor; vrt.pivot = anchor;
            vrt.sizeDelta = new Vector2(3f, 36f);
            vrt.anchoredPosition = hrt.anchoredPosition;
            flickerBorders.Add(v);
        }
    }

    private void AddAnimatedBorder(RectTransform parent, Color color)
    {
        foreach (Image img in parent.GetComponentsInChildren<Image>())
            if (img.name.StartsWith("Border", StringComparison.Ordinal))
                img.DOColor(color.WithA(0.28f), 0.52f).SetLoops(-1, LoopType.Yoyo).SetId(this);
    }

    private void AddEdge(RectTransform parent, string name, Color color, Vector2 min, Vector2 max, Vector2 size)
    {
        Image edge = MakeImage("Border " + name, parent, color);
        flickerBorders.Add(edge);
        RectTransform rt = edge.rectTransform;
        rt.anchorMin = min; rt.anchorMax = max; rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CINEMATIC HELPERS
    // ─────────────────────────────────────────────────────────────────────────
    private RectTransform CinematicOverlay(string name, Color shade)
    {
        RectTransform overlay = Rect(name, modalLayer); overlay.Fill();
        MakeImage("Shade", overlay, shade).rectTransform.Fill();
        return overlay;
    }

    private void AnimateScoreText(TextMeshProUGUI label, int from, int to, string prefix)
    {
        DOTween.To(() => from, v => { from = v; if (label != null) label.text = prefix + v + " pts"; }, to, 0.52f).SetEase(Ease.OutCubic).SetId(this);
    }

    private IEnumerator TypeLine(TextMeshProUGUI label, string line, float delay)
    {
        foreach (char c in line) { label.text += c; if (!reducedMotion) yield return new WaitForSeconds(delay); }
    }

    private IEnumerator TypeOverwrite(TextMeshProUGUI label, string text, float delay)
    {
        if (label == null) yield break;
        label.text = string.Empty;
        foreach (char c in text) { label.text += c; if (!reducedMotion) yield return new WaitForSeconds(delay); }
    }

    private IEnumerator FlipCard(RectTransform target, Action middle)
    {
        if (target == null) { middle?.Invoke(); yield break; }
        // NEW: heavier flip with InOutBack easing
        target.DOScaleX(0f, reducedMotion ? 0.03f : 0.28f).SetEase(Ease.InOutBack).SetId(this);
        yield return new WaitForSeconds(reducedMotion ? 0.03f : 0.28f);
        // NEW: card glint at flip peak
        Flash(TextWhite.WithA(0.03f), 0.04f);
        middle?.Invoke();
        target.DOScaleX(1f, reducedMotion ? 0.04f : 0.28f).SetEase(Ease.OutBack).SetId(this);
        yield return new WaitForSeconds(reducedMotion ? 0.04f : 0.28f);
    }

    private void StartGlitch(TextMeshProUGUI label, string source, float interval, float intensity)
    {
        DOTween.Sequence().AppendCallback(() => label.text = Glitch(source, intensity))
            .AppendInterval(interval).AppendCallback(() => label.text = source)
            .AppendInterval(1.2f).SetLoops(-1).SetId(this);
    }

    private string Glitch(string source, float intensity)
    {
        char[] chars = source.ToCharArray();
        const string glyphs = "0123456789#$%&";
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsWhiteSpace(chars[i]) && UnityEngine.Random.value < intensity)
                chars[i] = glyphs[UnityEngine.Random.Range(0, glyphs.Length)];
        return new string(chars);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STATUS
    // ─────────────────────────────────────────────────────────────────────────
    private void SetStatus(string message)
    {
        systemLine = message;
        if (statusRoutine != null) StopCoroutine(statusRoutine);
        if (statusText != null && statusText.gameObject.activeInHierarchy)
            statusRoutine = StartCoroutine(TypeStatus(message));
        else
            RefreshHud();
    }

    private IEnumerator TypeStatus(string message)
    {
        statusText.text = string.Empty;
        statusText.color = Primary;
        statusText.DOColor(TextWhite, 0.12f).SetId(this);
        // NEW: sweep underline
        if (statusUnderline != null)
        {
            statusUnderline.color = Secondary.WithA(0.8f);
            statusUnderline.rectTransform.sizeDelta = new Vector2(0f, 2f);
            statusUnderline.rectTransform.DOSizeDelta(new Vector2(1600f, 2f), 0.3f).SetEase(Ease.OutCubic).SetId(this);
            statusUnderline.DOFade(0f, 0.6f).SetDelay(0.3f).SetId(this);
        }
        foreach (char c in message) { statusText.text += c; if (!reducedMotion) yield return new WaitForSeconds(0.007f); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DIFFICULTY / CONFIG
    // ─────────────────────────────────────────────────────────────────────────
    private void ApplyForcedDifficulty()
    {
        List<DifficultyConfig> configs = Configs();
        selected = configs[Mathf.Clamp(ForcedDifficulty, 0, configs.Count - 1)];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIMITIVE FACTORIES
    // ─────────────────────────────────────────────────────────────────────────
    private RectTransform PanelBox(string name, Transform parent, Color fill, Color border)
    {
        Image img = MakeImage(name, parent, fill);
        img.raycastTarget = true;
        RectTransform rt  = img.rectTransform;
        Image inner = MakeImage("Inner Glow", rt, border.WithA(0.03f));
        inner.rectTransform.Fill();
        inner.rectTransform.offsetMin = new Vector2(6f, 6f);
        inner.rectTransform.offsetMax = new Vector2(-6f, -6f);
        AddPanelCorners(rt, border);
        AddEdge(rt, "Top",    border, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f));
        AddEdge(rt, "Bottom", border, Vector2.zero,        new Vector2(1f, 0f), new Vector2(0f, 2f));
        AddEdge(rt, "Left",   border, Vector2.zero,        new Vector2(0f, 1f), new Vector2(2f, 0f));
        AddEdge(rt, "Right",  border, new Vector2(1f, 0f), Vector2.one,         new Vector2(2f, 0f));
        return rt;
    }

    private RectTransform ScreenRoot(string name)
    {
        RectTransform rt = Rect(name + " Screen", safe); rt.Fill(); return rt;
    }

    private TextMeshProUGUI TMP(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions alignment)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        tmp.text        = text;
        tmp.fontSize    = largeText ? Mathf.RoundToInt(size * 1.12f) : size;
        tmp.color       = color;
        tmp.alignment   = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode     = TextOverflowModes.Ellipsis;
        tmp.raycastTarget    = false;
        scalableText.Add(tmp);
        return tmp;
    }

    private Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image img = obj.GetComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return img;
    }

    private RectTransform Rect(string name, Transform parent)
    {
        GameObject obj = new(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPRITE FACTORIES
    // ─────────────────────────────────────────────────────────────────────────
    private Sprite CreateCircleSprite(int size)
    {
        Texture2D tex  = new(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        Vector2 center = new(size * 0.5f, size * 0.5f);
        float radius   = size * 0.48f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), center);
            float a = Mathf.Clamp01(1f - (d - (radius - 2f)) / 3f);
            pixels[y * size + x] = d <= radius ? new Color(1f, 1f, 1f, a) : Color.clear;
        }
        tex.SetPixels(pixels); tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private Sprite CreateSquareSprite(int size)
    {
        Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
        Color[] px    = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private Sprite CreateDiamondSprite(int size)
    {
        Texture2D tex  = new(size, size, TextureFormat.RGBA32, false);
        Color[] px     = new Color[size * size];
        Vector2 center = new(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Abs(x - center.x) / (size * 0.5f);
            float dy = Mathf.Abs(y - center.y) / (size * 0.5f);
            px[y * size + x] = (dx + dy) <= 1f ? Color.white : Color.clear;
        }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY
    // ─────────────────────────────────────────────────────────────────────────
    private Vector2 ToRootPosition(RectTransform target)
    {
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 center = (corners[0] + corners[2]) * 0.5f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, RectTransformUtility.WorldToScreenPoint(null, center), null, out Vector2 local);
        return local;
    }

    private string CardShortLabel() => selected.mode == GameMode.Beginner ? "♦2" : selected.mode == GameMode.DeathGame ? "♦7" : "♦2";
    private string CardLabel()      => CardShortLabel() + " " + selected.title.Replace(CardShortLabel(), string.Empty).Trim();
    private string AnnouncementText()
    {
        if (selected.mode == GameMode.Beginner)  
            return "DIAMOND 2 — BEGINNER\n" +
                selected.seconds + " SECONDS — " + 
                selected.attempts + " ATTEMPTS\n" +
                "THIS GAME CAN BE CLEARED";
        if (selected.mode == GameMode.DeathGame) 
            return "DIAMOND 7 — DEATH GAME\n" +
                selected.seconds + " SECONDS — " + 
                selected.attempts + " ATTEMPTS\n" +
                "NO ONE HAS CLEARED THIS GAME";
        return "DIAMOND 2 — CHALLENGER\n" +
            selected.seconds + " SECONDS — " + 
            selected.attempts + " ATTEMPTS\n" +
            "ONLY THE SMART SURVIVE";
    }
    private TextMeshProUGUI FindText(RectTransform parent, string name) =>
        parent == null ? null : parent.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.name == name);

    private Mark StrongerMark(Mark a, Mark b) => (Mark)Mathf.Max((int)a, (int)b);
    private Color MarkColor(Mark mark)
    {
        if (mark == Mark.Locked) return SafeGreen;  // ← toujours vert
        if (mark == Mark.Signal) return Warning;
        return BlackMark;
    }
    private string MarkSymbol(Mark mark)
    {
        if (colorblind) return mark == Mark.Locked ? "*" : mark == Mark.Signal ? "O" : "X";
        return mark == Mark.Locked ? "♦" : mark == Mark.Signal ? "◆" : "■";
    }
    private string LifeSymbols() => lives > 0 ? "♦" : "♢";
    private string RankName(int v) => v >= 2000 ? "LEGEND" : v >= 1300 ? "ELITE" : v >= 700 ? "VETERAN" : v >= 300 ? "SURVIVOR" : "NEWCOMER";
    private string OnOff(bool v) => v ? "ON" : "OFF";
    private float DigitFrequency(int digit) { float[] n = { 659f, 262f, 294f, 330f, 349f, 392f, 440f, 494f, 523f, 587f }; return n[Mathf.Clamp(digit, 0, n.Length - 1)]; }
    private float DigitPan(int digit) { if (digit == 0) return 0f; int col = (digit - 1) % 3; return col == 0 ? -0.65f : col == 1 ? 0f : 0.65f; }
    private void SetText(TextMeshProUGUI[] labels, string name, string value) { TextMeshProUGUI label = labels.FirstOrDefault(t => t.name == name); if (label != null) label.text = value; }
    private string RandomCoordinate() => "TOKYO-Z" + UnityEngine.Random.Range(1, 99).ToString("00") + " / X:" + UnityEngine.Random.Range(100, 999) + " Y:" + UnityEngine.Random.Range(100, 999) + " / ID#" + UnityEngine.Random.Range(1000, 9999);
    private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out Color c); return c; }

    private List<DifficultyConfig> Configs() => new()
    {
        new DifficultyConfig { mode = GameMode.Beginner,   title = "♦2 BEGINNER",   seconds = 45, attempts = 7, lives = 3, multiplier = 1f,   color = SafeGreen },
        new DifficultyConfig { mode = GameMode.Challenger, title = "♦2 CHALLENGER", seconds = 30, attempts = 6, lives = 3, multiplier = 1.8f,  color = Primary   },
        new DifficultyConfig { mode = GameMode.DeathGame,  title = "♦7 DEATH GAME", seconds = 15, attempts = 4, lives = 1, multiplier = 3.5f,  color = Warning   }
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// EXTENSION METHODS
// ─────────────────────────────────────────────────────────────────────────────
public static class DiamondGameExtensions
{
    public static Color WithA(this Color color, float alpha) { color.a = alpha; return color; }
    public static void Fill(this RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    public static void Fill(this TextMeshProUGUI tmp) => (tmp.transform as RectTransform)?.Fill();
    public static void SetBox(this RectTransform rect, float ax, float ay, float width, float height)
    { rect.anchorMin = rect.anchorMax = new Vector2(ax, ay); rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = Vector2.zero; }
}
