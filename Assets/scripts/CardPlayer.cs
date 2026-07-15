using System;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

public class CardPlayer : NetworkBehaviour
{
    // Owner-only hand: list of card IDs (placeholder ints for now)
    public NetworkList<int> Hand;

    // Discard gating when hand > 8 after draw
    public NetworkVariable<bool> NeedsDiscard = new NetworkVariable<bool>(false);
    public NetworkVariable<int> DiscardCount = new NetworkVariable<int>(0);
    // Public hand count for opponent UI (readable by everyone, server writes)
    public NetworkVariable<int> PublicHandCount = new NetworkVariable<int>(0);

    private void Awake()
    {
        Hand = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Nothing else yet; UI will subscribe to Hand.OnListChanged
    }

    [ServerRpc]
    public void RequestDiscardServerRpc(int cardId, ServerRpcParams rpcParams = default)
    {
        // Server validates ownership and discard state
        ulong sender = rpcParams.Receive.SenderClientId;
        if (OwnerClientId != sender) return;
        if (!NeedsDiscard.Value || DiscardCount.Value <= 0) return;

        // Remove first matching card from hand
        for (int i = 0; i < Hand.Count; i++)
        {
            if (Hand[i] == cardId)
            {
                Hand.RemoveAt(i);
                // Add discarded card to graveyard
                var gm = UnityEngine.Object.FindFirstObjectByType<CardGameManager>();
                if (gm != null) gm.RegisterDiscard(sender, cardId);
                DiscardCount.Value = Mathf.Max(0, DiscardCount.Value - 1);
                if (DiscardCount.Value == 0)
                {
                    NeedsDiscard.Value = false;
                }
                PublicHandCount.Value = Hand.Count;
                return;
            }
        }
    }
}
