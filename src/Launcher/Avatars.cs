using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HyruleOnline {
 // The avatar slot on each player card and the mosaic of busts that replaces the card content while a player is choosing.
 sealed partial class Launcher {
  public static readonly string[] PlayerColors = { "#7EE9C9", "#E5C384" };
  Dictionary<string, byte[]> bustData = new Dictionary<string, byte[]>();
  readonly Dictionary<string, ImageSource> bustImages = new Dictionary<string, ImageSource>();
  readonly bool[] pickerOpen = new bool[2];
  readonly List<Button>[] avatarTiles = { new List<Button>(), new List<Button>() };
  bool stackedLayout;

  ImageSource Avatar(Character character) {
   string key = Characters.AvatarKey(character);
   ImageSource image;
   if (bustImages.TryGetValue(key, out image)) return image;
   byte[] data;
   try {
    if (bustData.TryGetValue(key, out data)) {
     var bitmap = new BitmapImage();
     bitmap.BeginInit();
     bitmap.CacheOption = BitmapCacheOption.OnLoad;
     bitmap.DecodePixelWidth = 160;
     bitmap.StreamSource = new MemoryStream(data);
     bitmap.EndInit();
     bitmap.Freeze();
     image = bitmap;
    }
   }
   catch (Exception) { image = null; }
   if (image == null) image = Initials(character.Label);
   bustImages[key] = image;
   return image;
  }

  // Stands in for a missing picture: the first letters on a plain tile.
  static ImageSource Initials(string label) {
   string text = new string((label ?? "?").Split(' ').Where(w => w.Length > 0 && char.IsLetter(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])).ToArray());
   if (text.Length == 0) text = "?";
   var visual = new DrawingVisual();
   using (var context = visual.RenderOpen()) {
    context.DrawEllipse(Paint("#304752"), null, new Point(48, 48), 44, 44);
    var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 34, Brushes.White);
    context.DrawText(formatted, new Point(48 - formatted.Width / 2, 48 - formatted.Height / 2));
   }
   var bitmap = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
   bitmap.Render(visual);
   bitmap.Freeze();
   return bitmap;
  }

  void BuildPicker(int player) {
   var grid = C<UniformGrid>("P" + (player + 1) + "AvatarGrid");
   var style = (Style)Window.Resources["AvatarTile"];
   grid.Children.Clear();
   avatarTiles[player].Clear();
   foreach (var character in characters) {
    var chosen = character;
    var picture = new Image { Source = Avatar(chosen), Margin = new Thickness(3, 3, 3, 17), Stretch = Stretch.Uniform, IsHitTestVisible = false };
    RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
    var caption = new TextBlock { Text = chosen.Label, FontSize = 11, FontStyle = FontStyles.Normal, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Padding = new Thickness(3, 1, 3, 1) };
    var strip = new Border { Background = Paint("#E0081014"), CornerRadius = new CornerRadius(0, 0, 4, 4), VerticalAlignment = VerticalAlignment.Bottom, Child = caption };
    var content = new Grid();
    content.Children.Add(picture);
    content.Children.Add(strip);
    var tile = new Button { Style = style, Content = content, Tag = chosen };
    System.Windows.Automation.AutomationProperties.SetName(tile, chosen.Label);
    tile.Click += (s, e) => ChooseAvatar(player, chosen);
    tile.MouseEnter += (s, e) => ShowPickerName(player, chosen);
    tile.GotKeyboardFocus += (s, e) => ShowPickerName(player, chosen);
    avatarTiles[player].Add(tile);
    grid.Children.Add(tile);
   }
   C<Button>("P" + (player + 1) + "AvatarButton").Click += (s, e) => SetPicker(player, true);
   RefreshAvatar(player);
  }

  void ShowPickerName(int player, Character character) {
   C<TextBlock>("P" + (player + 1) + "PickerName").Text = character.Label;
  }

  Character AvatarOf(int player) { return Characters.Find(characters, models[player]); }

  // Shows the chosen avatar on the slot and marks it in the mosaic with the player's colour.
  void RefreshAvatar(int player) {
   var current = AvatarOf(player);
   C<Image>("P" + (player + 1) + "AvatarImage").Source = Avatar(current);
   C<TextBlock>("P" + (player + 1) + "AvatarName").Text = current.Label;
   ShowPickerName(player, current);
   foreach (var tile in avatarTiles[player]) tile.BorderBrush = Paint(tile.Tag == current ? PlayerColors[player] : "#364E5659");
  }

  void ChooseAvatar(int player, Character character) {
   models[player] = character.Model;
   SavePrefs();
   RefreshAvatar(player);
   SetPicker(player, false);
   C<Button>("P" + (player + 1) + "AvatarButton").Focus();
  }

  void SetPicker(int player, bool open) {
   if (open && !C<Button>("P" + (player + 1) + "AvatarButton").IsEnabled) return;
   pickerOpen[player] = open;
   C<FrameworkElement>("P" + (player + 1) + "Main").Visibility = open ? Visibility.Collapsed : Visibility.Visible;
   C<FrameworkElement>("P" + (player + 1) + "Picker").Visibility = open ? Visibility.Visible : Visibility.Collapsed;
   if (open) {
    RefreshAvatar(player);
    var current = avatarTiles[player].FirstOrDefault(t => t.Tag == AvatarOf(player));
    if (current != null) current.Focus();
   }
   UpdatePlayLayout();
  }

  bool ClosePickers() {
   bool any = pickerOpen[0] || pickerOpen[1];
   SetPicker(0, false); SetPicker(1, false);
   return any;
  }

  // While anyone is choosing, the display column gives way and the two cards sit side by side so both mosaics fit at once.
  void UpdatePlayLayout() {
   bool choosing = pickerOpen[0] || pickerOpen[1], side = choosing && !stackedLayout;
   var first = C<Border>("P1Card"); var second = C<Border>("P2Card");
   Grid.SetRow(second, side ? 0 : 1); Grid.SetColumn(second, side ? 1 : 0);
   Grid.SetColumnSpan(first, side ? 1 : 2); Grid.SetColumnSpan(second, side ? 1 : 2);
   first.Margin = side ? new Thickness(0, 0, 6, 8) : new Thickness(0, 0, 0, 8);
   second.Margin = side ? new Thickness(6, 0, 0, 8) : new Thickness(0, 0, 0, 8);
   Grid.SetColumnSpan(C<StackPanel>("Players"), side ? 2 : 1);
   C<StackPanel>("DisplayPanel").Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
   C<StackPanel>("Players").Margin = side ? new Thickness(0) : stackedLayout ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 28, 0);
  }
 }
}
