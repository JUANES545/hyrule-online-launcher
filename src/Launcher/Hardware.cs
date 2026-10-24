using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HyruleOnline {
 sealed class HardwareInfo {
  public string Gpu = "";
  public long VramMb;
  public int Threads;
  public double RamGb;
  public int Tier;
 }

 // Works out how much of a graphics load this PC can carry from the names Windows reports, and turns that
 // into starting settings. The classes are rough rules of thumb about the graphics card, not measurements.
 static class Hardware {
  public const int Basic = 0, Balanced = 1, High = 2, Ultra = 3;
  public static readonly string[] TierNames = { "Basic", "Balanced", "High", "Ultra" };
  public const string AntiAliasingChoice = "Normal FXAA";
  static readonly string[] Ignored = { "virtual", "basic render", "remote", "parsec", "microsoft", "indirect", "displaylink", "sudomaker", "mirage" };

  [StructLayout(LayoutKind.Sequential)]
  struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }

  [DllImport("kernel32")]
  static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

  public static bool IsRealAdapter(string name) {
   if (string.IsNullOrWhiteSpace(name)) return false;
   return !Ignored.Any(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
  }

  // Rates one graphics card by its name: 0 basic, 1 balanced, 2 high, 3 ultra. Unknown cards count as balanced.
  public static int GpuTier(string name) {
   string n = (name ?? "").ToUpperInvariant();
   bool laptop = n.Contains("LAPTOP") || n.Contains("MAX-Q") || n.Contains("MOBILE");
   int tier = RawTier(n);
   return laptop && tier >= 2 ? tier - 1 : tier;
  }

  static int RawTier(string n) {
   Match m = Regex.Match(n, "\\bRTX\\s*(?:A|PRO\\s*)?(\\d{4})\\b");
   if (m.Success) {
    int tens = int.Parse(m.Groups[1].Value) % 100;
    return tens >= 70 ? Ultra : tens >= 60 ? High : Balanced;
   }
   m = Regex.Match(n, "\\bGTX\\s*(\\d{3,4})\\b");
   if (m.Success) {
    int number = int.Parse(m.Groups[1].Value);
    if (number >= 1600) return number >= 1660 ? High : Balanced;
    if (number >= 1000) return number >= 1070 ? High : number >= 1060 || (number == 1050 && n.Contains(" TI")) ? Balanced : Basic;
    return number == 970 || number == 980 ? Balanced : Basic;
   }
   if (Regex.IsMatch(n, "\\b(GT|MX)\\s*\\d{3,4}\\b")) return Basic;
   m = Regex.Match(n, "\\bRX\\s*(\\d{4})(?!\\d)");
   if (m.Success) {
    int number = int.Parse(m.Groups[1].Value), series = number / 1000, level = number / 100 % 10;
    if (series >= 9) return number >= 9070 ? Ultra : High;
    if (series >= 6) return level >= 7 ? Ultra : level >= 6 ? High : level >= 4 ? Balanced : Basic;
    return series == 5 ? (level >= 7 ? High : level >= 5 ? Balanced : Basic) : Basic;
   }
   m = Regex.Match(n, "\\bRX\\s*(\\d{3})(?!\\d)");
   if (m.Success) return int.Parse(m.Groups[1].Value) >= 570 ? Balanced : Basic;
   if (Regex.IsMatch(n, "\\bRX\\s*VEGA\\s*(56|64)\\b")) return High;
   m = Regex.Match(n, "\\bARC\\b.*?\\b[AB](\\d{3})\\b");
   if (m.Success) return int.Parse(m.Groups[1].Value) >= 570 ? High : Balanced;
   if (n.Contains("ARC")) return Balanced;
   m = Regex.Match(n, "\\bRADEON\\s*(\\d{3})M\\b");
   if (m.Success) return int.Parse(m.Groups[1].Value) >= 780 ? Balanced : Basic;
   if (Regex.IsMatch(n, "\\b(UHD|HD GRAPHICS|IRIS|VEGA|RADEON(\\(TM\\))? GRAPHICS|RADEON PRO)\\b")) return Basic;
   return Balanced;
  }

  // The card's class, lowered when the memory or the rest of the PC would hold the games back.
  public static int Tier(string gpu, long vramMb, int threads, double ramGb) {
   int tier = GpuTier(gpu);
   if (vramMb > 0 && vramMb < 2048) tier = Basic;
   else if (vramMb > 0 && vramMb < 4096) tier = Math.Min(tier, Balanced);
   if ((threads > 0 && threads < 4) || (ramGb > 0 && ramGb < 12)) tier = Math.Min(tier, Balanced);
   return tier;
  }

  public static HardwareInfo Detect() {
   var info = new HardwareInfo { Threads = Environment.ProcessorCount };
   try {
    var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
    if (GlobalMemoryStatusEx(ref status)) info.RamGb = Math.Round(status.TotalPhys / 1073741824.0, 1);
   }
   catch (Exception) { }
   try {
    string best = null; long bestMemory = 0; int bestTier = -1;
    using (var classKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}")) {
     if (classKey != null) foreach (string sub in classKey.GetSubKeyNames()) {
      if (!Regex.IsMatch(sub, "^\\d{4}$")) continue;
      using (var adapter = classKey.OpenSubKey(sub)) {
       string name = adapter == null ? null : Convert.ToString(adapter.GetValue("DriverDesc"));
       if (!IsRealAdapter(name)) continue;
       long memory = MemoryOf(adapter.GetValue("HardwareInformation.qwMemorySize"));
       int tier = Tier(name, memory / 1048576, 0, 0);
       if (tier > bestTier || (tier == bestTier && memory > bestMemory)) { best = name.Trim(); bestMemory = memory; bestTier = tier; }
      }
     }
    }
    info.Gpu = best ?? ""; info.VramMb = bestMemory / 1048576;
   }
   catch (Exception) { }
   info.Tier = Tier(info.Gpu, info.VramMb, info.Threads, info.RamGb);
   return info;
  }

  static long MemoryOf(object value) {
   if (value is long) return (long)value;
   if (value is int) return (int)value & 0xFFFFFFFFL;
   var bytes = value as byte[];
   return bytes != null && bytes.Length >= 8 ? BitConverter.ToInt64(bytes, 0) : 0;
  }

  // How many pixels one game may render before this class of card struggles. Two games share the card.
  static double PixelBudget(int tier, int players) {
   double[] budget = { 1.0e6, 2.1e6, 4.5e6, 8.3e6 };
   return budget[Math.Max(0, Math.Min(Ultra, tier))] * (players >= 2 ? 0.75 : 1.0);
  }

  // The sharpest offered scale up to the window size that this class of card should manage; never above 100%.
  public static double RecommendedScale(int tier, int width, int height, int players) {
   double pixels = (double)width * height, budget = PixelBudget(tier, players);
   var fits = GraphicsMod.Scales.Where(s => s <= GraphicsMod.DefaultScale && pixels * s * s <= budget).ToList();
   return fits.Count == 0 ? GraphicsMod.Scales.Min() : fits.Max();
  }

  // Preset name starts in the Graphics pack ("Low", "Medium", "High", "Very High").
  public static string ShadowsFor(int tier) { return new[] { "Low", "Medium", "High", "High" }[Math.Max(0, Math.Min(Ultra, tier))]; }
  public static string ShadowDistanceFor(int tier) { return new[] { "Medium", "High", "Very High", "Very High" }[Math.Max(0, Math.Min(Ultra, tier))]; }

  public static GraphicsMod.Advanced AdvancedFor(int tier) {
   return new GraphicsMod.Advanced { Api = GraphicsMod.AutoApi, VSync = false, AsyncCompile = true, AccurateBarriers = false, Upscale = tier == Basic ? 0 : 1, Downscale = 0 };
  }

  // The pack preset called exactly the wanted word (before any "(...)" note); the pack's own default otherwise.
  public static string PickPreset(IList<GraphicsMod.Choice> choices, string wanted) {
   var found = choices.FirstOrDefault(c => c.Name == wanted || c.Name.StartsWith(wanted + " ", StringComparison.Ordinal)) ?? GraphicsMod.Pick(choices, null);
   return found == null ? null : found.Name;
  }
 }
}
