using UnityEngine;

public class HostGameSettingsCarrier : MonoBehaviour
{
    private static HostGameSettingsCarrier _instance;
    public static HostGameSettingsCarrier Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HostGameSettingsCarrier");
                _instance = go.AddComponent<HostGameSettingsCarrier>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [Header("Host Settings")] 
    public string PlayerDisplayName = "Host";
    public string GameName = "Card Game";
    public int WinsToWin = 3; // 1-5
    public int RoundTimerSeconds = 0; // 0=Off, else 10/20/30
    public bool RequireBothPlayersForRematch = true;

    // Optionally, expose a method to apply to managers later
    public void ApplyWhenInGameScene()
    {
        // Integration point reserved for a future step
        // Example (requires adding setters on GameFlowManager):
        // var gfm = GameFlowManager.Instance;
        // if (gfm && gfm.IsServer) { gfm.SetWinsToWin(WinsToWin); }
    }
}
