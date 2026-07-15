using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Netcode.Transports.Ruffles;

public class DisconnectPanelManager : MonoBehaviour
{
    [Header("Host UI")]
    [SerializeField] private GameObject hostWaitingPanel;
    [SerializeField] private Button hostBackToMenuButton;

    [Header("Client UI")]
    [SerializeField] private GameObject clientHostDisconnectedPanel;
    [SerializeField] private Button clientBackToMenuButton;

    [Header("Client Self-Disconnect UI")] 
    [SerializeField] private GameObject clientSelfDisconnectedPanel; // shown when local client loses connection
    [SerializeField] private Button clientSelfBackToMenuButton;
    [SerializeField] private Button clientSelfReconnectButton;

    private bool _isReconnecting = false;

    private void Awake()
    {
        if (hostWaitingPanel != null) hostWaitingPanel.SetActive(false);
        if (clientHostDisconnectedPanel != null) clientHostDisconnectedPanel.SetActive(false);
        if (clientSelfDisconnectedPanel != null) clientSelfDisconnectedPanel.SetActive(false);
        if (hostBackToMenuButton != null)
        {
            hostBackToMenuButton.onClick.RemoveAllListeners();
            hostBackToMenuButton.onClick.AddListener(ReturnToMainMenu);
        }
        if (clientBackToMenuButton != null)
        {
            clientBackToMenuButton.onClick.RemoveAllListeners();
            clientBackToMenuButton.onClick.AddListener(ReturnToMainMenu);
        }
        if (clientSelfBackToMenuButton != null)
        {
            clientSelfBackToMenuButton.onClick.RemoveAllListeners();
            clientSelfBackToMenuButton.onClick.AddListener(ReturnToMainMenu);
        }
        if (clientSelfReconnectButton != null)
        {
            clientSelfReconnectButton.onClick.RemoveAllListeners();
            clientSelfReconnectButton.onClick.AddListener(OnReconnectClicked);
        }
    }

    private void OnReconnectClicked()
    {
        if (_isReconnecting) return;
        StartCoroutine(ReconnectCo());
    }

    private System.Collections.IEnumerator ReconnectCo()
    {
        _isReconnecting = true;
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            _isReconnecting = false;
            yield break;
        }

        if (nm.IsClient || nm.IsServer)
        {
            nm.Shutdown();
            float waited = 0f;
            while (waited < 2f && (nm.IsClient || nm.IsServer || nm.IsListening))
            {
                waited += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
        }

        var transport = nm.GetComponent<RufflesTransport>();
        if (transport == null)
        {
            _isReconnecting = false;
            yield break;
        }

        string ip = PlayerPrefs.GetString("Client_LastIPAddress", string.Empty);
        if (string.IsNullOrWhiteSpace(ip))
        {
            _isReconnecting = false;
            ReturnToMainMenu();
            yield break;
        }

        transport.ConnectAddress = ip.Trim();

        bool started = nm.StartClient();
        if (!started)
        {
            _isReconnecting = false;
            yield break;
        }

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (nm.IsConnectedClient)
            {
                if (clientHostDisconnectedPanel != null) clientHostDisconnectedPanel.SetActive(false);
                if (clientSelfDisconnectedPanel != null) clientSelfDisconnectedPanel.SetActive(false);
                _isReconnecting = false;
                yield break;
            }
            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }

        _isReconnecting = false;
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        if (_monitorCo == null) _monitorCo = StartCoroutine(MonitorConnectivity());
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
        if (_monitorCo != null)
        {
            StopCoroutine(_monitorCo);
            _monitorCo = null;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.IsServer)
        {
            if (clientId == nm.LocalClientId)
            {
                // Host's own client disconnected: immediately return to main menu
                ReturnToMainMenu();
                return;
            }
            if (clientId != nm.LocalClientId)
            {
                ActivatePanel(hostWaitingPanel);
            }
        }
        else
        {
            if (clientId == nm.LocalClientId)
            {
                StartCoroutine(ShowClientDisconnectPanelClassified());
            }
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.IsServer)
        {
            if (nm.ConnectedClients.Count >= 2)
            {
                if (hostWaitingPanel != null) hostWaitingPanel.SetActive(false);
            }
        }
        else
        {
            if (clientHostDisconnectedPanel != null) clientHostDisconnectedPanel.SetActive(false);
            if (clientSelfDisconnectedPanel != null) clientSelfDisconnectedPanel.SetActive(false);
            _isReconnecting = false;
        }
    }

    private void ReturnToMainMenu()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsServer)
            {
                // Host: move all clients to MainMenu via networked scene load, then shutdown shortly after
                try
                {
                    nm.SceneManager.LoadScene("MainMenuScene", LoadSceneMode.Single);
                }
                catch { }
                StartCoroutine(ShutdownAfterSceneCo());
                return;
            }
            // Client: return locally
            nm.Shutdown();
        }
        SceneManager.LoadScene("MainMenuScene");
    }

    private void ActivatePanel(GameObject panel)
    {
        if (panel == null) return;
        if (hostWaitingPanel != null && hostWaitingPanel != panel) hostWaitingPanel.SetActive(false);
        if (clientHostDisconnectedPanel != null && clientHostDisconnectedPanel != panel) clientHostDisconnectedPanel.SetActive(false);
        if (clientSelfDisconnectedPanel != null && clientSelfDisconnectedPanel != panel) clientSelfDisconnectedPanel.SetActive(false);
        panel.SetActive(true);
    }

    private System.Collections.IEnumerator ShowClientDisconnectPanelClassified()
    {
        float waited = 0f;
        while (waited < 0.6f)
        {
            waited += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }
        var reach = Application.internetReachability;
        if (reach == NetworkReachability.NotReachable)
        {
            ActivatePanel(clientSelfDisconnectedPanel);
        }
        else
        {
            ActivatePanel(clientHostDisconnectedPanel);
        }
    }
    

    private System.Collections.IEnumerator ShutdownAfterSceneCo()
    {
        // brief delay to let clients receive the scene-load message
        yield return new WaitForSeconds(0.25f);
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.Shutdown();
        }
    }

    // ===== Connectivity monitor fallback =====
    private Coroutine _monitorCo;
    private bool _lastIsConnectedClient = false;
    private int _lastServerClientCount = 0;
    private System.Collections.IEnumerator MonitorConnectivity()
    {
        while (true)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (nm.IsServer)
            {
                int curCount = nm.ConnectedClients != null ? nm.ConnectedClients.Count : 0;
                if (_lastServerClientCount >= 2 && curCount < 2)
                {
                    ActivatePanel(hostWaitingPanel);
                }
                _lastServerClientCount = curCount;
            }
            else
            {
                bool curConnected = nm.IsConnectedClient;
                if (_lastIsConnectedClient && !curConnected)
                {
                    StartCoroutine(ShowClientDisconnectPanelClassified());
                }
                _lastIsConnectedClient = curConnected;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }
}
