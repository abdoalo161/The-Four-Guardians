using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Netcode.Transports.Ruffles;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine.Localization.Settings;

public class LobbyManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject waitingForPlayersPanel;
    [SerializeField] private GameObject discoveredGamesPanel;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI waitingText;
    [SerializeField] private Transform gameListContent; // The parent transform for game buttons
    [SerializeField] private Button backToMainMenuButton; // Shown to host while waiting

    [Header("Manual Connection UI")]
    [SerializeField] private TMP_InputField ipAddressInput;
    [SerializeField] private Button manualJoinButton;
    [SerializeField] private TextMeshProUGUI hostIpText;

    // Client display name UI removed

    [Header("Prefabs")]
    [SerializeField] private Button gameButtonPrefab;

    private RufflesTransport rufflesTransport;
    private RPSNetworkDiscovery networkDiscovery;
    private Dictionary<IPEndPoint, Button> discoveredGames = new Dictionary<IPEndPoint, Button>();
    private bool isManualConnecting = false;

    void Awake()
    {
        // ** THE FIX IS HERE **
        // We must ensure the dispatcher is created on the main thread before any network threads can try to access it.
        UnityMainThreadDispatcher.Instance(); 

        // Ensure we have a network discovery component
        if (!TryGetComponent<RPSNetworkDiscovery>(out networkDiscovery))
        {
            networkDiscovery = gameObject.AddComponent<RPSNetworkDiscovery>();
        }
    }

    private IEnumerator SetHostIpLocalizedAfterInit()
    {
        var init = UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;
        if (!init.IsDone)
        {
            yield return init;
        }
        if (hostIpText != null && networkDiscovery != null)
        {
            hostIpText.text = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetLocalizedString(
                "ui",
                "Lobby_YourIP",
                arguments: new object[] { new System.Collections.Generic.Dictionary<string, object> { ["ip"] = networkDiscovery.GetLocalIP() } }
            );
            hostIpText.gameObject.SetActive(true);
        }
    }

    void OnEnable()
    {
        RPSNetworkDiscovery.OnServerFound += HandleServerFound;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    void OnDisable()
    {
        RPSNetworkDiscovery.OnServerFound -= HandleServerFound;
        if (networkDiscovery != null)
        {
            networkDiscovery.StopDiscovery();
        }
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
        if (backToMainMenuButton != null)
        {
            backToMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
        }
    }

    void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found! Make sure it's in the MainMenuScene and persists.");
            return;
        }

        rufflesTransport = NetworkManager.Singleton.GetComponent<RufflesTransport>();
        Debug.Log($"DIAGNOSTIC: Lobby Start. IsHost={NetworkManager.Singleton.IsHost}, IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}");
        Debug.Log($"DIAGNOSTIC: Transport present={(rufflesTransport!=null)}, Port={(rufflesTransport!=null ? rufflesTransport.Port : 0)}");

        if (NetworkManager.Singleton.IsHost)
        {
            waitingForPlayersPanel.SetActive(true);
            discoveredGamesPanel.SetActive(false);
            networkDiscovery.StartBroadcasting();
            Debug.Log($"DIAGNOSTIC: HOST broadcasting. LocalIP={networkDiscovery?.GetLocalIP()}");
            if (hostIpText != null)
            {
                var init = LocalizationSettings.InitializationOperation;
                if (init.IsDone)
                {
                    hostIpText.text = LocalizationSettings.StringDatabase.GetLocalizedString(
                        "ui",
                        "Lobby_YourIP",
                        arguments: new object[] { new System.Collections.Generic.Dictionary<string, object> { ["ip"] = networkDiscovery.GetLocalIP() } }
                    );
                    hostIpText.gameObject.SetActive(true);
                }
                else
                {
                    StartCoroutine(SetHostIpLocalizedAfterInit());
                }
            }
            
            // Add callback to handle when clients connect to the host
            NetworkManager.Singleton.OnClientConnectedCallback += OnHostClientConnected;
            if (ipAddressInput != null) ipAddressInput.gameObject.SetActive(false);
            if (manualJoinButton != null)
            {
                manualJoinButton.gameObject.SetActive(false);
                manualJoinButton.onClick.RemoveListener(OnJoinManuallyButtonClicked);
            }

            // Join name input removed

            // Ensure host can return to the Main Menu while waiting
            if (backToMainMenuButton != null)
            {
                backToMainMenuButton.gameObject.SetActive(true);
                backToMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
                backToMainMenuButton.onClick.AddListener(ReturnToMainMenu);
            }

            // Also set the host's SelectedCharacter NetworkVariable immediately
            TrySetHostCharacterForLocalPlayer();
        }
        else
        {
            waitingForPlayersPanel.SetActive(false);
            discoveredGamesPanel.SetActive(true);
            ClearDiscoveredGames();
            networkDiscovery.StartListening();
            Debug.Log("DIAGNOSTIC: CLIENT listening for broadcasts.");
            if (hostIpText != null) hostIpText.gameObject.SetActive(false);
            if (ipAddressInput != null)
            {
                ipAddressInput.gameObject.SetActive(true);
                // Prefill last used IP (simple persistence)
                string savedIp = PlayerPrefs.GetString("Client_LastIPAddress", string.Empty);
                if (!string.IsNullOrWhiteSpace(savedIp))
                {
                    ipAddressInput.text = savedIp;
                }
            }
            if (manualJoinButton != null)
            {
                manualJoinButton.gameObject.SetActive(true);
                manualJoinButton.onClick.RemoveListener(OnJoinManuallyButtonClicked);
                manualJoinButton.onClick.AddListener(OnJoinManuallyButtonClicked);
                manualJoinButton.interactable = true;
            }
            // Join name input removed
            // Hide back-to-main-menu for clients; host-only
            if (backToMainMenuButton != null)
            {
                backToMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
                backToMainMenuButton.gameObject.SetActive(false);
            }
        }
    }

    private void HandleServerFound(IPEndPoint endPoint, string serverName)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (!discoveredGames.ContainsKey(endPoint))
            {
                Button gameButton = Instantiate(gameButtonPrefab, gameListContent);
                gameButton.GetComponentInChildren<TextMeshProUGUI>().text = $"{serverName} [{endPoint.Address}]";
                
                string ipAddress = endPoint.Address.ToString();
                gameButton.onClick.AddListener(() => 
                {
                    JoinGame(ipAddress);
                });
                
                discoveredGames.Add(endPoint, gameButton);
            }
        });
    }

    public void OnJoinManuallyButtonClicked()
    {
        if (ipAddressInput != null && !string.IsNullOrEmpty(ipAddressInput.text))
        {
            if (isManualConnecting)
            {
                Debug.LogWarning("DIAGNOSTIC: Manual join already in progress; ignoring duplicate click.");
                return;
            }
            JoinGame(ipAddressInput.text.Trim());
        }
        else
        {
            Debug.LogWarning("IP Address input is empty.");
        }
    }

    public void JoinGame(string ipAddress)
    {
        Debug.Log($"DIAGNOSTIC[Join]: Requested JoinGame with ip='{ipAddress}'");
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("DIAGNOSTIC: NetworkManager.Singleton is NULL.");
            return;
        }

        // Ensure we're not already connected or connecting (do NOT shutdown here; avoid churn)
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("DIAGNOSTIC: Already connected or connecting; ignoring duplicate manual join.");
            return;
        }

        if (rufflesTransport == null)
        {
            Debug.LogError("DIAGNOSTIC: rufflesTransport is NULL. Re-fetching from NetworkManager.");
            rufflesTransport = NetworkManager.Singleton.GetComponent<RufflesTransport>();
            if (rufflesTransport == null)
            {
                Debug.LogError("DIAGNOSTIC: Failed to re-fetch rufflesTransport. Aborting.");
                return;
            }
        }

        // Validate IP address format
        if (string.IsNullOrEmpty(ipAddress) || ipAddress.Trim().Length == 0)
        {
            Debug.LogError("DIAGNOSTIC: Invalid IP address provided.");
            return;
        }

        string cleanIP = ipAddress.Trim();
        
        // Validate IP format
        if (!System.Net.IPAddress.TryParse(cleanIP, out System.Net.IPAddress parsedIP))
        {
            Debug.LogError($"DIAGNOSTIC: Invalid IP address format: '{cleanIP}'");
            return;
        }
        
        // Persist last used IP for convenience
        PlayerPrefs.SetString("Client_LastIPAddress", cleanIP);
        PlayerPrefs.Save();
        
        rufflesTransport.ConnectAddress = cleanIP;
        // Ensure the expected game port
        if (rufflesTransport.Port != 7777)
        {
            rufflesTransport.Port = 7777;
        }
        Debug.Log($"DIAGNOSTIC[Join]: Transport prepared addr='{rufflesTransport.ConnectAddress}', port={rufflesTransport.Port}");

        // Add connection callbacks (ensure single subscription)
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Guard against duplicate attempts via UI
        isManualConnecting = true;
        if (manualJoinButton != null) manualJoinButton.interactable = false;

        // Stop discovery BEFORE attempting to connect to avoid socket/thread interference
        if (networkDiscovery != null)
        {
            Debug.Log("DIAGNOSTIC[Join]: Stopping discovery before StartClient.");
            networkDiscovery.StopDiscovery();
        }
        Debug.Log("DIAGNOSTIC[Join]: Calling StartClient()...");
        bool startResult = NetworkManager.Singleton.StartClient();
        
        if (startResult)
        {
            Debug.Log("DIAGNOSTIC[Join]: StartClient() returned true. Waiting for connection callback or timeout.");
            // Start monitoring connection status (will reset UI on success/timeout)
            StartCoroutine(MonitorManualConnection());
        }
        else
        {
            Debug.LogError($"DIAGNOSTIC: StartClient() returned false. Failed to start client for IP: {cleanIP}");
            Debug.LogError($"DIAGNOSTIC: Final transport address: '{rufflesTransport.ConnectAddress}', port: {rufflesTransport.Port}");
            Debug.LogError($"DIAGNOSTIC: NetworkManager state after failed start - IsClient: {NetworkManager.Singleton.IsClient}, IsHost: {NetworkManager.Singleton.IsHost}");
            Debug.LogError("DIAGNOSTIC: Possible causes: Host not running, firewall blocking port 7777, invalid IP, or network unreachable.");
            
            // Clean up callbacks
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            // Reset UI/flags
            isManualConnecting = false;
            if (manualJoinButton != null) manualJoinButton.interactable = true;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"DIAGNOSTIC: OnClientConnected fired. clientId={clientId}, localId={NetworkManager.Singleton.LocalClientId}");
        // Stop network discovery when any client connects
        if (networkDiscovery != null)
        {
            networkDiscovery.StopDiscovery();
        }
        
        // If this is the local client connecting, just log success
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // Names disabled: skip sending display name
            // Send chosen character/element to server for this client
            TrySendLocalCharacter();
            // NOTE: Scene loading will be handled by the server/host automatically
            // Clients cannot load networked scenes - only the server can do this
            // Reset connecting guard; keep button disabled to prevent churn
            isManualConnecting = false;
            if (manualJoinButton != null) manualJoinButton.interactable = false;
        }
        else { }
    }

    private void OnHostClientConnected(ulong clientId)
    {
        
        // If this is not the host itself connecting, and we have at least 2 clients (host + 1 client)
        if (clientId != NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.ConnectedClients.Count >= 2)
        {
            // Stop broadcasting since we have enough players
            if (networkDiscovery != null)
            {
                networkDiscovery.StopDiscovery();
            }
            
            // Load the GameScene for all connected clients (server authority)
            try
            {
                NetworkManager.Singleton.SceneManager.LoadScene("RPSTossScene", LoadSceneMode.Single);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"DIAGNOSTIC: HOST - Failed to load RPSTossScene: {e.Message}");
            }
        }
        else { }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"DIAGNOSTIC: OnClientDisconnected fired. clientId={clientId}, localId={NetworkManager.Singleton.LocalClientId}, IsClient={NetworkManager.Singleton.IsClient}, IsConnectedClient={NetworkManager.Singleton.IsConnectedClient}");
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.LogError("DIAGNOSTIC: Disconnected from host!");
            // Clean up callbacks
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            // Reset UI/flags so user can retry
            isManualConnecting = false;
            if (manualJoinButton != null) manualJoinButton.interactable = true;
        }
    }

    // MonitorConnectionStatus method removed - clients cannot load networked scenes
    // Scene loading is handled automatically by the server/host

    private IEnumerator RetryConnectionAfterShutdown(string ipAddress)
    {
        // Wait for shutdown to complete
        yield return new WaitForSeconds(1f);
        JoinGame(ipAddress);
    }
    
    private IEnumerator MonitorManualConnection()
    {
        float timeout = 10f; // 10 second timeout
        float elapsed = 0f;
        bool wasConnecting = true;
        
        while (elapsed < timeout)
        {
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                isManualConnecting = false;
                yield break;
            }
            
            // Check if we're still in a connecting state
            if (!NetworkManager.Singleton.IsClient)
            {
                if (wasConnecting)
                {
                    Debug.LogWarning("DIAGNOSTIC: Connection attempt ended - this may be normal if retrying or if connection failed");
                    wasConnecting = false;
                }
                yield break;
            }
            
            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        
        Debug.LogWarning("DIAGNOSTIC: Manual connection monitoring timed out after 10 seconds");
        
        if (!NetworkManager.Singleton.IsConnectedClient)
        {
            Debug.LogError("DIAGNOSTIC: Failed to establish connection. Check host IP, network connectivity, and firewall settings.");
            // Reset UI/flags after failure
            isManualConnecting = false;
            if (manualJoinButton != null) manualJoinButton.interactable = true;
        }
    }

    void OnDestroy()
    {
        // Clean up all callbacks when LobbyManager is destroyed
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnHostClientConnected;
        }
    }

    public void ReturnToMainMenu()
    {
        StartCoroutine(ReturnToMainMenuRoutine());
    }
    
    private System.Collections.IEnumerator ReturnToMainMenuRoutine()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnHostClientConnected;
        }
        if (networkDiscovery != null)
        {
            networkDiscovery.StopDiscovery();
        }
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsServer || nm.IsClient || nm.IsListening))
        {
            nm.Shutdown();
        }
        float timeout = 3f;
        float elapsed = 0f;
        while (nm != null && (nm.IsServer || nm.IsClient || nm.IsListening) && elapsed < timeout)
        {
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        SceneManager.LoadScene("MainMenuScene");
    }
    
    private void ClearDiscoveredGames()
    {
        foreach(var button in discoveredGames.Values)
        {
            Destroy(button.gameObject);
        }
        discoveredGames.Clear();
    }

    // Display name feature removed

    private void TrySendLocalCharacter()
    {
        int selected = PlayerPrefs.GetInt("SelectedCharacter", 0); // 0=Fire,1=Water,2=Air,3=Earth
        var localClient = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient : null;
        if (localClient != null && localClient.PlayerObject != null)
        {
            var rps = localClient.PlayerObject.GetComponent<RPSPlayer>();
            if (rps != null)
            {
                rps.SetCharacterServerRpc(Mathf.Clamp(selected, 0, 3));
                return;
            }
        }

        // Fallback: wait briefly for PlayerObject
        StartCoroutine(WaitForPlayerObjectAndSendCharacter(selected));
    }

    private IEnumerator WaitForPlayerObjectAndSendCharacter(int selected)
    {
        float timeout = 5f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            var localClient = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient : null;
            if (localClient != null && localClient.PlayerObject != null)
            {
                var rps = localClient.PlayerObject.GetComponent<RPSPlayer>();
                if (rps != null)
                {
                    rps.SetCharacterServerRpc(Mathf.Clamp(selected, 0, 3));
                    yield break;
                }
            }
            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
        Debug.LogWarning("DIAGNOSTIC: Timed out waiting for PlayerObject to send SelectedCharacter.");
    }

    private void TrySetHostCharacterForLocalPlayer()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        int selected = PlayerPrefs.GetInt("SelectedCharacter", 0);
        var localClient = NetworkManager.Singleton.LocalClient;
        if (localClient != null && localClient.PlayerObject != null)
        {
            var rps = localClient.PlayerObject.GetComponent<RPSPlayer>();
            if (rps != null)
            {
                rps.SelectedCharacter.Value = Mathf.Clamp(selected, 0, 3);
                return;
            }
        }
        StartCoroutine(WaitAndSetHostCharacter(selected));
    }

    private IEnumerator WaitAndSetHostCharacter(int selected)
    {
        float timeout = 5f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            var localClient = NetworkManager.Singleton.LocalClient;
            if (localClient != null && localClient.PlayerObject != null)
            {
                var rps = localClient.PlayerObject.GetComponent<RPSPlayer>();
                if (rps != null)
                {
                    rps.SelectedCharacter.Value = Mathf.Clamp(selected, 0, 3);
                    yield break;
                }
            }
            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
        Debug.LogWarning("DIAGNOSTIC: Host timed out setting SelectedCharacter.");
    }

    // Display name feature removed

    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "Not found";
    }
}