using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace HyruleOnline {
 sealed class RouterSnapshot {
  public readonly int[] Routes = new int[2];
  public readonly string[] Names = new string[4];
  public readonly PadState[] Pads = new PadState[4];
  public readonly MotionReading[] Motion = new MotionReading[4];
  public int Assigning = -1;
 }

 // Reads controllers through SDL3 on its own thread and publishes them in the shared-memory layout the
 // xinput1_4.dll bridge inside each Cemu understands: Cemu A reads player one, Cemu B reads player two.
 // Layout: enabled@0, player[2]@4/8, heartbeat@12, states[4]@16+16i, vibration@80+4i, rumbleSeq@96+4i, seqlock@112.
 sealed class Router : IDisposable {
  const string MapName = "Local\\HyruleCoop.Routing.v2";
  // Menu button layout, shared with the launcher's pad navigation: the Nintendo layout claims a controller with the right button, Xbox with the bottom one.
  public static volatile bool NintendoMenus = true;
  const ushort ButtonBottom = 0x1000, ButtonRight = 0x2000;
  readonly object gate = new object();
  readonly int[] routes = { -1, -1 };
  readonly bool[] connected = new bool[4];
  readonly string[] names = new string[4];
  readonly PadState[] pads = new PadState[4];
  readonly MotionReading[] motion = new MotionReading[4];
  readonly bool[] testRequested = new bool[2];
  int assigning = -1;
  Thread thread;
  volatile bool stop;
  Exception startError;

  public event Action<string> Message;

  // A player keeps a reserved slot while its controller is away; a newly connected controller takes a player whose slot is empty or gone.
  public static void Reconcile(int[] routes, bool[] connected) {
   for (int player = 0; player < 2; player++) {
    if (routes[player] >= 0 && connected[routes[player]]) continue;
    for (int slot = 0; slot < 4; slot++) {
     if (!connected[slot] || slot == routes[1 - player]) continue;
     routes[player] = slot; break;
    }
   }
  }

  // Gives the slot to the player; if the other player held it, that player takes the old slot.
  public static void Assign(int[] routes, int player, int slot) {
   int other = 1 - player;
   if (routes[other] == slot) routes[other] = routes[player];
   routes[player] = slot;
  }

  public void Start() {
   var ready = new ManualResetEvent(false);
   thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "InputRouter" };
   thread.Start();
   ready.WaitOne();
   if (startError != null) throw startError;
  }

  public RouterSnapshot Snapshot() {
   var snapshot = new RouterSnapshot();
   lock (gate) {
    Array.Copy(routes, snapshot.Routes, 2);
    Array.Copy(names, snapshot.Names, 4);
    Array.Copy(pads, snapshot.Pads, 4);
    Array.Copy(motion, snapshot.Motion, 4);
    snapshot.Assigning = assigning;
   }
   return snapshot;
  }

  public void BeginAssign(int player) { lock (gate) assigning = player; }
  public void CancelAssign() { lock (gate) assigning = -1; }
  public void Swap() { lock (gate) { int first = routes[0]; routes[0] = routes[1]; routes[1] = first; } }
  public void TestRumble(int player) { lock (gate) testRequested[player] = true; }

  void Notify(string text) { var handler = Message; if (handler != null) handler(text); }

  void Run(ManualResetEvent ready) {
   try { Sdl.Init(); }
   catch (Exception error) { startError = error; ready.Set(); return; }
   ready.Set();
   try {
    using (var map = MemoryMappedFile.CreateOrOpen(MapName, 128, MemoryMappedFileAccess.ReadWrite))
    using (var view = map.CreateViewAccessor(0, 128, MemoryMappedFileAccess.ReadWrite)) {
     for (int i = 0; i < 128; i += 4) view.Write(i, 0);
     view.Write(4, -1); view.Write(8, -1);
     var published = new[] { -2, -2 };
     var lastRumble = new int[4];
     var previousButtons = new ushort[4];
     var testSlot = new[] { -1, -1 };
     var testEnds = new long[2];
     var clock = Stopwatch.StartNew();
     int sequence = 0;
     while (!stop) {
      Sdl.Refresh();
      var states = new XInputState[4];
      var present = new bool[4];
      for (int i = 0; i < 4; i++) { present[i] = Sdl.Present(i); if (present[i]) states[i] = Sdl.Read(i); }
      int[] current;
      lock (gate) {
       for (int i = 0; i < 4; i++) { connected[i] = present[i]; names[i] = Sdl.Names[i]; pads[i] = states[i].pad; motion[i] = Sdl.Motion(i); }
       if (assigning >= 0) {
        for (int slot = 0; slot < 4; slot++) {
         ushort accept = NintendoMenus ? ButtonRight : ButtonBottom;
         if (!connected[slot] || (states[slot].pad.buttons & accept) == 0 || (previousButtons[slot] & accept) != 0) continue;
         Assign(routes, assigning, slot); assigning = -1; break;
        }
       }
       Reconcile(routes, connected);
       current = (int[])routes.Clone();
       for (int player = 0; player < 2; player++) {
        if (!testRequested[player]) continue;
        testRequested[player] = false;
        if (testSlot[player] >= 0) Sdl.Rumble(testSlot[player], 0, 0, 0);
        testSlot[player] = current[player];
        if (testSlot[player] >= 0) { Sdl.Rumble(testSlot[player], 0xC000, 0xC000, 400); testEnds[player] = clock.ElapsedMilliseconds + 400; }
       }
      }
      for (int i = 0; i < 4; i++) previousButtons[i] = states[i].pad.buttons;
      for (int player = 0; player < 2; player++) {
       if (testSlot[player] >= 0 && clock.ElapsedMilliseconds >= testEnds[player]) { Sdl.Rumble(testSlot[player], 0, 0, 0); testSlot[player] = -1; }
       if (published[player] == current[player]) continue;
       published[player] = current[player];
       Notify("Player " + (player + 1) + ": " + (current[player] < 0 ? "no controller" : Sdl.Names[current[player]]));
      }
      view.Write(112, ++sequence); Thread.MemoryBarrier();
      for (int i = 0; i < 4; i++) view.Write(16 + i * 16, ref states[i]);
      view.Write(4, current[0]); view.Write(8, current[1]); view.Write(12, unchecked((uint)Environment.TickCount));
      Thread.MemoryBarrier(); view.Write(112, ++sequence); view.Write(0, 1);
      for (int i = 0; i < 4; i++) {
       int rumble = view.ReadInt32(96 + i * 4);
       if (rumble == lastRumble[i]) continue;
       lastRumble[i] = rumble;
       uint motors = view.ReadUInt32(80 + i * 4);
       Sdl.Rumble(i, (ushort)(motors & 0xffff), (ushort)(motors >> 16), uint.MaxValue);
      }
      Thread.Sleep(4);
     }
     view.Write(0, 0);
    }
   }
   finally { Sdl.Shutdown(); }
  }

  public void Dispose() {
   stop = true;
   if (thread != null) thread.Join(2000);
  }
 }
}
