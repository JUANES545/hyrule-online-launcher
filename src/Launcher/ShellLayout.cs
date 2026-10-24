using System.Windows;
using System.Windows.Controls;

namespace HyruleOnline {
 // Applies a ShellFit to the loaded launcher window. The tests load the same XAML and call this too, so they measure the real layout.
 static class ShellLayout {
  public static void Apply(FrameworkElement root, ShellFit fit) {
   var shell = (Grid)root.FindName("Shell");
   shell.Width = fit.Width; shell.Height = fit.Height;
   Place(root, "DisplayPanel", fit.Stacked);
   ((Grid)root.FindName("PlayBody")).ColumnDefinitions[1].Width = fit.Stacked ? new GridLength(0) : new GridLength(0.9, GridUnitType.Star);

   var multiplayer = (Grid)root.FindName("MultiplayerBody");
   multiplayer.ColumnDefinitions[1].Width = fit.Stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
   Place(root, "LivePanel", fit.Stacked);
   ((FrameworkElement)root.FindName("RulesPanel")).Margin = fit.Stacked ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 14, 0);
   ((FrameworkElement)root.FindName("LivePanel")).Margin = fit.Stacked ? new Thickness(0) : new Thickness(14, 0, 0, 0);

   var graphics = (Grid)root.FindName("GraphicsBody");
   graphics.ColumnDefinitions[1].Width = fit.Stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
   Place(root, "GraphicsRight", fit.Stacked);
   ((FrameworkElement)root.FindName("GraphicsLeft")).Margin = fit.Stacked ? new Thickness(0, 0, 0, 14) : new Thickness(0, 0, 10, 0);
   ((FrameworkElement)root.FindName("GraphicsRight")).Margin = fit.Stacked ? new Thickness(0) : new Thickness(10, 0, 0, 0);
  }

  // The second column moves under the first on a stacked (tall) canvas.
  static void Place(FrameworkElement root, string name, bool stacked) {
   var element = (UIElement)root.FindName(name);
   Grid.SetColumn(element, stacked ? 0 : 1);
   Grid.SetRow(element, stacked ? 1 : 0);
  }
 }
}
