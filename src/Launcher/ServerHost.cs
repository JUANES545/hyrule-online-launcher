using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // Runs the dedicated server without a console window, passes on its output and accepts its console commands.
 sealed class ServerHost {
  static readonly Regex Colors = new Regex("\u001b\\[[0-9;]*[A-Za-z]");
  Process process;

  public event Action<string> Line;
  public event Action<int> Exited;

  public bool Running {
   get {
    try { return process != null && !process.HasExited; }
    catch (Exception) { return false; }
   }
  }

  public static string Clean(string text) {
   return Colors.Replace(text ?? "", "").TrimEnd();
  }

  // A positive gamePort answers the finder's beacon with that port for as long as the server runs.
  public void Start(string directory, int gamePort = 0) {
   if (Running) return;
   string exe = Path.Combine(directory, "MBL.DedicatedServer.exe");
   if (!File.Exists(exe)) throw new FileNotFoundException(Lang.T("The dedicated server is missing."), exe);
   var info = new ProcessStartInfo(exe) {
    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
   };
   var started = new Process { StartInfo = info, EnableRaisingEvents = true };
   started.OutputDataReceived += (s, e) => Add(e.Data);
   started.ErrorDataReceived += (s, e) => Add(e.Data);
   BeaconResponder beacon = null;
   started.Exited += (s, e) => {
    if (beacon != null) beacon.Dispose();
    int code = -1;
    try { code = ((Process)s).ExitCode; } catch (Exception) { }
    var handler = Exited;
    if (handler != null) handler(code);
   };
   started.Start();
   started.BeginOutputReadLine(); started.BeginErrorReadLine();
   process = started;
   if (gamePort > 0) { beacon = new BeaconResponder(gamePort); if (!Running) beacon.Dispose(); }
  }

  void Add(string text) {
   string line = Clean(text);
   if (line.Length == 0) return;
   var handler = Line;
   if (handler != null) handler(line);
  }

  public bool Send(string command) {
   if (!Running || string.IsNullOrWhiteSpace(command)) return false;
   try {
    process.StandardInput.WriteLine(command.Trim());
    process.StandardInput.Flush();
    return true;
   }
   catch (IOException) { return false; }
  }
 }
}
