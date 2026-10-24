using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HyruleOnline {
 // Every controller steers a coloured cursor over the launcher: the pad routed to a player gets that player's colour on the Play page,
 // every other pad (and every pad on the other pages) shares a white one. A selects, B goes back, LB and RB still change page.
 sealed class PadCursor {
  public int Owner;
  public bool Active;
  public FrameworkElement Target;
  public Border Frame, Badge;
  public int Picker = -1;
  public ComboBox Combo;
  public int ComboIndex;
  public readonly Dictionary<int, FrameworkElement> Memory = new Dictionary<int, FrameworkElement>();
 }

 sealed partial class Launcher {
  static readonly string[] CursorColors = { "#7EE9C9", "#E5C384", "#F2F6F6" };
  static readonly string[] CursorBadges = { "P1", "P2", "" };
  readonly PadCursor[] cursors = new PadCursor[3];
  readonly PadNav[] slotNavs = { new PadNav(), new PadNav(), new PadNav(), new PadNav() };
  readonly PadNav[] scriptNavs = { new PadNav(), new PadNav(), new PadNav() };
  List<string[]> padScript;
  int padScriptStep;
  long padScriptNext;
  readonly bool[] scriptOwners = new bool[3];

  // HYRULE_PAD_SCRIPT=file drives the cursors without a controller, for layout checks. Lines: "p1|p2|any up|down|left|right|accept|back", "wait milliseconds", "page index".
  void LoadPadScript() {
   string file = Environment.GetEnvironmentVariable("HYRULE_PAD_SCRIPT");
   if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
   padScript = File.ReadAllLines(file).Select(l => l.Trim().ToLowerInvariant().Split(' ')).Where(p => p.Length >= 2 && p[0].Length > 0 && p[0][0] != '#').ToList();
   foreach (var step in padScript) { int owner = Array.IndexOf(new[] { "p1", "p2", "any" }, step[0]); if (owner >= 0) scriptOwners[owner] = true; }
  }

  void BuildCursors() {
   var layer = C<Canvas>("CursorLayer");
   for (int i = 0; i < 3; i++) {
    var color = (Color)ColorConverter.ConvertFromString(CursorColors[i]);
    var frame = new Border { BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    frame.Effect = new DropShadowEffect { Color = color, BlurRadius = 14, ShadowDepth = 0, Opacity = .9 };
    // The default rate follows the display refresh and re-blurs the glow every frame; 20 fps is smooth for a slow pulse and far cheaper.
    var pulse = new DoubleAnimation(.6, 1, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
    Timeline.SetDesiredFrameRate(pulse, 20);
    frame.BeginAnimation(UIElement.OpacityProperty, pulse);
    var badge = new Border { Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    badge.Child = new TextBlock { Text = CursorBadges[i], FontSize = 12, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Normal, Foreground = Paint("#0B141B"), Padding = new Thickness(0) };
    layer.Children.Add(frame);
    layer.Children.Add(badge);
    cursors[i] = new PadCursor { Owner = i, Frame = frame, Badge = badge };
   }
  }

  void ResetCursors() {
   foreach (var cursor in cursors) {
    if (cursor == null) continue;
    if (cursor.Combo != null) cursor.Combo.IsDropDownOpen = false;
    cursor.Combo = null; cursor.Target = null; cursor.Picker = -1;
   }
  }

  int OwnerOf(int slot, RouterSnapshot snapshot) {
   if (page != 0 || setupActive) return 2;
   return snapshot.Routes[0] == slot ? 0 : playerCount == 2 && snapshot.Routes[1] == slot ? 1 : 2;
  }

  // Reads every pad once per tick, then lets each cursor act on what its pads did.
  void DrivePads(RouterSnapshot snapshot) {
   if (cursors[0] == null) return;
   long now = Environment.TickCount;
   bool listening = (Window.IsActive || padScript != null) && snapshot.Assigning < 0 && !capturing;
   var active = new bool[3];
   var presses = new List<KeyValuePair<int, PadPress>>();
   for (int slot = 0; slot < 4; slot++) {
    if (snapshot.Names[slot] == null) { slotNavs[slot] = new PadNav(); continue; }
    int owner = OwnerOf(slot, snapshot);
    active[owner] = true;
    var state = snapshot.Pads[slot];
    slotNavs[slot].Layout = PadKind.Resolve(Router.MenuLayout, snapshot.Kinds[slot]);
    var press = slotNavs[slot].Update(state.buttons, state.lx, state.ly, now);
    if (snapshot.Assigning >= 0 && press.Back) router.CancelAssign();
    if (listening) presses.Add(new KeyValuePair<int, PadPress>(owner, press));
   }
   if (padScript != null) {
    for (int i = 0; i < 3; i++) if (scriptOwners[i]) active[page == 0 ? i : 2] = true;
    if (padScriptNext == 0) padScriptNext = now + 2500;
    while (padScriptStep < padScript.Count && now >= padScriptNext) {
     var step = padScript[padScriptStep++];
     padScriptNext = now + 450;
     int wait, index;
     if (step[0] == "wait" && int.TryParse(step[1], out wait)) { padScriptNext = now + wait; break; }
     if (step[0] == "page" && int.TryParse(step[1], out index)) { ShowPage(index); break; }
     int owner = Array.IndexOf(new[] { "p1", "p2", "any" }, step[0]);
     if (owner < 0) continue;
     var press = new PadPress { Direction = Array.IndexOf(new[] { "up", "down", "left", "right" }, step[1]), Accept = step[1] == "accept", Back = step[1] == "back" };
     presses.Add(new KeyValuePair<int, PadPress>(page == 0 ? owner : 2, press));
     break;
    }
   }
   foreach (var cursor in cursors) cursor.Active = active[cursor.Owner];
   foreach (var cursor in cursors) if (cursor.Active) Sync(cursor);
   foreach (var entry in presses) {
    var cursor = cursors[entry.Key];
    if (!cursor.Active) continue;
    if (entry.Value.Direction >= 0) MoveCursor(cursor, entry.Value.Direction);
    if (entry.Value.Accept) { AcceptCursor(cursor); var shown = cursor.Target; Window.Dispatcher.BeginInvoke(new Action(() => ShowHint(shown)), DispatcherPriority.Background); }
    if (entry.Value.Back) BackCursor(cursor);
   }
   DrawCursors();
  }

  // Keeps each cursor on something that exists: it follows a picker that opened or closed and re-seats when its target is gone.
  void Sync(PadCursor cursor) {
   if (cursor.Combo != null && !cursor.Combo.IsDropDownOpen) cursor.Combo = null;
   if (cursor.Picker >= 0 && !pickerOpen[cursor.Picker]) {
    int player = cursor.Picker;
    cursor.Picker = -1;
    cursor.Target = C<Button>("P" + (player + 1) + "AvatarButton");
   }
   for (int player = 0; player < 2; player++) {
    if (!pickerOpen[player] || cursors.Any(c => c.Active && c.Picker == player)) continue;
    var taker = new[] { cursors[player], cursors[2], cursors[1 - player] }.FirstOrDefault(c => c.Active && c.Picker < 0);
    if (taker != null) { taker.Picker = player; taker.Target = avatarTiles[player].FirstOrDefault(t => t.Tag == AvatarOf(player)); }
   }
   if (cursor.Target == null || !Reachable(cursor, cursor.Target)) Seat(cursor);
  }

  bool Reachable(PadCursor cursor, FrameworkElement element) {
   return Candidates(cursor).Contains(element);
  }

  // Where a cursor lands on a page: the spot it left last time, else the first option of the page (never Help or the footer buttons).
  void Seat(PadCursor cursor) {
   var all = Candidates(cursor);
   FrameworkElement start = null;
   if (cursor.Picker >= 0) start = avatarTiles[cursor.Picker].FirstOrDefault(t => t.Tag == AvatarOf(cursor.Picker));
   else if (HelpOpen) start = C<Button>("Help");
   else if (cursor.Memory.TryGetValue(page, out start) && !all.Contains(start)) start = null;
   if (start == null && cursor.Picker < 0 && !HelpOpen) {
    if (page == 0 && !setupActive) start = C<Button>(cursor.Owner == 0 ? "P1AvatarButton" : cursor.Owner == 1 ? "P2AvatarButton" : "Start");
    if (start == null || !all.Contains(start)) {
     var chrome = new[] { "Help", "Start", "Stop", "Exit" };
     var content = all.Where(e => !chrome.Contains(e.Name)).ToList();
     start = (content.Count > 0 ? content : all).OrderBy(e => Math.Round(Bounds(e).Top / 12)).ThenBy(e => Bounds(e).Left).FirstOrDefault();
    }
   }
   SetTarget(cursor, start);
  }

  bool HelpOpen { get { return C<Border>("HelpPanel").Visibility == Visibility.Visible; } }

  void SetTarget(PadCursor cursor, FrameworkElement element) {
   cursor.Target = element;
   if (element == null) return;
   if (cursor.Picker < 0 && !HelpOpen) cursor.Memory[page] = element;
   var tile = element.Tag as Character;
   if (tile != null && cursor.Picker >= 0) ShowPickerName(cursor.Picker, tile);
   ShowHint(element);
  }

  void ShowHint(FrameworkElement element) {
   string bar = page == 1 ? "GraphicsHint" : page == 2 ? "MultiplayerHint" : null;
   if (bar != null && element != null) C<TextBlock>(bar).Text = element.ToolTip as string ?? Tr("Hover an option, or move the focus to it, to see what it does.");
  }

  Rect Bounds(FrameworkElement element) {
   try { return element.TransformToVisual(C<Grid>("Shell")).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
   catch (Exception) { return Rect.Empty; }
  }

  void Collect(DependencyObject node, List<FrameworkElement> found) {
   for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) {
    var child = VisualTreeHelper.GetChild(node, i);
    var element = child as FrameworkElement;
    if (element != null && !element.IsVisible) continue;
    if (element is Button || element is CheckBox || element is ComboBox || element is TextBox && !((TextBox)element).IsReadOnly) {
     if (element.IsEnabled && element.ActualWidth > 0) found.Add(element);
     continue;
    }
    Collect(child, found);
   }
  }

  // What this cursor may move to: the open mosaic if it is choosing, otherwise the controls of the page and the footer, minus other players' cards.
  List<FrameworkElement> Candidates(PadCursor cursor) {
   if (cursor.Picker >= 0) return avatarTiles[cursor.Picker].Cast<FrameworkElement>().Where(t => t.IsVisible && t.IsEnabled).ToList();
   if (HelpOpen) return new List<FrameworkElement> { C<Button>("Help") };
   var found = new List<FrameworkElement>();
   Collect(C<FrameworkElement>(setupActive ? "SetupPage" : PageNames[page] + "Page"), found);
   foreach (var name in new[] { "Help", "Start", "Stop", "Exit" }) {
    var button = C<Button>(name);
    if (button.IsVisible && button.IsEnabled) found.Add(button);
   }
   for (int player = 0; player < 2; player++) {
    var card = C<Border>("P" + (player + 1) + "Card");
    var mosaic = C<FrameworkElement>("P" + (player + 1) + "Picker");
    bool taken = player != cursor.Owner && cursors[player].Active && page == 0;
    found.RemoveAll(e => e.IsDescendantOf(mosaic) || taken && e.IsDescendantOf(card));
   }
   return found;
  }

  static readonly string[] ChromeNames = { "Help", "Start", "Stop", "Exit" };

  void MoveCursor(PadCursor cursor, int direction) {
   if (cursor.Combo != null) {
    if (direction > 1) return;
    cursor.ComboIndex = Math.Max(0, Math.Min(cursor.Combo.Items.Count - 1, cursor.ComboIndex + (direction == 0 ? -1 : 1)));
    FocusComboItem(cursor.Combo, cursor.ComboIndex);
    return;
   }
   if (cursor.Target is TextBox && cursor.Target.IsKeyboardFocused) Window.Focus();
   var all = Candidates(cursor);
   var from = cursor.Target == null ? Rect.Empty : Bounds(cursor.Target);
   if (all.Count == 0 || from.IsEmpty) return;
   var boxes = all.Select(e => { var b = Bounds(e); return new Box(b.X, b.Y, b.Width, b.Height); }).ToList();
   int pick = Spatial.Pick(new Box(from.X, from.Y, from.Width, from.Height), boxes, direction);
   // Nothing in the same row: a content control may still cross to the other column (player 1's cards sit higher than the display options). The footer and Help never do.
   if (pick < 0 && direction >= 2 && cursor.Target != null && !ChromeNames.Contains(cursor.Target.Name)) {
    var sideways = all.Where(e => !ChromeNames.Contains(e.Name)).ToList();
    int side = Spatial.Pick(new Box(from.X, from.Y, from.Width, from.Height), sideways.Select(e => { var b = Bounds(e); return new Box(b.X, b.Y, b.Width, b.Height); }).ToList(), direction, true);
    if (side >= 0) pick = all.IndexOf(sideways[side]);
   }
   if (pick >= 0) SetTarget(cursor, all[pick]);
   else if (page == 4 && direction < 2) { if (direction == 0) C<TextBox>("Log").LineUp(); else C<TextBox>("Log").LineDown(); }
  }

  void FocusComboItem(ComboBox combo, int index) {
   var item = combo.ItemContainerGenerator.ContainerFromIndex(index) as ComboBoxItem;
   if (item == null) return;
   item.Focus();
   item.BringIntoView();
  }

  void AcceptCursor(PadCursor cursor) {
   var target = cursor.Target;
   if (target == null) return;
   if (cursor.Combo != null) {
    var open = cursor.Combo;
    open.SelectedIndex = cursor.ComboIndex;
    open.IsDropDownOpen = false;
    cursor.Combo = null;
    return;
   }
   var character = target.Tag as Character;
   if (character != null && cursor.Picker >= 0) {
    int player = cursor.Picker;
    cursor.Picker = -1;
    ChooseAvatar(player, character);
    cursor.Target = C<Button>("P" + (player + 1) + "AvatarButton");
    return;
   }
   var combo = target as ComboBox;
   var check = target as CheckBox;
   var text = target as TextBox;
   if (combo != null) {
    cursor.Combo = combo;
    cursor.ComboIndex = Math.Max(0, combo.SelectedIndex);
    combo.IsDropDownOpen = true;
    combo.Dispatcher.BeginInvoke(new Action(() => FocusComboItem(combo, cursor.ComboIndex)), DispatcherPriority.Loaded);
   }
   else if (check != null) check.IsChecked = check.IsChecked != true;
   else if (text != null) { text.Focus(); text.SelectAll(); }
   else if (target is Button) {
    int player = target.Name == "P1AvatarButton" ? 0 : target.Name == "P2AvatarButton" ? 1 : -1;
    target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if (player >= 0 && pickerOpen[player] && cursor.Picker < 0) {
     cursor.Picker = player;
     cursor.Target = avatarTiles[player].FirstOrDefault(t => t.Tag == AvatarOf(player));
    }
   }
  }

  void BackCursor(PadCursor cursor) {
   if (cursor.Combo != null) { cursor.Combo.IsDropDownOpen = false; cursor.Combo = null; return; }
   if (cursor.Picker < 0) {
    if (HelpOpen) C<Border>("HelpPanel").Visibility = Visibility.Collapsed;
    else if (gameRunning && !setupActive) ReturnToGame(cursor.Owner == 1 ? 1 : 0);
    return;
   }
   int player = cursor.Picker;
   cursor.Picker = -1;
   SetPicker(player, false);
   cursor.Target = C<Button>("P" + (player + 1) + "AvatarButton");
  }

  void DrawCursors() {
   var taken = new Dictionary<FrameworkElement, int>();
   foreach (var cursor in cursors) {
    var target = cursor.Active ? cursor.Target : null;
    var area = target == null || !target.IsVisible ? Rect.Empty : Bounds(target);
    if (area.IsEmpty) { cursor.Frame.Visibility = cursor.Badge.Visibility = Visibility.Collapsed; continue; }
    int layer;
    taken.TryGetValue(target, out layer);
    taken[target] = layer + 1;
    double grow = 3 + layer * 5;
    cursor.Frame.Width = area.Width + grow * 2; cursor.Frame.Height = area.Height + grow * 2;
    Canvas.SetLeft(cursor.Frame, area.Left - grow); Canvas.SetTop(cursor.Frame, area.Top - grow);
    cursor.Frame.Visibility = Visibility.Visible;
    bool named = CursorBadges[cursor.Owner].Length > 0;
    cursor.Badge.Visibility = named ? Visibility.Visible : Visibility.Collapsed;
    cursor.Badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    Canvas.SetLeft(cursor.Badge, area.Right + grow - cursor.Badge.DesiredSize.Width); Canvas.SetTop(cursor.Badge, area.Top - grow - 17);
   }
  }
 }
}
