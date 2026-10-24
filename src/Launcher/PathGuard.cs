using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HyruleOnline {
 enum PathIssue { TooLong, Network, ProgramFiles, OneDrive, NonAscii }

 // Checks the folder the launcher runs from. BCML, Cemu and the Milk Bar launcher break on paths over MAX_PATH, and the
 // whole project (Cemu instances, graphic packs, BCML content) lives under that folder.
 // The same limit is repeated in scripts\Path-Guard.ps1 and installer\HyruleOnline.iss; a test keeps the three equal.
 static class PathGuard {
  public const int MaxPath = 260;
  // The deepest file known under the root, with its leading backslash: a downloaded graphic pack of another game,
  // .tools\instances\cemuA\graphicPacks\downloadedGraphicPacks\XenobladeChroniclesX\Mods\HudNoBattleUI\... (166 characters).
  // The deepest BOTW file is 153 characters (a graphic pack workaround); Cemu's mlc01 and the BCML content stay under 105.
  public const int LongestKnownRelative = 166;
  // Room for graphic packs that Cemu downloads later and may nest a little deeper.
  public const int Headroom = 10;
  // Windows counts the terminating NUL inside MAX_PATH, so a root of this length keeps every known file under 260 characters.
  public const int MaxRootLength = MaxPath - 1 - LongestKnownRelative - Headroom;
  public const string SuggestedRoot = "C:\\HyruleOnline";

  // Too long and network folders stop the launcher; the rest only warn.
  public static bool Blocks(PathIssue issue) { return issue == PathIssue.TooLong || issue == PathIssue.Network; }

  public static List<PathIssue> Check(string root, IEnumerable<string> protectedFolders, IEnumerable<string> syncedFolders, bool networkDrive = false) {
   var issues = new List<PathIssue>();
   string path = (root ?? "").TrimEnd('\\', '/');
   if (path.Length > MaxRootLength) issues.Add(PathIssue.TooLong);
   if (networkDrive || path.StartsWith("\\\\", StringComparison.Ordinal)) issues.Add(PathIssue.Network);
   if (Inside(path, protectedFolders)) issues.Add(PathIssue.ProgramFiles);
   // OneDrive folders are also named "OneDrive - <organisation>"; a business or redirected folder can sit anywhere.
   if (Inside(path, syncedFolders) || path.Split('\\').Any(part => part.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase))) issues.Add(PathIssue.OneDrive);
   // RESEARCH.md has no report of a non-ASCII path breaking anything, so this only warns.
   if (path.Any(c => c > 127)) issues.Add(PathIssue.NonAscii);
   return issues;
  }

  public static List<PathIssue> Check(string root) {
   Func<string, string> env = Environment.GetEnvironmentVariable;
   var protectedFolders = new[] { env("ProgramFiles"), env("ProgramFiles(x86)"), env("ProgramW6432") };
   var synced = new[] { env("OneDrive"), env("OneDriveConsumer"), env("OneDriveCommercial") };
   bool network = false;
   try { network = Path.IsPathRooted(root) && root.Length > 1 && root[1] == ':' && new DriveInfo(root.Substring(0, 1)).DriveType == DriveType.Network; }
   catch (Exception) { }
   return Check(root, protectedFolders, synced, network);
  }

  static bool Inside(string path, IEnumerable<string> folders) {
   return folders.Any(folder => !string.IsNullOrEmpty(folder) && (path.Equals(folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));
  }

  static string Reason(PathIssue issue, int length) {
   switch (issue) {
    case PathIssue.TooLong: return Lang.T("The folder path is {0} characters long and the limit is {1}. BCML, Cemu and the Milk Bar launcher fail on long paths.", length, MaxRootLength);
    case PathIssue.Network: return Lang.T("The folder is on a network location. The game and its tools need a local drive.");
    case PathIssue.ProgramFiles: return Lang.T("The folder is under Program Files, which Windows protects from changes by regular programs.");
    case PathIssue.OneDrive: return Lang.T("The folder is synced by OneDrive, which can lock or rewrite files while the game uses them.");
    default: return Lang.T("The folder path has characters that are not plain ASCII, which some of the tools may not handle.");
   }
  }

  // The message for a set of issues, in the current launcher language, with the suggested short folder.
  public static string Describe(string root, IList<PathIssue> issues) {
   var text = new StringBuilder(Lang.T(issues.Any(Blocks) ? "Hyrule Online cannot start from this folder:" : "Hyrule Online may have problems in this folder:"));
   text.Append("\n").Append(root).Append("\n");
   foreach (var issue in issues) text.Append("\n").Append(Reason(issue, (root ?? "").TrimEnd('\\', '/').Length));
   text.Append("\n\n").Append(Lang.T("Move the whole folder to a short local path such as {0} and start it from there.", SuggestedRoot));
      if (!issues.Any(Blocks)) text.Append("\n\n").Append(Lang.T("Continue anyway?"));
      return text.ToString();
  }
 }
}
