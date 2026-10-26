using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // Reads the join, leave and refusal lines the Milk Bar server prints. Names are free text, so the fixed phrases are the anchors.
 static class ServerEvents {
  public const int WrongPassword = 3;

  const string Start = @"^\s*(?:\[\d{1,2}:\d{2}:\d{2}\]\s*)?Player (?<name>.+?)";
  static readonly Regex JoinedLine = new Regex(Start + @" joined the server\. Assigned to player (?<slot>\d+)\.?\s*$", RegexOptions.CultureInvariant);
  // A greedy name so a player called "X disconnected. - Details: y" cannot cut the line short.
  static readonly Regex LeftLine = new Regex(Start.Replace(".+?", ".+") + @" disconnected\.?(?:\s+-\s+Details:.*)?\s*$", RegexOptions.CultureInvariant);
  static readonly Regex RefusedLine = new Regex(Start + @" tried to connect but failed with error (?<code>\d+)\.?\s*$", RegexOptions.CultureInvariant);
  static readonly Regex Unprintable = new Regex(@"[\p{Cc}\p{Cf}]", RegexOptions.CultureInvariant);

  static bool Match(Regex pattern, string line, out string name, out Match found) {
   name = null; found = null;
   if (string.IsNullOrEmpty(line)) return false;
   found = pattern.Match(line);
   if (!found.Success) return false;
   name = found.Groups["name"].Value.Trim();
   // Control and bidi characters are never in a real name; they only forge or garble lines.
   return name.Length > 0 && !Unprintable.IsMatch(name);
  }

  public static bool TryJoined(string line, out string name, out int slot) {
   Match found; slot = 0;
   return Match(JoinedLine, line, out name, out found) && int.TryParse(found.Groups["slot"].Value, out slot);
  }

  public static bool TryLeft(string line, out string name) {
   Match found; int number;
   // A refusal or join whose player name imitates a leave is still that refusal or join.
   if (TryRefused(line, out name, out number) || TryJoined(line, out name, out number)) { name = null; return false; }
   return Match(LeftLine, line, out name, out found);
  }

  public static bool TryRefused(string line, out string name, out int code) {
   Match found; code = 0;
   return Match(RefusedLine, line, out name, out found) && int.TryParse(found.Groups["code"].Value, out code);
  }
 }

 // Who is connected to the launcher's own server.
 sealed class Roster {
  sealed class Entry { public string Name; public int Slot; public int Order; }

  readonly List<Entry> entries = new List<Entry>();
  int arrivals;

  public int Count { get { return entries.Count; } }

  public void Joined(string name, int slot) {
   entries.RemoveAll(e => e.Slot == slot);
   entries.Add(new Entry { Name = name, Slot = slot, Order = arrivals++ });
  }

  public void Left(string name) {
   int index = entries.FindIndex(e => e.Name == name);
   if (index >= 0) entries.RemoveAt(index);
  }

  public void Clear() { entries.Clear(); }

  // One colour per server slot, so a player keeps theirs while others come and go. The SteamOS launcher uses the same list.
  public static readonly string[] Palette = { "#7EE9C9", "#E5C384", "#8DB8F5", "#F08FA8", "#B9A0F2", "#F2A66B", "#9BDD7A", "#6FD3E8" };

  public static string ColorOf(int slot) { return Palette[((slot - 1) % Palette.Length + Palette.Length) % Palette.Length]; }

  // (slot, name) in slot order.
  public List<KeyValuePair<int, string>> Players() {
   var sorted = new List<Entry>(entries);
   sorted.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Order.CompareTo(b.Order));
   return sorted.ConvertAll(e => new KeyValuePair<int, string>(e.Slot, e.Name));
  }

  public List<string> Names() { return Players().ConvertAll(p => p.Value); }

  public string Text() { return string.Join(", ", Names().ToArray()); }

  string Signature() {
   var sorted = new List<Entry>(entries);
   sorted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
   var parts = new List<string>();
   foreach (Entry e in sorted) parts.Add(e.Slot + "=" + e.Name);
   return string.Join("\n", parts.ToArray());
  }

  // True only when the roster is different afterwards.
  public bool Apply(string line) {
   string name; int slot;
   string before = Signature();
   if (ServerEvents.TryJoined(line, out name, out slot)) Joined(name, slot);
   else if (ServerEvents.TryLeft(line, out name)) Left(name);
   return Signature() != before;
  }
 }
}
