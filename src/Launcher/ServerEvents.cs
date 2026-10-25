using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // Reads the join, leave and refusal lines the Milk Bar server prints. Names are free text, so the fixed phrases are the anchors.
 static class ServerEvents {
  public const int WrongPassword = 3;

  const string Start = @"^\s*(?:\[\d{1,2}:\d{2}:\d{2}\]\s*)?Player (?<name>.+?)";
  static readonly Regex JoinedLine = new Regex(Start + @" joined the server\. Assigned to player (?<slot>\d+)\.?\s*$", RegexOptions.CultureInvariant);
  static readonly Regex LeftLine = new Regex(Start + @" disconnected\.?(?:\s+-\s+Details:.*)?\s*$", RegexOptions.CultureInvariant);
  static readonly Regex RefusedLine = new Regex(Start + @" tried to connect but failed with error (?<code>\d+)\.?\s*$", RegexOptions.CultureInvariant);

  static bool Match(Regex pattern, string line, out string name, out Match found) {
   name = null; found = null;
   if (string.IsNullOrEmpty(line)) return false;
   found = pattern.Match(line);
   if (!found.Success) return false;
   name = found.Groups["name"].Value.Trim();
   return name.Length > 0;
  }

  public static bool TryJoined(string line, out string name, out int slot) {
   Match found; slot = 0;
   return Match(JoinedLine, line, out name, out found) && int.TryParse(found.Groups["slot"].Value, out slot);
  }

  public static bool TryLeft(string line, out string name) {
   Match found;
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

  public List<string> Names() {
   var sorted = new List<Entry>(entries);
   sorted.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Order.CompareTo(b.Order));
   return sorted.ConvertAll(e => e.Name);
  }

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
