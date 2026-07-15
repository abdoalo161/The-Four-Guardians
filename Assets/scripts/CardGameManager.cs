using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CardGameManager : NetworkBehaviour
{
    // Active player turn owner
    public NetworkVariable<ulong> ActivePlayerClientId = new NetworkVariable<ulong>(0);

    // Balance scale [-10..10]
    public NetworkVariable<int> BalanceScale = new NetworkVariable<int>(0);

    // Two sides * 6 lanes represented as board cards
    private BoardCard[,] board = new BoardCard[2, 6];

    // Map clientId -> side index (0 or 1) from server perspective
    private readonly Dictionary<ulong, int> sideOfClient = new Dictionary<ulong, int>();
    // Map clientId -> chosen element
    private readonly Dictionary<ulong, Element> elementOfClient = new Dictionary<ulong, Element>();

    // Per-client decks (placeholder IDs 0..159 shuffled; real definitions to come)
    private readonly Dictionary<ulong, Queue<int>> decks = new Dictionary<ulong, Queue<int>>();
    // Per-client graveyard (destroyed or tributed)
    private readonly Dictionary<ulong, List<int>> graveyardByClient = new Dictionary<ulong, List<int>>();
    // Server snapshot of current hands to restore after reconnect
    private readonly Dictionary<ulong, List<int>> handByClient = new Dictionary<ulong, List<int>>();
    private readonly Dictionary<ulong, int[]> seenByClient = new Dictionary<ulong, int[]>();
    private readonly Dictionary<ulong, int> drawIndexByClient = new Dictionary<ulong, int>();
    private const int DrawPeekWindow = 2;

    [SerializeField] private ElementDeckLibrary deckLibrary; // assign in inspector
    public ElementDeckLibrary DeckLibrary => deckLibrary;
    private readonly Dictionary<int, CardDefinition> defById = new Dictionary<int, CardDefinition>();
    private bool gameOver = false;
    private int turnsSinceStart = 0; // 0 until first end turn completes; combat runs only after this becomes >=1

    private struct BoardCard
    {
        public bool occupied;
        public int cardId;
        public int atk;
        public int hp;
        public ulong owner;
    }

    public void RegisterDiscard(ulong ownerClientId, int cardId)
    {
        if (!IsServer) return;
        AddToGraveyard(ownerClientId, cardId);
        SyncAllClients();
    }

    private void AddToGraveyard(ulong owner, int cardId)
    {
        if (cardId < 0) return;
        if (!graveyardByClient.TryGetValue(owner, out var list))
        {
            list = new List<int>();
            graveyardByClient[owner] = list;
        }
        list.Add(cardId);
    }

    private bool IsStrongAgainst(Element a, Element b)
    {
        return (a == Element.Water && b == Element.Fire)
            || (a == Element.Fire && b == Element.Air)
            || (a == Element.Air && b == Element.Earth)
            || (a == Element.Earth && b == Element.Water);
    }

    private int GetElementalAtkDelta(ulong attackerClientId)
    {
        try
        {
            if (!elementOfClient.TryGetValue(attackerClientId, out var atkElem)) return 0;
            ulong defenderId = GetOtherPlayerId(attackerClientId);
            if (!elementOfClient.TryGetValue(defenderId, out var defElem)) return 0;
            // Rules: Fire vs Air = +2; Fire vs Water = -2; others = 0 (extend as needed)
            if (IsStrongAgainst(atkElem, defElem)) return +1;
            return 0;
        }
        catch { return 0; }
    }

    private void Awake()
    {
        for (int s = 0; s < 2; s++)
            for (int i = 0; i < 6; i++)
                board[s, i] = new BoardCard { occupied = false };
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            // Ensure we don't double-subscribe across scene reloads
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnServerClientConnected;
                NetworkManager.Singleton.OnClientConnectedCallback += OnServerClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnServerClientDisconnected;
            }
            StartCoroutine(ServerInitCo());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnServerClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnected;
            Debug.Log("[DIAGNOSTIC][Despawn] CardGameManager unsubscribed from network callbacks.");
        }
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnServerClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnected;
            Debug.Log("[DIAGNOSTIC][Destroy] CardGameManager unsubscribed from network callbacks.");
        }
        base.OnDestroy();
    }

    private IEnumerator ServerInitCo()
    {
        // Wait a short moment for SelectedCharacter to propagate from Lobby (host and client RPCs)
        float timeout = 2f; // seconds
        float elapsed = 0f;
        bool ready = false;
        while (elapsed < timeout)
        {
            ready = true;
            var clients = NetworkManager.Singleton.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var po = clients[i].PlayerObject;
                var rps = po != null ? po.GetComponent<RPSPlayer>() : null;
                if (rps == null || rps.SelectedCharacter.Value < 0)
                {
                    ready = false;
                    break;
                }
            }
            if (ready) break;
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        InitializeMatchServer();
        SyncAllClients();
    }

    private void InitializeMatchServer()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count < 1) return;

        // Assign sides deterministically by order (0,1)
        for (int i = 0; i < clients.Count && i < 2; i++)
        {
            sideOfClient[clients[i].ClientId] = i; // 0 bottom, 1 top (per-client mapping done on send)
            Debug.Log($"[DIAGNOSTIC][Init] Map ClientId={clients[i].ClientId} -> side {i}");
        }

        // Try to build decks from element selections via ElementDeckLibrary
        bool usedLibrary = (deckLibrary != null);
        System.Random rnd = new System.Random();
        foreach (var c in clients)
        {
            var rps = c.PlayerObject != null ? c.PlayerObject.GetComponent<RPSPlayer>() : null;
            Element element = Element.Fire;
            if (rps != null && rps.SelectedCharacter.Value >= 0)
            {
                int idx = Mathf.Clamp(rps.SelectedCharacter.Value, 0, 3);
                element = (Element)idx;
                Debug.Log($"[CardGameManager] Init: Client {c.ClientId} SelectedCharacter={idx} -> element={element}");
            }
            else
            {
                Debug.LogWarning($"[CardGameManager] Init: Client {c.ClientId} has no SelectedCharacter set; defaulting to Fire");
            }
            if (!graveyardByClient.ContainsKey(c.ClientId)) graveyardByClient[c.ClientId] = new List<int>();
            elementOfClient[c.ClientId] = element;
            var defs = (deckLibrary != null) ? deckLibrary.GetDeck(element) : null;
            List<int> idList = new List<int>();
            if (defs != null && defs.Length > 0)
            {
                foreach (var d in defs)
                {
                    if (d == null) continue;
                    defById[d.Id] = d;
                    idList.Add(d.Id);
                }
                idList = BuildBalancedDeckList(idList, rnd);
                decks[c.ClientId] = new Queue<int>(idList);
                Debug.Log($"[DIAGNOSTIC][Init] Built deck for ClientId={c.ClientId} (count={idList.Count})");
            }
            else
            {
                usedLibrary = false; // fallback
            }
        }

        if (!usedLibrary)
        {
            var allIds = new List<int>();
            for (int i = 0; i < 160; i++) allIds.Add(i);
            // Build a separate balanced order for each player to keep decks isolated
            foreach (var c in clients)
            {
                var perPlayerBalanced = BuildBalancedDeckList(allIds, rnd);
                decks[c.ClientId] = new Queue<int>(perPlayerBalanced);
                Debug.Log($"[DIAGNOSTIC][Init] Built fallback deck for ClientId={c.ClientId} (count={perPlayerBalanced.Count})");
            }
        }

        // Choose starting player: prefer value from RPSTossScene if provided
        ulong starter = clients[0].ClientId;
        if (CardGameStartConfig.TryGetStarter(out var provided))
        {
            // ensure provided exists in current clients
            foreach (var c in clients)
            {
                if (c.ClientId == provided)
                {
                    starter = provided;
                    break;
                }
            }
            CardGameStartConfig.Clear();
        }
        ActivePlayerClientId.Value = starter;
        Debug.Log($"[DIAGNOSTIC][Init] Starter clientId set to {starter}");

        // Deal 5 to each player
        foreach (var c in clients)
        {
            var p = c.PlayerObject.GetComponent<CardPlayer>();
            if (p == null) continue;
            for (int k = 0; k < 5; k++)
            {
                if (decks[c.ClientId].Count > 0)
                {
                    int cid = decks[c.ClientId].Dequeue();
                    p.Hand.Add(cid);
                    EnsureSeen(c.ClientId);
                    int b = GetTributeBucket(cid);
                    seenByClient[c.ClientId][b]++;
                }
            }
            p.PublicHandCount.Value = p.Hand.Count;
            var snap = new List<int>(p.Hand.Count);
            for (int hi = 0; hi < p.Hand.Count; hi++) snap.Add(p.Hand[hi]);
            handByClient[c.ClientId] = snap;
        }
    }

    private void OnServerClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        Debug.Log($"[DIAGNOSTIC][Join] OnServerClientConnected clientId={clientId}. ConnectedClients={NetworkManager.Singleton.ConnectedClients.Count}");
        StartCoroutine(FinalizeClientJoin(clientId));
    }

    private System.Collections.IEnumerator FinalizeClientJoin(ulong clientId)
    {
        var hostId = NetworkManager.Singleton.LocalClientId;
        if (!sideOfClient.ContainsKey(hostId)) sideOfClient[hostId] = 0;
        Debug.Log($"[DIAGNOSTIC][Join] FinalizeClientJoin clientId={clientId}, hostId={hostId}");

        // Wait for player's NetworkObject to be spawned to avoid null refs/rejects
        float waited = 0f;
        while (waited < 5f)
        {
            if (NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId) &&
                NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject != null)
            {
                break;
            }
            waited += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        Debug.Log($"[DIAGNOSTIC][Join] PlayerObject ready={NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId) && NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject != null} after {waited:0.0}s");

        if (clientId == hostId)
        {
            SyncAllClients();
            yield break;
        }

        // Try to locate an orphaned client id (disconnected earlier) to transfer state from
        ulong orphan = 0;
        foreach (var kv in sideOfClient)
        {
            if (kv.Key != hostId && kv.Key != clientId && !NetworkManager.Singleton.ConnectedClients.ContainsKey(kv.Key))
            { orphan = kv.Key; break; }
        }
        if (orphan == 0)
        {
            foreach (var kv in decks)
            {
                if (kv.Key != hostId && kv.Key != clientId && !NetworkManager.Singleton.ConnectedClients.ContainsKey(kv.Key))
                { orphan = kv.Key; break; }
            }
        }
        if (orphan == 0)
        {
            foreach (var kv in graveyardByClient)
            {
                if (kv.Key != hostId && kv.Key != clientId && !NetworkManager.Singleton.ConnectedClients.ContainsKey(kv.Key))
                { orphan = kv.Key; break; }
            }
        }

        if (orphan != 0)
        {
            ReassignStateToNewClient(orphan, clientId);
            Debug.Log($"[DIAGNOSTIC][Join] Reassigned orphan {orphan} -> {clientId}");
        }
        else
        {
            // Last chance: pick any non-host mapping as orphan even if it's still in ConnectedClients (stale)
            foreach (var kv in sideOfClient)
            {
                if (kv.Key != hostId && kv.Key != clientId)
                {
                    orphan = kv.Key; break;
                }
            }
            if (orphan != 0)
            {
                ReassignStateToNewClient(orphan, clientId);
                Debug.Log($"[DIAGNOSTIC][Join] Forced reassignment {orphan} -> {clientId}");
            }
            else
            {
            // Default mapping for newly joined non-host
            sideOfClient[clientId] = 1;
            if (!decks.ContainsKey(clientId)) decks[clientId] = new Queue<int>();
            if (!graveyardByClient.ContainsKey(clientId)) graveyardByClient[clientId] = new List<int>();
            Debug.Log($"[DIAGNOSTIC][Join] Default mapping clientId={clientId} -> side 1; decks? {decks.ContainsKey(clientId)} gy? {graveyardByClient.ContainsKey(clientId)}");
            }
        }

        // If we have a snapshotted hand, rehydrate
        if (handByClient.TryGetValue(clientId, out var hand))
        {
            var po = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            var p = po != null ? po.GetComponent<CardPlayer>() : null;
            if (p != null)
            {
                p.Hand.Clear();
                for (int i = 0; i < hand.Count; i++) p.Hand.Add(hand[i]);
                p.PublicHandCount.Value = p.Hand.Count;
                Debug.Log($"[DIAGNOSTIC][Join] Rehydrated hand for clientId={clientId} count={hand.Count}");
            }
        }

        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(ActivePlayerClientId.Value))
        {
            int activeSide = 0;
            if (sideOfClient.TryGetValue(ActivePlayerClientId.Value, out var s)) activeSide = s;
            ulong replacement = hostId;
            foreach (var kv in sideOfClient)
            {
                if (kv.Value == activeSide && NetworkManager.Singleton.ConnectedClients.ContainsKey(kv.Key))
                {
                    replacement = kv.Key; break;
                }
            }
            ActivePlayerClientId.Value = replacement;
            Debug.Log($"[DIAGNOSTIC][Join] Active player replaced with {replacement}");
        }

        SyncAllClients();
    }

    private void OnServerClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        // Preserve side mapping to allow clean reassignment on reconnect
        // Keep decks/graveyard/board for potential reconnection
    }

    private void ReassignStateToNewClient(ulong oldId, ulong newId)
    {
        if (oldId == newId) return;
        if (sideOfClient.TryGetValue(oldId, out var oldSide))
        {
            sideOfClient[newId] = oldSide;
            sideOfClient.Remove(oldId);
        }
        if (elementOfClient.TryGetValue(oldId, out var elem))
        {
            elementOfClient[newId] = elem;
            elementOfClient.Remove(oldId);
        }
        if (decks.TryGetValue(oldId, out var dq))
        {
            decks[newId] = dq;
            decks.Remove(oldId);
        }
        if (graveyardByClient.TryGetValue(oldId, out var gy))
        {
            graveyardByClient[newId] = gy;
            graveyardByClient.Remove(oldId);
        }
        // Transfer draw variance state
        if (seenByClient.TryGetValue(oldId, out var seen))
        {
            seenByClient[newId] = seen;
            seenByClient.Remove(oldId);
        }
        if (drawIndexByClient.TryGetValue(oldId, out var di))
        {
            drawIndexByClient[newId] = di;
            drawIndexByClient.Remove(oldId);
        }
        // Transfer hand snapshot and rehydrate the new player's CardPlayer
        if (handByClient.TryGetValue(oldId, out var hand))
        {
            handByClient[newId] = hand;
            handByClient.Remove(oldId);
            if (NetworkManager.Singleton.ConnectedClients.ContainsKey(newId))
            {
                var po = NetworkManager.Singleton.ConnectedClients[newId].PlayerObject;
                var p = po != null ? po.GetComponent<CardPlayer>() : null;
                if (p != null)
                {
                    p.Hand.Clear();
                    for (int i = 0; i < hand.Count; i++) p.Hand.Add(hand[i]);
                    p.PublicHandCount.Value = p.Hand.Count;
                }
            }
        }
        for (int s = 0; s < 2; s++)
        {
            for (int i = 0; i < 6; i++)
            {
                if (board[s, i].occupied && board[s, i].owner == oldId)
                {
                    board[s, i] = new BoardCard
                    {
                        occupied = true,
                        cardId = board[s, i].cardId,
                        atk = board[s, i].atk,
                        hp = board[s, i].hp,
                        owner = newId
                    };
                }
            }
        }
        if (ActivePlayerClientId.Value == oldId) ActivePlayerClientId.Value = newId;
    }

    // ===== Summoning =====
    [ServerRpc(RequireOwnership = false)]
    public void RequestSummonServerRpc(int cardId, int laneIndex, int[] tributeLaneIndices, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (gameOver) return;
        if (sender != ActivePlayerClientId.Value) return; // only active player's turn
        if (!sideOfClient.TryGetValue(sender, out int side)) return;

        // Must not be in forced discard
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(sender, out var client) || client == null || client.PlayerObject == null)
        {
            Debug.LogWarning($"[CardGameManager] RequestSummonServerRpc: sender {sender} has no PlayerObject yet; ignoring summon.");
            return;
        }
        var p = client.PlayerObject.GetComponent<CardPlayer>();
        if (p == null) return;
        if (p.NeedsDiscard.Value) return;

        // Validate card is in hand
        bool inHand = false;
        for (int i = 0; i < p.Hand.Count; i++) if (p.Hand[i] == cardId) { inHand = true; break; }
        if (!inHand) return;

        // Stats & tribute from definitions (fallback to rules)
        CardStats stats;
        if (defById.TryGetValue(cardId, out var def))
        {
            stats = new CardStats { Atk = def.Attack, Hp = def.Health, TributeCost = def.TributeCost };
        }
        else
        {
            stats = CardRules.GetStats(cardId);
        }
        int atk = stats.Atk;
        int hp = stats.Hp;
        int tributeCost = stats.TributeCost;

        // Apply elemental ATK modifier based on attacker vs defender elements
        int delta = GetElementalAtkDelta(sender);
        atk = Mathf.Max(0, atk + delta);
        Debug.Log($"[CardGameManager] Summon: sender={sender} side={side} lane={laneIndex} cardId={cardId} baseATK={stats.Atk} elemDelta={delta} finalATK={atk} HP={hp}");

        // If tributeCost > 0, require exactly that many tribute lanes from own side
        if (tributeCost > 0)
        {
            if (tributeLaneIndices == null || tributeLaneIndices.Length != tributeCost) return;
            // Validate tributes are own occupied lanes (distinct)
            var seen = new HashSet<int>();
            foreach (var li in tributeLaneIndices)
            {
                if (li < 0 || li >= 6) return;
                if (!board[side, li].occupied || board[side, li].owner != sender) return;
                if (!seen.Add(li)) return;
            }
            // Remove tributes
            foreach (var li in tributeLaneIndices)
            {
                var bc = board[side, li];
                if (bc.occupied)
                {
                    AddToGraveyard(bc.owner, bc.cardId);
                }
                board[side, li] = new BoardCard { occupied = false };
            }
        }

        // Allow summoning into target lane if empty (or if it was tributed and thus freed)
        if (laneIndex < 0 || laneIndex >= 6) return;
        if (board[side, laneIndex].occupied) return;

        // Place card and remove from hand
        board[side, laneIndex] = new BoardCard { occupied = true, cardId = cardId, atk = atk, hp = hp, owner = sender };
        // Remove one instance from hand
        for (int i = 0; i < p.Hand.Count; i++) { if (p.Hand[i] == cardId) { p.Hand.RemoveAt(i); break; } }
        p.PublicHandCount.Value = p.Hand.Count;

        SyncAllClients();
    }

    // ===== End Turn / Combat =====
    [ServerRpc(RequireOwnership = false)]
    public void RequestEndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (gameOver) return;
        if (sender != ActivePlayerClientId.Value) return;

        if (!sideOfClient.TryGetValue(sender, out int atkSide)) return;
        int defSide = atkSide == 0 ? 1 : 0;

        Debug.Log($"[CardGameManager] EndTurn: sender={sender} atkSide={atkSide} defSide={defSide} turnsSinceStart={turnsSinceStart}");

        // Skip combat on the very first end turn (so combat begins at end of turn 2)
        if (turnsSinceStart >= 1)
        {
            // Resolve per-lane attacks; no counterattack
            for (int lane = 0; lane < 6; lane++)
            {
                if (!board[atkSide, lane].occupied) continue;
                int damage = board[atkSide, lane].atk;
                if (board[defSide, lane].occupied)
                {
                    int hp = board[defSide, lane].hp;
                    Debug.Log($"[Combat] Lane {lane}: ATT {damage} vs DEF HP {hp}");
                    if (damage >= hp)
                    {
                        int overflow = damage - hp;
                        var destroyed = board[defSide, lane];
                        board[defSide, lane] = new BoardCard { occupied = false };
                        AddToGraveyard(destroyed.owner, destroyed.cardId);
                        Debug.Log($"[Combat] Lane {lane}: Defender destroyed. Overflow={overflow}");
                        ApplyScaleDelta(atkSide == 0 ? +overflow : -overflow);
                    }
                    else
                    {
                        board[defSide, lane].hp = hp - damage;
                        Debug.Log($"[Combat] Lane {lane}: Defender remains with HP={board[defSide, lane].hp}");
                    }
                }
                else
                {
                    Debug.Log($"[Combat] Lane {lane}: Direct hit for {damage}");
                    ApplyScaleDelta(atkSide == 0 ? +damage : -damage);
                }
            }
        }

        // Rotate turn
        ActivePlayerClientId.Value = GetOtherPlayerId(ActivePlayerClientId.Value);

        // Start-of-turn draw for new active player
        var nextClient = ActivePlayerClientId.Value;
        TryDrawOne(nextClient);

        Debug.Log($"[CardGameManager] Turn rotated. Next active={ActivePlayerClientId.Value}. Syncing clients...");
        SyncAllClients();
        // Increase ended turns counter
        turnsSinceStart++;
        Debug.Log($"[CardGameManager] turnsSinceStart now {turnsSinceStart}");
    }

    private void ApplyScaleDelta(int delta)
    {
        if (delta == 0) return;
        int v = Mathf.Clamp(BalanceScale.Value + delta, -20, 20);
        BalanceScale.Value = v;
        // Win check at +/-10
        if (!gameOver)
        {
            if (v >= 20) EndGame(0);
            else if (v <= -20) EndGame(1);
        }
    }

    private void TryDrawOne(ulong clientId)
    {
        if (!decks.ContainsKey(clientId)) return;
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;
        var client = NetworkManager.Singleton.ConnectedClients[clientId];
        if (client == null || client.PlayerObject == null) return;
        var p = client.PlayerObject.GetComponent<CardPlayer>();
        if (p == null) return;

        if (decks[clientId].Count == 0)
        {
            if (sideOfClient.TryGetValue(clientId, out int losingSide))
            {
                int winnerSide = losingSide == 0 ? 1 : 0;
                EndGame(winnerSide);
            }
            return;
        }

        EnsureSeen(clientId);
        int currentDraw = drawIndexByClient.ContainsKey(clientId) ? (drawIndexByClient[clientId] + 1) : 1;
        int peek = Math.Min(DrawPeekWindow, decks[clientId].Count);
        var head = new List<int>(peek);
        for (int i = 0; i < peek; i++) head.Add(decks[clientId].Dequeue());
        int chosen = 0;
        var pref = EvaluatePreference(clientId, currentDraw);
        if (pref != 0 && !MatchesPreference(head[0], pref))
        {
            for (int i = 1; i < head.Count; i++)
            {
                if (MatchesPreference(head[i], pref)) { chosen = i; break; }
            }
        }
        int drawnCard = head[chosen];
        var remainder = new List<int>(head.Count - 1);
        for (int i = 0; i < head.Count; i++) if (i != chosen) remainder.Add(head[i]);
        var rest = decks[clientId].ToArray();
        var rebuilt = new List<int>(remainder.Count + rest.Length);
        rebuilt.AddRange(remainder);
        for (int i = 0; i < rest.Length; i++) rebuilt.Add(rest[i]);
        decks[clientId] = new Queue<int>(rebuilt);

        p.Hand.Add(drawnCard);
        int bb = GetTributeBucket(drawnCard);
        seenByClient[clientId][bb]++;
        drawIndexByClient[clientId] = currentDraw;

        if (!gameOver && decks[clientId].Count == 0)
        {
            if (sideOfClient.TryGetValue(clientId, out int losingSide2))
            {
                int winnerSide2 = losingSide2 == 0 ? 1 : 0;
                EndGame(winnerSide2);
                return;
            }
        }
        p.PublicHandCount.Value = p.Hand.Count;
        if (p.Hand.Count > 8)
        {
            int toDiscard = p.Hand.Count - 8;
            p.DiscardCount.Value = toDiscard;
            p.NeedsDiscard.Value = true;
        }
    }

    private void EnsureSeen(ulong clientId)
    {
        if (!seenByClient.ContainsKey(clientId)) seenByClient[clientId] = new int[4];
        if (!drawIndexByClient.ContainsKey(clientId)) drawIndexByClient[clientId] = 0;
    }

    private int GetTributeBucket(int cardId)
    {
        int t = 0;
        if (defById.TryGetValue(cardId, out var def)) t = def.TributeCost; else t = CardRules.GetStats(cardId).TributeCost;
        if (t <= 0) return 0; if (t == 1) return 1; if (t == 2) return 2; return 3;
    }

    private int EvaluatePreference(ulong clientId, int drawIndex)
    {
        var arr = seenByClient[clientId];
        int low = arr[0] + arr[1];
        int midhi = arr[2] + arr[3];
        int hi = arr[3];
        if (drawIndex >= 1 && drawIndex <= 3 && low < 3) return 1;
        if (drawIndex >= 4 && drawIndex <= 6 && midhi < 2) return 2;
        if (drawIndex >= 7 && drawIndex <= 10 && hi < 1) return 3;
        return 0;
    }

    private bool MatchesPreference(int cardId, int pref)
    {
        int b = GetTributeBucket(cardId);
        if (pref == 1) return b == 0 || b == 1;
        if (pref == 2) return b >= 2;
        if (pref == 3) return b == 3;
        return true;
    }

    private List<int> BuildBalancedDeckList(List<int> ids, System.Random rnd)
    {
        var z0 = new List<int>();
        var z1 = new List<int>();
        var z2 = new List<int>();
        var z3 = new List<int>();
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            int t = 0;
            if (defById.TryGetValue(id, out var def)) t = def.TributeCost;
            else t = CardRules.GetStats(id).TributeCost;
            if (t <= 0) z0.Add(id);
            else if (t == 1) z1.Add(id);
            else if (t == 2) z2.Add(id);
            else z3.Add(id);
        }
        void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
        Shuffle(z0); Shuffle(z1); Shuffle(z2); Shuffle(z3);
        var result = new List<int>(ids.Count);
        int take;
        take = Math.Min(2, z0.Count); for (int i = 0; i < take; i++) { result.Add(z0[0]); z0.RemoveAt(0); }
        take = Math.Min(2, z1.Count); for (int i = 0; i < take; i++) { result.Add(z1[0]); z1.RemoveAt(0); }
        if (z2.Count > 0) { result.Add(z2[0]); z2.RemoveAt(0); }
        while (result.Count < 5)
        {
            if (z0.Count > 0) { result.Add(z0[0]); z0.RemoveAt(0); }
            else if (z1.Count > 0) { result.Add(z1[0]); z1.RemoveAt(0); }
            else if (z2.Count > 0) { result.Add(z2[0]); z2.RemoveAt(0); }
            else if (z3.Count > 0) { result.Add(z3[0]); z3.RemoveAt(0); }
            else break;
        }
        int[] pattern = new int[] { 0, 1, 2, 1, 0, 3 };
        int pi = 0;
        while (z0.Count + z1.Count + z2.Count + z3.Count > 0)
        {
            bool added = false;
            for (int k = 0; k < pattern.Length; k++)
            {
                int b = pattern[(pi + k) % pattern.Length];
                if (b == 0 && z0.Count > 0) { result.Add(z0[0]); z0.RemoveAt(0); pi = (pi + k + 1) % pattern.Length; added = true; break; }
                if (b == 1 && z1.Count > 0) { result.Add(z1[0]); z1.RemoveAt(0); pi = (pi + k + 1) % pattern.Length; added = true; break; }
                if (b == 2 && z2.Count > 0) { result.Add(z2[0]); z2.RemoveAt(0); pi = (pi + k + 1) % pattern.Length; added = true; break; }
                if (b == 3 && z3.Count > 0) { result.Add(z3[0]); z3.RemoveAt(0); pi = (pi + k + 1) % pattern.Length; added = true; break; }
            }
            if (!added)
            {
                if (z0.Count > 0) { result.Add(z0[0]); z0.RemoveAt(0); }
                else if (z1.Count > 0) { result.Add(z1[0]); z1.RemoveAt(0); }
                else if (z2.Count > 0) { result.Add(z2[0]); z2.RemoveAt(0); }
                else if (z3.Count > 0) { result.Add(z3[0]); z3.RemoveAt(0); }
            }
        }
        return result;
    }

    private ulong GetOtherPlayerId(ulong current)
    {
        foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (c.ClientId != current) return c.ClientId;
        }
        return current;
    }

    // ===== Client Sync =====
    private void SyncAllClients()
    {
        try
        {
        // Snapshot latest hands for reconnect safety
        foreach (var cc in NetworkManager.Singleton.ConnectedClientsList)
        {
            var po = cc.PlayerObject;
            if (po != null)
            {
                var p = po.GetComponent<CardPlayer>();
                if (p != null)
                {
                    var snap2 = new List<int>(p.Hand.Count);
                    for (int hi = 0; hi < p.Hand.Count; hi++) snap2.Add(p.Hand[hi]);
                    handByClient[cc.ClientId] = snap2;
                }
            }
        }
        foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
        {
            ulong target = c.ClientId;
            // Build perspective arrays: own side is index 0 for receiver
            var hostId = NetworkManager.Singleton.LocalClientId;
            int recvSide = sideOfClient.ContainsKey(target) ? sideOfClient[target] : (target == hostId ? 0 : 1);
            int oppSide = recvSide == 0 ? 1 : 0;
            Debug.Log($"[DIAGNOSTIC][Sync] Target={target} recvSide={recvSide} oppSide={oppSide} decksHasTarget={decks.ContainsKey(target)}");

            var ownIds = new int[6]; var ownAtk = new int[6]; var ownHp = new int[6];
            var oppIds = new int[6]; var oppAtk = new int[6]; var oppHp = new int[6];
            for (int i = 0; i < 6; i++)
            {
                var a = board[recvSide, i];
                ownIds[i] = a.occupied ? a.cardId : -1; ownAtk[i] = a.occupied ? a.atk : 0; ownHp[i] = a.occupied ? a.hp : 0;
                var b = board[oppSide, i];
                oppIds[i] = b.occupied ? b.cardId : -1; oppAtk[i] = b.occupied ? b.atk : 0; oppHp[i] = b.occupied ? b.hp : 0;
            }

            int scaleForYou = (recvSide == 0) ? BalanceScale.Value : -BalanceScale.Value;
            var send = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { target } }
            };
            // Build graveyard arrays from owner clientIds of each side
            ulong recvClient = GetClientIdForSideIndex(recvSide);
            if (recvClient == 0) recvClient = target;
            ulong oppClient = GetClientIdForSideIndex(oppSide);
            if (oppClient == 0) oppClient = GetOtherPlayerId(target);
            var gyOwnList = graveyardByClient.ContainsKey(recvClient) ? graveyardByClient[recvClient] : new List<int>();
            var gyOppList = graveyardByClient.ContainsKey(oppClient) ? graveyardByClient[oppClient] : new List<int>();
            int ownDeckCount = decks.ContainsKey(target) ? decks[target].Count : 0;
            int oppHandCount = 0;
            if (handByClient.TryGetValue(oppClient, out var oppHandSnap))
            {
                oppHandCount = oppHandSnap != null ? oppHandSnap.Count : 0;
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(oppClient))
            {
                var poOpp = NetworkManager.Singleton.ConnectedClients[oppClient].PlayerObject;
                var oppP = poOpp != null ? poOpp.GetComponent<CardPlayer>() : null;
                if (oppP != null) oppHandCount = oppP.PublicHandCount.Value;
            }
            BoardStateClientRpc(ownIds, ownAtk, ownHp, oppIds, oppAtk, oppHp, scaleForYou, ActivePlayerClientId.Value, gyOwnList.ToArray(), gyOppList.ToArray(), ownDeckCount, oppHandCount, send);
        }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DIAGNOSTIC][Sync] Exception while syncing: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    [ClientRpc]
    private void BoardStateClientRpc(int[] ownIds, int[] ownAtk, int[] ownHp, int[] oppIds, int[] oppAtk, int[] oppHp, int scale, ulong activePlayerId, int[] ownGraveyard, int[] oppGraveyard, int ownDeckCount, int oppHandCount, ClientRpcParams rpcParams = default)
    {
        var ui = CardUIManager.Instance != null ? CardUIManager.Instance : UnityEngine.Object.FindFirstObjectByType<CardUIManager>();
        if (ui == null) return;
        ui.SetOpponentHandCount(oppHandCount);
        ui.UpdateBoardUI(ownIds, ownAtk, ownHp, oppIds, oppAtk, oppHp, scale, activePlayerId, ownGraveyard, oppGraveyard);
        var deckUi = UnityEngine.Object.FindFirstObjectByType<DeckCountUI>();
        if (deckUi != null)
        {
            deckUi.SetDeckCount(ownDeckCount);
        }
    }

    private ulong GetClientIdForSideIndex(int sideIndex)
    {
        foreach (var kv in sideOfClient)
        {
            if (kv.Value == sideIndex) return kv.Key;
        }
        return 0;
    }

    private void EndGame(int winnerSide)
    {
        gameOver = true;
        // Notify each client of win/lose based on their side mapping
        foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
        {
            int recvSide = sideOfClient.ContainsKey(c.ClientId) ? sideOfClient[c.ClientId] : 0;
            bool youWin = (recvSide == winnerSide);
            var send = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { c.ClientId } }
            };
            ShowResultClientRpc(youWin, send);
        }
    }

    [ClientRpc]
    private void ShowResultClientRpc(bool youWin, ClientRpcParams rpcParams = default)
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<CardUIManager>();
        if (ui == null) return;
        ui.ShowResult(youWin);
    }
}
