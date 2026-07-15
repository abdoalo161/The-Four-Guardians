using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RPSAudioManager : MonoBehaviour
{
    public static RPSAudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource musicSource;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip choiceSound;

    [Header("Music - Background")]
    [SerializeField] private string cardGameSceneName = "CardGameScene";
    [SerializeField] private AudioClip menuAndLobbyMusic; // Main menu, Options, Lobby
    [SerializeField] private string[] menuSceneNames = new [] { "MainMenuScene", "OptionsScene", "LobbyScene" };
    [SerializeField] private string tossSceneName = "RPSTossScene";
    [SerializeField] private AudioClip tossSceneMusic;

    [Header("Music - Card Game Character Themes")]
    [SerializeField] private AudioClip fireTheme;
    [SerializeField] private AudioClip waterTheme;
    [SerializeField] private AudioClip airTheme;
    [SerializeField] private AudioClip earthTheme;

    [Header("Music - Match Point")]
    [SerializeField] private AudioClip matchPointMusicWinning; // Local is winning (has match point, opponent does not)
    [SerializeField] private AudioClip matchPointMusicLosing;  // Local is losing (opponent has match point, local does not)

    // 0 = none, 1 = local at match point, 2 = opponent at match point
    private int lastMatchPointState = 0;
    private AudioClip currentMusicClip = null;
    private AudioClip currentBackgroundClip = null; // chosen per scene or character

    private void DLog(string msg) { /* stripped */ }

    // RPS flow decoupled; no delayed init needed

    [Header("Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float _masterVolume = 1f; // Backing field
    public float MasterVolume
    {
        get { return _masterVolume; }
        set
        {
            _masterVolume = Mathf.Clamp01(value);
            if (sfxSource != null) sfxSource.volume = _sfxVolume * _masterVolume;
            if (musicSource != null) musicSource.volume = _musicVolume * _masterVolume;
        }
    }
    [Range(0f, 1f)]
    [SerializeField] private float _sfxVolume = 1f; // Backing field
    public float SFXVolume
    {
        get { return _sfxVolume; }
        set
        {
            _sfxVolume = Mathf.Clamp01(value);
            if (sfxSource != null) sfxSource.volume = _sfxVolume * _masterVolume;
        }
    }

    private System.Collections.IEnumerator SelectCardGameThemeAndPlay()
    {
        // Wait briefly for player object to exist and SelectedCharacter to be valid
        float t = 0f;
        const float timeout = 2f;
        int sel = -1;
        while (t < timeout)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
            {
                var rps = nm.LocalClient.PlayerObject.GetComponent<RPSPlayer>();
                if (rps != null && rps.SelectedCharacter.Value >= 0)
                {
                    sel = Mathf.Clamp(rps.SelectedCharacter.Value, 0, 3);
                    break;
                }
            }
            t += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        AudioClip chosen = null;
        switch (sel)
        {
            case 0: chosen = fireTheme; break;
            case 1: chosen = waterTheme; break;
            case 2: chosen = airTheme; break;
            case 3: chosen = earthTheme; break;
            default: chosen = menuAndLobbyMusic; break;
        }
        currentBackgroundClip = chosen;
        if (lastMatchPointState == 0)
        {
            PlayBackgroundMusic();
        }
    }

    private void OnScoresChangedForMatchPoint(int p1Score, int p2Score) { }

    public void SetMatchPointState(int newState)
    {
        if (newState == lastMatchPointState) return;
        lastMatchPointState = newState;
        switch (newState)
        {
            case 1:
                PlayMusic(matchPointMusicWinning, true);
                break;
            case 2:
                PlayMusic(matchPointMusicLosing, true);
                break;
            default:
                PlayBackgroundMusic();
                break;
        }
    }

    [Range(0f, 1f)]
    [SerializeField] private float _musicVolume = 0.5f; // Backing field
    public float MusicVolume
    {
        get { return _musicVolume; }
        set
        {
            _musicVolume = Mathf.Clamp01(value);
            if (musicSource != null) musicSource.volume = _musicVolume * _masterVolume;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // Keep audio manager alive across scenes
        EnsureAudioSources();
    }

    void OnEnable()
    {
        // Subscribe to scene loaded event for generic button click SFX
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Find all buttons in the newly loaded scene and add a listener to play choice sound
        Button[] allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Button btn in allButtons)
        {
            // Ensure we don't stack duplicate listeners across scene loads
            btn.onClick.RemoveListener(PlayChoiceSound);
            btn.onClick.AddListener(PlayChoiceSound);
        }

        // Choose background by scene
        bool isCardGame = string.Equals(scene.name, cardGameSceneName);
        bool isToss = string.Equals(scene.name, tossSceneName);
        bool isMenuLike = false;
        if (menuSceneNames != null)
        {
            for (int i = 0; i < menuSceneNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(menuSceneNames[i]) && string.Equals(scene.name, menuSceneNames[i])) { isMenuLike = true; break; }
            }
        }

        if (isMenuLike)
        {
            currentBackgroundClip = menuAndLobbyMusic;
            lastMatchPointState = 0; // neutral state
            PlayBackgroundMusic();
        }
        else if (isToss)
        {
            currentBackgroundClip = tossSceneMusic;
            lastMatchPointState = 0; // neutral state
            PlayBackgroundMusic();
        }
        else if (isCardGame)
        {
            // In CardGameScene use the local player's character theme as the background when not at match point
            StartCoroutine(SelectCardGameThemeAndPlay());
        }
        else
        {
            // Fallback for any other scenes
            currentBackgroundClip = menuAndLobbyMusic;
            lastMatchPointState = 0; // neutral state
            PlayBackgroundMusic();
        }
    }

    void Start()
    {
        // Set initial volumes based on serialized fields (or loaded PlayerPrefs if applicable)
        SFXVolume = _sfxVolume; // Trigger setter to update AudioSource
        MusicVolume = _musicVolume; // Trigger setter to update AudioSource

        // Choose an initial background based on the active scene (covers first scene load)
        var scene = SceneManager.GetActiveScene();
        bool isCardGame = string.Equals(scene.name, cardGameSceneName);
        bool isToss = string.Equals(scene.name, tossSceneName);
        bool isMenuLike = false;
        if (menuSceneNames != null)
        {
            for (int i = 0; i < menuSceneNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(menuSceneNames[i]) && string.Equals(scene.name, menuSceneNames[i])) { isMenuLike = true; break; }
            }
        }

        if (isMenuLike)
        {
            currentBackgroundClip = menuAndLobbyMusic;
            SetMatchPointState(0);
        }
        else if (isToss)
        {
            currentBackgroundClip = tossSceneMusic;
            SetMatchPointState(0);
        }
        else if (isCardGame)
        {
            StartCoroutine(SelectCardGameThemeAndPlay());
        }
        else
        {
            currentBackgroundClip = menuAndLobbyMusic;
            SetMatchPointState(0);
        }

        // Ensure we have the chosen background music playing at startup
        PlayBackgroundMusic();

        // Diagnostics to help catch client-only missing refs
        if (sfxSource == null)
        {
            Debug.LogError("AUDIO: sfxSource is NULL on this client; win/lose/draw SFX will not play.");
        }
        if (musicSource == null)
        {
            Debug.LogError("AUDIO: musicSource is NULL on this client; background/match-point music will not play.");
        }
        else
        {
            if (matchPointMusicWinning == null)
            {
                Debug.LogWarning("AUDIO: matchPointMusicWinning clip is not assigned; winning-state track will not play.");
            }
            if (matchPointMusicLosing == null)
            {
                Debug.LogWarning("AUDIO: matchPointMusicLosing clip is not assigned; losing-state track will not play.");
            }
        }
    }

    // Old RPS handlers removed; using CardGame match-point instead

    private void OnUnifiedResultChanged(string result) { }

    // RPS-only helpers removed

    private bool TryResolveLocalIsP1() { return false; }

    // Old RPS initialization removed

    void OnGameEnded(string message) { }

    public void PlayChoiceSound()
    {
        PlaySFX(choiceSound);
    }

    void PlaySFX(AudioClip clip)
    {
        if (sfxSource == null)
        {
            Debug.LogError("AUDIO: PlaySFX called but sfxSource is NULL.");
            return;
        }
        if (clip == null)
        {
            Debug.LogWarning("AUDIO: PlaySFX called with NULL clip; no SFX will play.");
            return;
        }
        DLog($"PlaySFX: '{clip.name}' at volume={sfxSource.volume:F2}");
        sfxSource.PlayOneShot(clip);
    }

    private void PlayMusic(AudioClip clip, bool loop)
    {
        if (musicSource == null)
        {
            Debug.LogError("AUDIO: PlayMusic called but musicSource is NULL.");
            return;
        }
        if (clip == null)
        {
            Debug.LogWarning("AUDIO: PlayMusic called with NULL clip; no music will play.");
            return;
        }

        if (musicSource.clip == clip && musicSource.isPlaying)
            return; // Already playing desired track

        musicSource.loop = loop;
        musicSource.clip = clip;
        musicSource.Play();
        currentMusicClip = clip;
    }

    private void PlayBackgroundMusic()
    {
        if (musicSource == null)
        {
            Debug.LogError("AUDIO: PlayBackgroundMusic called but musicSource is NULL.");
            return;
        }
        var clip = currentBackgroundClip;
        if (clip == null)
        {
            Debug.LogWarning("AUDIO: PlayBackgroundMusic called but no background clip is assigned for this scene/state.");
            return;
        }

        if (musicSource.clip == clip && musicSource.isPlaying)
            return;

        musicSource.loop = true;
        musicSource.clip = clip;
        musicSource.Play();
        currentMusicClip = clip;
    }

    private void StopMusic()
    {
        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
        currentMusicClip = null;
        lastMatchPointState = 0;
    }

    private void EnsureAudioSources()
    {
        // Ensure SFX AudioSource exists
        if (sfxSource == null)
        {
            // Try to find an existing non-music AudioSource
            var sources = GetComponents<AudioSource>();
            foreach (var src in sources)
            {
                if (musicSource != null && src == musicSource) continue;
                sfxSource = src;
                break;
            }
            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.volume = _sfxVolume;
        }

        // Ensure Music AudioSource exists
        if (musicSource == null)
        {
            // Try to find another AudioSource not used by SFX
            var sources = GetComponents<AudioSource>();
            foreach (var src in sources)
            {
                if (sfxSource != null && src == sfxSource) continue;
                musicSource = src;
                break;
            }
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
            }
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.volume = _musicVolume;
        }
    }
}