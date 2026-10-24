using System;
using System.IO;
using System.Linq;

namespace HyruleOnline {
 // Opt-in help for the second Cemu's shader cache, applied only at session start while no Cemu runs.
 // Cemu 1.26 appends to its transferable cache and rewrites the Vulkan pipeline cache while the game runs, and both instances
 // run the same game at once, so a live shared folder (a junction) could interleave writes and corrupt the cache. That cannot be
 // tested here, so no folder is ever shared live. Instead the owner's cache (Cemu A) seeds the follower's (Cemu B) once, by a
 // real copy, when the follower has none. This saves the separate download or copy for the second instance but not disk space.
 // A junction left behind (for example by hand) is turned back into a real copy, whatever the flag says. Every problem leaves
 // the instances independent and is reported in the outcome and in the log.
 static class ShaderCache {
  public sealed class Outcome {
   public bool Seeded;
   public string Problem;
  }

  const long Margin = 256L << 20;

  public static Outcome Prepare(bool enabled, string owner, string follower, string logFile, Func<string, long> freeBytes = null) {
   var outcome = new Outcome();
   string temp = null;
   bool keepTemp = false;
   Log(logFile, "Shared shader cache: " + (enabled ? "on" : "off") + ". Owner: " + owner + ". Follower: " + follower + ".");
   try {
    Func<string, long> free = freeBytes ?? FreeSpace;
    foreach (string cache in new[] { follower, owner }) {
     if (!IsLink(cache)) continue;
     long size = Size(cache);
     Log(logFile, "Junction found at " + cache + " (" + Megabytes(size) + "); making it a real copy again.");
     temp = cache + ".copy";
     Duplicate(cache, temp, size, free, logFile);
     keepTemp = true;
     Directory.Delete(cache, false);
     Directory.Move(temp, cache);
     keepTemp = false; temp = null;
     Log(logFile, "Junction removed and a real copy created at " + cache + ".");
    }

    if (!enabled) { Log(logFile, "Both instances keep their own caches."); return outcome; }
    if (!HasFiles(owner)) { Log(logFile, "Nothing to share yet: the owner cache is empty or missing."); return outcome; }
    if (HasFiles(follower)) { Log(logFile, "Both caches exist; each instance keeps its own."); return outcome; }

    temp = follower + ".seed";
    Duplicate(owner, temp, Size(owner), free, logFile);
    keepTemp = true;
    if (Directory.Exists(follower)) Remove(follower);
    Directory.Move(temp, follower);
    keepTemp = false; temp = null;
    outcome.Seeded = true;
    Log(logFile, "The follower cache was seeded with a copy of the owner cache.");
   }
   catch (Exception error) {
    outcome.Problem = error.Message;
    Log(logFile, "Error: " + error.Message + " Fallback: independent caches for this session." + (keepTemp ? " The copy is kept at " + temp + "." : ""));
    if (!keepTemp && temp != null) { try { Remove(temp); } catch (Exception) { } }
   }
   return outcome;
  }

  static void Duplicate(string source, string target, long size, Func<string, long> free, string logFile) {
   long available = free(Path.GetFullPath(target)), needed = size + Margin;
   Log(logFile, "Copy " + source + " to " + target + ": " + Megabytes(size) + ", free disk " + Megabytes(available) + ".");
   if (available < needed) throw new IOException("Not enough free disk space: " + Megabytes(needed) + " needed, " + Megabytes(available) + " free.");
   if (Directory.Exists(target)) Remove(target);
   Directory.CreateDirectory(target);
   Copy(source, target);
   if (Size(target) != size) throw new IOException("The copy is not the same size as the original.");
  }

  static void Copy(string source, string target) {
   foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
   foreach (string folder in Directory.GetDirectories(source)) {
    string next = Path.Combine(target, Path.GetFileName(folder));
    Directory.CreateDirectory(next);
    Copy(folder, next);
   }
  }

  static void Remove(string folder) {
   foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
   Directory.Delete(folder, true);
  }

  static bool IsLink(string path) { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }

  static bool HasFiles(string path) { return Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(); }

  static long Size(string path) { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }

  static long FreeSpace(string path) { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }

  static string Megabytes(long bytes) { return (bytes >> 20) + " MB"; }

  static void Log(string file, string text) {
   try { File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n"); }
   catch (Exception) { }
  }
 }
}
