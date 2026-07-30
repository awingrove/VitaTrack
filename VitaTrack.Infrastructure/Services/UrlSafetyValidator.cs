using System.Net;

namespace VitaTrack.Infrastructure.Services;

internal static class UrlSafetyValidator
{
    public static bool IsUrlSafe(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Only allow HTTPS
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // Resolve the hostname to IP addresses and check each one
        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            foreach (var addr in addresses)
            {
                if (IPAddress.IsLoopback(addr) || IsPrivateOrReserved(addr))
                    return false;
            }
        }
        catch (Exception)
        {
            // DNS resolution failed — block by default
            return false;
        }

        return true;
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (
            bytes[0] == 10 ||                                          // 10.0.0.0/8
            bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||    // 172.16.0.0/12
            bytes[0] == 192 && bytes[1] == 168 ||                      // 192.168.0.0/16
            bytes[0] == 169 && bytes[1] == 254 ||                      // 169.254.0.0/16 (link-local)
            bytes[0] == 127                                            // 127.0.0.0/8
        );
    }
}
