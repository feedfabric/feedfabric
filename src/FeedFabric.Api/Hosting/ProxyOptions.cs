using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FeedFabric.Api.Hosting;

// Running behind a reverse proxy or load balancer that terminates TLS. With ForwardedHeaders:Enabled the API takes the
// client's address and scheme from X-Forwarded-For and X-Forwarded-Proto, so HTTPS redirection, HSTS, per-IP rate
// limits and logs see the real request. Only the proxies listed are believed; with none listed, any sender is, which
// is only safe when nothing but the proxy can reach the API.
public class ProxyOptions
{
    public const string Section = "ForwardedHeaders";

    public bool Enabled { get; set; }

    // Proxy addresses, e.g. "10.0.0.5"
    public List<string> KnownProxies { get; set; } = [];

    // Proxy networks in CIDR form, e.g. "10.0.0.0/8"
    public List<string> KnownNetworks { get; set; } = [];

    // How many proxies deep to read X-Forwarded-For
    public int ForwardLimit { get; set; } = 1;

    public void ApplyTo(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = ForwardLimit;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        if (KnownProxies.Count == 0 && KnownNetworks.Count == 0)
        {
            // Trust whoever sends the headers (see above)
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("0.0.0.0/0"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("::/0"));
            return;
        }
        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
        foreach (var network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }
}
