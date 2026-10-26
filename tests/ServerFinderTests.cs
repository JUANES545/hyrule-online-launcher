using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace HyruleOnline {
 static class ServerFinderTests {
  static int checks, failures;

  static void Check(bool ok, string message) {
   checks++;
   if (ok) return;
   failures++;
   Console.WriteLine("FAIL: " + message);
  }

  static KeyValuePair<string, int> Face(string address, int prefix) { return new KeyValuePair<string, int>(address, prefix); }
  static FoundServer Found(string via, string host, int ms) { return new FoundServer { Via = via, Host = host, Port = ServerFinder.Port, Ms = ms }; }
  static string Join(IEnumerable<string> items) { return string.Join(",", items.ToArray()); }

  static void Class(string address, string expected) {
   string actual = ServerFinder.Classify(address);
   Check(actual == expected, "Classify(" + address + ") is " + actual + ", expected " + expected);
  }

  static List<string> Range(string prefix, int from, int to, params int[] skip) {
   return Enumerable.Range(from, to - from + 1).Where(i => !skip.Contains(i)).Select(i => prefix + i).ToList();
  }

  static void Hosts() {
   var faces = new[] { Face("192.168.1.43", 24), Face("10.1.2.3", 16), Face("100.65.1.1", 32), Face("169.254.3.4", 16), Face("127.0.0.1", 8), Face("172.20.5.9", 30) };
   var expected = Range("192.168.1.", 1, 254, 43).Concat(Range("10.1.2.", 1, 254, 3)).Concat(new[] { "172.20.5.10" }).ToList();
   var actual = ServerFinder.LocalHosts(faces);
   Check(actual.SequenceEqual(expected), "LocalHosts gives " + actual.Count + " hosts, expected " + expected.Count + " (first " + (actual.Count > 0 ? actual[0] : "none") + ")");
   Check(ServerFinder.LocalHosts(new[] { Face("100.65.1.1", 24), Face("169.254.1.1", 16), Face("8.8.8.8", 24), Face("127.0.0.1", 8) }).Count == 0, "VPN, link-local, public and loopback addresses are never scanned");
   Check(ServerFinder.LocalHosts(new[] { Face("192.168.1.5", 32) }).Count == 0, "a /32 has no neighbours");
   Check(Join(ServerFinder.LocalHosts(new[] { Face("192.168.1.5", 31) })) == "192.168.1.4", "a /31 has one neighbour");
   Check(ServerFinder.LocalHosts(new[] { Face("192.168.1.5", 24), Face("192.168.1.9", 24) }).Count == 252, "two addresses on one network scan it once and skip both");
   Check(ServerFinder.LocalHosts(new[] { Face("not an address", 24) }).Count == 0, "a bad address gives nothing");
  }

  static void Tailscale() {
   const string json = "{\"Self\":{\"TailscaleIPs\":[\"100.1.1.1\",\"fd7a::1\"],\"Online\":true},\"Peer\":{\"k1\":{\"TailscaleIPs\":[\"100.65.234.99\",\"fd7a::2\"],\"Online\":true},\"k2\":{\"TailscaleIPs\":[\"100.70.0.2\"],\"Online\":false},\"k3\":{\"TailscaleIPs\":[\"fd7a::3\"],\"Online\":true}}}";
   Check(Join(ServerFinder.TailscaleHosts(json)) == "100.65.234.99", "Tailscale peers: [" + Join(ServerFinder.TailscaleHosts(json)) + "]");
   foreach (string garbage in new[] { "", "not json", "{", "{}", "[]", "{\"Peer\":null}", "{\"Peer\":{\"k\":5}}", "{\"Peer\":{\"k\":{\"Online\":true}}}", null })
    Check(ServerFinder.TailscaleHosts(garbage).Count == 0, "Tailscale garbage gives none: " + (garbage ?? "null"));
  }

  static void Ordering() {
   var order = ServerFinder.Order(new[] { Found("lan", "192.168.1.8", 4), Found("vpn", "100.70.0.2", 30), Found("vpn", "100.65.0.9", 30), Found("vpn", "100.99.0.1", 2), Found("lan", "192.168.1.20", 4) });
   string actual = Join(order.Select(f => f.Host));
   Check(actual == "100.99.0.1,100.65.0.9,100.70.0.2,192.168.1.8,192.168.1.20", "Order gives " + actual);
  }

  static void AutoFill() {
   Check(ServerFinder.Fills("", null), "an empty field is filled");
   Check(ServerFinder.Fills("", "1.2.3.4:5050"), "an empty field is filled after an earlier search");
   Check(ServerFinder.Fills("1.2.3.4:5050", "1.2.3.4:5050"), "the finder's own value is replaced");
   Check(!ServerFinder.Fills("x:1", "1.2.3.4:5050"), "typed text is kept");
   Check(!ServerFinder.Fills("x:1", null), "typed text is kept when nothing was filled yet");
   Check(!ServerFinder.Fills("  ", ""), "a field with only spaces counts as typed");
  }

  static void Candidates() {
   var faces = new[] { Face("192.168.1.43", 24), Face("100.65.1.1", 32), Face("127.0.0.1", 8) };
   var list = ServerFinder.Candidates(new[] { "100.65.234.99", "100.65.234.99", "100.65.1.1" }, faces, "192.168.1.20", 6000);
   Check(list[0].Host == "100.65.234.99" && list[0].Via == "vpn" && list.Count(f => f.Host == "100.65.234.99") == 1, "VPN peers come first, once, and this device is not one");
   Check(list.All(f => f.Host != "100.65.1.1" && f.Host != "192.168.1.43"), "this device's own addresses are never probed");
   Check(list[1].Address == "192.168.1.20:6000" && list[1].Via == "lan", "the last used address comes after the VPN peers with its own port");
   Check(list.Count(f => f.Port == 5050 && f.Via == "lan") == 253 && list.Count == 255, "the local network adds every other host once on the default port (" + list.Count + ")");
   Check(ServerFinder.Candidates(new string[0], faces, "100.70.0.2", 5050)[0].Via == "vpn", "a last used address in the VPN range counts as VPN");
   Check(ServerFinder.Candidates(new string[0], faces, "my-host.example", 5050).All(f => f.Host != "my-host.example"), "a name is not probed");
   var many = new[] { Face("192.168.1.1", 24), Face("192.168.2.1", 24), Face("192.168.3.1", 24), Face("192.168.4.1", 24), Face("192.168.5.1", 24) };
   var vpn = Enumerable.Range(1, 10).Select(i => "100.65.0." + i).ToList();
   var cut = ServerFinder.Candidates(vpn, many, null, 0);
   Check(cut.Count == ServerFinder.MaxHosts && cut.Take(10).All(f => f.Via == "vpn"), "candidates are cut to " + ServerFinder.MaxHosts + " keeping the VPN ones first (" + cut.Count + ")");
  }

  static byte[] Ascii(string text) { return System.Text.Encoding.ASCII.GetBytes(text); }

  static void Reply(string text, bool valid, int expected) {
   int port;
   bool ok = ServerFinder.TryReply(Ascii(text), out port);
   Check(ok == valid && (!valid || port == expected), "TryReply(\"" + text + "\") is " + ok + "/" + port);
  }

  static void Beacon() {
   Check(ServerFinder.BeaconPort == 5051 && ServerFinder.Request == "HYRULE?" && ServerFinder.ReplyPrefix == "HYRULE1 ", "beacon constants");
   Check(ServerFinder.IsRequest(Ascii("HYRULE?")), "the request is recognised");
   foreach (string text in new[] { "", "HYRULE", "HYRULE?\n", "hyrule?", "HYRULE? ", "HYRULE?HYRULE?", "GET / HTTP/1.1" }) Check(!ServerFinder.IsRequest(Ascii(text)), "not a request: \"" + text + "\"");
   Check(!ServerFinder.IsRequest(null), "null is not a request");
   Check(System.Text.Encoding.ASCII.GetString(ServerFinder.Reply(5050)) == "HYRULE1 5050", "reply for 5050");
   Reply("HYRULE1 5050", true, 5050);
   Reply("HYRULE1 1", true, 1);
   Reply("HYRULE1 65535", true, 65535);
   foreach (string text in new[] { "HYRULE1 0", "HYRULE1 65536", "HYRULE1 -5", "HYRULE1 ", "HYRULE1", "HYRULE1 50 50", "HYRULE1 5050\n", "HYRULE1  5050", "HYRULE2 5050", "hyrule1 5050", "HYRULE? 5050", "HYRULE1 5e3", "HYRULE1 123456", "" }) Reply(text, false, 0);
   int none;
   Check(!ServerFinder.TryReply(null, out none), "a null reply is invalid");
  }

  static int FreeUdp() {
   using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) return ((IPEndPoint)udp.Client.LocalEndPoint).Port;
  }

  static void Probing() {
   var listener = new TcpListener(IPAddress.Loopback, 0);
   listener.Start();
   int beacon = FreeUdp(), game = ((IPEndPoint)listener.LocalEndpoint).Port, port;
   try {
    int silent = ServerFinder.Ping("127.0.0.1", ServerFinder.ProbeMs, out port, beacon);
    Check(silent == -1 && port == 0, "Ping gets no answer while no host is running (" + silent + ")");
    using (var responder = new BeaconResponder(game, beacon)) {
     int ms = ServerFinder.Ping("127.0.0.1", ServerFinder.TestMs, out port, beacon);
     Check(ms >= 1 && port == game, "Ping reaches a responder and gets its game port (" + ms + ", " + port + ")");
     var candidates = new List<FoundServer> { new FoundServer { Host = "127.0.0.1", Port = 6000, Via = "lan" }, new FoundServer { Host = "127.0.0.1", Port = ServerFinder.Port, Via = "lan" } };
     var found = ServerFinder.Probe(candidates, CancellationToken.None, beacon);
     Check(found.Count == 1 && found[0].Port == game && found[0].Via == "lan" && found[0].Ms >= 1, "Probe lists the host once with the port from the reply (" + found.Count + ")");
     var cancelled = new CancellationTokenSource();
     cancelled.Cancel();
     Check(ServerFinder.Probe(candidates, cancelled.Token, beacon).Count == 0, "a cancelled search pings nothing");
     using (var second = new BeaconResponder(1234, beacon)) Check(ServerFinder.Ping("127.0.0.1", ServerFinder.TestMs, out port, beacon) >= 1 && port == game, "a second responder on a busy port is silent and the first keeps answering");
     using (var raw = new UdpClient()) {
      var target = new IPEndPoint(IPAddress.Loopback, beacon);
      foreach (string junk in new[] { "hello", "HYRULE?x", "" }) raw.Send(Ascii(junk), junk.Length, target);
      raw.Client.ReceiveTimeout = 300;
      IPEndPoint from = null;
      bool answered = true;
      try { raw.Receive(ref from); } catch (SocketException) { answered = false; }
      Check(!answered, "the responder ignores anything but the exact request");
     }
    }
    Check(ServerFinder.Ping("127.0.0.1", ServerFinder.ProbeMs, out port, beacon) == -1, "a stopped responder no longer answers");
    Check(!listener.Pending(), "no TCP connection reached the game port during any Ping or search");
    Thread.Sleep(100);
    Check(!listener.Pending(), "still no TCP connection after the responder stopped");
   }
   finally { listener.Stop(); }
   Check(ServerFinder.Ping("not a host name", ServerFinder.ProbeMs, out port, beacon) == -1, "Ping fails on a bad host");
   Check(ServerFinder.Interfaces().Any(f => f.Key == "127.0.0.1"), "Interfaces lists the loopback address");
   Check(ServerFinder.TailscaleHosts() != null, "TailscaleHosts never fails");
  }

  static int Main() {
   foreach (string address in new[] { "100.65.234.99", "100.64.0.1", "100.127.255.254" }) Class(address, "vpn");
   foreach (string address in new[] { "100.63.255.255", "100.128.0.0", "192.168.1.8", "10.0.0.5" }) Class(address, "lan");
   Hosts();
   Tailscale();
   Ordering();
   AutoFill();
   Candidates();
   Beacon();
   Probing();
   Console.WriteLine((failures == 0 ? "PASS" : "FAIL") + ": server finder, " + (checks - failures) + " of " + checks + " checks passed.");
   return failures == 0 ? 0 : 1;
  }
 }
}
