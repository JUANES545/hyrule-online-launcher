using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HyruleOnline {
 sealed class FoundServer {
  public string Host, Via;
  public int Port, Ms;
  public string Address { get { return Host + ":" + Port; } }
 }

 // Finds Hyrule Online servers: Tailscale peers first, then the local network. Nothing here may open a TCP connection to the game port: the dedicated
 // server dies on any connection without a valid handshake. A host answers a UDP beacon on BeaconPort instead, sent by a launcher while its server runs.
 static class ServerFinder {
  public const int Port = 5050, BeaconPort = 5051, ProbeMs = 400, Probes = 64, MaxHosts = 1024, TestMs = 1500, MaxRows = 6;
  public const string Vpn = "vpn", Lan = "lan", Request = "HYRULE?", ReplyPrefix = "HYRULE1 ";

  static bool TryIp(string text, out uint ip) {
   ip = 0;
   var part = (text ?? "").Split('.');
   if (part.Length != 4) return false;
   foreach (string piece in part) {
    if (piece.Length == 0 || piece.Length > 3 || !piece.All(c => c >= '0' && c <= '9')) return false;
    int value = int.Parse(piece);
    if (value > 255) return false;
    ip = ip << 8 | (uint)value;
   }
   return true;
  }

  static string Text(uint ip) { return (ip >> 24) + "." + ((ip >> 16) & 255) + "." + ((ip >> 8) & 255) + "." + (ip & 255); }

  static bool Private(uint ip) {
   uint a = ip >> 24, b = (ip >> 16) & 255;
   return a == 10 || a == 172 && b >= 16 && b <= 31 || a == 192 && b == 168;
  }

  // 100.64.0.0/10 is the VPN range (Tailscale); everything else is the local network.
  public static string Classify(string address) {
   uint ip;
   return TryIp(address, out ip) && (ip & 0xFFC00000) == 0x64400000 ? Vpn : Lan;
  }

  static HashSet<uint> Own(IEnumerable<KeyValuePair<string, int>> interfaces) {
   var own = new HashSet<uint>();
   foreach (var face in interfaces) { uint ip; if (TryIp(face.Key, out ip)) own.Add(ip); }
   return own;
  }

  // The hosts of the network around each private address: the /24, or the smaller network when the interface prefix is longer. Never this device's own addresses.
  public static List<string> LocalHosts(IEnumerable<KeyValuePair<string, int>> interfaces) {
   var own = Own(interfaces);
   var seen = new HashSet<uint>();
   var hosts = new List<string>();
   foreach (var face in interfaces) {
    uint ip;
    if (!TryIp(face.Key, out ip) || !Private(ip) || face.Value >= 32) continue;
    uint mask = face.Value <= 24 ? 0xFFFFFF00 : 0xFFFFFFFFu << (32 - face.Value), network = ip & mask, last = network | ~mask;
    for (uint host = face.Value == 31 ? network : network + 1; host <= (face.Value == 31 ? last : last - 1); host++)
     if (!own.Contains(host) && seen.Add(host)) hosts.Add(Text(host));
   }
   return hosts;
  }

  // The online peers' IPv4 addresses from `tailscale status --json`; anything unreadable gives none.
  public static List<string> TailscaleHosts(string json) {
   var hosts = new List<string>();
   try {
    var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
    var peers = root == null || !root.ContainsKey("Peer") ? null : root["Peer"] as Dictionary<string, object>;
    if (peers == null) return hosts;
    foreach (var peer in peers.Values.OfType<Dictionary<string, object>>()) {
     var ips = peer.ContainsKey("TailscaleIPs") ? peer["TailscaleIPs"] as System.Collections.IEnumerable : null;
     if (ips == null || !peer.ContainsKey("Online") || !true.Equals(peer["Online"])) continue;
     foreach (string address in ips.OfType<string>()) { uint ip; if (TryIp(address, out ip)) hosts.Add(address); }
    }
   }
   catch (Exception) { hosts.Clear(); }
   return hosts;
  }

  // Tailscale peers first, then the last address used, then the local network. Cut to MaxHosts so the VPN ones survive.
  // shortcut: the last address is only kept when it is an IPv4 literal, so no DNS lookup can stall a probe; upgrade if names must be re-found.
  public static List<FoundServer> Candidates(IEnumerable<string> vpnHosts, IEnumerable<KeyValuePair<string, int>> interfaces, string lastHost, int lastPort) {
   var own = Own(interfaces);
   var list = new List<FoundServer>();
   var seen = new HashSet<string>();
   Action<string, int, string> add = (host, port, via) => {
    uint ip;
    if (!TryIp(host, out ip) || own.Contains(ip) || !seen.Add(host + ":" + port)) return;
    list.Add(new FoundServer { Host = host, Port = port, Via = via });
   };
   foreach (string host in vpnHosts) add(host, Port, Vpn);
   if (lastHost != null) add(lastHost, lastPort, Classify(lastHost));
   foreach (string host in LocalHosts(interfaces)) add(host, Port, Lan);
   return list.Take(MaxHosts).ToList();
  }

  static long Key(string host) { uint ip; return TryIp(host, out ip) ? ip : long.MaxValue; }

  // VPN before local, then the fastest, then by address.
  public static List<FoundServer> Order(IEnumerable<FoundServer> found) {
   return found.OrderBy(f => f.Via == Vpn ? 0 : 1).ThenBy(f => f.Ms).ThenBy(f => Key(f.Host)).ThenBy(f => f.Port).ToList();
  }

  // A search may replace the address box only when it is empty or still holds what the last search put there.
  public static bool Fills(string field, string finderValue) { return string.IsNullOrEmpty(field) || field == finderValue; }

  public static bool IsRequest(byte[] data) { return data != null && Encoding.ASCII.GetString(data) == Request; }

  public static byte[] Reply(int tcpPort) { return Encoding.ASCII.GetBytes(ReplyPrefix + tcpPort); }

  // "HYRULE1 " and a decimal port from 1 to 65535, nothing else.
  public static bool TryReply(byte[] data, out int port) {
   port = 0;
   if (data == null || data.Length <= ReplyPrefix.Length || data.Length > ReplyPrefix.Length + 5) return false;
   string text = Encoding.ASCII.GetString(data);
   if (!text.StartsWith(ReplyPrefix, StringComparison.Ordinal)) return false;
   string digits = text.Substring(ReplyPrefix.Length);
   return digits.All(c => c >= '0' && c <= '9') && int.TryParse(digits, out port) && port >= 1 && port <= 65535;
  }

  // Sends the beacon request to the host and waits for a valid reply: the milliseconds it took (at least 1) and the game port the host announced, or -1.
  public static int Ping(string host, int timeoutMs, out int port, int beaconPort = BeaconPort) {
   port = 0;
   var watch = Stopwatch.StartNew();
   try {
    using (var client = new UdpClient(AddressFamily.InterNetwork)) {
     client.Connect(host, beaconPort);
     byte[] request = Encoding.ASCII.GetBytes(Request);
     client.Send(request, request.Length);
     while (true) {
      int left = timeoutMs - (int)watch.ElapsedMilliseconds;
      if (left <= 0) return -1;
      client.Client.ReceiveTimeout = left;
      IPEndPoint from = null;
      if (TryReply(client.Receive(ref from), out port)) return Math.Max(1, (int)Math.Round(watch.Elapsed.TotalMilliseconds));
     }
    }
   }
   catch (Exception) { port = 0; return -1; }
  }

  // Pings each distinct host with up to Probes at a time; cancelling stops new pings and the ones in flight end within ProbeMs.
  public static List<FoundServer> Probe(List<FoundServer> candidates, CancellationToken cancel, int beaconPort = BeaconPort) {
   var found = new List<FoundServer>();
   var workers = new List<Thread>();
   candidates = candidates.GroupBy(c => c.Host).Select(g => g.First()).ToList();
   int next = -1;
   for (int i = 0; i < Math.Min(Probes, candidates.Count); i++) {
    var worker = new Thread(() => {
     int at;
     while (!cancel.IsCancellationRequested && (at = Interlocked.Increment(ref next)) < candidates.Count) {
      var candidate = candidates[at];
      int port, ms = Ping(candidate.Host, ProbeMs, out port, beaconPort);
      if (ms > 0) lock (found) found.Add(new FoundServer { Host = candidate.Host, Port = port, Via = candidate.Via, Ms = ms });
     }
    }) { IsBackground = true };
    worker.Start();
    workers.Add(worker);
   }
   foreach (var worker in workers) worker.Join();
   return Order(found);
  }

  // This device's IPv4 addresses with their prefix lengths, loopback included so it counts as "own".
  public static List<KeyValuePair<string, int>> Interfaces() {
   var faces = new List<KeyValuePair<string, int>>();
   try {
    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
     if (adapter.OperationalStatus != OperationalStatus.Up) continue;
     foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
      if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) faces.Add(new KeyValuePair<string, int>(unicast.Address.ToString(), unicast.PrefixLength));
    }
   }
   catch (Exception) { }
   return faces;
  }

  // The tailscale CLI on PATH, else the default install. Not installed, failing or slow (3 s) means no VPN candidates.
  public static List<string> TailscaleHosts() {
   string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
   foreach (string file in new[] { "tailscale", installed }) {
    string json = Cli(file);
    if (json != null) return TailscaleHosts(json);
   }
   return new List<string>();
  }

  static string Cli(string file) {
   try {
    var info = new ProcessStartInfo(file, "status --json") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
    using (var process = Process.Start(info)) {
     var output = process.StandardOutput.ReadToEndAsync();
     if (!process.WaitForExit(3000)) { try { process.Kill(); } catch (Exception) { } return null; }
     return process.ExitCode == 0 ? output.Result : null;
    }
   }
   catch (Exception) { return null; }
  }
 }

 // Answers the finder's beacon while this launcher's dedicated server runs. It only ever replies to the exact request, never touches the game's TCP port, and
 // does nothing when the beacon port is busy or any socket call fails.
 sealed class BeaconResponder : IDisposable {
  UdpClient socket;
  volatile bool stopped;

  public BeaconResponder(int tcpPort, int beaconPort = ServerFinder.BeaconPort) {
   try {
    socket = new UdpClient(new IPEndPoint(IPAddress.Any, beaconPort));
    new Thread(() => Listen(socket, ServerFinder.Reply(tcpPort))) { IsBackground = true }.Start();
   }
   catch (Exception) { Dispose(); }
  }

  void Listen(UdpClient client, byte[] reply) {
   while (!stopped) {
    try {
     IPEndPoint from = null;
     if (ServerFinder.IsRequest(client.Receive(ref from))) client.Send(reply, reply.Length, from);
    }
    catch (SocketException error) { if (error.SocketErrorCode != SocketError.ConnectionReset) return; }
    catch (Exception) { return; }
   }
  }

  public void Dispose() {
   stopped = true;
   try { if (socket != null) socket.Close(); } catch (Exception) { }
   socket = null;
  }
 }
}
