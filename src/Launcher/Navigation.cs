using System;
using System.Collections.Generic;

namespace HyruleOnline {
 // LB and RB move between the launcher pages, wrapping around at the ends.
 static class PageNav {
  public const ushort LeftBumper = 0x0100, RightBumper = 0x0200;

  public static int Step(ushort previous, ushort current) {
   ushort pressed = (ushort)(current & ~previous);
   return (pressed & RightBumper) != 0 ? 1 : (pressed & LeftBumper) != 0 ? -1 : 0;
  }

  // True when the button (a mask such as PadKind.StartButton) went down since the previous poll.
  public static bool Pressed(ushort previous, ushort current, ushort mask) { return (current & mask) != 0 && (previous & mask) == 0; }

  public static int Move(int page, int step, int count) { return count <= 0 ? 0 : ((page + step) % count + count) % count; }
 }

 // Start (or Select) is both a menu action and part of the menu shortcut. When the shortcut uses it, the action waits for the release
 // and is dropped if anything else was held meanwhile, so opening or closing the menu with the shortcut never restarts the session.
 sealed class TapGuard {
  bool down, spoiled;

  public bool Update(ushort buttons, byte lt, byte rt, ushort mask, bool onRelease) {
   bool held = (buttons & mask) != 0;
   bool other = (buttons & ~mask) != 0 || lt >= MenuShortcut.TriggerOff || rt >= MenuShortcut.TriggerOff;
   bool fire = false;
   if (!onRelease) fire = held && !down;
   else if (held && !down) spoiled = other;
   else if (held && other) spoiled = true;
   else if (!held && down) fire = !spoiled;
   down = held;
   return fire;
  }
 }

 // What one controller did to the menu since the last poll: a direction (0 up, 1 down, 2 left, 3 right, -1 none) and new A / B presses.
 struct PadPress {
  public int Direction;
  public bool Accept, Back;
 }

 // Turns the D-pad, left stick, A and B of one controller into menu presses. A held direction repeats after a short delay.
 sealed class PadNav {
  public const ushort DpadUp = 0x1, DpadDown = 0x2, DpadLeft = 0x4, DpadRight = 0x8, ButtonA = 0x1000, ButtonB = 0x2000;
  public const int Deadzone = 16000, FirstRepeat = 420, NextRepeat = 120;
  // Buttons are read by position: the Nintendo layout confirms with the right button and goes back with the bottom one; Xbox and PlayStation do the opposite. Menus only, never the game.
  public int Layout = PadKind.Nintendo;
  public ushort AcceptButton { get { return PadKind.Accept(Layout); } }
  public ushort BackButton { get { return PadKind.Back(Layout); } }
  ushort previous;
  int held = -1;
  long nextAt;

  public static int Direction(ushort buttons, short x, short y) {
   if ((buttons & DpadUp) != 0) return 0;
   if ((buttons & DpadDown) != 0) return 1;
   if ((buttons & DpadLeft) != 0) return 2;
   if ((buttons & DpadRight) != 0) return 3;
   int ax = Math.Abs((int)x), ay = Math.Abs((int)y);
   if (Math.Max(ax, ay) < Deadzone) return -1;
   // XInput reports stick up as a positive Y.
   return ax > ay ? (x < 0 ? 2 : 3) : (y > 0 ? 0 : 1);
  }

  public PadPress Update(ushort buttons, short x, short y, long now) {
   var press = new PadPress { Direction = -1 };
   int direction = Direction(buttons, x, y);
   if (direction != held) {
    held = direction;
    if (direction >= 0) { press.Direction = direction; nextAt = now + FirstRepeat; }
   }
   else if (direction >= 0 && now >= nextAt) { press.Direction = direction; nextAt = now + NextRepeat; }
   ushort fresh = (ushort)(buttons & ~previous);
   press.Accept = (fresh & AcceptButton) != 0;
   press.Back = (fresh & BackButton) != 0;
   previous = buttons;
   return press;
  }
 }

 struct Box {
  public double X, Y, Width, Height;
  public Box(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
 }

 // Picks the neighbour a controller moves to. A box lined up with the current one (it shares a row for left/right, a column for up/down) always wins over one that is not;
 // among those the nearest wins and the best centred breaks ties. Only up and down may fall back to a box that is not lined up, so left and right never jump to another row.
 static class Spatial {
  static double Gap(double start, double length, double otherStart, double otherLength) {
   return Math.Max(0, Math.Max(otherStart - (start + length), start - (otherStart + otherLength)));
  }

  // With sideways set, left and right may also reach a box that is not in the same row when nothing lines up.
  // How far a point is from a span: zero inside it. A wide box above two narrow ones is then close to both, instead of favouring a wide box further down.
  static double Away(double point, double start, double length) {
   return point < start ? start - point : point > start + length ? point - (start + length) : 0;
  }

  public static int Pick(Box from, IList<Box> items, int direction) { return Pick(from, items, direction, false); }

  public static int Pick(Box from, IList<Box> items, int direction, bool sideways) {
   if (direction < 0 || direction > 3) return -1;
   bool vertical = direction < 2;
   int best = -1, fallback = -1;
   double bestScore = double.MaxValue, fallbackScore = double.MaxValue, fromX = from.X + from.Width / 2, fromY = from.Y + from.Height / 2;
   for (int i = 0; i < items.Count; i++) {
    var item = items[i];
    double itemX = item.X + item.Width / 2, itemY = item.Y + item.Height / 2, along, offset, across;
    switch (direction) {
     case 0: if (itemY >= fromY - 1) continue; along = Math.Max(0, from.Y - (item.Y + item.Height)) + (fromY - itemY) * .1; break;
     case 1: if (itemY <= fromY + 1) continue; along = Math.Max(0, item.Y - (from.Y + from.Height)) + (itemY - fromY) * .1; break;
     case 2: if (itemX >= fromX - 1) continue; along = Math.Max(0, from.X - (item.X + item.Width)) + (fromX - itemX) * .1; break;
     default: if (itemX <= fromX + 1) continue; along = Math.Max(0, item.X - (from.X + from.Width)) + (itemX - fromX) * .1; break;
    }
    double miss;
    if (vertical) { across = Gap(from.X, from.Width, item.X, item.Width); offset = Math.Abs(itemX - fromX); miss = Away(fromX, item.X, item.Width); }
    else { across = Gap(from.Y, from.Height, item.Y, item.Height); offset = Math.Abs(itemY - fromY); miss = Away(fromY, item.Y, item.Height); }
    if (across <= 0) {
     double score = along + miss * .5 + offset * .1;
     if (score < bestScore) { bestScore = score; best = i; }
    }
    else if (vertical || sideways) {
     double score = along + across * 1.5 + offset * .3;
     if (score < fallbackScore) { fallbackScore = score; fallback = i; }
    }
   }
   return best >= 0 ? best : fallback;
  }
 }
}
