using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HyruleOnline {
 static class NetworkHintTests {
  static int checks, failures;

  static void Check(bool ok, string message) {
   checks++;
   if (ok) return;
   failures++;
   Console.WriteLine("FAIL: " + message);
  }

  static void Kind(string address, AddressKind expected) {
   var actual = NetworkHint.Classify(address);
   Check(actual == expected, "Classify(" + (address ?? "null") + ") is " + actual + ", expected " + expected);
  }

  static void Vpn(string address, string expected) {
   string actual = NetworkHint.VpnName(address);
   Check(actual == expected, "VpnName(" + (address ?? "null") + ") is " + (actual ?? "null") + ", expected " + (expected ?? "null"));
  }

  static string Describe(List<NetworkHint.Found> list) {
   var parts = new List<string>();
   foreach (var item in list) parts.Add(item.Name + "=" + item.Address);
   return string.Join(",", parts.ToArray());
  }

  static void Adapter(string name, string description, string[] addresses, string expected) {
   string actual = Describe(NetworkHint.FromAdapter(name, description, addresses));
   Check(actual == expected, "FromAdapter(" + name + ", " + description + ") is [" + actual + "], expected [" + expected + "]");
  }

  static void Probe(string host, int port, NetworkHint.Reach expected) {
   var actual = NetworkHint.Probe(host, port, 3000);
   Check(actual == expected, "Probe(" + host + ", " + port + ") is " + actual + ", expected " + expected);
  }

  static int Main() {
   Kind("100.63.255.255", AddressKind.Public);
   Kind("100.64.0.0", AddressKind.Vpn);
   Kind("100.127.255.255", AddressKind.Vpn);
   Kind("100.128.0.0", AddressKind.Public);
   Kind("172.15.255.255", AddressKind.Public);
   Kind("172.16.0.1", AddressKind.Home);
   Kind("172.31.255.255", AddressKind.Home);
   Kind("172.32.0.1", AddressKind.Public);
   Kind("192.168.1.1", AddressKind.Home);
   Kind("192.167.1.1", AddressKind.Public);
   Kind("10.0.0.1", AddressKind.Home);
   Kind("25.1.2.3", AddressKind.Vpn);
   Kind("26.1.2.3", AddressKind.Vpn);
   Kind("24.1.2.3", AddressKind.Public);
   Kind("27.1.2.3", AddressKind.Public);
   Kind("8.8.8.8", AddressKind.Public);
   Kind("127.0.0.1", AddressKind.None);
   Kind("169.254.1.1", AddressKind.None);
   Kind("", AddressKind.None);
   Kind(null, AddressKind.None);
   Kind("  ", AddressKind.None);
   Kind(" 192.168.1.1 ", AddressKind.Home);
   Kind("mypc.tailnet.ts.net", AddressKind.Name);
   Kind("friend.example.com", AddressKind.Name);
   Kind("300.1.1.1", AddressKind.None);
   Kind("1.2.3", AddressKind.None);
   Kind("!!", AddressKind.None);
   Kind("-bad-", AddressKind.None);

   Vpn("100.64.0.0", "Tailscale");
   Vpn("100.127.255.255", "Tailscale");
   Vpn("25.1.2.3", "Hamachi");
   Vpn("26.1.2.3", "Radmin VPN");
   Vpn("mypc.tailnet.ts.net", "Tailscale");
   Vpn("MYPC.TAILNET.TS.NET", "Tailscale");
   Vpn("friend.example.com", null);
   Vpn("192.168.1.1", null);
   Vpn("8.8.8.8", null);
   Vpn("", null);
   Vpn(null, null);

   Adapter("Tailscale", "Tailscale Tunnel", new[] { "100.101.102.103" }, "Tailscale=100.101.102.103");
   Adapter("Ethernet 3", "ZeroTier Virtual Port", new[] { "10.147.17.5" }, "ZeroTier=10.147.17.5");
   Adapter("Ethernet", "Intel(R) Ethernet Connection", new[] { "192.168.1.20" }, "");
   Adapter("Ethernet", "Intel(R) Ethernet Connection", new[] { "100.64.0.9" }, "Tailscale=100.64.0.9");
   Adapter("Ethernet", "Intel(R) Ethernet Connection", new[] { "25.4.5.6" }, "Hamachi=25.4.5.6");
   Adapter("Radmin VPN", "Famatech Radmin VPN Ethernet Adapter", new[] { "26.7.8.9", "169.254.1.1" }, "Radmin VPN=26.7.8.9");
   Adapter("ZeroTier One", "ZeroTier Virtual Port", new[] { "10.147.17.5", "10.147.17.6" }, "ZeroTier=10.147.17.5,ZeroTier=10.147.17.6");
   Adapter("OpenVPN Data Channel Offload", "Wintun", new[] { "10.8.0.2" }, "");
   Adapter("Local Area Connection 2", "TAP-Windows Adapter V9", new[] { "10.9.0.2" }, "");
   Adapter("Wi-Fi", "NordLynx Tunnel", new[] { "10.5.0.2" }, "");
   Adapter("WireGuard", "WireGuard Tunnel", new[] { "10.6.0.2" }, "");
   Adapter("ProtonVPN", "Proton VPN TUN", new[] { "10.2.0.2" }, "");
   Adapter("Mullvad", "Mullvad Tunnel", new[] { "10.64.0.2" }, "");
   Adapter("NordLynx", "NordLynx Tunnel", new[] { "100.64.0.9" }, "Tailscale=100.64.0.9");
   Adapter("Ethernet", "Intel", new[] { "192.168.1.20", "100.100.1.1" }, "Tailscale=100.100.1.1");
   Adapter("Tailscale", "Tailscale Tunnel", new[] { "127.0.0.1", "not-an-ip", "" }, "");
   Adapter("Tailscale", "Tailscale Tunnel", new string[0], "");
   Adapter("Tailscale", "Tailscale Tunnel", null, "");
   Adapter(null, null, new[] { "192.168.1.20" }, "");

   try { Check(NetworkHint.Vpns() != null, "Vpns() returns a list"); }
   catch (Exception error) { Check(false, "Vpns() threw " + error.Message); }

   var listener = new TcpListener(IPAddress.Loopback, 0);
   listener.Start();
   int port = ((IPEndPoint)listener.LocalEndpoint).Port;
   Probe("127.0.0.1", port, NetworkHint.Reach.Open);
   listener.Stop();
   Probe("127.0.0.1", port, NetworkHint.Reach.Refused);
   Probe("no-such-host.invalid", 5050, NetworkHint.Reach.UnknownHost);
   Probe("127.0.0.1", 0, NetworkHint.Reach.Failed);
   Probe("127.0.0.1", 70000, NetworkHint.Reach.Failed);
   Probe("", 5050, NetworkHint.Reach.Failed);

   var clock = Stopwatch.StartNew();
   var slow = NetworkHint.Probe("10.255.255.1", 5050, 500);
   clock.Stop();
   Check(slow != NetworkHint.Reach.Open, "a non-routable address is not open (" + slow + ")");
   Check(clock.ElapsedMilliseconds < 1500, "the probe honours its timeout (" + clock.ElapsedMilliseconds + " ms)");

   Console.WriteLine((failures == 0 ? "PASS" : "FAIL") + ": network hint, " + (checks - failures) + " of " + checks + " checks passed.");
   return failures == 0 ? 0 : 1;
  }
 }
}
