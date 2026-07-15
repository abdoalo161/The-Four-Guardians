using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CardUIManager : MonoBehaviour
{
    public static CardUIManager Instance { get; private set; }
    [Header("Balance Scale (Sprite Swap)")]
    [SerializeField] private Image scaleImage;
    [SerializeField] private Sprite[] scaleSprites = new Sprite[21]; // index = value + 10, value in [-10..10]

    [Header("Core UI Refs")]
    [SerializeField] private Transform handPanel;
    [SerializeField] private Transform opponentPanel;
    [SerializeField] private Transform tableSlotsParent; // parent of P1_Lane_* and P2_Lane_*
    [SerializeField] private Transform dragLayer;
    [SerializeField] private GameObject cardViewPrefab;
    [SerializeField] private Sprite opponentCardBackSprite;
    [SerializeField] private float boardCardScale = 0.75f; // scale factor for cards placed on board slots
    [SerializeField] private TMP_Text turnBanner;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private TMP_Text discardPromptText;
    [SerializeField] private GameObject discardPromptContainer;

    [Header("Tribute UI")]
    [SerializeField] private GameObject tributeOverlay;
    [SerializeField] private TMP_Text tributeInstructionText;
    [SerializeField] private Button tributeConfirmButton;
    [SerializeField] private Button tributeCancelButton;
    private Image tributeOverlayImage; // optional background image

    [Header("Result UI")]
    public GameObject ResultOverlay;
    public TextMeshProUGUI ResultText;
    public Image ResultScaleImage; // optional: show final scale image
    public Button BackToLobbyButton; // optional

    [Header("Instruction Panel (One-Time)")]
    [SerializeField] private GameObject instructionPanel; // assign a panel in CardGameScene
    [SerializeField] private Button instructionCloseButton; // assign close button on the panel
    [SerializeField] private Button instructionOpenButton; // assign Help button in scene
    private const string InstructionsSeenKey = "CardGame_InstructionsShown";

    [Header("Pause Menu")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button pauseOpenButton;
    [SerializeField] private Button pauseResumeButton;
    [SerializeField] private Button pauseQuitButton;
    [SerializeField] private Button pauseOptionsButton;
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private Button optionsCloseButton;

    [Header("Graveyard")]
    [SerializeField] private Button graveyardButton;
    [SerializeField] private GameObject graveyardPanel;
    [SerializeField] private Button graveyardCloseButton;
    [SerializeField] private Transform graveyardOwnContent;
    [SerializeField] private Transform graveyardOppContent;

    private CardPlayer localPlayer;
    private CardGameManager gameManager;
    private Transform[] p1Slots = new Transform[6];
    private Transform[] p2Slots = new Transform[6];

    private bool tributeMode = false;
    private bool isPaused = false;
    private int pendingCardId = -1;
    private int pendingTargetLane = -1;
    private int requiredTributes = 0;
    private bool[] tributeSelected = new bool[6];
    private Dictionary<int, int> tributeCostCache = new Dictionary<int, int>();
    private Dictionary<int, Sprite> artworkCache = new Dictionary<int, Sprite>();

    private int currentScaleValue = 0;
    private bool isMyTurnFlag = false;
    private ulong lastActivePlayerId = 0;
    private bool dragActive = false;
    private List<int> lastOwnGraveyard = new List<int>();
    private List<int> lastOppGraveyard = new List<int>();
    private int[] prevOwnIds = new int[6];
    private int[] prevOwnAtk = new int[6];
    private int[] prevOwnHp = new int[6];
    private int[] prevOppIds = new int[6];
    private int[] prevOppAtk = new int[6];
    private int[] prevOppHp = new int[6];
    private int? overrideOpponentHandCount = null;

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < 6; i++)
        {
            prevOwnIds[i] = -2; prevOwnAtk[i] = -2; prevOwnHp[i] = -2;
            prevOppIds[i] = -2; prevOppAtk[i] = -2; prevOppHp[i] = -2;
        }
    }

    public void SetOpponentHandCount(int count)
    {
        overrideOpponentHandCount = Mathf.Max(0, count);
    }

    private void EnsureLocalPlayer()
    {
        if (localPlayer == null)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
            {
                localPlayer = nm.LocalClient.PlayerObject.GetComponent<CardPlayer>();
                if (localPlayer != null)
                {
                    localPlayer.Hand.OnListChanged += OnHandChanged;
                    localPlayer.NeedsDiscard.OnValueChanged += OnDiscardStateChanged;
                    localPlayer.DiscardCount.OnValueChanged += OnDiscardStateChanged;
                }

            }
        }
    }

    private RectTransform FindRectTransformDeep(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;
        var direct = root.Find(childName) as RectTransform;
        if (direct != null) return direct;
        var all = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == childName) return all[i];
        }
        return null;
    }

    private void ToggleStatBackgrounds(GameObject go, bool on)
    {
        if (go == null) return;
        var atkBg = FindRectTransformDeep(go.transform, "ATKBG");
        if (atkBg != null) atkBg.gameObject.SetActive(on);
        var hpBg = FindRectTransformDeep(go.transform, "HPBG");
        if (hpBg != null) hpBg.gameObject.SetActive(on);
        var atkT = FindRectTransformDeep(go.transform, "ATKText");
        if (atkT != null) atkT.gameObject.SetActive(on);
        var hpT = FindRectTransformDeep(go.transform, "HPText");
        if (hpT != null) hpT.gameObject.SetActive(on);
    }

    private void SetCardStatsOverlay(GameObject go, int atk, int hp)
    {
        if (go == null) return;
        var atkT = FindRectTransformDeep(go.transform, "ATKText");
        var hpT = FindRectTransformDeep(go.transform, "HPText");
        if (atkT == null)
        {
            var goAtk = new GameObject("ATKText");
            goAtk.transform.SetParent(go.transform, false);
            atkT = goAtk.AddComponent<RectTransform>();
            atkT.anchorMin = new Vector2(0f, 0f);
            atkT.anchorMax = new Vector2(0f, 0f);
            atkT.pivot = new Vector2(0f, 0f);
            atkT.anchoredPosition = new Vector2(6f, 6f);
            var tmp = goAtk.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.alignment = TMPro.TextAlignmentOptions.BottomLeft;
        }
        if (hpT == null)
        {
            var goHp = new GameObject("HPText");
            goHp.transform.SetParent(go.transform, false);
            hpT = goHp.AddComponent<RectTransform>();
            hpT.anchorMin = new Vector2(1f, 0f);
            hpT.anchorMax = new Vector2(1f, 0f);
            hpT.pivot = new Vector2(1f, 0f);
            hpT.anchoredPosition = new Vector2(-6f, 6f);
            var tmp = goHp.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.alignment = TMPro.TextAlignmentOptions.BottomRight;
        }
        var atkTmp = atkT.GetComponent<TMPro.TextMeshProUGUI>();
        var hpTmp = hpT.GetComponent<TMPro.TextMeshProUGUI>();
        if (atkTmp != null) atkTmp.text = atk > 0 ? atk.ToString() : "";
        if (hpTmp != null) hpTmp.text = hp > 0 ? hp.ToString() : "";
    }

    // Public wrapper for drag handlers to highlight a specific card view
    public void SetCardDragHighlight(GameObject cardView, bool on)
    {
        if (cardView == null) return;
        ApplyHighlight(cardView, on);
    }

    private void ClearCardHighlight(GameObject go)
    {
        if (go == null) return;
        var t = go.transform.Find("SelectionHighlight");
        if (t != null) t.gameObject.SetActive(false);
        Image targetImg = null;
        var art = go.transform.Find("Art");
        if (art != null) targetImg = art.GetComponent<Image>();
        if (targetImg == null) targetImg = go.GetComponent<Image>();
        if (targetImg != null)
        {
            var outline = targetImg.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
                Destroy(outline);
            }
        }
    }

    public void SetScaleImage(Image img)
    {
        scaleImage = img;
        RenderBalanceScale(currentScaleValue);
    }

    private void UpdateOpponentHandUI()
    {
        if (opponentPanel == null) return;
        // Find the opponent CardPlayer (the one not owned by local)
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        CardPlayer opponent = null;
        var players = FindObjectsByType<CardPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var cp in players)
        {
            if (cp.OwnerClientId != localId)
            {
                opponent = cp; break;
            }
        }
        int count = overrideOpponentHandCount.HasValue ? overrideOpponentHandCount.Value : (opponent != null ? opponent.PublicHandCount.Value : 0);
        // Rebuild opponent panel children to match count
        for (int i = opponentPanel.childCount - 1; i >= 0; i--) Destroy(opponentPanel.GetChild(i).gameObject);
        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(cardViewPrefab, opponentPanel);
            // ensure it's just a back (no drag/click)
            var drag = go.GetComponent<CardDragHandler>(); if (drag != null) Destroy(drag);
            var btn = go.GetComponent<Button>(); if (btn != null) btn.interactable = false;
            // ensure no highlight on opponent cards
            ClearCardHighlight(go);
            // hide stat backgrounds/texts for opponent hand placeholders
            ToggleStatBackgrounds(go, false);
            // apply back sprite if provided
            if (opponentCardBackSprite != null)
            {
                var tArt = go.transform.Find("Art");
                if (tArt != null)
                {
                    var img = tArt.GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = opponentCardBackSprite;
                        img.preserveAspect = false; // fill the rect
                        var rt = img.rectTransform;
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }
                }
                else
                {
                    var img = go.GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = opponentCardBackSprite;
                        img.preserveAspect = false; // fill the rect
                        var rt = img.rectTransform;
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }
                }
            }
        }
    }

    public void ShowResult(bool youWin)
    {
        ResultOverlay.SetActive(true);
        ResultText.text = LocalizationSettings.StringDatabase.GetLocalizedString("ui", youWin ? "Result_Win" : "Result_Lose");
        // Show final scale image: winner=20, loser=0 (extreme images), fallback to HUD sprite
        if (ResultScaleImage != null)
        {
            Sprite chosen = null;
            if (scaleSprites != null)
            {
                int desiredIdx = youWin ? Mathf.Max(0, (scaleSprites.Length - 1)) : 0;
                if (desiredIdx >= 0 && desiredIdx < scaleSprites.Length)
                    chosen = scaleSprites[desiredIdx];
            }
            if (chosen == null && scaleImage != null)
            {
                chosen = scaleImage.sprite;
            }
            if (chosen != null)
            {
                ResultScaleImage.sprite = chosen;
                ResultScaleImage.gameObject.SetActive(true);
                ResultScaleImage.enabled = true;
                ResultScaleImage.preserveAspect = true;
                ResultScaleImage.color = Color.white;
            }
            else
            {
                Debug.LogWarning("[CardUIManager] ShowResult: No sprite found for ResultScaleImage. Check scaleSprites assignment or HUD scaleImage.");
                ResultScaleImage.gameObject.SetActive(false);
            }
        }
        if (endTurnButton != null) endTurnButton.interactable = false;
        if (BackToLobbyButton != null)
        {
            BackToLobbyButton.onClick.RemoveAllListeners();
            BackToLobbyButton.onClick.AddListener(OnBackToLobbyClicked);
        }
    }

    private void OnBackToLobbyClicked()
    {
        // Clean shutdown so we can host again
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null)
        {
            // Stop discovery if present
            var disc = UnityEngine.Object.FindFirstObjectByType<RPSNetworkDiscovery>(FindObjectsInactive.Include);
            if (disc != null) disc.StopDiscovery();
            nm.Shutdown();
        }
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
    }

    public void SetScaleSprites(Sprite[] sprites)
    {
        scaleSprites = sprites;
        RenderBalanceScale(currentScaleValue);
    }

    public void RenderBalanceScale(int value)
    {
        currentScaleValue = Mathf.Clamp(value, -20, 20);
        if (scaleImage == null || scaleSprites == null || scaleSprites.Length == 0) return;
        // Map [-20..20] to [0..N-1] dynamically
        int n = scaleSprites.Length;
        float t = (currentScaleValue + 20f) / 40f; // 0..1
        int index = Mathf.Clamp(Mathf.RoundToInt(t * (n - 1)), 0, n - 1);
        var sp = scaleSprites[index];
        if (sp != null) { scaleImage.sprite = sp; }
    }

    private void Start()
    {
        // Resolve local player and manager
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
        {
            localPlayer = nm.LocalClient.PlayerObject.GetComponent<CardPlayer>();
        }
        gameManager = Object.FindFirstObjectByType<CardGameManager>();

        if (endTurnButton != null) endTurnButton.onClick.AddListener(OnEndTurnClicked);
        if (tributeConfirmButton != null) tributeConfirmButton.onClick.AddListener(OnConfirmTributeClicked);
        if (tributeCancelButton != null) tributeCancelButton.onClick.AddListener(OnCancelTributeClicked);
        if (tributeOverlay != null) tributeOverlayImage = tributeOverlay.GetComponent<Image>();

        // Instruction panel wiring
        if (instructionCloseButton != null) instructionCloseButton.onClick.AddListener(OnInstructionCloseClicked);
        if (instructionOpenButton != null) instructionOpenButton.onClick.AddListener(OnInstructionOpenClicked);
        ShowInstructionIfFirstTime();

        // Pause menu wiring
        if (pauseOpenButton != null) pauseOpenButton.onClick.AddListener(OpenPauseMenu);
        if (pauseResumeButton != null) pauseResumeButton.onClick.AddListener(ClosePauseMenu);
        if (pauseQuitButton != null) pauseQuitButton.onClick.AddListener(OnBackToLobbyClicked);
        if (pauseOptionsButton != null) pauseOptionsButton.onClick.AddListener(OpenOptionsMenu);
        if (optionsCloseButton != null) optionsCloseButton.onClick.AddListener(CloseOptionsMenu);

        if (graveyardButton != null) graveyardButton.onClick.AddListener(OpenGraveyard);
        if (graveyardCloseButton != null) graveyardCloseButton.onClick.AddListener(CloseGraveyard);

        // Subscribe to hand and discard changes for live UI updates
        if (localPlayer != null)
        {
            localPlayer.Hand.OnListChanged += OnHandChanged;
            localPlayer.NeedsDiscard.OnValueChanged += OnDiscardStateChanged;
            localPlayer.DiscardCount.OnValueChanged += OnDiscardStateChanged;
        }

        CacheSlots();
        RefreshHandUI();

        if (gameManager != null)
        {
            gameManager.ActivePlayerClientId.OnValueChanged += OnActivePlayerChanged;
            OnActivePlayerChanged(0, gameManager.ActivePlayerClientId.Value);
        }
    }

    private void OnActivePlayerChanged(ulong oldId, ulong newId)
    {
        ApplyTurnUIImmediate(newId);
    }

    private void ApplyTurnUIImmediate(ulong activePlayerId)
    {
        lastActivePlayerId = activePlayerId;
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        bool isMyTurn = (activePlayerId == localId);
        isMyTurnFlag = isMyTurn;
        if (turnBanner != null)
        {
            string key = isMyTurn ? "Turn_Your" : "Turn_Opponent";
            turnBanner.text = LocalizationSettings.StringDatabase.GetLocalizedString("ui", key);
            turnBanner.color = Color.white;
        }
        bool discarding = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
        if (endTurnButton != null)
        {
            endTurnButton.interactable = isMyTurn && !discarding && !tributeMode && !isPaused;
        }
        UpdateHandInteractivity();
    }

    private void Update()
    {
        bool esc = false;
#if ENABLE_INPUT_SYSTEM
        esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        esc = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (esc)
        {
            // If options panel is open, close it first
            if (optionsPanel != null && optionsPanel.activeSelf)
            {
                CloseOptionsMenu();
                return;
            }
            // Otherwise toggle pause
            if (!isPaused) OpenPauseMenu(); else ClosePauseMenu();
        }
    }

    private void ShowInstructionIfFirstTime()
    {
        bool seen = PlayerPrefs.GetInt(InstructionsSeenKey, 0) == 1;
        if (!seen)
        {
            if (instructionPanel != null) instructionPanel.SetActive(true);
        }
        else
        {
            if (instructionPanel != null) instructionPanel.SetActive(false);
        }
    }

    private void OnInstructionCloseClicked()
    {
        if (instructionPanel != null) instructionPanel.SetActive(false);
        PlayerPrefs.SetInt(InstructionsSeenKey, 1);
        PlayerPrefs.Save();
    }

    private void OnInstructionOpenClicked()
    {
        if (instructionPanel != null) instructionPanel.SetActive(true);
    }

    private void OpenPauseMenu()
    {
        isPaused = true;
        if (pausePanel != null) pausePanel.SetActive(true);
        UpdateHandInteractivity();
        if (endTurnButton != null) endTurnButton.interactable = false;
    }

    private void ClosePauseMenu()
    {
        isPaused = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        UpdateHandInteractivity();
        if (endTurnButton != null)
        {
            bool discarding = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
            endTurnButton.interactable = isMyTurnFlag && !discarding && !tributeMode && !isPaused;
        }
    }

    private void OpenOptionsMenu()
    {
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(true);
            var cv = optionsPanel.GetComponent<Canvas>();
            if (cv == null) cv = optionsPanel.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 1001;
            var gr = optionsPanel.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (gr == null) gr = optionsPanel.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }
    }

    private void CloseOptionsMenu()
    {
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (pausePanel != null && isPaused) pausePanel.SetActive(true);
    }

    private void OnDestroy()
    {
        if (localPlayer != null)
        {
            localPlayer.Hand.OnListChanged -= OnHandChanged;
            localPlayer.NeedsDiscard.OnValueChanged -= OnDiscardStateChanged;
            localPlayer.DiscardCount.OnValueChanged -= OnDiscardStateChanged;
        }
        if (gameManager != null)
        {
            gameManager.ActivePlayerClientId.OnValueChanged -= OnActivePlayerChanged;
        }
        if (Instance == this) Instance = null;
    }

    public void OnEndTurnClicked()
    {
        if (gameManager == null || !NetworkManager.Singleton.IsClient) return;
        if (localPlayer != null && localPlayer.NeedsDiscard.Value) return; // cannot end turn while discarding
        gameManager.RequestEndTurnServerRpc();
    }

    public void RefreshHandUI()
    {
        EnsureLocalPlayer();
        if (handPanel == null || cardViewPrefab == null || localPlayer == null) return;
        // Clear children
        for (int i = handPanel.childCount - 1; i >= 0; i--) Destroy(handPanel.GetChild(i).gameObject);
        // Rebuild from owner-only hand
        foreach (var id in localPlayer.Hand)
        {
            var go = Instantiate(cardViewPrefab, handPanel) as GameObject;
            // Ensure hand cards use normal scale
            go.transform.localScale = Vector3.one;
            // No highlight on hand cards
            ClearCardHighlight(go);
            // Hide board-only stat backgrounds on hand cards
            ToggleStatBackgrounds(go, false);
            var drag = go.GetComponent<CardDragHandler>();
            if (drag == null) drag = go.AddComponent<CardDragHandler>();
            drag.CardId = id;
            // Apply artwork
            ApplyArtwork(go, id);
            // When awaiting discard, clicking a card discards it
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => OnHandCardClickedToMaybeDiscard(drag));
            // Interactivity: only allow drag when it's your turn and not discarding
            bool discarding = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
            drag.enabled = isMyTurnFlag && !discarding;
            btn.interactable = true; // still allow click to discard when needed
        }

        // Discard prompt visibility
        {
            bool need = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
            if (discardPromptContainer != null) discardPromptContainer.SetActive(need);
            if (discardPromptText != null)
            {
                if (discardPromptContainer == null) discardPromptText.gameObject.SetActive(need);
                if (need) discardPromptText.text = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetLocalizedString("ui", "Discard_Prompt");
            }
        }
        if (endTurnButton != null)
        {
            bool discarding = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
            endTurnButton.interactable = isMyTurnFlag && !discarding && !tributeMode && !isPaused;
        }
    }

    private void OnHandCardClickedToMaybeDiscard(CardDragHandler drag)
    {
        if (localPlayer == null) return;
        if (localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0)
        {
            localPlayer.RequestDiscardServerRpc(drag.CardId);
        }
    }

    private void OnHandChanged(Unity.Netcode.NetworkListEvent<int> change)
    {
        RefreshHandUI();
    }

    private void OnDiscardStateChanged(bool oldValue, bool newValue)
    {
        RefreshHandUI();
        ApplyTurnUIImmediate(lastActivePlayerId);
    }

    private void OnDiscardStateChanged(int oldValue, int newValue)
    {
        RefreshHandUI();
        ApplyTurnUIImmediate(lastActivePlayerId);
    }

    // Drop forwarder: will be expanded to start tribute flow or simple summon
    public void OnCardDroppedToSlot(CardDragHandler drag, BoardSide side, int laneIndex)
    {
        if (gameManager == null || localPlayer == null) return;
        if (localPlayer.NeedsDiscard.Value) return;
        if (side != BoardSide.P1) return;
        if (!isMyTurnFlag) return;
        int tributeCost = GetTributeCostFromDefinitions(drag.CardId);
        if (tributeCost <= 0)
        {
            // Do NOT destroy the local card view immediately. Wait for server to accept and update the hand list.
            // This avoids the "card disappears" issue when the server rejects due to seat mapping after reconnect.
            CacheSlots();
            if (drag != null)
            {
                drag.WasDropped = true; // mark as dropped to disable further drag during the RPC roundtrip
                // Hide the dragged visual to avoid a floating ghost; UI will rebuild on server confirmation
                drag.gameObject.SetActive(false);
            }
            OnEndCardDrag();
            gameManager.RequestSummonServerRpc(drag.CardId, laneIndex, null);
        }
        else
        {
            // Accept drop into tribute flow: hide/remove current view; UI will refresh
            if (drag != null)
            {
                drag.WasDropped = true;
                Destroy(drag.gameObject);
            }
            OnEndCardDrag();
            BeginTributeSelection(drag.CardId, laneIndex, tributeCost);
        }
    }

    private int GetTributeCostFromDefinitions(int cardId)
    {
        if (tributeCostCache.TryGetValue(cardId, out int cached)) return cached;
        int? fromLib = FindTributeInDecks(cardId);
        if (fromLib.HasValue)
        {
            tributeCostCache[cardId] = fromLib.Value;
            return fromLib.Value;
        }
        // Fallback to rules if not found in library
        var stats = CardRules.GetStats(cardId);
        tributeCostCache[cardId] = stats.TributeCost;
        return stats.TributeCost;
    }

    private int? FindTributeInDecks(int cardId)
    {
        if (gameManager == null || gameManager.DeckLibrary == null) return null;
        int? v;
        v = FindInDeck(gameManager.DeckLibrary.FireDeck, cardId); if (v.HasValue) return v;
        v = FindInDeck(gameManager.DeckLibrary.WaterDeck, cardId); if (v.HasValue) return v;
        v = FindInDeck(gameManager.DeckLibrary.AirDeck, cardId); if (v.HasValue) return v;
        v = FindInDeck(gameManager.DeckLibrary.EarthDeck, cardId); if (v.HasValue) return v;
        return null;
    }

    private int? FindInDeck(CardDefinition[] deck, int cardId)
    {
        if (deck == null) return null;
        for (int i = 0; i < deck.Length; i++)
        {
            var d = deck[i];
            if (d == null) continue;
            if (d.Id == cardId) return d.TributeCost;
        }
        return null;
    }

    private Sprite GetArtworkSprite(int cardId)
    {
        if (artworkCache.TryGetValue(cardId, out var cached) && cached != null) return cached;
        var sp = FindArtworkInDecks(cardId);
        if (sp != null)
        {
            artworkCache[cardId] = sp;
        }
        return sp;
    }

    private Sprite FindArtworkInDecks(int cardId)
    {
        if (gameManager == null || gameManager.DeckLibrary == null) return null;
        var s = FindArtwork(gameManager.DeckLibrary.FireDeck, cardId); if (s != null) return s;
        s = FindArtwork(gameManager.DeckLibrary.WaterDeck, cardId); if (s != null) return s;
        s = FindArtwork(gameManager.DeckLibrary.AirDeck, cardId); if (s != null) return s;
        s = FindArtwork(gameManager.DeckLibrary.EarthDeck, cardId); if (s != null) return s;
        return null;
    }

    private Sprite FindArtwork(CardDefinition[] deck, int cardId)
    {
        if (deck == null) return null;
        for (int i = 0; i < deck.Length; i++)
        {
            var d = deck[i];
            if (d == null) continue;
            if (d.Id == cardId) return d.Artwork;
        }
        return null;
    }

    private void ApplyArtwork(GameObject go, int cardId)
    {
        var sp = GetArtworkSprite(cardId);
        if (sp == null)
        {
            StartCoroutine(ResolveAndApplyArtworkLater(go, cardId));
            return;
        }
        // Prefer a child named "Art" if present
        var tArt = go.transform.Find("Art");
        if (tArt != null)
        {
            var img = tArt.GetComponent<Image>();
            if (img == null)
            {
                img = tArt.gameObject.AddComponent<Image>();
            }
            if (img != null)
            {
                img.sprite = sp;
                img.preserveAspect = true;
                img.color = Color.white;
                return;
            }
        }
        // Fallback to root Image
        var rootImg = go.GetComponent<Image>();
        if (rootImg != null)
        {
            rootImg.sprite = sp;
            rootImg.preserveAspect = true;
            rootImg.color = Color.white;
        }
    }

    private System.Collections.IEnumerator ResolveAndApplyArtworkLater(GameObject go, int cardId)
    {
        float timeout = 2f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (go == null) yield break;
            var sp = FindArtworkInDecks(cardId);
            if (sp != null)
            {
                // write-through cache
                artworkCache[cardId] = sp;
                // apply
                var tArt = go.transform.Find("Art");
                if (tArt != null)
                {
                    var img = tArt.GetComponent<Image>();
                    if (img == null) img = tArt.gameObject.AddComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = sp;
                        img.preserveAspect = true;
                        img.color = Color.white;
                        yield break;
                    }
                }
                var rootImg = go.GetComponent<Image>();
                if (rootImg != null)
                {
                    rootImg.sprite = sp;
                    rootImg.preserveAspect = true;
                    rootImg.color = Color.white;
                    yield break;
                }
            }
            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void UpdateHandInteractivity()
    {
        EnsureLocalPlayer();
        if (handPanel == null) return;
        bool discarding = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
        for (int i = 0; i < handPanel.childCount; i++)
        {
            var go = handPanel.GetChild(i).gameObject;
            var drag = go.GetComponent<CardDragHandler>();
            if (drag != null) drag.enabled = isMyTurnFlag && !discarding;
            var btn = go.GetComponent<Button>();
            if (btn != null) btn.interactable = true; // keep clickable for discard
        }
    }

    // Used by CardDragHandler to reparent drags
    public Transform GetDragLayer() => dragLayer;

    public void OnBeginCardDrag()
    {
        dragActive = true;
        UpdateDragSlotHighlights(true);
    }

    public void OnEndCardDrag()
    {
        dragActive = false;
        UpdateDragSlotHighlights(false);
    }

    private void UpdateDragSlotHighlights(bool on)
    {
        CacheSlots();
        for (int i = 0; i < 6; i++)
        {
            var slot = p1Slots[i];
            if (slot == null) continue;
            bool empty = slot.childCount == 0;
            SetSlotHighlight(slot, on && empty && isMyTurnFlag && (localPlayer == null || !localPlayer.NeedsDiscard.Value));
        }
    }

    private void SetSlotHighlight(Transform slot, bool on)
    {
        var t = slot.Find("SlotHighlight");
        if (t != null)
        {
            t.gameObject.SetActive(on);
            return;
        }
        // Fallback: outline the slot Image, or any child Image if root has none
        var img = slot.GetComponent<Image>();
        if (img == null)
        {
            img = slot.GetComponentInChildren<Image>(true);
        }
        if (img != null)
        {
            var outline = img.GetComponent<Outline>();
            if (outline == null) outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
            outline.enabled = on;
            return;
        }

        // Last resort: create a child highlight container we can toggle
        var go = new GameObject("SlotHighlight");
        go.transform.SetParent(slot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var hi = go.AddComponent<Image>();
        hi.color = new Color(1f, 1f, 1f, 0f); // invisible fill
        hi.raycastTarget = false;
        var hOutline = go.AddComponent<Outline>();
        hOutline.effectColor = new Color(1f, 1f, 1f, 0.8f);
        hOutline.effectDistance = new Vector2(1.5f, -1.5f);
        hOutline.useGraphicAlpha = true;
        go.SetActive(on);
    }

    public void UpdateBoardUI(int[] ownIds, int[] ownAtk, int[] ownHp, int[] oppIds, int[] oppAtk, int[] oppHp, int scale, ulong activePlayerId, int[] ownGraveyard, int[] oppGraveyard)
    {
        EnsureLocalPlayer();
        lastActivePlayerId = activePlayerId;
        if (ownGraveyard != null) { lastOwnGraveyard = new List<int>(ownGraveyard); }
        if (oppGraveyard != null) { lastOppGraveyard = new List<int>(oppGraveyard); }
        CacheSlots();
        bool rebuild = (ownIds != null && oppIds != null);
        if (rebuild)
        {
            // Full rebuild to avoid stale children when perspective flips after reconnect
            for (int i = 0; i < 6; i++)
            {
                if (p1Slots[i] != null)
                {
                    for (int c = p1Slots[i].childCount - 1; c >= 0; c--) Destroy(p1Slots[i].GetChild(c).gameObject);
                }
                if (p2Slots[i] != null)
                {
                    for (int c = p2Slots[i].childCount - 1; c >= 0; c--) Destroy(p2Slots[i].GetChild(c).gameObject);
                }
            }
            for (int i = 0; i < 6; i++)
            {
                if (p1Slots[i] != null)
                {
                    int nid = (i < ownIds.Length ? ownIds[i] : -1);
                    int natk = (i < ownAtk.Length ? ownAtk[i] : 0);
                    int nhp = (i < ownHp.Length ? ownHp[i] : 0);
                    if (nid >= 0)
                    {
                        var go = Instantiate(cardViewPrefab, p1Slots[i]);
                        go.transform.localScale = Vector3.one * Mathf.Clamp(boardCardScale, 0.1f, 2f);
                        var rt = go.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            rt.anchorMin = Vector2.zero;
                            rt.anchorMax = Vector2.one;
                            rt.offsetMin = Vector2.zero;
                            rt.offsetMax = Vector2.zero;
                            rt.anchoredPosition = Vector2.zero;
                        }
                        ClearCardHighlight(go);
                        var drag = go.GetComponent<CardDragHandler>();
                        if (drag != null) Destroy(drag);
                        var click = go.GetComponent<LaneCardClickable>();
                        if (click == null) click = go.AddComponent<LaneCardClickable>();
                        click.Init(this, i);
                        ApplyArtwork(go, nid);
                        ToggleStatBackgrounds(go, true);
                        SetCardStatsOverlay(go, natk, nhp);
                    }
                    prevOwnIds[i] = nid; prevOwnAtk[i] = natk; prevOwnHp[i] = nhp;
                }
                if (p2Slots[i] != null)
                {
                    int nid = (i < oppIds.Length ? oppIds[i] : -1);
                    int natk = (i < oppAtk.Length ? oppAtk[i] : 0);
                    int nhp = (i < oppHp.Length ? oppHp[i] : 0);
                    if (nid >= 0)
                    {
                        var go = Instantiate(cardViewPrefab, p2Slots[i]);
                        go.transform.localScale = Vector3.one * Mathf.Clamp(boardCardScale, 0.1f, 2f);
                        var rt = go.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            rt.anchorMin = Vector2.zero;
                            rt.anchorMax = Vector2.one;
                            rt.offsetMin = Vector2.zero;
                            rt.offsetMax = Vector2.zero;
                            rt.anchoredPosition = Vector2.zero;
                        }
                        ClearCardHighlight(go);
                        var drag = go.GetComponent<CardDragHandler>();
                        if (drag != null) Destroy(drag);
                        ApplyArtwork(go, nid);
                        ToggleStatBackgrounds(go, true);
                        SetCardStatsOverlay(go, natk, nhp);
                    }
                    prevOppIds[i] = nid; prevOppAtk[i] = natk; prevOppHp[i] = nhp;
                }
            }
        }
        // Apply tribute highlights on existing p1 children
        for (int i = 0; i < 6; i++)
        {
            if (p1Slots[i] == null) continue;
            if (p1Slots[i].childCount > 0)
            {
                ApplyHighlight(p1Slots[i].GetChild(0).gameObject, tributeMode && tributeSelected[i]);
            }
        }
        if (!tributeMode)
        {
            for (int i = 0; i < 6; i++)
            {
                if (p1Slots[i] != null && p1Slots[i].childCount > 0)
                {
                    ApplyHighlight(p1Slots[i].GetChild(0).gameObject, false);
                }
            }
        }
        RenderBalanceScale(scale);
        // Match-point music based on local scale and threshold 7
        {
            var audio = RPSAudioManager.Instance;
            if (audio != null)
            {
                int mpState = 0;
                if (scale >= 10) mpState = 1; // winning
                else if (scale <= -10) mpState = 2; // losing
                audio.SetMatchPointState(mpState);
            }
        }

        // Turn banner and EndTurn interactivity
        ulong localId = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
        bool isMyTurn = (activePlayerId == localId);
        isMyTurnFlag = isMyTurn;
        if (turnBanner != null)
        {
            string key = isMyTurn ? "Turn_Your" : "Turn_Opponent";
            turnBanner.text = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetLocalizedString("ui", key);
        }
        if (endTurnButton != null)
        {
            bool discarding = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
            endTurnButton.interactable = isMyTurn && !discarding && !tributeMode;
        }
        // Also update discard label visibility here to be safe
        {
            bool need = localPlayer != null && localPlayer.NeedsDiscard.Value && localPlayer.DiscardCount.Value > 0;
            if (discardPromptContainer != null) discardPromptContainer.SetActive(need);
            if (discardPromptText != null)
            {
                if (discardPromptContainer == null) discardPromptText.gameObject.SetActive(need);
                if (need) discardPromptText.text = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetLocalizedString("ui", "Discard_Prompt");
            }
        }
        // Update opponent hand placeholders
        UpdateOpponentHandUI();
        UpdateHandInteractivity();
        UpdateDragSlotHighlights(dragActive);
    }

    private void OpenGraveyard()
    {
        if (graveyardPanel != null) graveyardPanel.SetActive(true);
        RefreshGraveyardUI();
    }

    private void CloseGraveyard()
    {
        if (graveyardPanel != null) graveyardPanel.SetActive(false);
    }

    private void RefreshGraveyardUI()
    {
        if (graveyardOwnContent != null)
        {
            for (int i = graveyardOwnContent.childCount - 1; i >= 0; i--) Destroy(graveyardOwnContent.GetChild(i).gameObject);
            for (int i = 0; i < lastOwnGraveyard.Count; i++)
            {
                var go = Instantiate(cardViewPrefab, graveyardOwnContent);
                var drag = go.GetComponent<CardDragHandler>(); if (drag != null) Destroy(drag);
                var btn = go.GetComponent<Button>(); if (btn != null) btn.interactable = false;
                ClearCardHighlight(go);
                ToggleStatBackgrounds(go, false);
                ApplyArtwork(go, lastOwnGraveyard[i]);
            }
        }
        if (graveyardOppContent != null)
        {
            for (int i = graveyardOppContent.childCount - 1; i >= 0; i--) Destroy(graveyardOppContent.GetChild(i).gameObject);
            for (int i = 0; i < lastOppGraveyard.Count; i++)
            {
                var go = Instantiate(cardViewPrefab, graveyardOppContent);
                var drag = go.GetComponent<CardDragHandler>(); if (drag != null) Destroy(drag);
                var btn = go.GetComponent<Button>(); if (btn != null) btn.interactable = false;
                ClearCardHighlight(go);
                ToggleStatBackgrounds(go, false);
                ApplyArtwork(go, lastOppGraveyard[i]);
            }
        }
    }

    private void BeginTributeSelection(int cardId, int targetLane, int cost)
    {
        tributeMode = true;
        pendingCardId = cardId;
        pendingTargetLane = targetLane;
        requiredTributes = cost;
        for (int i = 0; i < tributeSelected.Length; i++) tributeSelected[i] = false;
        if (tributeOverlay != null) tributeOverlay.SetActive(true);
        if (tributeOverlayImage != null)
        {
            tributeOverlayImage.raycastTarget = false; // let clicks pass to lane cards
            var c = tributeOverlayImage.color; // reduce alpha so highlights are visible
            tributeOverlayImage.color = new Color(c.r, c.g, c.b, 0.25f);
        }
        // Ensure overlay stays visible: keep it on top of siblings
        try { tributeOverlay.transform.SetAsLastSibling(); } catch { }

        // Ensure overlay has its own sorting canvas at the top so it is visible regardless of hierarchy
        if (tributeOverlay != null)
        {
            var cv = tributeOverlay.GetComponent<Canvas>();
            if (cv == null) cv = tributeOverlay.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 1000;
            var gr = tributeOverlay.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (gr == null) gr = tributeOverlay.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var cg = tributeOverlay.GetComponent<CanvasGroup>();
            if (cg == null) cg = tributeOverlay.AddComponent<CanvasGroup>();
            cg.alpha = 1f; // visible
            cg.interactable = true; // buttons should work
            cg.blocksRaycasts = true; // buttons should receive clicks
        }
        if (tributeInstructionText != null)
        {
            tributeInstructionText.text = LocalizationSettings.StringDatabase.GetLocalizedString(
                "ui",
                "Tribute_Select",
                arguments: new object[] { new Dictionary<string, object> { ["required"] = cost, ["current"] = 0 } }
            );
        }
        UpdateTributeConfirmState();
        var nm = NetworkManager.Singleton;
        if (nm != null) lastActivePlayerId = nm.LocalClientId;
        UpdateBoardUI(null, null, null, null, null, null, currentScaleValue, lastActivePlayerId, null, null);
    }

    public void OnLaneCardClicked(int laneIndex)
    {
        if (!tributeMode) return;
        if (laneIndex < 0 || laneIndex >= 6) return;
        tributeSelected[laneIndex] = !tributeSelected[laneIndex];
        UpdateTributeConfirmState();
        UpdateBoardUI(null, null, null, null, null, null, currentScaleValue, lastActivePlayerId, null, null);
    }

    private void UpdateTributeConfirmState()
    {
        int count = 0; for (int i = 0; i < 6; i++) if (tributeSelected[i]) count++;
        if (tributeInstructionText != null)
        {
            tributeInstructionText.text = LocalizationSettings.StringDatabase.GetLocalizedString(
                "ui",
                "Tribute_Select",
                arguments: new object[] { new Dictionary<string, object> { ["required"] = requiredTributes, ["current"] = count } }
            );
        }
        if (tributeConfirmButton != null) tributeConfirmButton.interactable = (count == requiredTributes);
    }

    private void OnConfirmTributeClicked()
    {
        if (!tributeMode) return;
        int count = 0; for (int i = 0; i < 6; i++) if (tributeSelected[i]) count++;
        if (count != requiredTributes) return;
        var lanes = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 6; i++) if (tributeSelected[i]) lanes.Add(i);
        gameManager.RequestSummonServerRpc(pendingCardId, pendingTargetLane, lanes.ToArray());
        EndTributeSelection();
    }

    private void OnCancelTributeClicked()
    {
        if (!tributeMode) return;
        EndTributeSelection();
    }

    private void EndTributeSelection()
    {
        tributeMode = false;
        pendingCardId = -1;
        pendingTargetLane = -1;
        requiredTributes = 0;
        for (int i = 0; i < tributeSelected.Length; i++) tributeSelected[i] = false;
        if (tributeOverlay != null) tributeOverlay.SetActive(false);
        RefreshHandUI();
        // Refresh interactivity immediately (re-enable End Turn etc.)
        UpdateBoardUI(null, null, null, null, null, null, currentScaleValue, lastActivePlayerId, null, null);
    }

    private void ApplyHighlight(GameObject go, bool on)
    {
        // Prefer explicit child overlay if present
        var t = go.transform.Find("SelectionHighlight");
        if (t != null)
        {
            t.gameObject.SetActive(on);
            return;
        }

        // Fallback: enable/disable an Outline on the card's primary Image
        Image targetImg = null;
        var art = go.transform.Find("Art");
        if (art != null) targetImg = art.GetComponent<Image>();
        if (targetImg == null) targetImg = go.GetComponent<Image>();
        if (targetImg != null)
        {
            var outline = targetImg.GetComponent<Outline>();
            if (on)
            {
                if (outline == null) outline = targetImg.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.85f, 0.2f, 1f); // gold-ish border
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
                outline.enabled = true;
            }
            else
            {
                if (outline != null)
                {
                    outline.enabled = false;
                    Destroy(outline);
                }
            }
        }
    }

    private void CacheSlots()
    {
        if (tableSlotsParent == null) return;
        for (int i = 0; i < 6; i++)
        {
            if (p1Slots[i] == null)
            {
                var t = tableSlotsParent.Find($"P1_Lane_{i}");
                if (t != null) p1Slots[i] = t;
            }
            if (p2Slots[i] == null)
            {
                var t = tableSlotsParent.Find($"P2_Lane_{i}");
                if (t != null) p2Slots[i] = t;
            }
        }
    }
}
