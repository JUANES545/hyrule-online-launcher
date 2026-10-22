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
  readonly string[] names = (string[])Characters.DefaultNames.Clone();
  readonly string[] models = { "", "" };
  ServerRules rules = new ServerRules();
  List<ServerRules> presets = new List<ServerRules>();
  static readonly string[] RuleBoxes = { "RuleEnemies", "RuleQuests", "RuleKoroks", "RuleTowers", "RuleShrines", "RuleLocations", "RuleDivineBeasts" };
  static readonly string[] LiveControls = { "SetTime", "SetWeather", "WeatherBox", "TimeBox", "LiveNameTags", "PauseSync", "ResumeSync", "BringP1", "BringP2" };
  List<Character> characters = Characters.Parse(null);
  int fpsLimit = FpsMod.AutoFps, displayWidth, displayHeight, displayHz;
  Process script;
  string lastError, failure;
  bool stopping;
  int ticks;
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
   gameLanguages = GameLanguages.Scan(GameLanguages.ContentFolder(Path.Combine(root, ".tools", "localappdata", "bcml", "settings.json")));
   language = GameLanguages.Find(gameLanguages, language).Code;
   LoadRules();
   for (int i = 0; i < 2; i++) WatchPlayer(i);
   WatchWorld();
   ApplyLanguage();
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
   C<Grid>("MultiplayerPage").ColumnDefinitions[1].Width = fit.Stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
   Grid.SetColumn(C<StackPanel>("LivePanel"), fit.Stacked ? 0 : 1); Grid.SetRow(C<StackPanel>("LivePanel"), fit.Stacked ? 1 : 0);
   C<StackPanel>("RulesPanel").Margin = fit.Stacked ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 14, 0);
   C<StackPanel>("LivePanel").Margin = fit.Stacked ? new Thickness(0) : new Thickness(14, 0, 0, 0);
  }

  static readonly string[] PageNames = { "Play", "Graphics", "Multiplayer", "Language", "Details" };
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
   foreach (var name in new[] { "FpsBox", "ScaleBox", "ShadowsBox", "ShadowDistanceBox", "AntiAliasingBox", "P1ModelBox", "P2ModelBox", "RulesBox", "SpecialBox", "WeatherBox" }) if (C<ComboBox>(name).IsDropDownOpen) return true;
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
    for (int i = 0; i < 2; i++) {
     names[i] = Characters.CleanName((string)root.Element("Name" + (i + 1)), Characters.DefaultNames[i]);
     models[i] = (string)root.Element("Model" + (i + 1)) ?? "";
    }
    language = Saved(root, "Language") ?? "en";
    launcherLanguage = Lang.Ui(Saved(root, "LauncherLanguage") ?? "en");
   }
   catch (Exception) { mode = LayoutMode.Auto; scale = GraphicsMod.DefaultScale; shadows = shadowDistance = antiAliasing = null; fpsLimit = FpsMod.AutoFps; language = launcherLanguage = "en"; }
  }

  // The name and model of each player sit on that player's card and are written into its Milk Bar settings when a session starts.
  void WatchPlayer(int player) {
   var nameBox = C<TextBox>("P" + (player + 1) + "NameBox");
   var modelBox = C<ComboBox>("P" + (player + 1) + "ModelBox");
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
   modelBox.SelectionChanged += (s, e) => {
    var item = modelBox.SelectedItem as ComboBoxItem;
    if (filling || item == null) return;
    models[player] = (string)item.Tag;
    SavePrefs();
   };
  }

  void FillModels(int player) {
   var modelBox = C<ComboBox>("P" + (player + 1) + "ModelBox");
   filling = true;
   try {
    modelBox.Items.Clear();
    foreach (var character in characters) modelBox.Items.Add(new ComboBoxItem { Content = character.Label, Tag = character.Model });
    modelBox.SelectedIndex = characters.IndexOf(Characters.Find(characters, models[player]));
   }
   finally { filling = false; }
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

  void SaveRules() {
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
   FillModels(0); FillModels(1);
   ShowRules(); UpdateLive();
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

  static string Saved(XElement root, string name) { string value = (string)root.Element(name); return string.IsNullOrEmpty(value) ? null : value; }

  void SavePrefs() {
   try {
    new XDocument(new XElement("Preferences", new XElement("Layout", mode), new XElement("RenderScale", GraphicsMod.NearestScale(scale).ToString(System.Globalization.CultureInfo.InvariantCulture)),
     new XElement("Shadows", shadows ?? ""), new XElement("ShadowDistance", shadowDistance ?? ""), new XElement("AntiAliasing", antiAliasing ?? ""),
     new XElement("FpsLimit", fpsLimit == FpsMod.AutoFps ? "Auto" : fpsLimit.ToString()),
     new XElement("Name1", names[0]), new XElement("Model1", models[0]), new XElement("Name2", names[1]), new XElement("Model2", models[1]),
     new XElement("Language", language), new XElement("LauncherLanguage", launcherLanguage))).Save(PrefsFile);
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
    foreach (double option in GraphicsMod.Scales) scaleBox.Items.Add(new ComboBoxItem { Content = GraphicsMod.ScaleLabel(option, text => Tr(text)), Tag = option });
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
   if (choices.Count == 0) { box.Items.Add(new ComboBoxItem { Content = Tr("Not available") }); box.SelectedIndex = 0; return; }
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
    if (current == null) { box.Items.Add(new ComboBoxItem { Content = Tr("FPS++ is not installed") }); box.SelectedIndex = 0; return; }
    box.Items.Add(new ComboBoxItem { Content = Tr("FPS target: Auto · {0} FPS", FpsMod.Best(choices, displayHz).Fps), Tag = FpsMod.AutoFps });
    foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = choice.Fps + " FPS", Tag = choice.Fps });
    box.SelectedIndex = fpsLimit == FpsMod.AutoFps ? 0 : choices.FindIndex(l => l.Fps == current.Fps) + 1;
   }
   finally { filling = false; }
  }
  void UpdatePlan() {
   WindowPlacer.PrimaryDisplay(out displayWidth, out displayHeight, out displayHz);
   C<TextBlock>("ScreenInfo").Text = displayWidth + " × " + displayHeight + "  /  " + displayHz + " Hz";
   C<Button>("Layout").Content = Tr(mode == LayoutMode.Auto ? "Split: automatic" : mode == LayoutMode.SideBySide ? "Split: side by side" : "Split: top and bottom");
   ShowFpsChoices();
   ShowQuality();
   if (displayWidth < 320 || displayHeight < 240) { C<TextBlock>("LayoutInfo").Text = Tr("The primary display is not ready."); return; }
   var plan = ScreenLayout.Plan(mode, scale, displayWidth, displayHeight);
   bool side = plan.Layout == LayoutMode.SideBySide;
   var first = plan.Players[0];
   var shownFps = CurrentFps();
   C<TextBlock>("LayoutInfo").Text = Tr(side ? "Side by side · P1 left / P2 right" : "Top and bottom · P1 top / P2 bottom") + Environment.NewLine + Tr("Each game renders {0} × {1}", first.RenderWidth, first.RenderHeight) + (shownFps == null ? "" : " " + Tr("at {0} FPS", shownFps.Fps));
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
    var gameLanguage = GameLanguages.Find(gameLanguages, language);
    Write("Graphics: render " + GraphicsMod.ScaleLabel(quality.Scale) + ", shadows " + quality.Shadows + ", shadow distance " + quality.ShadowDistance + ", anti-aliasing " + quality.AntiAliasing + ".");
    Write("Game language: " + gameLanguage.Name + " (Cemu console language " + gameLanguage.Console + ").");
    for (int i = 0; i < 2; i++) {
     var view = plan.Players[i];
     var fps = CurrentFps();
     CemuDisplay.Apply(Path.Combine(root, ".tools", "instances", names[i]), view, fps == null ? null : fps.Preset, quality, gameLanguage.Console);
     Write("Player " + (i + 1) + ": " + view.RenderWidth + " x " + view.RenderHeight + " (aspect " + view.RatioWidth + ":" + view.RatioHeight + ")" + (fps == null ? "." : ", " + fps.Fps + " FPS limit."));
    }
    return true;
   }
   catch (Exception error) {
    Fail("The display settings could not be prepared: {0}", error.Message);
    return false;
   }
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
   for (int i = 0; i < 2; i++) {
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
   failure = null; lastError = null; joined[0] = joined[1] = false;
   if (Scripts.Owned(root).Count == 0 && !PrepareDisplay()) return;
   SetStatus("Starting...");
   SetPrompt("");
   ApplyPlayers();
   SaveRules();
   Write("World rules: " + rules.Name + ", " + ServerRules.SpecialNames[rules.Special] + ".");
   ShowNameTags(true);
   Step("Server", "Starting", Busy); Step("A", "Waiting", Idle); Step("B", "Waiting", Idle);
   Write("Starting the session.");
   try {
    serverHost.Start(Path.Combine(root, ".artifacts", "extract", "milkbar", "DedicatedServer"));
   }
   catch (Exception error) {
    Fail("The dedicated server could not start: {0}", error.Message);
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
   Match match;
   if (text.StartsWith("Server started")) Step("Server", "Running", Good);
   else if ((match = Regex.Match(text, "^Player ([AB]): wait")).Success) {
    string name = match.Groups[1].Value;
    Step(name, "Waiting for Continue", Busy);
    SetPrompt(name == "A" ? "Cemu A: wait for the main menu, then press Continue within about a minute. Player 2 starts when player 1 has finished loading." : "Cemu B: wait for the main menu, then press Continue. If player 1 is not in yet, press Continue there too.");
   }
   else if ((match = Regex.Match(text, "^(\\d) of 2 players joined")).Success) {
    // The shared log does not say which player joined, so the steps change only when both are in.
    if (match.Groups[1].Value == "1") SetPrompt("One player joined. Press Continue in the other Cemu window.");
   }
   else if (text.StartsWith("Both players are connected")) {
    joined[0] = joined[1] = true;
    Step("A", "Joined the server", Good); Step("B", "Joined the server", Good);
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
   C<TextBlock>("CornerServer").Text = Tr(server ? "Server running" : "Server stopped");
   if (script == null && !stopping) {
    Step("Server", server ? "Running" : "Stopped", server ? Good : Idle);
    Step("A", a ? (joined[0] ? "Joined the server" : "Game open") : "Stopped", a ? (joined[0] ? Good : Busy) : Idle);
    Step("B", b ? (joined[1] ? "Joined the server" : "Game open") : "Stopped", b ? (joined[1] ? Good : Busy) : Idle);
    if (owned.Count > 0 && failure == null) SetStatus("Session running.");
    else if (owned.Count == 0 && failure == null) { SetStatus("Ready."); SetPrompt(""); }
   }
   C<Button>("Start").IsEnabled = script == null && !stopping && owned.Count == 0;
   C<Button>("Stop").IsEnabled = !stopping && (script != null || owned.Count > 0);
   EnableAppearance(script == null && !stopping && owned.Count == 0);
   if (serverUp != serverHost.Running) { serverUp = serverHost.Running; UpdateLive(); }
  }

  void EnableAppearance(bool enabled) {
   foreach (var name in new[] { "P1NameBox", "P2NameBox" }) C<TextBox>(name).IsEnabled = enabled;
   foreach (var name in new[] { "P1ModelBox", "P2ModelBox", "RulesBox", "SpecialBox" }) C<ComboBox>(name).IsEnabled = enabled;
   foreach (var name in RuleBoxes) C<CheckBox>(name).IsEnabled = enabled;
  }

  void SetButtons() {
   C<Button>("Start").IsEnabled = false;
   C<Button>("Stop").IsEnabled = !stopping;
   EnableAppearance(false);
  }

  void Tick() {
   var snapshot = router.Snapshot();
   NavigateWithPads(snapshot);
   for (int player = 0; player < 2; player++) {
    string key = "P" + (player + 1);
    int slot = snapshot.Routes[player];
    bool present = slot >= 0 && snapshot.Names[slot] != null;
    var pad = present ? snapshot.Pads[slot] : new PadState();
    C<TextBlock>(key + "Name").Text = present ? snapshot.Names[slot] : Tr("No controller detected");
    C<TextBlock>(key + "Status").Text = snapshot.Assigning == player ? Tr("Press the bottom face button on a controller to assign it.") : present ? Tr("Connected") : Tr("Waiting for a controller");
    C<Button>("Assign" + (player + 1)).Content = Tr(snapshot.Assigning == player ? "Cancel assignment" : "Assign controller");
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
