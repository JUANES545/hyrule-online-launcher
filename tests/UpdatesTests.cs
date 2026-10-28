using System;
using System.Diagnostics;
using System.IO;

namespace HyruleOnline {
 static class UpdatesTests {
  static int checks, failures;

  static void Check(bool ok, string message) {
   checks++;
   if (ok) return;
   failures++;
   Console.WriteLine("FAIL: " + message);
  }

  const string Base = "https://github.com/JUANES545/hyrule-online-launcher/releases/download/v0.6.0/";

  static string Release(string tag, string assets, bool prerelease = false) {
   return "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":" + (prerelease ? "true" : "false") + ",\"assets\":[" + assets + "]}";
  }

  static string Asset(string name, string url = null) { return "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + (url ?? Base + name) + "\"}"; }

  static void Versions() {
   Check(Updates.Newer("0.5.0", "v0.6.0"), "0.6.0 is newer than 0.5.0");
   Check(Updates.Newer("0.9.0", "0.10.0"), "0.10.0 is newer than 0.9.0, compared as numbers");
   Check(!Updates.Newer("0.6.0", "v0.6.0"), "the same version is not newer");
   Check(!Updates.Newer("0.6.0", "v0.5.9"), "an older version is not newer");
   Check(!Updates.Newer("0.5.0", "nightly"), "a tag that is not a version is ignored");
   Check(!Updates.Newer("unknown", "v0.6.0"), "an unknown installed version never updates");
  }

  static void Picking() {
   string both = Asset("HyruleOnline-0.6.0-setup.exe") + "," + Asset("HyruleOnline-0.6.0-setup.exe.sha256") + "," + Asset("HyruleOnline-0.6.0-portable.zip");
   var info = Updates.Pick(Release("v0.6.0", both), "0.5.0");
   Check(info != null && info.Version == "0.6.0" && info.SetupUrl.EndsWith("-setup.exe") && info.ShaUrl.EndsWith(".sha256"), "a newer release with both assets is picked");
   Check(Updates.Pick(Release("v0.6.0", both), "0.6.0") == null, "no update when already current");
   Check(Updates.Pick(Release("v0.6.0", Asset("HyruleOnline-0.6.0-setup.exe")), "0.5.0") == null, "no update without the checksum");
   Check(Updates.Pick(Release("v0.6.0", Asset("HyruleOnline-0.6.0-setup.exe") + "," + Asset("HyruleOnline-0.6.0-setup.exe.sha256", "https://example.com/x.sha256")), "0.5.0") == null, "assets from another host are ignored");
   Check(Updates.Pick(Release("v0.6.0", both, true), "0.5.0") == null, "a prerelease is ignored");
   Check(Updates.Pick("not json", "0.5.0") == null, "bad JSON is ignored");
   string hash = new string('a', 64);
   Check(Updates.ExpectedSha(hash.ToUpper() + "  HyruleOnline-0.6.0-setup.exe\n") == hash, "the checksum is read from a sha256sum line");
   Check(Updates.ExpectedSha("oops") == null, "a bad checksum file is rejected");
  }

  static string Temp(string name) { return Path.Combine(Path.GetTempPath(), "hyrule-update-test-" + Guid.NewGuid().ToString("N").Substring(0, 8), name); }

  static void Put(string file, string text) { Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, text); }

  static string Read(string file) { return File.Exists(file) ? File.ReadAllText(file).Trim() : null; }

  // A fake install: version 0.5.0 with an "exe" that is a batch file.
  static string Install(out string exe) {
   string root = Temp("root");
   Put(Path.Combine(root, "VERSION"), "0.5.0");
   Put(Path.Combine(root, "scripts", "Start-Online.ps1"), "old script");
   Put(Path.Combine(root, ".artifacts", "input", "input.dll"), "old input");
   Put(Path.Combine(root, ".artifacts", "app", "preferences.xml"), "my preferences");
   Put(Path.Combine(root, ".artifacts", "app", "Fonts", "a.ttf"), "font");
   exe = Path.Combine(root, ".artifacts", "app", "HyruleOnline.cmd");
   Put(exe, "@exit /b 0\r\n");
   return root;
  }

