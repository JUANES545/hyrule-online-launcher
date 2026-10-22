using System;
using System.Threading;

// Headless front end for the shared router, used by Start-Online.ps1 without the launcher.
namespace HyruleOnline {
 static class Program {
  static int Main(string[] args) {
   if (Array.IndexOf(args, "--probe") >= 0) {
    try { Sdl.Init(); Thread.Sleep(500); Sdl.Refresh(); for (int i = 0; i < 4; i++) Console.WriteLine("slot " + i + ": " + (Sdl.Names[i] ?? "(none)")); }
    catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    finally { Sdl.Shutdown(); }
    return 0;
   }
   var stop = new ManualResetEvent(false);
   Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };
   using (var router = new Router()) {
    router.Message += Console.WriteLine;
    try { router.Start(); }
    catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    Console.WriteLine("Routing controllers. Press Ctrl+C to stop.");
    stop.WaitOne();
   }
   return 0;
  }
 }
}
