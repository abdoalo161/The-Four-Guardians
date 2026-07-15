using UnityEngine;
using UnityEngine.UI;

public class ThemeApplier : MonoBehaviour
{
    [Header("Theme Library")]
    public ThemeLibrary Library;

    [Header("Targets")] 
    public Image BackgroundImage;
    public Image[] P1Slots = new Image[6];
    public Image[] P2Slots = new Image[6];

    [Header("Preview (fallback)")]
    public Element P1ElementTest = Element.Fire;
    public Element P2ElementTest = Element.Water;

    private void Start()
    {
        // Try to use live network player selections; fallback to preview if not available.
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsConnectedClient && nm.LocalClient != null)
        {
            RPSPlayer local = nm.LocalClient.PlayerObject ? nm.LocalClient.PlayerObject.GetComponent<RPSPlayer>() : null;
            RPSPlayer remote = null;
            var all = FindObjectsByType<RPSPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var p in all)
            {
                if (p != null && p != local)
                {
                    remote = p; break;
                }
            }

            if (local != null && remote != null && local.SelectedCharacter.Value >= 0 && remote.SelectedCharacter.Value >= 0)
            {
                var p1 = (Element)Mathf.Clamp(local.SelectedCharacter.Value, 0, 3);
                var p2 = (Element)Mathf.Clamp(remote.SelectedCharacter.Value, 0, 3);
                ApplyThemes(p1, p2);
                return;
            }
        }

        // Fallback
        ApplyThemes(P1ElementTest, P2ElementTest);
    }

    public void ApplyThemes(Element p1Element, Element p2Element)
    {
        if (Library == null) { Debug.LogWarning("ThemeApplier: Library is not assigned."); return; }
        var p1Theme = Library.GetTheme(p1Element);
        var p2Theme = Library.GetTheme(p2Element);
        if (p1Theme == null || p2Theme == null) { Debug.LogWarning("ThemeApplier: Missing theme(s) in library."); return; }

        // Background uses the local player's theme for now. This will be set based on local perspective later.
        if (BackgroundImage != null && p1Theme.Background != null)
            BackgroundImage.sprite = p1Theme.Background;

        // Apply per-side slot sprites
        for (int i = 0; i < P1Slots.Length; i++)
        {
            if (P1Slots[i] != null && p1Theme.SlotSprite != null)
                P1Slots[i].sprite = p1Theme.SlotSprite;
        }
        for (int i = 0; i < P2Slots.Length; i++)
        {
            if (P2Slots[i] != null && p2Theme.SlotSprite != null)
                P2Slots[i].sprite = p2Theme.SlotSprite;
        }
    }
}
