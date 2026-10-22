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
  const string StockPack = "graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Graphics";

  public static string BuildRules(string stock, PlayerView view) {
   string eol = stock.Contains("\r\n") ? "\r\n" : "\n";
   string rules = Regex.Replace(stock, "(?m)^name = Graphics[ \\t]*(?=\\r?$)", "name = Graphics (online display)");
   rules = Regex.Replace(rules, "(?m)^(path = \".*)/Graphics\"", "$1/Online Display\"");
   if (rules == stock || !rules.Contains("[Definition]")) throw new InvalidDataException("The BOTW Graphics pack has an unexpected format.");
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

  public static void Apply(string instance, PlayerView view, string fpsPreset = null) {
   string stockFolder = Path.Combine(instance, StockPack);
   string settingsFile = Path.Combine(instance, "settings.xml");
   if (!File.Exists(Path.Combine(stockFolder, "rules.txt"))) throw new FileNotFoundException("The BOTW Graphics pack is missing from " + instance + ".");
   if (!File.Exists(settingsFile)) throw new FileNotFoundException("Cemu settings are missing from " + instance + ".");

   string packFile = Path.Combine(instance, PackFile);
   string packFolder = Path.GetDirectoryName(packFile);
   Directory.CreateDirectory(packFolder);
   foreach (string file in Directory.GetFiles(stockFolder)) {
    string target = Path.Combine(packFolder, Path.GetFileName(file));
    if (!File.Exists(target) && !file.EndsWith("rules.txt", StringComparison.OrdinalIgnoreCase)) File.Copy(file, target);
   }
   var utf8 = new UTF8Encoding(false);
   File.WriteAllText(packFile, BuildRules(File.ReadAllText(Path.Combine(stockFolder, "rules.txt")), view), utf8);
   WriteSettings(settingsFile, view, fpsPreset);
  }

  static void WriteSettings(string settingsFile, PlayerView view, string fpsPreset) {
   var document = XDocument.Load(settingsFile);
   var content = document.Root;
   var packs = content.Element("GraphicPack");
   if (packs == null) { packs = new XElement("GraphicPack"); content.Add(packs); }
   // Two Graphics packs would patch the same game code, so the stock one must not stay enabled.
   packs.Elements("Entry").Where(e => ((string)e.Attribute("filename") ?? "").EndsWith("BreathOfTheWild\\Graphics\\rules.txt", StringComparison.OrdinalIgnoreCase)
    && !string.Equals((string)e.Attribute("filename"), PackFile, StringComparison.OrdinalIgnoreCase)).Remove();
   var entry = packs.Elements("Entry").FirstOrDefault(e => string.Equals((string)e.Attribute("filename"), PackFile, StringComparison.OrdinalIgnoreCase));
   if (entry == null) { entry = new XElement("Entry", new XAttribute("filename", PackFile)); packs.Add(entry); }
   entry.RemoveNodes();
   entry.Add(Preset("Aspect Ratio", DisplayPreset), Preset("Resolution", ResolutionPreset));

   if (fpsPreset != null) {
    var fps = packs.Elements("Entry").FirstOrDefault(e => string.Equals((string)e.Attribute("filename"), FpsMod.PackFile, StringComparison.OrdinalIgnoreCase));
    if (fps == null) { fps = new XElement("Entry", new XAttribute("filename", FpsMod.PackFile)); packs.Add(fps); }
    fps.RemoveNodes();
    fps.Add(Preset(FpsMod.ModeCategory, FpsMod.NormalMode), Preset(FpsMod.LimitCategory, fpsPreset));
   }

   var graphic = content.Element("Graphic");
   if (graphic == null) { graphic = new XElement("Graphic"); content.Add(graphic); }
   var scaling = graphic.Element("FullscreenScaling");
   if (scaling == null) graphic.Add(new XElement("FullscreenScaling", "1")); else scaling.Value = "1";

   var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding(false) };
   using (var writer = XmlWriter.Create(settingsFile, settings)) document.Save(writer);
  }

  static XElement Preset(string category, string preset) {
   return new XElement("Preset", new XElement("category", category), new XElement("preset", preset));
  }
 }
}
