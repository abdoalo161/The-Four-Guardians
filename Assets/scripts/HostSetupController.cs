using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class HostSetupController : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private TMP_InputField gameNameInput;
    // Wins-to-win and rematch toggle removed; use defaults from HostGameSettingsCarrier
    // character selection is handled by CharacterSelectionController; no dropdown here

    // Selected character/element index: 0=Fire,1=Water,2=Air,3=Earth
    private int selectedCharacterIndex = 0;

    private void Awake()
    {
        // Pre-fill from carrier
        var settings = HostGameSettingsCarrier.Instance;
        // Load saved values from PlayerPrefs with carrier defaults as fallbacks
        if (gameNameInput != null)
        {
            string saved = PlayerPrefs.GetString("Host_GameName", settings.GameName);
            // Migrate old default to new default
            if (string.IsNullOrWhiteSpace(saved) || saved == "Rock Paper Scissors Game")
            {
                saved = "Card Game";
            }
            gameNameInput.text = saved;
        }
        // Wins/Rematch UI removed

        // Load previously selected character (set elsewhere by CharacterSelectionController)
        int savedChar = PlayerPrefs.GetInt("SelectedCharacter", 0);
        selectedCharacterIndex = Mathf.Clamp(savedChar, 0, 3);
    }

    // Wins/Rematch UI callbacks removed

    public void OnStartHosting()
    {
        // Pull latest values from inputs
        var settings = HostGameSettingsCarrier.Instance;
        if (gameNameInput != null)
            settings.GameName = string.IsNullOrWhiteSpace(gameNameInput.text) ? "Card Game" : gameNameInput.text.Trim();
        // Wins/Rematch removed: keep existing defaults from carrier

        // Persist to PlayerPrefs for next sessions
        PlayerPrefs.SetString("Host_GameName", settings.GameName);
        // Wins/Rematch preferences removed
        // SelectedCharacter: honor value set by CharacterSelectionController
        selectedCharacterIndex = Mathf.Clamp(PlayerPrefs.GetInt("SelectedCharacter", selectedCharacterIndex), 0, 3);
        PlayerPrefs.SetInt("SelectedCharacter", selectedCharacterIndex);
        PlayerPrefs.Save();

        // Start Host and go to lobby
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("HostSetupController: NetworkManager.Singleton is null.");
            return;
        }

        // If manager is still running (e.g., came back from a previous session), shut it down first
        var nm = NetworkManager.Singleton;
        if (nm.IsServer || nm.IsClient || nm.IsListening)
        {
            Debug.LogWarning("HostSetup: Detected running NetworkManager. Shutting down before restarting host...");
            nm.Shutdown();
        }

        StartCoroutine(StartHostAfterShutdown());
    }

    private System.Collections.IEnumerator StartHostAfterShutdown()
    {
        var nm = NetworkManager.Singleton;
        float timeout = 2f;
        float elapsed = 0f;
        while (elapsed < timeout && (nm.IsServer || nm.IsClient || nm.IsListening))
        {
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        // Reinitialize transport to clear any stale state by recreating the component
        var oldTransport = nm != null ? nm.GetComponent<Netcode.Transports.Ruffles.RufflesTransport>() : null;
        if (oldTransport != null)
        {
            ushort port = oldTransport.Port != 0 ? oldTransport.Port : (ushort)7777;
            var go = nm.gameObject;
            UnityEngine.Object.Destroy(oldTransport);
            // wait a frame so the component is fully removed
            yield return null;
            var newTransport = go.AddComponent<Netcode.Transports.Ruffles.RufflesTransport>();
            newTransport.Port = port;
            // Assign as active transport so NetworkManager uses the new instance
            nm.NetworkConfig.NetworkTransport = newTransport;
        }

        if (nm.StartHost())
        {
            Debug.Log("HostSetup: Host started. Loading LobbyScene...");
            nm.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("HostSetup: Failed to start host.");
        }
    }

    public void OnBack()
    {
        // Return to main menu without starting host
        SceneManager.LoadScene("MainMenuScene", LoadSceneMode.Single);
    }
}
