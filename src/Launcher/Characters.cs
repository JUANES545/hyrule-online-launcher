using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HyruleOnline {
 // The player's name and model are Milk Bar launcher user settings. The launcher reads them when it connects,
 // so they are written into each instance's own user.config before a session starts.
 sealed class Character {
  public const string LinkModel = "Jugador1ModelNameLongForASpecificReason";
  public string Name, Model;
  // Position in the Milk Bar model list: Link, Bumii, Environmental, then the NPCs.
  public int Index;
  public bool IsLink { get { return Index == 0; } }
  public string Label;
  public bool Experimental;
  public string Display { get { return Label; } }
 }

 static class Characters {
  public const string ResourceName = "Breath_of_the_Wild_Multiplayer.Resources.NpcData.json";
  public const int MaxName = 16;
  public static readonly string[] DefaultNames = { "Link", "Pedro" };
  // Models seen working as the local player in a real session; every other NPC is untested and flagged Experimental (tracked here, not shown in the UI).
  static readonly string[] Verified = { "Npc_Gerudo_Hero" };
  // Revali hung the loading screen when used as the local player, so it is not offered.
  static readonly string[] Broken = { "Npc_Rito_Hero" };

  public static Character Link { get { return new Character { Name = "Link", Model = Character.LinkModel, Index = 0, Label = "Link" }; } }

  // Link first, then the NPC models the mod can use in place of Link (the same entries its own selector lists).
  public static List<Character> Parse(string json) {
   var list = new List<Character> { Link };
   int index = 3;
   foreach (Match match in Regex.Matches(json ?? "", "\"Name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"Folder\"\\s*:\\s*\"(Npc_[^\"]+)\"")) {
    string name = match.Groups[1].Value, folder = match.Groups[2].Value;
    string label = name;
    if (list.Any(c => c.Name == name)) label = name + " (" + folder.Substring(folder.LastIndexOf('_') + 1) + ")";
    int position = index++;
    if (Broken.Contains(folder)) continue;
    list.Add(new Character { Name = name, Model = folder, Index = position, Label = label, Experimental = !Verified.Contains(folder) });
   }
   return list;
  }

  // The list ships inside the Milk Bar launcher, so it is read from there instead of being copied here.
  public static List<Character> Read(string launcherDll) {
   try {
    if (!File.Exists(launcherDll)) return Parse(null);
    var assembly = Assembly.Load(File.ReadAllBytes(launcherDll));
    using (var stream = assembly.GetManifestResourceStream(ResourceName)) {
     if (stream == null) return Parse(null);
     using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
    }
   }
   catch (Exception) { return Parse(null); }
  }

  // Name of the bust picture the Milk Bar launcher ships for a character: "link", or the folder without "Npc_" in lower case.
  public static string AvatarKey(Character character) {
   if (character.IsLink) return "link";
   string folder = character.Model ?? "";
   if (folder.StartsWith("Npc_", StringComparison.OrdinalIgnoreCase)) folder = folder.Substring(4);
   return folder.ToLowerInvariant();
  }

  // The bust pictures are read from the Milk Bar launcher's own resources at run time; nothing is copied into this project.
  public static Dictionary<string, byte[]> ReadBusts(string launcherDll) {
   var busts = new Dictionary<string, byte[]>();
   try {
    if (!File.Exists(launcherDll)) return busts;
    var assembly = Assembly.Load(File.ReadAllBytes(launcherDll));
    foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))) {
     using (var stream = assembly.GetManifestResourceStream(resource))
     using (var reader = new System.Resources.ResourceReader(stream)) {
      foreach (System.Collections.DictionaryEntry entry in reader) {
       string key = ((string)entry.Key).Replace('\\', '/').ToLowerInvariant();
       var data = entry.Value as Stream;
       if (data == null || !key.StartsWith("images/bust/") || !key.EndsWith(".png")) continue;
       using (var copy = new MemoryStream()) { data.CopyTo(copy); busts[key.Substring(12, key.Length - 16)] = copy.ToArray(); }
      }
     }
    }
   }
   catch (Exception) { }
   return busts;
  }

  public static Character Find(IList<Character> all, string model) {
   return all.FirstOrDefault(c => c.Model == model) ?? all[0];
  }

  // Letters, digits, space, dot, dash and underscore only: the name travels in a ';' separated message and is drawn by the game font.
  public static string CleanName(string text, string fallback) {
   var kept = new StringBuilder();
   foreach (char c in text ?? "") if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '.' || c == '-' || c == '_') kept.Append(c);
   string clean = Regex.Replace(kept.ToString(), " +", " ").Trim();
   if (clean.Length > MaxName) clean = clean.Substring(0, MaxName).Trim();
   return clean.Length == 0 ? fallback : clean;
  }

  public static bool IsNameText(string text) {
   return (text ?? "").All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '.' || c == '-' || c == '_');
  }

  // The JSON the Milk Bar model selector stores in its playerModel setting.
  public static string ModelJson(Character character) {
   Func<string, string> quote = text => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
   return "{\"Name\":\"" + quote(character.Name) + "\",\"Description\":\"\",\"IsArmorSync\":" + (character.IsLink ? "true" : "false")
    + ",\"Model\":\"" + quote(character.Model) + "\",\"ModelIndex\":" + character.Index + ",\"HasCustomAction\":false,\"Selected\":true,\"Type\":" + (character.IsLink ? 0 : 1) + "}";
  }
 }

 static class MilkBarSettings {
  public static string UserRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Milk_Bar"); } }

  // Each launcher folder has its own user.config; the one that points at this player's BCML settings belongs to this player.
  public static string Find(string userRoot, string bcmlSettings) {
   if (!Directory.Exists(userRoot)) return null;
   string wanted = Path.GetFullPath(bcmlSettings);
   foreach (string file in Directory.GetFiles(userRoot, "user.config", SearchOption.AllDirectories)) {
    try {
     var location = Setting(XDocument.Load(file), "bcmlLocation");
     string value = location == null ? "" : location.Value;
     if (value.Length > 0 && string.Equals(Path.GetFullPath(value), wanted, StringComparison.OrdinalIgnoreCase)) return file;
    }
    catch (Exception) { }
   }
   return null;
  }

  static XElement Setting(XDocument document, string name) {
   return document.Descendants("setting").FirstOrDefault(e => (string)e.Attribute("name") == name);
  }

  static void Set(XDocument document, string name, string value) {
   var setting = Setting(document, name);
   if (setting == null) {
    var parent = document.Descendants("userSettings").First().Elements().First();
    setting = new XElement("setting", new XAttribute("name", name), new XAttribute("serializeAs", "String"), new XElement("value"));
    parent.Add(setting);
   }
   var element = setting.Element("value");
   if (element == null) setting.Add(element = new XElement("value"));
   element.Value = value;
  }

  // Link keeps the armor sync of the mod; any NPC replaces Link's model ("play as model").
  public static void Apply(string file, string name, Character character) {
   var document = XDocument.Load(file);
   Set(document, "playerName", name);
   Set(document, "playAsModel", character.IsLink ? "False" : "True");
   Set(document, "playerModel", Characters.ModelJson(character));
   document.Save(file);
  }
 }
}
