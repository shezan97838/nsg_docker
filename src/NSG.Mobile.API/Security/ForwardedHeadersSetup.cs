using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace NSG.Mobile.API.Security;

/// <summary>
/// Settings of the <c>ForwardedHeaders</c> section. Forwarded headers (<c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c>) are
/// client-controlled unless they come from a reverse proxy you run, so they are honoured ONLY when <see cref="Enabled"/> is true
/// and the request comes from one of the listed proxies or networks.
/// </summary>
public class TrustedProxySettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Off by default: with no reverse proxy configured, no forwarded header is trusted.</summary>
    public bool Enabled { get; set; }

    /// <summary>Addresses of the reverse proxies, for example <c>127.0.0.1</c> for nginx on the same host.</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>Networks in CIDR form, for example <c>10.20.0.0/24</c>.</summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();
}

/// <summary>Builds <see cref="ForwardedHeadersOptions"/> from configuration; fails startup on anything unsafe or malformed.</summary>
public static class ForwardedHeadersSetup
{
    /// <summary>Null when forwarded headers are not enabled (nothing is trusted).</summary>
    public static ForwardedHeadersOptions? Build(TrustedProxySettings settings)
    {
        if (!settings.Enabled) return null;

        if (settings.KnownProxies.Length == 0 && settings.KnownNetworks.Length == 0)
            throw new InvalidOperationException(
                $"{TrustedProxySettings.SectionName}:Enabled is true but no {TrustedProxySettings.SectionName}:KnownProxies or KnownNetworks are listed. " +
                "List the reverse proxy addresses explicitly; forwarded headers are never trusted from arbitrary clients.");

        ForwardedHeadersOptions options = new()
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,   // exactly one proxy hop: the one in front of the application
        };

        // The defaults trust loopback; replace them with exactly what was configured.
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (string proxy in settings.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out IPAddress? address))
                throw new InvalidOperationException($"{TrustedProxySettings.SectionName}:KnownProxies contains an invalid IP address.");
            options.KnownProxies.Add(address);
        }

        foreach (string network in settings.KnownNetworks)
        {
            string[] parts = network.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? prefix) || !int.TryParse(parts[1], out int length)
                || length < 0 || length > (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
                throw new InvalidOperationException($"{TrustedProxySettings.SectionName}:KnownNetworks must use CIDR notation such as 10.20.0.0/24.");
            options.KnownNetworks.Add(new IPNetwork(prefix, length));
        }

        return options;
    }
}
