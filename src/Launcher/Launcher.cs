using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

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
  readonly ServerHost serverHost = new ServerHost();
  readonly DispatcherTimer timer = new DispatcherTimer();
  readonly bool[] joined = new bool[2];
  readonly HashSet<IntPtr> placed = new HashSet<IntPtr>(), menuHidden = new HashSet<IntPtr>();
  LayoutMode mode = LayoutMode.Auto;
  bool sharp;
  int fpsLimit = 60, displayWidth, displayHeight, displayHz;
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
   LoadFonts();
   ReadPrefs();
   router.Message += text => Window.Dispatcher.BeginInvoke(new Action(() => Write(text)));
   C<Button>("Start").Click += (s, e) => StartSession();
   C<Button>("Stop").Click += (s, e) => StopSession();
   C<Button>("Swap").Click += (s, e) => router.Swap();
   for (int i = 0; i < 2; i++) {
    int player = i;
    C<Button>("Assign" + (i + 1)).Click += (s, e) => { if (router.Snapshot().Assigning == player) router.CancelAssign(); else router.BeginAssign(player); };
    C<Button>("Rumble" + (i + 1)).Click += (s, e) => router.TestRumble(player);
   }
   C<Button>("Layout").Click += (s, e) => { mode = mode == LayoutMode.Auto ? LayoutMode.SideBySide : mode == LayoutMode.SideBySide ? LayoutMode.TopBottom : LayoutMode.Auto; ChangedDisplayChoice(); };
   C<Button>("Quality").Click += (s, e) => { sharp = !sharp; ChangedDisplayChoice(); };
   C<Button>("FpsLimit").Click += (s, e) => { fpsLimit = fpsLimit == 60 ? 90 : 60; ChangedDisplayChoice(); };
   C<Button>("Details").Click += (s, e) => {
    var panel = C<Border>("DetailsPanel");
    panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    if (panel.Visibility == Visibility.Visible) C<ScrollViewer>("ContentScroll").ScrollToBottom(); else C<ScrollViewer>("ContentScroll").ScrollToTop();
   };
   C<Button>("Help").Click += (s, e) => { var help = C<Border>("HelpPanel"); help.Visibility = help.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; C<ScrollViewer>("ContentScroll").ScrollToTop(); };
   C<Button>("Exit").Click += (s, e) => Window.Close();
   serverHost.Line += line => Window.Dispatcher.BeginInvoke(new Action(() => {
    var log = C<TextBox>("ServerLog");
    log.AppendText(line + Environment.NewLine);
    log.ScrollToEnd();
   }));
   C<TextBox>("ServerCommand").KeyDown += (s, e) => {
    if (e.Key != Key.Enter) return;
    var box = (TextBox)s;
    if (serverHost.Send(box.Text)) box.Clear(); else Write("The server is not running, so the command was not sent.");
    e.Handled = true;
   };
   Window.PreviewKeyDown += (s, e) => {
    if (e.Key != Key.Escape) return;
    var help = C<Border>("HelpPanel");
    if (router.Snapshot().Assigning >= 0) router.CancelAssign();
    else if (help.Visibility == Visibility.Visible) help.Visibility = Visibility.Collapsed;
    else Window.Close();
    e.Handled = true;
   };
   Window.Closing += OnClosing;
   Window.Loaded += (s, e) => { FitPrimary(); UpdatePlan(); RefreshProcesses(); C<Button>("Start").Focus(); timer.Start(); };
   timer.Interval = TimeSpan.FromMilliseconds(33);
   timer.Tick += (s, e) => Tick();
  }

  void LoadFonts() {
   var directory = new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar);
   if (!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts"))) return;
   Window.Resources["MenuFont"] = new FontFamily(directory, "./#Roboto");
   Window.Resources["TitleFont"] = new FontFamily(directory, "./#Cinzel");
   Window.Resources["LogoFont"] = new FontFamily(directory, "./#Cinzel Decorative");
  }

  // The launcher covers the primary display, like the local co-op launcher, and switches to a stacked layout on tall displays.
  void FitPrimary() {
   Window.WindowState = WindowState.Normal;
   Window.Left = 0; Window.Top = 0;
   Window.Width = SystemParameters.PrimaryScreenWidth; Window.Height = SystemParameters.PrimaryScreenHeight;
   bool narrow = (double)displayWidth / Math.Max(1, displayHeight) < 1.1;
   var shell = C<Grid>("Shell");
   shell.Width = narrow ? 720 : 1280; shell.Height = narrow ? 1840 : 1100;
   Grid.SetColumn(C<StackPanel>("DisplayPanel"), narrow ? 0 : 1); Grid.SetRow(C<StackPanel>("DisplayPanel"), narrow ? 1 : 0);
   C<Grid>("MainGrid").ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(0.9, GridUnitType.Star);
   C<StackPanel>("Players").Margin = narrow ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 28, 0);
  }

  static string PrefsFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.xml"); } }

  void ReadPrefs() {
   try {
    if (!File.Exists(PrefsFile)) return;
    var root = XDocument.Load(PrefsFile).Root;
    mode = ScreenLayout.Parse((string)root.Element("Layout"));
    sharp = (string)root.Element("Quality") == "Sharp";
    fpsLimit = (string)root.Element("FpsLimit") == "90" ? 90 : 60;
   }
   catch (Exception) { mode = LayoutMode.Auto; sharp = false; fpsLimit = 60; }
  }

  void SavePrefs() {
   try {
    new XDocument(new XElement("Preferences", new XElement("Layout", mode), new XElement("Quality", sharp ? "Sharp" : "Balanced"), new XElement("FpsLimit", fpsLimit))).Save(PrefsFile);
   }
   catch (Exception error) { Write("Could not save the display settings: " + error.Message); }
  }

  void ChangedDisplayChoice() {
   SavePrefs();
   placed.Clear();
   UpdatePlan();
   PlaceWindows();
  }

  void UpdatePlan() {
   WindowPlacer.PrimaryDisplay(out displayWidth, out displayHeight, out displayHz);
   C<TextBlock>("ScreenInfo").Text = displayWidth + " × " + displayHeight + "  /  " + displayHz + " Hz";
   C<Button>("Layout").Content = "Split: " + (mode == LayoutMode.Auto ? "automatic" : mode == LayoutMode.SideBySide ? "side by side" : "top and bottom");
   C<Button>("Quality").Content = "Quality: " + (sharp ? "sharper" : "balanced");
   C<Button>("FpsLimit").Content = "FPS target: " + fpsLimit;
   if (displayWidth < 320 || displayHeight < 240) { C<TextBlock>("LayoutInfo").Text = "The primary display is not ready."; return; }
   var plan = ScreenLayout.Plan(mode, sharp, displayWidth, displayHeight);
   bool side = plan.Layout == LayoutMode.SideBySide;
   var first = plan.Players[0];
   C<TextBlock>("LayoutInfo").Text = (side ? "Side by side · P1 left / P2 right" : "Top and bottom · P1 top / P2 bottom") + Environment.NewLine + "Each game renders " + first.RenderWidth + " × " + first.RenderHeight + " at " + ScreenLayout.FrameRate(displayHz, fpsLimit) + " FPS";
   var frame = C<Border>("PreviewFrame");
   double ratio = (double)displayWidth / displayHeight;
   frame.Width = ratio < 1 ? 118 : Math.Min(360, 210 * ratio); frame.Height = frame.Width / ratio;
   var a = C<Border>("View1"); var b = C<Border>("View2");
   Grid.SetRow(a, 0); Grid.SetColumn(a, 0); Grid.SetRow(b, side ? 0 : 1); Grid.SetColumn(b, side ? 1 : 0);
   Grid.SetRowSpan(a, side ? 2 : 1); Grid.SetRowSpan(b, side ? 2 : 1); Grid.SetColumnSpan(a, side ? 1 : 2); Grid.SetColumnSpan(b, side ? 1 : 2);
   FitPrimary();
  }

  // Each Cemu window is placed once when it first appears, so moving it by hand is not undone.
  void PlaceWindows() {
   if (displayWidth < 320 || displayHeight < 240) return;
   var areas = ScreenLayout.Plan(mode, sharp, displayWidth, displayHeight).Players;
   string[] folders = { "\\cemuA\\", "\\cemuB\\" };
   for (int i = 0; i < 2; i++) {
    foreach (var window in WindowPlacer.CemuWindows(folders[i]).Values) {
     if (!placed.Add(window)) {
      if (WindowPlacer.HideMenu(window) && menuHidden.Add(window)) Write("Cemu " + (i == 0 ? "A" : "B") + ": hid the menu bar that Cemu restored.");
      continue;
     }
     int clientWidth, clientHeight;
     var area = areas[i].Area;
     WindowPlacer.Place(window, area, out clientWidth, out clientHeight);
     Write("Cemu " + (i == 0 ? "A" : "B") + " placed at " + area.X + "," + area.Y + " (" + area.Width + " x " + area.Height + "); the game area is " + clientWidth + " x " + clientHeight + ".");
    }
   }
  }

  // Each instance's settings are written for its own window before Cemu starts, so the game renders in the window's shape.
  bool PrepareDisplay() {
   try {
    WindowPlacer.PrimaryDisplay(out displayWidth, out displayHeight, out displayHz);
    var plan = ScreenLayout.Plan(mode, sharp, displayWidth, displayHeight);
    string[] names = { "cemuA", "cemuB" };
    for (int i = 0; i < 2; i++) {
     var view = plan.Players[i];
     CemuDisplay.Apply(Path.Combine(root, ".tools", "instances", names[i]), view);
     Write("Player " + (i + 1) + ": " + view.RenderWidth + " x " + view.RenderHeight + " (aspect " + view.RatioWidth + ":" + view.RatioHeight + ").");
    }
    return true;
   }
   catch (Exception error) {
    failure = "The display settings could not be prepared: " + error.Message;
    C<TextBlock>("Status").Text = failure;
    Write(failure);
    return false;
   }
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
   if (Scripts.Owned(root).Count == 0 && !PrepareDisplay()) return;
   C<TextBlock>("Status").Text = "Starting...";
   C<TextBlock>("Prompt").Text = "";
   Step("Server", "Starting", Busy); Step("A", "Waiting", Idle); Step("B", "Waiting", Idle);
   Write("Starting the session.");
   try {
    serverHost.Start(Path.Combine(root, ".artifacts", "extract", "milkbar", "DedicatedServer"));
   }
   catch (Exception error) {
    failure = "The dedicated server could not start: " + error.Message;
    C<TextBlock>("Status").Text = failure;
    Write(failure);
    Step("Server", "Stopped", Bad);
    return;
   }
   script = Scripts.Run(root, "Start-Online.ps1", "-InputMode Routed -ExternalRouter -ExternalServer", Line, Error, code => Window.Dispatcher.BeginInvoke(new Action(() => ScriptExited(code))));
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
    prompt.Text = name == "A" ? "Cemu A: wait for the main menu, then press Continue within about a minute. Player 2 starts when player 1 has finished loading." : "Cemu B: wait for the main menu, then press Continue. If player 1 is not in yet, press Continue there too.";
   }
   else if ((match = Regex.Match(text, "^(\\d) of 2 players joined")).Success) {
    // The shared log does not say which player joined, so the steps change only when both are in.
    if (match.Groups[1].Value == "1") prompt.Text = "One player joined. Press Continue in the other Cemu window.";
   }
   else if (text.StartsWith("Both players are connected")) {
    joined[0] = joined[1] = true;
    Step("A", "Joined the server", Good); Step("B", "Joined the server", Good);
    prompt.Text = ""; C<TextBlock>("Status").Text = "Session running.";
   }
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
    stopping = false; failure = null; joined[0] = joined[1] = false; placed.Clear(); RefreshProcesses();
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
   int width, height, hz;
   WindowPlacer.PrimaryDisplay(out width, out height, out hz);
   if (width != displayWidth || height != displayHeight || hz != displayHz) UpdatePlan();
   PlaceWindows();
   var owned = Scripts.Owned(root);
   bool server = false, a = false, b = false;
   foreach (string path in owned) {
    if (path.EndsWith("MBL.DedicatedServer.exe", StringComparison.OrdinalIgnoreCase)) server = true;
    if (path.IndexOf("\\cemuA\\", StringComparison.OrdinalIgnoreCase) >= 0) a = true;
    if (path.IndexOf("\\cemuB\\", StringComparison.OrdinalIgnoreCase) >= 0) b = true;
   }
   if (!a) joined[0] = false;
   if (!b) joined[1] = false;
   C<System.Windows.Shapes.Shape>("CornerDot").Fill = server ? Good : Idle;
   C<TextBlock>("CornerServer").Text = server ? "Server running" : "Server stopped";
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
