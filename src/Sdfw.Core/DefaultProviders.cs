using Sdfw.Core.Models;

namespace Sdfw.Core;

public static class DefaultProviders
{
    public static List<DnsProvider> CreateBuiltInProviders()
    {
        return
        [
            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0001-000000000001"),
                Name = "Cloudflare",
                Type = DnsProviderType.Standard,
                PrimaryIpv4 = "1.1.1.1",
                SecondaryIpv4 = "1.0.0.1",
                PrimaryIpv6 = "2606:4700:4700::1111",
                SecondaryIpv6 = "2606:4700:4700::1001",
                IsBuiltIn = true,
                Description = "Cloudflare DNS - fast and privacy-focused"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0001-000000000002"),
                Name = "Cloudflare (DoH)",
                Type = DnsProviderType.DoH,
                DohUrl = "https://cloudflare-dns.com/dns-query",
                BootstrapIps = ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111"],
                IsBuiltIn = true,
                Description = "Cloudflare DNS-over-HTTPS - encrypted DNS"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0002-000000000001"),
                Name = "Google",
                Type = DnsProviderType.Standard,
                PrimaryIpv4 = "8.8.8.8",
                SecondaryIpv4 = "8.8.4.4",
                PrimaryIpv6 = "2001:4860:4860::8888",
                SecondaryIpv6 = "2001:4860:4860::8844",
                IsBuiltIn = true,
                Description = "Google Public DNS"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0002-000000000002"),
                Name = "Google (DoH)",
                Type = DnsProviderType.DoH,
                DohUrl = "https://dns.google/dns-query",
                BootstrapIps = ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888"],
                IsBuiltIn = true,
                Description = "Google DNS-over-HTTPS"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0003-000000000001"),
                Name = "Quad9",
                Type = DnsProviderType.Standard,
                PrimaryIpv4 = "9.9.9.9",
                SecondaryIpv4 = "149.112.112.112",
                PrimaryIpv6 = "2620:fe::fe",
                SecondaryIpv6 = "2620:fe::9",
                IsBuiltIn = true,
                Description = "Quad9 DNS - security focused with threat blocking"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0003-000000000002"),
                Name = "Quad9 (DoH)",
                Type = DnsProviderType.DoH,
                DohUrl = "https://dns.quad9.net/dns-query",
                BootstrapIps = ["9.9.9.9", "149.112.112.112", "2620:fe::fe"],
                IsBuiltIn = true,
                Description = "Quad9 DNS-over-HTTPS with threat blocking"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0004-000000000001"),
                Name = "OpenDNS",
                Type = DnsProviderType.Standard,
                PrimaryIpv4 = "208.67.222.222",
                SecondaryIpv4 = "208.67.220.220",
                PrimaryIpv6 = "2620:119:35::35",
                SecondaryIpv6 = "2620:119:53::53",
                IsBuiltIn = true,
                Description = "OpenDNS by Cisco"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0004-000000000002"),
                Name = "OpenDNS (DoH)",
                Type = DnsProviderType.DoH,
                DohUrl = "https://doh.opendns.com/dns-query",
                BootstrapIps = ["208.67.222.222", "208.67.220.220"],
                IsBuiltIn = true,
                Description = "OpenDNS DNS-over-HTTPS by Cisco"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0005-000000000001"),
                Name = "AdGuard",
                Type = DnsProviderType.Standard,
                PrimaryIpv4 = "94.140.14.14",
                SecondaryIpv4 = "94.140.15.15",
                PrimaryIpv6 = "2a10:50c0::ad1:ff",
                SecondaryIpv6 = "2a10:50c0::ad2:ff",
                IsBuiltIn = true,
                Description = "AdGuard DNS - blocks ads and trackers"
            },

            new DnsProvider
            {
                Id = new Guid("00000000-0000-0000-0005-000000000002"),
                Name = "AdGuard (DoH)",
                Type = DnsProviderType.DoH,
                DohUrl = "https://dns.adguard-dns.com/dns-query",
                BootstrapIps = ["94.140.14.14", "94.140.15.15"],
                IsBuiltIn = true,
                Description = "AdGuard DNS-over-HTTPS - blocks ads and trackers"
            }
        ];
    }
}
