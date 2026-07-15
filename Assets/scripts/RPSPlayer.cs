using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode; // Required for NetworkBehaviour and NetworkVariable
using Unity.Collections; // For FixedString types used in NetworkVariable

public class RPSPlayer : NetworkBehaviour
{
    [Header("Player Settings")]
    [SerializeField] private int playerID = 1;

    // Properties
    public int PlayerID => playerID;

    // NetworkVariable for the player's current choice
    // OnValueChange will be called on all clients when the value changes
    public NetworkVariable<RPSChoice> CurrentChoice = new NetworkVariable<RPSChoice>(RPSChoice.None);


    // NetworkVariable for the player's selected character/element (0=Fire,1=Water,2=Air,3=Earth)
    public NetworkVariable<int> SelectedCharacter = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Event for UI updates (now triggered by NetworkVariable change)
    public static event UnityAction<RPSChoice> OnPlayerChoiceMade;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Only the owner of this NetworkObject should set their playerID
            // and subscribe to their own choice changes for local UI updates.
            // PlayerID will be assigned by the GameManager later.
            // For now, we'll use the default playerID.
        }

        // Subscribe to the NetworkVariable's value change event
        CurrentChoice.OnValueChanged += OnCurrentChoiceChanged;

        // Host display name removed; no initialization needed
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe to prevent memory leaks
        CurrentChoice.OnValueChanged -= OnCurrentChoiceChanged;
    }

    private void OnCurrentChoiceChanged(RPSChoice oldChoice, RPSChoice newChoice)
    {
        // This callback runs on all clients when CurrentChoice changes
        // Use this to update UI for all players (local and remote)
        OnPlayerChoiceMade?.Invoke(newChoice);
    }

    // This method is called by the local player to send their choice to the server
    [ServerRpc(RequireOwnership = false)]
    public void SetChoiceServerRpc(RPSChoice choice, ServerRpcParams rpcParams = default)
    {
        // Server-authoritative: apply choice to the RPSPlayer owned by the sender
        ulong senderId = rpcParams.Receive.SenderClientId;
        Debug.Log($"DIAGNOSTIC: SetChoiceServerRpc received from SenderClientId={senderId} with choice={choice}. This instance OwnerClientId={OwnerClientId}, PlayerID={PlayerID}");

        RPSPlayer target = null;
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out var netClient) &&
            netClient.PlayerObject != null)
        {
            target = netClient.PlayerObject.GetComponent<RPSPlayer>();
            Debug.Log($"DIAGNOSTIC: Resolved target RPSPlayer via ConnectedClients. Target OwnerClientId={target?.OwnerClientId}, PlayerID={target?.PlayerID}");
        }

        if (target == null)
        {
            // Fallback: scan all RPSPlayers
            var all = FindObjectsByType<RPSPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var p in all)
            {
                if (p.OwnerClientId == senderId && p.IsSpawned)
                {
                    target = p;
                    Debug.Log($"DIAGNOSTIC: Fallback resolved target RPSPlayer. Target OwnerClientId={target.OwnerClientId}, PlayerID={target.PlayerID}");
                    break;
                }
            }
        }

        if (target == null)
        {
            Debug.LogError($"DIAGNOSTIC: Could not resolve target RPSPlayer for SenderClientId={senderId}. Choice ignored.");
            return;
        }

        target.CurrentChoice.Value = choice;
        Debug.Log($"DIAGNOSTIC: Server set PlayerID={target.PlayerID} (OwnerClientId={target.OwnerClientId}) choice to {choice}. NV updated.");
    }


    // ServerRpc to set player's selected character/element (0..3)
    [ServerRpc(RequireOwnership = false)]
    public void SetCharacterServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        int idx = Mathf.Clamp(characterIndex, 0, 3);
        Debug.Log($"DIAGNOSTIC: SetCharacterServerRpc from SenderClientId={senderId} with characterIndex={characterIndex} -> clamped {idx}");

        RPSPlayer target = null;
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out var netClient) &&
            netClient.PlayerObject != null)
        {
            target = netClient.PlayerObject.GetComponent<RPSPlayer>();
        }

        if (target == null)
        {
            var all = FindObjectsByType<RPSPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var p in all)
            {
                if (p.OwnerClientId == senderId && p.IsSpawned)
                {
                    target = p;
                    break;
                }
            }
        }

        if (target == null)
        {
            Debug.LogError($"DIAGNOSTIC: Could not resolve RPSPlayer to set character for SenderClientId={senderId}");
            return;
        }

        target.SelectedCharacter.Value = idx;
        Debug.Log($"DIAGNOSTIC: Server set SelectedCharacter={idx} for PlayerID={target.PlayerID} (OwnerClientId={target.OwnerClientId})");
    }


    public void ResetChoice()
    {
        // This method should ideally be called by the server/host
        // For now, we'll allow it to be called locally, but it won't sync if not on server
        if (IsServer)
        {
            CurrentChoice.Value = RPSChoice.None;
        }
        else
        {
            Debug.LogWarning("ResetChoice called on client. This should ideally be server-driven.");
        }
    }

    public void SetPlayerID(int id)
    {
        // This should be set by the GameManager on the server
        playerID = id;
    }

    // These methods are called by UI buttons on the local client
    public void ChooseRock()
    {
        Debug.Log($"DIAGNOSTIC: ChooseRock called on Player {PlayerID} (OwnerClientID: {OwnerClientId}). IsOwner: {IsOwner}");
        if (IsOwner || (Unity.Netcode.NetworkManager.Singleton != null && OwnerClientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId))
        {
            Debug.Log($"DIAGNOSTIC: About to call SetChoiceServerRpc(Rock) for Player {PlayerID}");
            SetChoiceServerRpc(RPSChoice.Rock);
            Debug.Log($"DIAGNOSTIC: SetChoiceServerRpc(Rock) call completed for Player {PlayerID}");
        }
        else
        {
            Debug.LogWarning("DIAGNOSTIC: Attempted to choose Rock on a non-owned player object.");
        }
    }
    public void ChoosePaper()
    {
        Debug.Log($"DIAGNOSTIC: ChoosePaper called on Player {PlayerID} (OwnerClientID: {OwnerClientId}). IsOwner: {IsOwner}");
        if (IsOwner || (Unity.Netcode.NetworkManager.Singleton != null && OwnerClientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId))
        {
            Debug.Log($"DIAGNOSTIC: About to call SetChoiceServerRpc(Paper) for Player {PlayerID}");
            SetChoiceServerRpc(RPSChoice.Paper);
            Debug.Log($"DIAGNOSTIC: SetChoiceServerRpc(Paper) call completed for Player {PlayerID}");
        }
        else
        {
            Debug.LogWarning("DIAGNOSTIC: Attempted to choose Paper on a non-owned player object.");
        }
    }
    public void ChooseScissors()
    {
        Debug.Log($"DIAGNOSTIC: ChooseScissors called on Player {PlayerID} (OwnerClientID: {OwnerClientId}). IsOwner: {IsOwner}");
        if (IsOwner || (Unity.Netcode.NetworkManager.Singleton != null && OwnerClientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId))
        {
            Debug.Log($"DIAGNOSTIC: About to call SetChoiceServerRpc(Scissors) for Player {PlayerID}");
            SetChoiceServerRpc(RPSChoice.Scissors);
            Debug.Log($"DIAGNOSTIC: SetChoiceServerRpc(Scissors) call completed for Player {PlayerID}");
        }
        else
        {
            Debug.LogWarning("DIAGNOSTIC: Attempted to choose Scissors on a non-owned player object.");
        }
    }

    // --- Restart Request Path removed with legacy RPS flow ---
    [ServerRpc(RequireOwnership = false)]
    public void RequestRestartServerRpc(ServerRpcParams rpcParams = default)
    {
        Debug.Log("DIAGNOSTIC: RequestRestartServerRpc ignored (legacy RPS restart removed).");
    }

    public void RequestRestart()
    {
        Debug.Log("DIAGNOSTIC: RequestRestart ignored (legacy RPS restart removed).");
    }
}