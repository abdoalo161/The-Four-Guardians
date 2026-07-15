using UnityEngine;
using Unity.Netcode;

public class RPSTossUIProxy : MonoBehaviour
{
    private RPSPlayer cachedLocal;

    private RPSPlayer GetLocal()
    {
        if (cachedLocal != null && cachedLocal.IsSpawned) return cachedLocal;
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[RPSTossUIProxy] NetworkManager is null; cannot resolve local player.");
            return null;
        }

        // Try direct local player object
        var localObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localObj != null)
        {
            cachedLocal = localObj.GetComponent<RPSPlayer>();
            if (cachedLocal != null) return cachedLocal;
        }

        // Fallback: scan
        var players = Object.FindObjectsByType<RPSPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (var p in players)
        {
            if (p != null && p.IsSpawned && p.OwnerClientId == localId)
            {
                cachedLocal = p;
                return cachedLocal;
            }
        }
        Debug.LogWarning($"[RPSTossUIProxy] Could not find local RPSPlayer for LocalClientId={localId}. Players in scene: {players?.Length ?? 0}");
        return null;
    }

    public void OnRockClick()
    {
        Debug.Log("[RPSTossUIProxy] OnRockClick invoked");
        var p = GetLocal();
        if (p != null) p.ChooseRock();
        else Debug.LogWarning("[RPSTossUIProxy] Local RPSPlayer not found; Rock click ignored.");
    }

    public void OnPaperClick()
    {
        Debug.Log("[RPSTossUIProxy] OnPaperClick invoked");
        var p = GetLocal();
        if (p != null) p.ChoosePaper();
        else Debug.LogWarning("[RPSTossUIProxy] Local RPSPlayer not found; Paper click ignored.");
    }

    public void OnScissorsClick()
    {
        Debug.Log("[RPSTossUIProxy] OnScissorsClick invoked");
        var p = GetLocal();
        if (p != null) p.ChooseScissors();
        else Debug.LogWarning("[RPSTossUIProxy] Local RPSPlayer not found; Scissors click ignored.");
    }
}
