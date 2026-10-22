using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace HyruleOnline {
 enum LayoutMode { Auto, SideBySide, TopBottom }

 struct Area { public int X, Y, Width, Height; }

 // What one Cemu window needs: where it sits and what the game renders and assumes about its shape.
 struct PlayerView { public Area Area; public int RenderWidth, RenderHeight, RatioWidth, RatioHeight; public bool Ultrawide, Square; }

 sealed class DisplayPlan { public LayoutMode Layout; public PlayerView[] Players; }

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

  public const int MinDisplayWidth = 320, MinDisplayHeight = 240, MaxRenderSide = 8192;

  // Sharp renders at 150% of the window, like the local co-op launcher. Sizes are even and never above MaxRenderSide.
  public static void RenderSize(Area area, bool sharp, out int width, out int height) {
   double scale = sharp ? 1.5 : 1.0;
   double fit = Math.Min(1.0, (double)MaxRenderSide / Math.Max(area.Width, area.Height) / scale);
   width = Even(area.Width * scale * fit); height = Even(area.Height * scale * fit);
  }

  static int Even(double value) { return Math.Max(2, (int)Math.Floor(value / 2 + 1e-9) * 2); }

  // The aspect ratio the BOTW graphic pack gets for a render size, reduced and kept small enough for the pack.
  public static void AspectRatio(int width, int height, out int ratioWidth, out int ratioHeight) {
   int a = width, b = height;
   while (b != 0) { int rest = a % b; a = b; b = rest; }
   ratioWidth = width / a; ratioHeight = height / a;
   int largest = Math.Max(ratioWidth, ratioHeight);
   if (largest <= 1000) return;
   ratioWidth = Math.Max(1, (int)Math.Round(ratioWidth * 1000.0 / largest)); ratioHeight = Math.Max(1, (int)Math.Round(ratioHeight * 1000.0 / largest));
  }

  public static DisplayPlan Plan(LayoutMode mode, bool sharp, int width, int height) {
   if (width < MinDisplayWidth || height < MinDisplayHeight) throw new ArgumentOutOfRangeException("width", "The display is too small or not ready.");
   var areas = Split(mode, width, height);
   var plan = new DisplayPlan { Layout = Resolve(mode, width, height), Players = new PlayerView[2] };
   for (int i = 0; i < 2; i++) {
    var view = new PlayerView { Area = areas[i] };
    RenderSize(areas[i], sharp, out view.RenderWidth, out view.RenderHeight);
    AspectRatio(view.RenderWidth, view.RenderHeight, out view.RatioWidth, out view.RatioHeight);
    double ratio = (double)view.RenderWidth / view.RenderHeight;
    view.Ultrawide = ratio > 16.0 / 9.0 + 0.01;
    view.Square = ratio <= 1.5 + 0.01;
    plan.Players[i] = view;
   }
   return plan;
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

  [DllImport("user32.dll")] static extern bool SetMenu(IntPtr window, IntPtr menu);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
  [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }

  [DllImport("user32.dll")] static extern IntPtr GetMenu(IntPtr window);
  [DllImport("user32.dll")] static extern bool DrawMenuBar(IntPtr window);

  // Cemu puts its menu bar back when the game loads, which would shave 20 pixels off the picture.
  public static bool HideMenu(IntPtr window) {
   if (GetMenu(window) == IntPtr.Zero) return false;
   SetMenu(window, IntPtr.Zero);
   DrawMenuBar(window);
   return true;
  }

  // Removes the title bar, border and menu bar, then reports the client size so the caller can check that the game fills the area.
  public static void Place(IntPtr window, Area area, out int clientWidth, out int clientHeight) {
   ShowWindow(window, SwRestore);
   int style = GetWindowLong(window, GwlStyle);
   SetWindowLong(window, GwlStyle, style & ~(WsCaption | WsThickFrame | WsSysMenu));
   SetMenu(window, IntPtr.Zero);
   SetWindowPos(window, IntPtr.Zero, area.X, area.Y, area.Width, area.Height, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
   Rect client;
   GetClientRect(window, out client);
   clientWidth = client.Right - client.Left; clientHeight = client.Bottom - client.Top;
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
