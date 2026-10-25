using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 enum AddressKind { None, Home, Vpn, Public, Name }

 // Tells whether an address shared in an invite works only at home or from anywhere, finds installed VPNs and tests a TCP connection.
 static class NetworkHint {
  public enum Reach { Open, Refused, TimedOut, UnknownHost, Failed }

  public sealed class Found { public string Name; public string Address; }

  // Only overlay networks whose addresses friends can reach; privacy VPNs are left out on purpose.
  static readonly string[][] Products = {
   new[] { "Tailscale", "Tailscale" }, new[] { "ZeroTier", "ZeroTier" }, new[] { "Hamachi", "Hamachi" }, new[] { "Radmin", "Radmin VPN" }
  };

  static bool TryIpv4(string text, out int[] octets) {
   octets = null;
   var part = (text ?? "").Split('.');
   if (part.Length != 4) return false;
   var value = new int[4];
   for (int i = 0; i < 4; i++) {
    if (!Regex.IsMatch(part[i], "^[0-9]{1,3}$")) return false;
    value[i] = int.Parse(part[i]);
    if (value[i] > 255) return false;
   }
   octets = value;
   return true;
  }

  public static AddressKind Classify(string address) {
   address = (address ?? "").Trim();
   if (address.Length == 0) return AddressKind.None;
   int[] ip;
   if (TryIpv4(address, out ip)) {
    int a = ip[0], b = ip[1];
    if (a == 127 || a == 169 && b == 254) return AddressKind.None;
    if (a == 100 && b >= 64 && b <= 127 || a == 25 || a == 26) return AddressKind.Vpn;
    if (a == 10 || a == 192 && b == 168 || a == 172 && b >= 16 && b <= 31) return AddressKind.Home;
    return AddressKind.Public;
   }
   return Regex.IsMatch(address, "^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$") && Regex.IsMatch(address, "[A-Za-z]") ? AddressKind.Name : AddressKind.None;
  }

  public static string VpnName(string address) {
   address = (address ?? "").Trim();
   int[] ip;
   if (TryIpv4(address, out ip)) {
    if (Classify(address) != AddressKind.Vpn) return null;
    return ip[0] == 100 ? "Tailscale" : ip[0] == 25 ? "Hamachi" : "Radmin VPN";
   }
   return address.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) ? "Tailscale" : null;
  }

  // An adapter named after a VPN counts with all its addresses; any other adapter counts only for addresses in a VPN range.
  public static List<Found> FromAdapter(string adapterName, string description, IEnumerable<string> addresses) {
   var found = new List<Found>();
   string text = (adapterName ?? "") + " " + (description ?? ""), product = null;
   foreach (var item in Products)
    if (text.IndexOf(item[0], StringComparison.OrdinalIgnoreCase) >= 0) { product = item[1]; break; }
   foreach (string address in addresses ?? new string[0]) {
    int[] ip;
    if (!TryIpv4(address, out ip)) continue;
    var kind = Classify(address);
    if (kind == AddressKind.None) continue;
    string name = product ?? (kind == AddressKind.Vpn ? VpnName(address) : null);
    if (name != null) found.Add(new Found { Name = name, Address = address.Trim() });
   }
   return found;
  }

  public static List<Found> Vpns() {
   var found = new List<Found>();
   try {
    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
     if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
     var addresses = new List<string>();
     foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
      if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) addresses.Add(unicast.Address.ToString());
     found.AddRange(FromAdapter(adapter.Name, adapter.Description, addresses));
    }
   }
   catch (Exception) { return new List<Found>(); }
   return found;
  }

  // Blocking connect test with a real timeout; meant for a worker thread.
  public static Reach Probe(string host, int port, int timeoutMs) {
   if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535) return Reach.Failed;
   var client = new TcpClient();
   try {
    var pending = client.BeginConnect(host.Trim(), port, null, null);
    if (!pending.AsyncWaitHandle.WaitOne(timeoutMs < 0 ? 0 : timeoutMs)) return Reach.TimedOut;
    client.EndConnect(pending);
    return client.Connected ? Reach.Open : Reach.Failed;
   }
   catch (SocketException error) {
    switch (error.SocketErrorCode) {
     case SocketError.ConnectionRefused: return Reach.Refused;
     case SocketError.TimedOut: return Reach.TimedOut;
     case SocketError.HostNotFound:
     case SocketError.NoData:
     case SocketError.TryAgain: return Reach.UnknownHost;
     default: return Reach.Failed;
    }
   }
   catch (Exception) { return Reach.Failed; }
   finally { try { client.Close(); } catch (Exception) { } }
  }
 }
}
