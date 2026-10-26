using System;
using System.Collections.Generic;
using System.Linq;

namespace HyruleOnline {
 static class ServerEventsTests {
  static int checks, failures;

  static void Check(bool ok, string message) {
   checks++;
   if (ok) return;
   failures++;
   Console.WriteLine("FAIL: " + message);
  }

  static void Joined(string line, string name, int slot) {
   string n; int s;
   Check(ServerEvents.TryJoined(line, out n, out s) && n == name && s == slot, "joined '" + line + "' -> " + name + "/" + slot);
  }

  static void NotJoined(string line) {
   string n; int s;
   Check(!ServerEvents.TryJoined(line, out n, out s), "not joined: '" + line + "'");
  }

  static void Left(string line, string name) {
   string n;
   Check(ServerEvents.TryLeft(line, out n) && n == name, "left '" + line + "' -> " + name);
  }

  static void NotLeft(string line) {
   string n;
   Check(!ServerEvents.TryLeft(line, out n), "not left: '" + line + "'");
  }

  static void Refused(string line, string name, int code) {
   string n; int c;
   Check(ServerEvents.TryRefused(line, out n, out c) && n == name && c == code, "refused '" + line + "' -> " + name + "/" + code);
  }

  static void NotRefused(string line) {
   string n; int c;
   Check(!ServerEvents.TryRefused(line, out n, out c), "not refused: '" + line + "'");
  }

  static string Join(Roster roster) { return string.Join("|", roster.Names().ToArray()); }

