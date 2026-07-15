using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class LanguageDropdownBinder : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private string playerPrefsKey = "Game_Language"; // values like "en", "ar"
    // Order must match the dropdown options you create in the Editor
    [SerializeField] private string[] localeCodes = new[] { "en", "ar" };

    private bool _initialized;

    private void OnEnable()
    {
        if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();
        StartCoroutine(InitCo());
        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        if (dropdown != null)
            dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    private void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        if (dropdown != null)
            dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
    }

    private IEnumerator InitCo()
    {
        // Ensure Localization is ready
        var op = LocalizationSettings.InitializationOperation;
        if (!op.IsDone)
            yield return op;

        // Apply saved language if present
        var saved = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(saved))
        {
            var locale = FindLocaleByCode(saved);
            if (locale != null && LocalizationSettings.SelectedLocale != locale)
            {
                LocalizationSettings.SelectedLocale = locale;
            }
        }

        // Sync dropdown selection to current locale
        SyncDropdownToLocale(LocalizationSettings.SelectedLocale);
        _initialized = true;
    }

    private void OnDropdownChanged(int index)
    {
        if (!_initialized) return;
        if (index < 0 || index >= localeCodes.Length) return;
        string code = localeCodes[index];
        var locale = FindLocaleByCode(code);
        if (locale != null)
        {
            LocalizationSettings.SelectedLocale = locale;
            PlayerPrefs.SetString(playerPrefsKey, code);
            PlayerPrefs.Save();
        }
    }

    private void OnSelectedLocaleChanged(Locale locale)
    {
        SyncDropdownToLocale(locale);
    }

    private void SyncDropdownToLocale(Locale locale)
    {
        if (dropdown == null || locale == null) return;
        string code = locale.Identifier.Code.ToLowerInvariant();
        int idx = System.Array.FindIndex(localeCodes, c => string.Equals(c, code, System.StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx < dropdown.options.Count)
        {
            dropdown.SetValueWithoutNotify(idx);
        }
    }

    private Locale FindLocaleByCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        code = code.ToLowerInvariant();
        var locales = LocalizationSettings.AvailableLocales.Locales;
        // Match exact code (e.g., "ar", "en"). If project uses variants ("en-US"), match prefix.
        var exact = locales.FirstOrDefault(l => l != null && l.Identifier.Code.ToLowerInvariant() == code);
        if (exact != null) return exact;
        return locales.FirstOrDefault(l => l != null && l.Identifier.Code.ToLowerInvariant().StartsWith(code));
    }
}
