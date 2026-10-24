using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // Startup checks: the install folder guard, the saved preferences and the restart-gated display plan.
 static class StartupTests {
  static void Require(bool ok, string message) { if (!ok) throw new Exception("TEST: " + message); }

  static string Issues(string root, string[] protectedFolders = null, string[] synced = null, bool network = false) {
   return string.Join(",", PathGuard.Check(root, protectedFolders ?? new string[0], synced ?? new string[0], network).Select(i => i.ToString()).ToArray());
  }

  static string Read(string path) { return File.ReadAllText(path); }

  static int Number(string text, string pattern) {
   var match = Regex.Match(text, pattern);
   Require(match.Success, "found " + pattern);
   return int.Parse(match.Groups[1].Value);
  }

  static void Guard(string root) {
   Require(PathGuard.MaxRootLength == 83 && PathGuard.MaxRootLength + PathGuard.LongestKnownRelative < PathGuard.MaxPath, "the limit keeps the deepest known file under 260 characters");
   string source = Read(Path.Combine(root, "src", "Launcher", "PathGuard.cs"));
   Require(Number(source, "MaxPath = (\\d+);") == PathGuard.MaxPath && Number(source, "LongestKnownRelative = (\\d+);") == PathGuard.LongestKnownRelative && Number(source, "Headroom = (\\d+);") == PathGuard.Headroom, "the documented numbers are the constants");

   // The same limit is repeated in the script and the installer.
   int script = Number(Read(Path.Combine(root, "scripts", "Path-Guard.ps1")), "\\$MaxRootLength = (\\d+)");
   int installer = Number(Read(Path.Combine(root, "installer", "HyruleOnline.iss")), "#define MaxRootLength (\\d+)");
   Require(script == PathGuard.MaxRootLength, "scripts\\Path-Guard.ps1 uses the same limit as PathGuard.cs (" + script + " against " + PathGuard.MaxRootLength + ")");
   Require(installer == PathGuard.MaxRootLength, "installer\\HyruleOnline.iss uses the same limit as PathGuard.cs (" + installer + " against " + PathGuard.MaxRootLength + ")");

   string atLimit = "C:\\" + new string('a', PathGuard.MaxRootLength - 3);
   Require(Issues("C:\\HyruleOnline") == "" && Issues(PathGuard.SuggestedRoot) == "" && Issues(null) == "" && Issues("") == "", "the suggested short folder and empty values are fine");
   Require(Issues(atLimit) == "" && Issues(atLimit + "\\") == "" && Issues(atLimit + "a") == "TooLong", "the limit counts the folder without its closing backslash");
   Require(Issues("\\\\server\\share\\Hyrule") == "Network" && Issues("Z:\\Hyrule", null, null, true) == "Network" && Issues("\\\\?\\UNC\\server\\share") == "Network", "network folders");
   var programFiles = new[] { "C:\\Program Files", "C:\\Program Files (x86)\\", null, "" };
   Require(Issues("C:\\Program Files\\Hyrule", programFiles) == "ProgramFiles" && Issues("c:\\program files (x86)\\Hyrule", programFiles) == "ProgramFiles" && Issues("C:\\Program Files", programFiles) == "ProgramFiles", "Program Files, in any case");
   Require(Issues("C:\\Program Files Tools\\Hyrule", programFiles) == "" && Issues("C:\\Hyrule", programFiles) == "", "a folder that only starts with the same letters is not inside");
   Require(Issues("C:\\Users\\Ann\\OneDrive\\Hyrule") == "OneDrive" && Issues("C:\\Users\\Ann\\OneDrive - Contoso\\Games\\Hyrule") == "OneDrive" && Issues("D:\\Cloud\\Hyrule", null, new[] { "D:\\Cloud" }) == "OneDrive", "OneDrive by name or by the synced folders");
   Require(Issues("C:\\Users\\Ann\\Documents\\Hyrule", null, new[] { "C:\\Users\\Ann\\OneDrive" }) == "", "a folder next to the synced one is fine");
   Require(Issues("C:\\Usuarios\\Cámara\\Hyrule") == "NonAscii" && Issues("C:\\日本語") == "NonAscii", "non-ASCII characters");
   Require(Issues("\\\\server\\Cámara\\" + new string('a', 90)) == "TooLong,Network,NonAscii", "several problems are all reported, in a fixed order");
   Require(PathGuard.Blocks(PathIssue.TooLong) && PathGuard.Blocks(PathIssue.Network) && !PathGuard.Blocks(PathIssue.ProgramFiles) && !PathGuard.Blocks(PathIssue.OneDrive) && !PathGuard.Blocks(PathIssue.NonAscii), "only too long and network block");
   Require(PathGuard.Check(Directory.GetCurrentDirectory()) != null && PathGuard.Check("C:\\HyruleOnline").All(i => i != PathIssue.TooLong), "the environment based check runs");

   string tooLong = "C:\\" + new string('a', 100);
   try {
    string english = PathGuard.Describe(tooLong, PathGuard.Check(tooLong, new string[0], new string[0]));
    Require(english.Contains(tooLong) && english.Contains("103 characters") && english.Contains("limit is 83") && english.Contains(PathGuard.SuggestedRoot) && !english.Contains("Continue anyway?") && english.StartsWith("Hyrule Online cannot start"), "the message for a blocked folder names the folder, the limit and the short path");
    var soft = PathGuard.Describe("C:\\Users\\Ann\\OneDrive\\Hyrule", new List<PathIssue> { PathIssue.OneDrive });
    Require(soft.StartsWith("Hyrule Online may have problems") && soft.Contains("OneDrive") && soft.EndsWith("Continue anyway?"), "a warning asks whether to continue");
    foreach (string language in new[] { "es", "fr" }) {
     Lang.Current = language;
     foreach (var issue in Enum.GetValues(typeof(PathIssue)).Cast<PathIssue>()) {
      string text = PathGuard.Describe(tooLong, new List<PathIssue> { issue });
      Require(!text.Contains("The folder") && !text.Contains("Move the whole folder") && !text.Contains("Hyrule Online cannot") && !text.Contains("Hyrule Online may"), issue + " is translated into " + language);
     }
     Require(PathGuard.Describe(tooLong, new List<PathIssue> { PathIssue.TooLong }).Contains("103") && PathGuard.Describe(tooLong, new List<PathIssue> { PathIssue.OneDrive }).EndsWith(Lang.T("Continue anyway?")) && Lang.T("Continue anyway?") != "Continue anyway?", "the translation keeps its numbers and the question (" + language + ")");
    }
   }
   finally { Lang.Current = "en"; }

   // Every message PathGuard can show must be in the translation table.
   var known = new HashSet<string>(Lang.Table.Select(r => r[0]));
   var literals = Read(Path.Combine(root, "src", "Launcher", "PathGuard.cs")).Split('\n').Where(l => l.Contains("Lang.T(")).SelectMany(l => Regex.Matches(l, "(?:Lang\\.T\\(|\\? |: )\"((?:[^\"\\\\]|\\\\.)*)\"").Cast<Match>().Select(m => m.Groups[1].Value)).ToList();
   Require(literals.Count >= 8, "the guard messages were found");
   foreach (string literal in literals) Require(known.Contains(literal), "the guard text is translated: " + literal);
   Console.WriteLine("PASS: install folder guard.");
  }

  static Prefs Load(string folder, string content) {
   string file = Path.Combine(folder, "preferences-" + Guid.NewGuid().ToString("N") + ".xml");
   if (content != null) File.WriteAllText(file, content);
   return Prefs.Read(file);
  }

  static void Defaults(Prefs p) {
   Require(p.Mode == LayoutMode.Auto && p.Scale == GraphicsMod.DefaultScale && p.Shadows == null && p.ShadowDistance == null && p.AntiAliasing == null, "default layout and graphics");
   Require(p.Advanced.Api == GraphicsMod.AutoApi && !p.Advanced.VSync && !p.Advanced.AsyncCompile && !p.Advanced.AccurateBarriers, "default advanced graphics");
   Require(p.FpsLimit == FpsMod.AutoFps && !p.PerDisplay && p.PlayerCount == 2 && p.MenuLayout == PadKind.Auto && p.Language == "en" && p.LauncherLanguage == "en", "default players and languages");
   Require(p.Names[0] == "Link" && p.Names[1] == "Pedro" && p.Models[0] == "" && p.Models[1] == "" && p.Displays.All(d => d == null) && p.Scales.All(s => s == GraphicsMod.DefaultScale) && p.FpsLimits.All(f => f == FpsMod.AutoFps) && p.Rotations.All(r => r == 0), "default per-player values");
  }

  static void Preferences(string root) {
   string temp = Path.Combine(Path.GetTempPath(), "hyrule-prefs-" + Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(temp);
   try {
    Defaults(Load(temp, null));
    Defaults(Load(temp, ""));
    Defaults(Load(temp, "   "));
    Defaults(Load(temp, "this is not xml"));
    Defaults(Load(temp, "<Preferences><Layout>SideBySide</Layout>"));
    Defaults(Load(temp, "<Preferences/>"));
    Defaults(Prefs.Read(temp));
    Require(Load(temp, "<Preferences><Layout>SideBySide</Layout></Preferences>").Mode == LayoutMode.SideBySide, "a file with only one key still reads it");

    // What SavePrefs writes today.
    var current = Load(temp, "<?xml version=\"1.0\" encoding=\"utf-8\"?><Preferences><Layout>TopBottom</Layout><RenderScale>1.5</RenderScale><Shadows>Low (50%)</Shadows><ShadowDistance>High</ShadowDistance><AntiAliasing>None</AntiAliasing>"
     + "<GraphicsApi>Vulkan</GraphicsApi><VSync>1</VSync><AsyncCompile>1</AsyncCompile><AccurateBarriers>1</AccurateBarriers><UpscaleFilter>2</UpscaleFilter><DownscaleFilter>1</DownscaleFilter><FpsLimit>144</FpsLimit>"
     + "<Arrangement>PerDisplay</Arrangement><Players>1</Players><Display1>\\\\.\\DISPLAY2</Display1><Display2></Display2><Scale1>2</Scale1><Scale2>0.75</Scale2><Fps1>60</Fps1><Fps2>Auto</Fps2>"
     + "<Rotation1>90</Rotation1><Rotation2>270</Rotation2><Name1>Zelda</Name1><Model1>Npc_Gerudo_Hero</Model1><Name2>Ana</Name2><Model2></Model2><Language>fr</Language><LauncherLanguage>es</LauncherLanguage><MenuLayout>Xbox</MenuLayout></Preferences>");
    Require(current.Mode == LayoutMode.TopBottom && current.Scale == 1.5 && current.Shadows == "Low (50%)" && current.ShadowDistance == "High" && current.AntiAliasing == "None", "layout and graphics are read");
    Require(current.Advanced.Api == "Vulkan" && current.Advanced.VSync && current.Advanced.AsyncCompile && current.Advanced.AccurateBarriers && current.Advanced.Upscale == 2 && current.Advanced.Downscale == 1, "advanced graphics are read");
    Require(current.FpsLimit == 144 && current.PerDisplay && current.PlayerCount == 1 && current.MenuLayout == Array.IndexOf(PadKind.Names, "Xbox") && current.Language == "fr" && current.LauncherLanguage == "es", "players and languages are read");
    Require(current.Displays[0] == "\\\\.\\DISPLAY2" && current.Displays[1] == null && current.Scales[0] == 2.0 && current.Scales[1] == 0.75 && current.FpsLimits[0] == 60 && current.FpsLimits[1] == FpsMod.AutoFps && current.Rotations[0] == 90 && current.Rotations[1] == 270, "per-player values are read, an empty display is none");
    Require(current.Names[0] == "Zelda" && current.Names[1] == "Ana" && current.Models[0] == "Npc_Gerudo_Hero" && current.Models[1] == "", "names and models are read");

    // Older versions, unknown keys and nonsense values.
    var older = Load(temp, "<Preferences><Quality>Sharp</Quality><Layout>Diagonal</Layout><SomethingNew>1</SomethingNew></Preferences>");
    Require(older.Scale == 1.5 && older.Scales[0] == 1.5 && older.Scales[1] == 1.5 && older.Mode == LayoutMode.Auto, "the old sharper switch becomes a scale, per-player scales follow it, an unknown layout is Auto");
    Require(Load(temp, "<Preferences><Quality>Balanced</Quality></Preferences>").Scale == GraphicsMod.DefaultScale && Load(temp, "<Preferences><RenderScale>abc</RenderScale></Preferences>").Scale == GraphicsMod.DefaultScale, "a balanced or unreadable scale is the default");
    Require(Load(temp, "<Preferences><RenderScale>1.4</RenderScale><Quality>Sharp</Quality></Preferences>").Scale == 1.5 && Load(temp, "<Preferences><RenderScale>99</RenderScale></Preferences>").Scale == 2.0, "a saved scale is brought to an offered one and wins over the old switch");
    var odd = Load(temp, "<Preferences><FpsLimit>-5</FpsLimit><Fps1>0</Fps1><Fps2>fast</Fps2><Rotation1>45</Rotation1><Rotation2>abc</Rotation2><Players>3</Players><MenuLayout>Atari</MenuLayout><LauncherLanguage>de</LauncherLanguage><Language></Language><Name1>a;b|c</Name1><Name2>;;</Name2></Preferences>");
    Require(odd.FpsLimit == FpsMod.AutoFps && odd.FpsLimits.All(f => f == FpsMod.AutoFps) && odd.Rotations.All(r => r == 0) && odd.PlayerCount == 2 && odd.MenuLayout == PadKind.Auto, "out of range values fall back one by one");
    Require(odd.LauncherLanguage == "en" && odd.Language == "en" && odd.Names[0] == "abc" && odd.Names[1] == "Pedro", "an untranslated launcher language is English, unsafe name characters are dropped, an empty name is the default");
    Require(Load(temp, "<Preferences><Players>1</Players></Preferences>").PlayerCount == 1 && Load(temp, "<Preferences><Arrangement>Split</Arrangement></Preferences>").PerDisplay == false, "one player and the split arrangement");
    Require(Load(temp, "<Preferences><LauncherLanguage>fr</LauncherLanguage></Preferences>").LauncherLanguage == "fr", "the launcher language can be read on its own for the start-up message");

    // The keys SavePrefs writes must all be read back.
    string launcher = Read(Path.Combine(root, "src", "Launcher", "Launcher.cs"));
    int from = launcher.IndexOf("void SavePrefs()", StringComparison.Ordinal), to = launcher.IndexOf("void ChangedDisplayChoice()", StringComparison.Ordinal);
    Require(from > 0 && to > from, "SavePrefs was found");
    string reader = Read(Path.Combine(root, "src", "Launcher", "Prefs.cs"));
    var keys = Regex.Matches(launcher.Substring(from, to - from), "new XElement\\(\"(\\w+?)\\d?\"").Cast<Match>().Select(m => m.Groups[1].Value).Where(k => k != "Preferences").Distinct().ToList();
    Require(keys.Count >= 20, "the saved keys were found");
    foreach (string key in keys) Require(reader.Contains("\"" + key + "\""), "a saved preference is read back: " + key);
   }
   finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
   Console.WriteLine("PASS: saved preferences.");
  }

  static void RestartGating() {
   Require(!RestartGate.Differs(null, "a|b") && !RestartGate.Differs("a|b", "a|b") && RestartGate.Differs("a|b", "a|c") && RestartGate.Differs("", "a") && RestartGate.Differs("a", ""), "a restart is wanted only when the options differ from what the games started with");

   // The plan the games started with is a snapshot: later plans for another display or layout do not change it.
   var session = ScreenLayout.Plan(LayoutMode.Auto, 1.0, 3840, 1080, new[] { 0, 90 });
   Func<DisplayPlan, string> describe = plan => plan.Layout + "|" + plan.Separate + "|" + string.Join(";", plan.Players.Select(v => v.Area.X + "," + v.Area.Y + "," + v.Area.Width + "," + v.Area.Height + "," + v.Rotation + "," + v.RenderWidth + "x" + v.RenderHeight).ToArray());
   string before = describe(session);
   var later = ScreenLayout.Plan(LayoutMode.TopBottom, 2.0, 1920, 1080);
   var single = ScreenLayout.PlanSingle(1.5, 2560, 1440);
   Require(describe(session) == before && describe(later) != before && single.Players.Length == 1 && session.Layout == LayoutMode.SideBySide, "a plan kept for the session does not follow later changes");
   Require(session.Players[1].Rotation == 90 && session.Players[1].RenderWidth == 1080 && session.Players[1].RenderHeight == 1920 && session.Players[0].RenderWidth == 1920 && session.Players[0].RenderHeight == 1080, "the turned player renders the swapped shape, the other one is unchanged");
   Require(describe(ScreenLayout.Plan(LayoutMode.Auto, 1.0, 3840, 1080, new[] { 0, 90 })) == before, "the same inputs give the same plan");
   Console.WriteLine("PASS: restart gating and the session plan.");
  }

  static int Main(string[] args) {
   try {
    string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
    Guard(root);
    Preferences(root);
    RestartGating();
    Console.WriteLine("All startup tests passed.");
    return 0;
   }
   catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
  }
 }
}
