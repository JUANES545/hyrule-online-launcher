using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HyruleOnline {
 // The Online page: play on this PC only, host a server friends can join, or join a friend's server.
 sealed partial class Launcher {
  string onlineMode = ServerConnection.Local, hostPassword = "", hostDescription = ServerConnection.DefaultDescription, hostAddress = "", joinAddress = "", joinPassword = "";
  int hostPort = ServerConnection.DefaultPort;
  readonly Roster roster = new Roster();
  bool addressProblem;

  bool Joining { get { return onlineMode == ServerConnection.Join; } }

  static string FirewallRule(int port) { return ServerConnection.FirewallName + port; }

  void WatchOnline() {
   C<ComboBox>("OnlineModeBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null) return;
    SetOnlineMode((string)item.Tag);
   };
   foreach (var name in new[] { "HostPortBox", "HostPasswordBox", "HostDescriptionBox", "HostAddressBox", "JoinAddressBox", "JoinPasswordBox" }) C<TextBox>(name).LostFocus += (s, e) => CommitOnline();
   Placeholder(C<TextBox>("JoinAddressBox"), C<TextBlock>("JoinAddressHint"));
   C<Button>("CopyInvite").Click += (s, e) => CopyInvite();
   C<Button>("PlayCopyInvite").Click += (s, e) => CopyInvite();
   C<Button>("TestConnection").Click += (s, e) => TestJoin();
   C<Button>("OpenFirewall").Click += (s, e) => AllowFirewall();
  }

  void CopyInvite() {
   try { Clipboard.SetText(C<TextBox>("InviteBox").Text); ServerEvent("The invite was copied."); }
   catch (Exception error) { Write("The invite could not be copied: " + error.Message); }
  }

  // The kind of address decides what a friend needs to reach it, so the hint follows the address in the box.
  string AddressHint(string address) {
   string vpn = NetworkHint.VpnName(address);
   if (vpn == null) foreach (var found in NetworkHint.Vpns()) if (found.Address == address) vpn = found.Name;
   if (vpn != null) return Tr("This is a {0} address. Friends on the same {0} network can join from anywhere.", vpn);
   switch (NetworkHint.Classify(address)) {
    case AddressKind.Home: return Tr("This is a home network address, so only people on the same network can join. For anyone else, install a VPN such as Tailscale on both PCs, or forward the port on your router and use your public address.");
    case AddressKind.None: return Tr("No network address was found. Connect this PC to a network, or type an address.");
    default: return Tr("Friends can join from anywhere if the port is forwarded on your router.");
   }
  }

  string ReachText(NetworkHint.Reach reach, string host, int port, bool afterStart) {
   if (reach == NetworkHint.Reach.Open) return Tr(afterStart ? "The server at {0}:{1} answers, but the player could not join. Check the password first." : "The server at {0}:{1} answers. This does not check the password.", host, port);
   if (reach == NetworkHint.Reach.UnknownHost) return Tr("Could not find {0}. Check the address in the invite.", host);
   return Tr("Could not reach {0}:{1}. Check the invite, that the host has a session running, and that its firewall allows the port.", host, port);
  }

  void TestJoin() {
   string host, password;
   int port;
   var result = C<TextBlock>("TestResult");
   if (!ServerTarget(out host, out port, out password)) { result.Text = Tr("Enter an invite or an address first."); return; }
   var button = C<Button>("TestConnection");
   button.IsEnabled = false;
   result.Text = Tr("Testing {0}:{1}...", host, port);
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    var reach = NetworkHint.Probe(host, port, 4000);
    Window.Dispatcher.BeginInvoke(new Action(() => { result.Text = ReachText(reach, host, port, false); button.IsEnabled = true; }));
   });
  }

  // Joining failed: the launcher cannot read the protocol's reason, so it tells apart "nobody answers" from "answers but refused".
  void ExplainJoinFailure() {
   string host, password, original = failure;
   int port;
   if (!Joining || !ServerTarget(out host, out port, out password)) return;
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    var reach = NetworkHint.Probe(host, port, 4000);
    Window.Dispatcher.BeginInvoke(new Action(() => {
     if (failure != original) return;
     string hint = ReachText(reach, host, port, true);
     failure = reach == NetworkHint.Reach.Open ? original + " " + hint : hint;
     SetStatus("Start failed: {0}", failure);
    }));
   });
  }

  string eventText;
  object[] eventArgs;

  // A server that ends while the session is up and nobody asked for it: say so and offer the restart (Start becomes Restart session).
  void ServerStopped(int code) {
   if (stopping || Joining) return;
   if (script != null) {
    // Died while the start script was still waiting for it: stop waiting. ScriptExited keeps this reason.
    Write("The server stopped while starting (exit code " + code + ").");
    failure = Tr("The server stopped while starting (exit code {0}).", code);
    SetStatus("Start failed: {0}", failure);
    SetPrompt("Use Stop session to close anything that was started, then try again.");
    StopScript();
    return;
   }
   if (!sessionLive) return;
   serverCrashed = true;
   Write("The server stopped (exit code " + code + ").");
   SetStatus("The server stopped.");
   SetPrompt(CrashPrompt);
   roster.Clear();
   ShowOnlineStatus();
   UpdateStart();
  }

  void ServerEvent(string english, params object[] args) {
   eventText = english; eventArgs = args;
   Write(string.Format(english, args));
   ShowOnlineStatus();
  }

  void ResetServerEvents() { roster.Clear(); eventText = null; ShowOnlineStatus(); }

  // The server's own lines tell who is in and why a player was turned away.
  void ReadServerLine(string line) {
   string name;
   int slot, code;
   bool changed = roster.Apply(line);
   if (ServerEvents.TryRefused(line, out name, out code)) ServerEvent(code == ServerEvents.WrongPassword ? "{0} could not join: wrong password." : "{0} could not join (error {1}).", name, code);
   else if (ServerEvents.TryJoined(line, out name, out slot)) ServerEvent("{0} joined.", name);
   else if (ServerEvents.TryLeft(line, out name)) ServerEvent("{0} left.", name);
   else if (changed) ShowOnlineStatus();
  }

  // Under Play: where the session is open and who is in it.
  void ShowOnlineStatus() {
   bool host = onlineMode == ServerConnection.Host;
   C<UIElement>("OnlineStatus").Visibility = onlineMode == ServerConnection.Local ? Visibility.Collapsed : Visibility.Visible;
   C<UIElement>("PlayCopyInvite").Visibility = host ? Visibility.Visible : Visibility.Collapsed;
   var lines = new List<string>();
   string target, password;
   int port;
   if (host) {
    string address = hostAddress.Length > 0 ? hostAddress : DetectedAddress();
    lines.Add(Tr(serverHost.Running ? "Server open at {0}:{1}." : "Friends will join at {0}:{1} once the session starts.", address, hostPort));
    if (roster.Count > 0) lines.Add(Tr("In the world: {0}", roster.Text()));
    else if (serverHost.Running) lines.Add(Tr("Nobody has joined yet."));
   }
   else if (ServerTarget(out target, out port, out password)) lines.Add(Tr("Playing on the server at {0}:{1}.", target, port));
   else lines.Add(Tr("Enter an invite on the Online page."));
   if (eventText != null) lines.Add(Tr(eventText, eventArgs));
   C<TextBlock>("OnlineStatusText").Text = string.Join(Environment.NewLine, lines.ToArray());
  }

  // The hint sits over an empty box and disappears while the box has text or focus, like a form field.
  void Placeholder(TextBox box, TextBlock hint) {
   Action show = () => hint.Visibility = box.Text.Length == 0 && !box.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
   box.TextChanged += (s, e) => show();
   box.GotKeyboardFocus += (s, e) => show();
   box.LostKeyboardFocus += (s, e) => show();
   show();
  }

  void SetOnlineMode(string next) {
   if (next == onlineMode) return;
   onlineMode = next;
   SavePrefs();
   ShowOnline();
   UpdateStart();
   EnableAppearance(appearanceEnabled);
  }

  // Reads the boxes, writes the cleaned text back and refreshes what depends on it.
  void CommitOnline() {
   hostPort = ServerConnection.CleanPort(C<TextBox>("HostPortBox").Text);
   hostPassword = ServerConnection.CleanText(C<TextBox>("HostPasswordBox").Text, 40);
   hostDescription = ServerConnection.CleanText(C<TextBox>("HostDescriptionBox").Text, 80);
   string typed = ServerConnection.CleanText(C<TextBox>("HostAddressBox").Text, 100), shownHost, shownSecret;
   int shownPort;
   // A "host:port" or invite typed here would make the invite "host:port:port"; keep the host and move the port to its own box.
   string bare = typed.StartsWith("hyrule://", StringComparison.OrdinalIgnoreCase) ? typed.Substring(9) : typed;
   if (bare.IndexOf(':') >= 0 && ServerConnection.TryParse(bare, out shownHost, out shownPort, out shownSecret)) { typed = shownHost; hostPort = shownPort; }
   else typed = bare;
   addressProblem = typed.Length > 0 && !ServerConnection.ValidHost(typed);
   if (addressProblem) typed = hostAddress;
   hostAddress = typed == DetectedAddress() ? "" : typed;
   if (C<TextBox>("JoinAddressBox").Text != joinAddress) C<TextBlock>("TestResult").Text = "";
   joinAddress = ServerConnection.CleanText(C<TextBox>("JoinAddressBox").Text, 200);
   joinPassword = ServerConnection.CleanText(C<TextBox>("JoinPasswordBox").Text, 40);
   SavePrefs();
   ShowOnline();
   UpdateStart();
  }

  // A tunnel address (Tailscale, ZeroTier, Hamachi, Radmin) works from anywhere, so it is offered before a home address.
  static string detectedAddress;
  static int detectedAt;

  // Adapter enumeration is slow on machines with many virtual adapters, and the status refreshes every 2 s, so keep the answer for 30 s.
  static string DetectedAddress() {
   if (detectedAddress != null && Environment.TickCount - detectedAt < 30000) return detectedAddress;
   var vpns = NetworkHint.Vpns();
   detectedAddress = vpns.Count > 0 ? vpns[0].Address : ServerConnection.Best(ServerConnection.Addresses()) ?? "";
   detectedAt = Environment.TickCount;
   return detectedAddress;
  }

  void ShowOnline() {
   filling = true;
   try {
    var box = C<ComboBox>("OnlineModeBox");
    box.Items.Clear();
    var modes = new[] { new KeyValuePair<string, string>(Tr("Play on this PC"), ServerConnection.Local), new KeyValuePair<string, string>(Tr("Host a server"), ServerConnection.Host), new KeyValuePair<string, string>(Tr("Join a server"), ServerConnection.Join) };
    foreach (var pair in modes) {
     box.Items.Add(new ComboBoxItem { Content = pair.Key, Tag = pair.Value });
     if (pair.Value == onlineMode) box.SelectedIndex = box.Items.Count - 1;
    }
    C<TextBox>("HostPortBox").Text = hostPort.ToString();
    C<TextBox>("HostPasswordBox").Text = hostPassword;
    C<TextBox>("HostDescriptionBox").Text = hostDescription;
    C<TextBox>("JoinAddressBox").Text = joinAddress;
    C<TextBox>("JoinPasswordBox").Text = joinPassword;
    C<TextBox>("HostAddressBox").Text = hostAddress.Length > 0 ? hostAddress : DetectedAddress();
    string address = C<TextBox>("HostAddressBox").Text;
    C<TextBox>("InviteBox").Text = address.Length == 0 ? "" : ServerConnection.Invite(address, hostPort, hostPassword);
   }
   finally { filling = false; }
   bool host = onlineMode == ServerConnection.Host;
   Func<bool, Visibility> show = on => on ? Visibility.Visible : Visibility.Collapsed;
   C<UIElement>("HostFields").Visibility = C<UIElement>("HostShare").Visibility = show(host);
   C<UIElement>("JoinFields").Visibility = C<UIElement>("JoinHelp").Visibility = C<UIElement>("WorldJoinNote").Visibility = show(Joining);
   if (host) { string shown = C<TextBox>("HostAddressBox").Text; C<TextBlock>("NetworkHintText").Text = (addressProblem ? Tr("That address is not valid, so the previous one was kept. Use letters, digits, dots and dashes only.") + " " : "") + AddressHint(shown); }
   ShowOnlineStatus();
   C<TextBlock>("OnlineModeInfo").Text = Tr(host ? "Friends who run this launcher can join this PC. The world rules are the ones on the World page."
    : Joining ? "One or two players on this PC join a friend's server. The friend sets the rules."
    : "The server only accepts players from this PC.");
   ShowFirewall();
  }

  bool firewallOpen;

  void ShowFirewall() {
   if (onlineMode != ServerConnection.Host) return;
   firewallOpen = RunNetsh("advfirewall firewall show rule name=\"" + FirewallRule(hostPort) + "\"", false);
   C<TextBlock>("FirewallInfo").Text = Tr(firewallOpen ? "Windows Firewall already allows port {0}." : "Windows Firewall may block port {0}. Allow it so friends can connect.", hostPort);
   C<Button>("OpenFirewall").IsEnabled = !firewallOpen;
  }

  static bool RunNetsh(string arguments, bool elevated) {
   try {
    var info = new ProcessStartInfo("netsh.exe", arguments) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    if (elevated) { info.UseShellExecute = true; info.Verb = "runas"; }
    else { info.UseShellExecute = false; info.RedirectStandardOutput = true; }
    using (var process = Process.Start(info)) {
     if (!elevated) process.StandardOutput.ReadToEnd();
     if (!process.WaitForExit(20000)) return false;
     return process.ExitCode == 0;
    }
   }
   catch (Exception) { return false; }
  }

  // Windows asks for administrator permission once; the launcher itself keeps running without it. Rules left over from other ports are removed in the same step.
  void AllowFirewall() {
   int port = hostPort;
   C<Button>("OpenFirewall").IsEnabled = false;
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    var commands = new List<string>();
    foreach (int old in ServerConnection.StaleRulePorts(NetshOutput("advfirewall firewall show rule name=all"), port)) commands.Add("netsh advfirewall firewall delete rule name=\"" + FirewallRule(old) + "\"");
    commands.Add("netsh advfirewall firewall add rule name=\"" + FirewallRule(port) + "\" dir=in action=allow protocol=TCP localport=" + port);
    bool added = RunElevated("cmd.exe", "/c " + string.Join(" & ", commands.ToArray()));
    Window.Dispatcher.BeginInvoke(new Action(() => {
     Write(added ? "Firewall rule added for TCP port " + port + "." : "The firewall rule was not added.");
     ShowFirewall();
    }));
   });
  }

  static string NetshOutput(string arguments) {
   try {
    var info = new ProcessStartInfo("netsh.exe", arguments) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
    using (var process = Process.Start(info)) {
     string text = process.StandardOutput.ReadToEnd();
     process.WaitForExit(20000);
     return text;
    }
   }
   catch (Exception) { return ""; }
  }

  static bool RunElevated(string file, string arguments) {
   try {
    var info = new ProcessStartInfo(file, arguments) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = true, Verb = "runas" };
    using (var process = Process.Start(info)) return process.WaitForExit(30000) && process.ExitCode == 0;
   }
   catch (Exception) { return false; }
  }

  // Where the local players connect: this PC's own server, or the friend's server.
  bool ServerTarget(out string host, out int port, out string password) {
   host = ServerConnection.LoopbackAddress; port = ServerConnection.DefaultPort; password = "";
   if (onlineMode == ServerConnection.Host) { port = hostPort; password = hostPassword; return true; }
   if (!Joining) return true;
   string given;
   if (!ServerConnection.TryParse(joinAddress, out host, out port, out given)) return false;
   password = joinPassword.Length > 0 ? joinPassword : given;
   return true;
  }

  // Writes the connection the host's server will listen on; Play on this PC stays on this PC.
  void SaveServerConnection() {
   if (Joining || !Directory.Exists(ServerFolder)) return;
   try {
    bool host = onlineMode == ServerConnection.Host;
    ServerConnection.Save(ServerIni, host ? ServerConnection.AnyAddress : ServerConnection.LoopbackAddress, host ? hostPort : ServerConnection.DefaultPort, host ? hostPassword : "", host ? hostDescription : ServerConnection.DefaultDescription);
   }
   catch (Exception error) { Write("Could not save the server connection: " + error.Message); }
  }
 }
}
