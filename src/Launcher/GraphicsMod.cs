using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // The quality options come from the BOTW Graphics pack itself (its stock presets), so the launcher offers only
 // what the pack supports. The render scale is the launcher's own: it sets the Resolution preset it writes.
 static class GraphicsMod {
  public const string StockPack = "graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Graphics\\rules.txt";
  public const string ShadowsCategory = "Shadows", ShadowDistanceCategory = "Shadow Draw Distance", AntiAliasingCategory = "Anti-Aliasing";
  public static readonly string[] Categories = { ShadowsCategory, ShadowDistanceCategory, AntiAliasingCategory };
  public static readonly double[] Scales = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
  public const double DefaultScale = 1.0;

  public sealed class Choice {
   public string Name, Label;
   public bool Default;
  }

  // What a session asks of each pack category; a null name keeps the pack's own default.
  public sealed class Quality {
   public double Scale = DefaultScale;
   public string Shadows, ShadowDistance, AntiAliasing;
  }

  // Cemu's own graphics settings (settings.xml, <Graphic>), as opposed to the BOTW pack presets above.
  public const string AutoApi = "Auto", VulkanApi = "Vulkan", OpenGlApi = "OpenGL";
  public static readonly string[] Apis = { AutoApi, VulkanApi, OpenGlApi };
  // Cemu's filter numbers: 0 bilinear, 1 bicubic, 2 bicubic hermite, 3 nearest neighbor.
  public static readonly string[] Filters = { "Bilinear", "Bicubic", "Bicubic Hermite", "Nearest neighbor" };

  public sealed class Advanced {
   public string Api = AutoApi;
   public bool VSync, AsyncCompile, AccurateBarriers, ShowFps = true, ShowUsage;
   public int Upscale = 1, Downscale = 0;
  }

  [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
  static extern IntPtr LoadLibrary(string name);
  [System.Runtime.InteropServices.DllImport("kernel32")]
  static extern bool FreeLibrary(IntPtr module);

  // A Vulkan loader is installed with every GPU driver that supports Vulkan.
  public static bool VulkanAvailable() {
   IntPtr module = LoadLibrary("vulkan-1.dll");
   if (module == IntPtr.Zero) return false;
   FreeLibrary(module);
   return true;
  }

  public static string CleanApi(string value) { return Apis.FirstOrDefault(a => a == value) ?? AutoApi; }

  public static int CleanFilter(string value, int fallback) {
   int parsed;
   return int.TryParse(value, out parsed) && parsed >= 0 && parsed < Filters.Length ? parsed : fallback;
  }

  // Auto picks Vulkan whenever the machine has it; a Vulkan choice on a machine without it falls back to OpenGL.
  public static string ResolveApi(string wanted, bool vulkanAvailable) {
   return wanted == OpenGlApi || !vulkanAvailable ? OpenGlApi : VulkanApi;
  }

  public static List<Choice> Parse(string rules, string category) {
   var choices = new List<Choice>();
   foreach (string block in Regex.Split(rules ?? "", "(?m)^\\[Preset\\][ \\t]*\\r?$").Skip(1)) {
    var name = Regex.Match(block, "(?m)^name[ \\t]*=[ \\t]*(.+?)[ \\t]*\\r?$");
    var kind = Regex.Match(block, "(?m)^category[ \\t]*=[ \\t]*(.+?)[ \\t]*\\r?$");
    if (!name.Success || !kind.Success || kind.Groups[1].Value != category || choices.Any(c => c.Name == name.Groups[1].Value)) continue;
    bool isDefault = Regex.IsMatch(block, "(?m)^default[ \\t]*=[ \\t]*1[ \\t]*\\r?$");
    choices.Add(new Choice { Name = name.Groups[1].Value, Label = Label(name.Groups[1].Value), Default = isDefault });
   }
   return choices;
  }

  public static List<Choice> Read(string instance, string category) {
   string file = Path.Combine(instance, StockPack);
   return File.Exists(file) ? Parse(File.ReadAllText(file), category) : new List<Choice>();
  }

  // Keeps the name and the facts worth a glance (percentages and flags) and drops long explanations,
  // so "Low (Low Draw Distance, Sharp Shadows)" reads "Low" while "Medium (100%, Default)" is left as it is.
  // Only the shown text is translated; the preset name that reaches Cemu's rules stays as the pack spells it.
  public static string Label(string name, Func<string, string> translate = null) {
   Func<string, string> t = translate ?? (text => text);
   int open = name.IndexOf(" (", StringComparison.Ordinal);
   if (open < 0 || !name.EndsWith(")")) return t(name);
   var keep = name.Substring(open + 2, name.Length - open - 3).Split(',').Select(p => p.Trim())
    .Where(p => Regex.IsMatch(p, "^\\d+%$") || Regex.IsMatch(p, "^(Default|Recommended|Unstable|Experimental)\\b")).ToList();
   string word = t(name.Substring(0, open));
   return keep.Count == 0 ? word : word + " (" + string.Join(", ", keep.Select(p => Regex.IsMatch(p, "^\\d+%$") ? p : t(p))) + ")";
  }

  // Every word and flag Label can hand to the translator, so a test can check each one has a translation.
  public static readonly string[] LabelWords = { "Disabled", "NVIDIA FXAA", "Normal FXAA", "Low", "Medium", "High", "Very High", "Ultra", "Extreme", "Default", "Recommended", "Unstable", "Experimental" };

  // The wanted preset when the pack has it, otherwise the pack's default (or its first preset).
  public static Choice Pick(IList<Choice> choices, string wanted) {
   if (choices.Count == 0) return null;
   return choices.FirstOrDefault(c => c.Name == wanted) ?? choices.FirstOrDefault(c => c.Default) ?? choices[0];
  }

  // The closest offered render scale to a saved one, so an odd or hand-edited value cannot reach the plan.
  public static double NearestScale(double wanted) {
   if (double.IsNaN(wanted) || double.IsInfinity(wanted)) return DefaultScale;
   return Scales.OrderBy(s => Math.Abs(s - wanted)).First();
  }

  public static string ScaleLabel(double scale, Func<string, string> translate = null) {
   Func<string, string> t = translate ?? (text => text);
   string percent = Math.Round(scale * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
   return scale == DefaultScale ? percent + " " + t("(window size)") : scale > DefaultScale ? percent + " " + t("(sharper)") : percent + " " + t("(faster)");
  }
 }
}
