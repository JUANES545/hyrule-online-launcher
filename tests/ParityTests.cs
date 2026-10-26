using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace HyruleOnline {
 // Keeps the SteamOS launcher in step with the Windows one. steamos\parity.json records every Windows fact the SteamOS window mirrors
 // (limits, invite format, presets, tab order, the Online and World texts in three languages). This test reads those facts from the real
 // Windows code, compares them with the manifest, and compares the manifest with what the SteamOS files use.
 // A change on the Windows side therefore fails here until the SteamOS side and the manifest are updated on purpose.
 // Run with --facts to print the current Windows facts and every Online or World text (to copy into parity.json).
 static class ParityTests {
  const string ManifestFile = "steamos\\parity.json";
  static readonly List<string> Problems = new List<string>(), Skipped = new List<string>();
  static string root, xamlFile;
  static XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

  sealed class Seen { public string Text, Where; }

  static string Rel(string file) { return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file.Substring(root.Length).TrimStart('\\', '/') : file; }
  static string Quote(string text) { return "\"" + text.Replace("\n", "\\n") + "\""; }
  
  static string FindRoot(string start) {
   foreach (string from in new[] { start, Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory }) {
    for (var dir = new DirectoryInfo(from); dir != null; dir = dir.Parent)
     if (File.Exists(Path.Combine(dir.FullName, "src", "Launcher", "Launcher.xaml"))) return dir.FullName;
   }
   throw new Exception("TEST: src\\Launcher\\Launcher.xaml was not found from " + start);
  }

  static string Read(string relative) { return File.ReadAllText(Path.Combine(root, relative), Encoding.UTF8); }

  // ---- Windows facts, read from the real code ----

  static string Chars(IEnumerable<char> chars) { return new string(chars.ToArray()); }

  static string Parse(string text) {
   string host, password; int port;
   return ServerConnection.TryParse(text, out host, out port, out password) ? host + "|" + port + "|" + password : "invalid";
  }

  static string XamlMax(XDocument xaml, string name) {
   var box = xaml.Descendants().FirstOrDefault(e => (string)e.Attribute(x + "Name") == name);
   return box == null || box.Attribute("MaxLength") == null ? "missing" : (string)box.Attribute("MaxLength");
  }

  static List<KeyValuePair<string, string>> Facts(XDocument xaml) {
   var facts = new List<KeyValuePair<string, string>>();
   Action<string, string> add = (key, value) => facts.Add(new KeyValuePair<string, string>(key, value));
   var ports = Enumerable.Range(0, 70000).Where(p => ServerConnection.CleanPort(p.ToString()) == p).ToList();
   add("port.default", ServerConnection.DefaultPort.ToString());
   add("port.host.range", ports.Min() + "-" + ports.Max());
   var joinPorts = Enumerable.Range(0, 70000).Where(p => Parse("h:" + p) != "invalid").ToList();
   add("port.join.range", joinPorts.Min() + "-" + joinPorts.Max());
   add("password.host.max", ServerConnection.HostPassword(new string('a', 200)).Length.ToString());
   add("password.host.forbidden", Chars(Enumerable.Range(33, 94).Select(c => (char)c).Where(c => ServerConnection.HostPassword("a" + c + "a") != "a" + c + "a")));
   add("password.xaml.host.max", XamlMax(xaml, "HostPasswordBox"));
   add("password.xaml.join.max", XamlMax(xaml, "JoinPasswordBox"));
   add("description.max", XamlMax(xaml, "HostDescriptionBox"));
   add("description.default", ServerConnection.DefaultDescription);
   add("address.host.max", XamlMax(xaml, "HostAddressBox"));
   add("address.join.max", XamlMax(xaml, "JoinAddressBox"));
   add("port.xaml.max", XamlMax(xaml, "HostPortBox"));
   add("text.control.characters", ServerConnection.CleanText("a\r\nb\tc", 20) == "abc" ? "removed" : "kept");
   add("name.max", Characters.MaxName.ToString());
   add("name.allowed", Chars(Enumerable.Range(32, 95).Select(c => (char)c).Where(c => Characters.IsNameText(c.ToString()))));
   add("invite.sample.open", ServerConnection.Invite("h.example", 5050, ""));
   add("invite.sample.password", ServerConnection.Invite("100.64.0.5", 6000, "p w;&\u00e9"));
   foreach (string input in new[] { "hyrule://100.64.0.5:5050?pw=a%20b%26c", "192.168.1.20:6000", "my-host.example", "HYRULE://h:1/", "h:1023", "h:0", "h:65536", "-a", "a b", "h:5050?x=1&pw=p" })
    add("invite.parse " + input, Parse(input));
   foreach (string input in new[] { "a", "a-b.c", "a.", "-a", "a b", "" }) add("host.valid " + Quote(input), ServerConnection.ValidHost(input).ToString());
   var all = new ServerRules { Enemies = true, Quests = true, Koroks = true, Towers = true, Shrines = true, Locations = true, DivineBeasts = true };
   add("rules.order", string.Join(",", all.Shared(true).ToArray()));
   add("rules.specials", string.Join(",", ServerRules.SpecialNames));
   add("rules.recommended", ServerRules.RecommendedName);
   add("tabs", string.Join(",", Tabs(xaml).ToArray()));
   return facts;
  }

  static List<string> Tabs(XDocument xaml) {
   return xaml.Descendants().Where(e => ((string)e.Attribute(x + "Name") ?? "").StartsWith("Tab") && e.Name.LocalName == "Button" && e.Attribute("Content") != null).Select(e => (string)e.Attribute("Content")).ToList();
  }

  // The presets live in the dedicated server's Gamemodes.json, which is not in the repository: it is read when this PC has the server installed.
  static List<string> Presets(out string source) {
   source = Path.Combine(root, ".artifacts", "extract", "milkbar", "DedicatedServer", "Gamemodes.json");
   if (!File.Exists(source)) return null;
   return ServerRules.Presets(File.ReadAllText(source, Encoding.UTF8)).Select(r => r.Name + " " + Bits(r)).ToList();
  }

  static string Bits(ServerRules r) {
   Func<bool, string> b = v => v ? "1" : "0";
   return b(r.Enemies) + b(r.Quests) + b(r.Koroks) + b(r.Towers) + b(r.Shrines) + b(r.Locations) + b(r.DivineBeasts) + r.Special;
  }

  // ---- Online and World texts (what the SteamOS window has to decide about) ----

  static List<Seen> OnlineTexts(XDocument xaml, HashSet<string> known) {
   var seen = new List<Seen>();
   var translated = new[] { "Text", "Content", "ToolTip", "AutomationProperties.Name" };
   Action<XElement, string> take = (element, value) => {
    if (value == null || value.Length == 0 || value.StartsWith("{") || !known.Contains(value) || seen.Any(s => s.Text == value)) return;
    seen.Add(new Seen { Text = value, Where = Rel(xamlFile) + ":" + ((IXmlLineInfo)element).LineNumber });
   };
   var pages = new[] { "OnlinePage", "MultiplayerPage", "OnlineStatus", "PlayCopyInvite" };
   foreach (var page in xaml.Descendants().Where(e => pages.Contains((string)e.Attribute(x + "Name"))))
    foreach (var element in page.DescendantsAndSelf()) {
     foreach (var attribute in element.Attributes().Where(a => translated.Contains(a.Name.LocalName))) take(element, attribute.Value);
     take(element, string.Concat(element.Nodes().OfType<XText>().Select(n => n.Value).ToArray()).Trim());
    }
   foreach (var tab in xaml.Descendants().Where(e => e.Name.LocalName == "Button" && ((string)e.Attribute(x + "Name") ?? "").StartsWith("Tab"))) take(tab, (string)tab.Attribute("Content"));
   string code = Path.Combine(root, "src", "Launcher", "OnlineUi.cs");
   string[] lines = File.ReadAllLines(code, Encoding.UTF8);
   for (int i = 0; i < lines.Length; i++)
    foreach (Match m in Regex.Matches(lines[i], "\"((?:[^\"\\\\]|\\\\.)*)\"")) {
     string value = m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
     if (known.Contains(value) && !seen.Any(s => s.Text == value)) seen.Add(new Seen { Text = value, Where = Rel(code) + ":" + (i + 1) });
    }
   return seen;
  }

  // ---- The manifest ----

  static Dictionary<string, object> Obj(object value) { return (Dictionary<string, object>)value; }
  static string Str(object value) { return (string)value; }
  static List<string> Items(object value) { return ((IEnumerable)value).Cast<object>().Select(o => (string)o).ToList(); }

  static void Fail(string message) { Problems.Add(message); }

  // Quoted words of a bash array or a Python list/dict body.
  static List<string> Words(string body) {
   return Regex.Matches(body, "\"([^\"]*)\"|([^\\s\",]+)").Cast<Match>().Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
  }

  static string Section(string text, string pattern) {
   var m = Regex.Match(text, pattern, RegexOptions.Multiline | RegexOptions.Singleline);
   return m.Success ? m.Groups[1].Value : null;
  }

  static void Same(string what, List<string> expected, List<string> actual, string file, string fix) {
   if (actual == null) { Fail(file + ": could not find " + what + ". If you moved it, update the pattern in tests\\ParityTests.cs."); return; }
   if (!expected.SequenceEqual(actual)) Fail(file + ": " + what + " is [" + string.Join(", ", actual.ToArray()) + "] but " + ManifestFile + " says [" + string.Join(", ", expected.ToArray()) + "]. " + fix);
  }

  static string Fixes(string key) {
   if (key.StartsWith("port") || key.StartsWith("password") || key.StartsWith("description") || key.StartsWith("address") || key.StartsWith("name") || key.StartsWith("text"))
    return "Update the limits in steamos\\hyrule-online.sh (valid_host_port, clean_text; valid_name in lib.sh) and steamos\\launcher.py (the LIMITS dictionary, NAME_RE), then " + ManifestFile + ".";
   if (key.StartsWith("invite") || key.StartsWith("host.valid")) return "Update invite_url and parse_invite in steamos\\hyrule-online.sh and steamos\\launcher.py, then " + ManifestFile + ".";
   if (key.StartsWith("rules")) return "Update the rule names, specials and default preset in steamos\\hyrule-online.sh and steamos\\launcher.py, then " + ManifestFile + ".";
   return "Update steamos\\launcher.py (TABS) and the page it shows, then " + ManifestFile + ".";
  }

  static void CompareFacts(List<KeyValuePair<string, string>> windows, Dictionary<string, object> recorded) {
   foreach (var fact in windows) {
    if (!recorded.ContainsKey(fact.Key)) Fail("Windows has a new fact '" + fact.Key + "' = " + Quote(fact.Value) + " that " + ManifestFile + " does not record. " + Fixes(fact.Key));
    else if (Str(recorded[fact.Key]) != fact.Value) Fail("Windows changed '" + fact.Key + "': it is now " + Quote(fact.Value) + " but " + ManifestFile + " says " + Quote(Str(recorded[fact.Key])) + ". " + Fixes(fact.Key));
   }
   foreach (string key in recorded.Keys) if (!windows.Any(f => f.Key == key)) Fail(ManifestFile + " records '" + key + "' but Windows no longer has that fact. Remove it from the manifest and from the SteamOS side if it is dead.");
  }

  static void CheckPresets(Dictionary<string, object> manifest, string script, string launcher) {
   var wanted = ((IEnumerable)manifest["presets"]).Cast<object>().Select(o => Str(o)).ToList();
   string source;
   var windows = Presets(out source);
   if (windows == null) Skipped.Add("Windows presets: " + Rel(source) + " is not here (the Milk Bar server is not installed in this checkout)");
   else if (!wanted.SequenceEqual(windows)) Fail("Windows presets changed: Gamemodes.json now gives [" + string.Join("; ", windows.ToArray()) + "] but " + ManifestFile + " says [" + string.Join("; ", wanted.ToArray()) + "]. Update PRESETS and PRESET_BITS in steamos\\hyrule-online.sh and PRESETS in steamos\\launcher.py, then " + ManifestFile + ".");
   var names = Words(Section(script, "^PRESETS=\\(([^)]*)\\)") ?? ""); var bits = Words(Section(script, "^PRESET_BITS=\\(([^)]*)\\)") ?? "");
   var inScript = names.Select((n, i) => n + " " + (i < bits.Count ? bits[i] : "?")).ToList();
   Same("PRESETS/PRESET_BITS", wanted.Select(w => w.Substring(0, w.LastIndexOf(' ')) + " " + w.Substring(w.LastIndexOf(' ') + 1)).ToList(), inScript.Count == 0 ? null : inScript, "steamos\\hyrule-online.sh", "Change the arrays or the manifest.");
   var fallback = Section(launcher, "^PRESETS = \\{(.*?)\\}");
   var pairs = fallback == null ? null : Regex.Matches(fallback, "\"([^\"]+)\":\\s*\"([01]{7}[012])\"").Cast<Match>().Select(m => m.Groups[1].Value + " " + m.Groups[2].Value).ToList();
   Same("the PRESETS fallback", wanted, pairs, "steamos\\launcher.py", "Change the dictionary or the manifest.");
  }

  static void CheckSteamos(Dictionary<string, object> manifest, string script, string launcher) {
   var facts = Obj(manifest["facts"]);
   var tabs = Obj(manifest["tabs"]);
   Same("TABS", Items(tabs["steamos"]), Section(launcher, "^TABS = \\[([^\\]]*)\\]") == null ? null : Words(Section(launcher, "^TABS = \\[([^\\]]*)\\]")), "steamos\\launcher.py", "The SteamOS tabs are recorded under tabs.steamos.");
   var windowsTabs = Items(tabs["windows"]);
   var map = Obj(tabs["map"]);
   var order = Items(tabs["steamos"]).Select(t => windowsTabs.IndexOf(Str(map.ContainsKey(t) ? map[t] : ""))).ToList();
   if (order.Any(i => i < 0) || !order.SequenceEqual(order.OrderBy(i => i))) Fail(ManifestFile + ": tabs.map must send every SteamOS tab to a Windows tab and keep the Windows order (" + string.Join(", ", windowsTabs.ToArray()) + ").");
   Same("RULE_NAMES", facts["rules.order"].ToString().Split(',').ToList(), Section(launcher, "^RULE_NAMES = \\[([^\\]]*)\\]") == null ? null : Words(Section(launcher, "^RULE_NAMES = \\[([^\\]]*)\\]")), "steamos\\launcher.py", "Rule names follow the Windows World page.");
   Same("RULE_NAMES", facts["rules.order"].ToString().Split(',').ToList(), Section(script, "^RULE_NAMES=\\(([^)]*)\\)") == null ? null : Words(Section(script, "^RULE_NAMES=\\(([^)]*)\\)")), "steamos\\hyrule-online.sh", "Rule names follow the Windows World page.");
   Same("SPECIALS", facts["rules.specials"].ToString().Split(',').ToList(), Section(launcher, "^SPECIALS = \\[([^\\]]*)\\]") == null ? null : Words(Section(launcher, "^SPECIALS = \\[([^\\]]*)\\]")), "steamos\\launcher.py", "Special modes follow ServerRules.SpecialNames.");
   Needs(launcher, "steamos\\launcher.py", "the default port " + facts["port.default"], "\\b" + facts["port.default"] + "\\b");
   Needs(launcher, "steamos\\launcher.py", "the default description", Regex.Escape(Str(facts["description.default"])));
   Needs(launcher, "steamos\\launcher.py", "the host port range", "\\b1024\\b.*\\b65535\\b|\\b65535\\b.*\\b1024\\b|valid_port\\(.*1024");
   Needs(launcher, "steamos\\launcher.py", "the password limit " + facts["password.host.max"], "\\b" + facts["password.host.max"] + "\\b");
   Needs(launcher, "steamos\\launcher.py", "the description limit " + facts["description.max"], "\\b" + facts["description.max"] + "\\b");
   Needs(launcher, "steamos\\launcher.py", "the forbidden password character", Regex.Escape("\"" + facts["password.host.forbidden"] + "\""));
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the default description", Regex.Escape(Str(facts["description.default"])));
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the minimum host port 1024", "-ge 1024");
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the invite format", Regex.Escape("hyrule://%s:%s%s") + ".*" + Regex.Escape("?pw="));
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the invite length limit " + facts["address.join.max"], "-le " + facts["address.join.max"]);
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the password limit " + facts["password.host.max"], "\\b" + facts["password.host.max"] + "\\b");
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the description limit " + facts["description.max"], "\\b" + facts["description.max"] + "\\b");
   Needs(launcher, "steamos\\launcher.py", "the player name limit " + facts["name.max"], "\\{1," + facts["name.max"] + "\\}");
   Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the player name limit " + facts["name.max"], "\\{1," + facts["name.max"] + "\\}");
   Needs(launcher, "steamos\\launcher.py", "the invite scheme", Regex.Escape("hyrule://"));
   CheckLimits(facts, launcher);
   var cli = Obj(manifest["cli"]);
   foreach (string flag in Items(cli["flags"])) Needs(script, "steamos\\hyrule-online.sh or lib.sh", "the option " + flag, "(^|[ |(])" + Regex.Escape(flag) + "([ |)]|$)");
   foreach (string flag in Items(cli["usedByLauncher"])) Needs(launcher, "steamos\\launcher.py", "the option " + flag, Regex.Escape("\"" + flag + "\""));
   string status = Section(script, "^status_line\\(\\) \\{(.*?)^\\}") ?? "";
   foreach (string key in Items(cli["statusKeys"])) Needs(status, "steamos\\hyrule-online.sh (status_line)", "the status key " + key, Regex.Escape(key + "="));
  }

  static void CheckLimits(Dictionary<string, object> facts, string launcher) {
   string block = Section(launcher, "^LIMITS = \\{(.*?)\\}");
   if (block == null) return; // an older launcher.py keeps the limits inline; the Needs checks above cover it
   var range = Str(facts["port.host.range"]).Split('-');
   var expected = new Dictionary<string, string> {
    { "port_low", range[0] }, { "port_high", range[1] }, { "host_password", Str(facts["password.host.max"]) }, { "description", Str(facts["description.max"]) },
    { "host_address", Str(facts["address.host.max"]) }, { "join_address", Str(facts["address.join.max"]) }, { "join_password", Str(facts["password.xaml.join.max"]) }, { "name", Str(facts["name.max"]) }
   };
   foreach (var e in expected) {
    var m = Regex.Match(block, "\"" + e.Key + "\":\\s*([0-9]+)");
    if (!m.Success) Fail("steamos\\launcher.py LIMITS has no '" + e.Key + "'. Windows says " + e.Value + " (" + ManifestFile + ").");
    else if (m.Groups[1].Value != e.Value) Fail("steamos\\launcher.py LIMITS['" + e.Key + "'] is " + m.Groups[1].Value + " but the Windows limit recorded in " + ManifestFile + " is " + e.Value + ". Change LIMITS (and steamos\\lib.sh / hyrule-online.sh if they enforce it).");
   }
  }

  static void Needs(string text, string file, string what, string pattern) {
   if (!Regex.IsMatch(text, pattern, RegexOptions.Multiline)) Fail(file + " no longer has " + what + ". Windows and the manifest both say it must; if SteamOS changed on purpose, update " + ManifestFile + " and the pattern in tests\\ParityTests.cs.");
  }

  static void CheckTexts(Dictionary<string, object> manifest, List<Seen> online, string launcher, string strings) {
   var mirrored = ((IEnumerable)manifest["mirrored"]).Cast<object>().Select(Obj).ToList();
   var notApplicable = ((IEnumerable)manifest["notApplicable"]).Cast<object>().Select(Obj).ToList();
   var sorted = new HashSet<string>();
   Action<string, string> claim = (text, list) => { if (!sorted.Add(text)) Fail(ManifestFile + ": " + Quote(text) + " is listed twice (" + list + "). A text is either mirrored or notApplicable."); };
   foreach (var item in mirrored) claim(Str(item["en"]), "mirrored");
   foreach (var group in notApplicable) {
    if (string.IsNullOrWhiteSpace(Str(group["why"]))) Fail(ManifestFile + ": a notApplicable group has no 'why'.");
    foreach (string text in Items(group["texts"])) claim(text, "notApplicable");
   }
   foreach (var s in online.Where(s => !sorted.Contains(s.Text)))
    Fail("Windows has a new Online or World text " + Quote(s.Text) + " (" + s.Where + "). Decide if the SteamOS window needs it: if yes, add it to steamos\\launcher.py and steamos\\strings.json and list it under \"mirrored\" in " + ManifestFile + "; if not, list it under \"notApplicable\" there with the reason.");
   foreach (string text in sorted.Where(t => !online.Any(s => s.Text == t)))
    Fail(ManifestFile + " lists " + Quote(text) + " but no Online or World text in the Windows launcher uses it any more. Remove it from the manifest, and from the SteamOS window if it was mirrored.");

   var corpus = new HashSet<string>();
   if (strings != null) Collect(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(strings), corpus);
   else Skipped.Add("SteamOS translations: steamos\\strings.json does not exist yet, so only the English texts in launcher.py were checked");
   int checkedTexts = 0;
   foreach (var item in mirrored) {
    string en = Str(item["en"]);
    var row = Lang.Table.FirstOrDefault(r => r[0] == en);
    string es = row != null ? row[1] : en, fr = row != null ? row[2] : en;
    if (row == null && !Lang.Neutral.Contains(en)) { Fail("Windows no longer has the text " + Quote(en) + " in Lang.cs, so " + ManifestFile + " must drop it."); continue; }
    if (Str(item["es"]) != es) Fail("Windows changed the Spanish text of " + Quote(en) + " to " + Quote(es) + " (the manifest has " + Quote(Str(item["es"])) + "). Update steamos\\strings.json, then " + ManifestFile + ".");
    if (Str(item["fr"]) != fr) Fail("Windows changed the French text of " + Quote(en) + " to " + Quote(fr) + " (the manifest has " + Quote(Str(item["fr"])) + "). Update steamos\\strings.json, then " + ManifestFile + ".");
    var steamos = item.ContainsKey("steamos") ? Obj(item["steamos"]) : null;
    string[] wanted = steamos == null ? new[] { en, es, fr } : new[] { Str(steamos["en"]), Str(steamos["es"]), Str(steamos["fr"]) };
    string[] languages = { "English", "Spanish", "French" };
    for (int i = 0; i < 3; i++) {
     if (i == 0 && (launcher.Contains(wanted[0]) || corpus.Contains(wanted[0]))) continue;
     if (i > 0 && (strings == null || wanted[i] == wanted[0] || corpus.Contains(wanted[i]))) continue;
     if (i == 0 || strings != null) Fail((i == 0 ? "steamos\\launcher.py and steamos\\strings.json do not have" : "steamos\\strings.json has no " + languages[i] + " text") + " for the mirrored text " + Quote(en) + (i == 0 ? "" : " (expected " + Quote(wanted[i]) + ")") + ". Add it, or move the text to notApplicable in " + ManifestFile + " with a reason.");
    }
    checkedTexts++;
   }
   Console.WriteLine("parity: " + checkedTexts + " mirrored texts, " + sorted.Count(t => !mirrored.Any(m => Str(m["en"]) == t)) + " not applicable.");
  }

  // The finder vectors in the manifest must give the same answers from the real ServerFinder, so the SteamOS finder can be tested against the same file.
  static void CheckFinder(Dictionary<string, object> manifest) {
   if (!manifest.ContainsKey("finder")) { Fail(ManifestFile + " has no \"finder\" vectors. Add the shared server finder vectors (classify, local, tailscale, order, autofill, cap)."); return; }
   var finder = Obj(manifest["finder"]);
   var constants = Obj(finder["constants"]);
   var actualConstants = new Dictionary<string, int> { { "port", ServerFinder.Port }, { "beaconPort", ServerFinder.BeaconPort }, { "probeMs", ServerFinder.ProbeMs }, { "probes", ServerFinder.Probes }, { "maxHosts", ServerFinder.MaxHosts }, { "testMs", ServerFinder.TestMs }, { "rows", ServerFinder.MaxRows } };
   foreach (var pair in actualConstants) if (!constants.ContainsKey(pair.Key) || Convert.ToInt32(constants[pair.Key]) != pair.Value) Fail(ManifestFile + " finder.constants." + pair.Key + " must be " + pair.Value + " like ServerFinder.cs.");
   if (ServerFinder.Port != ServerConnection.DefaultPort) Fail("ServerFinder.Port is " + ServerFinder.Port + " but ServerConnection.DefaultPort is " + ServerConnection.DefaultPort + ".");

   var beacon = Obj(finder["beacon"]);
   if (Str(beacon["request"]) != ServerFinder.Request || Str(beacon["replyPrefix"]) != ServerFinder.ReplyPrefix) Fail(ManifestFile + " finder.beacon request and replyPrefix must be " + Quote(ServerFinder.Request) + " and " + Quote(ServerFinder.ReplyPrefix) + " like ServerFinder.cs.");
   foreach (var item in ((IEnumerable)beacon["requests"]).Cast<object>().Select(Obj))
    if (ServerFinder.IsRequest(Encoding.ASCII.GetBytes(Str(item["text"]))) != (bool)item["valid"]) Fail("ServerFinder.IsRequest(" + Quote(Str(item["text"])) + ") must be " + item["valid"] + " (finder.beacon.requests).");
   foreach (var item in ((IEnumerable)beacon["replies"]).Cast<object>().Select(Obj)) {
    int replyPort;
    bool valid = ServerFinder.TryReply(Encoding.ASCII.GetBytes(Str(item["text"])), out replyPort);
    int want = Convert.ToInt32(item["port"]);
    if (valid != (want > 0) || valid && replyPort != want) Fail("ServerFinder.TryReply(" + Quote(Str(item["text"])) + ") must " + (want > 0 ? "give port " + want : "be rejected") + " (finder.beacon.replies).");
   }

   foreach (var pair in Obj(finder["classify"])) if (ServerFinder.Classify(pair.Key) != Str(pair.Value)) Fail("ServerFinder.Classify(" + pair.Key + ") is " + ServerFinder.Classify(pair.Key) + " but " + ManifestFile + " says " + Str(pair.Value) + ".");

   var local = Obj(finder["local"]);
   var interfaces = ((IEnumerable)local["interfaces"]).Cast<object>().Select(o => { var item = ((IEnumerable)o).Cast<object>().ToList(); return new KeyValuePair<string, int>(Str(item[0]), Convert.ToInt32(item[1])); }).ToList();
   var expectedHosts = new List<string>();
   foreach (var range in ((IEnumerable)local["hosts"]).Cast<object>().Select(Obj)) {
    var except = ((IEnumerable)range["except"]).Cast<object>().Select(Convert.ToInt32).ToList();
    for (int i = Convert.ToInt32(range["from"]); i <= Convert.ToInt32(range["to"]); i++) if (!except.Contains(i)) expectedHosts.Add(Str(range["prefix"]) + i);
   }
   if (!ServerFinder.LocalHosts(interfaces).SequenceEqual(expectedHosts)) Fail("ServerFinder.LocalHosts gives " + ServerFinder.LocalHosts(interfaces).Count + " hosts for the finder.local interfaces but " + ManifestFile + " lists " + expectedHosts.Count + ".");

   var tailscale = Obj(finder["tailscale"]);
   if (!ServerFinder.TailscaleHosts(Str(tailscale["json"])).SequenceEqual(Items(tailscale["hosts"]))) Fail("ServerFinder.TailscaleHosts does not return " + string.Join(",", Items(tailscale["hosts"]).ToArray()) + " for the finder.tailscale JSON.");
   foreach (string garbage in Items(tailscale["garbage"])) if (ServerFinder.TailscaleHosts(garbage).Count != 0) Fail("ServerFinder.TailscaleHosts must give nothing for " + Quote(garbage) + ".");

   var order = Obj(finder["order"]);
   var found = ((IEnumerable)order["in"]).Cast<object>().Select(o => { var item = ((IEnumerable)o).Cast<object>().ToList(); return new FoundServer { Via = Str(item[0]), Host = Str(item[1]), Port = ServerFinder.Port, Ms = Convert.ToInt32(item[2]) }; }).ToList();
   if (!ServerFinder.Order(found).Select(f => f.Host).SequenceEqual(Items(order["out"]))) Fail("ServerFinder.Order does not give " + string.Join(", ", Items(order["out"]).ToArray()) + " for the finder.order input.");

   foreach (var item in ((IEnumerable)finder["autofill"]).Cast<object>().Select(Obj))
    if (ServerFinder.Fills(Str(item["field"]), Str(item["finder"])) != (bool)item["fills"]) Fail("ServerFinder.Fills(" + Quote(Str(item["field"])) + ", " + Quote(Str(item["finder"])) + ") must be " + item["fills"] + ".");

   var last = Obj(finder["lastUsed"]);
   var lastFaces = ((IEnumerable)last["interfaces"]).Cast<object>().Select(o => { var item = ((IEnumerable)o).Cast<object>().ToList(); return new KeyValuePair<string, int>(Str(item[0]), Convert.ToInt32(item[1])); }).ToList();
   foreach (var item in ((IEnumerable)last["cases"]).Cast<object>().Select(Obj)) {
    string host = Str(item["host"]);
    int port = Convert.ToInt32(item["port"]);
    var match = ServerFinder.Candidates(new string[0], lastFaces, host, port).FirstOrDefault(f => f.Host == host && f.Port == port);
    if ((match != null) != (bool)item["candidate"]) Fail("ServerFinder.Candidates " + ((bool)item["candidate"] ? "must" : "must not") + " probe the last used address " + host + ":" + port + " (finder.lastUsed).");
    else if (match != null && match.Via != Str(item["via"])) Fail("The last used address " + host + " must be classed " + Str(item["via"]) + " (finder.lastUsed).");
   }

   int max = Convert.ToInt32(Obj(finder["cap"])["max"]);
   var many = Enumerable.Range(1, 6).Select(i => new KeyValuePair<string, int>("192.168." + i + ".1", 24)).ToList();
   var capped = ServerFinder.Candidates(new[] { "100.65.0.1" }, many, null, 0);
   if (capped.Count != max || capped[0].Via != ServerFinder.Vpn) Fail("ServerFinder.Candidates must cut to " + max + " keeping the VPN hosts first (it gave " + capped.Count + ").");
  }

  static void Collect(object node, HashSet<string> into) {
   var dict = node as Dictionary<string, object>;
   if (dict != null) { foreach (var pair in dict) { into.Add(pair.Key); Collect(pair.Value, into); } }
   else if (node is string) into.Add((string)node);
   else if (node is IEnumerable) foreach (object child in (IEnumerable)node) Collect(child, into);
  }

  // ---- Run ----

  static int Main(string[] args) {
   try {
    root = FindRoot(args.Length > 0 && args[0] != "--facts" ? args[0] : ".");
    xamlFile = Path.Combine(root, "src", "Launcher", "Launcher.xaml");
    var xaml = XDocument.Load(xamlFile, LoadOptions.SetLineInfo);
    var known = new HashSet<string>(Lang.Table.Select(r => r[0]).Concat(Lang.Neutral));
    var facts = Facts(xaml);
    var online = OnlineTexts(xaml, known);
    if (args.Contains("--facts")) { Print(facts, online); return 0; }

    var manifest = Obj(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(Read(ManifestFile)));
    string version = File.ReadAllLines(Path.Combine(root, "VERSION"))[0].Trim();
    if (Str(manifest["version"]) != version) Fail("VERSION is " + version + " but " + ManifestFile + " is at " + Str(manifest["version"]) + ". Before a release, review the Windows changes since " + Str(manifest["version"]) + " against the SteamOS launcher, then set \"version\" to " + version + ".");
    CompareFacts(facts, Obj(manifest["facts"]));
    var windowsTabs = Items(Obj(manifest["tabs"])["windows"]);
    var actualTabs = Tabs(xaml);
    if (!windowsTabs.SequenceEqual(actualTabs)) Fail("The Windows tabs are now [" + string.Join(", ", actualTabs.ToArray()) + "] but " + ManifestFile + " says [" + string.Join(", ", windowsTabs.ToArray()) + "]. Decide what the SteamOS tabs need (steamos\\launcher.py TABS), then update tabs in " + ManifestFile + ".");
    string script = Read("steamos\\hyrule-online.sh") + "\n" + Read("steamos\\lib.sh"), launcher = Read("steamos\\launcher.py");
    string stringsFile = Path.Combine(root, "steamos", "strings.json");
    CheckPresets(manifest, script, launcher);
    CheckSteamos(manifest, script, launcher);
    CheckFinder(manifest);    CheckTexts(manifest, online, launcher, File.Exists(stringsFile) ? File.ReadAllText(stringsFile, Encoding.UTF8) : null);
   }
   catch (Exception error) {
    Problems.Add("the parity test could not run: " + error.GetType().Name + ": " + error.Message);
   }
   foreach (string note in Skipped) Console.WriteLine("SKIPPED: " + note);
   if (Problems.Count > 0) {
    Console.Error.WriteLine("TEST: the SteamOS launcher is out of step with the Windows launcher (" + Problems.Count + " problem(s)):\n  - " + string.Join("\n  - ", Problems.ToArray()));
    Console.Error.WriteLine("How to fix: change steamos\\ to match Windows (or decide it does not apply), then update steamos\\parity.json. Run .artifacts\\tests\\ParityTests.exe . --facts to print the current Windows values. See \"Keeping the Windows and SteamOS launchers in sync\" in CONTRIBUTING.md.");
    return 1;
   }
   Console.WriteLine("PASS: the SteamOS launcher matches the Windows facts in " + ManifestFile + ".");
   return 0;
  }

  static void Print(List<KeyValuePair<string, string>> facts, List<Seen> online) {
   Console.OutputEncoding = new UTF8Encoding(false);
   Console.WriteLine("== facts ==");
   foreach (var fact in facts) Console.WriteLine("  " + Quote(fact.Key) + ": " + Quote(fact.Value) + ",");
   string source;
   var presets = Presets(out source);
   Console.WriteLine("== presets (" + (presets == null ? "Gamemodes.json not found" : Rel(source)) + ") ==");
   if (presets != null) foreach (string preset in presets) Console.WriteLine("  " + Quote(preset) + ",");
   Console.WriteLine("== online and world texts ==");
   foreach (var seen in online) {
    var row = Lang.Table.FirstOrDefault(r => r[0] == seen.Text);
    Console.WriteLine("  " + seen.Where + "  " + Quote(seen.Text) + (row == null ? "  (neutral)" : "  es=" + Quote(row[1]) + " fr=" + Quote(row[2])));
   }
  }
 }
}
