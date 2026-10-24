using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // Runs the dedicated server without a console window, keeps its recent output and accepts its console commands.
 sealed class ServerHost {
  const int MaxLines = 300;
  static readonly Regex Colors = new Regex("\u001b\\[[0-9;]*[A-Za-z]");
  readonly object gate = new object();
  readonly List<string> lines = new List<string>();
  Process process;

  public event Action<string> Line;

  public bool Running {
   get {
    try { return process != null && !process.HasExited; }
    catch (Exception) { return false; }
   }
  }

  public static string Clean(string text) {
   return Colors.Replace(text ?? "", "").TrimEnd();
  }

  public void Start(string directory) {
   if (Running) return;
   string exe = Path.Combine(directory, "MBL.DedicatedServer.exe");
   if (!File.Exists(exe)) throw new FileNotFoundException(Lang.T("The dedicated server is missing."), exe);
   var info = new ProcessStartInfo(exe) {
    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
   };
   var started = new Process { StartInfo = info };
   started.OutputDataReceived += (s, e) => Add(e.Data);
   started.ErrorDataReceived += (s, e) => Add(e.Data);
   lock (gate) lines.Clear();
   started.Start();
   started.BeginOutputReadLine(); started.BeginErrorReadLine();
   process = started;
  }

  void Add(string text) {
   string line = Clean(text);
   if (line.Length == 0) return;
   lock (gate) {
    lines.Add(line);
    if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
   }
   var handler = Line;
   if (handler != null) handler(line);
  }

  public string Text() {
   lock (gate) return string.Join(Environment.NewLine, lines.ToArray());
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
