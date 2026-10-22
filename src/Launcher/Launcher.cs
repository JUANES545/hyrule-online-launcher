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
  double scale = GraphicsMod.DefaultScale;
  string shadows, shadowDistance, antiAliasing;
  int fpsLimit = FpsMod.AutoFps, displayWidth, displayHeight, displayHz;
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
   C<ComboBox>("ScaleBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is double)) return;
    scale = (double)item.Tag;
    Window.Dispatcher.BeginInvoke(new Action(ChangedDisplayChoice));
   };
   WatchQuality("ShadowsBox", name => shadows = name);
   WatchQuality("ShadowDistanceBox", name => shadowDistance = name);
   WatchQuality("AntiAliasingBox", name => antiAliasing = name);
   C<ComboBox>("FpsBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is int)) return;
    fpsLimit = (int)item.Tag;
    Window.Dispatcher.BeginInvoke(new Action(ChangedDisplayChoice));
   };
   foreach (var tab in new[] { "TabPlay", "TabGraphics", "TabDetails" }) {
    var button = C<Button>(tab);
    button.Click += (s, e) => ShowPage(int.Parse((string)((Button)s).Tag));
   }
   C<Button>("Help").Click += (s, e) => { var help = C<Border>("HelpPanel"); help.Visibility = help.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
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
    if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
     ShowPage(PageNav.Move(page, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1, PageNames.Length));
     e.Handled = true;
     return;
    }
    if (e.Key != Key.Escape) return;
    var help = C<Border>("HelpPanel");
    if (router.Snapshot().Assigning >= 0) router.CancelAssign();
    else if (help.Visibility == Visibility.Visible) help.Visibility = Visibility.Collapsed;
    else Window.Close();
    e.Handled = true;
   };
   Window.Closing += OnClosing;
   Window.SizeChanged += (s, e) => FitShell();
   Window.Loaded += (s, e) => { FitPrimary(); ShowPage(0); UpdatePlan(); RefreshProcesses(); C<Button>("Start").Focus(); timer.Start(); };
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

  // The launcher covers the primary display, like the local co-op launcher. HYRULE_LAUNCHER_SIZE=WIDTHxHEIGHT opens a smaller window for layout checks.
  void FitPrimary() {
   Window.WindowState = WindowState.Normal;
   Window.Left = 0; Window.Top = 0;
   Window.Width = SystemParameters.PrimaryScreenWidth; Window.Height = SystemParameters.PrimaryScreenHeight;
   var parts = (Environment.GetEnvironmentVariable("HYRULE_LAUNCHER_SIZE") ?? "").Split('x');
   int width, height;
   if (parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width >= 200 && height >= 200) { Window.Width = width; Window.Height = height; }
   FitShell();
  }

  // Sizes the design canvas to the window shape and stacks the display column under the players on tall windows.
  void FitShell() {
   if (C<Grid>("Shell") == null) return;
   var fit = ShellFit.For(Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width, Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height);
   var shell = C<Grid>("Shell");
   shell.Width = fit.Width; shell.Height = fit.Height;
   Grid.SetColumn(C<StackPanel>("DisplayPanel"), fit.Stacked ? 0 : 1); Grid.SetRow(C<StackPanel>("DisplayPanel"), fit.Stacked ? 1 : 0);
   C<Grid>("PlayPage").ColumnDefinitions[1].Width = fit.Stacked ? new GridLength(0) : new GridLength(0.9, GridUnitType.Star);
   C<StackPanel>("Players").Margin = fit.Stacked ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 28, 0);
  }

  static readonly string[] PageNames = { "Play", "Graphics", "Details" };
  int page;
  ushort[] previousButtons = new ushort[4];

  void ShowPage(int index) {
   page = PageNav.Move(0, index, PageNames.Length);
   for (int i = 0; i < PageNames.Length; i++) {
    bool current = i == page;
    C<FrameworkElement>(PageNames[i] + "Page").Visibility = current ? Visibility.Visible : Visibility.Collapsed;
    var tab = C<Button>("Tab" + PageNames[i]);
    tab.Background = Paint(current ? "#B03C554B" : "#801B252B");
    tab.BorderBrush = Paint(current ? "#D5C396" : "#364E5659");
   }
  }

  // LB and RB on any controller flip the page, but only while the launcher itself is in front, so they never fight with the games.
  void NavigateWithPads(RouterSnapshot snapshot) {
   int step = 0;
   for (int slot = 0; slot < 4; slot++) {
    ushort buttons = snapshot.Names[slot] != null ? snapshot.Pads[slot].buttons : (ushort)0;
    if (step == 0) step = PageNav.Step(previousButtons[slot], buttons);
    previousButtons[slot] = buttons;
   }
   if (step != 0 && Window.IsActive && !AnyDropDownOpen()) ShowPage(PageNav.Move(page, step, PageNames.Length));
  }

  bool AnyDropDownOpen() {
   foreach (var name in new[] { "FpsBox", "ScaleBox", "ShadowsBox", "ShadowDistanceBox", "AntiAliasingBox" }) if (C<ComboBox>(name).IsDropDownOpen) return true;
   return false;
  }

  static string PrefsFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.xml"); } }

  void ReadPrefs() {
   try {
    if (!File.Exists(PrefsFile)) return;
    var root = XDocument.Load(PrefsFile).Root;
    mode = ScreenLayout.Parse((string)root.Element("Layout"));
    double saved;
    // Older preferences only knew a balanced or sharper switch.
    scale = double.TryParse((string)root.Element("RenderScale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out saved) ? GraphicsMod.NearestScale(saved)
     : (string)root.Element("Quality") == "Sharp" ? 1.5 : GraphicsMod.DefaultScale;
    shadows = Saved(root, "Shadows"); shadowDistance = Saved(root, "ShadowDistance"); antiAliasing = Saved(root, "AntiAliasing");
    int wanted;
    fpsLimit = int.TryParse((string)root.Element("FpsLimit"), out wanted) && wanted > 0 ? wanted : FpsMod.AutoFps;
   }
   catch (Exception) { mode = LayoutMode.Auto; scale = GraphicsMod.DefaultScale; shadows = shadowDistance = antiAliasing = null; fpsLimit = FpsMod.AutoFps; }
  }

  static string Saved(XElement root, string name) { string value = (string)root.Element(name); return string.IsNullOrEmpty(value) ? null : value; }

  void SavePrefs() {
   try {
    new XDocument(new XElement("Preferences", new XElement("Layout", mode), new XElement("RenderScale", GraphicsMod.NearestScale(scale).ToString(System.Globalization.CultureInfo.InvariantCulture)),
     new XElement("Shadows", shadows ?? ""), new XElement("ShadowDistance", shadowDistance ?? ""), new XElement("AntiAliasing", antiAliasing ?? ""),
     new XElement("FpsLimit", fpsLimit == FpsMod.AutoFps ? "Auto" : fpsLimit.ToString()))).Save(PrefsFile);
   }
   catch (Exception error) { Write("Could not save the display settings: " + error.Message); }
  }

  void ChangedDisplayChoice() {
   SavePrefs();
   placed.Clear();
   UpdatePlan();
   PlaceWindows();
  }

  // The offered frame rates are the FPS++ presets that the primary display can actually show.
  List<FpsMod.Limit> FpsChoices() {
   return FpsMod.Available(FpsMod.Read(Path.Combine(root, ".tools", "instances", "cemuA")), displayHz);
  }

  FpsMod.Limit CurrentFps() { return FpsMod.Pick(FpsChoices(), fpsLimit, displayHz); }

  bool filling;

  void WatchQuality(string box, Action<string> set) {
   C<ComboBox>(box).SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is string)) return;
    set((string)item.Tag);
    SavePrefs();
   };
  }

  string InstanceFolder { get { return Path.Combine(root, ".tools", "instances", "cemuA"); } }

  void ShowQuality() {
   filling = true;
   try {
    var scaleBox = C<ComboBox>("ScaleBox");
    scaleBox.Items.Clear();
    foreach (double option in GraphicsMod.Scales) scaleBox.Items.Add(new ComboBoxItem { Content = GraphicsMod.ScaleLabel(option), Tag = option });
    scaleBox.SelectedIndex = Array.IndexOf(GraphicsMod.Scales, GraphicsMod.NearestScale(scale));
    FillQuality("ShadowsBox", GraphicsMod.ShadowsCategory, shadows);
    FillQuality("ShadowDistanceBox", GraphicsMod.ShadowDistanceCategory, shadowDistance);
    FillQuality("AntiAliasingBox", GraphicsMod.AntiAliasingCategory, antiAliasing);
   }
   finally { filling = false; }
  }

  void FillQuality(string name, string category, string wanted) {
   var choices = GraphicsMod.Read(InstanceFolder, category);
   var box = C<ComboBox>(name);
   box.Items.Clear();
   box.IsEnabled = choices.Count > 0;
   if (choices.Count == 0) { box.Items.Add(new ComboBoxItem { Content = "Not available" }); box.SelectedIndex = 0; return; }
   var current = GraphicsMod.Pick(choices, wanted);
   foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = choice.Label, ToolTip = choice.Name, Tag = choice.Name });
   box.SelectedIndex = choices.IndexOf(current);
  }

  GraphicsMod.Quality CurrentQuality() {
   Func<string, string, string> pick = (category, wanted) => { var choice = GraphicsMod.Pick(GraphicsMod.Read(InstanceFolder, category), wanted); return choice == null ? null : choice.Name; };
   return new GraphicsMod.Quality {
    Scale = GraphicsMod.NearestScale(scale),
    Shadows = pick(GraphicsMod.ShadowsCategory, shadows),
    ShadowDistance = pick(GraphicsMod.ShadowDistanceCategory, shadowDistance),
    AntiAliasing = pick(GraphicsMod.AntiAliasingCategory, antiAliasing)
   };
  }

  void ShowFpsChoices() {
   var choices = FpsChoices();
   var current = FpsMod.Pick(choices, fpsLimit, displayHz);
   var box = C<ComboBox>("FpsBox");
   filling = true;
   try {
    box.Items.Clear();
    box.IsEnabled = choices.Count > 0;
    if (current == null) { box.Items.Add(new ComboBoxItem { Content = "FPS++ is not installed" }); box.SelectedIndex = 0; return; }
    box.Items.Add(new ComboBoxItem { Content = "FPS target: Auto · " + FpsMod.Best(choices, displayHz).Fps + " FPS", Tag = FpsMod.AutoFps });
    foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = choice.Fps + " FPS", Tag = choice.Fps });
    box.SelectedIndex = fpsLimit == FpsMod.AutoFps ? 0 : choices.FindIndex(l => l.Fps == current.Fps) + 1;
   }
   finally { filling = false; }
  }
  void UpdatePlan() {
   WindowPlacer.PrimaryDisplay(out displayWidth, out displayHeight, out displayHz);
   C<TextBlock>("ScreenInfo").Text = displayWidth + " × " + displayHeight + "  /  " + displayHz + " Hz";
   C<Button>("Layout").Content = "Split: " + (mode == LayoutMode.Auto ? "automatic" : mode == LayoutMode.SideBySide ? "side by side" : "top and bottom");
   ShowFpsChoices();
   ShowQuality();
   if (displayWidth < 320 || displayHeight < 240) { C<TextBlock>("LayoutInfo").Text = "The primary display is not ready."; return; }
   var plan = ScreenLayout.Plan(mode, scale, displayWidth, displayHeight);
   bool side = plan.Layout == LayoutMode.SideBySide;
   var first = plan.Players[0];
   C<TextBlock>("LayoutInfo").Text = (side ? "Side by side · P1 left / P2 right" : "Top and bottom · P1 top / P2 bottom") + Environment.NewLine + "Each game renders " + first.RenderWidth + " × " + first.RenderHeight + " " + (CurrentFps() == null ? "" : "at " + CurrentFps().Fps + " FPS");
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
   var areas = ScreenLayout.Plan(mode, scale, displayWidth, displayHeight).Players;
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
    var plan = ScreenLayout.Plan(mode, scale, displayWidth, displayHeight);
    string[] names = { "cemuA", "cemuB" };
    var quality = CurrentQuality();
    Write("Graphics: render " + GraphicsMod.ScaleLabel(quality.Scale) + ", shadows " + quality.Shadows + ", shadow distance " + quality.ShadowDistance + ", anti-aliasing " + quality.AntiAliasing + ".");
    for (int i = 0; i < 2; i++) {
     var view = plan.Players[i];
     var fps = CurrentFps();
     CemuDisplay.Apply(Path.Combine(root, ".tools", "instances", names[i]), view, fps == null ? null : fps.Preset, quality);
     Write("Player " + (i + 1) + ": " + view.RenderWidth + " x " + view.RenderHeight + " (aspect " + view.RatioWidth + ":" + view.RatioHeight + ")" + (fps == null ? "." : ", " + fps.Fps + " FPS limit."));
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
   NavigateWithPads(snapshot);
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
    DrawMotion(key, present ? snapshot.Motion[slot] : new MotionReading());
   }
   if (++ticks % 60 == 0) RefreshProcesses();
  }

  // The glyph shows a pad that has motion sensors: the body rolls with the gyro and tilt, the dot follows the acceleration.
  void DrawMotion(string key, MotionReading reading) {
   bool active = reading.Status == MotionStatus.Active;
   var accent = (Brush)Window.Resources[key + "MotionAccent"];
   var device = C<Image>(key + "MotionDevice"); var ring = C<System.Windows.Shapes.Ellipse>(key + "MotionRing"); var dot = C<System.Windows.Shapes.Ellipse>(key + "MotionDot");
   C<Canvas>(key + "Motion").Opacity = active ? 1 : reading.Status == MotionStatus.Unavailable ? .35 : .6;
   System.Windows.Automation.AutomationProperties.SetItemStatus(device, reading.Status.ToString());
   C<System.Windows.Shapes.Path>(key + "MotionSlash").Visibility = reading.Status == MotionStatus.Unavailable ? Visibility.Visible : Visibility.Collapsed;
   ring.Stroke = active ? accent : (Brush)Window.Resources["MotionMuted"];
   ring.StrokeDashArray = reading.AccelFresh && reading.GyroFresh ? null : (DoubleCollection)Window.Resources["MotionDash"];
   device.Source = (ImageSource)Window.Resources[active ? key + "MotionDeviceImage" : "MotionDeviceImage"];
   ((RotateTransform)device.RenderTransform).Angle = active ? reading.Angle + reading.TiltX * 15 : 0;
   dot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
   dot.Width = dot.Height = 8 + (active ? reading.TiltZ * 2 : 0);
   dot.Opacity = .55 + reading.Activity * .45;
   Canvas.SetLeft(dot, 32 + reading.TiltX * 10 - dot.Width / 2); Canvas.SetTop(dot, 30 - reading.TiltY * 6 - dot.Height / 2);
  }

  static void Move(FrameworkElement stick, short x, short y, double left) {
   Canvas.SetLeft(stick, left + x / 32768.0 * 18);
   Canvas.SetTop(stick, 20 - y / 32768.0 * 18);
  }
 }
}
