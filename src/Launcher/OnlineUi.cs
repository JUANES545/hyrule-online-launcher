using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HyruleOnline {
 // The Online page: play on this PC only, host a server friends can join, or join a friend's server.
 sealed partial class Launcher {
  string onlineMode = ServerConnection.Local, hostPassword = "", hostDescription = ServerConnection.DefaultDescription, hostAddress = "", joinAddress = "", joinPassword = "";
  int hostPort = ServerConnection.DefaultPort;
  readonly Roster roster = new Roster();
  bool addressProblem;

  bool Joining { get { return onlineMode == ServerConnection.Join; } }

  static string FirewallRule(int port) { return ServerConnection.FirewallName + port; }
  const string FinderRule = "Hyrule Online Finder";

  void WatchOnline() {
   C<ComboBox>("OnlineModeBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null) return;
    SetOnlineMode((string)item.Tag);
   };
   foreach (var name in new[] { "HostPortBox", "HostPasswordBox", "HostDescriptionBox", "HostAddressBox", "JoinAddressBox", "JoinPasswordBox" }) C<TextBox>(name).LostFocus += (s, e) => CommitOnline();
   Placeholder(C<TextBox>("JoinAddressBox"), C<TextBlock>("JoinAddressHint"));
   C<Button>("CopyInvite").Click += (s, e) => CopyInvite();
   C<Button>("OpenFirewall").Click += (s, e) => AllowFirewall();
   C<Button>("FindServersButton").Click += (s, e) => FindServers(false);
   C<Button>("TestServerButton").Click += (s, e) => TestServer();
   C<UIElement>("OnlineBody").PreviewKeyDown += FinderTab;
   Window.Closing += (s, e) => { if (finding != null) finding.Cancel(); };
  }

  System.Threading.CancellationTokenSource finding;
  List<FoundServer> foundServers = new List<FoundServer>(), shownServers;
  object shownLanguage;
  string finderValue = "";
  Func<string> findLine;
  bool testing;

  // Tailscale peers and the local network are probed off the UI thread; the best answer fills an empty address box.
  void FindServers(bool automatic) {
   if (finding != null || !Joining || (automatic ? sessionLive || script != null : !appearanceEnabled)) return;
   var source = finding = new System.Threading.CancellationTokenSource();
   findLine = () => Tr("Searching your VPN and local network…");
   ShowFinder();
   string lastHost, secret;
   int lastPort;
   if (!ServerConnection.TryParse(joinAddress, out lastHost, out lastPort, out secret)) lastHost = null;
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    List<string> vpn = new List<string>();
    List<FoundServer> rows = new List<FoundServer>();
    try {
     vpn = ServerFinder.TailscaleHosts();
     rows = ServerFinder.Probe(ServerFinder.Candidates(vpn, ServerFinder.Interfaces(), lastHost, lastPort), source.Token);
    }
    catch (Exception) { }
    Window.Dispatcher.BeginInvoke(new Action(() => FinderDone(source, rows, vpn.Count == 0)));
   });
  }

  void FinderDone(System.Threading.CancellationTokenSource source, List<FoundServer> rows, bool noVpn) {
   if (finding != source) return;
   finding = null;
   if (source.IsCancellationRequested) { findLine = null; ShowFinder(); return; }
   int total = rows.Count;
   foundServers = rows.Take(ServerFinder.MaxRows).ToList();
   var box = C<TextBox>("JoinAddressBox");
   if (Joining && appearanceEnabled && foundServers.Count > 0 && ServerFinder.Fills(box.Text, finderValue)) {
    box.Text = finderValue = foundServers[0].Address;
    CommitOnline();
   }
   findLine = () => total > 0 ? Tr("Servers found: {0}.", total)
    : Tr("No servers found. Check that the host has a session running, or type the address.") + (noVpn ? " " + Tr("Tailscale was not found or is not connected on this device.") : "");
   ShowFinder();
  }

  // Test only sends the UDP beacon request; it never opens a connection to the game port, joins or starts anything.
  void TestServer() {
   if (testing) return;
   string host, secret;
   int port;
   if (!ServerConnection.TryParse(C<TextBox>("JoinAddressBox").Text, out host, out port, out secret)) {
    findLine = () => Tr("The server address is not valid. Enter an invite link or host:port on the Online page.");
    ShowFinder();
    return;
   }
   testing = true;
   findLine = null;
   ShowFinder();
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    int ignored, ms = ServerFinder.Ping(host, ServerFinder.TestMs, out ignored);
    Window.Dispatcher.BeginInvoke(new Action(() => {
     testing = false;
     findLine = () => ms > 0 ? Tr("Reachable in {0} ms.", ms) : Tr("Cannot reach {0}:{1}. Check the address, that the host has a session running, and its firewall.", host, port);
     ShowFinder();
    }));
   });
  }

  static Brush Soft { get { return new SolidColorBrush(Color.FromRgb(0x9E, 0xB2, 0xBD)); } }

  // Rebuilds the rows only when the results or the language changed, so a focused row keeps focus while other things refresh.
  void ShowFinder() {
   bool rows = Joining && foundServers.Count > 0;
   C<UIElement>("FoundBox").Visibility = rows ? Visibility.Visible : Visibility.Collapsed;
   var list = C<StackPanel>("FoundList");
   if (shownServers != foundServers || !object.Equals(shownLanguage, Lang.Current)) {
    shownServers = foundServers;
    shownLanguage = Lang.Current;
    list.Children.Clear();
    foreach (var found in foundServers) {
     var row = new Button { FontStyle = FontStyles.Normal, FontSize = 16, Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = found.Address, ToolTip = Tr("Paste the invite your friend sent you, or type their address and port.") };
     string badge = Tr(found.Via == ServerFinder.Vpn ? "VPN" : "Local"), speed = found.Ms + " ms";
     var grid = new Grid();
     grid.ColumnDefinitions.Add(new ColumnDefinition());
     grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
     grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
     var name = new TextBlock { Text = found.Address };
     var kind = new TextBlock { Text = badge, Margin = new Thickness(16, 0, 0, 0) };
     var time = new TextBlock { Text = speed, Margin = new Thickness(16, 0, 0, 0), Foreground = Soft };
     Grid.SetColumn(kind, 1);
     Grid.SetColumn(time, 2);
     grid.Children.Add(name); grid.Children.Add(kind); grid.Children.Add(time);
     row.Content = grid;
     System.Windows.Automation.AutomationProperties.SetName(row, found.Address + ", " + badge + ", " + speed);
     row.Click += (s, e) => { C<TextBox>("JoinAddressBox").Text = (string)((Button)s).Tag; CommitOnline(); };
     list.Children.Add(row);
    }
   }
   var line = C<TextBlock>("FindResult");
   line.Text = findLine == null ? "" : findLine();
   line.Visibility = line.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
   EnableFinder();
  }

  void EnableFinder() {
   C<Button>("FindServersButton").IsEnabled = appearanceEnabled && finding == null;
   C<Button>("TestServerButton").IsEnabled = !testing;
   foreach (var row in C<StackPanel>("FoundList").Children.OfType<Button>()) row.IsEnabled = appearanceEnabled;
  }

  // The rows sit in the right column, so Tab is steered to follow the spec: address, Search, Test, rows, then Password.
  void FinderTab(object sender, System.Windows.Input.KeyEventArgs e) {
   if (e.Key != System.Windows.Input.Key.Tab || (System.Windows.Input.Keyboard.Modifiers & ~System.Windows.Input.ModifierKeys.Shift) != 0) return;
   var rows = C<StackPanel>("FoundList").Children.OfType<Button>().Where(row => row.IsEnabled).ToList();
   if (rows.Count == 0 || !Joining) return;
   bool back = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
   var at = System.Windows.Input.Keyboard.FocusedElement;
   Action<UIElement> go = target => { target.Focus(); e.Handled = true; };
   if (!back && at == C<Button>("TestServerButton")) go(rows[0]);
   else if (!back && at == rows[rows.Count - 1]) go(C<TextBox>("JoinPasswordBox"));
   else if (!back && at == C<TextBox>("JoinPasswordBox")) { e.Handled = true; rows[rows.Count - 1].MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)); }
   else if (back && at == rows[0]) go(C<Button>("TestServerButton"));
   else if (back && at == C<TextBox>("JoinPasswordBox")) go(rows[rows.Count - 1]);
   else if (back && at == C<TextBox>("JoinPasswordBox")) go(rows[rows.Count - 1]);
   else if (back && at is UIElement && !rows.Contains(at as Button)) {
    // Whatever follows the list steps back to Password, not into the list.
    e.Handled = true;
    ((UIElement)at).MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Previous));
    if (System.Windows.Input.Keyboard.FocusedElement == rows[rows.Count - 1]) C<TextBox>("JoinPasswordBox").Focus();
   }
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

  // Joining failed. The launcher never connects to the host's port on its own when a session starts; only the Search and Test buttons do, with a bare connect that sends nothing.
  void ExplainJoinFailure() {
   if (!Joining) return;
   failure = failure + " " + Tr("Check the invite, the password, that the host has a session running, and that its firewall allows the port.");
   SetStatus("Start failed: {0}", failure);
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

  // On the Online page while hosting: where the session is open and a framed card, in the player's colour, for each player in the world.
  void ShowOnlineStatus() {
   if (onlineMode != ServerConnection.Host) return;
   string address = hostAddress.Length > 0 ? hostAddress : DetectedAddress();
   var players = roster.Players();
   var status = Tr(serverHost.Running ? "Server open at {0}:{1}." : "Friends will join at {0}:{1} once the session starts.", address, hostPort);
   if (players.Count == 0 && serverHost.Running) status += Environment.NewLine + Tr("Nobody has joined yet.");
   C<TextBlock>("OnlineStatusText").Text = status;
   C<TextBlock>("PlayerEvent").Text = eventText == null ? "" : Tr(eventText, eventArgs);
   string signature = string.Join("\n", players.Select(p => p.Key + "=" + p.Value).ToArray());
   if (signature == shownPlayers) return;
   shownPlayers = signature;
   var list = C<StackPanel>("PlayerList");
   list.Children.Clear();
   foreach (var player in players) list.Children.Add(PlayerCard(player.Key, player.Value));
  }
  string shownPlayers;

  static UIElement PlayerCard(int slot, string name) {
   var color = (Color)ColorConverter.ConvertFromString(Roster.ColorOf(slot));
   var badge = new Border {
    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 12, 0),
    Child = new TextBlock { Text = slot.ToString(), Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x15)), FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
   };
   var label = new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xF2)), FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
   Grid.SetColumn(label, 1);
   var row = new Grid();
   row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   row.ColumnDefinitions.Add(new ColumnDefinition());
   row.Children.Add(badge);
   row.Children.Add(label);
   var card = new Border {
    BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 0, 6),
    Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)), Child = row
   };
   System.Windows.Automation.AutomationProperties.SetName(card, slot + ". " + name);
   return card;
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
   FindServers(true);
  }

  // Reads the boxes, writes the cleaned text back and refreshes what depends on it.
  void CommitOnline() {
   hostPort = ServerConnection.CleanPort(C<TextBox>("HostPortBox").Text);
   hostPassword = ServerConnection.HostPassword(C<TextBox>("HostPasswordBox").Text);
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
   C<UIElement>("HostFields").Visibility = C<UIElement>("HostShare").Visibility = C<UIElement>("PlayersBox").Visibility = show(host);
   C<UIElement>("JoinFields").Visibility = C<UIElement>("JoinHelp").Visibility = C<UIElement>("WorldJoinNote").Visibility = show(Joining);
   if (host) { string shown = C<TextBox>("HostAddressBox").Text; C<TextBlock>("NetworkHintText").Text = (addressProblem ? Tr("That address is not valid, so the previous one was kept. Use letters, digits, dots and dashes only.") + " " : "") + AddressHint(shown); }
   ShowOnlineStatus();
   C<TextBlock>("OnlineModeInfo").Text = Tr(host ? "Friends who run this launcher can join this PC. The world rules are the ones on the World page."
    : Joining ? "One or two players on this PC join a friend's server. The friend sets the rules."
    : "The server only accepts players from this PC.");
   ShowFirewall();
   ShowFinder();
  }

  bool firewallOpen;

  void ShowFirewall() {
   if (onlineMode != ServerConnection.Host) return;
   // A rule without the VPN range in its output is missing or an older one open to every address.
   firewallOpen = IsScoped(FirewallRule(hostPort)) && IsScoped(FinderRule);
   C<TextBlock>("FirewallInfo").Text = Tr(firewallOpen ? "Windows Firewall already allows port {0}." : "Windows Firewall may block port {0}. Allow it so friends can connect.", hostPort);
   C<Button>("OpenFirewall").IsEnabled = !firewallOpen;
  }

  // Only the home network and Tailscale may reach the game: the server dies on any stray TCP connection.
  const string RuleScope = " remoteip=LocalSubnet,100.64.0.0/10";

  static bool IsScoped(string rule) {
   return NetshOutput("advfirewall firewall show rule name=\"" + rule + "\"").Contains("100.64.0.0");
  }

  // Windows asks for administrator permission once; the launcher itself keeps running without it. Rules left over from other ports are removed in the same step.
  void AllowFirewall() {
   int port = hostPort;
   C<Button>("OpenFirewall").IsEnabled = false;
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    var commands = new List<string>();
    foreach (int old in ServerConnection.StaleRulePorts(NetshOutput("advfirewall firewall show rule name=all"), port)) commands.Add("netsh advfirewall firewall delete rule name=\"" + FirewallRule(old) + "\"");
    commands.Add("netsh advfirewall firewall delete rule name=\"" + FirewallRule(port) + "\"");
    commands.Add("netsh advfirewall firewall delete rule name=\"" + FinderRule + "\"");
    commands.Add("netsh advfirewall firewall add rule name=\"" + FirewallRule(port) + "\" dir=in action=allow protocol=TCP localport=" + port + RuleScope
     + " && netsh advfirewall firewall add rule name=\"" + FinderRule + "\" dir=in action=allow protocol=UDP localport=" + ServerFinder.BeaconPort + RuleScope);
    bool added = RunElevated("cmd.exe", "/c " + string.Join(" & ", commands.ToArray()));
    Window.Dispatcher.BeginInvoke(new Action(() => {
     Write(added ? "Firewall rules added for TCP port " + port + " and the server finder (UDP " + ServerFinder.BeaconPort + ")." : "The firewall rule was not added.");
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
