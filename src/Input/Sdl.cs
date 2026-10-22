using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HyruleOnline {
 [StructLayout(LayoutKind.Sequential)] struct PadState { public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
 [StructLayout(LayoutKind.Sequential)] struct XInputState { public uint packet; public PadState pad; }

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
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern short SDL_GetGamepadAxis(IntPtr pad, int axis);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_GetGamepadButton(IntPtr pad, int button);
  [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_RumbleGamepad(IntPtr pad, ushort low, ushort high, uint duration);

  static readonly uint[] ids = new uint[4];
  static readonly IntPtr[] handles = new IntPtr[4];
  public static readonly string[] Names = new string[4];

  static string Utf8(IntPtr value) {
   if (value == IntPtr.Zero) return "Controller";
   int length = 0; while (Marshal.ReadByte(value, length) != 0) length++;
   var bytes = new byte[length]; Marshal.Copy(value, bytes, 0, length); return Encoding.UTF8.GetString(bytes);
  }

  public static void Init() {
   string folder = AppDomain.CurrentDomain.BaseDirectory;
   if (File.Exists(Path.Combine(folder, "xinput1_4.dll"))) throw new Exception("The bridge must not sit beside the router, where SDL could load it as system input.");
   SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
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
    }
    foreach (uint id in current) {
     if (Array.IndexOf(ids, id) >= 0) continue;
     int slot = Array.FindIndex(handles, h => h == IntPtr.Zero); if (slot < 0) break;
     var handle = SDL_OpenGamepad(id); if (handle == IntPtr.Zero) continue;
     ids[slot] = id; handles[slot] = handle; Names[slot] = Utf8(SDL_GetGamepadName(handle));
    }
   } finally { SDL_free(memory); }
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
