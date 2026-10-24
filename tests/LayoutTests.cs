using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace HyruleOnline {
 // Loads the real Launcher.xaml, applies the layout for many window shapes and measures it: pages that must not scroll, text that is cut off, panels that overlap.
 // Setting HYRULE_LAYOUT_DUMP=1 prints every page's extent for every shape, which replaces screenshots when tuning the layout.
 static class LayoutTests {
  static int failures;
  static bool dump = Environment.GetEnvironmentVariable("HYRULE_LAYOUT_DUMP") == "1";

  static void Check(string name, bool ok, string detail) {
   Console.WriteLine((ok ? "ok:   " : "FAIL: ") + name + (ok ? "" : " (" + detail + ")"));
   if (!ok) failures++;
  }

  static readonly object[][] Shapes = {
   new object[] { "1280x720", 1280, 720 }, new object[] { "1366x768", 1366, 768 }, new object[] { "1600x900", 1600, 900 }, new object[] { "1920x1080", 1920, 1080 },
   new object[] { "2560x1440", 2560, 1440 }, new object[] { "3440x1440 ultrawide", 3440, 1440 }, new object[] { "3840x2160", 3840, 2160 }, new object[] { "5120x1440 super ultrawide", 5120, 1440 },
   new object[] { "1024x768 4:3", 1024, 768 }, new object[] { "1280x1024 5:4", 1280, 1024 }, new object[] { "1100x1000 near square", 1100, 1000 },
   new object[] { "1080x1920 portrait", 1080, 1920 }, new object[] { "800x1280 narrow portrait", 800, 1280 }
  };
  static readonly string[] Pages = { "Play", "Graphics", "Multiplayer", "Language", "Details" };

  static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject {
   foreach (object child in LogicalTreeHelper.GetChildren(node)) {
    var item = child as DependencyObject;
    if (item == null) continue;
    if (item is T) yield return (T)item;
    foreach (var deeper in Descendants<T>(item)) yield return deeper;
   }
  }

  static Window Load(string root) {
   using (var stream = File.OpenRead(Path.Combine(root, "src", "Launcher", "Launcher.xaml"))) return (Window)XamlReader.Load(stream);
  }

  static Rect Bounds(FrameworkElement element, UIElement host) {
   return element.TransformToAncestor(host).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
  }

  static ScrollViewer Scroller(Window window, string page) {
   var body = window.FindName(page + "Body") as FrameworkElement;
   return body == null ? window.FindName(page + "Page") as ScrollViewer : body.Parent as ScrollViewer;
  }

  // Pad navigation uses the same picker the launcher does: from any control, the four directions must lead to every other one, with or without Return to game in the footer.
  static string Label(FrameworkElement element) {
   return string.IsNullOrEmpty(element.Name) ? element.GetType().Name : element.Name;
  }

  static void CheckNavigation(string name, Window window, string page) {
   var shell = (Grid)window.FindName("Shell");
   var items = ShellLayout.Controls(window, page + "Page");
   var boxes = items.Select(e => { var b = Bounds(e, shell); return new Box(b.X, b.Y, b.Width, b.Height); }).ToList();
   var chrome = items.Select(e => ShellLayout.ChromeNames.Contains(e.Name)).ToList();
   var lost = new List<string>();
   for (int start = 0; start < items.Count; start++) {
    var seen = new HashSet<int> { start };
    var queue = new Queue<int>();
    queue.Enqueue(start);
    while (queue.Count > 0) {
     int at = queue.Dequeue();
     for (int direction = 0; direction < 4; direction++) {
      int next = Spatial.Next(boxes[at], boxes, chrome, chrome[at], direction);
      if (next >= 0 && seen.Add(next)) queue.Enqueue(next);
     }
    }
    if (seen.Count < items.Count) lost.Add(Label(items[start]) + " cannot reach " + string.Join("/", Enumerable.Range(0, items.Count).Where(i => !seen.Contains(i)).Select(i => Label(items[i])).ToArray()));
   }
   if (dump) Console.WriteLine("      " + name + ": " + items.Count + " pad targets");
   Check(name + ": every control is reachable with the pad", items.Count > 0 && lost.Count == 0, items.Count == 0 ? "no controls found" : string.Join("; ", lost.Take(3).ToArray()));
   // A control type the pad code does not know about (a slider, a radio list) would be skipped silently.
   var skipped = Descendants<FrameworkElement>((DependencyObject)window.FindName(page + "Page")).Where(e => (e is ButtonBase || e is Selector || e is Slider || e is TextBoxBase && !((TextBoxBase)e).IsReadOnly) && e.IsVisible && e.IsEnabled && e.ActualWidth > 0 && !items.Contains(e)).Select(Label).ToArray();
   Check(name + ": the pad skips no control", skipped.Length == 0, string.Join(", ", skipped));
  }

  static void Run(string root) {
   var window = Load(root);
   window.WindowStyle = WindowStyle.None; window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = window.Top = -20000;
   window.Show();
   var content = (FrameworkElement)window.Content;
   var returnGame = (UIElement)window.FindName("ReturnGame");
   // The launcher puts the two preview cells side by side at run time; without it both sit in one cell and cannot be told apart.
   Grid.SetColumn((UIElement)window.FindName("View2"), 1);
   foreach (object[] shape in Shapes) {
    int width = (int)shape[1], height = (int)shape[2];
    var fit = ShellFit.For(width, height);
    ShellLayout.Apply(window, fit);
    foreach (string page in Pages) {
     foreach (string other in Pages) ((FrameworkElement)window.FindName(other + "Page")).Visibility = other == page ? Visibility.Visible : Visibility.Collapsed;
     content.Measure(new Size(width, height));
     content.Arrange(new Rect(0, 0, width, height));
     content.UpdateLayout();
     string name = shape[0] + " (canvas " + fit.Width + "x" + fit.Height + (fit.Stacked ? ", stacked" : "") + ") " + page + " page";
     foreach (bool running in new[] { false, true }) {
      returnGame.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
      content.UpdateLayout();
      CheckNavigation(name + (running ? " with a game running" : ""), window, page);
     }
     returnGame.Visibility = Visibility.Collapsed;
     content.UpdateLayout();
     var scroll = Scroller(window, page);
     if (scroll != null && dump) Console.WriteLine("      " + name + ": content " + Math.Round(scroll.ExtentHeight) + " in " + Math.Round(scroll.ViewportHeight));
     if (page == "Graphics") {
      Check(name + ": fits without scrolling", scroll.ExtentHeight <= scroll.ViewportHeight + 0.5, scroll.ExtentHeight + " > " + scroll.ViewportHeight);
      var toggles = (UniformGrid)window.FindName("AdvancedToggles");
      Check(name + ": the Cemu options use two columns", toggles.Columns == 2 && Bounds((FrameworkElement)toggles.Children[1], content).Left > Bounds((FrameworkElement)toggles.Children[0], content).Left + 50, "columns " + toggles.Columns);
      var left = Bounds((FrameworkElement)window.FindName("GraphicsLeft"), content);
      var right = Bounds((FrameworkElement)window.FindName("GraphicsRight"), content);
      Check(name + ": the two panels do not overlap", !left.IntersectsWith(Rect.Inflate(right, -1, -1)), left + " / " + right);
      var clipped = new List<string>();
      int checkedTexts = 0;
      foreach (var text in Descendants<TextBlock>(window.FindName("GraphicsBody") as DependencyObject)) {
       if (!text.IsVisible || text.ActualWidth <= 0 || text.TextTrimming != TextTrimming.None) continue;
       checkedTexts++;
       bool wraps = text.TextWrapping != TextWrapping.NoWrap;
       text.Measure(wraps ? new Size(text.ActualWidth, double.PositiveInfinity) : new Size(double.PositiveInfinity, double.PositiveInfinity));
       double needed = wraps ? text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom : text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;
       if (wraps ? needed > text.ActualHeight + 1 : needed > text.ActualWidth + 1) clipped.Add(text.Text);
      }
      Check(name + ": no text is cut off", clipped.Count == 0, string.Join(" | ", clipped.ToArray()));
      Check(name + ": the text check saw the page", checkedTexts > 20, "texts " + checkedTexts);
     }
    }
   }
   window.Close();
  }

  [STAThread]
  static int Main(string[] args) {
   Exception error = null;
   var thread = new Thread(() => { try { Run(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory()); } catch (Exception e) { error = e; } });
   thread.SetApartmentState(ApartmentState.STA);
   thread.Start();
   thread.Join();
   if (error != null) { Console.WriteLine("FAIL: " + error); return 1; }
   return failures == 0 ? 0 : 1;
  }
 }
}
