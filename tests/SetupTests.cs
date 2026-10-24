using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HyruleOnline {
 // Tests for the first-run setup: the manifest, the preflight, the game-file locator, the installer script and its output.
 // Everything runs in temp folders, offline or against a local server; nothing here touches a real install.
 static class SetupTests {
  static int failures;
  static string root, temp;

  static void Require(bool condition, string what) {
   if (condition) return;
   failures++;
   Console.WriteLine("FAIL: " + what);
  }

  static string NewTemp(string name) {
   string path = Path.Combine(temp, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
   Directory.CreateDirectory(path);
   return path;
  }

  static void Write(string file, string text) {
   Directory.CreateDirectory(Path.GetDirectoryName(file));
   File.WriteAllText(file, text, new UTF8Encoding(false));
  }

  static void Unlink(string dir) {
   foreach (string sub in Directory.GetDirectories(dir)) {
    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) Directory.Delete(sub, false); else Unlink(sub);
   }
  }

  static void Clean(string dir) {
   try { Unlink(dir); Directory.Delete(dir, true); } catch (Exception) { }
  }

  // A small manifest with one zip component, enough to run the whole pipeline without the real downloads.
  static string TinyManifest(string url, string zipHash, string markerHash, long size) {
   return "{ \"schema\": 1, \"requirements\": { \"freeDiskBytes\": 1000, \"minWindowsBuild\": 17763, \"minDotNetRelease\": 528040, \"gameTitleId\": \"00050000101C9400\", \"updateTitleId\": \"0005000E101C9400\", \"updateVersion\": 208, \"dlcTitleId\": \"0005000C101C9400\", \"dlcVersion\": 80 },"
    + " \"components\": [ { \"id\": \"tiny\", \"name\": \"Tiny\", \"version\": \"1\", \"kind\": \"zip\", \"url\": \"" + url + "\", \"sha256\": \"" + zipHash + "\", \"size\": " + size + ", \"stripRoot\": \"pack\","
    + " \"targets\": [\"tools/one\", \"tools/two\"], \"markers\": [ { \"file\": \"data/a.txt\", \"sha256\": \"" + markerHash + "\", \"size\": 5 } ], \"requiredFor\": \"tests\", \"verified\": \"tests\" } ] }";
  }

  static string MakeZip(string folder, bool slip) {
   string zip = Path.Combine(folder, slip ? "slip.zip" : "pack.zip");
   using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
    var entry = archive.CreateEntry(slip ? "pack/../../escaped.txt" : "pack/data/a.txt");
    using (var stream = new StreamWriter(entry.Open())) stream.Write("hello");
    var other = archive.CreateEntry("outside/ignored.txt");
    using (var stream = new StreamWriter(other.Open())) stream.Write("x");
   }
   return zip;
  }

  static string Hash(string text) {
   string file = Path.Combine(temp, "hash-" + Guid.NewGuid().ToString("N"));
   File.WriteAllText(file, text, new UTF8Encoding(false));
   try { return Preflight.Sha256(file); } finally { File.Delete(file); }
  }

  static SetupFacts GoodFacts() { return new SetupFacts { WindowsBuild = 19045, DotNetRelease = 528049, VcRuntime = true, Vulkan = true, FreeBytes = 50L * 1073741824 }; }

  static string Meta(string id, int version) { return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<menu><title_id type=\"hexBinary\" length=\"8\">" + id + "</title_id><title_version type=\"unsignedInt\" length=\"4\">" + version + "</title_version></menu>"; }

  // The user's own files as Cemu or a dump tool lays them out.
  static string MakeDump(string folder, int updateVersion, int dlcVersion) {
   string dump = Path.Combine(folder, "dump");
   Write(Path.Combine(dump, "game", "code", "U-King.rpx"), "rpx");
   Write(Path.Combine(dump, "game", "content", "Pack", "a.pack"), "a");
   Write(Path.Combine(dump, "game", "meta", "meta.xml"), Meta("00050000101C9400", 0));
   Write(Path.Combine(dump, "update", "code", "U-King.rpx"), "rpx");
   Write(Path.Combine(dump, "update", "content", "Pack", "b.pack"), "b");
   Write(Path.Combine(dump, "update", "meta", "meta.xml"), Meta("00050000101C9400", updateVersion));
   Write(Path.Combine(dump, "dlc", "content", "0010", "c.pack"), "c");
   Write(Path.Combine(dump, "dlc", "meta", "meta.xml"), Meta("0005000C101C9400", dlcVersion));
   Write(Path.Combine(dump, "mlc01", "usr", "save", "00050000", "101c9400", "user", "80000001", "game_data.sav"), "save");
   Write(Path.Combine(dump, "mlc01", "usr", "save", "00050000", "101c9400", "meta", "meta.xml"), "<meta/>");
   return dump;
  }

  static string TinyRoot(SetupManifest manifest, string name) {
   string install = NewTemp(name);
   foreach (string target in new[] { "tools/one", "tools/two" }) Write(Path.Combine(install, target.Replace('/', '\\'), "data", "a.txt"), "hello");
   Write(Path.Combine(install, ".artifacts", "input", "InputRouter.exe"), "x");
   Write(Path.Combine(install, ".artifacts", "input", "Bridge", "xinput1_4.dll"), "x");
   Write(Path.Combine(install, ".tools", "localappdata", "bcml", "merged", "content", "Actor", "Pack", "Jugador1.sbactorpack"), "x");
   return install;
  }

  static string TinyJson(string zipHash) { return TinyManifest("https://example.invalid/pack.zip", zipHash, Hash("hello"), 100); }

  static SetupCheck Find(List<SetupCheck> checks, string english) { return checks.FirstOrDefault(c => c.Item.ToString() == english || (c.Item.Plain && c.Item.English.StartsWith(english))); }

  static void Manifest() {
   var manifest = SetupManifest.Load(root);
   var problems = manifest.Validate();
   Require(problems.Count == 0, "the real manifest is valid: " + string.Join("; ", problems.ToArray()));
   Require(new[] { "cemu", "milkbar", "graphicpacks", "python", "bcml" }.All(id => manifest.Find(id) != null), "the manifest lists every component");
   foreach (var c in manifest.Components.Where(c => c.Kind != "pip")) Require(SetupManifest.IsSha256(c.Sha256) && c.Url.StartsWith("https://") && c.Size > 0, c.Id + " has an https url, a size and a SHA-256");
   Require(manifest.Find("milkbar").Url.Contains("MilkBarModding/MilkBarLauncher/releases/download/"), "Milk Bar comes from its own GitHub release");
   Require(manifest.Find("cemu").Url.StartsWith("https://cemu.info/") && manifest.Find("python").Url.StartsWith("https://www.python.org/"), "Cemu and Python come from their official sites");
   Require(manifest.Components.All(c => c.RequiredFor.Length > 0), "every component says what it is required for");
   Require(manifest.Components.All(c => !Regex.IsMatch(c.Url, "game|update|dlc|\\.rpx", RegexOptions.IgnoreCase) || c.Id == "milkbar"), "no download is the user's game");

   var bad = TinyManifest("http://insecure/x.zip", "abc", "zz", 1);
   var parsed = SetupManifest.Parse(bad);
   var found = parsed.Validate();
   Require(found.Any(p => p.Contains("https")) && found.Any(p => p.Contains("sha256 must be 64")) && found.Any(p => p.Contains("marker sha256")), "a bad url and bad hashes are reported");
   var escape = SetupManifest.Parse(TinyManifest("https://x/y.zip", new string('a', 64), new string('b', 64), 1).Replace("\"tools/one\"", "\"../outside\""));
   Require(escape.Validate().Any(p => p.Contains("targets")), "a target that leaves the install folder is rejected");
   Require(!SetupManifest.IsSafeRelative("C:\\x") && !SetupManifest.IsSafeRelative("a/../b") && !SetupManifest.IsSafeRelative("/a") && SetupManifest.IsSafeRelative("a/b.txt"), "relative path rules");
   Require(SetupManifest.IsSha256(new string('A', 64)) && !SetupManifest.IsSha256(new string('g', 64)) && !SetupManifest.IsSha256("abc"), "SHA-256 format");
   bool threw = false;
   try { SetupManifest.Parse("[1,2]"); } catch (FormatException) { threw = true; }
   Require(threw, "a manifest that is not an object is refused");

   string requirements = File.ReadAllText(Path.Combine(root, "scripts", "bcml-requirements.txt"));
   var packages = Regex.Matches(requirements, "(?m)^([A-Za-z0-9_.\\-]+)==([0-9][^ \\\\\\r\\n]*)((?: \\\\\\r?\\n\\s+--hash=sha256:[0-9a-f]{64})+)\\s*$");
   int lines = requirements.Split('\n').Count(l => Regex.IsMatch(l, "^[A-Za-z0-9_.\\-]+=="));
   Require(packages.Count >= 10 && packages.Count == lines, "every BCML requirement is pinned with at least one SHA-256 (" + packages.Count + " of " + lines + ")");
   Require(requirements.IndexOf("bcml==3.10.8", StringComparison.OrdinalIgnoreCase) >= 0, "BCML 3.10.8 is pinned");
  }

  static void PreflightLayouts() {
   var real = SetupManifest.Load(root);
   string empty = NewTemp("empty");
   var checks = Preflight.Run(empty, real, GoodFacts());
   Require(Preflight.NeedsSetup(checks), "an empty folder needs setup");
   foreach (var c in real.Components) {
    var check = checks.First(x => x.Component == c.Id);
    Require(check.Status == SetupStatus.Missing && check.Action == SetupAction.Download && check.Required && check.Fix != null, c.Id + " is missing and can be downloaded");
   }
   Require(Find(checks, "Zelda: Breath of the Wild (base game)").Action == SetupAction.Locate && Find(checks, "Save game to start from").Action == SetupAction.Locate, "game files are located, not downloaded");

   var small = SetupManifest.Parse(TinyJson(new string('c', 64)));
   string install = TinyRoot(small, "complete");
   var scan = GameFiles.Scan(MakeDump(install, 208, 80), small);
   Require(scan.HasGame && scan.Save != null, "the dump is found");
   var applied = SetupLayout.Apply(install, scan);
   Require(applied.Ok, "the layout is built");
   checks = Preflight.Run(install, small, GoodFacts());
   Require(!Preflight.NeedsSetup(checks), "a complete install needs no setup: " + string.Join(", ", checks.Where(c => c.Status != SetupStatus.Ok).Select(c => c.Item.ToString()).ToArray()));
   Require(checks.All(c => c.Status == SetupStatus.Ok && c.Action == SetupAction.None && c.Fix == null), "a complete install has nothing to fix");

   var full = GoodFacts();
   full.FreeBytes = 0;
   Require(!Preflight.NeedsSetup(Preflight.Run(install, small, full)), "a complete install never trips on a full disk");

   File.WriteAllText(Path.Combine(install, "tools", "two", "data", "a.txt"), "HELLO");
   checks = Preflight.Run(install, small, GoodFacts());
   var broken = checks.First(c => c.Component == "tiny");
   Require(broken.Status == SetupStatus.Broken && broken.Action == SetupAction.Download && Preflight.NeedsSetup(checks), "a changed file is broken and needs a download");
   Directory.Delete(Path.Combine(install, "tools", "two", "data"), true);
   Require(Preflight.Run(install, small, GoodFacts()).First(c => c.Component == "tiny").Status == SetupStatus.Missing, "a removed file is missing");
   Write(Path.Combine(install, "tools", "two", "data", "a.txt"), "hello");

   var low = GoodFacts();
   low.FreeBytes = 10;
   var lowChecks = Preflight.Run(NewTemp("low"), small, low);
   Require(Find(lowChecks, "Free disk space").Status == SetupStatus.Broken, "too little disk space is reported while something must be downloaded");

   foreach (var tweak in new Action<SetupFacts>[] { f => f.WindowsBuild = 10000, f => f.DotNetRelease = 0, f => f.VcRuntime = false, f => f.Vulkan = false }) {
    var facts = GoodFacts();
    tweak(facts);
    Require(Preflight.NeedsSetup(Preflight.Run(install, small, facts)), "a missing system requirement needs setup");
   }

   Preflight.RootPathHook = delegate(string folder) { return "not allowed"; };
   try {
    var hooked = Preflight.Run(install, small, GoodFacts());
    Require(Find(hooked, "Install folder").Status == SetupStatus.Broken && Preflight.NeedsSetup(hooked), "the install folder hook is honoured");
   }
   finally { Preflight.RootPathHook = null; }

   File.Delete(Path.Combine(install, ".artifacts", "input", "InputRouter.exe"));
   Require(Preflight.NeedsSetup(Preflight.Run(install, small, GoodFacts())), "a missing router needs setup");
   foreach (string folder in new[] { empty, install }) Clean(folder);
  }

  static void Locator() {
   var manifest = SetupManifest.Parse(TinyJson(new string('c', 64)));
   string install = NewTemp("locator");
   try {
    var scan = GameFiles.Scan(MakeDump(install, 208, 80), manifest);
    Require(scan.Base != null && scan.Base.EndsWith("game") && scan.Update.EndsWith("update") && scan.Dlc.EndsWith("dlc") && scan.Save.EndsWith("101c9400"), "base, update v208, DLC v80 and the save are told apart");
    Require(scan.Notes.Count == 0, "a good dump has no notes");

    string old = NewTemp("old");
    var wrong = GameFiles.Scan(MakeDump(old, 192, 64), manifest);
    Require(wrong.Base != null && wrong.Update == null && wrong.Dlc == null && !wrong.HasGame, "other versions are not accepted");
    Require(wrong.Notes.Any(n => n.ToString().Contains("v192")) && wrong.Notes.Any(n => n.ToString().Contains("v64")), "the notes say which versions were found");
    Require(GameFiles.Scan(Path.Combine(install, "missing"), manifest).Notes.Count == 1, "a folder that is not there is explained");
    Require(GameFiles.Scan(NewTemp("blank"), manifest).Notes.Count == 3, "an empty folder lists what is missing");

    string cemu = NewTemp("cemuinstall");
    Write(Path.Combine(cemu, "mlc01", "usr", "title", "0005000e", "101c9400", "meta", "meta.xml"), Meta("00050000101C9400", 208));
    Directory.CreateDirectory(Path.Combine(cemu, "mlc01", "usr", "title", "0005000e", "101c9400", "content"));
    Write(Path.Combine(cemu, "mlc01", "usr", "title", "0005000c", "101c9400", "meta", "meta.xml"), Meta("0005000C101C9400", 80));
    Directory.CreateDirectory(Path.Combine(cemu, "mlc01", "usr", "title", "0005000c", "101c9400", "content", "0010"));
    Write(Path.Combine(cemu, "graphicPacks", "shadercache", "ignored", "meta", "meta.xml"), Meta("0005000C101C9400", 1));
    var inCemu = GameFiles.Scan(cemu, manifest);
    Require(inCemu.Update != null && inCemu.Dlc != null && inCemu.Base == null, "a Cemu folder's update (base title id, 0005000e folder) and DLC are recognised");

    var meta = GameFiles.ReadMeta(Path.Combine(scan.Update, "meta", "meta.xml"));
    Require(meta != null && meta.TitleId == "00050000101C9400" && meta.Version == 208, "meta.xml is read");
    Write(Path.Combine(install, "bad.xml"), "<not xml");
    Require(GameFiles.ReadMeta(Path.Combine(install, "bad.xml")) == null && GameFiles.ReadMeta(Path.Combine(install, "none.xml")) == null, "a broken meta.xml is ignored");

    string target = NewTemp("instances");
    var result = SetupLayout.Apply(target, scan);
    Require(result.Ok, "Apply succeeds");
    foreach (string name in SetupPaths.Instances) {
     string instance = SetupPaths.Instance(target, name);
     string update = SetupPaths.TitleMeta(instance, "0005000e"), dlc = SetupPaths.TitleMeta(instance, "0005000c");
     Require(File.Exists(update) && File.Exists(dlc), name + " sees the update and DLC through links");
     Require((File.GetAttributes(Path.GetDirectoryName(Path.GetDirectoryName(update))) & FileAttributes.ReparsePoint) != 0, name + " links, it does not copy, the update");
     Require(File.Exists(Path.Combine(SetupPaths.SaveFolder(instance), "user", "80000001", "game_data.sav")), name + " has a copy of the save");
     Require((File.GetAttributes(SetupPaths.SaveFolder(instance)) & FileAttributes.ReparsePoint) == 0, name + " owns its save");
     string settings = File.ReadAllText(Path.Combine(instance, "settings.xml"));
     Require(settings.Contains(Path.Combine(instance, "mlc01")) && settings.Contains(Path.Combine(install, "dump")) && settings.Contains("<check_update>false</check_update>"), name + " settings point at its own mlc01 and the game");
     string bcml = File.ReadAllText(SetupPaths.Bcml(target, name));
     Require(bcml.Contains("game_dir") && GameLanguages.ContentFolder(SetupPaths.Bcml(target, name)) == Path.Combine(scan.Base, "content"), name + " BCML settings find the game");
    }
    Require(Directory.Exists(Path.Combine(scan.Update, "content")) && File.Exists(Path.Combine(scan.Save, "user", "80000001", "game_data.sav")), "the user's own folders are untouched");

    File.WriteAllText(Path.Combine(SetupPaths.SaveFolder(SetupPaths.Instance(target, "cemuA")), "user", "80000001", "game_data.sav"), "progress");
    var again = SetupLayout.Apply(target, scan);
    Require(again.Ok && File.ReadAllText(Path.Combine(SetupPaths.SaveFolder(SetupPaths.Instance(target, "cemuA")), "user", "80000001", "game_data.sav")) == "progress", "running it again keeps the player's progress");
    Require(!SetupLayout.Apply(NewTemp("nogame"), wrong).Ok, "it refuses without a complete game");

    string pack = Path.Combine(SetupPaths.Instance(target, "cemuA"), "graphicPacks", "bcmlPatches", "MilkBarLauncher", "rules.txt");
    Write(pack, "[Definition]");
    SetupLayout.EnsureGraphicPacks(SetupPaths.Instance(target, "cemuA"));
    SetupLayout.EnsureGraphicPacks(SetupPaths.Instance(target, "cemuA"));
    string text = File.ReadAllText(Path.Combine(SetupPaths.Instance(target, "cemuA"), "settings.xml"));
    Require(Regex.Matches(text, "bcmlPatches").Count == 1, "a pack that appears later is switched on once");

    foreach (string folder in new[] { target, cemu, old }) Clean(folder);
   }
   finally { Clean(install); }
  }

  static void Messages() {
   var status = SetupMessage.Parse("SETUP|STATUS|cemu|BROKEN|Cemu.exe is not the official file");
   Require(status != null && status.Kind == "STATUS" && status.Id == "cemu" && status.Status == SetupStatus.Broken && status.Text.Contains("official"), "a status line");
   var progress = SetupMessage.Parse("SETUP|PROGRESS|all|42|Cemu 1.26.2f\r\n");
   Require(progress != null && progress.Percent == 42 && progress.Text == "Cemu 1.26.2f", "a progress line");
   Require(SetupMessage.Parse("SETUP|PROGRESS|all|abc|x") == null && SetupMessage.Parse("hello") == null && SetupMessage.Parse(null) == null && SetupMessage.Parse("SETUP|STEP") == null, "other output is ignored");
   Require(SetupMessage.Parse("SETUP|PROGRESS|all|250|x").Percent == 100, "progress is clamped");
   Require(SetupMessage.Parse("SETUP|STEP|x|failed|a|b").Text == "a|b", "text may contain the separator");
  }

  class Output { public int Code; public List<string> Lines = new List<string>(); public string All { get { return string.Join("\n", Lines.ToArray()); } } }

  static Output Powershell(string arguments) {
   var start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass " + arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
   var output = new Output();
   using (var process = Process.Start(start)) {
    var error = process.StandardError.ReadToEndAsync();
    string line;
    while ((line = process.StandardOutput.ReadLine()) != null) output.Lines.Add(line);
    process.WaitForExit();
    output.Code = process.ExitCode;
    if (error.Result.Trim().Length > 0) output.Lines.Add("STDERR " + error.Result.Trim());
   }
   return output;
  }

  static string Script { get { return Path.Combine(root, "scripts", "Install-Components.ps1"); } }

  static void CheckMode() {
   string empty = NewTemp("check");
   var real = SetupManifest.Load(root);
   foreach (string flag in new[] { "-Check", "-WhatIf" }) {
    var output = Powershell("-File \"" + Script + "\" " + flag + " -Root \"" + empty + "\"");
    var messages = output.Lines.Select(SetupMessage.Parse).Where(m => m != null).ToList();
    Require(output.Code == 1, flag + " exits 1 while something is missing: " + output.All);
    var statuses = messages.Where(m => m.Kind == "STATUS").ToList();
    Require(statuses.Count == real.Components.Count && statuses.All(m => m.Status == SetupStatus.Missing) && real.Components.All(c => statuses.Any(m => m.Id == c.Id)), flag + " reports every component as missing");
    Require(messages.Last().Kind == "DONE" && messages.Last().State == "PENDING", flag + " ends with DONE PENDING");
   }
   Require(Directory.GetFileSystemEntries(empty).Length == 0, "-Check changes nothing");

   string zipFolder = NewTemp("zip");
   string zip = MakeZip(zipFolder, false);
   string manifest = Path.Combine(zipFolder, "tiny.json");
   Write(manifest, TinyManifest(new Uri(zip).AbsoluteUri, Preflight.Sha256(zip), Hash("hello"), new FileInfo(zip).Length));
   var parsed = SetupManifest.Parse(File.ReadAllText(manifest));

   var ok = Powershell("-File \"" + Script + "\" -Check -Root \"" + empty + "\" -ManifestPath \"" + manifest + "\"");
   Require(ok.Code == 1 && ok.Lines.Any(l => l.StartsWith("SETUP|STATUS|tiny|MISSING|")), "the fixture is missing at first");

   // The full pipeline from a local file: verify, extract with the root folder stripped, copy to every target, mark progress.
   var install = Powershell("-File \"" + Script + "\" -Root \"" + empty + "\" -ManifestPath \"" + manifest + "\" -CacheDir \"" + Path.Combine(zipFolder, "cache") + "\"");
   var lines = install.Lines.Select(SetupMessage.Parse).Where(m => m != null).ToList();
   Require(install.Code == 0 && lines.Last().Kind == "DONE" && lines.Last().State == "OK", "the fixture installs: " + install.All);
   Require(lines.Any(m => m.Kind == "STEP" && m.State == "start") && lines.Any(m => m.Kind == "STEP" && m.State == "done") && lines.Any(m => m.Kind == "PROGRESS" && m.Percent == 100), "the install reports its steps and progress");
   Require(File.ReadAllText(Path.Combine(empty, "tools", "one", "data", "a.txt")) == "hello" && File.Exists(Path.Combine(empty, "tools", "two", "data", "a.txt")) && !Directory.Exists(Path.Combine(empty, "tools", "one", "outside")), "files land in every target without the wrapper folder");
   Require(Preflight.Run(empty, parsed, GoodFacts()).First(c => c.Component == "tiny").Status == SetupStatus.Ok, "the C# preflight agrees that the fixture is installed");
   var recheck = Powershell("-File \"" + Script + "\" -Check -Root \"" + empty + "\" -ManifestPath \"" + manifest + "\"");
   Require(recheck.Code == 0 && recheck.Lines.Any(l => l.StartsWith("SETUP|STATUS|tiny|OK|")) && recheck.Lines.Last().StartsWith("SETUP|DONE|all|OK|"), "-Check agrees after the install");
   var second = Powershell("-File \"" + Script + "\" -Root \"" + empty + "\" -ManifestPath \"" + manifest + "\" -CacheDir \"" + Path.Combine(zipFolder, "cache") + "\"");
   Require(second.Code == 0 && !second.Lines.Any(l => l.StartsWith("SETUP|STEP|tiny|start")), "running it again installs nothing");

   File.WriteAllText(Path.Combine(empty, "tools", "one", "data", "a.txt"), "HELLO");
   var broken = Powershell("-File \"" + Script + "\" -Check -Root \"" + empty + "\" -ManifestPath \"" + manifest + "\"");
   Require(broken.Code == 1 && broken.Lines.Any(l => l.StartsWith("SETUP|STATUS|tiny|BROKEN|")), "-Check sees a changed file as broken");
   var repair = Powershell("-File \"" + Script + "\" -Root \"" + empty + "\" -ManifestPath \"" + manifest + "\" -CacheDir \"" + Path.Combine(zipFolder, "cache") + "\"");
   Require(repair.Code == 0 && File.ReadAllText(Path.Combine(empty, "tools", "one", "data", "a.txt")) == "hello", "a broken component is put right");

   string refused = NewTemp("refused");
   Write(manifest, TinyManifest(new Uri(zip).AbsoluteUri, new string('0', 64), Hash("hello"), 1));
   var bad = Powershell("-File \"" + Script + "\" -Root \"" + refused + "\" -ManifestPath \"" + manifest + "\" -CacheDir \"" + Path.Combine(refused, "cache") + "\"");
   Require(bad.Code == 1 && bad.Lines.Any(l => l.StartsWith("SETUP|DONE|all|FAILED|")) && !Directory.Exists(Path.Combine(refused, "tools")) && Directory.GetFiles(Path.Combine(refused, "cache")).Length == 0, "a zip with the wrong checksum installs nothing and is deleted: " + bad.All);
   foreach (string folder in new[] { empty, zipFolder, refused }) Clean(folder);
  }

  static void ZipSafety() {
   string folder = NewTemp("slip");
   string zip = MakeZip(folder, true);
   string destination = Path.Combine(folder, "out");
   var result = Powershell("-Command \". '" + Script + "' -LibraryOnly -Root '" + folder + "'; try { Expand-Zip '" + zip + "' '" + destination + "' 'pack'; 'RESULT|extracted' } catch { 'RESULT|refused' }\"");
   Require(result.Lines.Contains("RESULT|refused") && !File.Exists(Path.Combine(folder, "escaped.txt")) && !File.Exists(Path.Combine(Path.GetDirectoryName(folder), "escaped.txt")), "a zip entry that climbs out of the folder is refused: " + result.All);
   Clean(folder);
  }

  // A local server with Range support, to exercise the checksum, the removal of bad files and the resume.
  static void Downloads() {
   byte[] data = new byte[1500000];
   new Random(7).NextBytes(data);
   string folder = NewTemp("download");
   string source = Path.Combine(folder, "good.bin");
   File.WriteAllBytes(source, data);
   string good = Preflight.Sha256(source);
   var listener = new HttpListener();
   int port = 38000 + new Random().Next(1500);
   listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
   listener.Start();
   var ranges = new List<string>();
   var worker = new Thread(delegate() {
    while (listener.IsListening) {
     HttpListenerContext context;
     try { context = listener.GetContext(); } catch (Exception) { return; }
     try {
      string range = context.Request.Headers["Range"];
      if (range != null) ranges.Add(range);
      int from = 0;
      if (range != null) { var m = Regex.Match(range, "bytes=(\\d+)-"); from = int.Parse(m.Groups[1].Value); context.Response.StatusCode = 206; }
      context.Response.ContentLength64 = data.Length - from;
      context.Response.OutputStream.Write(data, from, data.Length - from);
      context.Response.OutputStream.Close();
     }
     catch (Exception) { }
    }
   });
   worker.IsBackground = true;
   worker.Start();
   try {
    string url = "http://127.0.0.1:" + port + "/good.bin";
    string library = ". '" + Script + "' -LibraryOnly -Root '" + folder + "'; ";
    string target = Path.Combine(folder, "cache", "good.bin");
    var pass = Powershell("-Command \"" + library + "try { Get-VerifiedFile '" + url + "' '" + target + "' '" + good + "' $null; 'RESULT|ok' } catch { 'RESULT|fail|' + $_.Exception.Message }\"");
    Require(pass.Lines.Contains("RESULT|ok") && File.Exists(target) && Preflight.Sha256(target) == good && !File.Exists(target + ".part"), "a download with the right checksum is kept: " + pass.All);

    var again = Powershell("-Command \"" + library + "try { Get-VerifiedFile 'http://127.0.0.1:1/never' '" + target + "' '" + good + "' $null; 'RESULT|ok' } catch { 'RESULT|fail' }\"");
    Require(again.Lines.Contains("RESULT|ok"), "a good file already in the cache is not downloaded again");

    string wrong = Path.Combine(folder, "cache", "bad.bin");
    var fail = Powershell("-Command \"" + library + "try { Get-VerifiedFile '" + url + "' '" + wrong + "' '" + new string('0', 64) + "' $null; 'RESULT|ok' } catch { 'RESULT|fail|' + $_.Exception.Message }\"");
    Require(fail.Lines.Any(l => l.StartsWith("RESULT|fail|") && l.Contains("Checksum mismatch")) && !File.Exists(wrong) && !File.Exists(wrong + ".part"), "a download with the wrong checksum fails and leaves no file: " + fail.All);

    string tampered = Path.Combine(folder, "cache", "tampered.bin");
    File.WriteAllText(tampered, "not the file");
    var replaced = Powershell("-Command \"" + library + "try { Get-VerifiedFile '" + url + "' '" + tampered + "' '" + good + "' $null; 'RESULT|ok' } catch { 'RESULT|fail' }\"");
    Require(replaced.Lines.Contains("RESULT|ok") && Preflight.Sha256(tampered) == good, "a bad file in the cache is replaced");

    string resumed = Path.Combine(folder, "cache", "resume.bin");
    using (var part = new FileStream(resumed + ".part", FileMode.Create)) part.Write(data, 0, 600000);
    ranges.Clear();
    var resume = Powershell("-Command \"" + library + "try { Get-VerifiedFile '" + url.Replace("good.bin", "resume.bin") + "' '" + resumed + "' '" + good + "' $null; 'RESULT|ok' } catch { 'RESULT|fail|' + $_.Exception.Message }\"");
    Require(resume.Lines.Contains("RESULT|ok") && Preflight.Sha256(resumed) == good && ranges.Contains("bytes=600000-"), "an interrupted download continues where it stopped: " + resume.All + " " + string.Join(",", ranges.ToArray()));

    string corrupt = Path.Combine(folder, "cache", "corrupt.bin");
    using (var part = new FileStream(corrupt + ".part", FileMode.Create)) part.Write(new byte[600000], 0, 600000);
    var mixed = Powershell("-Command \"" + library + "try { Get-VerifiedFile '" + url.Replace("good.bin", "corrupt.bin") + "' '" + corrupt + "' '" + good + "' $null; 'RESULT|ok' } catch { 'RESULT|fail|' + $_.Exception.Message }\"");
    Require(mixed.Lines.Any(l => l.StartsWith("RESULT|fail|Checksum mismatch")) && !File.Exists(corrupt) && !File.Exists(corrupt + ".part"), "a kept part that does not match is thrown away: " + mixed.All);

    string fileUrl = new Uri(source).AbsoluteUri;
    string local = Path.Combine(folder, "cache", "local.bin");
    var viaFile = Powershell("-Command \"" + library + "try { Get-VerifiedFile '" + fileUrl + "' '" + local + "' '" + good + "' $null; 'RESULT|ok' } catch { 'RESULT|fail|' + $_.Exception.Message }\"");
    Require(viaFile.Lines.Contains("RESULT|ok") && Preflight.Sha256(local) == good, "file:// downloads are verified too: " + viaFile.All);
   }
   finally { listener.Close(); Clean(folder); }
  }

  static string Unescape(string text) { return text.Replace("\\\"", "\"").Replace("\\\\", "\\"); }

  // Every sentence the first-run screen can show must exist in all three languages.
  static void Translations() {
   var known = new HashSet<string>(Lang.Table.Select(r => r[0]));
   foreach (string n in Lang.Neutral) known.Add(n);
   var literals = new List<string>();
   foreach (string file in new[] { "Setup.cs", "SetupWizard.cs" }) {
    string path = Path.Combine(root, "src", "Launcher", file);
    if (!File.Exists(path)) continue;
    string source = File.ReadAllText(path);
    foreach (Match m in Regex.Matches(source, "Msg\\.Say\\(\"((?:[^\"\\\\]|\\\\.)*)\"")) literals.Add(Unescape(m.Groups[1].Value));
    Require(!Regex.IsMatch(source, "Msg\\.Say\\((?!\")"), file + ": every Msg.Say takes a plain string literal");
    Require(!Regex.IsMatch(source, "Msg\\.Say\\(\"(?:[^\"\\\\]|\\\\.)*\"\\s*\\+"), file + ": Msg.Say literals are not joined");
   }
   Require(literals.Count >= 40, "the setup texts were found (" + literals.Count + ")");
   foreach (string text in literals.Distinct()) Require(known.Contains(text), "no translation for: " + text);
   foreach (string text in literals.Distinct().Where(known.Contains)) {
    var row = Lang.Table.FirstOrDefault(r => r[0] == text);
    if (row == null) continue;
    Require(row.Length == 3 && row[1].Length > 0 && row[2].Length > 0, "all three languages for: " + text);
   }
  }

  // The first-run screen is wired in, starts hidden, and is only entered when the preflight says something required is missing.
  static void Wizard() {
   string launcher = Path.Combine(root, "src", "Launcher");
   string xaml = File.ReadAllText(Path.Combine(launcher, "Launcher.xaml"));
   foreach (string name in new[] { "SetupPage", "SetupList", "SetupProgress", "SetupProgressText", "SetupNotes", "SetupDownload", "SetupCancel", "SetupLocate", "SetupRecheck", "NavRow", "StatusBar" })
    Require(xaml.Contains("x:Name=\"" + name + "\""), "Launcher.xaml has " + name);
   Require(Regex.IsMatch(xaml, "<Grid x:Name=\"SetupPage\" Visibility=\"Collapsed\""), "the setup page starts hidden");
   Require(Regex.IsMatch(xaml, "x:Name=\"SetupCancel\"[^>]*Visibility=\"Collapsed\""), "Cancel only shows while a download runs");
   var texts = new HashSet<string>(Lang.Table.Select(r => r[0]));
   foreach (string name in Lang.Neutral) texts.Add(name);
   foreach (string name in new[] { "SetupDownload", "SetupCancel", "SetupLocate", "SetupRecheck" }) {
    string english = Regex.Match(xaml, "x:Name=\"" + name + "\" Content=\"([^\"]*)\"").Groups[1].Value;
    Require(texts.Contains(english), name + " has a translation: " + english);
   }
   Require(Lang.Dynamic.Contains("SetupProgressText") && Lang.Dynamic.Contains("SetupNotes"), "the texts set from code are kept out of the XAML translator");
   string wizard = File.ReadAllText(Path.Combine(launcher, "SetupWizard.cs"));
   foreach (Match m in Regex.Matches(wizard, "\\bTr\\(\"((?:[^\"\\\\]|\\\\.)*)\"")) Require(texts.Contains(Unescape(m.Groups[1].Value)), "no translation for: " + m.Groups[1].Value);
   Require(!Regex.IsMatch(wizard, "\\bTr\\((?!\")"), "SetupWizard.cs: every Tr takes a plain string literal");
   Require(Regex.Matches(wizard, "EnterSetup\\(\\)").Count == 2 && wizard.Contains("if (needed) { if (!setupActive) EnterSetup();"), "the wizard is only entered when the preflight needs setup");
   string main = File.ReadAllText(Path.Combine(launcher, "Launcher.cs"));
   foreach (string method in new[] { "void ShowPage(int index)", "void PressStart()", "void StartSession()" })
    Require(Regex.IsMatch(main, Regex.Escape(method) + " \\{\\s*if \\(setupActive(?: \\|\\| seeding)?\\) return;"), method + " does nothing while the wizard is up");
   Require(main.Contains("StartSetupCheck(); };"), "the check runs when the window loads");
   Require(File.ReadAllText(Path.Combine(launcher, "PadCursors.cs")).Contains("setupActive ? \"SetupPage\""), "controller navigation follows the wizard page");
   Require(!Preflight.NeedsSetup(new List<SetupCheck>()), "no findings never shows the wizard");
  }

  static int Main(string[] args) {
   root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
   temp = Path.Combine(Path.GetTempPath(), "HyruleSetupTests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
   Directory.CreateDirectory(temp);
   try {
    foreach (var test in new KeyValuePair<string, Action>[] {
     new KeyValuePair<string, Action>("manifest", Manifest), new KeyValuePair<string, Action>("preflight", PreflightLayouts), new KeyValuePair<string, Action>("locator", Locator),
     new KeyValuePair<string, Action>("messages", Messages), new KeyValuePair<string, Action>("install script", CheckMode), new KeyValuePair<string, Action>("zip safety", ZipSafety),
     new KeyValuePair<string, Action>("downloads", Downloads), new KeyValuePair<string, Action>("translations", Translations), new KeyValuePair<string, Action>("wizard", Wizard) }) {
     int before = failures;
     try { test.Value(); }
     catch (Exception error) { failures++; Console.WriteLine("FAIL: " + test.Key + " threw " + error); }
     Console.WriteLine((failures == before ? "ok   " : "FAIL ") + test.Key);
    }
   }
   finally { Clean(temp); }
   Console.WriteLine(failures == 0 ? "All setup tests passed." : failures + " setup test failure(s).");
   return failures == 0 ? 0 : 1;
  }
 }
}
