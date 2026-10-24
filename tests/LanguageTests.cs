using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace HyruleOnline {
 // Reads the launcher sources and fails on any text the user can see that is not translated, so a text added by a later change
 // is caught without editing this test. Every problem names the text and where it was found.
 static class LanguageTests {
  sealed class Token { public string Kind, Text; public int Line; }
  sealed class Source { public string Path, Name; public List<Token> Tokens; }
  sealed class Piece { public int Line; public string Value; public bool Literal; }

  // Texts that are the same in every language: product and feature names, numbers and units. Each row is the English text and the languages that may repeat it.
  static readonly string[][] SameAsEnglish = {
   new[] { "AVATAR", "es", "fr" },                // Spanish and French both say "avatar".
   new[] { "SESSION", "fr" },                     // The French word is spelled the same.
   new[] { "VSync", "es", "fr" },                 // Graphics option name.
   new[] { "Auto ({0})", "es", "fr" },            // "Auto" is the same word in both languages; {0} is the API name.
   new[] { "LAUNCHER", "es", "fr" },              // Used as a loan word in both.
   new[] { "Auto · {0} FPS", "es", "fr" },        // Unit and number.
   new[] { "Ultra", "es", "fr" },
   new[] { "Experimental", "es" },               // Same word in Spanish.
   new[] { "DLC v{0}", "es", "fr" },              // DLC is the same abbreviation in both.                 // Performance class name.
  };

  // Expressions that may be passed to a translation call instead of a literal because the values come from a list the test checks below (see CheckComputed).
  static readonly string[] Computed = {
   "Launcher.cs: english",                        // Re-translation of the stored status and prompt texts, which were already checked where they were set.
   "Launcher.cs: statusText",
   "Launcher.cs: promptText",
   "Launcher.cs: steps[step].Key",
   "Launcher.cs: ServerRules.SpecialNames[i]",    // CheckComputed reads SpecialNames.
   "Launcher.cs: option[0]",                      // CheckComputed reads ServerCommands.Weather.
   "Launcher.cs: Hardware.TierNames[hardware.Tier]", // CheckComputed reads TierNames.
   "Launcher.cs: n",                              // CheckComputed reads ServerRules.Shared.
   "Launcher.cs: text",                           // CheckComputed reads GraphicsMod.ScaleLabel.
   "Setup.cs: English",                           // The setup checklist texts, listed with Msg.Say and checked there.
   "SetupWizard.cs: group",                       // CheckComputed reads the setup group names.
   "Translator.cs: item.English",                 // The XAML text, checked from the XAML.
  };

  // Literals assigned in code that are units, not words.
  static readonly string[] Units = { " FPS", " Hz" };

  static readonly string[] Helpers = { "Tr", "Fail", "SetStatus", "SetPrompt", "Step" };
  static readonly string[] TextProperties = { "Text", "Content", "ToolTip", "Header", "Title" };
  static readonly string[] English = { "the", "and", "is", "are", "with", "for", "of", "to", "when", "then", "your", "this", "that", "from", "you", "will", "not", "can", "does", "each", "only", "while", "before", "after" };

  public static void Audit(string root) {
   root = FindRoot(root);
   var problems = new List<string>();
   var sources = new List<Source>();
   foreach (string file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)) {
    sources.Add(new Source { Path = file, Name = Path.GetFileName(file), Tokens = Tokenize(File.ReadAllText(file)) });
   }
   var known = new HashSet<string>(Lang.Table.Select(r => r[0]).Concat(Lang.Neutral));
   var used = new HashSet<string>();
   string langFile = Path.Combine(root, "src", "Launcher", "Lang.cs");

   var rowLines = CheckTable(root, langFile, problems);
   CheckHelp(problems);
   int calls = CheckCode(root, sources.Where(s => s.Name != "Lang.cs").ToList(), known, used, problems);
   int xaml = CheckXaml(root, known, used, problems);
   CheckComputed(known, problems);
   foreach (var row in Lang.Table) if (!used.Contains(row[0])) problems.Add(Where(root, langFile, rowLines.ContainsKey(row[0]) ? rowLines[row[0]] : 0) + ": the row is not used by any text in src (remove it if it is dead): " + Quote(row[0]));
   if (calls < 40 || xaml < 40) problems.Add("the scan found only " + calls + " translated calls and " + xaml + " XAML texts, so it is not reading the sources");

   if (problems.Count > 0) throw new Exception("TEST: the translation audit found " + problems.Count + " problem(s):\n  - " + string.Join("\n  - ", problems.ToArray()));
   Console.WriteLine("PASS: " + Lang.Table.Length + " translations, " + calls + " translated calls and " + xaml + " XAML texts checked.");
  }

  // The repository root: the given folder, or the nearest parent of it (or of the working folder) that holds the launcher sources.
  static string FindRoot(string root) {
   foreach (string start in new[] { root, Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory }) {
    for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent) {
     if (File.Exists(Path.Combine(dir.FullName, "src", "Launcher", "Launcher.xaml"))) return dir.FullName;
    }
   }
   throw new Exception("TEST: src\\Launcher\\Launcher.xaml was not found from " + root);
  }

  static string Quote(string text) { return "\"" + text.Replace("\n", "\\n") + "\""; }

  static string Where(string root, string file, int line) {
   string relative = file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file.Substring(root.Length).TrimStart('\\', '/') : file;
   return relative + ":" + line;
  }

  // ---- The table ----

  static string Holes(string text) {
   return string.Join(",", Regex.Matches(text, "\\{\\d+[^}]*\\}").Cast<Match>().Select(m => m.Value).OrderBy(v => v).ToArray());
  }

  static int HoleCount(string text) {
   var found = Regex.Matches(text, "\\{(\\d+)").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
   return found.Count == 0 ? 0 : found.Max() + 1;
  }

  static string Edge(string text) {
   return text.Substring(0, text.Length - text.TrimStart().Length) + "|" + text.Substring(text.TrimEnd().Length);
  }

  static string Ending(string text) {
   string trimmed = text.TrimEnd().Replace("…", "...");
   var match = Regex.Match(trimmed, "(\\.\\.\\.|[.:!?])$");
   return match.Success ? match.Value : "";
  }

  static string LeftOverEnglish(string text) {
   string plain = Regex.Replace(text, "\\{\\d+[^}]*\\}", " ");
   var words = Regex.Matches(plain, "[A-Za-z]+").Cast<Match>().Select(m => m.Value.ToLowerInvariant()).Where(w => English.Contains(w)).Distinct().ToArray();
   return words.Length == 0 ? null : string.Join(", ", words);
  }

  // Returns the line of each English text in Lang.cs.
  static Dictionary<string, int> CheckTable(string root, string langFile, List<string> problems) {
   var lines = new Dictionary<string, int>();
   foreach (var token in Tokenize(File.ReadAllText(langFile))) if (token.Kind == "str" && !lines.ContainsKey(token.Text)) lines[token.Text] = token.Line;
   Func<string, string> at = text => Where(root, langFile, lines.ContainsKey(text) ? lines[text] : 0);

   var seen = new HashSet<string>();
   foreach (var row in Lang.Table) {
    string english = row[0];
    if (!seen.Add(english)) problems.Add(at(english) + ": English text appears twice in Lang.Table: " + Quote(english));
    if (row.Length != Lang.Supported.Length) { problems.Add(at(english) + ": the row needs one text per language (" + string.Join("/", Lang.Supported) + "): " + Quote(english)); continue; }
    if (Lang.Neutral.Contains(english)) problems.Add(at(english) + ": the text is in both Lang.Table and Lang.Neutral: " + Quote(english));
    for (int c = 1; c < row.Length; c++) {
     string lang = Lang.Supported[c], text = row[c], label = at(english) + ": " + lang + " row for " + Quote(english);
     if (string.IsNullOrWhiteSpace(text)) { problems.Add(label + " is empty"); continue; }
     if (Holes(text) != Holes(english)) problems.Add(label + " has the placeholders [" + Holes(text) + "] instead of [" + Holes(english) + "]: " + Quote(text));
     if (text.Split('\n').Length != english.Split('\n').Length) problems.Add(label + " has a different number of line breaks: " + Quote(text));
     if (Edge(text) != Edge(english)) problems.Add(label + " starts or ends with different spaces or line breaks: " + Quote(text));
     if (Ending(text) != Ending(english)) problems.Add(label + " ends with '" + Ending(text) + "' but the English ends with '" + Ending(english) + "': " + Quote(text));
     bool allowed = SameAsEnglish.Any(a => a[0] == english && a.Skip(1).Contains(lang));
     if (text == english && !allowed) problems.Add(label + " is still English (if it is a name or a unit, add it to SameAsEnglish): " + Quote(text));
     if (text != english && allowed) problems.Add(label + " is allowed to match English but no longer does; remove it from SameAsEnglish");
     string stray = LeftOverEnglish(text);
     if (stray != null && !allowed) problems.Add(label + " still has English words (" + stray + "): " + Quote(text));
    }
   }
   foreach (var a in SameAsEnglish) if (!seen.Contains(a[0])) problems.Add("SameAsEnglish lists a text that is not in Lang.Table: " + Quote(a[0]));
   return lines;
  }

  static void CheckHelp(List<string> problems) {
   Func<string, string[]> sections = text => text.Split(new[] { "\n\n" }, StringSplitOptions.None);
   if (Lang.HelpText.Length != Lang.Supported.Length) { problems.Add("Lang.HelpText needs one text per language"); return; }
   for (int c = 0; c < Lang.HelpText.Length; c++) {
    string lang = Lang.Supported[c], text = Lang.HelpText[c];
    if (string.IsNullOrWhiteSpace(text)) { problems.Add("Lang.HelpText has no " + lang + " text"); continue; }
    if (c == 0) continue;
    if (sections(text).Length != sections(Lang.HelpText[0]).Length) problems.Add("Lang.HelpText " + lang + " has " + sections(text).Length + " sections instead of " + sections(Lang.HelpText[0]).Length);
    if (text == Lang.HelpText[0]) problems.Add("Lang.HelpText " + lang + " is still English");
    string stray = LeftOverEnglish(text);
    if (stray != null) problems.Add("Lang.HelpText " + lang + " still has English words (" + stray + ")");
   }
  }

  // ---- C# sources ----

  static List<Token> Tokenize(string code) {
   var tokens = new List<Token>();
   int i = 0, line = 1;
   while (i < code.Length) {
    char c = code[i];
    char next = i + 1 < code.Length ? code[i + 1] : '\0';
    if (c == '\n') { line++; i++; }
    else if (char.IsWhiteSpace(c)) i++;
    else if (c == '/' && next == '/') { while (i < code.Length && code[i] != '\n') i++; }
    else if (c == '/' && next == '*') {
     int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
     end = end < 0 ? code.Length : end + 2;
     for (; i < end; i++) if (code[i] == '\n') line++;
    }
    else if (c == '@' && next == '"') {
     var text = new StringBuilder();
     int start = line;
     i += 2;
     while (i < code.Length) {
      if (code[i] == '"') {
       if (i + 1 < code.Length && code[i + 1] == '"') { text.Append('"'); i += 2; continue; }
       break;
      }
      if (code[i] == '\n') line++;
      text.Append(code[i++]);
     }
     i++;
     tokens.Add(new Token { Kind = "str", Text = text.ToString(), Line = start });
    }
    else if (c == '"') {
     var text = new StringBuilder();
     i++;
     while (i < code.Length && code[i] != '"') {
      if (code[i] == '\\') {
       char e = code[i + 1];
       if (e == 'u') { text.Append((char)Convert.ToInt32(code.Substring(i + 2, 4), 16)); i += 6; continue; }
       text.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == '0' ? '\0' : e);
       i += 2;
      }
      else text.Append(code[i++]);
     }
     i++;
     tokens.Add(new Token { Kind = "str", Text = text.ToString(), Line = line });
    }
    else if (c == '\'') {
     i++;
     while (i < code.Length && code[i] != '\'') i += code[i] == '\\' ? 2 : 1;
     i++;
     tokens.Add(new Token { Kind = "chr", Text = "'", Line = line });
    }
    else if (char.IsLetter(c) || c == '_') {
     int start = i;
     while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
     tokens.Add(new Token { Kind = "id", Text = code.Substring(start, i - start), Line = line });
    }
    else if (char.IsDigit(c)) {
     int start = i;
     while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '.')) i++;
     tokens.Add(new Token { Kind = "num", Text = code.Substring(start, i - start), Line = line });
    }
    else { tokens.Add(new Token { Kind = "p", Text = c.ToString(), Line = line }); i++; }
   }
   return tokens;
  }

  static bool Open(Token t) { return t.Kind == "p" && (t.Text == "(" || t.Text == "[" || t.Text == "{"); }
  static bool Shut(Token t) { return t.Kind == "p" && (t.Text == ")" || t.Text == "]" || t.Text == "}"); }

  static int Close(List<Token> t, int open) {
   int depth = 0;
   for (int i = open; i < t.Count; i++) {
    if (Open(t[i])) depth++;
    else if (Shut(t[i]) && --depth == 0) return i;
   }
   return t.Count - 1;
  }

  // The tokens in [from, to) split at the commas that are not inside brackets.
  static List<List<Token>> Split(List<Token> t, int from, int to) {
   var parts = new List<List<Token>>();
   var current = new List<Token>();
   int depth = 0;
   for (int i = from; i < to; i++) {
    if (Open(t[i])) depth++;
    else if (Shut(t[i])) depth--;
    if (depth == 0 && t[i].Kind == "p" && t[i].Text == ",") { parts.Add(current); current = new List<Token>(); }
    else current.Add(t[i]);
   }
   if (current.Count > 0 || parts.Count > 0) parts.Add(current);
   return parts;
  }

  // Index of the argument that holds the English text when t[i] starts a call to a translation helper, else -1.
  static int TextArgument(List<Token> t, int i) {
   if (t[i].Kind != "id" || i + 1 >= t.Count || t[i + 1].Text != "(") return -1;
   Token before = i > 0 ? t[i - 1] : null;
   if (t[i].Text == "T") return before != null && before.Text == "." && i > 1 && t[i - 2].Text == "Lang" ? 0 : -1;
   if (!Helpers.Contains(t[i].Text)) return -1;
   if (before != null && (before.Text == "." || (before.Kind == "id" && before.Text != "return" && before.Text != "else"))) return -1;
   return t[i].Text == "Step" ? 1 : 0;
  }

  // const strings, so that a call can pass a named constant.
  static Dictionary<string, string> Constants(List<Source> sources) {
   var found = new Dictionary<string, string>();
   foreach (var s in sources) {
    var t = s.Tokens;
    for (int i = 2; i + 3 < t.Count; i++) {
     if (!(t[i].Text == "string" && t[i + 1].Kind == "id" && t[i + 2].Text == "=" && t[i + 3].Kind == "str")) continue;
     bool constant = false;
     for (int back = 1; back <= 3 && i - back >= 0; back++) if (t[i - back].Text == "const" || t[i - back].Text == "readonly") constant = true;
     if (!constant) continue;
     var value = new StringBuilder();
     for (int j = i + 3; j < t.Count && (t[j].Kind == "str" || t[j].Text == "+"); j++) if (t[j].Kind == "str") value.Append(t[j].Text);
     found[t[i + 1].Text] = value.ToString();
    }
   }
   return found;
  }

  static List<Token> Part(List<Token> e, int from, int to) { return e.Skip(from).Take(to - from).ToList(); }

  // Index of the top-level token with this text, or -1.
  static int TopLevel(List<Token> e, string text) {
   int depth = 0;
   for (int i = 0; i < e.Count; i++) {
    if (Open(e[i])) depth++;
    else if (Shut(e[i])) depth--;
    else if (depth == 0 && e[i].Kind == "p" && e[i].Text == text) {
     bool doubled = (i > 0 && e[i - 1].Text == text) || (i + 1 < e.Count && e[i + 1].Text == text);
     if (!doubled) return i;
    }
   }
   return -1;
  }

  // Splits an expression into the texts it can produce: both sides of a ?:, literals, concatenated literals and named constants.
  // Anything else comes back as a Piece that is not a literal.
  static void Pieces(List<Token> e, Dictionary<string, string> consts, List<Piece> output) {
   if (e.Count == 0) return;
   int question = TopLevel(e, "?");
   if (question > 0) {
    int depth = 0, nested = 0, colon = -1;
    for (int i = question + 1; i < e.Count && colon < 0; i++) {
     if (Open(e[i])) depth++;
     else if (Shut(e[i])) depth--;
     else if (depth == 0 && e[i].Text == "?") nested++;
     else if (depth == 0 && e[i].Text == ":") { if (nested == 0) colon = i; else nested--; }
    }
    if (colon > 0) { Pieces(Part(e, question + 1, colon), consts, output); Pieces(Part(e, colon + 1, e.Count), consts, output); return; }
   }
   if (Open(e[0]) && e[0].Text == "(" && Close(e, 0) == e.Count - 1) { Pieces(Part(e, 1, e.Count - 1), consts, output); return; }
   if (e[0].Kind == "str" && e.All(x => x.Kind == "str" || x.Text == "+")) {
    output.Add(new Piece { Line = e[0].Line, Literal = true, Value = string.Concat(e.Where(x => x.Kind == "str").Select(x => x.Text).ToArray()) });
    return;
   }
   if (e.All(x => x.Kind == "id" || x.Text == ".") && consts.ContainsKey(e[e.Count - 1].Text)) {
    output.Add(new Piece { Line = e[0].Line, Literal = true, Value = consts[e[e.Count - 1].Text] });
    return;
   }
   output.Add(new Piece { Line = e[0].Line, Literal = false, Value = string.Concat(e.Select(x => x.Text).ToArray()) });
  }

  static int Letters(string text) { return text.Count(char.IsLetter); }

  // Checks the string literals of tokens [from, to) that are shown to the user without going through a translation call.
  static void Direct(string root, Source s, int from, int to, HashSet<string> known, List<string> problems, string what) {
   var t = s.Tokens;
   for (int j = from; j < to; j++) {
    if (TextArgument(t, j) >= 0) { j = Close(t, j + 1); continue; }
    if (t[j].Kind != "str" || Letters(t[j].Text) < 2 || known.Contains(t[j].Text) || Units.Contains(t[j].Text)) continue;
    problems.Add(Where(root, s.Path, t[j].Line) + ": " + what + " shows a text that is not translated (use Tr, or add it to Lang.Neutral or Units if it is a name or a unit): " + Quote(t[j].Text));
   }
  }

  // The end of the value assigned from t[start], at the next top-level , or ; or the bracket that closes the initializer.
  static int ValueEnd(List<Token> t, int start) {
   int depth = 0;
   for (int j = start; j < t.Count; j++) {
    if (Open(t[j])) depth++;
    else if (Shut(t[j]) && --depth < 0) return j;
    else if (depth == 0 && t[j].Kind == "p" && (t[j].Text == "," || t[j].Text == ";")) return j;
   }
   return t.Count;
  }

  // Returns how many translated calls were checked.
  static int CheckCode(string root, List<Source> sources, HashSet<string> known, HashSet<string> used, List<string> problems) {
   var consts = Constants(sources);
   var table = new HashSet<string>(Lang.Table.Select(r => r[0]));
   int checkedCalls = 0;
   foreach (var s in sources) {
    var t = s.Tokens;
    foreach (var token in t) if (token.Kind == "str") used.Add(token.Text);
    for (int i = 0; i < t.Count; i++) {
     int argument = TextArgument(t, i);
     if (argument >= 0) {
      var args = Split(t, i + 2, Close(t, i + 1));
      string call = t[i].Text == "T" ? "Lang.T" : t[i].Text;
      if (args.Count <= argument) { problems.Add(Where(root, s.Path, t[i].Line) + ": " + call + " has no text argument"); continue; }
      int extra = args.Count - argument - 1;
      var pieces = new List<Piece>();
      Pieces(args[argument], consts, pieces);
      foreach (var piece in pieces) {
       string place = Where(root, s.Path, piece.Line);
       if (!piece.Literal) {
        if (!Computed.Contains(s.Name + ": " + piece.Value)) problems.Add(place + ": " + call + " must get a text literal so it can be checked, found the expression " + piece.Value + " (if its values are checked elsewhere, list it in LanguageTests.Computed)");
        continue;
       }
       if (piece.Value.Length == 0) continue;
       checkedCalls++;
       if (!known.Contains(piece.Value)) { problems.Add(place + ": " + call + " text is missing from Lang.Table and Lang.Neutral: " + Quote(piece.Value)); continue; }
       if (call == "Step") continue; // Step's other arguments are a color, not format arguments.
       int holes = HoleCount(piece.Value);
       if (holes > 0 && extra == 0) problems.Add(place + ": " + call + " text has placeholders but no arguments: " + Quote(piece.Value));
       else if (extra > 1 && holes > extra) problems.Add(place + ": " + call + " text needs " + holes + " arguments and gets " + extra + ": " + Quote(piece.Value));
       else if (extra > 0 && holes == 0) problems.Add(place + ": " + call + " passes arguments to a text without placeholders: " + Quote(piece.Value));
      }
      continue;
     }
     // "Text = ..." and "Text += ...", but not "Text == ..." or "Text != ...".
     int value = i + 3 < t.Count && t[i].Kind == "id" && TextProperties.Contains(t[i].Text) ? (t[i + 1].Text == "=" && t[i + 2].Text != "=" ? i + 2 : t[i + 1].Text == "+" && t[i + 2].Text == "=" ? i + 3 : -1) : -1;
     if (value > 0) Direct(root, s, value, ValueEnd(t, value), known, problems, t[i].Text);
     else if (t[i].Text == "Show" && i > 1 && t[i - 1].Text == "." && t[i - 2].Text == "MessageBox" && i + 1 < t.Count && t[i + 1].Text == "(") Direct(root, s, i + 2, Close(t, i + 1), known, problems, "MessageBox");
     else if (t[i].Text == "SetName" && i + 1 < t.Count && t[i + 1].Text == "(") Direct(root, s, i + 2, Close(t, i + 1), known, problems, "AutomationProperties name");
    }
   }
   return checkedCalls;
  }

  // ---- XAML ----

  // Returns how many texts were checked. The Translator only handles these four, so other attributes must hold a neutral text.
  static int CheckXaml(string root, HashSet<string> known, HashSet<string> used, List<string> problems) {
   string file = Path.Combine(root, "src", "Launcher", "Launcher.xaml");
   var document = XDocument.Load(file, LoadOptions.SetLineInfo);
   XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
   var translated = new[] { "Text", "Content", "ToolTip", "AutomationProperties.Name" };
   var untranslated = new[] { "Header", "Title", "AutomationProperties.HelpText" };
   int count = 0;
   Func<XElement, string> describe = e => "<" + e.Name.LocalName + (e.Attribute(x + "Name") != null ? " x:Name=\"" + (string)e.Attribute(x + "Name") + "\"" : "") + ">";
   Func<XElement, int> line = e => ((IXmlLineInfo)e).LineNumber;
   Func<XElement, bool> fromCode = e => Lang.Dynamic.Contains((string)e.Attribute(x + "Name"));
   foreach (var element in document.Descendants()) {
    // Elements named in Lang.Dynamic get their text from code; the Translator skips them and everything inside them.
    if (element.Ancestors().Any(fromCode)) continue;
    bool own = fromCode(element);
    var values = new List<KeyValuePair<string, string>>();
    foreach (var attribute in element.Attributes()) {
     string name = attribute.Name.LocalName;
     if (translated.Contains(name) || untranslated.Contains(name)) values.Add(new KeyValuePair<string, string>(name, attribute.Value));
    }
    if (element.Name.LocalName == "Setter") {
     string property = (string)element.Attribute("Property"), value = (string)element.Attribute("Value");
     if (value != null && (translated.Contains(property) || untranslated.Contains(property))) values.Add(new KeyValuePair<string, string>(property, value));
    }
    string inner = string.Concat(element.Nodes().OfType<XText>().Select(n => n.Value).ToArray()).Trim();
    if (inner.Length > 0 && !element.Name.LocalName.Contains(".") && !Regex.IsMatch(element.Name.LocalName, "Font|Collection|Brush|Color|Thickness|Geometry")) values.Add(new KeyValuePair<string, string>("text", inner));
    foreach (var pair in values) {
     string value = pair.Value;
     if (own && (pair.Key == "Text" || pair.Key == "Content")) continue;
     if (value.Length == 0 || value.StartsWith("{") || Letters(value) < 2) continue;
     used.Add(value);
     count++;
     string place = Where(root, file, line(element)) + " " + describe(element) + " " + pair.Key;
     if (untranslated.Contains(pair.Key)) { if (!Lang.Neutral.Contains(value)) problems.Add(place + ": the Translator does not translate " + pair.Key + ", so it must be a Lang.Neutral text: " + Quote(value)); }
     else if (!known.Contains(value)) problems.Add(place + ": text is missing from Lang.Table and Lang.Neutral: " + Quote(value));
    }
   }
   return count;
  }

  // ---- Texts the code takes from lists ----

  // These reach the screen through a computed Tr call, so their values are read from the lists themselves.
  static void CheckComputed(HashSet<string> known, List<string> problems) {
   Action<string, string> need = (text, source) => { if (!known.Contains(text)) problems.Add(source + ": text is missing from Lang.Table and Lang.Neutral: " + Quote(text)); };
   foreach (string name in ServerRules.SpecialNames) need(name, "ServerRules.SpecialNames");
   foreach (string name in Hardware.TierNames) need(name, "Hardware.TierNames");
   foreach (var option in ServerCommands.Weather) need(option[0], "ServerCommands.Weather");
   foreach (string name in new[] { "System", "Components", "Your game" }) need(name, "Preflight groups");
   var rules = new ServerRules();
   foreach (string name in rules.Shared(true).Concat(rules.Shared(false))) need(name, "ServerRules.Shared");
   foreach (string word in GraphicsMod.LabelWords) need(word, "GraphicsMod.LabelWords");
   foreach (double scale in GraphicsMod.Scales) GraphicsMod.ScaleLabel(scale, text => { need(text, "GraphicsMod.ScaleLabel"); return text; });
  }
 }
}
