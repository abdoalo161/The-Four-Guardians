using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OptionsPanelBinder : MonoBehaviour
{
    [Header("Optional UI Bindings (assign what's present on this panel)")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TextMeshProUGUI musicVolumeText;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TextMeshProUGUI sfxVolumeText;

    private void OnEnable()
    {
        // Bind immediately if ready; otherwise wait a short moment
        if (!TryBindNow())
        {
            StartCoroutine(BindWhenReady());
        }
        else
        {
            UpdateTexts();
        }
    }

    private void OnDisable()
    {
        if (masterVolumeSlider != null) masterVolumeSlider.onValueChanged.RemoveListener(OnMasterChanged);
        if (musicVolumeSlider != null) musicVolumeSlider.onValueChanged.RemoveListener(OnMusicChanged);
        if (sfxVolumeSlider != null) sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxChanged);
    }

    private void OnMasterChanged(float v)
    {
        if (OptionsManager.Instance == null) return;
        OptionsManager.Instance.SetMasterVolume(v);
        UpdateTexts();
    }

    private void OnMusicChanged(float v)
    {
        if (OptionsManager.Instance == null) return;
        OptionsManager.Instance.SetMusicVolume(v);
        UpdateTexts();
    }

    private void OnSfxChanged(float v)
    {
        if (OptionsManager.Instance == null) return;
        OptionsManager.Instance.SetSFXVolume(v);
        UpdateTexts();
    }

    private void UpdateTexts()
    {
        if (OptionsManager.Instance == null) return;
        if (masterVolumeText != null) masterVolumeText.text = Mathf.RoundToInt(OptionsManager.Instance.MasterVolume * 100) + "%";
        if (musicVolumeText != null) musicVolumeText.text = Mathf.RoundToInt(OptionsManager.Instance.MusicVolume * 100) + "%";
        if (sfxVolumeText != null) sfxVolumeText.text = Mathf.RoundToInt(OptionsManager.Instance.SFXVolume * 100) + "%";
    }

    public void Refresh()
    {
        TryBindNow();
        UpdateTexts();
    }

    private System.Collections.IEnumerator BindWhenReady()
    {
        float t = 0f;
        while (OptionsManager.Instance == null && t < 1f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        TryBindNow();
        // One extra frame to ensure UI objects are active before setting text
        yield return null;
        UpdateTexts();
    }

    private bool TryBindNow()
    {
        if (OptionsManager.Instance == null) return false;
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(OptionsManager.Instance.MasterVolume);
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterChanged);
            masterVolumeSlider.onValueChanged.AddListener(OnMasterChanged);
        }
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(OptionsManager.Instance.MusicVolume);
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicChanged);
            musicVolumeSlider.onValueChanged.AddListener(OnMusicChanged);
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.SetValueWithoutNotify(OptionsManager.Instance.SFXVolume);
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxChanged);
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxChanged);
        }
        return true;
    }
}
