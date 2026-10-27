using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace HyruleOnline {
 enum SetupStatus { Ok, Missing, Broken }

 // What the first-run screen can offer for an item that is not OK.
 enum SetupAction { None, Download, Locate }

 // A line of text for the first-run screen. The English template is also the key of the translation table.
 sealed class Msg {
  public readonly string English;
  public readonly object[] Args;
  public readonly bool Plain;
  Msg(string english, object[] args, bool plain) { English = english; Args = args; Plain = plain; }
  public static Msg Say(string english, params object[] args) { return new Msg(english, args, false); }
  // Names and paths that read the same in every language.
  public static Msg Text(string text) { return new Msg(text, new object[0], true); }
  public override string ToString() { return Args.Length == 0 ? English : string.Format(English, Args); }
  public string Translated() { return Plain ? ToString() : Lang.T(English, Args); }
 }

 sealed class SetupCheck {
  public string Group, Component;
  public Msg Item, Detail, Fix;
  public SetupStatus Status;
  public SetupAction Action;
  public bool Required;
 }

 sealed class SetupMarker {
  public string File, Sha256, FileVersion;
  public long Size;
  public bool FirstTargetOnly;
 }

 sealed class SetupComponent {
  public string Id, Name, Version, Kind, Url, Sha256, StripRoot, Requires, Requirements, VersionFile, VersionText, RequiredFor, Verified;
  public long Size;
  public List<string> Targets = new List<string>();
  public List<SetupMarker> Markers = new List<SetupMarker>();
 }

 // scripts/components.json: the one list of downloadable components, read here and by scripts/Install-Components.ps1.
 sealed class SetupManifest {
  public const string FileName = "components.json";
  public int Schema, MinWindowsBuild, MinDotNetRelease, UpdateVersion, DlcVersion;
  public long FreeDiskBytes;
  public string GameTitleId, UpdateTitleId, DlcTitleId;
  public List<SetupComponent> Components = new List<SetupComponent>();

  public static string PathIn(string root) { return Path.Combine(root, "scripts", FileName); }

  public static SetupManifest Load(string root) { return Parse(File.ReadAllText(PathIn(root))); }

  public static SetupManifest Parse(string json) {
   var data = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
   if (data == null) throw new FormatException("The component manifest is not a JSON object.");
   var manifest = new SetupManifest { Schema = (int)Num(data, "schema") };
   var needs = Obj(data, "requirements");
   manifest.FreeDiskBytes = Num(needs, "freeDiskBytes");
   manifest.MinWindowsBuild = (int)Num(needs, "minWindowsBuild");
   manifest.MinDotNetRelease = (int)Num(needs, "minDotNetRelease");
   manifest.UpdateVersion = (int)Num(needs, "updateVersion");
   manifest.DlcVersion = (int)Num(needs, "dlcVersion");
   manifest.GameTitleId = Str(needs, "gameTitleId");
   manifest.UpdateTitleId = Str(needs, "updateTitleId");
   manifest.DlcTitleId = Str(needs, "dlcTitleId");
   foreach (var item in List(data, "components")) {
    var d = item as Dictionary<string, object>;
    if (d == null) throw new FormatException("A component entry is not an object.");
    var c = new SetupComponent {
     Id = Str(d, "id"), Name = Str(d, "name"), Version = Str(d, "version"), Kind = Str(d, "kind"), Url = Str(d, "url"), Sha256 = Str(d, "sha256"),
     StripRoot = Str(d, "stripRoot"), Requires = Str(d, "requires"), Requirements = Str(d, "requirements"), VersionFile = Str(d, "versionFile"),
     VersionText = Str(d, "versionText"), RequiredFor = Str(d, "requiredFor"), Verified = Str(d, "verified"), Size = Num(d, "size")
    };
    foreach (var target in List(d, "targets")) c.Targets.Add(Convert.ToString(target));
    foreach (var entry in List(d, "markers")) {
     var m = entry as Dictionary<string, object>;
     if (m == null) throw new FormatException("A marker entry is not an object.");
     c.Markers.Add(new SetupMarker { File = Str(m, "file"), Sha256 = Str(m, "sha256"), FileVersion = Str(m, "fileVersion"), Size = Num(m, "size"), FirstTargetOnly = m.ContainsKey("firstTargetOnly") && Convert.ToBoolean(m["firstTargetOnly"]) });
    }
    manifest.Components.Add(c);
   }
   return manifest;
  }

  static string Str(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : ""; }
  static long Num(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToInt64(v) : 0; }
  static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v is Dictionary<string, object> ? (Dictionary<string, object>)v : new Dictionary<string, object>(); }
  static IEnumerable<object> List(Dictionary<string, object> d, string key) { object v; var list = d.TryGetValue(key, out v) ? v as IEnumerable : null; return list == null ? new object[0] : list.Cast<object>(); }

  public static bool IsSha256(string text) { return text != null && Regex.IsMatch(text, "^[0-9A-Fa-f]{64}$"); }

  // A path from the manifest must stay inside the install folder.
  public static bool IsSafeRelative(string path) {
   if (string.IsNullOrWhiteSpace(path) || path.Contains(":") || path.StartsWith("/") || path.StartsWith("\\")) return false;
   return !path.Split('/', '\\').Any(part => part == ".." || part.Length == 0);
  }

  public SetupComponent Find(string id) { return Components.FirstOrDefault(c => c.Id == id); }

  public List<string> Validate() {
   var problems = new List<string>();
   if (Schema != 1) problems.Add("schema must be 1");
   if (FreeDiskBytes <= 0) problems.Add("freeDiskBytes must be positive");
   foreach (string id in new[] { GameTitleId, UpdateTitleId, DlcTitleId }) if (!Regex.IsMatch(id ?? "", "^[0-9A-F]{16}$")) problems.Add("title id is not 16 hex digits: " + id);
   var seen = new HashSet<string>();
   foreach (var c in Components) {
    string who = "component " + c.Id + ": ";
    if (!Regex.IsMatch(c.Id ?? "", "^[a-z0-9]+$")) problems.Add(who + "the id must be lower case letters and digits");
    if (!seen.Add(c.Id ?? "")) problems.Add(who + "duplicate id");
    if (c.Name.Length == 0 || c.Version.Length == 0 || c.RequiredFor.Length == 0) problems.Add(who + "name, version and requiredFor are required");
    if (c.Kind == "zip" || c.Kind == "python-installer") {
     if (!c.Url.StartsWith("https://", StringComparison.Ordinal)) problems.Add(who + "the url must be https");
     if (!IsSha256(c.Sha256)) problems.Add(who + "sha256 must be 64 hex digits");
     if (c.Size <= 0) problems.Add(who + "size must be positive");
    }
    else if (c.Kind == "pip") { if (!IsSafeRelative(c.Requirements)) problems.Add(who + "requirements must be a relative path"); }
    else problems.Add(who + "unknown kind " + c.Kind);
    if (c.Requires.Length > 0 && Find(c.Requires) == null) problems.Add(who + "requires an unknown component " + c.Requires);
    if (c.Targets.Count == 0 || c.Targets.Any(t => !IsSafeRelative(t))) problems.Add(who + "targets must be relative paths inside the install folder");
    if (c.Markers.Count == 0) problems.Add(who + "at least one marker file is needed to tell that it is installed");
    foreach (var m in c.Markers) {
     if (!IsSafeRelative(m.File)) problems.Add(who + "marker path is not relative: " + m.File);
     if (!string.IsNullOrEmpty(m.Sha256) && !IsSha256(m.Sha256)) problems.Add(who + "marker sha256 must be 64 hex digits: " + m.File);
    }
    if (c.StripRoot.Length > 0 && !IsSafeRelative(c.StripRoot)) problems.Add(who + "stripRoot must be a plain folder name");
   }
   return problems;
  }
 }

 // What the machine offers. Tests fill it in; Detect reads the real values.
 sealed class SetupFacts {
  public int WindowsBuild, DotNetRelease;
  public bool VcRuntime, Vulkan;
  public long FreeBytes = long.MaxValue;

  public static SetupFacts Detect(string root) {
   var facts = new SetupFacts();
   int build;
   int.TryParse(Convert.ToString(Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "CurrentBuildNumber", "0")), out build);
   facts.WindowsBuild = build;
   facts.DotNetRelease = Convert.ToInt32(Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", "Release", 0));
   string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
   facts.VcRuntime = File.Exists(Path.Combine(system, "vcruntime140.dll")) && File.Exists(Path.Combine(system, "msvcp140.dll"));
   facts.Vulkan = GraphicsMod.VulkanAvailable();
   try { facts.FreeBytes = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace; }
   catch (Exception) { facts.FreeBytes = long.MaxValue; }
   return facts;
  }
 }

 // Where the user's game lives, found by Locate.
 sealed class GameScan {
  public string Base, Update, Dlc, Save;
  public List<Msg> Notes = new List<Msg>();
  public bool HasGame { get { return Base != null && Update != null && Dlc != null; } }
 }

 static class SetupPaths {
  public static string Combine(string root, string relative) { return Path.Combine(root, relative.Replace('/', '\\')); }
  public static string Instance(string root, string name) { return Path.Combine(root, ".tools", "instances", name); }
  public static string Bcml(string root, string name) { return Path.Combine(root, ".tools", "localappdata", name == "cemuA" ? "bcml" : "bcml-b", "settings.json"); }
  public static string TitleMeta(string instance, string kind) { return Path.Combine(instance, "mlc01", "usr", "title", kind, "101c9400", "meta", "meta.xml"); }
  public static string SaveFolder(string instance) { return Path.Combine(instance, "mlc01", "usr", "save", "00050000", "101c9400"); }
  public static readonly string[] Instances = { "cemuA", "cemuB" };
 }

 static class Preflight {
  // Too long and network folders are already stopped by PathGuard before the launcher opens, so the wizard never sees them.
  // The hook stays for callers (and tests) that want the checklist to list a folder problem.
  public static Func<string, string> RootPathHook = null;

  public static List<SetupCheck> Run(string root, SetupManifest manifest, SetupFacts facts) {
   var checks = new List<SetupCheck>();
   foreach (var component in manifest.Components) checks.Add(CheckComponent(root, component));
   bool downloading = checks.Any(c => c.Action == SetupAction.Download && c.Status != SetupStatus.Ok);

   checks.Add(Input(root));
   checks.Add(Mod(root));
   checks.AddRange(Game(root, manifest));

   const string system = "System";
   checks.Add(Make(system, Msg.Say("Windows 10 or newer"), facts.WindowsBuild >= manifest.MinWindowsBuild ? SetupStatus.Ok : SetupStatus.Broken,
    Msg.Say("Windows build {0}", facts.WindowsBuild), Msg.Say("This launcher needs Windows 10 version 1809 or newer."), SetupAction.None, true));
   checks.Add(Make(system, Msg.Say(".NET Framework 4.8"), facts.DotNetRelease >= manifest.MinDotNetRelease ? SetupStatus.Ok : SetupStatus.Missing,
    Msg.Say("Release {0}", facts.DotNetRelease), Msg.Say("Install the .NET Framework 4.8 runtime from Microsoft."), SetupAction.None, true));
   checks.Add(Make(system, Msg.Say("Visual C++ runtime"), facts.VcRuntime ? SetupStatus.Ok : SetupStatus.Missing,
    Msg.Say("Needed by Cemu and Python"), Msg.Say("Install the Microsoft Visual C++ 2015-2022 x64 Redistributable (aka.ms/vs/17/release/vc_redist.x64.exe)."), SetupAction.None, true));
   checks.Add(Make(system, Msg.Say("Vulkan graphics"), facts.Vulkan ? SetupStatus.Ok : SetupStatus.Missing,
    Msg.Say("Both players render with Vulkan"), Msg.Say("Install the latest driver for your graphics card."), SetupAction.None, true));

   // Free space only matters while something has to be downloaded; a finished install never needs it again.
   bool room = !downloading || facts.FreeBytes >= manifest.FreeDiskBytes;
   checks.Add(Make(system, Msg.Say("Free disk space"), room ? SetupStatus.Ok : SetupStatus.Broken,
    downloading ? Msg.Say("{0} GB free, {1} GB needed", Math.Round(facts.FreeBytes / 1073741824.0, 1), Math.Round(manifest.FreeDiskBytes / 1073741824.0, 1)) : Msg.Say("Nothing to download"),
    Msg.Say("Free some disk space or install Hyrule Online on another drive."), SetupAction.None, true));

   string problem = RootPathHook == null ? null : RootPathHook(root);
   checks.Add(Make(system, Msg.Say("Install folder"), problem == null ? SetupStatus.Ok : SetupStatus.Broken, Msg.Text(root),
    problem == null ? null : Msg.Text(problem), SetupAction.None, true));
   return checks;
  }

  public static bool NeedsSetup(IEnumerable<SetupCheck> checks) { return checks.Any(c => c.Required && c.Status != SetupStatus.Ok); }

  static SetupCheck Make(string group, Msg item, SetupStatus status, Msg detail, Msg fix, SetupAction action, bool required) {
   return new SetupCheck { Group = group, Item = item, Status = status, Detail = detail, Fix = status == SetupStatus.Ok ? null : fix, Action = status == SetupStatus.Ok ? SetupAction.None : action, Required = required };
  }

  public static string Sha256(string file) {
   using (var sha = SHA256.Create())
   using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
    return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("X2")));
   }
  }

  public static SetupStatus CheckMarker(string file, SetupMarker marker, out Msg detail) {
   detail = null;
   if (!File.Exists(file)) { detail = Msg.Say("{0} is missing", marker.File); return SetupStatus.Missing; }
   if (!string.IsNullOrEmpty(marker.FileVersion)) {
    string version = FileVersionInfo.GetVersionInfo(file).ProductVersion ?? "";
    if (!version.StartsWith(marker.FileVersion, StringComparison.Ordinal)) { detail = Msg.Say("{0} is version {1}, {2} is needed", marker.File, version, marker.FileVersion); return SetupStatus.Broken; }
   }
   if (marker.Size > 0 && new FileInfo(file).Length != marker.Size) { detail = Msg.Say("{0} is not the official file", marker.File); return SetupStatus.Broken; }
   if (!string.IsNullOrEmpty(marker.Sha256) && !string.Equals(Sha256(file), marker.Sha256, StringComparison.OrdinalIgnoreCase)) { detail = Msg.Say("{0} is not the official file", marker.File); return SetupStatus.Broken; }
   return SetupStatus.Ok;
  }

  static SetupCheck CheckComponent(string root, SetupComponent component) {
   var status = SetupStatus.Ok;
   Msg detail = null;
   for (int i = 0; i < component.Targets.Count; i++) {
    string target = SetupPaths.Combine(root, component.Targets[i]);
    foreach (var marker in component.Markers) {
     if (marker.FirstTargetOnly && i > 0) continue;
     Msg found;
     var result = CheckMarker(Path.Combine(target, marker.File.Replace('/', '\\')), marker, out found);
     // A broken file outranks a missing one, and the first problem of that rank is the one that is shown.
     if (result == SetupStatus.Ok) continue;
     if (status == SetupStatus.Ok || (result == SetupStatus.Broken && status == SetupStatus.Missing)) { status = result; detail = found; }
    }
   }
   if (status == SetupStatus.Ok) detail = Msg.Text(component.Targets[0]);
   return new SetupCheck {
    Group = "Components", Component = component.Id, Item = Msg.Text(component.Name + " " + component.Version), Status = status, Detail = detail, Required = true,
    Fix = status == SetupStatus.Ok ? null : Msg.Say("Use Download what is missing."), Action = status == SetupStatus.Ok ? SetupAction.None : SetupAction.Download
   };
  }

  static SetupCheck Input(string root) {
   string folder = Path.Combine(root, ".artifacts", "input");
   bool ok = File.Exists(Path.Combine(folder, "InputRouter.exe")) && File.Exists(Path.Combine(folder, "Bridge", "xinput1_4.dll"));
   return Make("Components", Msg.Say("Controller router"), ok ? SetupStatus.Ok : SetupStatus.Missing, Msg.Text(".artifacts/input"),
    Msg.Say("Reinstall Hyrule Online, or run scripts/Build.ps1 when working from source."), SetupAction.None, true);
  }

  public const string ModComponent = "mod";

  // The merged multiplayer mod. The wizard builds it with scripts/Merge-Mod.ps1 once everything else is ready (see RESEARCH.md, "How setup works").
  static SetupCheck Mod(string root) {
   string merged = Path.Combine(root, ".tools", "localappdata", "bcml", "merged", "content", "Actor", "Pack", "Jugador1.sbactorpack");
   var check = Make("Components", Msg.Say("Multiplayer mod merged in BCML"), File.Exists(merged) ? SetupStatus.Ok : SetupStatus.Missing, Msg.Text(".tools/localappdata/bcml/merged"),
    Msg.Say("It is merged on its own once everything else is ready. Or run scripts/Merge-Mod.ps1."), SetupAction.None, true);
   check.Component = ModComponent;
   return check;
  }

  // True when the merge is the only thing left, so the wizard can run it without asking.
  public static bool OnlyModMissing(IEnumerable<SetupCheck> checks) {
   return checks.Any(c => c.Component == ModComponent && c.Status != SetupStatus.Ok) && checks.All(c => c.Component == ModComponent || !c.Required || c.Status == SetupStatus.Ok);
  }

  static IEnumerable<SetupCheck> Game(string root, SetupManifest manifest) {
   var content = new List<string>();
   foreach (string name in SetupPaths.Instances) {
    string folder = GameLanguages.ContentFolder(SetupPaths.Bcml(root, name));
    content.Add(folder != null && File.Exists(Path.Combine(Path.GetDirectoryName(folder.TrimEnd('\\', '/')) ?? "", "code", "U-King.rpx")) ? null : name);
   }
   yield return Make("Your game", Msg.Say("{0} (base game)", "Zelda: Breath of the Wild"), content.All(c => c == null) ? SetupStatus.Ok : SetupStatus.Missing,
    Msg.Say("Game folder known to BCML"), Msg.Say("Use Locate my game files and pick the folder with your game dump."), SetupAction.Locate, true);
   yield return Title(root, manifest, "0005000e", manifest.UpdateTitleId, manifest.UpdateVersion, Msg.Say("Game update v{0}", manifest.UpdateVersion));
   yield return Title(root, manifest, "0005000c", manifest.DlcTitleId, manifest.DlcVersion, Msg.Say("DLC v{0}", manifest.DlcVersion));
   bool saved = SetupPaths.Instances.All(i => Directory.Exists(Path.Combine(SetupPaths.SaveFolder(SetupPaths.Instance(root, i)), "user", "80000001")));
   yield return Make("Your game", Msg.Say("Save game to start from"), saved ? SetupStatus.Ok : SetupStatus.Missing, Msg.Say("A new game on an empty save hangs in Cemu"),
    Msg.Say("Use Locate my game files and pick a Cemu folder that has your save (user/80000001)."), SetupAction.Locate, true);
  }

  static SetupCheck Title(string root, SetupManifest manifest, string kind, string titleId, int version, Msg item) {
   var status = SetupStatus.Ok;
   Msg detail = null;
   foreach (string name in SetupPaths.Instances) {
    var meta = GameFiles.ReadMeta(SetupPaths.TitleMeta(SetupPaths.Instance(root, name), kind));
    if (meta == null) { if (status == SetupStatus.Ok) { status = SetupStatus.Missing; detail = Msg.Say("Not found for {0}", name); } }
    else if (!(string.Equals(meta.TitleId, titleId, StringComparison.OrdinalIgnoreCase) || (kind == "0005000e" && string.Equals(meta.TitleId, manifest.GameTitleId, StringComparison.OrdinalIgnoreCase))) || meta.Version != version) { status = SetupStatus.Broken; detail = Msg.Say("{0} has version {1}, version {2} is needed", name, meta.Version, version); break; }
   }
   return Make("Your game", item, status, status == SetupStatus.Ok ? Msg.Say("Linked into both Cemu folders") : detail,
    Msg.Say("Use Locate my game files and pick the folder with the right version."), SetupAction.Locate, true);
  }
 }

 sealed class TitleMeta { public string TitleId; public int Version; }

 // Finds and validates the user's own files and builds the Cemu folders around them. Nothing here writes to the user's folders.
 static class GameFiles {
  const int MaxDepth = 6, MaxFolders = 20000;
  static readonly string[] Skipped = { "shadercache", "graphicpacks", "$recycle.bin", "system volume information", "windows", "node_modules" };

  public static TitleMeta ReadMeta(string metaFile) {
   try {
    if (!File.Exists(metaFile)) return null;
    var root = XDocument.Load(metaFile).Root;
    string id = (string)root.Element("title_id");
    int version;
    if (id == null || !int.TryParse((string)root.Element("title_version"), out version)) return null;
    return new TitleMeta { TitleId = id.Trim().Replace("-", "").ToUpperInvariant(), Version = version };
   }
   catch (Exception) { return null; }
  }

  // Looks through a folder (a Cemu install, a mlc01 folder or a folder of dumps) for the game, the update, the DLC and a save.
  public static GameScan Scan(string folder, SetupManifest manifest) {
   var scan = new GameScan();
   if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { scan.Notes.Add(Msg.Say("That folder does not exist.")); return scan; }
   int budget = MaxFolders;
   var wrong = new List<KeyValuePair<string, Msg>>();
   Walk(folder, 0, ref budget, dir => Inspect(dir, manifest, scan, wrong));
   if (budget <= 0) scan.Notes.Add(Msg.Say("That folder has too many subfolders. Pick a smaller one."));
   if (scan.Base == null) scan.Notes.Add(Msg.Say("The base game (code/U-King.rpx with its content) was not found."));
   if (scan.Update == null) scan.Notes.Add(Msg.Say("The game update v{0} was not found.", manifest.UpdateVersion));
   if (scan.Dlc == null) scan.Notes.Add(Msg.Say("The DLC v{0} was not found.", manifest.DlcVersion));
   scan.Notes.AddRange(wrong.Where(w => (w.Key == "update" ? scan.Update : scan.Dlc) == null).Select(w => w.Value));
   return scan;
  }

  static void Walk(string dir, int depth, ref int budget, Action<string> visit) {
   if (budget <= 0) return;
   budget--;
   visit(dir);
   if (depth >= MaxDepth) return;
   string[] children;
   try { children = Directory.GetDirectories(dir); }
   catch (Exception) { return; }
   foreach (string child in children) {
    if (Skipped.Contains(Path.GetFileName(child).ToLowerInvariant())) continue;
    Walk(child, depth + 1, ref budget, visit);
   }
  }

  static void Inspect(string dir, SetupManifest manifest, GameScan scan, List<KeyValuePair<string, Msg>> wrong) {
   var meta = ReadMeta(Path.Combine(dir, "meta", "meta.xml"));
   if (meta != null) {
    // The update's meta.xml carries the base game's title id, so it is told apart by its folder or by its version: a base game dump is version 0.
    bool updateFolder = dir.Replace('/', '\\').ToLowerInvariant().Contains("\\0005000e\\");
    bool updateLike = meta.TitleId == manifest.UpdateTitleId || (meta.TitleId == manifest.GameTitleId && (updateFolder || meta.Version > 0));
    if (meta.TitleId == manifest.GameTitleId && !updateLike) {
     if (scan.Base == null && File.Exists(Path.Combine(dir, "code", "U-King.rpx")) && Directory.Exists(Path.Combine(dir, "content"))) scan.Base = dir;
    }
    else if (updateLike) {
     if (meta.Version == manifest.UpdateVersion) { if (scan.Update == null && Directory.Exists(Path.Combine(dir, "content"))) scan.Update = dir; }
     else wrong.Add(new KeyValuePair<string, Msg>("update", Msg.Say("The game update at {0} is v{1}, v{2} is needed.", dir, meta.Version, manifest.UpdateVersion)));
    }
    else if (meta.TitleId == manifest.DlcTitleId) {
     if (meta.Version == manifest.DlcVersion) { if (scan.Dlc == null && Directory.Exists(Path.Combine(dir, "content", "0010"))) scan.Dlc = dir; }
     else wrong.Add(new KeyValuePair<string, Msg>("dlc", Msg.Say("The DLC at {0} is v{1}, v{2} is needed.", dir, meta.Version, manifest.DlcVersion)));
    }
   }
   // Cemu keeps the account's saves in <mlc01>/usr/save/00050000/101c9400/user/80000001.
   if (scan.Save == null && Path.GetFileName(dir).Equals("101c9400", StringComparison.OrdinalIgnoreCase)
    && Path.GetFileName(Path.GetDirectoryName(dir) ?? "") == "00050000" && Directory.Exists(Path.Combine(dir, "user", "80000001"))) scan.Save = dir;
  }
 }

 sealed class LayoutResult {
  public bool Ok = true;
  public List<Msg> Log = new List<Msg>();
 }

 // Builds the instance folders the launcher expects (see RESEARCH.md, "Isolated install") around the user's game files.
 // What it leaves to the person is listed in RESEARCH.md, "How setup works".
 static class SetupLayout {
  static readonly string[] BcmlPacks = {
   "graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Mods\\ExtendedMemory\\rules.txt",
   "graphicPacks\\BreathOfTheWild_BCML\\rules.txt",
   "graphicPacks\\bcmlPatches\\MilkBarLauncher\\rules.txt"
  };

  public static LayoutResult Apply(string root, GameScan scan) {
   var result = new LayoutResult();
   if (scan == null || !scan.HasGame) { result.Ok = false; result.Log.Add(Msg.Say("The game, update and DLC must all be found first.")); return result; }
   foreach (string name in SetupPaths.Instances) {
    string instance = SetupPaths.Instance(root, name);
    string mlc = Path.Combine(instance, "mlc01");
    try {
     Directory.CreateDirectory(mlc);
     Link(Path.Combine(mlc, "usr", "title", "0005000e", "101c9400"), scan.Update, result);
     Link(Path.Combine(mlc, "usr", "title", "0005000c", "101c9400"), scan.Dlc, result);
     CopySave(instance, scan.Save, name, result);
     WriteSettings(instance, Path.GetDirectoryName(scan.Base));
     EnsureGraphicPacks(instance);
     WriteBcml(root, name, scan);
     result.Log.Add(Msg.Say("{0} is ready.", name));
    }
    catch (Exception error) {
     result.Ok = false;
     result.Log.Add(Msg.Say("{0} could not be set up: {1}", name, error.Message));
    }
   }
   return result;
  }

  static void Link(string link, string target, LayoutResult result) {
   if (Directory.Exists(link)) return;
   // A junction whose target moved still exists as a name, so it has to go before it can be made again.
   try { if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link, false); } catch (Exception) { }
   Directory.CreateDirectory(Path.GetDirectoryName(link));
   Junction(link, target);
   result.Log.Add(Msg.Say("Linked {0} to {1}.", link, target));
  }

  // Junctions need no administrator rights, unlike symbolic links.
  public static void Junction(string link, string target) {
   var start = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
   using (var process = Process.Start(start)) {
    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0 || !Directory.Exists(link)) throw new IOException("The folder link could not be created: " + output.Trim());
   }
  }

  static void CopySave(string instance, string save, string name, LayoutResult result) {
   string destination = SetupPaths.SaveFolder(instance);
   if (save == null || Directory.Exists(destination)) return;
   CopyFolder(save, destination);
   result.Log.Add(Msg.Say("Copied your save into {0}.", name));
  }

  static void CopyFolder(string from, string to) {
   Directory.CreateDirectory(to);
   foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
   foreach (string folder in Directory.GetDirectories(from)) CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
  }

  // Cemu fills in everything it does not find, so a new settings file only needs the parts that tie it to this folder.
  static void WriteSettings(string instance, string gameFolder) {
   string file = Path.Combine(instance, "settings.xml");
   var document = File.Exists(file) ? XDocument.Load(file) : new XDocument(new XElement("content"));
   var content = document.Root;
   Set(content, "mlc_path", Path.Combine(instance, "mlc01"), true);
   Set(content, "permanent_storage", "true", false);
   Set(content, "check_update", "false", false);
   Set(content, "vk_warning", "false", false);
   Set(content, "gp_download", "true", false);
   var paths = content.Element("GamePaths");
   if (paths == null) { paths = new XElement("GamePaths"); content.Add(paths); }
   if (!paths.Elements("Entry").Any(e => string.Equals((string)e, gameFolder, StringComparison.OrdinalIgnoreCase))) paths.Add(new XElement("Entry", gameFolder));
   Save(document, file);
  }

  static void Set(XElement parent, string name, string value, bool overwrite) {
   var element = parent.Element(name);
   if (element == null) parent.Add(new XElement(name, value)); else if (overwrite) element.Value = value;
  }

  static void Save(XDocument document, string file) {
   using (var writer = XmlWriter.Create(file, new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding(false) })) document.Save(writer);
  }

  // Turns on the packs the mod needs once their files exist, so it can run again after the BCML export is in place.
  public static void EnsureGraphicPacks(string instance) {
   string file = Path.Combine(instance, "settings.xml");
   if (!File.Exists(file)) return;
   var document = XDocument.Load(file);
   var packs = document.Root.Element("GraphicPack");
   if (packs == null) { packs = new XElement("GraphicPack"); document.Root.Add(packs); }
   bool changed = false;
   foreach (string pack in BcmlPacks) {
    if (!File.Exists(Path.Combine(instance, pack)) || packs.Elements("Entry").Any(e => string.Equals((string)e.Attribute("filename"), pack, StringComparison.OrdinalIgnoreCase))) continue;
    packs.Add(new XElement("Entry", new XAttribute("filename", pack)));
    changed = true;
   }
   if (changed) Save(document, file);
  }

  // BCML's own settings, one file per player; they never touch the global BCML install (RESEARCH.md, "Isolation caveats").
  static void WriteBcml(string root, string name, GameScan scan) {
   string file = SetupPaths.Bcml(root, name);
   if (File.Exists(file)) return;
   string instance = SetupPaths.Instance(root, name);
   string store = name == "cemuA" ? Path.GetDirectoryName(file) : Path.Combine(Path.GetDirectoryName(file), "store");
   var settings = new Dictionary<string, object> {
    { "cemu_dir", instance }, { "game_dir", Path.Combine(scan.Base, "content") }, { "game_dir_nx", "" }, { "update_dir", Path.Combine(scan.Update, "content") },
    { "dlc_dir", Path.Combine(scan.Dlc, "content", "0010") }, { "dlc_dir_nx", "" }, { "store_dir", store }, { "export_dir", Path.Combine(instance, "graphicPacks", "BreathOfTheWild_BCML") },
    { "export_dir_nx", "" }, { "load_reverse", false }, { "site_meta", "" }, { "dark_theme", false }, { "no_guess", false }, { "lang", "USen" }, { "no_cemu", false },
    { "wiiu", true }, { "no_hardlinks", true }, { "force_7z", false }, { "suppress_update", true }, { "nsfw", false }, { "last_version", "3.10.8" }, { "changelog", false },
    { "strip_gfx", false }, { "auto_gb", false }, { "show_gb", false }
   };
   Directory.CreateDirectory(store);
   File.WriteAllText(file, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
  }
 }

 // One line of scripts/Install-Components.ps1 output: SETUP|KIND|id|state|text.
 //   STATUS|id|OK/MISSING/BROKEN|detail   what -Check found, one line per component
 //   PROGRESS|id|0-100|text               overall progress while installing
 //   STEP|id|start/done/skipped/failed|text
 //   DONE|all|OK/FAILED|text              always the last line
 sealed class SetupMessage {
  public string Kind, Id, State, Text;
  public int Percent;

  public static SetupMessage Parse(string line) {
   if (line == null || !line.StartsWith("SETUP|", StringComparison.Ordinal)) return null;
   string[] parts = line.TrimEnd('\r', '\n').Split(new[] { '|' }, 5);
   if (parts.Length < 4) return null;
   var message = new SetupMessage { Kind = parts[1], Id = parts[2], State = parts[3], Text = parts.Length > 4 ? parts[4] : "" };
   if (message.Kind == "PROGRESS") { int percent; if (!int.TryParse(message.State, out percent)) return null; message.Percent = Math.Max(0, Math.Min(100, percent)); }
   return message;
  }

  public SetupStatus Status { get { return State == "OK" ? SetupStatus.Ok : State == "BROKEN" ? SetupStatus.Broken : SetupStatus.Missing; } }
 }
}
