using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MobileSpeaker.Core;

public sealed record LocalAddress(IPAddress Address, string InterfaceName, bool HasGateway, bool IsVirtual, bool IsWireless);

public static class NetworkInfo
{
    private static readonly string[] VirtualHints =
    {
        "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "wsl", "tap-", "tunnel", "vpn", "loopback", "bluetooth"
    };

    /// <summary>
    /// IPv4 locales (sin loopback ni 169.254.x). Primero las reales con puerta de enlace
    /// (Wi-Fi antes que cable) y al final las de adaptadores virtuales.
    /// </summary>
    public static List<LocalAddress> GetLocalIPv4Addresses()
    {
        var result = new List<LocalAddress>();

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return result;
        }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            var props = nic.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            string text = (nic.Name + " " + nic.Description).ToLowerInvariant();
            bool isVirtual = VirtualHints.Any(text.Contains);

            foreach (var unicast in props.UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                    continue;

                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                    continue;

                if (result.Any(r => r.Address.Equals(ip)))
                    continue;

                result.Add(new LocalAddress(ip, nic.Name, hasGateway, isVirtual,
                    nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
            }
        }

        return result
            .OrderBy(a => a.IsVirtual)
            .ThenByDescending(a => a.HasGateway)
            .ThenByDescending(a => a.IsWireless)
            .ToList();
    }
}
