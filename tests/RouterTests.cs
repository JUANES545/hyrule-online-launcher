using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HyruleOnline {
 static class RouterTests {
  static string FpsRules {
   get {
    var text = new System.Text.StringBuilder("[Definition]\r\nname = FPS++\r\nversion = 6\r\n\r\n[Default]\r\n$fpsLimitNormal = 60\r\n\r\n[Preset]\r\nname = Normal Settings\r\ncategory = Mode\r\ndefault = 1\r\n$advancedMode:int = 0\r\n");
    foreach (int fps in new[] { 240, 165, 144, 120, 90, 75, 72, 60, 55, 45, 30, 20 })
     text.Append("\r\n[Preset]\r\nname = ").Append(fps).Append("FPS Limit").Append(fps == 60 ? " (Default)" : "").Append("\r\ncategory = FPS Limit\r\ncondition = $advancedMode == 0\r\n$fpsLimitNormal = ").Append(fps).Append("\r\n$keepVsync:int = 0\r\n");
    foreach (int fps in new[] { 244, 48 })
     text.Append("\r\n[Preset]\r\nname = ").Append(fps).Append("FPS (ideal)\r\ncategory = Framerate Limit\r\ncondition = $advancedMode == 1\r\n$fpsLimitAdvanced = ").Append(fps).Append("\r\n");
    return text.ToString();
   }
  }

  const string QualityRules = "[Definition]\nname = Graphics\n\n[Preset]\nname = Low (50%)\ncategory = Shadows\n$shadowRes = 0.5\n\n[Preset]\nname = Medium (100%, Default)\ncategory = Shadows\ndefault = 1\n$shadowRes = 1\n\n[Preset]\nname = Extreme (400%, Unstable)\ncategory = Shadows\n$shadowRes = 4\n\n[Preset]\nname = Low (Low Draw Distance, Sharp Shadows)\ncategory = Shadow Draw Distance\n\n[Preset]\nname = High (Default)\ncategory = Shadow Draw Distance\ndefault = 1\n\n[Preset]\nname = Very High (Recommended)\ncategory = Shadow Draw Distance\n\n[Preset]\nname = None\ncategory = Anti-Aliasing\n\n[Preset]\nname = Normal FXAA (Default)\ncategory = Anti-Aliasing\ndefault = 1\n\n[Preset]\nname = 16:9 (Default)\ncategory = Aspect Ratio\ndefault = 1\n";

  static void Graphics() {
   var shadows = GraphicsMod.Parse(QualityRules, GraphicsMod.ShadowsCategory);
   Require(shadows.Count == 3 && shadows[0].Name == "Low (50%)" && shadows.Count(c => c.Default) == 1 && shadows[1].Default, "shadow presets are read in order with their default");
   var distance = GraphicsMod.Parse(QualityRules, GraphicsMod.ShadowDistanceCategory);
   Require(distance.Count == 3 && distance[0].Label == "Low" && distance[1].Label == "High (Default)" && distance[2].Label == "Very High (Recommended)", "long explanations are dropped from the labels");
   Require(shadows[2].Label == "Extreme (400%, Unstable)" && shadows[0].Label == "Low (50%)" && GraphicsMod.Label("None") == "None" && GraphicsMod.Label("Ultra (Higher Draw Distance, Slightly Blurrier Shadows)") == "Ultra", "labels keep percentages and flags");
   Require(GraphicsMod.Parse(QualityRules, "Aspect Ratio").Count == 1 && GraphicsMod.Parse(null, GraphicsMod.ShadowsCategory).Count == 0 && GraphicsMod.Parse("[Definition]", GraphicsMod.ShadowsCategory).Count == 0, "other categories and odd packs are handled");
   Require(GraphicsMod.Pick(shadows, "Extreme (400%, Unstable)").Name == "Extreme (400%, Unstable)" && GraphicsMod.Pick(shadows, null).Name == "Medium (100%, Default)" && GraphicsMod.Pick(shadows, "Gone").Default, "a missing preset falls back to the pack default");
   Require(GraphicsMod.Pick(new List<GraphicsMod.Choice>(), "x") == null, "no presets means no choice");
   Require(GraphicsMod.NearestScale(1.0) == 1.0 && GraphicsMod.NearestScale(1.4) == 1.5 && GraphicsMod.NearestScale(0) == 0.5 && GraphicsMod.NearestScale(9) == 2.0 && GraphicsMod.NearestScale(double.NaN) == 1.0, "a saved scale is brought to an offered one");
   Require(GraphicsMod.ScaleLabel(1.0) == "100% (window size)" && GraphicsMod.ScaleLabel(1.5) == "150% (sharper)" && GraphicsMod.ScaleLabel(0.75) == "75% (faster)", "scale labels");

   string temp = Path.Combine(Path.GetTempPath(), "HyruleOnlineQuality-" + Guid.NewGuid().ToString("N"));
   try {
    string instance = Path.Combine(temp, "cemu"), stockFolder = Path.Combine(instance, "graphicPacks", "downloadedGraphicPacks", "BreathOfTheWild", "Graphics");
    Directory.CreateDirectory(stockFolder);
    File.WriteAllText(Path.Combine(stockFolder, "rules.txt"), QualityRules.Replace("name = Graphics\n", "name = Graphics\npath = \"The Legend of Zelda: Breath of the Wild/Graphics\"\n"));
    string settingsFile = Path.Combine(instance, "settings.xml");
    File.WriteAllText(settingsFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<content>\n  <GraphicPack />\n</content>");
    Require(GraphicsMod.Read(instance, GraphicsMod.ShadowsCategory).Count == 3 && GraphicsMod.Read(temp, GraphicsMod.ShadowsCategory).Count == 0, "an instance's own pack is read, a missing one gives nothing");
    var view = ScreenLayout.Plan(LayoutMode.SideBySide, 1.5, 3840, 2160).Players[0];
    var quality = new GraphicsMod.Quality { Shadows = "Extreme (400%, Unstable)", AntiAliasing = "None" };
    CemuDisplay.Apply(instance, view, null, quality);
    var presets = System.Xml.Linq.XDocument.Load(settingsFile).Root.Element("GraphicPack").Element("Entry").Elements("Preset").ToDictionary(p => (string)p.Element("category"), p => (string)p.Element("preset"));
    Require(presets.Count == 4 && presets["Shadows"] == "Extreme (400%, Unstable)" && presets["Anti-Aliasing"] == "None" && !presets.ContainsKey("Shadow Draw Distance") && presets["Resolution"] == CemuDisplay.ResolutionPreset, "chosen quality presets are written, the rest stay at the pack default");
    CemuDisplay.Apply(instance, view, null, new GraphicsMod.Quality());
    Require(System.Xml.Linq.XDocument.Load(settingsFile).Root.Element("GraphicPack").Element("Entry").Elements("Preset").Count() == 2, "a later session without choices clears the earlier ones");
    CemuDisplay.Apply(instance, view);
    Require(System.Xml.Linq.XDocument.Load(settingsFile).Root.Element("GraphicPack").Element("Entry").Elements("Preset").Count() == 2, "no quality given writes only the display presets");
   }
   finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
  }

  static void Motion() {
   const ulong time = 1000000000;
   var motion = new ControllerMotion();
   Require(motion.Read(1).Status == MotionStatus.Unavailable, "a pad without sensors is unavailable");
   Require(!motion.Accept(1, 1, 2, 3, 1, 1), "reports from a sensor the pad lacks are ignored");
   motion.Configure(true, true); Require(motion.Read(1).Status == MotionStatus.Waiting, "sensors without reports are waiting");
   Require(!motion.Accept(1, 0, 0, 0, time, time), "an all-zero acceleration is not a report");
   Require(motion.Accept(1, 0, 9.80665f, 0, time, time) && motion.Accept(2, 0, 0, 0, time, time), "sensor reports are accepted");
   var reading = motion.Read(time);
   Require(reading.Status == MotionStatus.Active && reading.AccelFresh && reading.GyroFresh && reading.TiltX == 0 && reading.TiltY == 0, "the first pose is the rest pose");
   Require(!motion.Accept(1, 1, 2, 3, time, time), "a repeated timestamp is rejected");
   Require(!motion.Accept(1, 1, 2, 3, 0, time) && !motion.Accept(1, 1, 2, 3, time + 1, time), "missing and future timestamps are rejected");
   Require(!motion.Accept(2, float.NaN, 0, 0, time + 1, time + 1) && !motion.Accept(1, 0, float.PositiveInfinity, 0, time + 1, time + 1), "non-finite reports are rejected");
   Require(!motion.Accept(1, 201, 0, 0, time + 1, time + 1) && !motion.Accept(2, 0, 101, 0, time + 1, time + 1), "implausible reports are rejected");
   Require(motion.Accept(1, 9.80665f, 0, 9.80665f, time + 10000000, time + 10000000) && motion.Accept(2, 0, 0, 2, time + 10000000, time + 10000000), "tilt and roll are accepted");
   reading = motion.Read(time + 10000000);
   Require(reading.TiltX > .99 && reading.TiltY < -.99 && reading.TiltZ > .99 && reading.Angle > 0 && reading.Activity > 0, "the reading follows three acceleration axes and the gyro");
   Require(motion.Accept(2, 0, 0, 100, time + 20000000, time + 20000000) && motion.Accept(2, 0, 0, 100, time + 70000000, time + 70000000) && motion.Accept(2, 0, 0, 100, time + 120000000, time + 120000000), "fast spin is accepted");
   Require(motion.Read(time + 120000000).Angle == 35, "the angle is limited to 35 degrees");
   reading = motion.Read(time + 120000000 + ControllerMotion.FreshNanoseconds + 1);
   Require(reading.Status == MotionStatus.Stale && !reading.AccelFresh && reading.TiltX == 0 && reading.Angle == 0, "old reports clear the reading");
   Require(motion.Accept(1, 3, 4, 5, time + 2000000000, time + 2000000000) && motion.Read(time + 2000000000).TiltX == 0, "resuming sets a new rest pose");
   motion.Configure(false, false); Require(motion.Read(time).Status == MotionStatus.Unavailable, "disconnecting clears the state");
  }

  static void Navigation() {
   int shells = 0;
   foreach (int width in new[] { 320, 640, 800, 1024, 1280, 1366, 1600, 1920, 2560, 3440, 3840, 5120, 7680 })
    foreach (int height in new[] { 240, 480, 600, 720, 768, 900, 1080, 1200, 1440, 1600, 2160, 3840 }) {
     double aspect = (double)width / height;
     var fit = ShellFit.For(width, height);
     string name = width + "x" + height;
     Require(fit.Stacked == (aspect < ShellFit.StackBelow), "stacking follows the window shape at " + name);
     Require(fit.Width >= (fit.Stacked ? ShellFit.StackWidth : ShellFit.MinWideWidth) && fit.Height >= (fit.Stacked ? ShellFit.StackHeight : ShellFit.WideHeight), "the canvas never gets smaller than the content at " + name);
     double scale = fit.Scale(width, height);
     Require(scale > 0 && fit.Width * scale <= width + 1e-6 && fit.Height * scale <= height + 1e-6, "the canvas fits inside the window at " + name);
     Require(Math.Abs(fit.Width * scale - width) < 1e-6 || Math.Abs(fit.Height * scale - height) < 1e-6, "the canvas fills one side of the window at " + name);
     if (!fit.Stacked && aspect * ShellFit.WideHeight <= ShellFit.MaxWideWidth || fit.Stacked && ShellFit.StackWidth / aspect >= ShellFit.StackHeight)
      Require(Math.Abs(fit.Width / fit.Height - aspect) < 1e-9, "the canvas follows the window shape at " + name);
     shells++;
    }
   var ultrawide = ShellFit.For(5120, 1440);
   Require(ultrawide.Width == ShellFit.MaxWideWidth && ultrawide.Height == ShellFit.WideHeight && !ultrawide.Stacked, "an ultrawide window keeps a centred canvas of limited width");
   Require(ShellFit.For(0, 0).Width > 0 && ShellFit.For(-5, 10).Height > 0, "an empty window still gets a canvas");
   Require(PageNav.Step(0, PageNav.RightBumper) == 1 && PageNav.Step(0, PageNav.LeftBumper) == -1, "RB goes forward and LB goes back");
   Require(PageNav.Step(PageNav.RightBumper, PageNav.RightBumper) == 0 && PageNav.Step(PageNav.LeftBumper, 0) == 0, "a held or released bumper does nothing");
   Require(PageNav.Step(PageNav.LeftBumper, (ushort)(PageNav.LeftBumper | PageNav.RightBumper)) == 1 && PageNav.Step(0, 0x1000) == 0, "only a new bumper press counts");
   Require(PageNav.Move(0, 1, 3) == 1 && PageNav.Move(2, 1, 3) == 0 && PageNav.Move(0, -1, 3) == 2 && PageNav.Move(1, -1, 3) == 0 && PageNav.Move(5, 0, 0) == 0, "pages wrap around");
   Console.WriteLine("PASS: window fitting for " + shells + " window sizes and page navigation.");
  }

  static void Players() {
   const string json = "[{\"Name\":\"Urbosa\",\"Folder\":\"Npc_Gerudo_Hero\"},{\"Name\":\"Revali\",\"Folder\":\"Npc_Rito_Hero\"},{\"Name\":\"Hudson\",\"Folder\":\"Npc_Hylia_Carpenter_001\"},{\"Name\":\"Hudson\",\"Folder\":\"Npc_Hylia_Carpenter_001_Wedding\"},{\"Name\":\"Hat\",\"Folder\":\"Armor_Hat\"}]";
   var list = Characters.Parse(json);
   Require(list.Count == 4 && list[0].IsLink && list[0].Model == Character.LinkModel && list[0].Index == 0, "Link comes first");
   Require(list[1].Index == 3 && list[1].Model == "Npc_Gerudo_Hero" && list[3].Index == 6, "NPC models follow the Milk Bar numbering, after Bumii and Environmental");
   Require(list[2].Label == "Hudson" && list[3].Label == "Hudson (Wedding)", "repeated names stay distinguishable");
   Require(!list[1].Experimental && list[1].Display == "Urbosa" && list[2].Experimental && list[2].Display == "Hudson" && !list[0].Experimental, "only tested models are flagged experimental and the list shows plain names");
   Require(!list.Any(c => c.Model == "Npc_Rito_Hero"), "a model that hangs the game is not offered");
   Require(Characters.Parse("").Count == 1 && Characters.Parse(null)[0].IsLink, "without the mod's list only Link is offered");
   Require(Characters.Find(list, "Npc_Gerudo_Hero") == list[1] && Characters.Find(list, "").IsLink && Characters.Find(list, "Npc_Rito_Hero").IsLink, "a saved model that is gone falls back to Link");
   Require(Characters.CleanName("Pedro", "x") == "Pedro" && Characters.CleanName("  a;b|c  d ", "x") == "abc d" && Characters.CleanName(";;", "Link") == "Link" && Characters.CleanName(null, "Link") == "Link", "names keep only safe characters");
   Require(Characters.CleanName("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "x").Length == Characters.MaxName && Characters.IsNameText("Player 1") && !Characters.IsNameText("a;b"), "names are limited in length");
   Require(Characters.DefaultNames[0] == "Link" && Characters.DefaultNames[1] == "Pedro", "default names");
   var link = Characters.ModelJson(list[0]);
   Require(link.Contains("\"ModelIndex\":0") && link.Contains("\"Type\":0") && link.Contains("\"IsArmorSync\":true"), "Link keeps armor sync");
   Require(Characters.ModelJson(list[1]).Contains("\"Model\":\"Npc_Gerudo_Hero\"") && Characters.ModelJson(list[1]).Contains("\"Type\":1") && Characters.ModelJson(list[1]).Contains("\"ModelIndex\":3"), "an NPC is a custom model");

   string temp = Path.Combine(Path.GetTempPath(), "hyrule-players-" + Guid.NewGuid().ToString("N"));
   try {
    string bcmlA = Path.Combine(temp, "bcml", "settings.json"), bcmlB = Path.Combine(temp, "bcml-b", "settings.json");
    Func<string, string, string> write = (folder, bcml) => {
     string file = Path.Combine(temp, "Milk_Bar", folder, "1.0.0.0", "user.config");
     Directory.CreateDirectory(Path.GetDirectoryName(file));
     File.WriteAllText(file, "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><userSettings><App.Settings>"
      + "<setting name=\"playerName\" serializeAs=\"String\"><value>Old</value></setting>"
      + "<setting name=\"serversAdded\" serializeAs=\"String\"><value>[{\"Name\":\"Local\"}]</value></setting>"
      + "<setting name=\"bcmlLocation\" serializeAs=\"String\"><value>" + bcml + "</value></setting>"
      + "<setting name=\"playAsModel\" serializeAs=\"String\"><value>False</value></setting></App.Settings></userSettings></configuration>");
     return file;
    };
    string fileA = write("hashA", bcmlA), fileB = write("hashB", bcmlB.ToUpperInvariant());
    Require(MilkBarSettings.Find(Path.Combine(temp, "Milk_Bar"), bcmlA) == fileA && MilkBarSettings.Find(Path.Combine(temp, "Milk_Bar"), bcmlB) == fileB, "each player finds its own settings file");
    Require(MilkBarSettings.Find(Path.Combine(temp, "Milk_Bar"), Path.Combine(temp, "other.json")) == null && MilkBarSettings.Find(Path.Combine(temp, "none"), bcmlA) == null, "an unknown player has no settings file");
    MilkBarSettings.Apply(fileA, "Pedro", list[1]);
    var text = File.ReadAllText(fileA);
    Func<string, string, string> read = (file, name) => System.Xml.Linq.XDocument.Load(file).Descendants("setting").First(e => (string)e.Attribute("name") == name).Value;
    Require(read(fileA, "playerName") == "Pedro" && read(fileA, "playAsModel") == "True" && read(fileA, "playerModel") == Characters.ModelJson(list[1]), "an NPC is written with play as model on");
    Require(read(fileA, "serversAdded") == "[{\"Name\":\"Local\"}]" && read(fileA, "bcmlLocation") == bcmlA, "the other settings are kept");
    Require(read(fileB, "playerName") == "Old", "the other player's file is untouched");
    MilkBarSettings.Apply(fileA, "Link", list[0]);
    Require(read(fileA, "playAsModel") == "False" && read(fileA, "playerName") == "Link" && System.Xml.Linq.XDocument.Load(fileA).Descendants("setting").Count(e => (string)e.Attribute("name") == "playerModel") == 1, "going back to Link turns play as model off and adds no duplicate");
   }
   finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
   Console.WriteLine("PASS: player names and models.");
  }

  static void Require(bool ok, string message) { if (!ok) throw new Exception("TEST: " + message); }

  // The text of every string literal inside the calls to the given methods.
  static List<string> CallLiterals(string source, params string[] calls) {
   var found = new List<string>();
   foreach (string call in calls) {
    int at = 0;
    while ((at = source.IndexOf(call + "(", at, StringComparison.Ordinal)) >= 0) {
     if (at > 0 && (char.IsLetterOrDigit(source[at - 1]) || source[at - 1] == '_' || source[at - 1] == '.')) { at += call.Length; continue; }
     int i = at + call.Length + 1, depth = 1;
     while (i < source.Length && depth > 0) {
      char c = source[i];
      if (c == '"') {
       var text = new System.Text.StringBuilder();
       i++;
       while (source[i] != '"') {
        if (source[i] == '\\') { char next = source[i + 1]; text.Append(next == 'n' ? '\n' : next); i += 2; }
        else text.Append(source[i++]);
       }
       found.Add(text.ToString());
      }
      else if (c == '(') depth++;
      else if (c == ')') depth--;
      i++;
     }
     at = i;
    }
   }
   return found;
  }

  static void ServerRulesTest() {
   const string ini = "\uFEFF[Connection]\r\nIP=100.1.2.3\r\nPort=5050\r\nPassword=secret\r\n\r\n[Gamemode]\r\n# keep me\r\nDefaultGamemode=False\r\n\r\n[DefaultGamemode]\r\n# rules\r\nName=Custom\r\nEnemySync=False\r\nQuestSync=True\r\n# special\r\nSpecial=0\r\n";
   var rules = ServerRules.Parse(ini);
   Require(rules.Quests && !rules.Enemies && !rules.Koroks && rules.Special == 0 && rules.Name == "Custom", "rules are read from the ini");
   rules.Enemies = true; rules.Koroks = true; rules.DivineBeasts = true; rules.Special = 2; rules.Name = "Death Swap";
   string text = rules.Apply(ini.TrimStart('\uFEFF'));
   var back = ServerRules.Parse(text);
   Require(back.SameRules(rules) && back.Name == "Death Swap", "written rules read back the same");
   Require(text.Contains("IP=100.1.2.3\r\n") && text.Contains("Password=secret") && text.Contains("# keep me") && text.Contains("# rules") && text.Contains("# special"), "connection settings and comments stay");
   Require(text.Contains("DefaultGamemode=True") && !text.Contains("DefaultGamemode=False") && text.Contains("DungeonSync=True") && text.Contains("TowerSync=False"), "missing keys are added and the prompt is turned off");
   Require(!text.Replace("\r\n", "").Contains("\n") && text.EndsWith("\r\n") && text.Split(new[] { "EnemySync" }, StringSplitOptions.None).Length == 2, "line endings stay and keys are not repeated");
   Require(new ServerRules().Apply("").Contains("[DefaultGamemode]") && ServerRules.Parse(new ServerRules().Apply("")).SameRules(new ServerRules()), "an empty ini gets the sections");

   const string json = "[{\"SettingsName\":\"Game Completion\",\"EnemySync\":true,\"GameMode\":\"NoGamemode\",\"QuestSyncSettings\":{\"Vanilla\":false,\"Koroks\":true,\"Towers\":true,\"Shrines\":true,\"Locations\":true,\"DivineBeast\":false}},"
    + "{\"SettingsName\":\"Hunter Vs Speedrunner\",\"EnemySync\":false,\"GameMode\":\"HunterVsSpeedrunner\",\"QuestSyncSettings\":{\"Vanilla\":true,\"Koroks\":false,\"Towers\":false,\"Shrines\":false,\"Locations\":false,\"DivineBeast\":true}},"
    + "{\"SettingsName\":\"Prop hunt\",\"EnemySync\":false,\"GameMode\":\"PropHunt\",\"QuestSyncSettings\":{}}]";
   var presets = ServerRules.Presets(json);
   Require(presets.Count == 2 && presets[0].Name == "Game Completion" && presets[0].Enemies && presets[0].Koroks && presets[0].Locations && !presets[0].Quests && !presets[0].DivineBeasts && presets[0].Special == 0, "presets are read");
   Require(presets[1].Special == 1 && presets[1].Quests && presets[1].DivineBeasts && !presets[1].Towers, "a special mode preset");
   Require(ServerRules.Match(presets, presets[0].Copy()) == presets[0] && ServerRules.Match(presets, new ServerRules()) == null && ServerRules.Presets("").Count == 0, "a matching mix is a preset");

   Require(ServerCommands.Time("7:05") == "Time Set 07:05" && ServerCommands.Time(" 23:59 ") == "Time Set 23:59" && ServerCommands.Time("24:00") == null && ServerCommands.Time("12:60") == null && ServerCommands.Time("noon") == null && ServerCommands.Time(null) == null, "time text");
   Require(ServerCommands.SetWeather("Rain") == "Weather Set Rain" && ServerCommands.Bring("Link", "Pedro") == "Teleport Link Pedro" && ServerCommands.Bring("Sir Link", "Pedro") == "Teleport \"Sir Link\" Pedro", "weather and teleport commands");
   Require(ServerCommands.NameTags("Activated Name Tags") == true && ServerCommands.NameTags("Deactivated Name Tags") == false && ServerCommands.NameTags("Server started") == null, "name tag lines");

   string temp = Path.Combine(Path.GetTempPath(), "hyrule-rules-" + Guid.NewGuid().ToString("N"));
   try {
    Directory.CreateDirectory(temp);
    string file = Path.Combine(temp, "ServerConfig.ini");
    File.WriteAllText(file, ini.TrimStart('\uFEFF'), new System.Text.UTF8Encoding(true));
    rules.Save(file);
    var bytes = File.ReadAllBytes(file);
    Require(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && bytes[3] == (byte)'[' && ServerRules.Load(file).SameRules(rules), "the file keeps its byte order mark");
    Require(new ServerRules().SameRules(ServerRules.Load(Path.Combine(temp, "missing.ini"))), "a missing file gives default rules");
   }
   finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
   Console.WriteLine("PASS: server rules and commands.");
  }

  static void Languages(string root) {
   var table = Lang.Table;
   Require(table.Select(r => r[0]).Distinct().Count() == table.Length, "every English text appears once in the table");
   Func<string, string> holes = text => string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, "\\{\\d+\\}").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).OrderBy(v => v));
   foreach (var row in table) {
    Require(row.Length == Lang.Supported.Length && row.All(t => !string.IsNullOrWhiteSpace(t)), "every language has text for: " + row[0]);
    Require(row.All(t => holes(t) == holes(row[0])), "the same placeholders in every language: " + row[0]);
    Require(row.All(t => t.Split('\n').Length == row[0].Split('\n').Length), "the same line breaks in every language: " + row[0]);
   }
   Require(Lang.HelpText.Length == Lang.Supported.Length && Lang.HelpText.All(t => t.Split(new[] { "\n\n" }, StringSplitOptions.None).Length == Lang.HelpText[0].Split(new[] { "\n\n" }, StringSplitOptions.None).Length), "the help text has the same sections in every language");

   try {
    Lang.Current = "es";
    Require(Lang.T("PLAY") == "JUGAR" && Lang.T("Unknown text") == "Unknown text" && Lang.T("Each game renders {0} × {1}", 1920, 1080) == "Cada juego se renderiza a 1920 × 1080", "Spanish text, English fallback and placeholders");
    Require(GraphicsMod.ScaleLabel(1.5, t => Lang.T(t)) == "150% (más nítido)" && GraphicsMod.ScaleLabel(1.0) == "100% (window size)", "render scale labels follow the language");
    Lang.Current = "fr";
    Require(Lang.T("PLAY") == "JOUER" && Lang.Help().StartsWith("SESSION :"), "French text");
    Lang.Current = Lang.Ui("de");
    Require(Lang.Current == "en" && Lang.T("PLAY") == "PLAY" && Lang.Help().StartsWith("SESSION: Start"), "a language without a translation shows English");
   }
   finally { Lang.Current = "en"; }

   // Nothing the launcher writes may be missing from the table.
   var known = new HashSet<string>(table.Select(r => r[0]).Concat(Lang.Neutral));
   var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", "Launcher", "Launcher.xaml"));
   System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
   foreach (var element in xaml.Descendants()) {
    if (element.AncestorsAndSelf().Any(e => e.Name.LocalName == "Window.Resources" || Lang.Dynamic.Contains((string)e.Attribute(x + "Name")))) continue;
    foreach (string attribute in new[] { "Text", "Content", "ToolTip", "AutomationProperties.Name" }) {
     string value = (string)element.Attribute(attribute);
     if (string.IsNullOrEmpty(value) || value.StartsWith("{")) continue;
     Require(known.Contains(value), "the XAML text is translated: " + value);
    }
   }
   string code = File.ReadAllText(Path.Combine(root, "src", "Launcher", "Launcher.cs"));
   var skipped = new[] { "", "Server", "A", "B" };
   var literals = CallLiterals(code, "Tr", "Fail", "SetStatus", "SetPrompt", "Step").Where(t => !skipped.Contains(t)).ToList();
   Require(literals.Count > 40, "the launcher code uses the translation helpers");
   foreach (string literal in literals) Require(known.Contains(literal), "the launcher text is translated: " + literal);

   string temp = Path.Combine(Path.GetTempPath(), "hyrule-lang-" + Guid.NewGuid().ToString("N"));
   try {
    string content = Path.Combine(temp, "content");
    Directory.CreateDirectory(Path.Combine(content, "Pack"));
    foreach (string pack in new[] { "Bootup_USfr.pack", "Bootup_USen.pack", "Bootup_USes.pack", "Bootup.pack", "Bootup_Graphics.pack", "Bootup_EUen.pack", "Bootup_USxx1.pack" }) File.WriteAllText(Path.Combine(content, "Pack", pack), "x");
    var found = GameLanguages.Scan(content);
    Require(string.Join(",", found.Select(l => l.Code)) == "en,es,fr", "the languages come from the game's Bootup packs, English first");
    Require(found[0].Name == "English" && found[0].Console == 1 && found[1].Name == "Español (Latinoamérica)" && found[1].Console == 5 && found[2].Name == "Français (Canada)" && found[2].Console == 2, "native names and Cemu console languages");
    Require(GameLanguages.Find(found, "fr") == found[2] && GameLanguages.Find(found, "de") == found[0], "an unknown language falls back to the first one");

    string europe = Path.Combine(temp, "europe");
    Directory.CreateDirectory(Path.Combine(europe, "Pack"));
    foreach (string pack in new[] { "Bootup_EUru.pack", "Bootup_EUde.pack", "Bootup_EUit.pack", "Bootup_EUnl.pack", "Bootup_EUes.pack" }) File.WriteAllText(Path.Combine(europe, "Pack", pack), "x");
    var other = GameLanguages.Scan(europe);
    Require(string.Join(",", other.Select(l => l.Code)) == "de,es,it,nl,ru" && other.Select(l => l.Console).SequenceEqual(new[] { 3, 5, 4, 8, 10 }) && other[1].Name == "Español (España)", "other regions map to their console languages");
    Require(Lang.Ui("es") == "es" && Lang.Ui("de") == "en", "the launcher has Spanish and French; other languages use English");
    string asia = Path.Combine(temp, "asia");
    Directory.CreateDirectory(Path.Combine(asia, "Pack"));
    File.WriteAllText(Path.Combine(asia, "Pack", "Bootup_TWzh.pack"), "x"); File.WriteAllText(Path.Combine(asia, "Pack", "Bootup_JPja.pack"), "x");
    Require(GameLanguages.Scan(asia).Select(l => l.Console).SequenceEqual(new[] { 0, 11 }), "Japanese and Traditional Chinese console languages");
    Require(GameLanguages.Scan(null).Count == 1 && GameLanguages.Scan(null)[0].Code == "en" && GameLanguages.Scan(Path.Combine(temp, "missing"))[0].Console == 1 && GameLanguages.Scan(temp).Count == 1, "without a game folder only English is offered");

    string bcml = Path.Combine(temp, "settings.json");
    File.WriteAllText(bcml, "{\n  \"game_dir\": \"E:\\\\Games\\\\Zelda [Game]\\\\content\",\n  \"game_dir_nx\": \"\"\n}");
    Require(GameLanguages.ContentFolder(bcml) == "E:\\Games\\Zelda [Game]\\content" && GameLanguages.ContentFolder(Path.Combine(temp, "none.json")) == null, "the game folder comes from the BCML settings");
    File.WriteAllText(bcml, "{ \"game_dir\": \"\" }");
    Require(GameLanguages.ContentFolder(bcml) == null, "an empty game folder is ignored");

    string instance = Path.Combine(temp, "cemu"), stockFolder = Path.Combine(instance, "graphicPacks", "downloadedGraphicPacks", "BreathOfTheWild", "Graphics");
    Directory.CreateDirectory(stockFolder);
    File.WriteAllText(Path.Combine(stockFolder, "rules.txt"), "[Definition]\r\ntitleIds = 00050000101C9400\r\nname = Graphics\r\npath = \"The Legend of Zelda: Breath of the Wild/Graphics\"\r\nversion = 7\r\n");
    string settingsFile = Path.Combine(instance, "settings.xml");
    File.WriteAllText(settingsFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<content>\n  <language>0</language>\n  <console_language>1</console_language>\n  <GraphicPack />\n</content>");
    var view = ScreenLayout.Plan(LayoutMode.SideBySide, 1.0, 1920, 1080).Players[0];
    Func<string, string> setting = name => (string)System.Xml.Linq.XDocument.Load(settingsFile).Root.Element(name);
    CemuDisplay.Apply(instance, view, null, null, 5);
    Require(setting("console_language") == "5" && setting("language") == "0", "the game language is written and Cemu's own menu language is kept");
    CemuDisplay.Apply(instance, view);
    Require(setting("console_language") == "5", "no language given leaves the game language as it was");
    File.WriteAllText(settingsFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<content>\n  <GraphicPack />\n</content>");
    CemuDisplay.Apply(instance, view, null, null, 2);
    Require(setting("console_language") == "2", "a settings file without the language gets one");
   }
   finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
   Console.WriteLine("PASS: game and launcher languages.");
  }

  static int[] Reconcile(int[] routes, params bool[] connected) { Router.Reconcile(routes, connected); return routes; }

  static int Main(string[] args) {
   try {
    var routes = Reconcile(new[] { -1, -1 }, true, true, false, false);
    Require(routes[0] == 0 && routes[1] == 1, "first two pads take players one and two");

    routes = Reconcile(new[] { -1, -1 }, false, false, true, true);
    Require(routes[0] == 2 && routes[1] == 3, "pads in slots two and three");

    routes = Reconcile(new[] { 0, 1 }, false, true, true, false);
    Require(routes[0] == 2 && routes[1] == 1, "reconnection keeps player two and refills player one");

    routes = Reconcile(new[] { 0, 1 }, false, false, false, false);
    Require(routes[0] == 0 && routes[1] == 1, "slots stay reserved while pads are away");

    for (int mask = 0; mask < 16; mask++)
     for (int first = -1; first < 4; first++)
      for (int second = -1; second < 4; second++) {
       if (first >= 0 && first == second) continue;
       var present = new bool[4];
       int count = 0;
       for (int i = 0; i < 4; i++) { present[i] = (mask & (1 << i)) != 0; if (present[i]) count++; }
       routes = Reconcile(new[] { first, second }, present);
       Require(routes[0] < 0 || routes[0] != routes[1], "unique controller ownership");
       Require(first < 0 || !present[first] || routes[0] == first, "connected player one is kept");
       Require(second < 0 || !present[second] || routes[1] == second, "connected player two is kept");
       int assigned = 0;
       foreach (int route in routes) if (route >= 0 && present[route]) assigned++;
       Require(assigned == Math.Min(2, count), "every available player is filled");
      }

    routes = new[] { 0, 1 }; Router.Assign(routes, 0, 1);
    Require(routes[0] == 1 && routes[1] == 0, "assigning the other player's pad swaps them");
    routes = new[] { 0, -1 }; Router.Assign(routes, 1, 0);
    Require(routes[1] == 0 && routes[0] == -1, "taking the only pad leaves the other player empty");
    routes = new[] { 0, 1 }; Router.Assign(routes, 0, 2);
    Require(routes[0] == 2 && routes[1] == 1, "assigning a free pad changes only that player");

    Console.WriteLine("PASS: router reconciliation and assignment.");

    Require(ScreenLayout.Resolve(LayoutMode.Auto, 1920, 1080) == LayoutMode.TopBottom, "a 16:9 display is stacked by Auto");
    Require(ScreenLayout.Resolve(LayoutMode.Auto, 3840, 1080) == LayoutMode.SideBySide, "a 32:9 display is split side by side by Auto");
    Require(ScreenLayout.Resolve(LayoutMode.SideBySide, 1080, 1920) == LayoutMode.SideBySide, "an explicit choice is kept");
    var halves = ScreenLayout.Split(LayoutMode.SideBySide, 1921, 1080);
    Require(halves[0].X == 0 && halves[0].Width + halves[1].Width == 1921 && halves[1].X == halves[0].Width && halves[1].Height == 1080, "side by side covers the display");
    halves = ScreenLayout.Split(LayoutMode.TopBottom, 1920, 1081);
    Require(halves[0].Y == 0 && halves[0].Height + halves[1].Height == 1081 && halves[1].Y == halves[0].Height && halves[1].Width == 1920, "top and bottom covers the display");
    Require(halves.Length == 2 && ScreenLayout.Resolve(LayoutMode.TopBottom, 3840, 1080) == LayoutMode.TopBottom, "an explicit stacked choice is kept");
    int rw, rh;
    ScreenLayout.RenderSize(new Area { Width = 1920, Height = 540 }, 1.0, out rw, out rh);
    Require(rw == 1920 && rh == 540, "balanced renders at window size");
    ScreenLayout.RenderSize(new Area { Width = 1920, Height = 540 }, 1.5, out rw, out rh);
    Require(rw == 2880 && rh == 810, "a 1.5 scale renders at 150 percent");
    Require(ScreenLayout.Parse("TopBottom") == LayoutMode.TopBottom && ScreenLayout.Parse("garbage") == LayoutMode.Auto && ScreenLayout.Parse("7") == LayoutMode.Auto && ScreenLayout.Parse(null) == LayoutMode.Auto, "saved layout parsing falls back to Auto");
    Console.WriteLine("PASS: screen layout.");

    var displays = new[] { new[] { 320, 240 }, new[] { 800, 600 }, new[] { 1024, 768 }, new[] { 1280, 720 }, new[] { 1280, 1024 }, new[] { 1366, 768 }, new[] { 1600, 900 }, new[] { 1920, 1080 }, new[] { 1920, 1200 }, new[] { 1921, 1081 },
     new[] { 2560, 1080 }, new[] { 2560, 1440 }, new[] { 3440, 1440 }, new[] { 3840, 1080 }, new[] { 3840, 2160 }, new[] { 5120, 1440 }, new[] { 7680, 2160 }, new[] { 7680, 4320 }, new[] { 1200, 1200 }, new[] { 1080, 1920 }, new[] { 1440, 2560 }, new[] { 2160, 3840 } };
    int plans = 0;
    foreach (var display in displays) foreach (var mode in new[] { LayoutMode.Auto, LayoutMode.SideBySide, LayoutMode.TopBottom }) foreach (double scale in GraphicsMod.Scales) {
     string name = display[0] + "x" + display[1] + " " + mode + " x" + scale;
     var plan = ScreenLayout.Plan(mode, scale, display[0], display[1]);
     var a = plan.Players[0].Area; var b = plan.Players[1].Area;
     bool side = plan.Layout == LayoutMode.SideBySide;
     Require(a.X == 0 && a.Y == 0 && (side ? a.Height == display[1] && b.X == a.Width && b.Y == 0 && b.Height == display[1] && a.Width + b.Width == display[0]
      : a.Width == display[0] && b.X == 0 && b.Y == a.Height && b.Width == display[0] && a.Height + b.Height == display[1]), name + ": the two windows tile the whole display");
     foreach (var view in plan.Players) {
      double windowRatio = (double)view.Area.Width / view.Area.Height, renderRatio = (double)view.RenderWidth / view.RenderHeight, ratio = (double)view.RatioWidth / view.RatioHeight;
      Require(view.RenderWidth % 2 == 0 && view.RenderHeight % 2 == 0 && view.RenderWidth >= 2 && view.RenderHeight >= 2, name + ": render size is even");
      Require(view.RenderWidth <= ScreenLayout.MaxRenderSide && view.RenderHeight <= ScreenLayout.MaxRenderSide, name + ": render size stays within the GPU limit");
      Require(Math.Abs(renderRatio / windowRatio - 1) < 0.01 + 2.0 / Math.Min(view.RenderWidth, view.RenderHeight), name + ": render has the window shape");
      Require(Math.Abs(ratio / renderRatio - 1) < 0.005 && view.RatioWidth <= 1000 && view.RatioHeight <= 1000 && view.RatioWidth > 0 && view.RatioHeight > 0, name + ": aspect ratio matches the render");
      Require(view.Ultrawide == (renderRatio > 16.0 / 9.0 + 0.01) && view.Square == (renderRatio <= 1.51) && !(view.Ultrawide && view.Square), name + ": HUD flags follow the shape");
      double grown = Math.Min(scale, (double)ScreenLayout.MaxRenderSide / Math.Max(view.Area.Width, view.Area.Height));
      Require(Math.Abs(view.RenderWidth - view.Area.Width * grown) <= 2 && Math.Abs(view.RenderHeight - view.Area.Height * grown) <= 2, name + ": render follows the scale");
      if (scale == 1.0) Require(view.RenderWidth >= view.Area.Width - 1 && view.RenderWidth <= view.Area.Width && view.RenderHeight >= view.Area.Height - 1 && view.RenderHeight <= view.Area.Height, name + ": balanced renders at window size");
     }
     plans++;
    }
    var wide = ScreenLayout.Plan(LayoutMode.Auto, 1.0, 5120, 1440).Players[1];
    Require(wide.Area.X == 2560 && wide.RenderWidth == 2560 && wide.RenderHeight == 1440 && wide.RatioWidth == 16 && wide.RatioHeight == 9 && !wide.Ultrawide, "a 32:9 display gives each player a 16:9 game");
    var stacked = ScreenLayout.Plan(LayoutMode.Auto, 1.0, 1920, 1080).Players[1];
    Require(stacked.Area.Y == 540 && stacked.RenderWidth == 1920 && stacked.RenderHeight == 540 && stacked.RatioWidth == 32 && stacked.RatioHeight == 9 && stacked.Ultrawide, "a 16:9 display stacks two 32:9 games");
    var huge = ScreenLayout.Plan(LayoutMode.SideBySide, 2.0, 7680, 4320).Players[0];
    Require(huge.RenderWidth <= ScreenLayout.MaxRenderSide && huge.RenderHeight <= ScreenLayout.MaxRenderSide, "a doubled render on a huge display is capped");
    bool rejected = false;
    try { ScreenLayout.Plan(LayoutMode.Auto, 1.0, 0, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "a display that is not ready is rejected");
    Console.WriteLine("PASS: display plans for " + plans + " display, layout and quality combinations.");

    Func<string, int, int, int, int, int, bool, MonitorInfo> mon = (dev, x, y, w, h, hzv, prim) => new MonitorInfo { Device = dev, X = x, Y = y, Width = w, Height = h, Hz = hzv, Primary = prim };
    var d2 = mon("\\\\.\\DISPLAY2", 0, 0, 5120, 1440, 240, true);
    var d1 = mon("\\\\.\\DISPLAY1", 1602, 1440, 1920, 720, 60, false);
    var d3 = mon("\\\\.\\DISPLAY3", -1920, 100, 1920, 1080, 144, false);
    var tiny = mon("\\\\.\\DISPLAY4", 5120, 0, 200, 100, 60, false);
    MonitorInfo p1, p2;
    Require(!ScreenLayout.Pair(new MonitorInfo[0], null, null, out p1, out p2) && !ScreenLayout.Pair(new[] { d2 }, null, null, out p1, out p2), "fewer than two displays never separate the players");
    Require(!ScreenLayout.Pair(new[] { d2, tiny }, null, null, out p1, out p2), "a display that is too small does not count");
    Require(ScreenLayout.Pair(new[] { d1, d2 }, null, null, out p1, out p2) && p1.Device == d2.Device && p2.Device == d1.Device, "with nothing chosen the primary display is player 1");
    Require(ScreenLayout.Pair(new[] { d1, d2, d3 }, d3.Device, d3.Device, out p1, out p2) && p1.Device == d3.Device && p2.Device == d2.Device, "both players never share a display");
    Require(ScreenLayout.Pair(new[] { d1, d2 }, "\\\\.\\DISPLAY9", d1.Device, out p1, out p2) && p1.Device == d2.Device && p2.Device == d1.Device, "a display that is gone is replaced by a free one");
    Require(ScreenLayout.Pair(new[] { d1, d2, d3 }, d1.Device, null, out p1, out p2) && p1.Device == d1.Device && p2.Device == d2.Device, "a free display fills the missing choice");
    var swapped = ScreenLayout.Assign(new[] { d1, d2 }, d2.Device, d1.Device, 0, d1.Device);
    Require(swapped[0] == d1.Device && swapped[1] == d2.Device, "choosing the other player's display swaps the two");
    var moved = ScreenLayout.Assign(new[] { d1, d2, d3 }, d2.Device, d1.Device, 1, d3.Device);
    Require(moved[0] == d2.Device && moved[1] == d3.Device, "choosing a free display changes only that player");
    Require(ScreenLayout.DisplayNumber(d2.Device) == 2 && ScreenLayout.DisplayNumber("\\\\.\\DISPLAY12") == 12 && ScreenLayout.DisplayNumber("x") == 0 && ScreenLayout.DisplayNumber(null) == 0, "display numbers are read from the device name");

    int separatePlans = 0;
    foreach (var first in displays) foreach (var second in displays) foreach (double scale in GraphicsMod.Scales) {
     string name = first[0] + "x" + first[1] + " + " + second[0] + "x" + second[1] + " x" + scale;
     var plan = ScreenLayout.PlanSeparate(mon("a", -first[0], -300, first[0], first[1], 60, false), scale, mon("b", 40, 17, second[0], second[1], 144, true), 1.0);
     Require(plan.Separate && plan.Players.Length == 2, name + ": the plan is separate");
     Require(plan.Players[0].Area.X == -first[0] && plan.Players[0].Area.Y == -300 && plan.Players[0].Area.Width == first[0] && plan.Players[0].Area.Height == first[1], name + ": player 1 fills its display, even at a negative origin");
     Require(plan.Players[1].Area.X == 40 && plan.Players[1].Area.Y == 17 && plan.Players[1].Area.Width == second[0] && plan.Players[1].Area.Height == second[1], name + ": player 2 fills its display");
     for (int i = 0; i < 2; i++) {
      var view = plan.Players[i];
      double windowRatio = (double)view.Area.Width / view.Area.Height, renderRatio = (double)view.RenderWidth / view.RenderHeight, ratio = (double)view.RatioWidth / view.RatioHeight;
      Require(view.RenderWidth % 2 == 0 && view.RenderHeight % 2 == 0 && view.RenderWidth <= ScreenLayout.MaxRenderSide && view.RenderHeight <= ScreenLayout.MaxRenderSide, name + ": render size is even and within the GPU limit");
      Require(Math.Abs(renderRatio / windowRatio - 1) < 0.01 + 2.0 / Math.Min(view.RenderWidth, view.RenderHeight) && Math.Abs(ratio / renderRatio - 1) < 0.005, name + ": render and aspect ratio match the display");
     }
     Require(plan.Players[1].RenderWidth >= second[0] - 1 && plan.Players[1].RenderWidth <= second[0], name + ": each player has their own scale");
     separatePlans++;
    }
    rejected = false;
    try { ScreenLayout.PlanSeparate(d2, 1.0, tiny, 1.0); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "a display that is too small is rejected");
    Console.WriteLine("PASS: one display per player, " + separatePlans + " display pairs.");

    var all = FpsMod.Parse(FpsRules);
    Require(all.Count == 12 && all[0].Fps == 20 && all[11].Fps == 240 && all.Any(l => l.Fps == 60 && l.Preset == "60FPS Limit (Default)"), "the mod's FPS limits are read from its rules, once each, in order");
    Require(!all.Any(l => l.Fps == 244 || l.Fps == 48), "advanced framerate presets are not offered");
    Func<int, string> offered = hz => string.Join(",", FpsMod.Available(all, hz).Select(l => l.Fps));
    Require(offered(240) == "30,45,55,60,72,75,90,120,144" && offered(360) == offered(240), "a fast display offers 30 to 144 only");
    Require(offered(120) == "30,45,55,60,72,75,90,120", "a 120 Hz display offers nothing above 120");
    Require(offered(144) == "30,45,55,60,72,75,90,120,144" && offered(100) == "30,45,55,60,72,75,90" && offered(60) == "30,45,55,60", "limits stop at the refresh rate");
    Require(offered(d2.Hz) == "30,45,55,60,72,75,90,120,144" && offered(d1.Hz) == "30,45,55,60" && offered(d3.Hz) == offered(144), "each display offers FPS limits up to its own refresh rate");
    Require(offered(0) == "30,45,55,60" && offered(1) == "30,45,55,60", "an unknown refresh rate is treated as 60 Hz");
    Require(offered(10) == "30" && offered(20) == "30", "a very slow display still offers the lowest limit");
    Func<int, int> best = hz => FpsMod.Best(FpsMod.Available(all, hz), hz).Fps;
    Require(best(240) == 120 && best(360) == 120 && best(144) == 144 && best(120) == 120 && best(60) == 60 && best(75) == 75, "auto picks the highest rate that divides the refresh rate");
    Require(best(143) == 144 && best(239) == 120 && best(59) == 60, "auto tolerates refresh rates reported a little low");
    Require(best(165) == 144 && best(100) == 90 && best(0) == 60 && best(30) == 30 && FpsMod.Best(new List<FpsMod.Limit>(), 60) == null, "auto falls back to the highest offered rate");
    var at120 = FpsMod.Available(all, 120);
    Require(FpsMod.Choose(at120, 144).Fps == 120 && FpsMod.Choose(at120, 90).Fps == 90 && FpsMod.Choose(at120, 100).Fps == 90 && FpsMod.Choose(at120, 5).Fps == 30, "a saved choice that the display cannot show is lowered");
    Require(FpsMod.Pick(at120, FpsMod.AutoFps, 120).Fps == 120 && FpsMod.Pick(FpsMod.Available(all, 240), FpsMod.AutoFps, 240).Fps == 120 && FpsMod.Pick(at120, 72, 120).Fps == 72, "auto and manual choices are told apart");
    Require(FpsMod.Choose(new List<FpsMod.Limit>(), 60) == null && FpsMod.Parse(null).Count == 0 && FpsMod.Parse("[Definition]\nname = FPS++").Count == 0, "a missing or odd pack gives no options");
    Console.WriteLine("PASS: FPS limits from the mod.");

    string temp = Path.Combine(Path.GetTempPath(), "HyruleOnlineTests-" + Guid.NewGuid().ToString("N"));
    try {
     string instance = Path.Combine(temp, "cemu"), stockFolder = Path.Combine(instance, "graphicPacks", "downloadedGraphicPacks", "BreathOfTheWild", "Graphics");
     Directory.CreateDirectory(stockFolder);
     string stock = "[Definition]\r\ntitleIds = 00050000101C9400\r\nname = Graphics\r\npath = \"The Legend of Zelda: Breath of the Wild/Graphics\"\r\nversion = 7\r\n\r\n[Default]\r\n$width = 1280\r\n\r\n[Preset]\r\nname = 16:9 (Default)\r\ncategory = Aspect Ratio\r\n$aspectRatioWidth = 16\r\n";
     File.WriteAllText(Path.Combine(stockFolder, "rules.txt"), stock);
     File.WriteAllText(Path.Combine(stockFolder, "patch_AspectRatio.asm"), "patch");
     string settingsFile = Path.Combine(instance, "settings.xml");
     File.WriteAllText(settingsFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<content>\n  <GraphicPack>\n    <Entry filename=\"graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Mods\\ExtendedMemory\\rules.txt\" />\n    <Entry filename=\"graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Graphics\\rules.txt\">\n      <Preset><category>Resolution</category><preset>1920x1080</preset></Preset>\n    </Entry>\n  </GraphicPack>\n  <Graphic>\n    <FullscreenScaling>0</FullscreenScaling>\n  </Graphic>\n</content>");

     var view = ScreenLayout.Plan(LayoutMode.TopBottom, 1.0, 1920, 1080).Players[1];
     CemuDisplay.Apply(instance, view, "120FPS Limit");
     string rules = File.ReadAllText(Path.Combine(instance, CemuDisplay.PackFile));
     Require(rules.Contains("name = Graphics (online display)") && rules.Contains("/Online Display\"") && rules.Contains("$aspectRatioWidth = 32\r\n$aspectRatioHeight = 9\r\n$showUltrawideOptions:int = 1") && rules.Contains("$width = 1920\r\n$height = 540"), "the instance pack carries the window's aspect ratio and resolution");
     Require(rules.StartsWith(stock.Substring(0, 12)) && File.ReadAllText(Path.Combine(stockFolder, "rules.txt")) == stock, "the stock pack is left untouched");
     Require(File.Exists(Path.Combine(instance, "graphicPacks", "HyruleOnline", "Graphics", "patch_AspectRatio.asm")), "the pack's patches are copied");
     var settings = System.Xml.Linq.XDocument.Load(settingsFile).Root;
     var entries = settings.Element("GraphicPack").Elements("Entry").Select(e => (string)e.Attribute("filename")).ToArray();
     Require(entries.Length == 3 && entries.Contains(CemuDisplay.PackFile) && entries.Contains(FpsMod.PackFile) && entries.Any(e => e.EndsWith("ExtendedMemory\\rules.txt")), "the stock Graphics entry is replaced, FPS++ is enabled and other packs are kept");
     var fpsPresets = settings.Element("GraphicPack").Elements("Entry").First(e => (string)e.Attribute("filename") == FpsMod.PackFile).Elements("Preset").Select(p => (string)p.Element("category") + "=" + (string)p.Element("preset")).ToArray();
     Require(fpsPresets.Length == 2 && fpsPresets[0] == "Mode=Normal Settings" && fpsPresets[1] == "FPS Limit=120FPS Limit", "FPS++ gets the chosen limit in normal mode");
     var presets = settings.Element("GraphicPack").Elements("Entry").First(e => (string)e.Attribute("filename") == CemuDisplay.PackFile).Elements("Preset").Select(p => (string)p.Element("category") + "=" + (string)p.Element("preset")).ToArray();
     Require(presets.Length == 2 && presets[0] == "Aspect Ratio=Online Display" && presets[1] == "Resolution=Online Resolution", "both online presets are selected");
     Require((string)settings.Element("Graphic").Element("FullscreenScaling") == "1", "the picture keeps its aspect ratio");

     CemuDisplay.Apply(instance, ScreenLayout.Plan(LayoutMode.SideBySide, 1.5, 3840, 2160).Players[0], "60FPS Limit (Default)");
     rules = File.ReadAllText(Path.Combine(instance, CemuDisplay.PackFile));
     Require(rules.Contains("$width = 2880\r\n$height = 3240") && !rules.Contains("$width = 1920\r\n$height = 540") && rules.Split(new[] { "name = Online Display" }, StringSplitOptions.None).Length == 2, "a new session replaces the previous values");
     var again = System.Xml.Linq.XDocument.Load(settingsFile).Root.Element("GraphicPack").Elements("Entry");
     Require(again.Count() == 3 && (string)again.First(e => (string)e.Attribute("filename") == FpsMod.PackFile).Elements("Preset").Last().Element("preset") == "60FPS Limit (Default)", "settings do not collect duplicate entries and take the new limit");
     CemuDisplay.Apply(instance, view);
     Require(System.Xml.Linq.XDocument.Load(settingsFile).Root.Element("GraphicPack").Elements("Entry").Count() == 3, "no limit given leaves FPS++ as it was");

     bool broken = false;
     try { CemuDisplay.BuildRules("name = something else", view); } catch (InvalidDataException) { broken = true; }
     Require(broken, "an unexpected stock pack is refused");
    }
    finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    Console.WriteLine("PASS: Cemu display settings.");
    Graphics();
    Console.WriteLine("PASS: graphics quality.");
    Motion();
    Console.WriteLine("PASS: controller motion.");
    Navigation();
    Players();
    ServerRulesTest();
    Languages(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
        return 0;
       }
   catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
  }
 }
}
