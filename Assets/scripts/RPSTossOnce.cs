using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;

public class RPSTossOnce : NetworkBehaviour
{
    [SerializeField] private string cardGameSceneName = "CardGameScene";
    private bool _started = false;
    private ulong p1Id;
    private ulong p2Id;
    private ulong winnerId;
    private bool starterChosen;
    private ulong starterChosenId;

    [Header("UI Panels")]
    [SerializeField] private GameObject drawPanel; // shown for ~2s on draw
    [SerializeField] private float drawPanelDuration = 2f;
    [SerializeField] private GameObject chooseStarterPanel; // shown only to winner
    [SerializeField] private Button chooseYouButton;
    [SerializeField] private Button chooseOpponentButton;
    [SerializeField] private GameObject losePanel; // shown to the loser while waiting

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartToss();
        }
        else
        {
        }
    }

    private void Start()
    {
        // Fallback in case this object lacks a NetworkObject or OnNetworkSpawn didn't fire
        if (!_started && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            StartToss();
        }

        // Local UI setup (both clients)
        try
        {
            if (drawPanel != null) drawPanel.SetActive(false);
            if (chooseStarterPanel != null) chooseStarterPanel.SetActive(false);
            if (losePanel != null) losePanel.SetActive(false);
            if (chooseYouButton != null)
            {
                chooseYouButton.onClick.RemoveListener(OnChooseYouClicked);
                chooseYouButton.onClick.AddListener(OnChooseYouClicked);
            }
            if (chooseOpponentButton != null)
            {
                chooseOpponentButton.onClick.RemoveListener(OnChooseOpponentClicked);
                chooseOpponentButton.onClick.AddListener(OnChooseOpponentClicked);
            }
        }
        catch { }
    }

    private void StartToss()
    {
        if (_started) return;
        _started = true;
        StartCoroutine(TossRoutine());
    }

    private IEnumerator TossRoutine()
    {
        // Wait for 2 network player objects (distinct owners)
        RPSPlayer p1 = null, p2 = null;
        while (true)
        {
            if (NetworkManager.Singleton != null)
            {
                foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
                {
                    var po = c.PlayerObject;
                    if (po == null) continue;
                    var rp = po.GetComponent<RPSPlayer>();
                    if (rp == null || !rp.IsSpawned) continue;
                    if (p1 == null) { p1 = rp; continue; }
                    if (p2 == null && rp.OwnerClientId != p1.OwnerClientId) { p2 = rp; }
                }
            }
            if (p1 != null && p2 != null)
            {
                p1Id = p1.OwnerClientId; p2Id = p2.OwnerClientId;
                break;
            }
            p1 = null; p2 = null; // re-evaluate next frame
            yield return null;
        }

        // Keep playing RPS until there is a winner (no draws)
        ulong starter = 0UL;
        while (true)
        {
            // Wait until both have chosen
            while (p1.CurrentChoice.Value == RPSChoice.None || p2.CurrentChoice.Value == RPSChoice.None)
            {
                yield return null;
            }

            var c1 = p1.CurrentChoice.Value;
            var c2 = p2.CurrentChoice.Value;
            int result = Compare(c1, c2);
            if (result > 0)
            {
                winnerId = p1.OwnerClientId;
                PromptWinnerChooseStarterClientRpc(winnerId);
                starterChosen = false;
                while (!starterChosen) yield return null;
                starter = starterChosenId;
                break;
            }
            if (result < 0)
            {
                winnerId = p2.OwnerClientId;
                PromptWinnerChooseStarterClientRpc(winnerId);
                starterChosen = false;
                while (!starterChosen) yield return null;
                starter = starterChosenId;
                break;
            }

            // Draw: reset choices and wait again
            ShowDrawClientRpc();
            p1.CurrentChoice.Value = RPSChoice.None;
            p2.CurrentChoice.Value = RPSChoice.None;
            yield return null;
        }

        CardGameStartConfig.SetStarter(starter);

        // Load CardGameScene for all clients via networked scene manager
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(cardGameSceneName, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(cardGameSceneName);
        }
    }

    private int Compare(RPSChoice a, RPSChoice b)
    {
        if (a == b) return 0;
        if (a == RPSChoice.Rock && b == RPSChoice.Scissors) return 1;
        if (a == RPSChoice.Scissors && b == RPSChoice.Paper) return 1;
        if (a == RPSChoice.Paper && b == RPSChoice.Rock) return 1;
        return -1;
    }

    [ClientRpc]
    private void ShowDrawClientRpc()
    {
        if (drawPanel == null) return;
        StartCoroutine(ShowDrawRoutine());
    }

    private IEnumerator ShowDrawRoutine()
    {
        drawPanel.SetActive(true);
        yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, drawPanelDuration));
        drawPanel.SetActive(false);
    }

    [ClientRpc]
    private void PromptWinnerChooseStarterClientRpc(ulong winnerClientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (chooseStarterPanel == null) return;
        bool isLocalWinner = (nm.LocalClientId == winnerClientId);
        chooseStarterPanel.SetActive(isLocalWinner);
        if (losePanel != null)
        {
            // Show lose panel on the non-winner
            losePanel.SetActive(!isLocalWinner);
        }
    }

    private void OnChooseYouClicked()
    {
        var nm = NetworkManager.Singleton; if (nm == null) return;
        SubmitStarterChoiceServerRpc(nm.LocalClientId);
        if (chooseStarterPanel != null) chooseStarterPanel.SetActive(false);
    }

    private void OnChooseOpponentClicked()
    {
        var nm = NetworkManager.Singleton; if (nm == null) return;
        ulong local = nm.LocalClientId;
        ulong opp = local;
        foreach (var c in nm.ConnectedClientsList)
        {
            if (c.ClientId != local) { opp = c.ClientId; break; }
        }
        if (opp != local)
        {
            SubmitStarterChoiceServerRpc(opp);
        }
        if (chooseStarterPanel != null) chooseStarterPanel.SetActive(false);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitStarterChoiceServerRpc(ulong chosenStarterId, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        // Only the winner may choose, and the choice must be one of the two players
        if (sender != winnerId) return;
        if (chosenStarterId != p1Id && chosenStarterId != p2Id) return;
        starterChosenId = chosenStarterId;
        starterChosen = true;
        HideChoosePanelClientRpc();
    }

    [ClientRpc]
    private void HideChoosePanelClientRpc()
    {
        if (chooseStarterPanel != null) chooseStarterPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }
}
