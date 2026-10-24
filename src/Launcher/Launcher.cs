using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
      if (path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
     }
     catch (Exception) { }
     finally { process.Dispose(); }
    }
   }
   return paths;
  }
 }

 static class Entry {
  // Long or unsafe folders are caught before anything starts; only the hard failures stop the launcher.
  static bool RootAllowed(string root) {
   var issues = PathGuard.Check(root);
   if (issues.Count == 0) return true;
   Lang.Current = Prefs.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.xml")).LauncherLanguage;
   string text = PathGuard.Describe(root, issues);
   if (issues.Any(PathGuard.Blocks)) { MessageBox.Show(text, "Hyrule Online", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
   return MessageBox.Show(text, "Hyrule Online", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
  }

  [STAThread] static void Main() {
   bool first;
   // Nothing is loaded yet, so these messages follow the Windows display language.
   Lang.Current = Lang.Ui(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
   using (var instance = new Mutex(true, "Local\\HyruleOnline.Launcher.v1", out first)) {
    if (!first) { MessageBox.Show(Lang.T("Hyrule Online is already open."), "Hyrule Online"); return; }
    foreach (string name in new[] { "InputRouter", "HyruleCoop" }) {
     if (Process.GetProcessesByName(name).Length > 0) { MessageBox.Show(Lang.T("{0} is running and uses the same controller routing. Close it first.", name), "Hyrule Online"); return; }
    }
    string root = Scripts.FindRoot();
    if (root == null) { MessageBox.Show(Lang.T("Run Hyrule Online from the project's .artifacts\\app folder."), "Hyrule Online"); return; }
    if (!RootAllowed(root)) return;
    using (var router = new Router()) {
     try { router.Start(); }
     catch (Exception error) { MessageBox.Show(Lang.T("Controller input could not start.\n\n{0}", error.Message), "Hyrule Online"); return; }
     var app = new Application();
     app.DispatcherUnhandledException += (s, e) => { MessageBox.Show(Lang.T("The action could not be completed.\n\n{0}", e.Exception.Message), "Hyrule Online"); e.Handled = true; };
     app.Run(new Launcher(root, router).Window);
    }
   }
  }
 }

 sealed partial class Launcher {
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
  GraphicsMod.Advanced advanced = new GraphicsMod.Advanced();
  readonly bool vulkanAvailable = GraphicsMod.VulkanAvailable();
  readonly HardwareInfo hardware = Hardware.Detect();
  readonly string[] names = (string[])Characters.DefaultNames.Clone();
  readonly string[] models = { "", "" };
  ServerRules rules = new ServerRules();
  List<ServerRules> presets = new List<ServerRules>();
  static readonly string[] RuleBoxes = { "RuleEnemies", "RuleQuests", "RuleKoroks", "RuleTowers", "RuleShrines", "RuleLocations", "RuleDivineBeasts" };
  static readonly string[] LiveControls = { "SetTime", "SetWeather", "WeatherBox", "TimeBox", "LiveNameTags", "PauseSync", "ResumeSync", "BringP1", "BringP2" };
  List<Character> characters = Characters.Parse(null);
  int fpsLimit = FpsMod.AutoFps, displayWidth, displayHeight, displayHz;
  // One display per player: the arrangement, and what each player has (display, render scale, FPS target).
  bool perDisplay, canSeparate;
 bool sharedShaders;
 string shaderProblem;
  // The menu shortcut ("Off" or a MenuShortcut string), whether a game is open, and the state of the capture that records a new shortcut.
  string shortcutText = MenuShortcut.Default;
  bool gameRunning, capturing, captureArmed;
  int lastChordSeq = -1, captureSlot = -1, shortcutLayout = PadKind.Nintendo;
  long captureStarted;
  ushort captureButtons;
  bool captureLt, captureRt;
  readonly TapGuard[] startGuards = { new TapGuard(), new TapGuard(), new TapGuard(), new TapGuard() };
  readonly TapGuard[] selectGuards = { new TapGuard(), new TapGuard(), new TapGuard(), new TapGuard() };
  int playerCount = 2;
  // The controller routes from before a controller chose single player, put back when multiplayer returns.
  int[] routesBeforeSingle;
  // The controller whose press is being handled, so choosing single player can make it player 1 (-1 for the mouse).
  int actorSlot = -1;
  readonly string[] displays = { null, null };
  readonly double[] scales = { GraphicsMod.DefaultScale, GraphicsMod.DefaultScale };
  readonly int[] rotations = { 0, 0 };
  readonly int[] fpsLimits = { FpsMod.AutoFps, FpsMod.AutoFps };
  List<MonitorInfo> monitors = new List<MonitorInfo>();
  MonitorInfo[] pair;
  string monitorKey = "";
  Process script;
  string lastError, failure;
  bool stopping;
  int ticks;
  bool sessionLive, restartAfterStop, restartPending;
  string appliedSignature;
  // The plan the running games were started with. Windows and ReShade follow it until the session restarts, because the game resolution cannot change while it runs.
  DisplayPlan sessionPlan;
  const string RestartPrompt = "Some changes only apply after a restart. Press Restart session to apply them.";
  string language = "en", launcherLanguage = "en";
  List<GameLanguage> gameLanguages = new List<GameLanguage> { GameLanguages.English };
  readonly Translator translator = new Translator();
  readonly Dictionary<string, KeyValuePair<string, Brush>> steps = new Dictionary<string, KeyValuePair<string, Brush>>();
  string statusText = "Ready.", promptText = "";
  object[] statusArgs, promptArgs;

  static Brush Paint(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
  static string Tr(string english, params object[] args) { return Lang.T(english, args); }
  T C<T>(string name) where T : class { return Window.FindName(name) as T; }

  public Launcher(string root, Router router) {
   this.root = root; this.router = router;
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.xaml"))
    Window = (Window)XamlReader.Load(stream);
   LoadFonts();
   translator.Capture(Window);
   ReadPrefs();
   characters = Characters.Read(Path.Combine(root, ".artifacts", "extract", "milkbar", "Milk Bar Launcher.dll"));
   bustData = Characters.ReadBusts(Path.Combine(root, ".artifacts", "extract", "milkbar", "Milk Bar Launcher.dll"));
   gameLanguages = GameLanguages.Scan(GameLanguages.ContentFolder(Path.Combine(root, ".tools", "localappdata", "bcml", "settings.json")));
   language = GameLanguages.Find(gameLanguages, language).Code;
   LoadRules();
   for (int i = 0; i < 2; i++) WatchPlayer(i);
   WatchWorld();
   ApplyLanguage();
   router.Message += text => Window.Dispatcher.BeginInvoke(new Action(() => Write(text)));
   C<Button>("Start").Click += (s, e) => PressStart();
   C<Button>("Stop").Click += (s, e) => StopSession();
   C<Button>("Swap").Click += (s, e) => router.Swap();
   C<Button>("Mode").Click += (s, e) => TogglePlayerCount();
   for (int i = 0; i < 2; i++) {
    int turned = i;
    C<Button>("View" + (i + 1)).Click += (s, e) => TurnScreen(turned);
   }
   C<Button>("ResetTurns").Click += (s, e) => { rotations[0] = 0; rotations[1] = 0; ChangedDisplayChoice(); };
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
   WatchAdvanced();
   C<Button>("UseRecommendedGraphics").Click += (s, e) => UseRecommendedGraphics();
   C<Button>("UseRecommendedRules").Click += (s, e) => UseRecommendedRules();
   WireHints("GraphicsPage", "GraphicsHint");
   WireHints("MultiplayerPage", "MultiplayerHint");
   BuildCursors();
   LoadPadScript();
   C<ComboBox>("FpsBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is int)) return;
    fpsLimit = (int)item.Tag;
    Window.Dispatcher.BeginInvoke(new Action(ChangedDisplayChoice));
   };
   C<Button>("Arrange").Click += (s, e) => { perDisplay = !perDisplay; ChangedDisplayChoice(); };
   for (int i = 0; i < 2; i++) {
    int player = i;
    C<ComboBox>("Display" + (i + 1) + "Box").SelectionChanged += (s, e) => {
     var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
     if (filling || item == null || !(item.Tag is string)) return;
     var chosen = ScreenLayout.Assign(monitors, displays[0], displays[1], player, (string)item.Tag);
     displays[0] = chosen[0]; displays[1] = chosen[1];
     Window.Dispatcher.BeginInvoke(new Action(ChangedDisplayChoice));
    };
    C<ComboBox>("Scale" + (i + 1) + "Box").SelectionChanged += (s, e) => {
     var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
     if (filling || item == null || !(item.Tag is double)) return;
     scales[player] = (double)item.Tag;
     Window.Dispatcher.BeginInvoke(new Action(ChangedDisplayChoice));
    };
    C<ComboBox>("Fps" + (i + 1) + "Box").SelectionChanged += (s, e) => {
     var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
     if (filling || item == null || !(item.Tag is int)) return;
     fpsLimits[player] = (int)item.Tag;
     Window.Dispatcher.BeginInvoke(new Action(ChangedDisplayChoice));
    };
   }
   foreach (var tab in new[] { "TabPlay", "TabGraphics", "TabMultiplayer", "TabLanguage", "TabDetails" }) {
    var button = C<Button>(tab);
    button.Click += (s, e) => ShowPage(int.Parse((string)((Button)s).Tag));
   }
   C<Button>("Help").Click += (s, e) => { var help = C<Border>("HelpPanel"); help.Visibility = help.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
   C<Button>("Exit").Click += (s, e) => Window.Close();
   serverHost.Line += line => Window.Dispatcher.BeginInvoke(new Action(() => {
    var log = C<TextBox>("ServerLog");
    log.AppendText(line + Environment.NewLine);
    log.ScrollToEnd();
    bool? tags = ServerCommands.NameTags(line);
    if (tags.HasValue) ShowNameTags(tags.Value);
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
    else if (ClosePickers()) { }
    else if (help.Visibility == Visibility.Visible) help.Visibility = Visibility.Collapsed;
    else if (GameOpen()) Window.WindowState = WindowState.Minimized;
    else Window.Close();
    e.Handled = true;
   };
   WireSetup();
   Window.Closing += OnClosing;
   Window.SizeChanged += (s, e) => FitShell();
   Window.Loaded += (s, e) => { FitPrimary(); ShowPage(0); UpdatePlan(); RefreshProcesses(); C<Button>("Start").Focus(); timer.Start(); StartSetupCheck(); };
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
   stackedLayout = fit.Stacked;
   UpdatePlayLayout();
   C<Grid>("MultiplayerPage").ColumnDefinitions[1].Width = fit.Stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
   Grid.SetColumn(C<StackPanel>("LivePanel"), fit.Stacked ? 0 : 1); Grid.SetRow(C<StackPanel>("LivePanel"), fit.Stacked ? 1 : 0);
   C<StackPanel>("RulesPanel").Margin = fit.Stacked ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 14, 0);
   C<StackPanel>("LivePanel").Margin = fit.Stacked ? new Thickness(0) : new Thickness(14, 0, 0, 0);
  }

  static readonly string[] PageNames = { "Play", "Graphics", "Multiplayer", "Language", "Details" };
  int page;
  ushort[] previousButtons = new ushort[4];

  void ShowPage(int index) {
   if (setupActive) return;
   ClosePickers();
   ResetCursors();
   page = PageNav.Move(0, index, PageNames.Length);
   for (int i = 0; i < PageNames.Length; i++) {
    bool current = i == page;
    C<FrameworkElement>(PageNames[i] + "Page").Visibility = current ? Visibility.Visible : Visibility.Collapsed;
    var tab = C<Button>("Tab" + PageNames[i]);
    tab.Background = Paint(current ? "#B03C554B" : "#801B252B");
    tab.BorderBrush = Paint(current ? "#D5C396" : "#364E5659");
   }
   Window.UpdateLayout();
  }

  void PressStart() {
   if (setupActive || seeding) return;
   if (sessionLive) { restartAfterStop = true; StopSession(); } else StartSession();
  }

  // LB and RB on any controller flip the page, but only while the launcher itself is in front, so they never fight with the games.
  // Start (or its equivalent) presses Start session; Select flips between 1 and 2 players on the Play page.
  void NavigateWithPads(RouterSnapshot snapshot) {
   int step = 0;
   bool start = false, select = false;
   int selectSlot = -1;
   var shortcut = Router.Shortcut;
   for (int slot = 0; slot < 4; slot++) {
    bool present = snapshot.Names[slot] != null;
    ushort buttons = present ? snapshot.Pads[slot].buttons : (ushort)0;
    byte lt = present ? snapshot.Pads[slot].lt : (byte)0, rt = present ? snapshot.Pads[slot].rt : (byte)0;
    if (step == 0 && !snapshot.Engaged[slot]) step = PageNav.Step(previousButtons[slot], buttons);
    start |= startGuards[slot].Update(buttons, lt, rt, PadKind.StartButton, shortcut != null && (shortcut.Buttons & PadKind.StartButton) != 0);
    if (selectGuards[slot].Update(buttons, lt, rt, PadKind.SelectButton, shortcut != null && (shortcut.Buttons & PadKind.SelectButton) != 0)) { select = true; if (selectSlot < 0) selectSlot = slot; }
    previousButtons[slot] = buttons;
   }
   if (capturing || !Window.IsActive || AnyDropDownOpen() || HelpOpen || setupActive) return;
   if (step != 0) ShowPage(PageNav.Move(page, step, PageNames.Length));
   else if (start && C<Button>("Start").IsEnabled) PressStart();
   else if (select && page == 0 && C<Button>("Mode").IsEnabled) SetPlayerCount(playerCount == 1 ? 2 : 1, selectSlot);
  }

  bool AnyDropDownOpen() {
   foreach (var name in new[] { "FpsBox", "ScaleBox", "ShadowsBox", "ShadowDistanceBox", "AntiAliasingBox", "RulesBox", "SpecialBox", "WeatherBox", "Display1Box", "Display2Box", "Scale1Box", "Scale2Box", "Fps1Box", "Fps2Box", "ApiBox", "UpscaleBox", "DownscaleBox", "PadLayoutBox", "ShortcutBox" }) if (C<ComboBox>(name).IsDropDownOpen) return true;
   return pickerOpen[0] || pickerOpen[1] || cursors.Any(c => c != null && c.Combo != null);
  }

  static string PrefsFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.xml"); } }

  void ReadPrefs() {
   var saved = Prefs.Read(PrefsFile);
   mode = saved.Mode; scale = saved.Scale; shadows = saved.Shadows; shadowDistance = saved.ShadowDistance; antiAliasing = saved.AntiAliasing; advanced = saved.Advanced;
   fpsLimit = saved.FpsLimit; perDisplay = saved.PerDisplay; playerCount = saved.PlayerCount; Router.MenuLayout = saved.MenuLayout;
   for (int i = 0; i < 2; i++) {
    displays[i] = saved.Displays[i]; scales[i] = saved.Scales[i]; fpsLimits[i] = saved.FpsLimits[i]; rotations[i] = saved.Rotations[i]; names[i] = saved.Names[i]; models[i] = saved.Models[i];
   }
   sharedShaders = saved.SharedShaders; language = saved.Language; launcherLanguage = saved.LauncherLanguage;
   shortcutText = saved.Shortcut; Router.Shortcut = MenuShortcut.Parse(shortcutText);
  }

  // The name and avatar of each player sit on that player's card and are written into its Milk Bar settings when a session starts.
  void WatchPlayer(int player) {
   var nameBox = C<TextBox>("P" + (player + 1) + "NameBox");
   nameBox.Text = names[player];
   nameBox.PreviewTextInput += (s, e) => { if (!Characters.IsNameText(e.Text)) e.Handled = true; };
   DataObject.AddPastingHandler(nameBox, (s, e) => {
    string pasted = e.DataObject.GetData(typeof(string)) as string;
    if (pasted == null || !Characters.IsNameText(pasted)) e.CancelCommand();
   });
   nameBox.LostFocus += (s, e) => {
    names[player] = Characters.CleanName(nameBox.Text, Characters.DefaultNames[player]);
    nameBox.Text = names[player];
    SavePrefs();
   };
   BuildPicker(player);
  }

  string ServerFolder { get { return Path.Combine(root, ".artifacts", "extract", "milkbar", "DedicatedServer"); } }
  string ServerIni { get { return Path.Combine(ServerFolder, "ServerConfig.ini"); } }
  bool serverUp, echoing;

  // Shows the name tag state without sending a command.
  void ShowNameTags(bool on) {
   echoing = true;
   try { C<CheckBox>("LiveNameTags").IsChecked = on; }
   finally { echoing = false; }
  }

  // The world rules live in the server's own ini, so what is shown is always what the server will start with.
  void LoadRules() {
   try {
    rules = ServerRules.Load(ServerIni);
    string file = Path.Combine(ServerFolder, "Gamemodes.json");
    presets = File.Exists(file) ? ServerRules.Presets(File.ReadAllText(file)) : new List<ServerRules>();
   }
   catch (Exception) { rules = new ServerRules(); presets = new List<ServerRules>(); }
  }

  // While a session is open the running server keeps its rules; the new ones are written when the session restarts.
  void SaveRules(bool force = false) {
   if (sessionLive && !force) return;
   try { if (Directory.Exists(ServerFolder)) rules.Save(ServerIni); }
   catch (Exception error) { Write("Could not save the world rules: " + error.Message); }
  }

  void WatchWorld() {
   C<ComboBox>("RulesBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null) return;
    var preset = item.Tag as ServerRules;
    if (preset != null) rules = preset.Copy();
    else { rules.Name = "Custom"; }
    SaveRules();
    Window.Dispatcher.BeginInvoke(new Action(ShowRules));
   };
   C<ComboBox>("SpecialBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is int)) return;
    rules.Special = (int)item.Tag;
    RulesChanged();
   };
   foreach (var name in RuleBoxes) {
    RoutedEventHandler changed = (s, e) => {
     if (filling) return;
     rules.Enemies = C<CheckBox>("RuleEnemies").IsChecked == true; rules.Quests = C<CheckBox>("RuleQuests").IsChecked == true;
     rules.Koroks = C<CheckBox>("RuleKoroks").IsChecked == true; rules.Towers = C<CheckBox>("RuleTowers").IsChecked == true;
     rules.Shrines = C<CheckBox>("RuleShrines").IsChecked == true; rules.Locations = C<CheckBox>("RuleLocations").IsChecked == true;
     rules.DivineBeasts = C<CheckBox>("RuleDivineBeasts").IsChecked == true;
     RulesChanged();
    };
    C<CheckBox>(name).Checked += changed; C<CheckBox>(name).Unchecked += changed;
   }
   C<TextBox>("TimeBox").TextChanged += (s, e) => UpdateLive();
   C<Button>("SetTime").Click += (s, e) => SendLive(ServerCommands.Time(C<TextBox>("TimeBox").Text));
   C<Button>("SetWeather").Click += (s, e) => {
    var item = C<ComboBox>("WeatherBox").SelectedItem as ComboBoxItem;
    if (item != null) SendLive(ServerCommands.SetWeather((string)item.Tag));
   };
   RoutedEventHandler tagsChanged = (s, e) => {
    if (echoing) return;
    var box = C<CheckBox>("LiveNameTags");
    if (!SendLive(ServerCommands.ToggleNameTags)) ShowNameTags(box.IsChecked != true);
   };
   C<CheckBox>("LiveNameTags").Checked += tagsChanged; C<CheckBox>("LiveNameTags").Unchecked += tagsChanged;
   C<Button>("PauseSync").Click += (s, e) => SendLive(ServerCommands.PauseSync);
   C<Button>("ResumeSync").Click += (s, e) => SendLive(ServerCommands.ResumeSync);
   C<Button>("BringP1").Click += (s, e) =>       SendLive(ServerCommands.Bring(names[0], names[1]));
      C<Button>("BringP2").Click += (s, e) => SendLive(ServerCommands.Bring(names[1], names[0]));
  }

  // Editing a rule by hand turns the preset into Custom, unless the rules still match one.
  void RulesChanged() {
   var match = ServerRules.Match(presets, rules);
   rules.Name = match == null ? "Custom" : match.Name;
   SaveRules();
   Window.Dispatcher.BeginInvoke(new Action(ShowRules));
  }

  void ShowRules() {
   filling = true;
   try {
    var box = C<ComboBox>("RulesBox");
    box.Items.Clear();
    box.Items.Add(new ComboBoxItem { Content = Tr("Custom") });
    foreach (var preset in presets) box.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });
    var match = ServerRules.Match(presets, rules);
    box.SelectedIndex = match == null ? 0 : presets.IndexOf(match) + 1;
    var special = C<ComboBox>("SpecialBox");
    special.Items.Clear();
    for (int i = 0; i < ServerRules.SpecialNames.Length; i++) special.Items.Add(new ComboBoxItem { Content = Tr(ServerRules.SpecialNames[i]), Tag = i });
    special.SelectedIndex = rules.Special;
    C<CheckBox>("RuleEnemies").IsChecked = rules.Enemies; C<CheckBox>("RuleQuests").IsChecked = rules.Quests;
    C<CheckBox>("RuleKoroks").IsChecked = rules.Koroks; C<CheckBox>("RuleTowers").IsChecked = rules.Towers;
    C<CheckBox>("RuleShrines").IsChecked = rules.Shrines; C<CheckBox>("RuleLocations").IsChecked = rules.Locations;
    C<CheckBox>("RuleDivineBeasts").IsChecked = rules.DivineBeasts;
    var weather = C<ComboBox>("WeatherBox");
    int chosen = Math.Max(0, weather.SelectedIndex);
    weather.Items.Clear();
    foreach (var option in ServerCommands.Weather) weather.Items.Add(new ComboBoxItem { Content = Tr(option[0]), Tag = option[1] });
    weather.SelectedIndex = chosen;
    ShowRuleInfo(match);
   }
   finally { filling = false; }
  }

  // The live controls only work while the launcher's own server is running.
  void UpdateLive() {
   foreach (var name in LiveControls) C<Control>(name).IsEnabled = serverUp;
   C<Button>("SetTime").IsEnabled = serverUp && ServerCommands.Time(C<TextBox>("TimeBox").Text) != null;
  }

  bool SendLive(string command) {
   if (command == null) return false;
   if (serverHost.Send(command)) { Write("Server command: " + command); return true; }
   Write("The server is not running, so the command was not sent.");
   return false;
  }

  // Switches the launcher text to the chosen language and redraws everything the code writes itself.
  void ApplyLanguage() {
   Lang.Current = launcherLanguage;
   translator.Apply();
   C<TextBlock>("HelpText").Text = Lang.Help();
   filling = true;
   try { ShowAdvanced(); } finally { filling = false; }
   ResetHints();
   ShowRules(); UpdateLive(); ShowModeSwitch();
   FillLanguages();
   C<TextBlock>("Status").Text = Tr(statusText, statusArgs);
   C<TextBlock>("Prompt").Text = Tr(promptText, promptArgs);
   foreach (var step in new List<string>(steps.Keys)) Step(step, steps[step].Key, steps[step].Value);
   if (Window.IsLoaded) { UpdatePlan(); RefreshProcesses(); }
  }

  Button LanguageButton(string name, string code, bool current, string idPrefix, Action choose) {
   var button = new Button { Content = name, FontSize = 18, Margin = new Thickness(0, 0, 8, 8), HorizontalAlignment = HorizontalAlignment.Stretch,
    Background = Paint(current ? "#B03C554B" : "#801B252B"), BorderBrush = Paint(current ? "#D5C396" : "#364E5659") };
   System.Windows.Automation.AutomationProperties.SetAutomationId(button, idPrefix + code);
   System.Windows.Automation.AutomationProperties.SetItemStatus(button, current ? "Selected" : "");
   button.Click += (s, e) => choose();
   return button;
  }

  void FillLanguages() {
   var launcher = C<Panel>("LauncherLanguageList");
   launcher.Children.Clear();
   foreach (string code in Lang.Supported) {
    string chosen = code;
    launcher.Children.Add(LanguageButton(GameLanguages.Names[code], code, code == launcherLanguage, "Launcher_", () => ChooseLauncherLanguage(chosen)));
   }
   var list = C<Panel>("LanguageList");
   list.Children.Clear();
   foreach (var item in gameLanguages) {
    string chosen = item.Code;
    list.Children.Add(LanguageButton(item.Name, chosen, chosen == language, "Language_", () => ChooseLanguage(chosen)));
   }
  }

  // The game language is written into both Cemu settings when a session starts.
  void ChooseLanguage(string code) {
   if (code == language) return;
   language = code;
   SavePrefs();
   FillLanguages();
   Write("Game language: " + GameLanguages.Find(gameLanguages, code).Name + ".");
  }

  void ChooseLauncherLanguage(string code) {
   if (code == launcherLanguage) return;
   launcherLanguage = code;
   SavePrefs();
   ApplyLanguage();
   Write("Launcher language: " + GameLanguages.Names[code] + ".");
  }

  void SetStatus(string english, params object[] args) {
   statusText = english; statusArgs = args;
   C<TextBlock>("Status").Text = Tr(english, args);
  }

  void SetPrompt(string english, params object[] args) {
   promptText = english; promptArgs = args;
   C<TextBlock>("Prompt").Text = Tr(english, args);
  }

  void SavePrefs() {
   try {
    new XDocument(new XElement("Preferences", new XElement("Layout", mode), new XElement("RenderScale", GraphicsMod.NearestScale(scale).ToString(System.Globalization.CultureInfo.InvariantCulture)),
     new XElement("Shadows", shadows ?? ""), new XElement("ShadowDistance", shadowDistance ?? ""), new XElement("AntiAliasing", antiAliasing ?? ""),
     new XElement("GraphicsApi", advanced.Api), new XElement("VSync", advanced.VSync ? "1" : "0"), new XElement("AsyncCompile", advanced.AsyncCompile ? "1" : "0"),
     new XElement("AccurateBarriers", advanced.AccurateBarriers ? "1" : "0"), new XElement("UpscaleFilter", advanced.Upscale), new XElement("DownscaleFilter", advanced.Downscale),
     new XElement("FpsLimit", fpsLimit == FpsMod.AutoFps ? "Auto" : fpsLimit.ToString()),
     new XElement("Arrangement", perDisplay ? "PerDisplay" : "Split"), new XElement("Players", playerCount),
     new XElement("Display1", displays[0] ?? ""), new XElement("Display2", displays[1] ?? ""),
     new XElement("Scale1", GraphicsMod.NearestScale(scales[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)), new XElement("Scale2", GraphicsMod.NearestScale(scales[1]).ToString(System.Globalization.CultureInfo.InvariantCulture)),
     new XElement("Fps1", fpsLimits[0] == FpsMod.AutoFps ? "Auto" : fpsLimits[0].ToString()), new XElement("Fps2", fpsLimits[1] == FpsMod.AutoFps ? "Auto" : fpsLimits[1].ToString()),
     new XElement("Rotation1", rotations[0]), new XElement("Rotation2", rotations[1]),
     new XElement("Name1", names[0]), new XElement("Model1", models[0]), new XElement("Name2", names[1]), new XElement("Model2", models[1]),
     new XElement("SharedShaderCache", sharedShaders ? "1" : "0"), new XElement("Language", language), new XElement("LauncherLanguage", launcherLanguage), new XElement("MenuLayout", Router.MenuLayout < 0 ? "Auto" : PadKind.Names[Router.MenuLayout]), new XElement("MenuShortcut", shortcutText))).Save(PrefsFile);
   }
   catch (Exception error) { Write("Could not save the display settings: " + error.Message); }
  }

  void ChangedDisplayChoice() {
   SavePrefs();
   UpdatePlan();
   if (sessionLive) return;
   placed.Clear();
   PlaceWindows();
  }

  // The offered frame rates are the FPS++ presets that the display (the primary one, or the player's own) can actually show.
  List<FpsMod.Limit> FpsChoices(int hz) {
   return FpsMod.Available(FpsMod.Read(Path.Combine(root, ".tools", "instances", "cemuA")), hz);
  }

  int HzOf(int player) { return pair != null ? pair[player].Hz : displayHz; }

  FpsMod.Limit CurrentFps(int player) { return FpsMod.Pick(FpsChoices(HzOf(player)), pair != null ? fpsLimits[player] : fpsLimit, HzOf(player)); }

  DisplayPlan CurrentPlan() {
   return pair != null ? ScreenLayout.PlanSeparate(pair[0], scales[0], pair[1], scales[1], rotations)
    : playerCount == 1 ? ScreenLayout.PlanSingle(scale, displayWidth, displayHeight) : ScreenLayout.Plan(mode, scale, displayWidth, displayHeight, rotations);
  }

  // Clicking a player's cell in the preview turns that player's screen a quarter turn clockwise.
  void TurnScreen(int player) {
   if (playerCount != 2) return;
   rotations[player] = (rotations[player] + 90) % 360;
   ChangedDisplayChoice();
  }

  // Each preview cell shows its player's avatar and label, turned like that player's screen; one player never turns.
  void ShowBadges() {
   for (int i = 0; i < 2; i++) {
    var cell = C<Button>("View" + (i + 1));
    cell.Content = PlayerBadge.Build(Avatar(AvatarOf(i)), Tr(i == 0 ? "P1" : "P2"), Paint(PlayerColors[i]), playerCount == 2 ? rotations[i] : 0);
    cell.IsEnabled = appearanceEnabled && playerCount == 2;
    TipTurn(cell, i);
   }
   var reset = C<Button>("ResetTurns");
   reset.Visibility = playerCount == 2 ? Visibility.Visible : Visibility.Collapsed;
   reset.IsEnabled = appearanceEnabled && (rotations[0] != 0 || rotations[1] != 0);
   reset.ToolTip = Tr("Turn both screens back upright");
   System.Windows.Automation.AutomationProperties.SetName(reset, Tr("Turn both screens back upright"));
   foreach (var tile in C<Canvas>("MonitorMap").Children.OfType<Button>()) {
    var info = (KeyValuePair<int, string>)tile.Tag;
    tile.Content = TileContent(info.Key, info.Value);
   }
  }

  void TipTurn(Button button, int player) {
   string tip = Tr("Turn the screen of player {0} a quarter turn clockwise", player + 1);
   button.ToolTip = tip;
   System.Windows.Automation.AutomationProperties.SetName(button, tip);
  }

  UIElement TileContent(int owner, string label) {
   return PlayerBadge.Build(Avatar(AvatarOf(owner)), label, Paint(PlayerColors[owner]), rotations[owner]);
  }

  static string MonitorKey(List<MonitorInfo> list) {
   return string.Join(";", list.ConvertAll(m => m.Device + "," + m.X + "," + m.Y + "," + m.Width + "x" + m.Height + "@" + m.Hz + (m.Primary ? "p" : "")).ToArray());
  }

  void RefreshMonitors() {
   WindowPlacer.PrimaryDisplay(out displayWidth, out displayHeight, out displayHz);
   monitors = WindowPlacer.Monitors();
   monitorKey = MonitorKey(monitors);
   MonitorInfo first, second;
   canSeparate = playerCount == 2 && ScreenLayout.Pair(monitors, null, null, out first, out second);
   pair = null;
   if (perDisplay && canSeparate && ScreenLayout.Pair(monitors, displays[0], displays[1], out first, out second)) pair = new[] { first, second };
  }

  // The model name from the display's EDID, or "Display N" when Windows does not know it.
  string MonitorTitle(MonitorInfo monitor) {
   return string.IsNullOrEmpty(monitor.Name) ? Tr("Display {0}", ScreenLayout.DisplayNumber(monitor.Device)) : monitor.Name;
  }

  string MonitorLabel(MonitorInfo monitor) {
   return Tr("Display {0}", ScreenLayout.DisplayNumber(monitor.Device)) + (monitor.Primary ? " (" + Tr("primary") + ")" : "") + " · " + monitor.Width + " × " + monitor.Height + " · " + monitor.Hz + " Hz";
  }

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

  void FillScales(ComboBox box, double current, bool compact) {
   box.Items.Clear();
   foreach (double option in GraphicsMod.Scales) {
    string label = GraphicsMod.ScaleLabel(option, text => Tr(text));
    box.Items.Add(new ComboBoxItem { Content = compact ? Math.Round(option * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%" : label, ToolTip = label, Tag = option });
   }
   box.SelectedIndex = Array.IndexOf(GraphicsMod.Scales, GraphicsMod.NearestScale(current));
  }

  void ShowQuality() {
   filling = true;
   try {
    FillScales(C<ComboBox>("ScaleBox"), scale, false);
    for (int i = 0; i < 2; i++) FillScales(C<ComboBox>("Scale" + (i + 1) + "Box"), scales[i], true);
    C<ComboBox>("ScaleBox").IsEnabled = pair == null;
    C<TextBlock>("GraphicsSeparateNote").Visibility = pair == null ? Visibility.Collapsed : Visibility.Visible;
    FillQuality("ShadowsBox", GraphicsMod.ShadowsCategory, shadows);
    FillQuality("ShadowDistanceBox", GraphicsMod.ShadowDistanceCategory, shadowDistance);
    FillQuality("AntiAliasingBox", GraphicsMod.AntiAliasingCategory, antiAliasing);
    ShowAdvanced();
   }
   finally { filling = false; }
  }

  void ShowAdvanced() {
   var api = C<ComboBox>("ApiBox");
   api.Items.Clear();
   foreach (string option in GraphicsMod.Apis)
    api.Items.Add(new ComboBoxItem { Content = option == GraphicsMod.AutoApi ? Tr("Auto ({0})", GraphicsMod.ResolveApi(option, vulkanAvailable)) : option, Tag = option, IsEnabled = option != GraphicsMod.VulkanApi || vulkanAvailable });
   api.SelectedIndex = Array.IndexOf(GraphicsMod.Apis, advanced.Api);
   Action<string, int> filters = (name, current) => {
    var box = C<ComboBox>(name);
    box.Items.Clear();
    for (int i = 0; i < GraphicsMod.Filters.Length; i++) box.Items.Add(new ComboBoxItem { Content = GraphicsMod.Filters[i], Tag = i });
    box.SelectedIndex = current;
   };
   filters("UpscaleBox", advanced.Upscale);
   filters("DownscaleBox", advanced.Downscale);
   C<CheckBox>("VSyncBox").IsChecked = advanced.VSync;
   C<CheckBox>("AsyncBox").IsChecked = advanced.AsyncCompile;
   C<CheckBox>("BarriersBox").IsChecked = advanced.AccurateBarriers;
   C<CheckBox>("BarriersBox").IsEnabled = GraphicsMod.ResolveApi(advanced.Api, vulkanAvailable) == GraphicsMod.VulkanApi;
   C<CheckBox>("SharedShadersBox").IsChecked = sharedShaders;
   var layout = C<ComboBox>("PadLayoutBox");
   layout.Items.Clear();
   layout.Items.Add(new ComboBoxItem { Content = Tr("Auto (match each controller)"), Tag = "Auto" });
   layout.Items.Add(new ComboBoxItem { Content = Tr("Nintendo (A right, B bottom)"), Tag = "Nintendo" });
   layout.Items.Add(new ComboBoxItem { Content = Tr("Xbox (A bottom, B right)"), Tag = "Xbox" });
   layout.Items.Add(new ComboBoxItem { Content = Tr("PlayStation ({0} bottom, {1} right)", CrossMark, CircleMark), Tag = "PlayStation" });
   layout.SelectedIndex = Router.MenuLayout + 1;
   padLayoutShown = null;
   FillShortcut();
   ShowRecommended();
  }

  string padLayoutShown;

  const string CrossMark = "\u2715", CircleMark = "\u25CB", ListSeparator = ", ";
  static string ConfirmLabel(int layout) { return layout == PadKind.PlayStation ? CrossMark : "A"; }
  static string BackLabel(int layout) { return layout == PadKind.PlayStation ? CircleMark : "B"; }

  // Tells which layout each connected controller follows and keeps the "A: select" hint in step with the first one.
  void ShowPadLayout(RouterSnapshot snapshot) {
   var parts = new List<string>();
   int first = -1;
   for (int slot = 0; slot < 4; slot++) {
    if (snapshot.Names[slot] == null) continue;
    int layout = PadKind.Resolve(Router.MenuLayout, snapshot.Kinds[slot]);
    if (first < 0) first = layout;
    parts.Add(snapshot.Names[slot] + ": " + PadKind.Names[layout]);
   }
   string signature = Router.MenuLayout + "|" + string.Join(";", parts.ToArray()) + "|" + Lang.Current;
   if (signature == padLayoutShown) return;
   padLayoutShown = signature;
   int shown = first < 0 ? PadKind.Resolve(Router.MenuLayout, -1) : first;
   shortcutLayout = shown;
   ShowShortcut();
   string hint = Tr("D-pad: move   {0}: select   {1}: back", ConfirmLabel(shown), BackLabel(shown));
   C<TextBlock>("PadHint1").Text = hint;
   C<TextBlock>("PadHint2").Text = hint;
   C<ContentControl>("StartIcon").Content = PadIcons.Start(shown);
   C<ContentControl>("ModeIcon").Content = PadIcons.Select(shown);
   C<TextBlock>("PadLayoutInfo").Text = parts.Count == 0 ? Tr("No controller detected: menus use the {0} layout.", PadKind.Names[shown]) : Tr("Detected: {0}", string.Join(ListSeparator, parts.ToArray()));
  }

  void FillShortcut() {
   var box = C<ComboBox>("ShortcutBox");
   var tags = new List<string> { "Start+LT", "Start+LT+RT", "L3+R3" };
   if (shortcutText != "Off" && !tags.Contains(shortcutText)) tags.Add(shortcutText);
   box.Items.Clear();
   foreach (string tag in tags) box.Items.Add(new ComboBoxItem { Content = tag.Replace("+", " + "), Tag = tag });
   box.Items.Add(new ComboBoxItem { Content = Tr("Off"), Tag = "Off" });
   box.SelectedIndex = tags.IndexOf(shortcutText) >= 0 ? tags.IndexOf(shortcutText) : tags.Count;
   ShowShortcut();
  }

  void ShowShortcut() {
   if (capturing) return;
   C<Button>("ShortcutChange").Content = Tr("Change shortcut");
   C<TextBlock>("ShortcutInfo").Text = shortcutText == "Off" ? Tr("The menu shortcut is off.") : Tr("Hold {0} for 1.5 seconds to bring this menu over the game. Hold it again, or press {1}, to go back.", shortcutText.Replace("+", " + "), BackLabel(shortcutLayout));
  }

  void SetShortcut(string text) {
   shortcutText = text;
   Router.Shortcut = MenuShortcut.Parse(text);
   SavePrefs();
  }

  void BeginCapture() {
   capturing = true; captureArmed = false; captureSlot = -1; captureButtons = 0; captureLt = captureRt = false;
   captureStarted = Environment.TickCount;
   Router.Shortcut = null;
   C<Button>("ShortcutChange").Content = Tr("Cancel");
   C<TextBlock>("ShortcutInfo").Text = Tr("Hold the new shortcut on a controller (2 to 4 inputs), then let go. Nothing pressed for 10 seconds cancels.");
  }

  void EndCapture(MenuShortcut result) {
   capturing = false;
   if (result != null) SetShortcut(result.ToString()); else Router.Shortcut = MenuShortcut.Parse(shortcutText);
   filling = true;
   try { FillShortcut(); } finally { filling = false; }
   if (result != null) C<TextBlock>("ShortcutInfo").Text = Tr("Shortcut saved: {0}.", shortcutText.Replace("+", " + "));
  }

  // Records the inputs held together on one controller and finishes when it lets go, so the shortcut is whatever was held at the peak.
  void CaptureStep(RouterSnapshot snapshot) {
   bool any = false, mine = false;
   for (int slot = 0; slot < 4; slot++) {
    if (snapshot.Names[slot] == null) continue;
    ushort buttons; bool lt, rt;
    MenuShortcut.Pressed(snapshot.Pads[slot], out buttons, out lt, out rt);
    bool held = buttons != 0 || lt || rt;
    any |= held;
    if (!captureArmed || !held) continue;
    if (captureSlot < 0) captureSlot = slot;
    if (slot != captureSlot) continue;
    mine = true;
    captureButtons |= buttons; captureLt |= lt; captureRt |= rt;
   }
   if (!captureArmed) { if (!any) { captureArmed = true; captureStarted = Environment.TickCount; } return; }
   if (captureSlot < 0) { if (Environment.TickCount - captureStarted > 10000) EndCapture(null); return; }
   if (mine) return;
   var result = MenuShortcut.Capture(captureButtons, captureLt, captureRt);
   if (result != null) { EndCapture(result); return; }
   captureSlot = -1; captureButtons = 0; captureLt = captureRt = false; captureStarted = Environment.TickCount;
   C<TextBlock>("ShortcutInfo").Text = Tr("That combination cannot be used: pick 2 to 4 inputs, and not both small centre buttons.");
  }

  // The launcher comes in front of the running game even though no keyboard or mouse touched it.
  void ShowFromGame() {
   if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
   Window.Show();
   WindowPlacer.Focus(new System.Windows.Interop.WindowInteropHelper(Window).Handle);
   Window.Activate();
   Write("Menu shortcut: the launcher is in front of the game.");
  }

  // Hides the launcher and gives the focus back to the Cemu window of the player who asked, or the other one when that player has none.
  void ReturnToGame(int player) {
   string[] folders = { "\\cemuA\\", "\\cemuB\\" };
   var target = IntPtr.Zero;
   foreach (int index in new[] { player, 1 - player }) {
    foreach (var window in WindowPlacer.CemuWindows(folders[index]).Values) { target = window; break; }
    if (target != IntPtr.Zero) break;
   }
   if (target != IntPtr.Zero) WindowPlacer.Focus(target);
   Window.WindowState = WindowState.Minimized;
   Write("Menu shortcut: back to the game.");
  }

  void UseShortcut(RouterSnapshot snapshot) {
   if (lastChordSeq < 0) lastChordSeq = snapshot.ChordSeq;
   Router.MenuOpen = Window.IsActive && gameRunning;
   if (snapshot.ChordSeq == lastChordSeq) return;
   lastChordSeq = snapshot.ChordSeq;
   if (capturing || setupActive || !gameRunning) return;
   if (Window.IsActive) ReturnToGame(Array.IndexOf(snapshot.Routes, snapshot.ChordSlot) == 1 ? 1 : 0); else ShowFromGame();
  }

  void WatchAdvanced() {
   C<ComboBox>("ShortcutBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is string)) return;
    SetShortcut((string)item.Tag);
    ShowShortcut();
   };
   C<Button>("ShortcutChange").Click += (s, e) => { if (capturing) EndCapture(null); else BeginCapture(); };
   C<Button>("ReturnGame").Click += (s, e) => ReturnToGame(0);
   C<ComboBox>("PadLayoutBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is string)) return;
    Router.MenuLayout = Array.IndexOf(PadKind.Names, (string)item.Tag);
    padLayoutShown = null;
    SavePrefs();
   };
   C<ComboBox>("ApiBox").SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is string)) return;
    advanced.Api = (string)item.Tag;
    C<CheckBox>("BarriersBox").IsEnabled = GraphicsMod.ResolveApi(advanced.Api, vulkanAvailable) == GraphicsMod.VulkanApi;
    SavePrefs();
   };
   Action<string, Action<int>> filter = (name, set) => C<ComboBox>(name).SelectionChanged += (s, e) => {
    var item = ((ComboBox)s).SelectedItem as ComboBoxItem;
    if (filling || item == null || !(item.Tag is int)) return;
    set((int)item.Tag);
    SavePrefs();
   };
   filter("UpscaleBox", value => advanced.Upscale = value);
   filter("DownscaleBox", value => advanced.Downscale = value);
   Action<string, Action<bool>> toggle = (name, set) => {
    var box = C<CheckBox>(name);
    RoutedEventHandler changed = (s, e) => { if (filling) return; set(box.IsChecked == true); SavePrefs(); };
    box.Checked += changed; box.Unchecked += changed;
   };
   toggle("VSyncBox", value => advanced.VSync = value);
   toggle("AsyncBox", value => advanced.AsyncCompile = value);
   toggle("BarriersBox", value => advanced.AccurateBarriers = value);
   toggle("SharedShadersBox", value => sharedShaders = value);
  }

  readonly List<TextBlock> hintBars = new List<TextBlock>();

  static string Percent(double value) { return Math.Round(value * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"; }

  static string Gigabytes(double value) { return value.ToString(value >= 2 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture); }

  // The window one game gets when the primary display is shared (or all of it when playing alone).
  Area GameWindow() {
   int width = displayWidth > 0 ? displayWidth : 1920, height = displayHeight > 0 ? displayHeight : 1080;
   return playerCount == 1 ? new Area { Width = width, Height = height } : ScreenLayout.Split(mode, width, height)[0];
  }

  // The recommended render scale for the shared display [0] and for each player's own display [1], [2].
  double[] RecommendedScales() {
   var window = GameWindow();
   var best = new double[3];
   best[0] = Hardware.RecommendedScale(hardware.Tier, window.Width, window.Height, playerCount);
   MonitorInfo first, second;
   var own = pair;
   if (own == null && canSeparate && ScreenLayout.Pair(monitors, displays[0], displays[1], out first, out second)) own = new[] { first, second };
   for (int i = 0; i < 2; i++) best[i + 1] = own == null ? best[0] : Hardware.RecommendedScale(hardware.Tier, own[i].Width, own[i].Height, playerCount);
   return best;
  }

  string RecommendedPreset(string category, string wanted) { return Hardware.PickPreset(GraphicsMod.Read(InstanceFolder, category), wanted); }

  string PresetLabel(string name) { return name == null ? Tr("Not available") : GraphicsMod.Label(name, text => Tr(text)); }

  void ShowRecommended() {
   var lines = new List<string>();
   lines.Add(string.IsNullOrEmpty(hardware.Gpu) ? Tr("Graphics card not detected") : hardware.Gpu + (hardware.VramMb > 0 ? " (" + Gigabytes(hardware.VramMb / 1024.0) + " GB)" : ""));
   string processor = !string.IsNullOrEmpty(hardware.Cpu) ? Tr("{0} · {1} threads", hardware.Cpu, hardware.Threads) : "";
   if (hardware.RamGb > 0) processor += (processor.Length > 0 ? " · " : "") + Tr("{0} GB of memory", Gigabytes(hardware.RamGb));
   if (processor.Length > 0) lines.Add(processor);
   lines.Add(Tr("Performance class: {0}", Tr(Hardware.TierNames[hardware.Tier])));
   C<TextBlock>("HardwareText").Text = string.Join("\n", lines.ToArray());
   var best = RecommendedScales();
   string rendering = pair != null ? Percent(best[1]) + " / " + Percent(best[2]) : Percent(best[0]);
   C<TextBlock>("RecommendedText").Text = Tr("Recommended: render {0}, shadows {1}, shadow distance {2}, anti-aliasing {3}.", rendering,
     PresetLabel(RecommendedPreset(GraphicsMod.ShadowsCategory, Hardware.ShadowsFor(hardware.Tier))),
     PresetLabel(RecommendedPreset(GraphicsMod.ShadowDistanceCategory, Hardware.ShadowDistanceFor(hardware.Tier))),
     PresetLabel(RecommendedPreset(GraphicsMod.AntiAliasingCategory, Hardware.AntiAliasingChoice)))
    + "\n" + Tr("Cemu: automatic API, {0} upscale filter, asynchronous shaders on, VSync off. FPS target Auto.", GraphicsMod.Filters[Hardware.AdvancedFor(hardware.Tier).Upscale]);
  }

  // Puts every Graphics option back to what suits this PC; the FPS targets go back to Auto.
  void UseRecommendedGraphics() {
   int tier = hardware.Tier;
   var best = RecommendedScales();
   scale = best[0]; scales[0] = best[1]; scales[1] = best[2];
   shadows = RecommendedPreset(GraphicsMod.ShadowsCategory, Hardware.ShadowsFor(tier));
   shadowDistance = RecommendedPreset(GraphicsMod.ShadowDistanceCategory, Hardware.ShadowDistanceFor(tier));
   antiAliasing = RecommendedPreset(GraphicsMod.AntiAliasingCategory, Hardware.AntiAliasingChoice);
   advanced = Hardware.AdvancedFor(tier);
   fpsLimit = FpsMod.AutoFps; fpsLimits[0] = fpsLimits[1] = FpsMod.AutoFps;
   Write("Recommended graphics settings applied (" + Hardware.TierNames[tier] + " class).");
   ChangedDisplayChoice();
  }

  string PresetBlurb(string name) {
   switch (name) {
    case "Game Completion": return Tr("For playing the whole game together. Defeated enemies, Korok seeds, towers, shrines and map locations are shared; quests and Divine Beasts stay separate for each player.");
    case "Hunter Vs Speedrunner": return Tr("A chase game: the speedrunner races through the game while the hunters try to stop them. What one player clears, activates or discovers counts for both.");
    case "Any% Speedrun": return Tr("Built for speedruns: what one player defeats, activates or discovers counts for both, except quests, Korok seeds and Divine Beasts.");
    case "Hide n' seek": return Tr("Only defeated enemies are shared. Everything else stays separate for each player, which suits hide and seek in the same world.");
    case "Death Swap": return Tr("A game mode where the players are swapped around the world at intervals. Nothing else is shared.");
    case "Custom": return Tr("Your own mix: choose below what the players share.");
   }
   return "";
  }

  string SpecialBlurb(int special) {
   return special == 1 ? Tr("Adds a chase: one player speedruns while the others hunt them. Best with the Hunter Vs Speedrunner preset.")
    : special == 2 ? Tr("Adds Death Swap: the players are swapped around the world at intervals. Best with the Death Swap preset.")
    : Tr("Normal co-op: only the rules above decide what the players share.");
  }

  void ShowRuleInfo(ServerRules match) {
   Func<List<string>, string> list = names => names.Count == 0 ? Tr("Nothing") :    string.Join(ListSeparator, names.ConvertAll(n => Tr(n)).ToArray());
   C<TextBlock>("PresetInfo").Text = PresetBlurb(match == null ? "Custom" : match.Name);
   C<TextBlock>("PresetShares").Text = Tr("Shared: {0}", list(rules.Shared(true))) + "\n" + Tr("Not shared: {0}", list(rules.Shared(false)));
   C<TextBlock>("SpecialInfo").Text = SpecialBlurb(rules.Special);
  }

  // The preset every new player should start from; it does not depend on this PC's hardware.
  void UseRecommendedRules() {
   if (presets.Count == 0) return;
   rules = ServerRules.Recommended(presets);
   SaveRules();
   ShowRules();
  }

  void ResetHints() {
   foreach (var bar in hintBars) bar.Text = Tr("Hover an option, or move the focus to it, to see what it does.");
  }

  // A page's hint bar shows the tip of the option under the mouse or holding keyboard focus.
  readonly Dictionary<string, string> hintIdle = new Dictionary<string, string>();

  void WireHints(string page, string bar) {
   var target = C<TextBlock>(bar);
   hintBars.Add(target);
   Action<FrameworkElement> attach = null;
   attach = element => {
    if (element.ToolTip is string) {
     ToolTipService.SetIsEnabled(element, false);
     element.MouseEnter += (s, e) => target.Text = element.ToolTip as string ?? Tr("Hover an option, or move the focus to it, to see what it does.");
     element.GotKeyboardFocus += (s, e) => target.Text = element.ToolTip as string ?? Tr("Hover an option, or move the focus to it, to see what it does.");
     element.MouseLeave += (s, e) => target.Text = Tr("Hover an option, or move the focus to it, to see what it does.");
    }
    foreach (object child in LogicalTreeHelper.GetChildren(element)) {
     var next = child as FrameworkElement;
     if (next != null) attach(next);
    }
   };
   attach(C<FrameworkElement>(page));
   target.Text = Tr("Hover an option, or move the focus to it, to see what it does.");
  }

  void FillQuality(string name, string category, string wanted) {
   var choices = GraphicsMod.Read(InstanceFolder, category);
   var box = C<ComboBox>(name);
   box.Items.Clear();
   box.IsEnabled = choices.Count > 0;
   if (choices.Count == 0) { box.Items.Add(new ComboBoxItem { Content = Tr("Not available") }); box.SelectedIndex = 0; return; }
   var current = GraphicsMod.Pick(choices, wanted);
   foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = GraphicsMod.Label(choice.Name, text => Tr(text)), ToolTip = choice.Name, Tag = choice.Name });
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

  void FillFps(ComboBox box, List<FpsMod.Limit> choices, int wanted, int hz, bool compact) {
   var current = FpsMod.Pick(choices, wanted, hz);
   filling = true;
   try {
    box.Items.Clear();
    box.IsEnabled = choices.Count > 0;
    if (current == null) { box.Items.Add(new ComboBoxItem { Content = Tr("FPS++ is not installed") }); box.SelectedIndex = 0; return; }
    int best = FpsMod.Best(choices, hz).Fps;
    box.Items.Add(new ComboBoxItem { Content = compact ? Tr("Auto · {0} FPS", best) : Tr("FPS target: Auto · {0} FPS", best), Tag = FpsMod.AutoFps });
    foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = choice.Fps + " FPS", Tag = choice.Fps });
    box.SelectedIndex = wanted == FpsMod.AutoFps ? 0 : choices.FindIndex(l => l.Fps == current.Fps) + 1;
   }
   finally { filling = false; }
  }

  void ShowFpsChoices() {
   FillFps(C<ComboBox>("FpsBox"), FpsChoices(displayHz), fpsLimit, displayHz, false);
   for (int i = 0; i < 2; i++) {
    int hz = pair != null ? pair[i].Hz : displayHz;
    FillFps(C<ComboBox>("Fps" + (i + 1) + "Box"), FpsChoices(hz), fpsLimits[i], hz, true);
   }
  }
  void UpdatePlan() {
   RefreshMonitors();
   ShowPlayers();
   bool separate = pair != null;
   C<Button>("Arrange").Visibility = canSeparate ? Visibility.Visible : Visibility.Collapsed;
   C<Button>("Arrange").Content = Tr(separate ? "Displays: one per player" : "Displays: split the primary display");
   foreach (var name in new[] { "Preview", "Layout", "FpsBox" }) C<UIElement>(name).Visibility = separate ? Visibility.Collapsed : Visibility.Visible;
   C<UIElement>("Layout").Visibility = separate || playerCount == 1 ? Visibility.Collapsed : Visibility.Visible;
   foreach (var name in new[] { "MonitorMap", "SeparatePanel" }) C<UIElement>(name).Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
   var primary = monitors.FirstOrDefault(m => m.Primary);
   C<TextBlock>("ScreenInfo").Text = separate ? MonitorTitle(pair[0]) + "  +  " + MonitorTitle(pair[1]) : (primary.Device == null ? displayWidth + " × " + displayHeight : MonitorTitle(primary)) + "  /  " + displayHz + " Hz";
   C<Button>("Layout").Content = Tr(mode == LayoutMode.Auto ? "Split: automatic" : mode == LayoutMode.SideBySide ? "Split: side by side" : "Split: top and bottom");
   ShowDisplays();
   ShowFpsChoices();
   ShowQuality();
   ShowBadges();
   if (displayWidth < 320 || displayHeight < 240) { C<TextBlock>("LayoutInfo").Text = Tr("The primary display is not ready."); return; }
   var plan = CurrentPlan();
   if (separate) { ShowSeparatePlan(plan); FitPrimary(); return; }
   bool side = plan.Layout == LayoutMode.SideBySide;
   var first = plan.Players[0];
   var shownFps = CurrentFps(0);
   string shape = playerCount == 1 ? Tr("One player · the whole display") : Tr(side ? "Side by side · P1 left / P2 right" : "Top and bottom · P1 top / P2 bottom");
   C<TextBlock>("LayoutInfo").Text = shape + Environment.NewLine + Tr("Each game renders {0} × {1}", first.RenderWidth, first.RenderHeight) + (shownFps == null ? "" : " " + Tr("at {0} FPS", shownFps.Fps));
   var frame = C<Border>("PreviewFrame");
   double ratio = (double)displayWidth / displayHeight;
   frame.Width = ratio < 1 ? 118 : Math.Min(360, 210 * ratio); frame.Height = frame.Width / ratio;
   var a = C<Button>("View1"); var b = C<Button>("View2");
   b.Visibility = playerCount == 1 ? Visibility.Collapsed : Visibility.Visible;
   if (playerCount == 1) { Grid.SetRow(a, 0); Grid.SetColumn(a, 0); Grid.SetRowSpan(a, 2); Grid.SetColumnSpan(a, 2); FitPrimary(); return; }
   Grid.SetRow(a, 0); Grid.SetColumn(a, 0); Grid.SetRow(b, side ? 0 : 1); Grid.SetColumn(b, side ? 1 : 0);
   Grid.SetRowSpan(a, side ? 2 : 1); Grid.SetRowSpan(b, side ? 2 : 1); Grid.SetColumnSpan(a, side ? 1 : 2); Grid.SetColumnSpan(b, side ? 1 : 2);
   FitPrimary();
  }

  // One player hides everything that only makes sense with two: the second card, the swap, its preview, its status row and the teleports.
  void ShowPlayers() {
   bool two = playerCount == 2;
   Func<bool, Visibility> show = on => on ? Visibility.Visible : Visibility.Collapsed;
   foreach (var name in new[] { "P2Card", "Swap", "BLabel", "BDot", "BText", "BringP1", "BringP2" }) C<UIElement>(name).Visibility = show(two);
   if (!two) SetPicker(1, false);
   ShowModeSwitch();
   UpdatePlayLayout();
  }

  // The switch always offers the other mode, so its text and tooltip follow the current one.
  void ShowModeSwitch() {
   bool two = playerCount == 2;
   C<TextBlock>("ModeText").Text = two ? Tr("Switch to single player") : Tr("Switch to multiplayer");
   C<Button>("Mode").ToolTip = two ? Tr("Play alone: one game on the whole primary display.") : Tr("Two players on this PC, with the screen split or one display each.");
  }

  void TogglePlayerCount() { SetPlayerCount(playerCount == 1 ? 2 : 1, actorSlot); }

  // The controller that picks single player becomes player 1; going back to two players restores the routes it replaced.
  void SetPlayerCount(int count, int slot) {
   if (count == playerCount) return;
   if (count == 1) {
    routesBeforeSingle = null;
    if (slot >= 0) { routesBeforeSingle = router.Snapshot().Routes; router.Promote(slot); }
   }
   else if (routesBeforeSingle != null) { router.SetRoutes(routesBeforeSingle); routesBeforeSingle = null; }
   playerCount = count;
   ChangedDisplayChoice();
   UpdateStart();
  }

  void ShowDisplays() {
   filling = true;
   try {
    for (int i = 0; i < 2; i++) {
     var box = C<ComboBox>("Display" + (i + 1) + "Box");
     box.Items.Clear();
     foreach (var monitor in monitors.OrderByDescending(m => m.Primary).ThenBy(m => m.X).ThenBy(m => m.Y)) {
      box.Items.Add(new ComboBoxItem { Content = MonitorLabel(monitor), Tag = monitor.Device });
      if (pair != null && monitor.Device == pair[i].Device) box.SelectedIndex = box.Items.Count - 1;
     }
    }
   }
   finally { filling = false; }
  }

  // The preview maps the displays as Windows lays them out; each player's display is tinted with that player's colour.
  void ShowSeparatePlan(DisplayPlan plan) {
   var lines = new List<string>();
   for (int i = 0; i < 2; i++) {
    var view = plan.Players[i];
    var fps = CurrentFps(i);
    lines.Add(fps == null ? Tr("Player {0}: {1} × {2}", i + 1, view.RenderWidth, view.RenderHeight) : Tr("Player {0}: {1} × {2} at {3} FPS", i + 1, view.RenderWidth, view.RenderHeight, fps.Fps));
   }
   C<TextBlock>("LayoutInfo").Text = string.Join(Environment.NewLine, lines.ToArray());
   var map = C<Canvas>("MonitorMap");
   map.Children.Clear();
   int left = monitors.Min(m => m.X), top = monitors.Min(m => m.Y), right = monitors.Max(m => m.X + m.Width), bottom = monitors.Max(m => m.Y + m.Height);
   var frame = C<Border>("PreviewFrame");
   double extent = (double)(right - left) / (bottom - top);
   frame.Width = 360; frame.Height = Math.Max(96, Math.Min(132, 348 / extent + 12));
   map.Width = 348; map.Height = frame.Height - 12;
   double zoom = Math.Min(map.Width / (right - left), map.Height / (bottom - top)) * 0.94;
   double offsetX = (map.Width - (right - left) * zoom) / 2, offsetY = (map.Height - (bottom - top) * zoom) / 2;
   foreach (var monitor in monitors) {
    int owner = pair[0].Device == monitor.Device ? 0 : pair[1].Device == monitor.Device ? 1 : -1;
    double tileWidth = Math.Max(4, monitor.Width * zoom - 4), tileHeight = Math.Max(4, monitor.Height * zoom - 4);
    FrameworkElement tile;
    if (owner >= 0) {
     var cell = new Button { Style = (Style)Window.FindResource("PreviewCell"), Background = Paint(owner == 0 ? "#A629554C" : "#A6504631"), IsEnabled = appearanceEnabled };
     int turned = owner;
     cell.Click += (s, e) => TurnScreen(turned);
     TipTurn(cell, owner);
     cell.Tag = new KeyValuePair<int, string>(owner, Tr(owner == 0 ? "P1" : "P2"));
     cell.Content = TileContent(owner, ((KeyValuePair<int, string>)cell.Tag).Value);
     tile = cell;
    } else {
     tile = new Border { CornerRadius = new CornerRadius(3), Background = Paint("#40303B42"), BorderBrush = Paint("#364E5659"), BorderThickness = new Thickness(1),
      Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(3), Child = new TextBlock { Text = ScreenLayout.DisplayNumber(monitor.Device).ToString(), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Paint("#78939F") } } };
    }
    tile.Width = tileWidth; tile.Height = tileHeight;
    Canvas.SetLeft(tile, offsetX + (monitor.X - left) * zoom + 2); Canvas.SetTop(tile, offsetY + (monitor.Y - top) * zoom + 2);
    map.Children.Add(tile);
   }
  }

  // Each Cemu window is placed once when it first appears, so moving it by hand is not undone.
  void PlaceWindows() {
   if (displayWidth < 320 || displayHeight < 240) return;
   var areas = (sessionPlan ?? CurrentPlan()).Players;
   string[] folders = { "\\cemuA\\", "\\cemuB\\" };
   for (int i = 0; i < areas.Length; i++) {
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
    RefreshMonitors();
    var plan = CurrentPlan();
    sessionPlan = plan;
    string[] names = { "cemuA", "cemuB" };
    var quality = CurrentQuality();
    var gameLanguage = GameLanguages.Find(gameLanguages, language);
    string api = GraphicsMod.ResolveApi(advanced.Api, vulkanAvailable);
    Write("Graphics: " + (pair == null ? "render " + GraphicsMod.ScaleLabel(quality.Scale) + ", " : "") + "shadows " + quality.Shadows + ", shadow distance " + quality.ShadowDistance + ", anti-aliasing " + quality.AntiAliasing + ".");
    Write("Cemu: " + api + (advanced.Api == GraphicsMod.AutoApi ? " (automatic)" : "") + ", VSync " + (advanced.VSync ? "on" : "off") + ", async shader compile " + (advanced.AsyncCompile ? "on" : "off")
     + ", upscale " + GraphicsMod.Filters[advanced.Upscale] + ", downscale " + GraphicsMod.Filters[advanced.Downscale] + (api == GraphicsMod.VulkanApi ? ", accurate barriers " + (advanced.AccurateBarriers ? "on" : "off") : "") + ".");
    Write("Game language: " + gameLanguage.Name + " (Cemu console language " + gameLanguage.Console + ").");
    bool turned = plan.Players.Any(v => v.Rotation != 0);
    string reshade = Path.Combine(root, ".tools", "reshade");
    if (turned && api != GraphicsMod.VulkanApi) { Fail("Turning a screen needs Vulkan. Choose Vulkan in the graphics settings."); return false; }
    if (turned && !RotationMod.LayerReady(reshade)) { Fail("Turning a screen needs ReShade. Copy ReShade64.dll and ReShade64.json to {0}.", reshade); return false; }
    for (int i = 0; i < plan.Players.Length; i++) {
     var view = plan.Players[i];
     var fps = CurrentFps(i);
     CemuDisplay.Apply(Path.Combine(root, ".tools", "instances", names[i]), view, fps == null ? null : fps.Preset, quality, gameLanguage.Console, advanced, vulkanAvailable);
     RotationMod.Apply(Path.Combine(root, ".tools", "instances", names[i]), view.Rotation, reshade);
     if (view.Rotation != 0) Write("Player " + (i + 1) + ": the screen is turned " + view.Rotation + " degrees clockwise with ReShade.");
     Write("Player " + (i + 1) + ": " + view.RenderWidth + " x " + view.RenderHeight + " (aspect " + view.RatioWidth + ":" + view.RatioHeight + ")"
      + (pair == null ? "" : " on display " + ScreenLayout.DisplayNumber(pair[i].Device) + " at " + pair[i].Hz + " Hz, render " + GraphicsMod.ScaleLabel(scales[i])) + (fps == null ? "." : ", " + fps.Fps + " FPS limit."));
    }
    return true;
   }
   catch (Exception error) {
    Fail("The display settings could not be prepared: {0}", error.Message);
    return false;
   }
  }

  // Runs at session start, before any Cemu opens, on a worker thread because the first copy is about 1.2 GB; the session goes on when it is done.
  // A problem never stops the session: the games just keep their own caches.
  void PrepareShaderCache(Action next) {
   shaderProblem = null;
   string instances = Path.Combine(root, ".tools", "instances");
   string owner = Path.Combine(instances, "cemuA", "shaderCache"), follower = Path.Combine(instances, "cemuB", "shaderCache"), log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shader-cache.log");
   bool on = sharedShaders;
   seeding = true;
   SetButtons();
   SetStatus(ShaderCache.WillCopy(on, owner, follower) ? "Copying the shader cache for player 2 (first time only)..." : "Starting...");
   ThreadPool.QueueUserWorkItem(state => {
    var outcome = ShaderCache.Prepare(on, owner, follower, log);
    Window.Dispatcher.BeginInvoke(new Action(() => {
     seeding = false;
     shaderProblem = outcome.Problem;
     Write("Shared shader cache: " + (on ? "on" : "off") + (outcome.Seeded ? "; player 2's cache was copied from player 1's" : "") + (outcome.Problem == null ? "." : "; it failed (" + outcome.Problem + "), so each player keeps an independent cache. See shader-cache.log."));
     next();
    }));
   });
  }

  // The log keeps English; the status line follows the chosen language.
  void Fail(string english, params object[] args) {
   failure = string.Format(english, args);
   SetStatus(english, args);
   Write(failure);
  }

  void Write(string text) {
   var log = C<TextBox>("Log");
   log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
   log.ScrollToEnd();
  }

  void Step(string name, string english, Brush color) {
   steps[name] = new KeyValuePair<string, Brush>(english, color);
   C<TextBlock>(name + "Text").Text = Tr(english);
   C<System.Windows.Shapes.Shape>(name + "Dot").Fill = color;
  }

  void ApplyPlayers() {
   for (int i = 0; i < playerCount; i++) {
    var character = Characters.Find(characters, models[i]);
    names[i] = Characters.CleanName(C<TextBox>("P" + (i + 1) + "NameBox").Text, Characters.DefaultNames[i]);
    string bcml = Path.Combine(root, ".tools", "localappdata", i == 0 ? "bcml" : "bcml-b", "settings.json");
    try {
     string file = MilkBarSettings.Find(MilkBarSettings.UserRoot, bcml);
     if (file == null) { Write("Player " + (i + 1) + ": no Milk Bar settings file was found, so the name and model are not applied."); continue; }
     MilkBarSettings.Apply(file, names[i], character);
     Write("Player " + (i + 1) + ": " + names[i] + " as " + character.Display + ".");
    }
    catch (Exception error) { Write("Player " + (i + 1) + ": the name and model could not be applied: " + error.Message); }
   }
   SavePrefs();
  }

  void StartSession() {
   if (setupActive || seeding) return;
   failure = null; lastError = null; shaderProblem = null; joined[0] = joined[1] = false;
   if (Scripts.Owned(root).Count != 0) { BeginSession(); return; }
   if (!PrepareDisplay()) return;
   PrepareShaderCache(BeginSession);
  }

  void BeginSession() {
   SetStatus("Starting...");
   if (shaderProblem != null) SetStatus("Shared shader cache is off for this session: {0}", shaderProblem);
   SetPrompt("");
   ApplyPlayers();
   SaveRules(true);
   appliedSignature = Signature();
   Write("World rules: " + rules.Name + ", " + ServerRules.SpecialNames[rules.Special] + ".");
   ShowNameTags(true);
   Step("Server", "Starting", Busy); Step("A", "Waiting", Idle); Step("B", playerCount == 2 ? "Waiting" : "Stopped", Idle);
   Write(playerCount == 1 ? "Starting a one player session." : "Starting the session.");
   try {
    serverHost.Start(Path.Combine(root, ".artifacts", "extract", "milkbar", "DedicatedServer"));
   }
   catch (Exception error) {
    Fail("The dedicated server could not start: {0}", error.Message);
    Step("Server", "Stopped", Bad);
    return;
   }
   script = Scripts.Run(root, "Start-Online.ps1", "-InputMode Routed -ExternalRouter -ExternalServer -PlayerCount " + playerCount, Line, Error, code => Window.Dispatcher.BeginInvoke(new Action(() => ScriptExited(code))));
   SetButtons();
  }

  void Line(string text) { Window.Dispatcher.BeginInvoke(new Action(() => Handle(text))); }
  void Error(string text) { Window.Dispatcher.BeginInvoke(new Action(() => { Write(text); if (lastError == null && text.Trim().Length > 0) lastError = text.Trim(); })); }

  void Handle(string text) {
   Write(text);
   Match match;
   if (text.StartsWith("Server started")) Step("Server", "Running", Good);
   else if ((match = Regex.Match(text, "^Player ([AB]): wait")).Success) {
    string name = match.Groups[1].Value;
    Step(name, "Waiting for Continue", Busy);
    SetPrompt(name == "A" ? playerCount == 1 ? "Cemu A: wait for the main menu, then press Continue within about a minute." : "Cemu A: wait for the main menu, then press Continue within about a minute. Player 2 starts when player 1 has finished loading." : "Cemu B: wait for the main menu, then press Continue. If player 1 is not in yet, press Continue there too.");
   }
   else if ((match = Regex.Match(text, "^(\\d) of (\\d) players joined")).Success) {
    // The shared log does not say which player joined, so the steps change only when everyone is in.
    if (match.Groups[1].Value == "1" && match.Groups[2].Value == "2") SetPrompt("One player joined. Press Continue in the other Cemu window.");
   }
   else if (text.StartsWith("All players are connected")) {
    joined[0] = true; joined[1] = playerCount == 2;
    Step("A", "Joined the server", Good);
    if (playerCount == 2) Step("B", "Joined the server", Good);
    SetPrompt(""); SetStatus("Session running.");
   }
  }

  void ScriptExited(int code) {
   script = null;
   if (code != 0) {
    failure = lastError ?? Tr("The start script failed.");
    SetStatus("Start failed: {0}", failure);
    SetPrompt("Use Stop session to close anything that was started, then try again.");
    Write("Start failed (exit code " + code + ").");
   }
   RefreshProcesses();
  }

  void StopSession() {
   stopping = true; SetButtons();
   SetStatus("Stopping...");
   SetPrompt("");
   StopScript();
   Scripts.Run(root, "Stop-Online.ps1", "", Line, Error, code => Window.Dispatcher.BeginInvoke(new Action(() => {
    stopping = false; failure = null; joined[0] = joined[1] = false; placed.Clear(); appliedSignature = null; sessionPlan = null; RefreshProcesses();
    if (!restartAfterStop) return;
    restartAfterStop = false;
    if (sessionLive) Write("The previous session did not close completely, so it was not restarted.");
    else StartSession();
   })));
  }

  // Everything a session writes before the games start. A change here only reaches the games after a restart; the launcher language and the live controls are not part of it.
  string Signature() {
   Func<double, string> size = v => GraphicsMod.NearestScale(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
   var parts = new List<object> { shadows, shadowDistance, antiAliasing, advanced.Api, advanced.VSync, advanced.AsyncCompile, advanced.AccurateBarriers, advanced.Upscale, advanced.Downscale, sharedShaders, language,
    rules.Special, rules.Enemies, rules.Quests, rules.Koroks, rules.Towers, rules.Shrines, rules.Locations, rules.DivineBeasts, pair != null, playerCount };
    if (playerCount == 2) parts.AddRange(new object[] { rotations[0], rotations[1] });
   for (int i = 0; i < playerCount; i++) parts.AddRange(new object[] { Characters.CleanName(C<TextBox>("P" + (i + 1) + "NameBox").Text, Characters.DefaultNames[i]), models[i] });
   if (pair != null) for (int i = 0; i < 2; i++) parts.AddRange(new object[] { displays[i], size(scales[i]), fpsLimits[i] });
   else parts.AddRange(new object[] { playerCount == 2 ? mode.ToString() : "", size(scale), fpsLimit });
   return string.Join("|", parts.Select(p => p == null ? "" : p.ToString()).ToArray());
  }

  // While the games run, Start becomes Restart as soon as an option that needs a restart differs from what the games were started with.
  void UpdateStart() {
   var start = C<Button>("Start");
   bool settled = !stopping && !seeding;
   if (sessionLive && settled && appliedSignature == null) appliedSignature = Signature();
   bool dirty = sessionLive && settled && RestartGate.Differs(appliedSignature, Signature());
   if (dirty != restartPending) {
    restartPending = dirty;
    if (dirty) SetPrompt(RestartPrompt);
    else if (promptText == RestartPrompt) SetPrompt("");
   }
   string label = Tr(dirty ? "RESTART SESSION" : "START SESSION");
   var startText = C<TextBlock>("StartText");
   if (startText.Text != label) startText.Text = label;
   start.IsEnabled = settled && (sessionLive ? dirty : script == null);
   C<UIElement>("StartIcon").Opacity = start.IsEnabled ? 1 : 0.4;
  }

  void StopScript() {
   var running = script;
   if (running == null) return;
   try { if (!running.HasExited) running.Kill(); } catch (Exception) { }
  }

  // Escape minimizes the launcher while a game is open or starting, and closes it otherwise.
  bool GameOpen() {
   return script != null || Scripts.Owned(root).Exists(path => path.EndsWith("Cemu.exe", StringComparison.OrdinalIgnoreCase));
  }

  void OnClosing(object sender, System.ComponentModel.CancelEventArgs e) {
   if (script == null && Scripts.Owned(root).Count == 0) return;
   var answer = MessageBox.Show(Window, Tr("A session is still open.\n\nYes: stop the session and close.\nNo: close only the launcher. The game keeps running and the controllers fall back to Windows XInput.\nCancel: keep this window open."), "Hyrule Online", MessageBoxButton.YesNoCancel);
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
   if (width != displayWidth || height != displayHeight || hz != displayHz || MonitorKey(WindowPlacer.Monitors()) != monitorKey) UpdatePlan();
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
   C<TextBlock>("CornerServer").Text = Tr(server ? "Server running" : "Server stopped");
   if (script == null && !stopping && !seeding) {
    Step("Server", server ? "Running" : "Stopped", server ? Good : Idle);
    Step("A", a ? (joined[0] ? "Joined the server" : "Game open") : "Stopped", a ? (joined[0] ? Good : Busy) : Idle);
    Step("B", b ? (joined[1] ? "Joined the server" : "Game open") : "Stopped", b ? (joined[1] ? Good : Busy) : Idle);
    if (owned.Count > 0 && failure == null) SetStatus("Session running.");
    else if (owned.Count == 0 && failure == null) { SetStatus("Ready."); SetPrompt(""); }
   }
   sessionLive = owned.Count > 0;
   gameRunning = a || b;
   C<Button>("ReturnGame").Visibility = gameRunning ? Visibility.Visible : Visibility.Collapsed;
   if (!sessionLive && script == null && !stopping) { appliedSignature = null; sessionPlan = null; }
   UpdateStart();
   C<Button>("Stop").IsEnabled = !stopping && (script != null || owned.Count > 0);
   EnableAppearance(!stopping && !seeding && (script == null || sessionLive));
   if (serverUp != serverHost.Running) { serverUp = serverHost.Running; UpdateLive(); }
  }

  bool seeding;
  bool appearanceEnabled = true;

  void EnableAppearance(bool enabled) {
   appearanceEnabled = enabled;
   foreach (var name in new[] { "P1NameBox", "P2NameBox" }) C<TextBox>(name).IsEnabled = enabled;
   foreach (var name in new[] { "RulesBox", "SpecialBox" }) C<ComboBox>(name).IsEnabled = enabled;
   foreach (var name in new[] { "P1AvatarButton",    "P2AvatarButton", "Mode", "UseRecommendedRules" }) C<Button>(name).IsEnabled = enabled;
   foreach (var name in new[] { "View1", "View2" }) C<Button>(name).IsEnabled = enabled && playerCount == 2;
   C<Button>("ResetTurns").IsEnabled = enabled && (rotations[0] != 0 || rotations[1] != 0);
   foreach (var tile in C<Canvas>("MonitorMap").Children.OfType<Button>()) tile.IsEnabled = enabled;
   C<UIElement>("ModeIcon").Opacity = enabled ? 1 : 0.4;
   if (!enabled) ClosePickers();
   foreach (var name in RuleBoxes) C<CheckBox>(name).IsEnabled = enabled;
  }

  void SetButtons() {
   C<Button>("Start").IsEnabled = false;
   C<Button>("Stop").IsEnabled = !stopping;
   EnableAppearance(false);
  }

  void Tick() {
   var snapshot = router.Snapshot();
   if (capturing) CaptureStep(snapshot);
   UseShortcut(snapshot);
   NavigateWithPads(snapshot);
   ShowPadLayout(snapshot);
   DrivePads(snapshot);
   for (int player = 0; player < 2; player++) {
    string key = "P" + (player + 1);
    int slot = snapshot.Routes[player];
    bool present = slot >= 0 && snapshot.Names[slot] != null;
    var pad = present ? snapshot.Pads[slot] : new PadState();
    C<TextBlock>(key + "Name").Text = present ? snapshot.Names[slot] : Tr("No controller detected");
    C<TextBlock>(key + "Status").Text = snapshot.Assigning == player ? Tr("Press the confirm button on a controller to assign it.") : present ? Tr("Connected") : Tr("Waiting for a controller");
    C<Button>("Assign" + (player + 1)).Content = Tr(snapshot.Assigning == player ? "Cancel assignment" : "Assign controller");
    Move(C<FrameworkElement>(key + "L"), pad.lx, pad.ly, 20);
    Move(C<FrameworkElement>(key + "R"), pad.rx, pad.ry, 111);
    C<ProgressBar>(key + "LT").Value = pad.lt;
    C<ProgressBar>(key + "RT").Value = pad.rt;
    DrawMotion(key, present ? snapshot.Motion[slot] : new MotionReading());
   }
   if (++ticks % 60 == 0) RefreshProcesses();
   else if (ticks % 5 == 0) UpdateStart();
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
