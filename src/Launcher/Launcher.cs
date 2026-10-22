using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace HyruleOnline {
 // Runs the project's PowerShell scripts without a console window and finds the processes the project owns.
 static class Scripts {
  public static string FindRoot() {
   string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
   while (dir != null) {
    if (File.Exists(Path.Combine(dir, "scripts", "Start-Online.ps1"))) return dir;
    var parent = Directory.GetParent(dir);
    dir = parent == null ? null : parent.FullName;
   }
   return null;
  }

  public static Process Run(string root, string script, string arguments, Action<string> output, Action<string> error, Action<int> exited) {
   var info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "scripts", script) + "\" " + arguments) {
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root
   };
   var process = new Process { StartInfo = info, EnableRaisingEvents = true };
   process.OutputDataReceived += (s, e) => { if (e.Data != null) output(e.Data); };
   process.ErrorDataReceived += (s, e) => { if (e.Data != null) error(e.Data); };
   process.Exited += (s, e) => exited(process.ExitCode);
   process.Start();
   process.BeginOutputReadLine(); process.BeginErrorReadLine();
   return process;
  }

  // Paths of Cemu, launcher and server processes that run from this project.
  public static List<string> Owned(string root) {
   var paths = new List<string>();
   foreach (string name in new[] { "Cemu", "MBL.DedicatedServer", "Milk Bar Launcher" }) {
    foreach (var process in Process.GetProcessesByName(name)) {
     try {
      string path = process.MainModule.FileName;
      if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) paths.Add(path);
     }
     catch (Exception) { }
     finally { process.Dispose(); }
    }
   }
   return paths;
  }
 }

 static class Entry {
  [STAThread] static void Main() {
   bool first;
   using (var instance = new Mutex(true, "Local\\HyruleOnline.Launcher.v1", out first)) {
    if (!first) { MessageBox.Show("Hyrule Online is already open.", "Hyrule Online"); return; }
    foreach (string name in new[] { "InputRouter", "HyruleCoop" }) {
     if (Process.GetProcessesByName(name).Length > 0) { MessageBox.Show(name + " is running and uses the same controller routing. Close it first.", "Hyrule Online"); return; }
    }
    string root = Scripts.FindRoot();
    if (root == null) { MessageBox.Show("Run Hyrule Online from the project's .artifacts\\app folder.", "Hyrule Online"); return; }
    using (var router = new Router()) {
     try { router.Start(); }
     catch (Exception error) { MessageBox.Show("Controller input could not start.\n\n" + error.Message, "Hyrule Online"); return; }
     var app = new Application();
     app.DispatcherUnhandledException += (s, e) => { MessageBox.Show("The action could not be completed.\n\n" + e.Exception.Message, "Hyrule Online"); e.Handled = true; };
     app.Run(new Launcher(root, router).Window);
    }
   }
  }
 }

 sealed class Launcher {
  static readonly Brush Idle = Paint("#4B5A60"), Busy = Paint("#E0BE75"), Good = Paint("#63E0BD"), Bad = Paint("#E06C6C");
  public readonly Window Window;
  readonly string root;
  readonly Router router;
  readonly DispatcherTimer timer = new DispatcherTimer();
  readonly bool[] joined = new bool[2];
  Process script;
  string lastError, failure;
  bool stopping;
  int ticks;

  static Brush Paint(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
  T C<T>(string name) where T : class { return Window.FindName(name) as T; }

  public Launcher(string root, Router router) {
   this.root = root; this.router = router;
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.xaml"))
    Window = (Window)XamlReader.Load(stream);
   router.Message += text => Window.Dispatcher.BeginInvoke(new Action(() => Write(text)));
   C<Button>("Start").Click += (s, e) => StartSession();
   C<Button>("Stop").Click += (s, e) => StopSession();
   C<Button>("Swap").Click += (s, e) => router.Swap();
   for (int i = 0; i < 2; i++) {
    int player = i;
    C<Button>("Assign" + (i + 1)).Click += (s, e) => { if (router.Snapshot().Assigning == player) router.CancelAssign(); else router.BeginAssign(player); };
    C<Button>("Rumble" + (i + 1)).Click += (s, e) => router.TestRumble(player);
   }
   Window.Closing += OnClosing;
   timer.Interval = TimeSpan.FromMilliseconds(33);
   timer.Tick += (s, e) => Tick();
   Window.Loaded += (s, e) => { RefreshProcesses(); timer.Start(); };
  }

  void Write(string text) {
   var log = C<TextBox>("Log");
   log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
   log.ScrollToEnd();
  }

  void Step(string name, string text, Brush color) {
   C<TextBlock>(name + "Text").Text = text;
   C<System.Windows.Shapes.Shape>(name + "Dot").Fill = color;
  }

  void StartSession() {
   failure = null; lastError = null; joined[0] = joined[1] = false;
   C<TextBlock>("Status").Text = "Starting...";
   C<TextBlock>("Prompt").Text = "";
   Step("Server", "Starting", Busy); Step("A", "Waiting", Idle); Step("B", "Waiting", Idle);
   Write("Starting the session.");
   script = Scripts.Run(root, "Start-Online.ps1", "-InputMode Routed -ExternalRouter", Line, Error, code => Window.Dispatcher.BeginInvoke(new Action(() => ScriptExited(code))));
   SetButtons();
  }

  void Line(string text) { Window.Dispatcher.BeginInvoke(new Action(() => Handle(text))); }
  void Error(string text) { Window.Dispatcher.BeginInvoke(new Action(() => { Write(text); if (lastError == null && text.Trim().Length > 0) lastError = text.Trim(); })); }

  void Handle(string text) {
   Write(text);
   var prompt = C<TextBlock>("Prompt");
   Match match;
   if (text.StartsWith("Server started")) Step("Server", "Running", Good);
   else if ((match = Regex.Match(text, "^Player ([AB]): wait")).Success) {
    string name = match.Groups[1].Value;
    Step(name, "Waiting for Continue", Busy);
    prompt.Text = "Cemu " + name + ": wait for the main menu, then press Continue within about a minute.";
   }
   else if ((match = Regex.Match(text, "^Player ([AB]) joined")).Success) {
    string name = match.Groups[1].Value;
    joined[name == "A" ? 0 : 1] = true;
    Step(name, "Joined the server", Good);
    prompt.Text = name == "A" ? "Player 1 joined. Starting player 2..." : "";
   }
   else if (text.StartsWith("Both players are connected")) { prompt.Text = ""; C<TextBlock>("Status").Text = "Session running."; }
  }

  void ScriptExited(int code) {
   script = null;
   if (code != 0) {
    failure = lastError ?? "The start script failed.";
    C<TextBlock>("Status").Text = "Start failed: " + failure;
    C<TextBlock>("Prompt").Text = "Use Stop session to close anything that was started, then try again.";
    Write("Start failed (exit code " + code + ").");
   }
   RefreshProcesses();
  }

  void StopSession() {
   stopping = true; SetButtons();
   C<TextBlock>("Status").Text = "Stopping...";
   C<TextBlock>("Prompt").Text = "";
   StopScript();
   Scripts.Run(root, "Stop-Online.ps1", "", Line, Error, code => Window.Dispatcher.BeginInvoke(new Action(() => {
    stopping = false; failure = null; joined[0] = joined[1] = false; RefreshProcesses();
   })));
  }

  void StopScript() {
   var running = script;
   if (running == null) return;
   try { if (!running.HasExited) running.Kill(); } catch (Exception) { }
  }

  void OnClosing(object sender, System.ComponentModel.CancelEventArgs e) {
   if (script == null && Scripts.Owned(root).Count == 0) return;
   var answer = MessageBox.Show(Window, "A session is still open.\n\nYes: stop the session and close.\nNo: close only the launcher. The game keeps running and the controllers fall back to Windows XInput.\nCancel: keep this window open.", "Hyrule Online", MessageBoxButton.YesNoCancel);
   if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
   if (answer != MessageBoxResult.Yes) return;
   StopScript();
   var done = new ManualResetEvent(false);
   Scripts.Run(root, "Stop-Online.ps1", "", t => { }, t => { }, code => done.Set());
   done.WaitOne(20000);
  }

  void RefreshProcesses() {
   var owned = Scripts.Owned(root);
   bool server = false, a = false, b = false;
   foreach (string path in owned) {
    if (path.EndsWith("MBL.DedicatedServer.exe", StringComparison.OrdinalIgnoreCase)) server = true;
    if (path.IndexOf("\\cemuA\\", StringComparison.OrdinalIgnoreCase) >= 0) a = true;
    if (path.IndexOf("\\cemuB\\", StringComparison.OrdinalIgnoreCase) >= 0) b = true;
   }
   if (!a) joined[0] = false;
   if (!b) joined[1] = false;
   if (script == null && !stopping) {
    Step("Server", server ? "Running" : "Stopped", server ? Good : Idle);
    Step("A", a ? (joined[0] ? "Joined the server" : "Game open") : "Stopped", a ? (joined[0] ? Good : Busy) : Idle);
    Step("B", b ? (joined[1] ? "Joined the server" : "Game open") : "Stopped", b ? (joined[1] ? Good : Busy) : Idle);
    var status = C<TextBlock>("Status");
    if (owned.Count > 0 && failure == null) status.Text = "Session running.";
    else if (owned.Count == 0 && failure == null) { status.Text = "Ready."; C<TextBlock>("Prompt").Text = ""; }
   }
   C<Button>("Start").IsEnabled = script == null && !stopping && owned.Count == 0;
   C<Button>("Stop").IsEnabled = !stopping && (script != null || owned.Count > 0);
  }

  void SetButtons() {
   C<Button>("Start").IsEnabled = false;
   C<Button>("Stop").IsEnabled = !stopping;
  }

  void Tick() {
   var snapshot = router.Snapshot();
   for (int player = 0; player < 2; player++) {
    string key = "P" + (player + 1);
    int slot = snapshot.Routes[player];
    bool present = slot >= 0 && snapshot.Names[slot] != null;
    var pad = present ? snapshot.Pads[slot] : new PadState();
    C<TextBlock>(key + "Name").Text = present ? snapshot.Names[slot] : "No controller detected";
    C<TextBlock>(key + "Status").Text = snapshot.Assigning == player ? "Press the bottom face button on a controller to assign it." : present ? "Connected" : "Waiting for a controller";
    C<Button>("Assign" + (player + 1)).Content = snapshot.Assigning == player ? "Cancel assignment" : "Assign controller";
    Move(C<FrameworkElement>(key + "L"), pad.lx, pad.ly, 20);
    Move(C<FrameworkElement>(key + "R"), pad.rx, pad.ry, 111);
    C<ProgressBar>(key + "LT").Value = pad.lt;
    C<ProgressBar>(key + "RT").Value = pad.rt;
   }
   if (++ticks % 60 == 0) RefreshProcesses();
  }

  static void Move(FrameworkElement stick, short x, short y, double left) {
   Canvas.SetLeft(stick, left + x / 32768.0 * 18);
   Canvas.SetTop(stick, 20 - y / 32768.0 * 18);
  }
 }
}
