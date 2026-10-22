using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyruleOnline {
 // The launcher text is written in English and looked up by that English text, so a missing translation shows English.
 // Session log lines, script output and the mod's own names are not translated.
 static class Lang {
  public static readonly string[] Supported = { "en", "es", "fr" };
  public static string Current = "en";

  // Elements whose text the launcher sets from code; the XAML walker leaves them alone.
  public static readonly string[] Dynamic = {
   "CornerServer", "P1Status", "P2Status", "P1Name", "P2Name", "ScreenInfo", "LayoutInfo", "Layout", "Assign1", "Assign2",
   "ServerText", "AText", "BText", "Status", "Prompt", "HelpText", "Log", "ServerLog", "LanguageList", "LauncherLanguageList", "TimeBox"
  };

  // Text that reads the same in every language.
  public static readonly string[] Neutral = { "THE LEGEND OF ZELDA", "BREATH OF THE WILD", "LT / RT", "LB", "RB", "Hunter vs Speedrunner", "Death Swap" };

  // English, Spanish, French.
  public static readonly string[][] Table = {
   new[] { "Help", "Ayuda", "Aide" },
   new[] { "ONLINE MULTIPLAYER", "MULTIJUGADOR EN LÍNEA", "MULTIJOUEUR EN LIGNE" },
   new[] { "Previous page (LB or Ctrl+Shift+Tab)", "Página anterior (LB o Ctrl+Mayús+Tab)", "Page précédente (LB ou Ctrl+Maj+Tab)" },
   new[] { "Previous page (LB)", "Página anterior (LB)", "Page précédente (LB)" },
   new[] { "Next page (RB or Ctrl+Tab)", "Página siguiente (RB o Ctrl+Tab)", "Page suivante (RB ou Ctrl+Tab)" },
   new[] { "Next page (RB)", "Página siguiente (RB)", "Page suivante (RB)" },
   new[] { "PLAY", "JUGAR", "JOUER" },
   new[] { "GRAPHICS", "GRÁFICOS", "GRAPHISMES" },
   new[] { "MULTIPLAYER", "MULTIJUGADOR", "MULTIJOUEUR" },
   new[] { "WORLD RULES", "REGLAS DEL MUNDO", "RÈGLES DU MONDE" },
   new[] { "LIVE CONTROLS", "CONTROLES EN VIVO", "COMMANDES EN DIRECT" },
   new[] { "What the two players share. Applied when the next session starts.", "Lo que comparten los dos jugadores. Se aplica cuando inicie la próxima sesión.", "Ce que les deux joueurs partagent. S'applique au prochain démarrage d'une session." },
   new[] { "Available while the server is running.", "Disponible mientras el servidor está en marcha.", "Disponible tant que le serveur fonctionne." },
   new[] { "Preset", "Preajuste", "Préréglage" },
   new[] { "Rules preset", "Preajuste de reglas", "Préréglage des règles" },
   new[] { "Special mode", "Modo especial", "Mode spécial" },
   new[] { "Custom", "Personalizado", "Personnalisé" },
   new[] { "No special mode", "Sin modo especial", "Aucun mode spécial" },
   new[] { "Enemies", "Enemigos", "Ennemis" },
   new[] { "Quests", "Misiones", "Quêtes" },
   new[] { "Koroks", "Koroks", "Korogus" },
   new[] { "Towers", "Torres", "Tours" },
   new[] { "Shrines", "Santuarios", "Sanctuaires" },
   new[] { "Locations", "Lugares", "Lieux" },
   new[] { "Divine Beasts", "Bestias Divinas", "Créatures divines" },
   new[] { "Time of day (24 hour, HH:MM)", "Hora del día (24 h, HH:MM)", "Heure du jour (24 h, HH:MM)" },
   new[] { "Time of day", "Hora del día", "Heure du jour" },
   new[] { "Set time", "Fijar hora", "Régler l'heure" },
   new[] { "Weather", "Clima", "Météo" },
   new[] { "Set weather", "Fijar clima", "Régler la météo" },
   new[] { "Automatic", "Automático", "Automatique" },
   new[] { "Clear sky", "Cielo despejado", "Ciel dégagé" },
   new[] { "Cloudy", "Nublado", "Nuageux" },
   new[] { "Rain", "Lluvia", "Pluie" },
   new[] { "Heavy rain", "Lluvia fuerte", "Forte pluie" },
   new[] { "Snow", "Nieve", "Neige" },
   new[] { "Heavy snow", "Nevada fuerte", "Forte neige" },
   new[] { "Thunderstorm", "Tormenta eléctrica", "Orage" },
   new[] { "Thunder and rain", "Truenos y lluvia", "Orage et pluie" },
   new[] { "Clear sky with rain", "Cielo despejado con lluvia", "Ciel dégagé avec pluie" },
   new[] { "Name tags", "Nombres sobre los jugadores", "Noms au-dessus des joueurs" },
   new[] { "Pause sync", "Pausar sincronización", "Mettre la synchro en pause" },
   new[] { "Resume sync", "Reanudar sincronización", "Reprendre la synchro" },
   new[] { "Player 1 → player 2", "Jugador 1 → jugador 2", "Joueur 1 → joueur 2" },
   new[] { "Player 2 → player 1", "Jugador 2 → jugador 1", "Joueur 2 → joueur 1" },
   new[] { "Teleports player 1 next to player 2", "Teletransporta al jugador 1 junto al jugador 2", "Téléporte le joueur 1 près du joueur 2" },
   new[] { "Teleports player 2 next to player 1", "Teletransporta al jugador 2 junto al jugador 1", "Téléporte le joueur 2 près du joueur 1" },
   new[] { "LANGUAGE", "IDIOMA", "LANGUE" },
   new[] { "DETAILS", "DETALLES", "DÉTAILS" },
   new[] { "CONTROLLERS", "MANDOS", "MANETTES" },
   new[] { "Player 1", "Jugador 1", "Joueur 1" },
   new[] { "Player 2", "Jugador 2", "Joueur 2" },
   new[] { "NAME", "NOMBRE", "NOM" },
   new[] { "MODEL", "MODELO", "MODÈLE" },
   new[] { "Player 1 name", "Nombre del jugador 1", "Nom du joueur 1" },
   new[] { "Player 2 name", "Nombre del jugador 2", "Nom du joueur 2" },
   new[] { "Player 1 model", "Modelo del jugador 1", "Modèle du joueur 1" },
   new[] { "Player 2 model", "Modelo del jugador 2", "Modèle du joueur 2" },
   new[] { "Player 1 motion sensor", "Sensor de movimiento del jugador 1", "Capteur de mouvement du joueur 1" },
   new[] { "Player 2 motion sensor", "Sensor de movimiento del jugador 2", "Capteur de mouvement du joueur 2" },
   new[] { "Test vibration", "Probar vibración", "Tester la vibration" },
   new[] { "⇄  Swap players", "⇄  Intercambiar jugadores", "⇄  Inverser les joueurs" },
   new[] { "DISPLAY", "PANTALLA", "ÉCRAN" },
   new[] { "Primary display · automatic detection", "Pantalla principal · detección automática", "Écran principal · détection automatique" },
   new[] { "P1", "J1", "J1" },
   new[] { "P2", "J2", "J2" },
   new[] { "SESSION", "SESIÓN", "SESSION" },
   new[] { "Dedicated server", "Servidor dedicado", "Serveur dédié" },
   new[] { "Player 1 (Cemu A)", "Jugador 1 (Cemu A)", "Joueur 1 (Cemu A)" },
   new[] { "Player 2 (Cemu B)", "Jugador 2 (Cemu B)", "Joueur 2 (Cemu B)" },
   new[] { "Applied to both games the next time a session starts.", "Se aplica a ambos juegos la próxima vez que inicies una sesión.", "S'applique aux deux jeux au prochain démarrage d'une session." },
   new[] { "Render resolution", "Resolución de renderizado", "Résolution de rendu" },
   new[] { "Shadows", "Sombras", "Ombres" },
   new[] { "Shadow draw distance", "Distancia de sombras", "Distance des ombres" },
   new[] { "Anti-aliasing", "Suavizado de bordes", "Anticrénelage" },
   new[] { "LAUNCHER", "LAUNCHER", "LAUNCHER" },
   new[] { "GAME", "JUEGO", "JEU" },
   new[] { "The launcher text changes right away.", "El texto del launcher cambia al instante.", "Le texte du launcher change immédiatement." },
   new[] { "The game applies it to both players the next time a session starts. Text added by the mod itself may stay in English.", "El juego lo aplica a ambos jugadores la próxima vez que inicies una sesión. El texto que añade el propio mod puede seguir en inglés.", "Le jeu l'applique aux deux joueurs au prochain démarrage d'une session. Le texte ajouté par le mod lui-même peut rester en anglais." },
   new[] { "Session log", "Registro de la sesión", "Journal de session" },
   new[] { "Server console", "Consola del servidor", "Console du serveur" },
   new[] { "Server command, then Enter (type help)", "Comando del servidor y Enter (escribe help)", "Commande du serveur puis Entrée (tapez help)" },
   new[] { "START SESSION", "INICIAR SESIÓN", "DÉMARRER LA SESSION" },
   new[] { "Stop session", "Detener sesión", "Arrêter la session" },
   new[] { "Exit", "Salir", "Quitter" },

   new[] { "Server running", "Servidor activo", "Serveur actif" },
   new[] { "Server stopped", "Servidor detenido", "Serveur arrêté" },
   new[] { "Assign controller", "Asignar mando", "Assigner manette" },
   new[] { "Cancel assignment", "Cancelar asignación", "Annuler l'assignation" },
   new[] { "No controller detected", "No se detecta ningún mando", "Aucune manette détectée" },
   new[] { "Press the bottom face button on a controller to assign it.", "Pulsa el botón inferior de un mando para asignarlo.", "Appuyez sur le bouton du bas d'une manette pour l'assigner." },
   new[] { "Connected", "Conectado", "Connecté" },
   new[] { "Waiting for a controller", "Esperando un mando", "En attente d'une manette" },
   new[] { "Split: automatic", "División: automática", "Division : automatique" },
   new[] { "Split: side by side", "División: lado a lado", "Division : côte à côte" },
   new[] { "Split: top and bottom", "División: arriba y abajo", "Division : haut et bas" },
   new[] { "The primary display is not ready.", "La pantalla principal no está lista.", "L'écran principal n'est pas prêt." },
   new[] { "Side by side · P1 left / P2 right", "Lado a lado · J1 izquierda / J2 derecha", "Côte à côte · J1 gauche / J2 droite" },
   new[] { "Top and bottom · P1 top / P2 bottom", "Arriba y abajo · J1 arriba / J2 abajo", "Haut et bas · J1 haut / J2 bas" },
   new[] { "Each game renders {0} × {1}", "Cada juego se renderiza a {0} × {1}", "Chaque jeu est rendu en {0} × {1}" },
   new[] { "at {0} FPS", "a {0} FPS", "à {0} FPS" },
   new[] { "FPS++ is not installed", "FPS++ no está instalado", "FPS++ n'est pas installé" },
   new[] { "FPS target: Auto · {0} FPS", "Objetivo de FPS: Auto · {0} FPS", "Objectif FPS : Auto · {0} FPS" },
   new[] { "Not available", "No disponible", "Indisponible" },
   new[] { "(window size)", "(tamaño de ventana)", "(taille de la fenêtre)" },
   new[] { "(sharper)", "(más nítido)", "(plus net)" },
   new[] { "(faster)", "(más rápido)", "(plus rapide)" },

   new[] { "Starting...", "Iniciando...", "Démarrage..." },
   new[] { "Stopping...", "Deteniendo...", "Arrêt..." },
   new[] { "Ready.", "Listo.", "Prêt." },
   new[] { "Session running.", "Sesión en marcha.", "Session en cours." },
   new[] { "Start failed: {0}", "No se pudo iniciar: {0}", "Échec du démarrage : {0}" },
   new[] { "The display settings could not be prepared: {0}", "No se pudo preparar la configuración de pantalla: {0}", "Les paramètres d'affichage n'ont pas pu être préparés : {0}" },
   new[] { "The dedicated server could not start: {0}", "El servidor dedicado no pudo iniciarse: {0}", "Le serveur dédié n'a pas pu démarrer : {0}" },
   new[] { "The start script failed.", "El script de inicio falló.", "Le script de démarrage a échoué." },
   new[] { "Cemu A: wait for the main menu, then press Continue within about a minute. Player 2 starts when player 1 has finished loading.", "Cemu A: espera al menú principal y pulsa Continuar en aproximadamente un minuto. El jugador 2 arranca cuando el jugador 1 termina de cargar.", "Cemu A : attendez le menu principal, puis appuyez sur Continuer dans la minute environ. Le joueur 2 démarre quand le joueur 1 a fini de charger." },
   new[] { "Cemu B: wait for the main menu, then press Continue. If player 1 is not in yet, press Continue there too.", "Cemu B: espera al menú principal y pulsa Continuar. Si el jugador 1 aún no ha entrado, pulsa Continuar también allí.", "Cemu B : attendez le menu principal, puis appuyez sur Continuer. Si le joueur 1 n'est pas encore entré, appuyez aussi sur Continuer là-bas." },
   new[] { "One player joined. Press Continue in the other Cemu window.", "Un jugador se ha unido. Pulsa Continuar en la otra ventana de Cemu.", "Un joueur a rejoint. Appuyez sur Continuer dans l'autre fenêtre Cemu." },
   new[] { "Use Stop session to close anything that was started, then try again.", "Usa Detener sesión para cerrar lo que se haya iniciado y vuelve a intentarlo.", "Utilisez Arrêter la session pour fermer ce qui a démarré, puis réessayez." },
   new[] { "Starting", "Iniciando", "Démarrage" },
   new[] { "Waiting", "Esperando", "En attente" },
   new[] { "Running", "En marcha", "En cours" },
   new[] { "Stopped", "Detenido", "Arrêté" },
   new[] { "Waiting for Continue", "Esperando Continuar", "En attente de Continuer" },
   new[] { "Joined the server", "Unido al servidor", "Connecté au serveur" },
   new[] { "Game open", "Juego abierto", "Jeu ouvert" },
   new[] { "A session is still open.\n\nYes: stop the session and close.\nNo: close only the launcher. The game keeps running and the controllers fall back to Windows XInput.\nCancel: keep this window open.",
    "Todavía hay una sesión abierta.\n\nSí: detener la sesión y cerrar.\nNo: cerrar solo el launcher. El juego sigue en marcha y los mandos vuelven a XInput de Windows.\nCancelar: mantener esta ventana abierta.",
    "Une session est encore ouverte.\n\nOui : arrêter la session et fermer.\nNon : fermer uniquement le launcher. Le jeu continue et les manettes repassent sur XInput de Windows.\nAnnuler : garder cette fenêtre ouverte." }
  };

  // The help panel is one long text, so it is kept per language instead of in the table.
  public static readonly string[] HelpText = {
   "SESSION: Start session opens the dedicated server, then Player 1 and Player 2 in their own Cemu windows. When each game reaches the main menu, press Continue in that window within about a minute.\n\nPAGES: LB and RB on any controller (or Ctrl+Tab) move between Play, Graphics, Multiplayer, Language and Details while this launcher is in front.\n\nCONTROLLERS: choose a player, then press the bottom face button on a controller to assign it. Each Cemu window only sees its own controller. Vibration support depends on the controller.\n\nDISPLAY: the two Cemu windows share the primary display. Split chooses side by side or top and bottom; Auto splits wide displays side by side. Windows are placed when they open, so moving one by hand is kept.\n\nGRAPHICS: the choices come from the game's own graphics pack and apply the next time a session starts.\n\nMULTIPLAYER: the world rules choose what the two players share (enemies, quests, koroks, towers, shrines, locations, Divine Beasts) and are used when the server starts. The live controls change the time, weather and name tags, pause sync or bring one player to the other while the server runs.\n\nLANGUAGE: the launcher language changes this launcher right away. The game language is applied to both players the next time a session starts, and the two can differ.\n\nThe preview shows the split layout, not a game screenshot. Use Alt+Tab to come back to this launcher while the game is open. Esc minimizes the launcher while a game is open and closes it otherwise.",
   "SESIÓN: Iniciar sesión abre el servidor dedicado y después al jugador 1 y al jugador 2 en sus propias ventanas de Cemu. Cuando cada juego llegue al menú principal, pulsa Continuar en esa ventana en aproximadamente un minuto.\n\nPÁGINAS: LB y RB en cualquier mando (o Ctrl+Tab) cambian entre Jugar, Gráficos, Multijugador, Idioma y Detalles mientras este launcher esté al frente.\n\nMANDOS: elige un jugador y pulsa el botón inferior de un mando para asignarlo. Cada ventana de Cemu solo ve su propio mando. La vibración depende del mando.\n\nPANTALLA: las dos ventanas de Cemu comparten la pantalla principal. División elige lado a lado o arriba y abajo; Auto divide las pantallas anchas lado a lado. Las ventanas se colocan al abrirse, así que si mueves una a mano se respeta.\n\nGRÁFICOS: las opciones vienen del propio paquete gráfico del juego y se aplican la próxima vez que inicies una sesión.\n\nMULTIJUGADOR: las reglas del mundo eligen lo que comparten los dos jugadores (enemigos, misiones, koroks, torres, santuarios, lugares, Bestias Divinas) y se usan cuando arranca el servidor. Los controles en vivo cambian la hora, el clima y los nombres, pausan la sincronización o llevan a un jugador junto al otro mientras el servidor está en marcha.\n\nIDIOMA: el idioma del launcher cambia este launcher al instante. El idioma del juego se aplica a ambos jugadores la próxima vez que inicies una sesión, y pueden ser distintos.\n\nLa vista previa muestra la división, no una captura del juego. Usa Alt+Tab para volver a este launcher con el juego abierto. Esc minimiza el launcher mientras hay un juego abierto y lo cierra si no.",
   "SESSION : Démarrer la session ouvre le serveur dédié, puis le joueur 1 et le joueur 2 dans leurs propres fenêtres Cemu. Quand chaque jeu arrive au menu principal, appuyez sur Continuer dans cette fenêtre dans la minute environ.\n\nPAGES : LB et RB sur n'importe quelle manette (ou Ctrl+Tab) passent de Jouer à Graphismes, Multijoueur, Langue et Détails tant que ce launcher est au premier plan.\n\nMANETTES : choisissez un joueur, puis appuyez sur le bouton du bas d'une manette pour l'assigner. Chaque fenêtre Cemu ne voit que sa propre manette. La vibration dépend de la manette.\n\nÉCRAN : les deux fenêtres Cemu se partagent l'écran principal. Division choisit côte à côte ou haut et bas ; Auto place les écrans larges côte à côte. Les fenêtres sont placées à leur ouverture, donc un déplacement manuel est conservé.\n\nGRAPHISMES : les choix viennent du pack graphique du jeu et s'appliquent au prochain démarrage d'une session.\n\nMULTIJOUEUR : les règles du monde choisissent ce que les deux joueurs partagent (ennemis, quêtes, korogus, tours, sanctuaires, lieux, Créatures divines) et sont utilisées au démarrage du serveur. Les commandes en direct changent l'heure, la météo et les noms, mettent la synchro en pause ou amènent un joueur près de l'autre pendant que le serveur fonctionne.\n\nLANGUE : la langue du launcher change ce launcher immédiatement. La langue du jeu s'applique aux deux joueurs au prochain démarrage d'une session, et les deux peuvent différer.\n\nL'aperçu montre la division, pas une capture du jeu. Utilisez Alt+Tab pour revenir à ce launcher pendant que le jeu est ouvert. Échap réduit le launcher tant qu'un jeu est ouvert et le ferme sinon."
  };

  static readonly Dictionary<string, string[]> Map = Build();

  static Dictionary<string, string[]> Build() {
   var map = new Dictionary<string, string[]>();
   foreach (var row in Table) map[row[0]] = row;
   return map;
  }

  // The launcher is translated into some languages only; any other value shows English.
  public static string Ui(string code) {
   return Supported.Contains(code) ? code : "en";
  }

  static int Column { get { int i = Array.IndexOf(Supported, Current); return i < 0 ? 0 : i; } }

  public static string T(string english, params object[] args) {
   string[] row;
   string text = english;
   if (Column != 0 && english != null && Map.TryGetValue(english, out row) && !string.IsNullOrEmpty(row[Column])) text = row[Column];
   return args == null || args.Length == 0 ? text : string.Format(text, args);
  }

  public static string Help() { return HelpText[Column]; }
 }

 sealed class GameLanguage {
  public string Code, Name;
  // The value Cemu stores in <console_language>.
  public int Console;
 }

 // A BOTW dump carries one Bootup_<region><language>.pack for each language it can show, so the available languages are read from the game.
 static class GameLanguages {
  static readonly Dictionary<string, int> Consoles = new Dictionary<string, int> {
   { "ja", 0 }, { "en", 1 }, { "fr", 2 }, { "de", 3 }, { "it", 4 }, { "es", 5 }, { "zh", 6 }, { "ko", 7 }, { "nl", 8 }, { "pt", 9 }, { "ru", 10 }
  };
  const int TraditionalChinese = 11;

  public static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
   { "en", "English" }, { "es", "Español" }, { "fr", "Français" }, { "de", "Deutsch" }, { "it", "Italiano" }, { "ja", "日本語" },
   { "zh", "中文" }, { "ko", "한국어" }, { "nl", "Nederlands" }, { "pt", "Português" }, { "ru", "Русский" }
  };

  // The same language differs by region in a few releases.
  static readonly Dictionary<string, string> RegionNames = new Dictionary<string, string> {
   { "USes", "Español (Latinoamérica)" }, { "USfr", "Français (Canada)" }, { "EUes", "Español (España)" }, { "TWzh", "中文 (繁體)" }
  };

  public static GameLanguage English { get { return new GameLanguage { Code = "en", Name = "English", Console = 1 }; } }

  // English first, then the others by code. Without a readable game folder only English is offered.
  public static List<GameLanguage> Scan(string contentFolder) {
   var found = new List<GameLanguage>();
   try {
    string pack = contentFolder == null ? null : Path.Combine(contentFolder, "Pack");
    if (pack != null && Directory.Exists(pack)) {
     foreach (string file in Directory.GetFiles(pack, "Bootup_*.pack")) {
      var match = Regex.Match(Path.GetFileName(file), "^Bootup_([A-Z]{2})([a-z]{2})\\.pack$");
      if (!match.Success) continue;
      string region = match.Groups[1].Value, code = match.Groups[2].Value;
      if (found.Any(l => l.Code == code) || !Consoles.ContainsKey(code)) continue;
      string name;
      if (!RegionNames.TryGetValue(region + code, out name) && !Names.TryGetValue(code, out name)) name = code;
      found.Add(new GameLanguage { Code = code, Name = name, Console = region == "TW" && code == "zh" ? TraditionalChinese : Consoles[code] });
     }
    }
   }
   catch (Exception) { found.Clear(); }
   if (found.Count == 0) found.Add(English);
   return found.OrderBy(l => l.Code == "en" ? 0 : 1).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
  }

  // BCML keeps the game's content folder in its settings.json as game_dir.
  public static string ContentFolder(string bcmlSettings) {
   try {
    if (!File.Exists(bcmlSettings)) return null;
    var match = Regex.Match(File.ReadAllText(bcmlSettings), "\"game_dir\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
    if (!match.Success) return null;
    string path = Regex.Replace(match.Groups[1].Value, "\\\\(.)", "$1");
    return path.Length == 0 ? null : path;
   }
   catch (Exception) { return null; }
  }

  public static GameLanguage Find(IList<GameLanguage> all, string code) {
   return all.FirstOrDefault(l => l.Code == code) ?? all[0];
  }
 }
}
