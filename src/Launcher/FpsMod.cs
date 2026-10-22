using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // The frame rates come from the FPS++ graphic pack itself (its Normal Settings "FPS Limit" presets),
 // so the launcher only offers what the mod supports, and never more than the display can show.
 static class FpsMod {
  public const string PackFile = "graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Mods\\FPS++\\rules.txt";
  public const string ModeCategory = "Mode", NormalMode = "Normal Settings", LimitCategory = "FPS Limit";
  public const int DefaultFps = 60;

  public sealed class Limit {
   public int Fps;
   public string Preset;
  }

  public static List<Limit> Parse(string rules) {
   var limits = new List<Limit>();
   foreach (string block in Regex.Split(rules ?? "", "(?m)^\\[Preset\\][ \\t]*\\r?$").Skip(1)) {
    var name = Regex.Match(block, "(?m)^name[ \\t]*=[ \\t]*(.+?)[ \\t]*\\r?$");
    var category = Regex.Match(block, "(?m)^category[ \\t]*=[ \\t]*(.+?)[ \\t]*\\r?$");
    var fps = Regex.Match(block, "(?m)^\\$fpsLimitNormal[ \\t]*=[ \\t]*(\\d+)[ \\t]*\\r?$");
    if (!name.Success || !category.Success || !fps.Success || category.Groups[1].Value != LimitCategory) continue;
    int value = int.Parse(fps.Groups[1].Value);
    if (value > 0 && limits.All(l => l.Fps != value)) limits.Add(new Limit { Fps = value, Preset = name.Groups[1].Value });
   }
   return limits.OrderBy(l => l.Fps).ToList();
  }

  public static List<Limit> Read(string instance) {
   string file = Path.Combine(instance, PackFile);
   return File.Exists(file) ? Parse(File.ReadAllText(file)) : new List<Limit>();
  }

  // Windows reports 0 or 1 when the refresh rate is not known; assume 60 then.
  public static List<Limit> Available(IEnumerable<Limit> all, int hz) {
   int cap = hz > 1 ? hz : DefaultFps;
   var list = all.OrderBy(l => l.Fps).ToList();
   var fit = list.Where(l => l.Fps <= cap).ToList();
   return fit.Count > 0 ? fit : list.Take(1).ToList();
  }

  // The wanted rate when it is offered, otherwise the closest lower one (or the lowest one).
  public static Limit Choose(IList<Limit> options, int wanted) {
   if (options.Count == 0) return null;
   return options.Where(l => l.Fps <= wanted).OrderByDescending(l => l.Fps).FirstOrDefault() ?? options.OrderBy(l => l.Fps).First();
  }
 }
}