  static void Backups() {
   string exe; string root = Install(out exe);
   Updates.MakeBackup(root, Path.GetDirectoryName(exe));
   string backup = Updates.Backup(root);
   Check(Read(Path.Combine(backup, "VERSION")) == "0.5.0" && Read(Path.Combine(backup, "scripts", "Start-Online.ps1")) == "old script", "the backup keeps the version and the scripts");
   Check(Read(Path.Combine(backup, "input", "input.dll")) == "old input" && Read(Path.Combine(backup, "app", "Fonts", "a.ttf")) == "font", "the backup keeps the input and the app folders");
   Check(!File.Exists(Path.Combine(backup, "app", "preferences.xml")), "the preferences are not part of the backup");
   Updates.MakeBackup(root, Path.GetDirectoryName(exe));
   Updates.DropBackup(root);
   Check(!Directory.Exists(backup), "dropping the backup removes it");
   Directory.Delete(root, true);
  }

  // Runs the real helper against a fake install. The setup and the new launcher are batch files, so no installer is needed.
  static void Helper(string title, int setupExit, bool launcherStays, bool expectRollback) {
   string exe; string root = Install(out exe);
   Updates.MakeBackup(root, Path.GetDirectoryName(exe));
   string setup = Path.Combine(root, "setup.cmd");
   Put(setup, "@echo 0.6.0> \"" + Path.Combine(root, "VERSION") + "\"\r\n@echo new script> \"" + Path.Combine(root, "scripts", "Start-Online.ps1") + "\"\r\n@echo new input> \"" + Path.Combine(root, ".artifacts", "input", "input.dll") + "\"\r\n@exit /b " + setupExit + "\r\n");
   Put(exe, launcherStays ? "@ping -n 6 127.0.0.1 >nul\r\n" : "@exit /b 1\r\n");
   string script = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "Update-Launcher.ps1");
   var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Root \"" + root + "\" -Exe \"" + exe + "\" -Setup \"" + setup + "\" -Version 0.6.0 -Grace 2") { UseShellExecute = false, CreateNoWindow = true });
   Check(process.WaitForExit(60000), title + ": the helper finishes");
   string note = Read(Updates.Failure(root));
   if (expectRollback) {
    Check(Read(Path.Combine(root, "VERSION")) == "0.5.0", title + ": the old version is back");
    Check(Read(Path.Combine(root, "scripts", "Start-Online.ps1")) == "old script" && Read(Path.Combine(root, ".artifacts", "input", "input.dll")) == "old input", title + ": the old files are back");
    Check(Read(Path.Combine(root, ".artifacts", "app", "preferences.xml")) == "my preferences", title + ": the preferences are untouched");
    Check(note != null && note.Length > 0, title + ": a failure note is left");
    Check(!Directory.Exists(Updates.Backup(root)), title + ": the backup is removed after the restore");
   }
   else {
    Check(Read(Path.Combine(root, "VERSION")) == "0.6.0" && note == null, title + ": the update stays and leaves no note");
    Check(Directory.Exists(Updates.Backup(root)), title + ": the backup stays until the new launcher opens its window");
   }
   System.Threading.Thread.Sleep(6000);
   try { Directory.Delete(root, true); } catch (Exception) { }
  }

  static int Main() {
   Versions();
   Picking();
   Backups();
   Helper("a good update", 0, true, false);
   Helper("a failing installer", 1, true, true);
   Helper("a launcher that does not start", 0, false, true);
   Console.WriteLine(failures == 0 ? "Updates tests passed (" + checks + " checks)." : failures + " of " + checks + " update checks failed.");
   return failures == 0 ? 0 : 1;
  }
 }
}