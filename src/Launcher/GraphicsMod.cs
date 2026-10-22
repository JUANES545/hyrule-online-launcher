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
  public static string Label(string name) {
   int open = name.IndexOf(" (", StringComparison.Ordinal);
   if (open < 0 || !name.EndsWith(")")) return name;
   var keep = name.Substring(open + 2, name.Length - open - 3).Split(',').Select(p => p.Trim())
    .Where(p => Regex.IsMatch(p, "^\\d+%$") || Regex.IsMatch(p, "^(Default|Recommended|Unstable|Experimental)\\b")).ToList();
   return keep.Count == 0 ? name.Substring(0, open) : name.Substring(0, open) + " (" + string.Join(", ", keep) + ")";
  }

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

  public static string ScaleLabel(double scale) {
   string percent = Math.Round(scale * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
   return scale == DefaultScale ? percent + " (window size)" : scale > DefaultScale ? percent + " (sharper)" : percent + " (faster)";
  }
 }
}
