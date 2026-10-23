using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HyruleOnline {
 // Lays the player badge out in the cells the preview really produces and checks that it is never cut off, at every turn.
 static class BadgeTests {
  static int failures;

  static void Check(string name, bool ok, string detail) {
   Console.WriteLine((ok ? "ok:   " : "FAIL: ") + name + (ok ? "" : " (" + detail + ")"));
   if (!ok) failures++;
  }

  static Rect Bounds(FrameworkElement element, UIElement host) {
   return element.TransformToAncestor(host).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
  }

  static void Run() {
   var avatar = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
   // Split-screen cells for the usual display shapes, and one-display-per-player tiles from a large to a very small second display.
   var cells = new object[][] {
    new object[] { "16:9 side by side", 174.0, 168.0 }, new object[] { "16:9 top and bottom", 348.0, 84.0 }, new object[] { "ultrawide top and bottom", 348.0, 44.0 },
    new object[] { "portrait phone side by side", 55.0, 118.0 }, new object[] { "large second display", 220.0, 90.0 }, new object[] { "small second display", 60.0, 34.0 }, new object[] { "tall second display", 34.0, 90.0 }
   };
   foreach (object[] cell in cells) {
    foreach (int turn in new[] { 0, 90, 180, 270 }) {
     double width = (double)cell[1], height = (double)cell[2];
     var host = new Border { Width = width, Height = height };
     var badge = (Viewbox)PlayerBadge.Build(avatar, "P1", Brushes.White, turn);
     host.Child = badge;
     host.Measure(new Size(width, height));
     host.Arrange(new Rect(0, 0, width, height));
     host.UpdateLayout();
     var content = (StackPanel)badge.Child;
     var textBounds = Bounds((FrameworkElement)content.Children[0], host);
     var pictureBounds = Bounds((FrameworkElement)content.Children[1], host);
     var bounds = Rect.Union(textBounds, pictureBounds);
     string name = cell[0] + " cell " + width + " x " + height + ", turned " + turn;
     Check(name + ": the label and avatar stay inside the cell", bounds.Left >= -0.5 && bounds.Top >= -0.5 && bounds.Right <= width + 0.5 && bounds.Bottom <= height + 0.5, bounds.ToString());
     Check(name + ": the badge keeps a readable size", bounds.Width > 8 && bounds.Height > 8, bounds.ToString());
     bool turnedSideways = turn == 90 || turn == 270;
     if (turnedSideways) {
      Check(name + ": the text is on the left and the picture on the right", textBounds.Right <= pictureBounds.Left + 0.5, textBounds + " / " + pictureBounds);
      Check(name + ": the sideways pair is not stacked", bounds.Width > bounds.Height * 0.9 || pictureBounds.Top < textBounds.Bottom, bounds.ToString());
     } else {
      bool textFirst = turn == 0;
      Check(name + ": the pair is mirrored by the turn", textFirst ? textBounds.Right <= pictureBounds.Left + 0.5 : pictureBounds.Right <= textBounds.Left + 0.5, textBounds + " / " + pictureBounds);
      Check(name + ": the upright pair is wider than tall", bounds.Width > bounds.Height, bounds.ToString());
     }
    }
   }
  }

  [STAThread]
  static int Main() {
   Exception error = null;
   var thread = new Thread(() => { try { Run(); } catch (Exception e) { error = e; } });
   thread.SetApartmentState(ApartmentState.STA);
   thread.Start();
   thread.Join();
   if (error != null) { Console.WriteLine("FAIL: " + error); return 1; }
   return failures == 0 ? 0 : 1;
  }
 }
}
