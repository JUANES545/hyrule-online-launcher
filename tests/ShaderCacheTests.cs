using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HyruleOnline {
 static class ShaderCacheTests {
  static void Require(bool ok, string message) { if (!ok) throw new Exception("TEST: " + message); }

  static string Fill(string cache) {
   Directory.CreateDirectory(Path.Combine(cache, "transferable"));
   Directory.CreateDirectory(Path.Combine(cache, "driver", "vk"));
   File.WriteAllText(Path.Combine(cache, "info.txt"), "info");
   File.WriteAllText(Path.Combine(cache, "transferable", "game_shaders.bin"), "shaders 1234567890");
   File.WriteAllText(Path.Combine(cache, "driver", "vk", "game.bin"), "pipeline");
   return cache;
  }

  static string Snapshot(string cache) {
   if (!Directory.Exists(cache)) return "missing";
   return string.Join("|", Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => f.Substring(cache.Length) + "=" + File.ReadAllText(f)).ToArray());
  }

  static bool IsLink(string path) { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }

  static void Junction(string link, string target) {
   var info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"") { CreateNoWindow = true, UseShellExecute = false };
   using (var process = Process.Start(info)) { process.WaitForExit(); Require(process.ExitCode == 0, "the test junction is created"); }
  }

  static string Log(string file) { return File.Exists(file) ? File.ReadAllText(file) : ""; }

  static int Main() {
   string temp = Path.Combine(Path.GetTempPath(), "hyrule-shader-" + Guid.NewGuid().ToString("N"));
   FileStream locked = null;
   try {
    Func<string, long> plenty = path => long.MaxValue / 2, none = path => 0;

    // Flag off: nothing changes.
    string root = Path.Combine(temp, "off"), a = Fill(Path.Combine(root, "a")), b = Path.Combine(root, "b"), log = Path.Combine(root, "shader-cache.log");
    string before = Snapshot(a);
    var outcome = ShaderCache.Prepare(false, a, b, log);
    Require(!outcome.Seeded && outcome.Problem == null && Snapshot(a) == before && !Directory.Exists(b), "flag off leaves the instances untouched");
    Require(Log(log).Contains("Shared shader cache: off. Owner: " + a + ". Follower: " + b + "."), "the log records the flag and the paths");

    // Flag on: the follower is seeded with a real copy; the owner is untouched.
    root = Path.Combine(temp, "on"); a = Fill(Path.Combine(root, "a")); b = Path.Combine(root, "b"); log = Path.Combine(root, "shader-cache.log");
    before = Snapshot(a);
    outcome = ShaderCache.Prepare(true, a, b, log);
    Require(outcome.Seeded && outcome.Problem == null && Snapshot(b) == before && Snapshot(a) == before && !IsLink(b), "flag on seeds the follower with a real copy");
    Require(!Directory.Exists(b + ".seed"), "no temporary folder is left");
    string text = Log(log);
    Require(text.Contains("Shared shader cache: on.") && text.Contains("Copy " + a + " to " + b + ".seed") && text.Contains("free disk") && text.Contains("seeded"), "the log records the decision, the copy, the sizes and the free disk");

    // Starting again changes nothing, and the follower's own shaders stay.
    File.WriteAllText(Path.Combine(b, "transferable", "game_shaders.bin"), "follower has more shaders");
    outcome = ShaderCache.Prepare(true, a, b, log);
    Require(!outcome.Seeded && outcome.Problem == null && File.ReadAllText(Path.Combine(b, "transferable", "game_shaders.bin")) == "follower has more shaders", "repeated starts are idempotent and keep the follower's data");
    Require(Snapshot(a) == before && Log(log).Contains("each instance keeps its own"), "the owner stays the same and the second start is logged");

    // An empty follower folder is seeded too; an empty owner shares nothing.
    root = Path.Combine(temp, "empty"); a = Fill(Path.Combine(root, "a")); b = Path.Combine(root, "b"); log = Path.Combine(root, "shader-cache.log");
    Directory.CreateDirectory(Path.Combine(b, "transferable"));
    outcome = ShaderCache.Prepare(true, a, b, log);
    Require(outcome.Seeded && Snapshot(b) == Snapshot(a), "an empty follower folder is seeded");
    string emptyOwner = Path.Combine(root, "none"), other = Path.Combine(root, "other");
    outcome = ShaderCache.Prepare(true, emptyOwner, other, log);
    Require(!outcome.Seeded && outcome.Problem == null && !Directory.Exists(other) && Log(log).Contains("Nothing to share yet"), "an empty owner cache shares nothing");

    // A junction is turned back into a real copy, with the flag off or on, and no data is lost.
    foreach (bool enabled in new[] { false, true }) {
     root = Path.Combine(temp, "junction" + enabled); a = Fill(Path.Combine(root, "a")); b = Path.Combine(root, "b"); log = Path.Combine(root, "shader-cache.log");
     Directory.CreateDirectory(root);
     Junction(b, a);
     before = Snapshot(a);
     outcome = ShaderCache.Prepare(enabled, a, b, log);
     Require(outcome.Problem == null && !IsLink(b) && !IsLink(a) && Snapshot(b) == before && Snapshot(a) == before, "a junction becomes an independent real copy (flag " + enabled + ")");
     File.WriteAllText(Path.Combine(b, "transferable", "game_shaders.bin"), "changed");
     Require(File.ReadAllText(Path.Combine(a, "transferable", "game_shaders.bin")) == "shaders 1234567890", "the copy is independent of the owner");
     Require(!Directory.Exists(b + ".copy") && Log(log).Contains("Junction found at " + b) && Log(log).Contains("Junction removed"), "the junction removal is logged");
    }

    // A failed copy falls back to independent caches and is logged.
    root = Path.Combine(temp, "locked"); a = Fill(Path.Combine(root, "a")); b = Path.Combine(root, "b"); log = Path.Combine(root, "shader-cache.log");
    locked = new FileStream(Path.Combine(a, "transferable", "game_shaders.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    outcome = ShaderCache.Prepare(true, a, b, log);
    Require(!outcome.Seeded && !string.IsNullOrEmpty(outcome.Problem) && !Directory.Exists(b) && !Directory.Exists(b + ".seed"), "a locked file falls back and cleans up");
    text = Log(log);
    Require(text.Contains("Error: " + outcome.Problem) && text.Contains("Fallback: independent caches"), "the error and the fallback are logged");
    locked.Dispose(); locked = null;
    Require(Snapshot(a).Contains("shaders 1234567890"), "the owner keeps its data");

    // Not enough disk: nothing is copied or removed, even with a junction in place.
    root = Path.Combine(temp, "space"); a = Fill(Path.Combine(root, "a")); b = Path.Combine(root, "b"); log = Path.Combine(root, "shader-cache.log");
    outcome = ShaderCache.Prepare(true, a, b, log, none);
    Require(!outcome.Seeded && outcome.Problem.Contains("Not enough free disk space") && !Directory.Exists(b) && !Directory.Exists(b + ".seed") && Log(log).Contains("Fallback"), "insufficient space refuses the seed");
    Directory.CreateDirectory(b + "x");
    string link = b + "x" + Path.DirectorySeparatorChar + "link";
    Junction(link, a);
    before = Snapshot(a);
    outcome = ShaderCache.Prepare(false, a, link, log, none);
    Require(outcome.Problem.Contains("Not enough free disk space") && IsLink(link) && Snapshot(a) == before && Snapshot(link) == before && !Directory.Exists(link + ".copy"), "insufficient space keeps the junction and the data");
    Directory.Delete(link, false);
    Require(Snapshot(a) == before, "removing the junction keeps the target");

    // A log that cannot be written never throws.
    string nowhere = Path.Combine(temp, "missing-folder", "shader-cache.log");
    outcome = ShaderCache.Prepare(false, a, Path.Combine(temp, "x"), nowhere, plenty);
    Require(outcome.Problem == null && !File.Exists(nowhere), "an unwritable log is ignored");

    Console.WriteLine("PASS: shared shader cache.");
    return 0;
   }
   catch (Exception error) {
    Console.Error.WriteLine("FAIL: " + error.Message);
    return 1;
   }
   finally {
    if (locked != null) locked.Dispose();
    try {
     foreach (string folder in Directory.Exists(temp) ? Directory.GetDirectories(temp, "*", SearchOption.AllDirectories) : new string[0])
      if (Directory.Exists(folder) && IsLink(folder)) Directory.Delete(folder, false);
     if (Directory.Exists(temp)) Directory.Delete(temp, true);
    }
    catch (Exception) { }
   }
  }
 }
}
