using UnityEngine;
using UnityEngine.SceneManagement; // Required for scene management
using UnityEngine.UI; // Required for UI elements if you need to reference them directly
using Unity.Netcode; // Required for NetworkManager
using Netcode.Transports.Ruffles;
using System.Collections;

public class MainMenuManager : MonoBehaviour
{
    // Assign these methods to your buttons' OnClick() events in the Inspector

    private IEnumerator Start()
    {
        // Ensure any previous networking session is fully stopped when we land on the main menu
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            Debug.Log($"DIAGNOSTIC[Menu]: Entered MainMenu. IsClient={nm.IsClient}, IsServer={nm.IsServer}, IsListening={nm.IsListening}");
            if (nm.IsClient || nm.IsServer || nm.IsListening)
            {
                Debug.Log("DIAGNOSTIC[Menu]: Calling NetworkManager.Shutdown()...");
                nm.Shutdown();
            }
            float timeout = 3f;
            float elapsed = 0f;
            while ((nm.IsClient || nm.IsServer || nm.IsListening) && elapsed < timeout)
            {
                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
            Debug.Log($"DIAGNOSTIC[Menu]: After shutdown wait. IsClient={nm.IsClient}, IsServer={nm.IsServer}, IsListening={nm.IsListening}");

            // Reinitialize transport to avoid stale sockets between sessions (affects both host and client next starts)
            var oldTransport = nm.GetComponent<RufflesTransport>();
            if (oldTransport != null)
            {
                ushort port = oldTransport.Port != 0 ? oldTransport.Port : (ushort)7777;
                var go = nm.gameObject;
                Debug.Log($"DIAGNOSTIC[Menu]: Recreating RufflesTransport. OldPort={oldTransport.Port}");
                Destroy(oldTransport);
                // wait a frame so the component is fully removed
                yield return null;
                var newTransport = go.AddComponent<RufflesTransport>();
                newTransport.Port = port;
                nm.NetworkConfig.NetworkTransport = newTransport;
                Debug.Log($"DIAGNOSTIC[Menu]: New transport assigned. Port={newTransport.Port}");
            }
        }
        // Best-effort stop of any discovery broadcaster/listener without hard dependency
        var mbs = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < mbs.Length; i++)
        {
            var mb = mbs[i];
            if (mb == null || mb.gameObject == this.gameObject) continue;
            mb.SendMessage("StopAdvertising", SendMessageOptions.DontRequireReceiver);
            mb.SendMessage("StopDiscovery", SendMessageOptions.DontRequireReceiver);
        }
    }

    public void HostGame()
    {
        // Go to the Host Setup scene to configure settings before hosting
        SceneManager.LoadScene("HostSetupScene", LoadSceneMode.Single);
    }

    public void JoinGame()
    {
        // Simply load the LobbyScene. The LobbyManager will handle discovery.
        SceneManager.LoadScene("LobbyScene");
    }

    public void OpenOptions()
    {
        // Load the OptionsScene
        SceneManager.LoadScene("OptionsScene");
    }

    // You might want a Quit button as well
    public void QuitGame()
    {
        Application.Quit(); // Quits the application (only works in build, not in editor)
    }
}