  static int Main() {
   string stamp = "[17:19:00] ";

   // Real lines, with and without the timestamp.
   foreach (string prefix in new[] { "", stamp }) {
    Joined(prefix + "Player Link joined the server. Assigned to player 1.", "Link", 1);
    Joined(prefix + "Player Pedro joined the server. Assigned to player 2.", "Pedro", 2);
    Left(prefix + "Player Pedro disconnected. - Details: Se ha forzado la interrupci\u00f3n de una conexi\u00f3n existente por el host remoto.", "Pedro");
    Left(prefix + "Player Pedro disconnected.", "Pedro");
    Refused(prefix + "Player WrongPw tried to connect but failed with error 3", "WrongPw", ServerEvents.WrongPassword);
   }
   Check(ServerEvents.WrongPassword == 3, "wrong password is error 3");

   // Free-text names.
   Joined("Player Mar\u00eda Jos\u00e9 joined the server. Assigned to player 3.", "Mar\u00eda Jos\u00e9", 3);
   Joined("Player Zelda 2 joined the server. Assigned to player 4.", "Zelda 2", 4);
   Joined("Player a.b-c_d! joined the server. Assigned to player 10.", "a.b-c_d!", 10);
   Joined("Player Player joined the server. Assigned to player 2.", "Player", 2);
   Joined("Player Player Two joined the server. Assigned to player 2.", "Player Two", 2);
   Joined("Player I joined the server joined the server. Assigned to player 5.", "I joined the server", 5);
   Joined("Player joined joined the server. Assigned to player 6.", "joined", 6);
   Left("Player Mar\u00eda Jos\u00e9 disconnected.", "Mar\u00eda Jos\u00e9");
   Left("Player Player disconnected.", "Player");
   Refused("Player Mar\u00eda Jos\u00e9 tried to connect but failed with error 3", "Mar\u00eda Jos\u00e9", 3);
   Joined("Player " + new string('x', 40) + " joined the server. Assigned to player 7.", new string('x', 40), 7);

   // Trailing noise.
   Joined("Player Link joined the server. Assigned to player 1.\r", "Link", 1);
   Joined("Player Link joined the server. Assigned to player 1.  \r\n", "Link", 1);
   Joined("Player Link joined the server. Assigned to player 1", "Link", 1);
   Joined("  [17:19:00]  Player Link joined the server. Assigned to player 1.", "Link", 1);
   Left("Player Pedro disconnected. \r", "Pedro");
   Left("Player Pedro disconnected", "Pedro");
   Left("Player Pedro disconnected. - Details: x\r", "Pedro");
   Refused("Player WrongPw tried to connect but failed with error 3 \r", "WrongPw", 3);
   Refused("Player WrongPw tried to connect but failed with error 3.", "WrongPw", 3);

   // Other error codes.
   Refused("Player Ana tried to connect but failed with error 1", "Ana", 1);
   Refused("Player Ana tried to connect but failed with error 12", "Ana", 12);
   Refused(stamp + "Player Ana tried to connect but failed with error 0", "Ana", 0);

   // Lines that must not match.
   foreach (string line in new[] {
    null, "", "   ", "\r", "Server started", "[17:19:00] Server started",
    "Requested teleport of 1 players to Link", "[17:19:00] Requested teleport of 1 players to Link",
    "Link: Player Pedro joined the server. Assigned to player 2.",
    "Player Link joined the server.", "Player Link joined the server. Assigned to player", "Player Link joined the server. Assigned to player x.",
    "Player  joined the server. Assigned to player 1.", "Player joined the server. Assigned to player 1.",
    "Link joined the server. Assigned to player 1.", "player Link joined the server. Assigned to player 1.",
    "Player Link joined the server. Assigned to player 99999999999999999999.",
    "Player Link tried to connect but failed with error 3", "Player Link disconnected. extra",
    "Error receiving message. Disconnecting player..."
   }) NotJoined(line);
   foreach (string line in new[] {
    null, "", "Server started", "Requested teleport of 1 players to Link", "Player Link joined the server. Assigned to player 1.",
    "Player  disconnected.", "Pedro disconnected.",     "Player Pedro disconnected. extra",    "Player Link tried to connect but failed with error 3", "Error receiving message. Disconnecting player..."
   }) NotLeft(line);
   foreach (string line in new[] {
    null, "", "Server started", "Player Link joined the server. Assigned to player 1.", "Player Pedro disconnected.",
    "Player  tried to connect but failed with error 3", "Player Link tried to connect but failed with error",
    "Player Link tried to connect but failed with error x", "Player Link tried to connect but failed with error 3 now"
   }) NotRefused(line);

   // Hostile names: a refusal or join that imitates a leave stays what it is, and control characters are dropped.
   string forged = "Link disconnected. - Details: x";
   Refused("Player " + forged + " tried to connect but failed with error 3", forged, 3);
   NotLeft("Player " + forged + " tried to connect but failed with error 3");
   NotLeft("Player " + forged + " joined the server. Assigned to player 3.");
   Left("Player " + forged + " disconnected. - Details: reset", forged);
   NotJoined("Player Bo\u202eb joined the server. Assigned to player 4.");
   NotLeft("Player Bo\u202eb disconnected.");
   NotRefused("Player Bo\tb tried to connect but failed with error 3");
   Joined("Player Zelda \U0001F600 joined the server. Assigned to player 4.", "Zelda \U0001F600", 4);
   var spoofed = new Roster();
   spoofed.Apply("Player Link joined the server. Assigned to player 1.");
   spoofed.Apply("Player Link disconnected. - Details: x tried to connect but failed with error 3");
   Check(spoofed.Text() == "Link", "a forged refusal does not remove a player");
   // Roster.
   var roster = new Roster();
   Check(roster.Count == 0 && roster.Text() == "" && roster.Names().Count == 0, "empty roster");
   roster.Left("Nobody");
   Check(roster.Count == 0, "leaving an unknown name on an empty roster is ignored");
   roster.Joined("Link", 1);
   Check(roster.Count == 1 && roster.Text() == "Link", "one player text");
   roster.Joined("Pedro", 3);
   roster.Joined("Ana", 2);
   Check(roster.Count == 3 && roster.Text() == "Link, Ana, Pedro", "three players ordered by slot: " + roster.Text());
   Check(Join(roster) == "Link|Ana|Pedro", "Names ordered by slot");
   roster.Left("Nobody");
   Check(roster.Count == 3, "leaving an unknown name is ignored");
   roster.Left("Ana");
   Check(roster.Count == 2 && roster.Text() == "Link, Pedro", "leaving removes the player");
   roster.Joined("Luis", 3);
   Check(roster.Count == 2 && roster.Text() == "Link, Luis", "same slot replaces the old entry");
   roster.Joined("Luis", 4);
   Check(roster.Count == 3 && roster.Text() == "Link, Luis, Luis", "the same name on another slot is another player");
   roster.Clear();
   Check(roster.Count == 0 && roster.Text() == "", "Clear empties the roster");
   roster.Joined("B", 2);
   roster.Joined("A", 1);
   Check(Join(roster) == "A|B", "slot order beats arrival order");
   roster.Clear();

   // Duplicate names (the defaults are fixed): each slot is a player, and one leave removes one.
   roster.Joined("Sam", 1);
   roster.Joined("Sam", 2);
   Check(roster.Count == 2 && roster.Text() == "Sam, Sam", "duplicate names keep both players");
   roster.Left("Sam");
   Check(roster.Count == 1, "one leave removes one of them");
   roster.Clear();

   // Apply.
   Check(roster.Apply("[17:19:00] Player Link joined the server. Assigned to player 1."), "Apply: join changes the roster");
   Check(roster.Apply("Player Pedro joined the server. Assigned to player 2.\r") && roster.Text() == "Link, Pedro", "Apply: second join with \\r");
   Check(!roster.Apply("Player Pedro joined the server. Assigned to player 2."), "Apply: an identical join changes nothing");
   Check(roster.Apply("Player Pedro joined the server. Assigned to player 3.") && roster.Text() == "Link, Pedro, Pedro", "Apply: a known name on a new slot is another player");
   Check(!roster.Apply("Player Ghost disconnected."), "Apply: an unknown name leaving changes nothing");
   Check(!roster.Apply("Player WrongPw tried to connect but failed with error 3") && roster.Count == 3, "Apply: a refusal changes nothing");
   Check(!roster.Apply("Server started") && !roster.Apply("") && !roster.Apply(null), "Apply: other lines change nothing");
   Check(roster.Apply("Player Pedro disconnected. - Details: gone") && roster.Text() == "Link, Pedro", "Apply: leaving changes the roster");
   Check(roster.Apply("Player Pedro disconnected.") && roster.Text() == "Link", "Apply: the second Pedro leaves");
   Check(roster.Apply("Player Link disconnected.") && roster.Count == 0, "Apply: last player leaves");

   // Players and colours.
   roster.Joined("Pedro", 3);
   roster.Joined("Link", 1);
   var players = roster.Players();
   Check(players.Count == 2 && players[0].Key == 1 && players[0].Value == "Link" && players[1].Key == 3 && players[1].Value == "Pedro", "Players are (slot, name) in slot order");
   Check(Roster.ColorOf(1) == "#7EE9C9" && Roster.ColorOf(2) == "#E5C384", "slots 1 and 2 keep the player 1 and player 2 colours");
   Check(Roster.ColorOf(1 + Roster.Palette.Length) == Roster.ColorOf(1) && Roster.ColorOf(0) == Roster.ColorOf(Roster.Palette.Length), "the colours repeat after the last one and slot 0 does not throw");
   Check(Roster.Palette.Distinct().Count() == Roster.Palette.Length, "every colour in the palette is different");

   Console.WriteLine(failures == 0 ? "PASS: server events (" + checks + " checks)." : "FAIL: " + failures + " of " + checks + " server event checks failed.");
   return failures == 0 ? 0 : 1;
  }
 }
}
