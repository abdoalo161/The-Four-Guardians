using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OptionsManager : MonoBehaviour
{
    public static OptionsManager Instance { get; private set; }

    [Header("Optional UI Bindings (assign when attached to an OptionsPanel)")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TextMeshProUGUI musicVolumeText;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TextMeshProUGUI sfxVolumeText;

    private const string MASTER_VOLUME_KEY = "MasterVolume";
    private const string MUSIC_VOLUME_KEY = "MusicVolume";
    private const string SFX_VOLUME_KEY = "SFXVolume";

    public float MasterVolume { get; private set; } = 1.0f;
    public float MusicVolume { get; private set; } = 0.5f;
    public float SFXVolume { get; private set; } = 1.0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSettings();
        ApplySettings();
    }

    private void OnEnable()
    {
        // If UI is assigned (when this is on an OptionsPanel in a scene), bind and sync values
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = MasterVolume;
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = MusicVolume;
            musicVolumeSlider.onValueChanged.AddListener(SetMusicVolume);
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = SFXVolume;
            sfxVolumeSlider.onValueChanged.AddListener(SetSFXVolume);
        }
        UpdateUITexts();
    }

    private void OnDisable()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(SetMasterVolume);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(SetMusicVolume);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(SetSFXVolume);
    }

    public void SetMasterVolume(float volume)
    {
        MasterVolume = Mathf.Clamp01(volume);
        if (RPSAudioManager.Instance != null) RPSAudioManager.Instance.MasterVolume = MasterVolume;
        PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, MasterVolume);
        UpdateUITexts();
    }

    public void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp01(volume);
        if (RPSAudioManager.Instance != null) RPSAudioManager.Instance.MusicVolume = MusicVolume;
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, MusicVolume);
        UpdateUITexts();
    }

    public void SetSFXVolume(float volume)
    {
        SFXVolume = Mathf.Clamp01(volume);
        if (RPSAudioManager.Instance != null) RPSAudioManager.Instance.SFXVolume = SFXVolume;
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, SFXVolume);
        UpdateUITexts();
    }

    public void LoadSettings()
    {
        MasterVolume = PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1.0f);
        MusicVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 0.5f);
        SFXVolume = PlayerPrefs.GetFloat(SFX_VOLUME_KEY, 1.0f);
    }

    public void ApplySettings()
    {
        if (RPSAudioManager.Instance != null)
        {
            RPSAudioManager.Instance.MasterVolume = MasterVolume;
            RPSAudioManager.Instance.MusicVolume = MusicVolume;
            RPSAudioManager.Instance.SFXVolume = SFXVolume;
        }
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, MasterVolume);
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, MusicVolume);
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, SFXVolume);
        PlayerPrefs.Save();
    }

    private void UpdateUITexts()
    {
        if (masterVolumeText != null) masterVolumeText.text = Mathf.RoundToInt(MasterVolume * 100) + "%";
        if (musicVolumeText != null) musicVolumeText.text = Mathf.RoundToInt(MusicVolume * 100) + "%";
        if (sfxVolumeText != null) sfxVolumeText.text = Mathf.RoundToInt(SFXVolume * 100) + "%";
    }
}