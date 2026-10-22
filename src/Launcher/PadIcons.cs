using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HyruleOnline {
 // Line drawings of the two centre buttons of each controller family, so the launcher can show the real button next to an action.
 // Start: three lines on Xbox (Menu) and PlayStation (Options), a plus on Nintendo. Select: two squares on Xbox (View), a share arrow on PlayStation (Create), a minus on Nintendo.
 static class PadIcons {
  static readonly Brush Ink = Make(0xCF, 0xE3, 0xE4);

  static Brush Make(byte r, byte g, byte b) {
   var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
   brush.Freeze();
   return brush;
  }

  static Line Stroke(double x1, double y1, double x2, double y2) {
   return new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Ink, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
  }

  // Xbox and Nintendo buttons are round; PlayStation's centre buttons are pills.
  static Canvas Body(int layout) {
   bool pill = layout == PadKind.PlayStation;
   var canvas = new Canvas { Width = pill ? 32 : 24, Height = pill ? 20 : 24 };
   var outline = new Rectangle { Width = canvas.Width - 2, Height = canvas.Height - 2, RadiusX = (canvas.Height - 2) / 2, RadiusY = (canvas.Height - 2) / 2, Stroke = Ink, StrokeThickness = 1.8 };
   Canvas.SetLeft(outline, 1);
   Canvas.SetTop(outline, 1);
   canvas.Children.Add(outline);
   return canvas;
  }

  public static FrameworkElement Start(int layout) {
   var canvas = Body(layout);
   if (layout == PadKind.Nintendo) {
    canvas.Children.Add(Stroke(7, 12, 17, 12));
    canvas.Children.Add(Stroke(12, 7, 12, 17));
   }
   else if (layout == PadKind.PlayStation) {
    foreach (double y in new[] { 6.5, 10, 13.5 }) canvas.Children.Add(Stroke(10, y, 22, y));
   }
   else {
    foreach (double y in new[] { 8.0, 12, 16 }) canvas.Children.Add(Stroke(7, y, 17, y));
   }
   return canvas;
  }

  public static FrameworkElement Select(int layout) {
   var canvas = Body(layout);
   if (layout == PadKind.Nintendo) {
    canvas.Children.Add(Stroke(7, 12, 17, 12));
   }
   else if (layout == PadKind.PlayStation) {
    canvas.Children.Add(Stroke(16, 12, 16, 4.5));
    canvas.Children.Add(new Polyline { Points = new PointCollection { new Point(12.5, 8), new Point(16, 4.5), new Point(19.5, 8) }, Stroke = Ink, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
    canvas.Children.Add(Stroke(11, 15, 21, 15));
   }
   else {
    foreach (double offset in new[] { 6.0, 10 }) {
     var square = new Rectangle { Width = 8, Height = 8, RadiusX = 1.5, RadiusY = 1.5, Stroke = Ink, StrokeThickness = 1.6 };
     Canvas.SetLeft(square, offset);
     Canvas.SetTop(square, offset);
     canvas.Children.Add(square);
    }
   }
   return canvas;
  }
 }
}
