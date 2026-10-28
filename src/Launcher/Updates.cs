using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HyruleOnline {
 sealed class UpdateInfo { public string Version, SetupUrl, ShaUrl; }

 // The in-app update: finds a newer release, downloads its installer, keeps one backup and hands over to scripts\Update-Launcher.ps1.
 static class Updates {
  const string Repo = "JUANES545/hyrule-online-launcher";
  const string Downloads = "https://github.com/" + Repo + "/releases/download/";

  public static string Backup(string root) { return Path.Combine(root, ".artifacts", "update-backup"); }
  public static string Failure(string root) { return Path.Combine(root, ".artifacts", "update-failed.txt"); }

  static Version Parse(string text) {
   Version version;
   return text != null && Regex.IsMatch(text.Trim().TrimStart('v'), @"^\d+\.\d+\.\d+$") && Version.TryParse(text.Trim().TrimStart('v'), out version) ? version : null;
  }

  public static bool Newer(string current, string candidate) {
   var a = Parse(current); var b = Parse(candidate);
   return a != null && b != null && b > a;
  }

  // The latest release as GitHub returns it. Null unless it is newer and carries both the installer and its checksum.
  public static UpdateInfo Pick(string json, string current) {
   try {
    var release = (IDictionary)new JavaScriptSerializer().DeserializeObject(json);
    string tag = Convert.ToString(release["tag_name"]);
    if (Convert.ToBoolean(release["draft"]) || Convert.ToBoolean(release["prerelease"]) || !Newer(current, tag)) return null;
    string version = tag.TrimStart('v');
    string setup = "HyruleOnline-" + version + "-setup.exe";
    var info = new UpdateInfo { Version = version };
    foreach (IDictionary asset in (IEnumerable)release["assets"]) {
     string name = Convert.ToString(asset["name"]), url = Convert.ToString(asset["browser_download_url"]);
     if (!url.StartsWith(Downloads, StringComparison.Ordinal)) continue;
     if (name == setup) info.SetupUrl = url;
     else if (name == setup + ".sha256") info.ShaUrl = url;
    }
    return info.SetupUrl != null && info.ShaUrl != null ? info : null;
   }
   catch (Exception) { return null; }
  }

  static HttpWebRequest Request(string url) {
   ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
   var request = (HttpWebRequest)WebRequest.Create(url);
   request.UserAgent = "HyruleOnline"; request.Timeout = 8000; request.ReadWriteTimeout = 30000;
   return request;
  }

  // Silent on any failure: no network, no release or a rate limit all just mean "no update".
  public static UpdateInfo Find(string current) {
   try {
    using (var response = Request("https://api.github.com/repos/" + Repo + "/releases/latest").GetResponse())
    using (var reader = new StreamReader(response.GetResponseStream())) return Pick(reader.ReadToEnd(), current);
   }
   catch (Exception) { return null; }
  }

  // The first 64 hex characters of a sha256sum line.
  public static string ExpectedSha(string text) {
   var match = Regex.Match(text ?? "", @"\b[0-9a-fA-F]{64}\b");
   return match.Success ? match.Value.ToLowerInvariant() : null;
  }

  public static string Sha256(string file) {
   using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
  }

  // Downloads and checks the installer. Throws with a readable reason when it cannot be trusted.
  public static string Fetch(UpdateInfo info, Action<int> progress) {
   string expected;
   using (var response = Request(info.ShaUrl).GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) expected = ExpectedSha(reader.ReadToEnd());
   if (expected == null) throw new InvalidDataException("The checksum file is not valid.");
   string file = Path.Combine(Path.GetTempPath(), "HyruleOnline-" + info.Version + "-setup.exe");
   using (var response = Request(info.SetupUrl).GetResponse()) using (var input = response.GetResponseStream()) using (var output = File.Create(file)) {
    var buffer = new byte[81920]; long done = 0, total = response.ContentLength; int read, last = -1;
    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
     output.Write(buffer, 0, read); done += read;
     int percent = total > 0 ? (int)(done * 100 / total) : 0;
     if (percent != last) { last = percent; progress(percent); }
    }
   }
   if (Sha256(file) != expected) { File.Delete(file); throw new InvalidDataException("The download does not match its checksum."); }
   return file;
  }

  // The only backup: what the installer replaces. The player's preferences stay where they are because the installer never touches them.
  public static void MakeBackup(string root, string appDir) {
   string backup = Backup(root);
   if (Directory.Exists(backup)) Directory.Delete(backup, true);
   Copy(appDir, Path.Combine(backup, "app"));
   Copy(Path.Combine(root, ".artifacts", "input"), Path.Combine(backup, "input"));
   Copy(Path.Combine(root, "scripts"), Path.Combine(backup, "scripts"));
   File.Copy(Path.Combine(root, "VERSION"), Path.Combine(backup, "VERSION"));
  }

  public static void DropBackup(string root) {
   try { if (Directory.Exists(Backup(root))) Directory.Delete(Backup(root), true); } catch (Exception) { }
  }

  static void Copy(string from, string to) {
   if (!Directory.Exists(from)) return;
   Directory.CreateDirectory(to);
   foreach (string file in Directory.GetFiles(from)) if (!Path.GetFileName(file).Equals("preferences.xml", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
   foreach (string dir in Directory.GetDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
  }

  // Runs from a copy in %TEMP% because the installer replaces the scripts folder.
  public static void Handover(string root, string exe, string setup, string version) {
   string helper = Path.Combine(Path.GetTempPath(), "Hyrule-Update-Launcher.ps1");
   File.Copy(Path.Combine(root, "scripts", "Update-Launcher.ps1"), helper, true);
   Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + helper + "\" -Root \"" + root.TrimEnd('\\') + "\" -Exe \"" + exe + "\" -Setup \"" + setup + "\" -Version " + version + " -ProcessId " + Process.GetCurrentProcess().Id) { UseShellExecute = false, CreateNoWindow = true });
  }
 }
}