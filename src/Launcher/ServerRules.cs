using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // Console commands the launcher sends to the running dedicated server.
 static class ServerCommands {
  // Display text and the server's own weather word.
  public static readonly string[][] Weather = {
   new[] { "Automatic", "auto" }, new[] { "Clear sky", "BlueSky" }, new[] { "Cloudy", "Cloudy" }, new[] { "Rain", "Rain" },
   new[] { "Heavy rain", "HeavyRain" }, new[] { "Snow", "Snow" }, new[] { "Heavy snow", "HeavySnow" },
   new[] { "Thunderstorm", "Thunderstorm" }, new[] { "Thunder and rain", "ThunderRain" }, new[] { "Clear sky with rain", "BlueSkyRain" }
  };

  // "H:MM" or "HH:MM" on a 24 hour clock; null when the text is not a valid time.
  public static string Time(string text) {
   var match = Regex.Match((text ?? "").Trim(), "^(\\d{1,2}):(\\d{2})$");
   if (!match.Success) return null;
   int hour = int.Parse(match.Groups[1].Value), minute = int.Parse(match.Groups[2].Value);
   return hour > 23 || minute > 59 ? null : "Time Set " + hour.ToString("00") + ":" + minute.ToString("00");
  }

  public static string SetWeather(string word) { return "Weather Set " + word; }

  // Moves the player called `from` next to the player called `to`. The server matches players by name.
  public static string Bring(string from, string to) { return "Teleport " + Quote(from) + " " + Quote(to); }

  static string Quote(string name) { return name.IndexOf(' ') >= 0 ? "\"" + name + "\"" : name; }

  public const string ToggleNameTags = "NameTags", PauseSync = "Stop", ResumeSync = "Start";

  // The name tag state the server reports, or null for any other line.
  public static bool? NameTags(string line) {
   // Player names are free text, so a join or leave line never counts.
   if (Regex.IsMatch(line, @"^\s*(?:\[[^\]]*\]\s*)?Player ")) return null;
   if (line.IndexOf("Deactivated Name Tags", StringComparison.OrdinalIgnoreCase) >= 0) return false;
   if (line.IndexOf("Activated Name Tags", StringComparison.OrdinalIgnoreCase) >= 0) return true;
   return null;
  }
 }

 // The sync rules the dedicated server starts with. They live in the [DefaultGamemode] section of ServerConfig.ini.
 sealed class ServerRules {
  public string Name = "Custom";
  public bool Enemies, Quests, Koroks, Towers, Shrines, Locations, DivineBeasts;
  // 0 none, 1 Hunter vs Speedrunner, 2 Death Swap, as documented in the ini.
  public int Special;

  public static readonly string[] SpecialNames = { "No special mode", "Hunter vs Speedrunner", "Death Swap" };

  public ServerRules Copy() { return (ServerRules)MemberwiseClone(); }

  public bool SameRules(ServerRules other) {
   return Enemies == other.Enemies && Quests == other.Quests && Koroks == other.Koroks && Towers == other.Towers
    && Shrines == other.Shrines && Locations == other.Locations && DivineBeasts == other.DivineBeasts && Special == other.Special;
  }

  static readonly string[] Keys = { "EnemySync", "QuestSync", "KorokSync", "TowerSync", "ShrineSync", "LocationSync", "DungeonSync", "Special", "Name" };

  string[] Values() {
   return new[] { B(Enemies), B(Quests), B(Koroks), B(Towers), B(Shrines), B(Locations), B(DivineBeasts), Special.ToString(), Name };
  }

  static string B(bool value) { return value ? "True" : "False"; }

  static bool Flag(string value) { return string.Equals(value == null ? "" : value.Trim(), "true", StringComparison.OrdinalIgnoreCase); }

  // Reads the rules from ini text; missing keys keep their defaults.
  public static ServerRules Parse(string ini) {
   var rules = new ServerRules();
   var values = Section(ini, "DefaultGamemode");
   string v;
   if (values.TryGetValue("Name", out v) && v.Trim().Length > 0) rules.Name = v.Trim();
   if (values.TryGetValue("EnemySync", out v)) rules.Enemies = Flag(v);
   if (values.TryGetValue("QuestSync", out v)) rules.Quests = Flag(v);
   if (values.TryGetValue("KorokSync", out v)) rules.Koroks = Flag(v);
   if (values.TryGetValue("TowerSync", out v)) rules.Towers = Flag(v);
   if (values.TryGetValue("ShrineSync", out v)) rules.Shrines = Flag(v);
   if (values.TryGetValue("LocationSync", out v)) rules.Locations = Flag(v);
   if (values.TryGetValue("DungeonSync", out v)) rules.DivineBeasts = Flag(v);
   int special;
   if (values.TryGetValue("Special", out v) && int.TryParse(v.Trim(), out special) && special >= 0 && special < SpecialNames.Length) rules.Special = special;
   return rules;
  }

  static Dictionary<string, string> Section(string ini, string wanted) {
   var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
   string section = "";
   foreach (string raw in ini.Split('\n')) {
    string line = raw.Trim();
    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
    if (line.StartsWith("#") || line.StartsWith(";") || section != wanted) continue;
    int eq = line.IndexOf('=');
    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1);
   }
   return values;
  }

  // Rewrites only the rule keys, and turns on DefaultGamemode so the server does not wait for a prompt.
  // Connection settings, comments, line endings and the byte order mark stay as they were.
  public string Apply(string ini) {
   string newline = ini.Contains("\r\n") ? "\r\n" : "\n";
   var lines = new List<string>(ini.Split('\n'));
   for (int i = 0; i < lines.Count; i++) if (lines[i].EndsWith("\r")) lines[i] = lines[i].Substring(0, lines[i].Length - 1);
   var values = Values();
   var written = new bool[Keys.Length];
   bool defaultGamemode = false;
   string section = "";
   int ruleEnd = -1;
   for (int i = 0; i < lines.Count; i++) {
    string line = lines[i].Trim();
    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
    if (line.StartsWith("#") || line.StartsWith(";")) continue;
    int eq = line.IndexOf('=');
    if (eq <= 0) continue;
    string key = line.Substring(0, eq).Trim();
    if (section == "Gamemode" && key == "DefaultGamemode") { lines[i] = "DefaultGamemode=True"; defaultGamemode = true; }
    else if (section == "DefaultGamemode") {
     ruleEnd = i;
     int index = Array.IndexOf(Keys, key);
     if (index >= 0) { lines[i] = key + "=" + values[index]; written[index] = true; }
    }
   }
   var missing = new List<string>();
   for (int k = 0; k < Keys.Length; k++) if (!written[k]) missing.Add(Keys[k] + "=" + values[k]);
   if (missing.Count > 0) {
    if (ruleEnd >= 0) lines.InsertRange(ruleEnd + 1, missing);
    else { while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1); lines.Add(""); lines.Add("[DefaultGamemode]"); lines.AddRange(missing); lines.Add(""); }
   }
   if (!defaultGamemode) {
    int at = lines.FindIndex(l => l.Trim() == "[Gamemode]");
    if (at >= 0) lines.Insert(at + 1, "DefaultGamemode=True");
    else { lines.Add("[Gamemode]"); lines.Add("DefaultGamemode=True"); lines.Add(""); }
   }
   return string.Join(newline, lines.ToArray());
  }

  public static ServerRules Load(string file) {
   return File.Exists(file) ? Parse(File.ReadAllText(file, Encoding.UTF8)) : new ServerRules();
  }

  // Writes the rules in place; the file keeps its byte order mark.
  public void Save(string file) {
   string text = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "";
   File.WriteAllText(file, Apply(text), new UTF8Encoding(true));
  }

  // The presets in Gamemodes.json. Only the fields the ini can express are read.
  public static List<ServerRules> Presets(string json) {
   var list = new List<ServerRules>();
   var parts = Regex.Split(json, "\"SettingsName\"");
   for (int i = 1; i < parts.Length; i++) {
    string part = "\"SettingsName\"" + parts[i];
    var name = Regex.Match(part, "\"SettingsName\"\\s*:\\s*\"([^\"]*)\"");
    if (!name.Success) continue;
    var rules = new ServerRules { Name = name.Groups[1].Value };
    rules.Enemies = JsonFlag(part, "EnemySync");
    rules.Quests = JsonFlag(part, "Vanilla");
    rules.Koroks = JsonFlag(part, "Koroks");
    rules.Towers = JsonFlag(part, "Towers");
    rules.Shrines = JsonFlag(part, "Shrines");
    rules.Locations = JsonFlag(part, "Locations");
    rules.DivineBeasts = JsonFlag(part, "DivineBeast");
    var mode = Regex.Match(part, "\"GameMode\"\\s*:\\s*\"([^\"]*)\"");
    string gameMode = mode.Success ? mode.Groups[1].Value : "";
    rules.Special = gameMode == "HunterVsSpeedrunner" ? 1 : gameMode == "DeathSwap" ? 2 : 0;
    // Prop Hunt needs landmark setup the launcher does not offer yet.
    if (gameMode == "PropHunt") continue;
    list.Add(rules);
   }
   return list;
  }

  static bool JsonFlag(string text, string key) {
   var match = Regex.Match(text, "\"" + key + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
   return match.Success && Flag(match.Groups[1].Value);
  }

  // The preset whose rules match, or null for a custom mix.
  public static ServerRules Match(List<ServerRules> presets, ServerRules rules) {
   foreach (var preset in presets) if (preset.SameRules(rules)) return preset;
   return null;
  }

  public const string RecommendedName = "Game Completion";

  // The rules the launcher suggests: the Game Completion preset when it exists, otherwise the first preset without a special mode.
  public static ServerRules Recommended(List<ServerRules> presets) {
   var found = presets.Find(p => p.Name == RecommendedName) ?? presets.Find(p => p.Special == 0);
   return found == null ? new ServerRules { Name = "Custom" } : found.Copy();
  }

  // English names of the rules that are on (or off), in the order of the page.
  public List<string> Shared(bool on) {
   var all = new[] { new KeyValuePair<string, bool>("Enemies", Enemies), new KeyValuePair<string, bool>("Quests", Quests), new KeyValuePair<string, bool>("Koroks", Koroks),
    new KeyValuePair<string, bool>("Towers", Towers), new KeyValuePair<string, bool>("Shrines", Shrines), new KeyValuePair<string, bool>("Locations", Locations), new KeyValuePair<string, bool>("Divine Beasts", DivineBeasts) };
   var names = new List<string>();
   foreach (var rule in all) if (rule.Value == on) names.Add(rule.Key);
   return names;
  }
 }

 // How this PC takes part online: alone on this PC, hosting a server friends can join, or joining a friend's server.
 static class ServerConnection {
  public const string Local = "Local", Host = "Host", Join = "Join";
  public const int DefaultPort = 5050;
  public const string LoopbackAddress = "127.0.0.1", AnyAddress = "0.0.0.0", DefaultDescription = "Explore Hyrule with Friends!";

  public static string CleanMode(string text) { return text == Host || text == Join ? text : Local; }

  public static int CleanPort(string text) {
   int port;
   return int.TryParse((text ?? "").Trim(), out port) && port >= 1024 && port <= 65535 ? port : DefaultPort;
  }

  // The ini and the invite are line based, so text keeps to one line.
  public static string CleanText(string text, int max) {
   var clean = new StringBuilder();
   foreach (char c in text ?? "") if (!char.IsControl(c)) clean.Append(c);
   string result = clean.ToString().Trim();
   return result.Length > max ? result.Substring(0, max) : result;
  }

  // The server's ini reader treats ';' as a comment and cuts the password there, so a host password never holds one.
  public static string HostPassword(string text) { return CleanText((text ?? "").Replace(";", ""), 40); }

  public static string Invite(string host, int port, string password) {
   return "hyrule://" + host + ":" + port + (string.IsNullOrEmpty(password) ? "" : "?pw=" + Uri.EscapeDataString(password));
  }

  // Reads an invite link, "host:port" or just "host" (port 5050). False when the text is not an address.
  public static bool TryParse(string text, out string host, out int port, out string password) {
   host = null; port = DefaultPort; password = "";
   text = (text ?? "").Trim();
   if (text.StartsWith("hyrule://", StringComparison.OrdinalIgnoreCase)) text = text.Substring(9);
   int query = text.IndexOf('?');
   if (query >= 0) {
    var secret = Regex.Match(text.Substring(query + 1), "(?:^|&)pw=([^&]*)");
    if (secret.Success) password = Uri.UnescapeDataString(secret.Groups[1].Value);
    text = text.Substring(0, query);
   }
   text = text.TrimEnd('/');
   int colon = text.LastIndexOf(':');
   if (colon >= 0) {
    if (!int.TryParse(text.Substring(colon + 1), out port) || port < 1 || port > 65535) return false;
    text = text.Substring(0, colon);
   }
   if (!ValidHost(text)) return false;
   host = text;
   return true;
  }

  public static bool ValidHost(string text) {
   return Regex.IsMatch(text ?? "", "^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$");
  }

  public const string FirewallName = "Hyrule Online Server ";

  // Ports of the launcher's own firewall rules other than the one in use, read from `netsh ... show rule name=all` (the labels are localized, the rule names are not).
  public static List<int> StaleRulePorts(string output, int keep) {
   var ports = new List<int>();
   foreach (Match found in Regex.Matches(output ?? "", Regex.Escape(FirewallName) + "(\\d{1,5})\\s*$", RegexOptions.Multiline)) {
    int port;
    if (int.TryParse(found.Groups[1].Value, out port) && port != keep && !ports.Contains(port)) ports.Add(port);
   }
   return ports;
  }

  // The address worth sharing: a Tailscale one (reachable from anywhere), then a home network one; null when there is neither.
  public static string Best(IEnumerable<string> addresses) {
   string home = null;
   foreach (string address in addresses) {
    var part = address.Split('.');
    int first, second;
    if (part.Length != 4 || !int.TryParse(part[0], out first) || !int.TryParse(part[1], out second)) continue;
    if (first == 100 && second >= 64 && second <= 127) return address;
    if (home == null && (first == 10 || first == 192 && second == 168 || first == 172 && second >= 16 && second <= 31)) home = address;
   }
   return home;
  }

  // IPv4 addresses of the network adapters that are up, without Windows' virtual switches.
  public static List<string> Addresses() {
   var found = new List<string>();
   try {
    foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
     if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
     if (adapter.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)) continue;
     foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
      if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) found.Add(unicast.Address.ToString());
    }
   }
   catch (Exception) { }
   return found;
  }

  // Sets one key of an ini section and keeps every other line, the comments, the line endings and the section order.
  public static string Set(string ini, string section, string key, string value) {
   string newline = ini.Contains("\r\n") ? "\r\n" : "\n";
   var lines = new List<string>(ini.Split('\n'));
   for (int i = 0; i < lines.Count; i++) if (lines[i].EndsWith("\r")) lines[i] = lines[i].Substring(0, lines[i].Length - 1);
   string current = "";
   int last = -1, start = -1;
   for (int i = 0; i < lines.Count; i++) {
    string line = lines[i].Trim();
    if (line.StartsWith("[") && line.EndsWith("]")) { current = line.Substring(1, line.Length - 2); if (current == section) { start = i; last = i; } continue; }
    if (current != section || line.Length == 0) continue;
    last = i;
    int eq = line.IndexOf('=');
    if (!line.StartsWith("#") && !line.StartsWith(";") && eq > 0 && line.Substring(0, eq).Trim() == key) { lines[i] = key + "=" + value; return string.Join(newline, lines.ToArray()); }
   }
   if (start >= 0) lines.Insert(last + 1, key + "=" + value);
   else { while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1); lines.Add(""); lines.Add("[" + section + "]"); lines.Add(key + "=" + value); lines.Add(""); }
   return string.Join(newline, lines.ToArray());
  }

  public static string Apply(string ini, string ip, int port, string password, string description) {
   ini = Set(ini, "Connection", "IP", ip);
   ini = Set(ini, "Connection", "Port", port.ToString());
   ini = Set(ini, "Connection", "Password", password);
   return Set(ini, "ServerInformation", "Description", description);
  }

  // Writes the connection into the server's ini in place; the file keeps its byte order mark.
  public static void Save(string file, string ip, int port, string password, string description) {
   string text = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "";
   File.WriteAllText(file, Apply(text, ip, port, password, description), new UTF8Encoding(true));
  }
 }
}
