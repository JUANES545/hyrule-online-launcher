using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HyruleOnline {
 [StructLayout(LayoutKind.Sequential)] struct PadState { public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
 [StructLayout(LayoutKind.Sequential)] struct XInputState { public uint packet; public PadState pad; }

 // Which family of button labels a controller uses. SDL reports buttons by position, so only the labels and the confirm button differ.
 static class PadKind {
  public const int Auto = -1, Nintendo = 0, Xbox = 1, PlayStation = 2;
  public const ushort Bottom = 0x1000, Right = 0x2000;
  public static readonly string[] Names = { "Nintendo", "Xbox", "PlayStation" };

  // typeCode is SDL_GamepadType: 4 to 6 are PlayStation pads, 7 to 11 Nintendo ones; 0 and 1 (unknown, standard) fall back to the name.
  public static int Classify(int typeCode, string name) {
   if (typeCode >= 4 && typeCode <= 6) return PlayStation;
   if (typeCode >= 7 && typeCode <= 11) return Nintendo;
   string lower = (name ?? "").ToLowerInvariant();
   if (typeCode <= 1) {
    if (lower.Contains("nintendo") || lower.Contains("switch") || lower.Contains("joy-con") || lower.Contains("joycon")) return Nintendo;
    if (lower.Contains("playstation") || lower.Contains("dualshock") || lower.Contains("dualsense") || lower.Contains("wireless controller") || lower.Contains("ps4") || lower.Contains("ps5")) return PlayStation;
   }
   return Xbox;
  }

  // The layout a controller follows: the chosen one, or its own when the choice is Auto. Without a controller, Auto means Nintendo.
  public static int Resolve(int setting, int detected) { return setting != Auto ? setting : detected < 0 ? Nintendo : detected; }

  public static ushort Accept(int layout) { return layout == Nintendo ? Right : Bottom; }
  public static ushort Back(int layout) { return layout == Nintendo ? Bottom : Right; }

  // The two small centre buttons: XInput's Start (0x10) and Back (0x20).
  public const ushort StartButton = 0x10, SelectButton = 0x20;
 }

 // Thin SDL3 gamepad wrapper. Call every method from the same thread.
 static class Sdl {
  const string Dll = "SDL3.dll";
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_Init(uint flags);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_Quit();
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_SetHint(string name, string value);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetError();
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_AddGamepadMappingsFromFile(byte[] file);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_UpdateGamepads();
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_PumpEvents();
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetGamepads(out int count);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_free(IntPtr memory);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_OpenGamepad(uint id);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_CloseGamepad(IntPtr pad);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetGamepadName(IntPtr pad);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_GetGamepadType(IntPtr pad);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern short SDL_GetGamepadAxis(IntPtr pad, int axis);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_GetGamepadButton(IntPtr pad, int button);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_RumbleGamepad(IntPtr pad, ushort low, ushort high, uint duration);

  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SDL_GetTicksNS();
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_GamepadHasSensor(IntPtr pad, int sensor);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_SetGamepadSensorEnabled(IntPtr pad, int sensor, [MarshalAs(UnmanagedType.I1)] bool enabled);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_PeepEvents([Out] SensorEvent[] events, int count, int action, uint first, uint last);

  [StructLayout(LayoutKind.Explicit, Size = 128)] struct SensorEvent {
   [FieldOffset(0)] public uint type;
   [FieldOffset(8)] public ulong timestamp;
   [FieldOffset(16)] public uint device;
   [FieldOffset(20)] public int sensor;
   [FieldOffset(24)] public float x;
   [FieldOffset(28)] public float y;
   [FieldOffset(32)] public float z;
  }

  static readonly uint[] ids = new uint[4];
  static readonly IntPtr[] handles = new IntPtr[4];
  static readonly ControllerMotion[] motion = { new ControllerMotion(), new ControllerMotion(), new ControllerMotion(), new ControllerMotion() };
  static readonly SensorEvent[] events = new SensorEvent[64];
  public static readonly string[] Names = new string[4];
  // The button layout family each connected controller belongs to (PadKind), decided when it is opened.
  public static readonly int[] Kinds = new int[4];

  static string Utf8(IntPtr value) {
   if (value == IntPtr.Zero) return "Controller";
   int length = 0; while (Marshal.ReadByte(value, length) != 0) length++;
   var bytes = new byte[length]; Marshal.Copy(value, bytes, 0, length); return Encoding.UTF8.GetString(bytes);
  }

  public static void Init() {
   string folder = AppDomain.CurrentDomain.BaseDirectory;
   if (File.Exists(Path.Combine(folder, "xinput1_4.dll"))) throw new Exception("The bridge must not sit beside the router, where SDL could load it as system input.");
   SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
   SDL_SetHint("SDL_JOYSTICK_ENHANCED_REPORTS", "1");
   if (!SDL_Init(0x2000)) throw new Exception("SDL could not initialize controllers: " + Utf8(SDL_GetError()));
   var database = Path.Combine(folder, "gamecontrollerdb.txt");
   if (SDL_AddGamepadMappingsFromFile(Encoding.UTF8.GetBytes(database + "\0")) < 0) throw new Exception("Could not load gamecontrollerdb.txt: " + Utf8(SDL_GetError()));
  }

  public static bool Present(int slot) { return handles[slot] != IntPtr.Zero; }

  public static void Refresh() {
   SDL_PumpEvents(); SDL_UpdateGamepads();
   int count; var memory = SDL_GetGamepads(out count);
   if (memory == IntPtr.Zero) return;
   try {
    var current = new uint[count];
    for (int i = 0; i < count; i++) current[i] = unchecked((uint)Marshal.ReadInt32(memory, i * 4));
    for (int i = 0; i < 4; i++) if (handles[i] != IntPtr.Zero && Array.IndexOf(current, ids[i]) < 0) {
     SDL_CloseGamepad(handles[i]); handles[i] = IntPtr.Zero; ids[i] = 0; Names[i] = null;
     motion[i].Configure(false, false);
    }
    foreach (uint id in current) {
     if (Array.IndexOf(ids, id) >= 0) continue;
     int slot = Array.FindIndex(handles, h => h == IntPtr.Zero); if (slot < 0) break;
     var handle = SDL_OpenGamepad(id); if (handle == IntPtr.Zero) continue;
     ids[slot] = id; handles[slot] = handle; Names[slot] = Utf8(SDL_GetGamepadName(handle));
     Kinds[slot] = PadKind.Classify(SDL_GetGamepadType(handle), Names[slot]);
     bool accel = SDL_GamepadHasSensor(handle, 1) && SDL_SetGamepadSensorEnabled(handle, 1, true);
     bool gyro = SDL_GamepadHasSensor(handle, 2) && SDL_SetGamepadSensorEnabled(handle, 2, true);
     motion[slot].Configure(accel, gyro);
    }
   } finally { SDL_free(memory); }
   // Buttons are polled, so drain the joystick and gamepad events here: unused axis events must not starve the sensor reports.
   for (int batch = 0; batch < 4; batch++) {
    int read = SDL_PeepEvents(events, events.Length, 2, 0x600, 0x65b);
    if (read <= 0) break;
    ulong now = SDL_GetTicksNS();
    for (int index = 0; index < read; index++) {
     if (events[index].type != 0x659) continue;
     var value = events[index]; int slot = Array.IndexOf(ids, value.device);
     if (slot >= 0 && handles[slot] != IntPtr.Zero) motion[slot].Accept(value.sensor, value.x, value.y, value.z, value.timestamp, now);
    }
    if (read < events.Length) break;
   }
  }

  public static MotionReading Motion(int slot) {
   return slot >= 0 && slot < 4 && handles[slot] != IntPtr.Zero ? motion[slot].Read(SDL_GetTicksNS()) : new MotionReading();
  }

  public static short InvertY(short value) { return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, -(int)value)); }
  public static byte Trigger(short value) { return (byte)(Math.Max(0, (int)value) * 255 / 32767); }

  public static XInputState Read(int slot) {
   var state = new XInputState();
   if (handles[slot] == IntPtr.Zero) return state;
   ushort[] masks = { 0x1000, 0x2000, 0x4000, 0x8000, 0x20, 0, 0x10, 0x40, 0x80, 0x100, 0x200, 1, 2, 4, 8 };
   var axes = new short[6];
   for (int i = 0; i < 6; i++) axes[i] = SDL_GetGamepadAxis(handles[slot], i);
   state.pad.lx = axes[0]; state.pad.ly = InvertY(axes[1]); state.pad.rx = axes[2]; state.pad.ry = InvertY(axes[3]);
   state.pad.lt = Trigger(axes[4]); state.pad.rt = Trigger(axes[5]);
   for (int i = 0; i < masks.Length; i++) if (SDL_GetGamepadButton(handles[slot], i)) state.pad.buttons |= masks[i];
   state.packet = unchecked((uint)Environment.TickCount);
   return state;
  }

  public static void Rumble(int slot, ushort low, ushort high, uint milliseconds) {
   if (handles[slot] != IntPtr.Zero) SDL_RumbleGamepad(handles[slot], low, high, milliseconds);
  }

  public static void Shutdown() {
   for (int i = 0; i < 4; i++) if (handles[i] != IntPtr.Zero) { SDL_RumbleGamepad(handles[i], 0, 0, 0); SDL_CloseGamepad(handles[i]); handles[i] = IntPtr.Zero; }
   SDL_Quit();
  }
 }
}
