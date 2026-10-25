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
  int hostPort = ServerConnection.DefaultPort, playersBeforeJoin = 2;

  bool Joining { get { return onlineMode == ServerConnection.Join; } }

  static string FirewallRule(int port) { return "Hyrule Online Server " + port; }

  void WatchOnline() {
   C<ComboBox>("OnlineModeBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null) return;
    SetOnlineMode((string)item.Tag);
   };
   foreach (var name in new[] { "HostPortBox", "HostPasswordBox", "HostDescriptionBox", "HostAddressBox", "JoinAddressBox", "JoinPasswordBox" }) C<TextBox>(name).LostFocus += (s, e) => CommitOnline();
   Placeholder(C<TextBox>("JoinAddressBox"), C<TextBlock>("JoinAddressHint"));
   C<Button>("CopyInvite").Click += (s, e) => {
    try { Clipboard.SetText(C<TextBox>("InviteBox").Text); Write("The invite was copied."); }
    catch (Exception error) { Write("The invite could not be copied: " + error.Message); }
   };
   C<Button>("OpenFirewall").Click += (s, e) => AllowFirewall();
  }

  // The hint sits over an empty box and disappears while the box has text or focus, like a form field.
  void Placeholder(TextBox box, TextBlock hint) {
   Action show = () => hint.Visibility = box.Text.Length == 0 && !box.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
   box.TextChanged += (s, e) => show();
   box.GotKeyboardFocus += (s, e) => show();
   box.LostKeyboardFocus += (s, e) => show();
   show();
  }

  // Joining plays one local player, so the player count is set aside until the mode changes back.
  void SetOnlineMode(string next) {
   if (next == onlineMode) return;
   bool wasJoining = Joining;
   onlineMode = next;
   if (Joining) { playersBeforeJoin = playerCount; SetPlayerCount(1, actorSlot); }
   else if (wasJoining && playersBeforeJoin == 2) SetPlayerCount(2, actorSlot);
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
   string typed = ServerConnection.CleanText(C<TextBox>("HostAddressBox").Text, 100);
   hostAddress = typed == DetectedAddress() ? "" : typed;
   joinAddress = ServerConnection.CleanText(C<TextBox>("JoinAddressBox").Text, 200);
   joinPassword = ServerConnection.CleanText(C<TextBox>("JoinPasswordBox").Text, 40);
   SavePrefs();
   ShowOnline();
   UpdateStart();
  }

  static string DetectedAddress() { return ServerConnection.Best(ServerConnection.Addresses()) ?? ""; }

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
   C<UIElement>("JoinFields").Visibility = C<UIElement>("JoinHelp").Visibility = show(Joining);
   C<TextBlock>("OnlineModeInfo").Text = Tr(host ? "Friends who run this launcher can join this PC. The world rules are the ones on the Multiplayer page."
    : Joining ? "You play on one screen with one controller on a friend's server. The friend sets the rules."
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

  // Windows asks for administrator permission; the launcher itself keeps running without it.
  void AllowFirewall() {
   int port = hostPort;
   C<Button>("OpenFirewall").IsEnabled = false;
   System.Threading.ThreadPool.QueueUserWorkItem(state => {
    bool added = RunNetsh("advfirewall firewall add rule name=\"" + FirewallRule(port) + "\" dir=in action=allow protocol=TCP localport=" + port, true);
    Window.Dispatcher.BeginInvoke(new Action(() => {
     Write(added ? "Firewall rule added for TCP port " + port + "." : "The firewall rule was not added.");
     ShowFirewall();
    }));
   });
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
