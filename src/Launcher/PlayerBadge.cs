using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HyruleOnline {
 // The "P1" / "P2" mark of the screen preview: the player's avatar and label, turned with that player's screen and always scaled to fit its cell.
 static class PlayerBadge {
  public static FrameworkElement Build(ImageSource avatar, string label, Brush color, int rotation) {
   var text = new TextBlock { Text = label, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
   var frame = new Border {
    Width = 44, Height = 44, CornerRadius = new CornerRadius(8), BorderBrush = color, BorderThickness = new Thickness(2), ClipToBounds = true,
    Child = new Image { Source = avatar, Stretch = Stretch.UniformToFill }
   };
   var slot = new Border { Margin = new Thickness(8, 0, 0, 0), Child = frame };
   var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
   content.Children.Add(text);
   content.Children.Add(slot);
   // A sideways screen keeps the text on the left and the picture on the right, each turned on its own, so the pair stays side by side.
   // Otherwise the whole pair turns together. LayoutTransform makes each box report its turned size, so the Viewbox can shrink it to fit the cell.
   if (rotation == 90 || rotation == 270) {
    text.LayoutTransform = new RotateTransform(rotation);
    frame.LayoutTransform = new RotateTransform(rotation);
   } else {
    content.LayoutTransform = new RotateTransform(rotation);
   }
   return new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = content };
  }
 }
}
