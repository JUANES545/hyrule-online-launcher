using System;

namespace HyruleOnline {
 // The launcher is drawn on a design canvas that a Viewbox scales to the window, so nothing ever scrolls.
 // The canvas follows the window shape: wide windows get a wider canvas (up to a limit, then the content is centred),
 // narrow ones get a taller canvas and stack the two columns.
 sealed class ShellFit {
  public const double WideHeight = 1030, MinWideWidth = 1280, MaxWideWidth = 1760;
  public const double StackWidth = 760, StackHeight = 1700;
  public const double StackBelow = 1.1;

  public double Width, Height;
  public bool Stacked;

  public static ShellFit For(double windowWidth, double windowHeight) {
   double aspect = Math.Max(1, windowWidth) / Math.Max(1, windowHeight);
   var fit = new ShellFit();
   if (aspect < StackBelow) {
    fit.Stacked = true;
    fit.Width = StackWidth;
    fit.Height = Math.Max(StackHeight, StackWidth / aspect);
    return fit;
   }
   double natural = aspect * WideHeight;
   fit.Width = Math.Min(MaxWideWidth, Math.Max(MinWideWidth, natural));
   fit.Height = natural < MinWideWidth ? MinWideWidth / aspect : WideHeight;
   return fit;
  }

  public double Scale(double windowWidth, double windowHeight) { return Math.Min(windowWidth / Width, windowHeight / Height); }
 }
}
