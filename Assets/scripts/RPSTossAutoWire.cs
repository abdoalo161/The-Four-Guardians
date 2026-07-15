using UnityEngine;
using UnityEngine.UI;

public class RPSTossAutoWire : MonoBehaviour
{
    [Header("Optional: assign directly; otherwise we'll search by name under Container")] 
    public Button RockButton;
    public Button PaperButton;
    public Button ScissorsButton;

    [Header("Search root (optional). If set, will search children named 'rock', 'paper', 'scissors' (case-insensitive)")]
    public Transform Container;

    private RPSTossUIProxy proxy;

    private void Awake()
    {
        proxy = Object.FindFirstObjectByType<RPSTossUIProxy>(FindObjectsInactive.Include);
        if (proxy == null)
        {
            Debug.LogWarning("[RPSTossAutoWire] RPSTossUIProxy not found in scene; buttons will not be wired.");
            return;
        }

        if (RockButton == null || PaperButton == null || ScissorsButton == null)
        {
            TryFindButtons();
        }

        WireButton(RockButton, proxy.OnRockClick, "rock");
        WireButton(PaperButton, proxy.OnPaperClick, "paper");
        WireButton(ScissorsButton, proxy.OnScissorsClick, "scissors");
    }

    private void TryFindButtons()
    {
        if (Container == null) Container = transform;
        foreach (var btn in Container.GetComponentsInChildren<Button>(true))
        {
            var name = btn.gameObject.name.ToLowerInvariant();
            if (name.Contains("rock") && RockButton == null) RockButton = btn;
            else if (name.Contains("paper") && PaperButton == null) PaperButton = btn;
            else if (name.Contains("scissors") && ScissorsButton == null) ScissorsButton = btn;
        }
    }

    private void WireButton(Button btn, UnityEngine.Events.UnityAction action, string label)
    {
        if (btn == null)
        {
            Debug.LogWarning($"[RPSTossAutoWire] Could not find '{label}' button to wire.");
            return;
        }
        // Don't remove existing listeners; just add ours
        btn.onClick.AddListener(action);
        Debug.Log($"[RPSTossAutoWire] Wired '{btn.gameObject.name}' to proxy {label} click.");
    }
}
