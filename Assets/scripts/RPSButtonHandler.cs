using UnityEngine;

/// <summary>
/// Simple button handler that can be added to prefabs to handle RPS button clicks
/// </summary>
public class RPSButtonHandler : MonoBehaviour
{
    // These methods can be called from UI buttons in prefabs
    public void OnRockButtonClicked()
    {
        HandleChoice(RPSChoice.Rock);
    }

    public void OnPaperButtonClicked()
    {
        HandleChoice(RPSChoice.Paper);
    }

    public void OnScissorsButtonClicked()
    {
        HandleChoice(RPSChoice.Scissors);
    }

    private void HandleChoice(RPSChoice choice)
    {
        
        // Prefer authoritative local PlayerObject from NetworkManager
        RPSPlayer localPlayer = null;
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClient != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<RPSPlayer>();
            if (localPlayer != null) { }
        }

        // Fallback: scan all RPSPlayers to find the owned one
        if (localPlayer == null)
        {
            RPSPlayer[] allPlayers = FindObjectsByType<RPSPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (RPSPlayer player in allPlayers)
            {
                if (player.IsOwner)
                {
                    localPlayer = player;
                    break;
                }
            }
        }
        
        if (localPlayer != null)
        {
            
            try
            {
                // Call the appropriate choice method
                switch (choice)
                {
                    case RPSChoice.Rock:
                        localPlayer.ChooseRock();
                        break;
                    case RPSChoice.Paper:
                        localPlayer.ChoosePaper();
                        break;
                    case RPSChoice.Scissors:
                        localPlayer.ChooseScissors();
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"DIAGNOSTIC: Exception when calling choice method: {e.Message}");
                Debug.LogError($"DIAGNOSTIC: Stack trace: {e.StackTrace}");
            }
        }
        else
        {
            Debug.LogError("DIAGNOSTIC: Could not find local owned player to make choice!");
        }
    }
}
