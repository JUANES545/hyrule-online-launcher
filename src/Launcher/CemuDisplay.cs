using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace HyruleOnline {
 // Makes one Cemu instance render the BOTW picture at exactly the shape of its window.
 // The stock BOTW Graphics pack is copied to graphicPacks\HyruleOnline\Graphics with two extra presets
 // (an aspect ratio and a resolution) that are rewritten for every session. The downloaded pack is never changed.
 static class CemuDisplay {
  public const string PackFile = "graphicPacks\\HyruleOnline\\Graphics\\rules.txt";
  public const string DisplayPreset = "Online Display", ResolutionPreset = "Online Resolution";
  // The pack's own switch for shapes narrower than 16:9; without it the HUD is stretched sideways.
  public const string SquareHudCategory = "Square HUD Mode", SquareHudMode = "Enabled (Experimental, Will Have Some Issues)";
  const string StockPack = "graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Graphics";

  public static string BuildRules(string stock, PlayerView view) {
   string eol = stock.Contains("\r\n") ? "\r\n" : "\n";
   string rules = Regex.Replace(stock, "(?m)^name = Graphics[ \\t]*(?=\\r?$)", "name = Graphics (online display)");
   rules = Regex.Replace(rules, "(?m)^(path = \".*)/Graphics\"", "$1/Online Display\"");
   if (rules == stock || !rules.Contains("[Definition]")) throw new InvalidDataException(Lang.T("The BOTW Graphics pack has an unexpected format."));
   var text = new StringBuilder(rules.TrimEnd());
   Action<string> line = value => text.Append(eol).Append(value);
   line(""); line("# Written by the Hyrule Online launcher for this window.");
   line("[Preset]"); line("name = " + DisplayPreset); line("category = Aspect Ratio");
   line("$aspectRatioWidth = " + view.RatioWidth); line("$aspectRatioHeight = " + view.RatioHeight);
   line("$showUltrawideOptions:int = " + (view.Ultrawide ? 1 : 0)); line("$showSquareHUDOption:int = " + (view.Square ? 1 : 0));
   if (!view.Ultrawide) line("$ultrawideHUDMode:int = 0");
   line(""); line("[Preset]"); line("name = " + ResolutionPreset); line("category = Resolution");
   line("$width = " + view.RenderWidth); line("$height = " + view.RenderHeight);
   return text.Append(eol).ToString();
  }

  public static void Apply(string instance, PlayerView view, string fpsPreset = null, GraphicsMod.Quality quality = null, int? consoleLanguage = null, GraphicsMod.Advanced advanced = null, bool vulkanAvailable = true) {
   string stockFolder = Path.Combine(instance, StockPack);
   string settingsFile = Path.Combine(instance, "settings.xml");
   if (!File.Exists(Path.Combine(stockFolder, "rules.txt"))) throw new FileNotFoundException(Lang.T("The BOTW Graphics pack is missing from {0}.", instance));
   if (!File.Exists(settingsFile)) throw new FileNotFoundException(Lang.T("Cemu settings are missing from {0}.", instance));

   string packFile = Path.Combine(instance, PackFile);
   string packFolder = Path.GetDirectoryName(packFile);
   Directory.CreateDirectory(packFolder);
   foreach (string file in Directory.GetFiles(stockFolder)) {
    string target = Path.Combine(packFolder, Path.GetFileName(file));
    if (!File.Exists(target) && !file.EndsWith("rules.txt", StringComparison.OrdinalIgnoreCase)) File.Copy(file, target);
   }
   var utf8 = new UTF8Encoding(false);
   File.WriteAllText(packFile, BuildRules(File.ReadAllText(Path.Combine(stockFolder, "rules.txt")), view), utf8);
   WriteSettings(settingsFile, view.Square, fpsPreset, quality, consoleLanguage, advanced, vulkanAvailable);
  }

  static void WriteSettings(string settingsFile, bool squareHud, string fpsPreset, GraphicsMod.Quality quality, int? consoleLanguage, GraphicsMod.Advanced advanced, bool vulkanAvailable) {
   var document = XDocument.Load(settingsFile);
   var content = document.Root;
   // The game language; Cemu's own menu language (<language>) is a different setting and stays as it is.
   if (consoleLanguage.HasValue) {
    var language = content.Element("console_language");
    if (language == null) content.Add(new XElement("console_language", consoleLanguage.Value)); else language.Value = consoleLanguage.Value.ToString();
   }
   var packs = content.Element("GraphicPack");
   if (packs == null) { packs = new XElement("GraphicPack"); content.Add(packs); }
   // Two Graphics packs would patch the same game code, so the stock one must not stay enabled.
   packs.Elements("Entry").Where(e => ((string)e.Attribute("filename") ?? "").EndsWith("BreathOfTheWild\\Graphics\\rules.txt", StringComparison.OrdinalIgnoreCase)
    && !string.Equals((string)e.Attribute("filename"), PackFile, StringComparison.OrdinalIgnoreCase)).Remove();
   var entry = packs.Elements("Entry").FirstOrDefault(e => string.Equals((string)e.Attribute("filename"), PackFile, StringComparison.OrdinalIgnoreCase));
   if (entry == null) { entry = new XElement("Entry", new XAttribute("filename", PackFile)); packs.Add(entry); }
   entry.RemoveNodes();
   entry.Add(Preset("Aspect Ratio", DisplayPreset), Preset("Resolution", ResolutionPreset));
   if (squareHud) entry.Add(Preset(SquareHudCategory, SquareHudMode));
   if (quality != null) {
    var chosen = new[] { Tuple.Create(GraphicsMod.ShadowsCategory, quality.Shadows), Tuple.Create(GraphicsMod.ShadowDistanceCategory, quality.ShadowDistance), Tuple.Create(GraphicsMod.AntiAliasingCategory, quality.AntiAliasing) };
    foreach (var item in chosen) if (!string.IsNullOrEmpty(item.Item2)) entry.Add(Preset(item.Item1, item.Item2));
   }

   if (fpsPreset != null) {
    var fps = packs.Elements("Entry").FirstOrDefault(e => string.Equals((string)e.Attribute("filename"), FpsMod.PackFile, StringComparison.OrdinalIgnoreCase));
    if (fps == null) { fps = new XElement("Entry", new XAttribute("filename", FpsMod.PackFile)); packs.Add(fps); }
    fps.RemoveNodes();
    fps.Add(Preset(FpsMod.ModeCategory, FpsMod.NormalMode), Preset(FpsMod.LimitCategory, fpsPreset));
   }

   var graphic = content.Element("Graphic");
   if (graphic == null) { graphic = new XElement("Graphic"); content.Add(graphic); }
   SetValue(graphic, "FullscreenScaling", "1");
   if (advanced != null) {
    SetValue(graphic, "api", GraphicsMod.ResolveApi(advanced.Api, vulkanAvailable) == GraphicsMod.OpenGlApi ? "0" : "1");
    SetValue(graphic, "VSync", advanced.VSync ? "1" : "0");
    SetValue(graphic, "AsyncCompile", advanced.AsyncCompile ? "true" : "false");
    SetValue(graphic, "VulkanAccurateBarriers", advanced.AccurateBarriers ? "true" : "false");
    SetValue(graphic, "UpscaleFilter", GraphicsMod.CleanFilter(advanced.Upscale.ToString(), 1).ToString());
    SetValue(graphic, "DownscaleFilter", GraphicsMod.CleanFilter(advanced.Downscale.ToString(), 0).ToString());
    var overlay = graphic.Element("Overlay");
    if (overlay == null) { overlay = new XElement("Overlay"); graphic.Add(overlay); }
    SetValue(overlay, "FPS", advanced.ShowFps ? "true" : "false");
    foreach (string name in new[] { "CPUUsage", "RAMUsage", "VRAMUsage" }) SetValue(overlay, name, advanced.ShowUsage ? "true" : "false");
    var notification = graphic.Element("Notification");
    if (notification == null) { notification = new XElement("Notification"); graphic.Add(notification); }
    SetValue(notification, "ShaderCompiling", advanced.ShowShaderNotice ? "true" : "false");
    var audio = content.Element("Audio");
    if (audio == null) { audio = new XElement("Audio"); content.Add(audio); }
    SetValue(audio, "TVVolume", GraphicsMod.CleanVolume(advanced.Volume.ToString()).ToString());
   }

   var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding(false) };
   using (var writer = XmlWriter.Create(settingsFile, settings)) document.Save(writer);
  }

  static void SetValue(XElement parent, string name, string value) {
   var element = parent.Element(name);
   if (element == null) parent.Add(new XElement(name, value)); else element.Value = value;
  }

  static XElement Preset(string category, string preset) {
   return new XElement("Preset", new XElement("category", category), new XElement("preset", preset));
  }
 }
}
