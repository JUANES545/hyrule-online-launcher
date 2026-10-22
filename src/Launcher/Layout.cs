using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace HyruleOnline {
 enum LayoutMode { Auto, SideBySide, TopBottom }

 struct Area { public int X, Y, Width, Height; }

 // Splits the primary display between the two Cemu windows. Same Auto rule as the local co-op launcher:
 // a wide display (2:1 or more) goes side by side, anything else is stacked.
 static class ScreenLayout {
  public static LayoutMode Resolve(LayoutMode mode, int width, int height) {
   if (mode != LayoutMode.Auto) return mode;
   return (double)width / height >= 2.0 ? LayoutMode.SideBySide : LayoutMode.TopBottom;
  }

  public static Area[] Split(LayoutMode mode, int width, int height) {
   if (Resolve(mode, width, height) == LayoutMode.SideBySide) {
    int half = width / 2;
    return new[] { new Area { X = 0, Y = 0, Width = half, Height = height }, new Area { X = half, Y = 0, Width = width - half, Height = height } };
   }
   int top = height / 2;
   return new[] { new Area { X = 0, Y = 0, Width = width, Height = top }, new Area { X = 0, Y = top, Width = width, Height = height - top } };
  }

  public static LayoutMode Parse(string text) {
   LayoutMode mode;
   return Enum.TryParse(text == null ? "" : text.Trim(), true, out mode) && Enum.IsDefined(typeof(LayoutMode), mode) ? mode : LayoutMode.Auto;
  }

  // Sharp renders at 150% of the window, like the local co-op launcher.
  public static void RenderSize(Area area, bool sharp, out int width, out int height) {
   width = area.Width; height = area.Height;
   if (!sharp) return;
   width = (int)Math.Round(width * 1.5 / 2) * 2; height = (int)Math.Round(height * 1.5 / 2) * 2;
  }

  public static int FrameRate(int hz, int limit) {
   if (limit != 60 && limit != 90) throw new ArgumentOutOfRangeException("limit", "Choose 60 or 90 FPS.");
   int cap = Math.Min(Math.Max(hz, 20), limit);
   return new[] { 90, 60, 55, 48, 40, 33, 30, 20 }.First(fps => fps <= cap);
  }
 }

 [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DevMode {
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string device;
  public ushort spec, driver, size, extra; public uint fields;
  public int x, y; public uint orientation, fixedOutput; public short color, duplex, yResolution, tt, collate;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string form;
  public ushort logPixels; public uint bits, width, height, flags, frequency, icmMethod, icmIntent, media, dither, reserved1, reserved2, panningWidth, panningHeight;
 }

 // Moves a Cemu window to a screen area without a title bar or border, so the game fills the area.
 static class WindowPlacer {
  const int GwlStyle = -16, WsCaption = 0xC00000, WsThickFrame = 0x40000, WsSysMenu = 0x80000;
  const uint SwpNoZOrder = 0x4, SwpFrameChanged = 0x20, SwpShowWindow = 0x40;
  const int SwRestore = 9;

  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
  [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string name, int mode, ref DevMode settings);

  // Physical size and refresh rate of the primary display (the launcher is per-monitor DPI aware).
  public static void PrimaryDisplay(out int width, out int height, out int hz) {
   width = GetSystemMetrics(0); height = GetSystemMetrics(1); hz = 60;
   var mode = new DevMode();
   mode.size = (ushort)Marshal.SizeOf(typeof(DevMode));
   if (EnumDisplaySettings(null, -1, ref mode)) {
    if (mode.width > 0 && mode.height > 0) { width = (int)mode.width; height = (int)mode.height; }
    if (mode.frequency > 1) hz = (int)mode.frequency;
   }
  }

  public static void Place(IntPtr window, Area area) {
   ShowWindow(window, SwRestore);
   int style = GetWindowLong(window, GwlStyle);
   SetWindowLong(window, GwlStyle, style & ~(WsCaption | WsThickFrame | WsSysMenu));
   SetWindowPos(window, IntPtr.Zero, area.X, area.Y, area.Width, area.Height, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
  }

  // Main window handles of the Cemu instances that run from the given instance folder, keyed by process id.
  public static Dictionary<int, IntPtr> CemuWindows(string instanceFolder) {
   var windows = new Dictionary<int, IntPtr>();
   foreach (var process in Process.GetProcessesByName("Cemu")) {
    try {
     string path = process.MainModule.FileName;
     if (path.IndexOf(instanceFolder, StringComparison.OrdinalIgnoreCase) >= 0 && process.MainWindowHandle != IntPtr.Zero)
      windows[process.Id] = process.MainWindowHandle;
    }
    catch (Exception) { }
    finally { process.Dispose(); }
   }
   return windows;
  }
 }
}
