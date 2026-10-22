using System;

namespace HyruleOnline {
 static class RouterTests {
  static void Require(bool ok, string message) { if (!ok) throw new Exception("TEST: " + message); }

  static int[] Reconcile(int[] routes, params bool[] connected) { Router.Reconcile(routes, connected); return routes; }

  static int Main() {
   try {
    var routes = Reconcile(new[] { -1, -1 }, true, true, false, false);
    Require(routes[0] == 0 && routes[1] == 1, "first two pads take players one and two");

    routes = Reconcile(new[] { -1, -1 }, false, false, true, true);
    Require(routes[0] == 2 && routes[1] == 3, "pads in slots two and three");

    routes = Reconcile(new[] { 0, 1 }, false, true, true, false);
    Require(routes[0] == 2 && routes[1] == 1, "reconnection keeps player two and refills player one");

    routes = Reconcile(new[] { 0, 1 }, false, false, false, false);
    Require(routes[0] == 0 && routes[1] == 1, "slots stay reserved while pads are away");

    for (int mask = 0; mask < 16; mask++)
     for (int first = -1; first < 4; first++)
      for (int second = -1; second < 4; second++) {
       if (first >= 0 && first == second) continue;
       var present = new bool[4];
       int count = 0;
       for (int i = 0; i < 4; i++) { present[i] = (mask & (1 << i)) != 0; if (present[i]) count++; }
       routes = Reconcile(new[] { first, second }, present);
       Require(routes[0] < 0 || routes[0] != routes[1], "unique controller ownership");
       Require(first < 0 || !present[first] || routes[0] == first, "connected player one is kept");
       Require(second < 0 || !present[second] || routes[1] == second, "connected player two is kept");
       int assigned = 0;
       foreach (int route in routes) if (route >= 0 && present[route]) assigned++;
       Require(assigned == Math.Min(2, count), "every available player is filled");
      }

    routes = new[] { 0, 1 }; Router.Assign(routes, 0, 1);
    Require(routes[0] == 1 && routes[1] == 0, "assigning the other player's pad swaps them");
    routes = new[] { 0, -1 }; Router.Assign(routes, 1, 0);
    Require(routes[1] == 0 && routes[0] == -1, "taking the only pad leaves the other player empty");
    routes = new[] { 0, 1 }; Router.Assign(routes, 0, 2);
    Require(routes[0] == 2 && routes[1] == 1, "assigning a free pad changes only that player");

    Console.WriteLine("PASS: router reconciliation and assignment.");

    Require(ScreenLayout.Resolve(LayoutMode.Auto, 1920, 1080) == LayoutMode.TopBottom, "a 16:9 display is stacked by Auto");
    Require(ScreenLayout.Resolve(LayoutMode.Auto, 3840, 1080) == LayoutMode.SideBySide, "a 32:9 display is split side by side by Auto");
    Require(ScreenLayout.Resolve(LayoutMode.SideBySide, 1080, 1920) == LayoutMode.SideBySide, "an explicit choice is kept");
    var halves = ScreenLayout.Split(LayoutMode.SideBySide, 1921, 1080);
    Require(halves[0].X == 0 && halves[0].Width + halves[1].Width == 1921 && halves[1].X == halves[0].Width && halves[1].Height == 1080, "side by side covers the display");
    halves = ScreenLayout.Split(LayoutMode.TopBottom, 1920, 1081);
    Require(halves[0].Y == 0 && halves[0].Height + halves[1].Height == 1081 && halves[1].Y == halves[0].Height && halves[1].Width == 1920, "top and bottom covers the display");
    Require(halves.Length == 2 && ScreenLayout.Resolve(LayoutMode.TopBottom, 3840, 1080) == LayoutMode.TopBottom, "an explicit stacked choice is kept");
    int rw, rh;
    ScreenLayout.RenderSize(new Area { Width = 1920, Height = 540 }, false, out rw, out rh);
    Require(rw == 1920 && rh == 540, "balanced renders at window size");
    ScreenLayout.RenderSize(new Area { Width = 1920, Height = 540 }, true, out rw, out rh);
    Require(rw == 2880 && rh == 810, "sharp renders at 150 percent");
    Require(ScreenLayout.FrameRate(60, 90) == 60 && ScreenLayout.FrameRate(144, 90) == 90 && ScreenLayout.FrameRate(144, 60) == 60 && ScreenLayout.FrameRate(50, 60) == 48, "frame rate follows the refresh rate");
    Require(ScreenLayout.Parse("TopBottom") == LayoutMode.TopBottom && ScreenLayout.Parse("garbage") == LayoutMode.Auto && ScreenLayout.Parse("7") == LayoutMode.Auto && ScreenLayout.Parse(null) == LayoutMode.Auto, "saved layout parsing falls back to Auto");
    Console.WriteLine("PASS: screen layout.");
    return 0;
   }
   catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
  }
 }
}
