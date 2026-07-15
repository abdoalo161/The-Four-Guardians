
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Linq;

public class RPSNetworkDiscovery : MonoBehaviour
{
    public static event Action<IPEndPoint, string> OnServerFound;

    private const int DiscoveryPort = 9999;
    private const int GamePort = 7777; // Ruffles Transport default port
    private const string DiscoveryBroadcastMessage = "RPS_GAME_HOST_RUFFLES";
    private UdpClient udpClient;
    private bool isBroadcasting;
    private bool isListening;
    private List<(IPAddress ip, IPAddress mask)> localIPs = new List<(IPAddress, IPAddress)>();
    private IPAddress primaryIPv4;
    
    // New: game name to broadcast with discovery
    public string ServerGameName = "Rock Paper Scissors Game";

    void Start()
    {
        // Load game name from settings carrier if present
        var carrier = FindFirstObjectByType<HostGameSettingsCarrier>(FindObjectsInactive.Include);
        if (carrier != null && !string.IsNullOrWhiteSpace(carrier.GameName))
        {
            ServerGameName = carrier.GameName;
        }
        RefreshLocalIPs();
    }

    void OnDestroy()
    {
        StopDiscovery();
    }

    private void RefreshLocalIPs()
    {
        localIPs.Clear();
        primaryIPv4 = null;
        try
        {
            // Get all network interfaces
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            int bestScore = int.MinValue;
            foreach (NetworkInterface ni in interfaces)
            {
                if (!IsCandidateInterface(ni)) continue;

                // Get IP properties
                IPInterfaceProperties ipProps = ni.GetIPProperties();
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    // Only IPv4 addresses
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var mask = addr.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                        localIPs.Add((addr.Address, mask));
                        int score = ScoreCandidate(ni, addr.Address, ipProps);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            primaryIPv4 = addr.Address;
                        }
                    }
                }
            }
            // Fallback to first collected private IPv4 if primary not chosen yet
            if (primaryIPv4 == null)
            {
                primaryIPv4 = localIPs.Select(t => t.ip).FirstOrDefault(ip => IsPrivateIPv4(ip));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting local IPs: {e.Message}");
            // Fallback to basic method
            // Keep list empty; primary will be null; GetLocalIP will return Unknown
        }
    }

    private static bool IsCandidateInterface(NetworkInterface ni)
    {
        if (ni == null) return false;
        if (ni.OperationalStatus != OperationalStatus.Up) return false;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
            ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
            ni.NetworkInterfaceType == NetworkInterfaceType.Unknown ||
            ni.NetworkInterfaceType == NetworkInterfaceType.Ppp)
            return false;
        string desc = (ni.Description ?? string.Empty) + " " + (ni.Name ?? string.Empty);
        string lower = desc.ToLowerInvariant();
        // Skip common virtual adapters (Docker, WSL, Hyper-V, VMware, Tailscale, etc.)
        string[] virtualHints = { "virtual", "vmware", "hyper-v", "veth", "vEthernet", "docker", "wsl", "loopback", "tailscale", "zerotier", "hamachi" };
        foreach (var hint in virtualHints)
        {
            if (lower.Contains(hint.ToLowerInvariant())) return false;
        }
        return true;
    }

    private static bool IsPrivateIPv4(IPAddress ip)
    {
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        // 10.0.0.0/8
        if (b[0] == 10) return true;
        // 172.16.0.0/12
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        // 192.168.0.0/16
        if (b[0] == 192 && b[1] == 168) return true;
        return false;
    }

    private static int ScoreCandidate(NetworkInterface ni, IPAddress ip, IPInterfaceProperties ipProps)
    {
        int score = 0;
        if (!IsPrivateIPv4(ip)) score -= 1000; // strongly prefer private LAN IPs
        // Range preference: 192.168.* best, then 10.*, then 172.16-31.*
        byte[] b = ip.GetAddressBytes();
        if (b[0] == 192 && b[1] == 168) score += 50;
        else if (b[0] == 10) score += 40;
        else if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) score += 30;

        // Interface type preference
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 20;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 15;

        // Having a default gateway suggests routable interface
        bool hasGateway = ipProps.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
        if (hasGateway) score += 10;

        // Penalize likely NAT/bridge/virtual names
        string desc = (ni.Description ?? string.Empty) + " " + (ni.Name ?? string.Empty);
        string lower = desc.ToLowerInvariant();
        string[] natHints = { "nat", "bridge", "docker", "hyper-v", "vethernet", "vmware", "wsl" };
        foreach (var hint in natHints)
        {
            if (lower.Contains(hint)) { score -= 50; break; }
        }
        return score;
    }

    public void StartBroadcasting()
    {
        if (isBroadcasting) return;

        try
        {
            RefreshLocalIPs();
            udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            // Bind to any available port for sending
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            isBroadcasting = true;
            InvokeRepeating(nameof(BroadcastPresence), 0, 2f); // Broadcast every 2 seconds
            Debug.Log($"DIAGNOSTIC: Discovery StartBroadcasting primary={primaryIPv4} adapters={localIPs.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not start broadcasting: {e.Message}");
            StopDiscovery();
        }
    }

    public void StartListening()
    {
        if (isListening) return;

        try
        {
            udpClient = new UdpClient(DiscoveryPort);
            udpClient.BeginReceive(OnReceive, null);
            isListening = true;
            Debug.Log($"DIAGNOSTIC: Discovery StartListening on port {DiscoveryPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not start listening: {e.Message}");
            StopDiscovery();
        }
    }

    private void BroadcastPresence()
    {
        if (!isBroadcasting || udpClient == null) return;

        string safeName = string.IsNullOrEmpty(ServerGameName) ? "Rock Paper Scissors Game" : ServerGameName;
        string ipString = primaryIPv4?.ToString() ?? "0.0.0.0";
        string hostInfo = $"{DiscoveryBroadcastMessage}|{GamePort}|{safeName}|{ipString}";
        byte[] data = Encoding.UTF8.GetBytes(hostInfo);
        
        try
        {
            // Broadcast to general broadcast address
            udpClient.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            
            // Also broadcast to subnet-specific addresses for better LAN/hotspot coverage
            foreach (var tuple in localIPs)
            {
                try
                {
                    // Calculate broadcast address using mask when available
                    byte[] ipBytes = tuple.ip.GetAddressBytes();
                    byte[] maskBytes = (tuple.mask ?? IPAddress.Parse("255.255.255.0")).GetAddressBytes();
                    byte[] bcBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bcBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
                    }
                    IPAddress broadcastAddr = new IPAddress(bcBytes);
                    udpClient.Send(data, data.Length, new IPEndPoint(broadcastAddr, DiscoveryPort));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to broadcast to subnet {tuple.ip}: {ex.Message}");
                }
            }
            
        }
        catch (Exception e)
        {
            Debug.LogError($"Broadcast failed: {e.Message}");
        }
    }

    private void OnReceive(IAsyncResult ar)
    {
        if (!isListening || udpClient == null) return;

        IPEndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] receivedBytes;
        try
        {
            receivedBytes = udpClient.EndReceive(ar, ref senderEndPoint);
        }
        catch (ObjectDisposedException)
        {
            return; // UdpClient was closed
        }
        catch (Exception e)
        {
            Debug.LogError($"Error receiving broadcast: {e.Message}");
            if (isListening && udpClient != null)
            {
                udpClient.BeginReceive(OnReceive, null);
            }
            return;
        }

        string message = Encoding.UTF8.GetString(receivedBytes);
        Debug.Log($"DIAGNOSTIC: Discovery received from {senderEndPoint.Address}: '{message}'");

        // Parse the message to extract game info
        string[] parts = message.Split('|');
        if (parts.Length >= 2 && parts[0].Equals(DiscoveryBroadcastMessage))
        {
            if (int.TryParse(parts[1], out int gamePort))
            {
                string serverName = parts.Length >= 3 ? parts[2] : "Rock Paper Scissors Game";
                IPAddress preferIp = senderEndPoint.Address;
                if (parts.Length >= 4)
                {
                    if (IPAddress.TryParse(parts[3], out var embeddedIp))
                    {
                        preferIp = embeddedIp;
                    }
                }
                
                // Dispatch to main thread for UI updates
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    OnServerFound?.Invoke(new IPEndPoint(preferIp, gamePort), serverName);
                });
            }
        }

        // Continue listening
        if (isListening && udpClient != null)
        {
            try
            {
                udpClient.BeginReceive(OnReceive, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error continuing to listen: {e.Message}");
            }
        }
    }

    public void StopDiscovery()
    {
        isBroadcasting = false;
        isListening = false;
        CancelInvoke(nameof(BroadcastPresence));
        
        if (udpClient != null)
        {
            try
            {
                udpClient.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error closing UDP client: {e.Message}");
            }
            udpClient = null;
        }
        Debug.Log("DIAGNOSTIC: Discovery stopped.");
    }

    // Public method to get local IP for display
    public string GetLocalIP()
    {
        RefreshLocalIPs();
        return primaryIPv4?.ToString() ?? "Unknown";
    }
}
