using UnityEngine;
using Unity.Netcode;

// Attach this to any GameObject in LobbyScene (or HostSetupScene if you prefer).
// Wire your 4 character buttons' OnClick to OnCharacterSelected(int):
// 0=Fire, 1=Water, 2=Air, 3=Earth
public class CharacterSelectionController : MonoBehaviour
{
    public void OnCharacterSelected(int index)
    {
        int clamped = Mathf.Clamp(index, 0, 3);
        PlayerPrefs.SetInt("SelectedCharacter", clamped);
        PlayerPrefs.Save();
        

        // Immediately propagate to the networked player if available
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
        {
            var rps = nm.LocalClient.PlayerObject.GetComponent<RPSPlayer>();
            if (rps != null)
            {
                if (nm.IsServer)
                {
                    // Host/server can set directly
                    rps.SelectedCharacter.Value = clamped;
                }
                else
                {
                    // Clients notify server via RPC
                    rps.SetCharacterServerRpc(clamped);
                }
            }
        }
    }
}
