using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace HyruleOnline {
 // The first-run screen. It replaces the PLAY page and the footer while the preflight finds something required missing, and goes away by
 // itself once nothing is. Everything slow (the checks, the download, the scan of the user's folder) runs off the UI thread.
 sealed partial class Launcher {
  bool setupActive, setupChecking, installing, installCancelled, locating;
  List<SetupCheck> setupChecks = new List<SetupCheck>();
  readonly List<Msg> setupNotes = new List<Msg>();
  Process installProcess;
  Msg setupProgress;
  int setupPercent;
  string installFailure;

  // Existing complete installs pass the check without any change on screen: the wizard only appears when something is missing.
  void StartSetupCheck() {
   if (setupChecking || installing || locating) return;
   setupChecking = true;
   if (setupActive) { setupProgress = Msg.Say("Checking your install..."); RenderSetup(); }
   ThreadPool.QueueUserWorkItem(state => {
    List<SetupCheck> checks = null;
    string problem = null;
    try { checks = Preflight.Run(root, SetupManifest.Load(root), SetupFacts.Detect(root)); }
    catch (Exception error) { problem = error.Message; }
    Window.Dispatcher.BeginInvoke(new Action(() => {
     setupChecking = false;
     if (problem != null) Write("The setup check was skipped: " + problem);
     ShowSetup(checks);
    }));
   });
  }

  void ShowSetup(List<SetupCheck> checks) {
   setupProgress = null;
   if (checks == null) { if (setupActive) RenderSetup(); return; }
   setupChecks = checks;
   bool needed = Preflight.NeedsSetup(checks);
   if (needed && !setupActive && (script != null || sessionLive || Scripts.Owned(root).Count > 0)) return;
   if (needed) { if (!setupActive) EnterSetup(); RenderSetup(); }
   else if (setupActive) LeaveSetup();
  }

  static readonly string[] SetupChrome = { "NavRow", "StatusBar", "Start", "Stop" };

  void EnterSetup() {
   setupActive = true;
   ClosePickers();
   ResetCursors();
   foreach (string name in PageNames) C<FrameworkElement>(name + "Page").Visibility = Visibility.Collapsed;
   foreach (string name in SetupChrome) C<FrameworkElement>(name).Visibility = Visibility.Collapsed;
   C<FrameworkElement>("SetupPage").Visibility = Visibility.Visible;
   Window.UpdateLayout();
   C<Button>("SetupDownload").Focus();
  }

  void LeaveSetup() {
   setupActive = false;
   ResetCursors();
   C<FrameworkElement>("SetupPage").Visibility = Visibility.Collapsed;
   foreach (string name in SetupChrome) C<FrameworkElement>(name).Visibility = Visibility.Visible;
   ShowPage(0);
   C<Button>("Start").Focus();
  }

  void WireSetup() {
   C<Button>("SetupDownload").Click += (s, e) => RunInstall();
   C<Button>("SetupCancel").Click += (s, e) => StopInstall();
   C<Button>("SetupLocate").Click += (s, e) => LocateGame();
   C<Button>("SetupRecheck").Click += (s, e) => StartSetupCheck();
  }

  static Brush SetupBrush(SetupStatus status) { return status == SetupStatus.Ok ? Good : status == SetupStatus.Missing ? Busy : Bad; }

  static string SetupWord(SetupStatus status) { return status == SetupStatus.Ok ? Tr("Ready") : status == SetupStatus.Missing ? Tr("Missing") : Tr("Needs repair"); }

  TextBlock SetupText(string text, string color, double size, bool bold) {
   return new TextBlock { Text = text, Foreground = Paint(color), FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
  }

  FrameworkElement SetupRow(SetupCheck check) {
   var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
   row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
   row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
   row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
   row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
   var dot = SetupText("\u25CF", "#FFFFFF", 14, false);
   dot.Foreground = SetupBrush(check.Status);
   row.Children.Add(dot);
   var item = SetupText(check.Item.Translated(), "#CFE3E4", 15, true);
   Grid.SetColumn(item, 1);
   row.Children.Add(item);
   var word = SetupText(SetupWord(check.Status), "#FFFFFF", 14, false);
   word.Foreground = SetupBrush(check.Status);
   Grid.SetColumn(word, 2);
   row.Children.Add(word);
   var detail = new StackPanel();
   if (check.Detail != null) detail.Children.Add(SetupText(check.Detail.Translated(), "#9EB2BD", 13, false));
   if (check.Fix != null) detail.Children.Add(SetupText(check.Fix.Translated(), "#E0BE75", 13, false));
   Grid.SetColumn(detail, 3);
   row.Children.Add(detail);
   return row;
  }

  void RenderSetup() {
   if (!setupActive) return;
   var list = C<Panel>("SetupList");
   list.Children.Clear();
   string group = null;
   foreach (var check in setupChecks) {
    if (check.Group != group) {
     group = check.Group;
     var header = SetupText(Lang.T(group).ToUpperInvariant(), "#78939F", 11, false);
     header.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 12, 0, 6);
     list.Children.Add(header);
    }
    list.Children.Add(SetupRow(check));
   }
   bool busy = installing || locating || setupChecking;
   C<Button>("SetupDownload").IsEnabled = !busy && setupChecks.Any(c => c.Action == SetupAction.Download && c.Status != SetupStatus.Ok);
   C<Button>("SetupCancel").Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
   C<Button>("SetupLocate").IsEnabled = !busy;
   C<Button>("SetupRecheck").IsEnabled = !busy;
   var bar = C<ProgressBar>("SetupProgress");
   bar.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
   bar.Value = setupPercent;
   C<TextBlock>("SetupProgressText").Text = setupProgress == null ? "" : setupProgress.Translated();
   C<TextBlock>("SetupNotes").Text = string.Join("\n", setupNotes.Select(n => n.Translated()).Take(8).ToArray());
  }

  // Runs scripts/Install-Components.ps1 and follows its SETUP| lines.
  void RunInstall() {
   if (installing || locating) return;
   installing = true;
   installCancelled = false;
   installFailure = null;
   setupPercent = 0;
   setupNotes.Clear();
   setupProgress = Msg.Say("Starting the download...");
   RenderSetup();
   try {
    installProcess = Scripts.Run(root, "Install-Components.ps1", "-Root \"" + root.TrimEnd('\\') + "\"",
     line => Window.Dispatcher.BeginInvoke(new Action(() => OnInstallLine(line))),
     line => Window.Dispatcher.BeginInvoke(new Action(() => Write(line))),
     code => Window.Dispatcher.BeginInvoke(new Action(() => OnInstallExit(code))));
   }
   catch (Exception error) {
    installing = false;
    setupProgress = null;
    setupNotes.Add(Msg.Say("The download failed: {0}", error.Message));
    RenderSetup();
   }
  }

  void OnInstallLine(string line) {
   var message = SetupMessage.Parse(line);
   if (message == null) { Write(line); return; }
   if (message.Kind == "PROGRESS") {
    setupPercent = message.Percent;
    setupProgress = Msg.Say("Downloading and installing {0}", message.Text);
    RenderSetup();
    return;
   }
   if (message.Kind == "STEP" || message.Kind == "DONE") Write("Setup: " + message.Id + " " + message.State + " " + message.Text);
   if (message.State == "failed" || message.State == "FAILED") installFailure = message.Text;
  }

  void OnInstallExit(int code) {
   installProcess = null;
   installing = false;
   setupProgress = null;
   if (installCancelled) setupNotes.Add(Msg.Say("Download cancelled."));
   else if (code != 0) setupNotes.Add(Msg.Say("The download failed: {0}", installFailure ?? code.ToString()));
   RenderSetup();
   StartSetupCheck();
  }

  // A download in progress stops with everything it started; what was already downloaded stays and is continued next time.
  void StopInstall() {
   var process = installProcess;
   if (process == null) return;
   installCancelled = true;
   try { Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false }); }
   catch (Exception) { }
  }

  // The user's own files: pick a folder (the Cemu folder or any folder above the game, update and DLC), then scan it and build the two Cemu folders around what is found.
  void LocateGame() {
   if (installing || locating || setupChecking) return;
   string folder;
   using (var dialog = new WinForms.FolderBrowserDialog()) {
    dialog.Description = Tr("Select the folder with your game files. A Cemu folder, or a folder with your game, update and DLC.");
    dialog.ShowNewFolderButton = false;
    if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
    folder = dialog.SelectedPath;
   }
   locating = true;
   setupProgress = Msg.Say("Looking through that folder...");
   RenderSetup();
   ThreadPool.QueueUserWorkItem(state => {
    var notes = new List<Msg>();
    try {
     var scan = GameFiles.Scan(folder, SetupManifest.Load(root));
     if (scan.HasGame) notes.AddRange(SetupLayout.Apply(root, scan).Log);
     else notes.AddRange(scan.Notes);
    }
    catch (Exception error) { notes.Add(Msg.Text(error.Message)); }
    Window.Dispatcher.BeginInvoke(new Action(() => {
     locating = false;
     setupProgress = null;
     setupNotes.Clear();
     setupNotes.AddRange(notes);
     RenderSetup();
     StartSetupCheck();
    }));
   });
  }
 }
}
