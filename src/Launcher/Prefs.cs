using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace HyruleOnline {
 // What the launcher remembers between runs (preferences.xml). A missing, empty or corrupt file gives the defaults,
 // and keys that are absent, unknown or out of range fall back one by one.
 sealed class Prefs {
  public LayoutMode Mode = LayoutMode.Auto;
  public double Scale = GraphicsMod.DefaultScale;
  public string Shadows, ShadowDistance, AntiAliasing;
  public GraphicsMod.Advanced Advanced = new GraphicsMod.Advanced();
  public int FpsLimit = FpsMod.AutoFps, PlayerCount = 2, MenuLayout = PadKind.Auto;
  public bool PerDisplay, SharedShaders;
  public string Shortcut = MenuShortcut.Default;
  public string[] Displays = new string[2], Names = (string[])Characters.DefaultNames.Clone(), Models = { "", "" };
  public double[] Scales = { GraphicsMod.DefaultScale, GraphicsMod.DefaultScale };
  public int[] FpsLimits = { FpsMod.AutoFps, FpsMod.AutoFps }, Rotations = { 0, 0 };
  public string Language = "en", LauncherLanguage = "en";
  public string OnlineMode = ServerConnection.Local, HostPassword = "", HostDescription = ServerConnection.DefaultDescription, HostAddress = "", JoinAddress = "", JoinPassword = "";
  public int HostPort = ServerConnection.DefaultPort;
  public string LastPage = "Play";

  // The pages the launcher can reopen on; the first-run setup is never one of them.
  public static readonly string[] RememberedPages = { "Play", "Online", "Graphics", "Multiplayer", "Language", "Details" };

  public static Prefs Read(string file) {
   var prefs = new Prefs();
   try {
    if (!File.Exists(file)) return prefs;
    var root = XDocument.Load(file).Root;
    prefs.Mode = ScreenLayout.Parse((string)root.Element("Layout"));
    double saved;
    // Older preferences only knew a balanced or sharper switch.
    prefs.Scale = double.TryParse((string)root.Element("RenderScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out saved) ? GraphicsMod.NearestScale(saved)
     : (string)root.Element("Quality") == "Sharp" ? 1.5 : GraphicsMod.DefaultScale;
    prefs.Shadows = Saved(root, "Shadows"); prefs.ShadowDistance = Saved(root, "ShadowDistance"); prefs.AntiAliasing = Saved(root, "AntiAliasing");
    prefs.Advanced = new GraphicsMod.Advanced {
     Api = GraphicsMod.CleanApi(Saved(root, "GraphicsApi")), VSync = (string)root.Element("VSync") == "1", AsyncCompile = (string)root.Element("AsyncCompile") == "1",
     AccurateBarriers = (string)root.Element("AccurateBarriers") == "1",
     ShowFps = (string)root.Element("ShowFps") != "0", ShowUsage = (string)root.Element("ShowUsage") == "1",
     ShowShaderNotice = (string)root.Element("ShowShaderNotice") != "0", Volume = GraphicsMod.CleanVolume((string)root.Element("Volume")),
     Upscale = GraphicsMod.CleanFilter((string)root.Element("UpscaleFilter"), 1), Downscale = GraphicsMod.CleanFilter((string)root.Element("DownscaleFilter"), 0)
    };
    int wanted;
    prefs.FpsLimit = int.TryParse((string)root.Element("FpsLimit"), out wanted) && wanted > 0 ? wanted : FpsMod.AutoFps;
    prefs.PerDisplay = (string)root.Element("Arrangement") == "PerDisplay";
    prefs.PlayerCount = (string)root.Element("Players") == "1" ? 1 : 2;
    prefs.MenuLayout = Array.IndexOf(PadKind.Names, (string)root.Element("MenuLayout"));
    for (int i = 0; i < 2; i++) {
     prefs.Displays[i] = Saved(root, "Display" + (i + 1));
     prefs.Scales[i] = double.TryParse((string)root.Element("Scale" + (i + 1)), NumberStyles.Float, CultureInfo.InvariantCulture, out saved) ? GraphicsMod.NearestScale(saved) : prefs.Scale;
     prefs.FpsLimits[i] = int.TryParse((string)root.Element("Fps" + (i + 1)), out wanted) && wanted > 0 ? wanted : FpsMod.AutoFps;
     prefs.Rotations[i] = ScreenLayout.CleanRotation(int.TryParse((string)root.Element("Rotation" + (i + 1)), out wanted) ? wanted : 0);
     prefs.Names[i] = Characters.CleanName((string)root.Element("Name" + (i + 1)), Characters.DefaultNames[i]);
     prefs.Models[i] = (string)root.Element("Model" + (i + 1)) ?? "";
    }
    prefs.SharedShaders = (string)root.Element("SharedShaderCache") == "1";
    var shortcut = MenuShortcut.Parse((string)root.Element("MenuShortcut"));
    prefs.Shortcut = (string)root.Element("MenuShortcut") == "Off" ? "Off" : shortcut != null ? shortcut.ToString() : MenuShortcut.Default;
    prefs.Language = Saved(root, "Language") ?? "en";
    prefs.LauncherLanguage = Lang.Ui(Saved(root, "LauncherLanguage") ?? "en");
    prefs.OnlineMode = ServerConnection.CleanMode((string)root.Element("OnlineMode"));
    prefs.HostPort = ServerConnection.CleanPort((string)root.Element("HostPort"));
    prefs.HostPassword = ServerConnection.CleanText((string)root.Element("HostPassword"), 40);
    prefs.HostDescription = ServerConnection.CleanText((string)root.Element("HostDescription") ?? ServerConnection.DefaultDescription, 80);
    prefs.HostAddress = ServerConnection.CleanText((string)root.Element("HostAddress"), 100);
    prefs.JoinAddress = ServerConnection.CleanText((string)root.Element("JoinAddress"), 200);
    prefs.JoinPassword = ServerConnection.CleanText((string)root.Element("JoinPassword"), 40);
    prefs.LastPage = Array.IndexOf(RememberedPages, (string)root.Element("LastPage")) >= 0 ? (string)root.Element("LastPage") : "Play";
    return prefs;
   }
   catch (Exception) { return new Prefs(); }
  }

  static string Saved(XElement root, string name) { string value = (string)root.Element(name); return string.IsNullOrEmpty(value) ? null : value; }
 }
}
