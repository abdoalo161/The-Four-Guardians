using System.Collections;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class LocalizationBootstrapper : MonoBehaviour
{
    [SerializeField] private string playerPrefsKey = "Game_Language"; // expects codes like "en", "ar"

    private void OnEnable()
    {
        StartCoroutine(ApplySavedLocale());
    }

    private IEnumerator ApplySavedLocale()
    {
        var op = LocalizationSettings.InitializationOperation;
        if (!op.IsDone)
            yield return op;

        string saved = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(saved))
            yield break;

        saved = saved.ToLowerInvariant();
        Locale target = null;
        var locales = LocalizationSettings.AvailableLocales.Locales;
        for (int i = 0; i < locales.Count; i++)
        {
            var loc = locales[i];
            if (loc == null) continue;
            var code = loc.Identifier.Code.ToLowerInvariant();
            if (code == saved || code.StartsWith(saved))
            {
                target = loc;
                break;
            }
        }
        if (target != null && LocalizationSettings.SelectedLocale != target)
        {
            LocalizationSettings.SelectedLocale = target;
        }
    }
}
