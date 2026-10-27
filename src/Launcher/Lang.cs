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
   "CornerServer", "P1Status", "P2Status", "P1Name", "P2Name", "P1AvatarName", "P2AvatarName", "P1PickerName", "P2PickerName", "ScreenInfo", "LayoutInfo", "Layout", "Arrange", "Assign1", "Assign2",
   "ServerText", "AText", "BText", "Status", "Prompt", "HelpText", "Log", "ServerLog", "LanguageList", "LauncherLanguageList", "TimeBox", "GraphicsHint", "MultiplayerHint", "Start", "PresetInfo", "PresetShares", "SpecialInfo", "PadHint1", "PadHint2", "PadLayoutInfo", "StartText", "Mode", "SetupProgressText", "SetupNotes", "OnlineModeInfo", "FirewallInfo", "OnlineStatusText", "PlayerEvent", "NetworkHintText"
  };

  // Text that reads the same in every language.
  public static readonly string[] Neutral = { "Hyrule Online", "THE LEGEND OF ZELDA", "BREATH OF THE WILD", "LT / RT", "LB", "RB", "Hunter vs Speedrunner", "Death Swap", "NVIDIA FXAA", ".NET Framework 4.8", "Start + LT", "Start + LT + RT", "L3 + R3", "VPN", "Local" };

  // English, Spanish, French.
  public static readonly string[][] Table = {
   new[] { "Help", "Ayuda", "Aide" },
   new[] { "System", "Sistema", "Système" },
   new[] { "Components", "Componentes", "Composants" },
   new[] { "Your game", "Tu juego", "Votre jeu" },
   new[] { "Ready", "Listo", "Prêt" },
   new[] { "Missing", "Falta", "Manquant" },
   new[] { "Needs repair", "Necesita reparación", "À réparer" },
   new[] { "FIRST-TIME SETUP", "PRIMERA CONFIGURACIÓN", "PREMIÈRE CONFIGURATION" },
   new[] { "Some things this launcher needs are not here yet. Download what can be downloaded, then point it at your own copy of the game.", "Faltan algunas cosas que necesita este lanzador. Descarga lo que se pueda descargar y luego indícale tu propia copia del juego.", "Certains éléments nécessaires à ce lanceur manquent encore. Téléchargez ce qui peut l'être, puis indiquez-lui votre propre copie du jeu." },
   new[] { "Download what is missing", "Descargar lo que falta", "Télécharger ce qui manque" },
   new[] { "Cancel", "Cancelar", "Annuler" },
   new[] { "Locate my game files", "Buscar mis archivos del juego", "Localiser mes fichiers de jeu" },
   new[] { "Check again", "Comprobar de nuevo", "Vérifier à nouveau" },
   new[] { "Checking your install...", "Comprobando tu instalación...", "Vérification de votre installation..." },
   new[] { "Starting the download...", "Iniciando la descarga...", "Démarrage du téléchargement..." },
   new[] { "The download failed: {0}", "La descarga ha fallado: {0}", "Le téléchargement a échoué : {0}" },
   new[] { "Downloading and installing {0}", "Descargando e instalando {0}", "Téléchargement et installation de {0}" },
   new[] { "Download cancelled.", "Descarga cancelada.", "Téléchargement annulé." },
   new[] { "Looking through that folder...", "Buscando en esa carpeta...", "Recherche dans ce dossier..." },
   new[] { "Select the folder with your game files. A Cemu folder, or a folder with your game, update and DLC.", "Elige la carpeta con tus archivos del juego. Una carpeta de Cemu, o una carpeta con tu juego, la actualización y el DLC.", "Choisissez le dossier contenant vos fichiers de jeu. Un dossier Cemu, ou un dossier avec votre jeu, la mise à jour et le DLC." },
   new[] { "Windows 10 or newer", "Windows 10 o posterior", "Windows 10 ou plus récent" },
   new[] { "Windows build {0}", "Compilación de Windows {0}", "Version de Windows {0}" },
   new[] { "This launcher needs Windows 10 version 1809 or newer.", "Este lanzador necesita Windows 10 versión 1809 o posterior.", "Ce lanceur nécessite Windows 10 version 1809 ou plus récente." },
   new[] { "Release {0}", "Versión {0}", "Version {0}" },
   new[] { "Install the .NET Framework 4.8 runtime from Microsoft.", "Instala el entorno .NET Framework 4.8 de Microsoft.", "Installez le runtime .NET Framework 4.8 de Microsoft." },
   new[] { "Visual C++ runtime", "Runtime de Visual C++", "Runtime Visual C++" },
   new[] { "Needed by Cemu and Python", "Lo necesitan Cemu y Python", "Requis par Cemu et Python" },
   new[] { "Install the Microsoft Visual C++ 2015-2022 x64 Redistributable (aka.ms/vs/17/release/vc_redist.x64.exe).", "Instala el paquete redistribuible Microsoft Visual C++ 2015-2022 x64 (aka.ms/vs/17/release/vc_redist.x64.exe).", "Installez le package redistribuable Microsoft Visual C++ 2015-2022 x64 (aka.ms/vs/17/release/vc_redist.x64.exe)." },
   new[] { "Vulkan graphics", "Gráficos Vulkan", "Graphismes Vulkan" },
   new[] { "Both players render with Vulkan", "Los dos jugadores se renderizan con Vulkan", "Les deux joueurs utilisent Vulkan" },
   new[] { "Install the latest driver for your graphics card.", "Instala el controlador más reciente de tu tarjeta gráfica.", "Installez le dernier pilote de votre carte graphique." },
   new[] { "Free disk space", "Espacio libre en disco", "Espace disque libre" },
   new[] { "{0} GB free, {1} GB needed", "{0} GB libres, se necesitan {1} GB", "{0} Go libres, {1} Go nécessaires" },
   new[] { "Nothing to download", "Nada que descargar", "Rien à télécharger" },
   new[] { "Free some disk space or install Hyrule Online on another drive.", "Libera espacio en disco o instala Hyrule Online en otra unidad.", "Libérez de l'espace disque ou installez Hyrule Online sur un autre disque." },
   new[] { "Install folder", "Carpeta de instalación", "Dossier d'installation" },
   new[] { "{0} is missing", "Falta {0}", "{0} est absent" },
   new[] { "{0} is version {1}, {2} is needed", "{0} es la versión {1}, se necesita la {2}", "{0} est en version {1}, la {2} est requise" },
   new[] { "{0} is not the official file", "{0} no es el archivo oficial", "{0} n'est pas le fichier officiel" },
   new[] { "Use Download what is missing.", "Usa Descargar lo que falta.", "Utilisez Télécharger ce qui manque." },
   new[] { "Controller router", "Enrutador de mandos", "Routeur de manettes" },
   new[] { "Reinstall Hyrule Online, or run scripts/Build.ps1 when working from source.", "Reinstala Hyrule Online o ejecuta scripts/Build.ps1 si trabajas desde el código fuente.", "Réinstallez Hyrule Online, ou lancez scripts/Build.ps1 si vous travaillez depuis les sources." },
   new[] { "Multiplayer mod merged in BCML", "Mod multijugador fusionado en BCML", "Mod multijoueur fusionné dans BCML" },
   new[] { "It is merged on its own once everything else is ready. Or run scripts/Merge-Mod.ps1.", "Se fusiona solo cuando todo lo demás está listo. O ejecuta scripts/Merge-Mod.ps1.", "Il est fusionné tout seul quand le reste est prêt. Ou lancez scripts/Merge-Mod.ps1." },
   new[] { "Setting up {0}", "Preparando {0}", "Préparation de {0}" },
   new[] { "Merging the multiplayer mod...", "Fusionando el mod multijugador...", "Fusion du mod multijoueur..." },
   new[] { "The mod merge failed: {0}", "La fusión del mod ha fallado: {0}", "La fusion du mod a échoué : {0}" },
   new[] { "{0} (base game)", "{0} (juego base)", "{0} (jeu de base)" },
   new[] { "Game folder known to BCML", "Carpeta del juego conocida por BCML", "Dossier du jeu connu de BCML" },
   new[] { "Use Locate my game files and pick the folder with your game dump.", "Usa Buscar mis archivos del juego y elige la carpeta con tu copia del juego.", "Utilisez Localiser mes fichiers de jeu et choisissez le dossier contenant votre copie du jeu." },
   new[] { "Game update v{0}", "Actualización del juego v{0}", "Mise à jour du jeu v{0}" },
   new[] { "DLC v{0}", "DLC v{0}", "DLC v{0}" },
   new[] { "Save game to start from", "Partida guardada de la que partir", "Sauvegarde de départ" },
   new[] { "Optional. Without one, local multiplayer unlocks after your first save in single player", "Opcional. Sin una, el multijugador local se desbloquea tras tu primer guardado en un jugador", "Facultatif. Sans sauvegarde, le multijoueur local se débloque après votre première sauvegarde en solo" },
   new[] { "Use Locate my game files and pick a Cemu folder that has your save (user/80000001), or start a new single player game.", "Usa Buscar mis archivos del juego y elige una carpeta de Cemu que tenga tu guardado (user/80000001), o empieza una partida nueva en un jugador.", "Utilisez Localiser mes fichiers de jeu et choisissez un dossier Cemu contenant votre sauvegarde (user/80000001), ou commencez une nouvelle partie en solo." },
   new[] { "A new game on an empty save hangs in Cemu", "Una partida nueva con un guardado vacío se cuelga en Cemu", "Une nouvelle partie sur une sauvegarde vide se fige dans Cemu" },
   new[] { "Use Locate my game files and pick a Cemu folder that has your save (user/80000001).", "Usa Buscar mis archivos del juego y elige una carpeta de Cemu que tenga tu guardado (user/80000001).", "Utilisez Localiser mes fichiers de jeu et choisissez un dossier Cemu contenant votre sauvegarde (user/80000001)." },
   new[] { "First game: no mod and no server yet. Start a new game, let it save, then close Cemu; local multiplayer unlocks by itself.", "Primera partida: aún sin mod ni servidor. Empieza una partida nueva, deja que guarde y cierra Cemu; el multijugador local se desbloquea solo.", "Première partie : pas encore de mod ni de serveur. Commencez une nouvelle partie, laissez-la sauvegarder puis fermez Cemu ; le multijoueur local se débloque tout seul." },
   new[] { "The game files were not found. Open Check again on the setup page.", "No se encontraron los archivos del juego. Pulsa Comprobar de nuevo en la página de configuración.", "Les fichiers du jeu sont introuvables. Utilisez Vérifier à nouveau sur la page de configuration." },
   new[] { "Cemu A could not be prepared: {0}", "No se pudo preparar Cemu A: {0}", "Cemu A n'a pas pu être préparé : {0}" },
   new[] { "Local multiplayer needs a save", "El multijugador local necesita un guardado", "Le multijoueur local nécessite une sauvegarde" },
   new[] { "Local multiplayer unlocks after your first save. Play single player and start a new game; once the game has saved, it unlocks by itself.", "El multijugador local se desbloquea tras tu primer guardado. Juega en un jugador y empieza una partida nueva; cuando el juego guarde, se desbloquea solo.", "Le multijoueur local se débloque après votre première sauvegarde. Jouez en solo et commencez une nouvelle partie ; dès que le jeu a sauvegardé, il se débloque tout seul." },
   new[] { "Your save was found, so local multiplayer is unlocked.", "Se encontró tu guardado, así que el multijugador local está desbloqueado.", "Votre sauvegarde a été trouvée : le multijoueur local est débloqué." },
   new[] { "Not found for {0}", "No se encontró para {0}", "Introuvable pour {0}" },
   new[] { "{0} has version {1}, version {2} is needed", "{0} tiene la versión {1}, se necesita la {2}", "{0} est en version {1}, la version {2} est requise" },
   new[] { "Use Locate my game files and pick the folder with the right version.", "Usa Buscar mis archivos del juego y elige la carpeta con la versión correcta.", "Utilisez Localiser mes fichiers de jeu et choisissez le dossier avec la bonne version." },
   new[] { "Linked into both Cemu folders", "Enlazado en las dos carpetas de Cemu", "Lié dans les deux dossiers Cemu" },
   new[] { "That folder does not exist.", "Esa carpeta no existe.", "Ce dossier n'existe pas." },
   new[] { "That folder has too many subfolders. Pick a smaller one.", "Esa carpeta tiene demasiadas subcarpetas. Elige una más pequeña.", "Ce dossier contient trop de sous-dossiers. Choisissez-en un plus petit." },
   new[] { "The base game (code/U-King.rpx with its content) was not found.", "No se encontró el juego base (code/U-King.rpx con su contenido).", "Le jeu de base (code/U-King.rpx avec son contenu) est introuvable." },
   new[] { "The game update v{0} was not found.", "No se encontró la actualización v{0} del juego.", "La mise à jour v{0} du jeu est introuvable." },
   new[] { "The DLC v{0} was not found.", "No se encontró el DLC v{0}.", "Le DLC v{0} est introuvable." },
   new[] { "The game update at {0} is v{1}, v{2} is needed.", "La actualización en {0} es la v{1}, se necesita la v{2}.", "La mise à jour dans {0} est en v{1}, la v{2} est requise." },
   new[] { "The DLC at {0} is v{1}, v{2} is needed.", "El DLC en {0} es la v{1}, se necesita la v{2}.", "Le DLC dans {0} est en v{1}, la v{2} est requise." },
   new[] { "The game, update and DLC must all be found first.", "Primero hay que encontrar el juego, la actualización y el DLC.", "Le jeu, la mise à jour et le DLC doivent d'abord être trouvés." },
   new[] { "{0} is ready.", "{0} está listo.", "{0} est prêt." },
   new[] { "{0} could not be set up: {1}", "No se pudo preparar {0}: {1}", "{0} n'a pas pu être préparé : {1}" },
   new[] { "Linked {0} to {1}.", "Se enlazó {0} con {1}.", "{0} a été lié à {1}." },
   new[] { "Copied your save into {0}.", "Se copió tu guardado en {0}.", "Votre sauvegarde a été copiée dans {0}." },
   new[] { "ONLINE MULTIPLAYER", "MULTIJUGADOR EN LÍNEA", "MULTIJOUEUR EN LIGNE" },
   new[] { "Previous page (LB or Ctrl+Shift+Tab)", "Página anterior (LB o Ctrl+Mayús+Tab)", "Page précédente (LB ou Ctrl+Maj+Tab)" },
   new[] { "Previous page (LB)", "Página anterior (LB)", "Page précédente (LB)" },
   new[] { "Next page (RB or Ctrl+Tab)", "Página siguiente (RB o Ctrl+Tab)", "Page suivante (RB ou Ctrl+Tab)" },
   new[] { "Next page (RB)", "Página siguiente (RB)", "Page suivante (RB)" },
   new[] { "PLAY", "JUGAR", "JOUER" },
   new[] { "GRAPHICS", "GRÁFICOS", "GRAPHISMES" },
   new[] { "WORLD", "MUNDO", "MONDE" },
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
   new[] { "Koroks", "Kologs", "Korogus" },
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
   new[] { "Heavy snow", "Nevada fuerte", "Neige abondante" },
   new[] { "Thunderstorm", "Tormenta eléctrica", "Orage" },
   new[] { "Thunder and rain", "Truenos y lluvia", "Tonnerre et pluie" },
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
   new[] { "Player 1", "Jugador 1", "Joueur 1" },
   new[] { "Player 2", "Jugador 2", "Joueur 2" },
   new[] { "NAME", "NOMBRE", "NOM" },
   new[] { "AVATAR", "AVATAR", "AVATAR" },
   new[] { "Player 1 name", "Nombre del jugador 1", "Nom du joueur 1" },
   new[] { "Player 2 name", "Nombre del jugador 2", "Nom du joueur 2" },
   new[] { "Player 1 avatar", "Avatar del jugador 1", "Avatar du joueur 1" },
   new[] { "Player 2 avatar", "Avatar del jugador 2", "Avatar du joueur 2" },
   new[] { "Choose the avatar for player 1", "Elige el avatar del jugador 1", "Choisissez l'avatar du joueur 1" },
   new[] { "Choose the avatar for player 2", "Elige el avatar del jugador 2", "Choisissez l'avatar du joueur 2" },
   new[] { "Choose your avatar", "Elige tu avatar", "Choisissez votre avatar" },
   new[] { "D-pad: move   {0}: select   {1}: back", "Cruceta: mover   {0}: elegir   {1}: volver", "Croix : déplacer   {0} : choisir   {1} : retour" },
   new[] { "Player 1 motion sensor", "Sensor de movimiento del jugador 1", "Capteur de mouvement du joueur 1" },
   new[] { "Player 2 motion sensor", "Sensor de movimiento del jugador 2", "Capteur de mouvement du joueur 2" },
   new[] { "Test vibration", "Probar vibración", "Tester la vibration" },
   new[] { "⇄  Swap players", "⇄  Intercambiar jugadores", "⇄  Inverser les joueurs" },
   new[] { "DISPLAY", "PANTALLA", "ÉCRAN" },
   new[] { "P1", "J1", "J1" },
   new[] { "P2", "J2", "J2" },
   new[] { "SESSION", "SESIÓN", "SESSION" },
   new[] { "Dedicated server", "Servidor dedicado", "Serveur dédié" },
   new[] { "Player 1 (Cemu A)", "Jugador 1 (Cemu A)", "Joueur 1 (Cemu A)" },
   new[] { "Player 2 (Cemu B)", "Jugador 2 (Cemu B)", "Joueur 2 (Cemu B)" },
   new[] { "Applies at the next session start.", "Se aplica al iniciar la próxima sesión.", "S'applique au prochain démarrage d'une session." },
   new[] { "Render resolution", "Resolución de renderizado", "Résolution de rendu" },
   new[] { "Shadows", "Sombras", "Ombres" },
   new[] { "Shadow draw distance", "Distancia de sombras", "Distance des ombres" },
   new[] { "Anti-aliasing", "Suavizado de bordes", "Anticrénelage" },
   new[] { "ADVANCED (CEMU)", "AVANZADO (CEMU)", "AVANCÉ (CEMU)" },
   new[] { "MENU CONTROLS", "CONTROL DE MENÚS", "COMMANDES DES MENUS" },
   new[] { "Controller button layout", "Distribución de botones del mando", "Disposition des boutons de la manette" },
   new[] { "Which button confirms and which one goes back while a controller moves through this launcher. Auto follows each controller: Nintendo pads confirm with the right button, Xbox, PlayStation and Steam Input pads with the bottom one. It does not change the controls inside the game.", "Qué botón confirma y cuál vuelve atrás cuando un mando se mueve por este launcher. Auto sigue a cada mando: los de Nintendo confirman con el botón derecho; los de Xbox, PlayStation y Steam Input, con el de abajo. No cambia los controles dentro del juego.", "Quel bouton valide et lequel revient en arrière quand une manette parcourt ce launcher. Auto suit chaque manette : les manettes Nintendo valident avec le bouton de droite ; Xbox, PlayStation et Steam Input avec celui du bas. Cela ne change pas les commandes dans le jeu." },
   new[] { "Nintendo (A right, B bottom)", "Nintendo (A a la derecha, B abajo)", "Nintendo (A à droite, B en bas)" },
   new[] { "Xbox (A bottom, B right)", "Xbox (A abajo, B a la derecha)", "Xbox (A en bas, B à droite)" },
   new[] { "Auto (match each controller)", "Auto (según cada mando)", "Auto (selon chaque manette)" },
   new[] { "PlayStation ({0} bottom, {1} right)", "PlayStation ({0} abajo, {1} a la derecha)", "PlayStation ({0} en bas, {1} à droite)" },
   new[] { "Detected: {0}", "Detectado: {0}", "Détecté : {0}" },
  new[] { "Open this menu from a game", "Abrir este menú desde un juego", "Ouvrir ce menu depuis un jeu" },
  new[] { "Hold these controller buttons for 1.5 seconds while a game is in front to bring this launcher over it. Hold them again, or press the back button here, to return to the game. Pick buttons the game does not need together, or turn it off.",
   "Mantén estos botones del mando durante 1,5 segundos con un juego delante para traer este launcher encima. Vuelve a mantenerlos, o pulsa el botón de volver aquí, para regresar al juego. Elige botones que el juego no use a la vez, o desactívalo.",
   "Maintenez ces boutons de la manette pendant 1,5 seconde quand un jeu est au premier plan pour afficher ce launcher par-dessus. Maintenez-les à nouveau, ou appuyez sur le bouton retour ici, pour revenir au jeu. Choisissez des boutons que le jeu n'utilise pas ensemble, ou désactivez-le." },
  new[] { "Change shortcut", "Cambiar atajo", "Changer le raccourci" },
  new[] { "Off", "Desactivado", "Désactivé" },
  new[] { "Return to game", "Volver al juego", "Retourner au jeu" },
  new[] { "Hold {0} for 1.5 seconds to bring this menu over the game. Hold it again, or press {1}, to go back.", "Mantén {0} durante 1,5 segundos para traer este menú sobre el juego. Vuelve a mantenerlo, o pulsa {1}, para regresar.", "Maintenez {0} pendant 1,5 seconde pour afficher ce menu par-dessus le jeu. Maintenez-le à nouveau, ou appuyez sur {1}, pour revenir." },
  new[] { "The menu shortcut is off.", "El atajo del menú está desactivado.", "Le raccourci du menu est désactivé." },
  new[] { "Hold the new shortcut on a controller (2 to 4 inputs), then let go. Tap {0} alone, or press nothing for 10 seconds, to cancel.", "Mantén el nuevo atajo en un mando (de 2 a 4 botones) y suéltalo. Pulsa {0} solo, o no pulses nada durante 10 segundos, para cancelar.", "Maintenez le nouveau raccourci sur une manette (2 à 4 touches), puis relâchez. Appuyez seul sur {0}, ou n'appuyez sur rien pendant 10 secondes, pour annuler." },
  new[] { "That combination cannot be used: pick 2 to 4 inputs, and not both small centre buttons.", "Esa combinación no sirve: elige de 2 a 4 botones, y no los dos botones pequeños del centro a la vez.", "Cette combinaison est impossible : choisissez 2 à 4 touches, pas les deux petits boutons centraux ensemble." },
  new[] { "Shortcut saved: {0}.", "Atajo guardado: {0}.", "Raccourci enregistré : {0}." },
   new[] { "No controller detected: menus use the {0} layout.", "Ningún mando detectado: los menús usan la distribución {0}.", "Aucune manette détectée : les menus utilisent la disposition {0}." },
   new[] { "How many pixels each game renders. Higher is sharper but needs a stronger graphics card.", "Cuántos píxeles renderiza cada juego. Más es más nítido, pero exige una tarjeta gráfica más potente.", "Nombre de pixels rendus par chaque jeu. Plus c'est élevé, plus c'est net, mais il faut une carte graphique plus puissante." },
   new[] { "Quality of the shadows in the game.", "Calidad de las sombras del juego.", "Qualité des ombres dans le jeu." },
   new[] { "How far from the player shadows are drawn.", "A qué distancia del jugador se dibujan las sombras.", "Jusqu'où les ombres sont dessinées autour du joueur." },
   new[] { "Smooths jagged edges.", "Suaviza los bordes dentados.", "Adoucit les bords crénelés." },
   new[] { "The defaults suit most PCs.", "Los valores por defecto sirven para la mayoría de PC.", "Les valeurs par défaut conviennent à la plupart des PC." },
   new[] { "Graphics API", "API gráfica", "API graphique" },
   new[] { "The graphics backend Cemu uses. Vulkan is usually the better choice on modern graphics cards; OpenGL is the older backend, useful if Vulkan crashes or glitches on this PC. Auto picks Vulkan when this PC supports it and OpenGL otherwise. After changing it, the first start can take longer while shaders compile again.", "El motor gráfico que usa Cemu. Vulkan suele ser mejor en tarjetas gráficas modernas; OpenGL es el motor antiguo y sirve si Vulkan se cuelga o da fallos en este PC. Auto elige Vulkan si este PC lo admite y OpenGL en caso contrario. Tras cambiarla, el primer inicio puede tardar más mientras se recompilan los shaders.", "Le moteur graphique utilisé par Cemu. Vulkan est en général le meilleur choix sur les cartes graphiques récentes ; OpenGL est l'ancien moteur, utile si Vulkan plante ou provoque des défauts sur ce PC. Auto choisit Vulkan si ce PC le gère, sinon OpenGL. Après le changement, le premier démarrage peut être plus long pendant la recompilation des shaders." },
   new[] { "Upscale filter", "Filtro de ampliación", "Filtre d'agrandissement" },
   new[] { "How Cemu stretches the picture when the window is larger than the game's resolution. Bilinear is the softest and fastest; Bicubic and Bicubic Hermite look sharper at a small cost; Nearest neighbor keeps hard pixel edges.", "Cómo estira Cemu la imagen cuando la ventana es mayor que la resolución del juego. Bilinear es el más suave y rápido; Bicubic y Bicubic Hermite se ven más nítidos con un pequeño coste; Nearest neighbor mantiene los bordes duros de los píxeles.", "La façon dont Cemu étire l'image quand la fenêtre est plus grande que la résolution du jeu. Bilinear est le plus doux et le plus rapide ; Bicubic et Bicubic Hermite sont plus nets pour un petit coût ; Nearest neighbor garde des bords de pixels francs." },
   new[] { "Downscale filter", "Filtro de reducción", "Filtre de réduction" },
   new[] { "How Cemu shrinks the picture when the window is smaller than the game's resolution, which happens with a render resolution above 100%. Bilinear suits almost everyone; the others are slower and rarely worth it.", "Cómo reduce Cemu la imagen cuando la ventana es menor que la resolución del juego, algo que ocurre con una resolución de renderizado superior al 100%. Bilinear sirve para casi todos; los demás son más lentos y rara vez compensan.", "La façon dont Cemu réduit l'image quand la fenêtre est plus petite que la résolution du jeu, ce qui arrive avec une résolution de rendu supérieure à 100 %. Bilinear convient à presque tout le monde ; les autres sont plus lents et valent rarement la peine." },
   new[] { "VSync", "VSync", "VSync" },
   new[] { "Waits for the display refresh to avoid screen tearing. It can cap the frame rate at the display's refresh rate and adds a little input delay. Off suits most setups.", "Espera al refresco de la pantalla para evitar cortes en la imagen. Puede limitar los FPS a la frecuencia de la pantalla y añade un poco de retardo. Desactivado sirve para la mayoría.", "Attend le rafraîchissement de l'écran pour éviter le déchirement. Peut limiter les FPS à la fréquence de l'écran et ajoute un peu de latence. Désactivé convient à la plupart des configurations." },
   new[] { "Asynchronous shader compile", "Compilación asíncrona de shaders", "Compilation asynchrone des shaders" },
   new[] { "Compiles shaders in the background instead of pausing the game, so there is less stutter when something new appears for the first time. The price: objects may be missing for a moment. Recommended on.", "Compila los shaders en segundo plano en lugar de pausar el juego, así hay menos tirones cuando algo nuevo aparece por primera vez. El precio: pueden faltar objetos un momento. Se recomienda activado.", "Compile les shaders en arrière-plan au lieu de mettre le jeu en pause, ce qui limite les saccades quand quelque chose de nouveau apparaît pour la première fois. Le prix : des objets peuvent manquer un instant. Activé recommandé." },
   new[] { "Accurate barriers (Vulkan)", "Barreras precisas (Vulkan)", "Barrières précises (Vulkan)" },
   new[] { "Vulkan only. Fixes rare graphical glitches on some graphics cards at a small cost in speed. Leave it off unless you see flickering or missing effects.", "Solo Vulkan. Corrige fallos gráficos poco comunes en algunas tarjetas a cambio de algo de rendimiento. Déjalo desactivado salvo que veas parpadeos o efectos que faltan.", "Vulkan uniquement. Corrige de rares défauts d'affichage sur certaines cartes pour un petit coût en vitesse. Laissez-le désactivé sauf si vous voyez du scintillement ou des effets manquants." },
   new[] { "Show FPS", "Mostrar FPS", "Afficher les FPS" },
   new[] { "Shows Cemu's frame counter in a corner of each game window. It only changes what is drawn on screen, not how the game runs. Applied at the next session start.", "Muestra el contador de FPS de Cemu en una esquina de cada ventana de juego. Solo cambia lo que se dibuja en pantalla, no cómo funciona el juego. Se aplica al iniciar la próxima sesión.", "Affiche le compteur de FPS de Cemu dans un coin de chaque fenêtre de jeu. Cela ne change que l'affichage, pas le fonctionnement du jeu. S'applique au prochain démarrage d'une session." },
   new[] { "Show CPU, RAM and VRAM usage", "Mostrar uso de CPU, RAM y VRAM", "Afficher l'utilisation du CPU, de la RAM et de la VRAM" },
   new[] { "Adds Cemu's CPU, RAM and VRAM readouts to the counter in each game window, to see how much two games ask of this PC. Applied at the next session start.", "Añade las lecturas de CPU, RAM y VRAM de Cemu al contador de cada ventana de juego, para ver cuánto le piden dos juegos a este PC. Se aplica al iniciar la próxima sesión.", "Ajoute les mesures de CPU, de RAM et de VRAM de Cemu au compteur de chaque fenêtre de jeu, pour voir ce que deux jeux demandent à ce PC. S'applique au prochain démarrage d'une session." },
   new[] { "Master volume", "Volumen general", "Volume général" },
   new[] { "The sound level of every game window, for one or two players. Muted silences the games. Applied at the next session start.", "El nivel de sonido de todas las ventanas de juego, con uno o dos jugadores. Silenciado apaga los juegos. Se aplica al iniciar la próxima sesión.", "Le niveau sonore de toutes les fenêtres de jeu, à un ou deux joueurs. Muet coupe le son des jeux. S'applique au prochain démarrage d'une session." },
   new[] { "Muted", "Silenciado", "Muet" },
   new[] { "Show the \"compiling shaders\" notice", "Mostrar el aviso \"compilando shaders\"", "Afficher l'avis « compilation des shaders »" },
   new[] { "Shows Cemu's \"compiling shaders\" message in each game window while new shaders build. Turn it off for a cleaner picture; the shaders still compile. Applied at the next session start.", "Muestra el mensaje \"compilando shaders\" de Cemu en cada ventana de juego mientras se crean shaders nuevos. Desactívalo para una imagen más limpia; los shaders se compilan igual. Se aplica al iniciar la próxima sesión.", "Affiche le message « compilation des shaders » de Cemu dans chaque fenêtre de jeu pendant la création de nouveaux shaders. Désactivez-le pour une image plus nette ; les shaders sont compilés quand même. S'applique au prochain démarrage d'une session." },
   new[] { "Example: Time Set 18:30 (sets the time of day). Type help to list every command.", "Ejemplo: Time Set 18:30 (fija la hora del día). Escribe help para ver todos los comandos.", "Exemple : Time Set 18:30 (règle l'heure du jour). Tapez help pour lister toutes les commandes." },
   new[] { "Share shader cache between players (experimental)", "Compartir la caché de shaders entre jugadores (experimental)", "Partager le cache de shaders entre les joueurs (expérimental)" },
   new[] { "Experimental. When the second player's shader cache is missing or empty, it is copied once from the first player's when a session starts, so it needs no separate copy. Nothing is shared while the games run, and afterwards each player keeps an independent cache. If anything fails, the games use their own caches and the reason is written to shader-cache.log. Off by default.", "Experimental. Cuando falta la caché de shaders del segundo jugador o está vacía, se copia una sola vez desde la del primero al iniciar una sesión, así no necesita una copia aparte. No se comparte nada mientras los juegos funcionan, y después cada jugador conserva una caché independiente. Si algo falla, los juegos usan su propia caché y el motivo se escribe en shader-cache.log. Desactivado por defecto.", "Expérimental. Quand le cache de shaders du deuxième joueur est absent ou vide, il est copié une seule fois depuis celui du premier au démarrage d'une session, il n'a donc pas besoin d'une copie séparée. Rien n'est partagé pendant que les jeux tournent, et ensuite chaque joueur garde un cache indépendant. En cas de problème, les jeux utilisent leur propre cache et la raison est écrite dans shader-cache.log. Désactivé par défaut." },
   new[] { "Auto ({0})", "Auto ({0})", "Auto ({0})" },
   new[] { "Hover an option, or move the focus to it, to see what it does.", "Pasa el ratón por una opción, o ponle el foco, para ver qué hace.", "Survolez une option, ou placez le focus dessus, pour voir son effet." },
   new[] { "A ready-made set of sync rules. You can still change each rule below.", "Un conjunto de reglas de sincronización ya preparado. Aún puedes cambiar cada regla abajo.", "Un ensemble de règles de synchro prêt à l'emploi. Vous pouvez encore modifier chaque règle ci-dessous." },
   new[] { "An optional game mode on top of the rules: Hunter vs Speedrunner or Death Swap.", "Un modo de juego opcional sobre las reglas: Hunter vs Speedrunner o Death Swap.", "Un mode de jeu optionnel en plus des règles : Hunter vs Speedrunner ou Death Swap." },
   new[] { "Keeps enemies in sync: what one player defeats is defeated for both.", "Sincroniza los enemigos: lo que derrota un jugador queda derrotado para ambos.", "Synchronise les ennemis : ce qu'un joueur bat l'est pour les deux." },
   new[] { "Shares quest progress between the players.", "Comparte el progreso de las misiones entre los jugadores.", "Partage la progression des quêtes entre les joueurs." },
   new[] { "Shares the Korok seeds found by each player.", "Comparte las semillas Kolog que encuentra cada jugador.", "Partage les graines Korogu trouvées par chaque joueur." },
   new[] { "Shares activated towers and the map they reveal.", "Comparte las torres activadas y el mapa que revelan.", "Partage les tours activées et la carte qu'elles révèlent." },
   new[] { "Shares the shrines either player has completed.", "Comparte los santuarios que complete cualquiera de los jugadores.", "Partage les sanctuaires terminés par l'un ou l'autre joueur." },
   new[] { "Shares the map locations either player has discovered.", "Comparte los lugares del mapa que descubra cualquiera de los jugadores.", "Partage les lieux de la carte découverts par l'un ou l'autre joueur." },
   new[] { "Shares Divine Beast progress between the players.", "Comparte el progreso de las Bestias Divinas entre los jugadores.", "Partage la progression des Créatures divines entre les joueurs." },
   new[] { "The time to set in the game, written as 24 hour HH:MM.", "La hora que se fijará en el juego, en formato de 24 h HH:MM.", "L'heure à régler dans le jeu, au format 24 h HH:MM." },
   new[] { "Changes the time of day for every player.", "Cambia la hora del día para todos los jugadores.", "Change l'heure de la journée pour tous les joueurs." },
   new[] { "The weather to apply with the button next to it.", "El clima que se aplica con el botón de al lado.", "La météo appliquée par le bouton à côté." },
   new[] { "Changes the weather for every player.", "Cambia el clima para todos los jugadores.", "Change la météo pour tous les joueurs." },
   new[] { "Shows or hides the names above the other players' heads.", "Muestra u oculta los nombres sobre la cabeza de los demás jugadores.", "Affiche ou masque les noms au-dessus de la tête des autres joueurs." },
   new[] { "Stops the shared rules from syncing for now, so the players can explore apart.", "Detiene por ahora la sincronización de las reglas, para que los jugadores exploren por separado.", "Arrête pour l'instant la synchro des règles, pour que les joueurs explorent séparément." },
   new[] { "Starts syncing the shared rules again.", "Vuelve a sincronizar las reglas compartidas.", "Reprend la synchronisation des règles partagées." },
   new[] { "LAUNCHER", "LAUNCHER", "LAUNCHER" },
   new[] { "GAME", "JUEGO", "JEU" },
   new[] { "The launcher text changes right away.", "El texto del launcher cambia al instante.", "Le texte du launcher change immédiatement." },
   new[] { "The game applies it to both players the next time a session starts. Text added by the mod itself may stay in English.", "El juego lo aplica a ambos jugadores la próxima vez que inicies una sesión. El texto que añade el propio mod puede seguir en inglés.", "Le jeu l'applique aux deux joueurs au prochain démarrage d'une session. Le texte ajouté par le mod lui-même peut rester en anglais." },
   new[] { "Session log", "Registro de la sesión", "Journal de session" },
   new[] { "Server console", "Consola del servidor", "Console du serveur" },
   new[] { "Server command, then Enter (type help)", "Comando del servidor y Enter (escribe help)", "Commande du serveur puis Entrée (tapez help)" },
   new[] { "START SESSION", "INICIAR SESIÓN", "DÉMARRER LA SESSION" },
   new[] { "RESTART SESSION", "REINICIAR SESIÓN", "REDÉMARRER LA SESSION" },
   new[] { "Some changes only apply after a restart. Press Restart session to apply them.", "Algunos cambios solo se aplican tras reiniciar. Pulsa Reiniciar sesión para aplicarlos.", "Certains changements ne s'appliquent qu'après un redémarrage. Appuyez sur Redémarrer la session pour les appliquer." },
   new[] { "Stop session", "Detener sesión", "Arrêter la session" },
   new[] { "Exit", "Salir", "Quitter" },
   new[] { "ONLINE", "EN LÍNEA", "EN LIGNE" },
   new[] { "How this PC takes part. Applied when the next session starts.", "Cómo participa este PC. Se aplica al iniciar la próxima sesión.", "Comment ce PC participe. S'applique au démarrage de la prochaine session." },
   new[] { "Online mode", "Modo en línea", "Mode en ligne" },
   new[] { "Play on this PC keeps the server private. Host a server lets friends join over the network. Join a server connects to a friend's server.", "Jugar en este PC mantiene el servidor privado. Alojar un servidor deja que tus amigos entren por la red. Unirse a un servidor te conecta al de un amigo.", "Jouer sur ce PC garde le serveur privé. Héberger un serveur permet à vos amis de vous rejoindre par le réseau. Rejoindre un serveur vous connecte à celui d'un ami." },
   new[] { "Play on this PC", "Jugar en este PC", "Jouer sur ce PC" },
   new[] { "Host a server", "Alojar un servidor", "Héberger un serveur" },
   new[] { "Join a server", "Unirse a un servidor", "Rejoindre un serveur" },
   new[] { "The server only accepts players from this PC.", "El servidor solo acepta jugadores de este PC.", "Le serveur n'accepte que les joueurs de ce PC." },
   new[] { "Friends who run this launcher can join this PC. The world rules are the ones on the World page.", "Los amigos que usen este launcher pueden unirse a este PC. Las reglas del mundo son las de la página Mundo.", "Les amis qui utilisent ce launcher peuvent rejoindre ce PC. Les règles du monde sont celles de la page Monde." },
   new[] { "One or two players on this PC join a friend's server. The friend sets the rules.", "Uno o dos jugadores de este PC entran en el servidor de un amigo. Tu amigo decide las reglas.", "Un ou deux joueurs de ce PC rejoignent le serveur d'un ami. Votre ami fixe les règles." },
   new[] { "Port", "Puerto", "Port" },
   new[] { "The TCP port the server listens on, from 1024 to 65535. It goes in the invite. The default is 5050.", "El puerto TCP en el que escucha el servidor, de 1024 a 65535. Va en la invitación. Por defecto es 5050.", "Le port TCP sur lequel le serveur écoute, de 1024 à 65535. Il figure dans l'invitation. Par défaut : 5050." },
   new[] { "Server port", "Puerto del servidor", "Port du serveur" },
   new[] { "Password", "Contraseña", "Mot de passe" },
   new[] { "Optional. Friends need it to join. Leave it empty for an open server.", "Opcional. Tus amigos la necesitan para entrar. Déjala vacía para un servidor abierto.", "Facultatif. Vos amis en ont besoin pour entrer. Laissez vide pour un serveur ouvert." },
   new[] { "Server password", "Contraseña del servidor", "Mot de passe du serveur" },
   new[] { "Server description", "Descripción del servidor", "Description du serveur" },
   new[] { "The text friends see about this server.", "El texto que ven tus amigos sobre este servidor.", "Le texte que vos amis voient pour ce serveur." },
   new[] { "Invite or address", "Invitación o dirección", "Invitation ou adresse" },
   new[] { "Paste the invite your friend sent you, or type their address and port.", "Pega la invitación que te envió tu amigo, o escribe su dirección y puerto.", "Collez l'invitation envoyée par votre ami, ou saisissez son adresse et son port." },
   new[] { "Example: hyrule://100.64.0.5:5050 or 192.168.1.20:5050", "Ejemplo: hyrule://100.64.0.5:5050 o 192.168.1.20:5050", "Exemple : hyrule://100.64.0.5:5050 ou 192.168.1.20:5050" },
   new[] { "The server password, if the invite does not carry it.", "La contraseña del servidor, si la invitación no la incluye.", "Le mot de passe du serveur, si l'invitation ne le contient pas." },
   new[] { "Server password to join", "Contraseña del servidor para entrar", "Mot de passe du serveur pour entrer" },
   new[] { "INVITE FRIENDS", "INVITAR AMIGOS", "INVITER DES AMIS" },
   new[] { "Address in the invite", "Dirección de la invitación", "Adresse de l'invitation" },
   new[] { "The Tailscale or home network address of this PC. Use your public address if you forward the port on your router. Empty it to detect the address again.", "La dirección de este PC en Tailscale o en tu red local. Usa tu dirección pública si redirigiste el puerto en el router. Vacíala para detectarla de nuevo.", "L'adresse de ce PC sur Tailscale ou sur votre réseau local. Utilisez votre adresse publique si vous avez redirigé le port sur le routeur. Videz le champ pour la détecter à nouveau." },
   new[] { "Invite", "Invitación", "Invitation" },
   new[] { "Check the invite, the password, that the host has a session running, and that its firewall allows the port.", "Revisa la invitación, la contraseña, que el anfitrión tenga una sesión abierta y que su cortafuegos permita el puerto.", "Vérifiez l'invitation, le mot de passe, que l'hôte a une session ouverte et que son pare-feu autorise le port." },
   new[] { "Copy invite",  "Copiar invitación", "Copier l'invitation" },
   new[] { "Copies the invite. Your friends paste it in the Join a server mode of their launcher.", "Copia la invitación. Tus amigos la pegan en el modo Unirse a un servidor de su launcher.", "Copie l'invitation. Vos amis la collent dans le mode Rejoindre un serveur de leur launcher." },
   new[] { "Allow through firewall", "Permitir en el cortafuegos", "Autoriser dans le pare-feu" },
   new[] { "Adds a Windows Firewall rule for the server port. Windows asks for administrator permission.", "Añade una regla al cortafuegos de Windows para el puerto del servidor. Windows pide permiso de administrador.", "Ajoute une règle au pare-feu Windows pour le port du serveur. Windows demande l'autorisation de l'administrateur." },
   new[] { "Windows Firewall already allows port {0}.", "El cortafuegos de Windows ya permite el puerto {0}.", "Le pare-feu Windows autorise déjà le port {0}." },
   new[] { "Windows Firewall may block port {0}. Allow it so friends can connect.", "El cortafuegos de Windows puede bloquear el puerto {0}. Permítelo para que tus amigos se conecten.", "Le pare-feu Windows peut bloquer le port {0}. Autorisez-le pour que vos amis se connectent." },
   new[] { "You joined a friend's server: the host sets the world rules, so these are locked.", "Te uniste al servidor de un amigo: el anfitrión fija las reglas del mundo, así que estas están bloqueadas.", "Vous avez rejoint le serveur d'un ami : l'hôte fixe les règles du monde, elles sont donc verrouillées." },
   new[] { "The invite was copied.", "Se copió la invitación.", "L'invitation a été copiée." },
   new[] { "This is a {0} address. Friends on the same {0} network can join from anywhere.", "Esta es una dirección de {0}. Los amigos en la misma red de {0} pueden entrar desde cualquier sitio.", "Ceci est une adresse {0}. Les amis sur le même réseau {0} peuvent rejoindre de n'importe où." },
   new[] { "This is a home network address, so only people on the same network can join. For anyone else, install a VPN such as Tailscale on both PCs, or forward the port on your router and use your public address.", "Esta es una dirección de red local, así que solo entran las personas de la misma red. Para el resto, instala una VPN como Tailscale en ambos PC, o redirige el puerto en tu router y usa tu dirección pública.", "Ceci est une adresse de réseau local : seules les personnes du même réseau peuvent rejoindre. Pour les autres, installez un VPN comme Tailscale sur les deux PC, ou redirigez le port sur votre routeur et utilisez votre adresse publique." },
   new[] { "No network address was found. Connect this PC to a network, or type an address.", "No se encontró ninguna dirección de red. Conecta este PC a una red, o escribe una dirección.", "Aucune adresse réseau n'a été trouvée. Connectez ce PC à un réseau, ou saisissez une adresse." },
   new[] { "Friends can join from anywhere if the port is forwarded on your router.", "Los amigos pueden entrar desde cualquier sitio si el puerto está redirigido en tu router.", "Les amis peuvent rejoindre de n'importe où si le port est redirigé sur votre routeur." },
   new[] { "{0} could not join: wrong password.", "{0} no pudo entrar: contraseña incorrecta.", "{0} n'a pas pu entrer : mot de passe incorrect." },
   new[] { "{0} could not join (error {1}).", "{0} no pudo entrar (error {1}).", "{0} n'a pas pu entrer (erreur {1})." },
   new[] { "{0} joined.", "{0} entró.", "{0} a rejoint la partie." },
   new[] { "{0} left.", "{0} salió.", "{0} est parti." },
   new[] { "Server open at {0}:{1}.", "Servidor abierto en {0}:{1}.", "Serveur ouvert à {0}:{1}." },
   new[] { "Friends will join at {0}:{1} once the session starts.", "Los amigos entrarán en {0}:{1} cuando empiece la sesión.", "Les amis rejoindront à {0}:{1} dès que la session démarre." },
   new[] { "PLAYERS", "JUGADORES", "JOUEURS" },
   new[] { "Nobody has joined yet.", "Todavía no ha entrado nadie.", "Personne n'a encore rejoint." },
   new[] { "The server stopped. Press Restart session to bring it back.", "El servidor se detuvo. Pulsa Reiniciar sesión para recuperarlo.", "Le serveur s'est arrêté. Appuyez sur Redémarrer la session pour le relancer." },
   new[] { "The server stopped.", "El servidor se detuvo.", "Le serveur s'est arrêté." },
   new[] { "The server stopped while starting (exit code {0}).", "El servidor se detuvo al arrancar (código de salida {0}).", "Le serveur s'est arrêté au démarrage (code de sortie {0})." },
   new[] { "That address is not valid, so the previous one was kept. Use letters, digits, dots and dashes only.", "Esa dirección no es válida, así que se mantuvo la anterior. Usa solo letras, dígitos, puntos y guiones.", "Cette adresse n'est pas valide, l'ancienne a donc été conservée. Utilisez uniquement des lettres, des chiffres, des points et des tirets." },
   new[] { "JOINING A SERVER", "UNIRSE A UN SERVIDOR", "REJOINDRE UN SERVEUR" },
   new[] { "SERVERS FOUND", "SERVIDORES ENCONTRADOS", "SERVEURS TROUVÉS" },
   new[] { "Search", "Buscar", "Rechercher" },
   new[] { "Test", "Probar", "Tester" },
   new[] { "Looks for servers on your VPN (Tailscale) and on your local network.", "Busca servidores en tu VPN (Tailscale) y en tu red local.", "Cherche des serveurs sur votre VPN (Tailscale) et sur votre réseau local." },
   new[] { "Checks that this address answers, without joining.", "Comprueba que esta dirección responde, sin unirse.", "Vérifie que cette adresse répond, sans rejoindre." },
   new[] { "Searching your VPN and local network…", "Buscando en tu VPN y en la red local…", "Recherche sur votre VPN et votre réseau local…" },
   new[] { "Servers found: {0}.", "Servidores encontrados: {0}.", "Serveurs trouvés : {0}." },
   new[] { "No servers found. Check that the host has a session running, or type the address.", "No se encontró ningún servidor. Comprueba que el anfitrión tenga una sesión abierta, o escribe la dirección.", "Aucun serveur trouvé. Vérifiez que l'hôte a une session ouverte, ou saisissez l'adresse." },
   new[] { "Tailscale was not found or is not connected on this device.", "Tailscale no se encontró o no está conectado en este dispositivo.", "Tailscale est introuvable ou n'est pas connecté sur cet appareil." },
   new[] { "Reachable in {0} ms.", "Responde en {0} ms.", "Joignable en {0} ms." },
   new[] { "Cannot reach {0}:{1}. Check the address, that the host has a session running, and its firewall.", "No se puede llegar a {0}:{1}. Comprueba la dirección, que el anfitrión tenga una sesión abierta y su firewall.", "Impossible de joindre {0}:{1}. Vérifiez l'adresse, que l'hôte a une session ouverte, et son pare-feu." },
   new[] { "Ask the host for their invite and paste it here. You play on this PC with one or two players. The host chooses the world rules and runs the server.", "Pide su invitación al anfitrión y pégala aquí. Juegas en este PC con uno o dos jugadores. El anfitrión elige las reglas del mundo y ejecuta el servidor.", "Demandez son invitation à l'hôte et collez-la ici. Vous jouez sur ce PC avec un ou deux joueurs. L'hôte choisit les règles du monde et lance le serveur." },
   new[] { "Remote", "Remoto", "Distant" },
   new[] { "The server address is not valid. Enter an invite link or host:port on the Online page.", "La dirección del servidor no es válida. Escribe un enlace de invitación o host:puerto en la página En línea.", "L'adresse du serveur n'est pas valide. Saisissez un lien d'invitation ou hôte:port sur la page En ligne." },

   new[] { "Server running", "Servidor activo", "Serveur actif" },
   new[] { "Server stopped", "Servidor detenido", "Serveur arrêté" },
   new[] { "Assign controller", "Asignar mando", "Assigner la manette" },
   new[] { "Cancel assignment", "Cancelar asignación", "Annuler l'assignation" },
   new[] { "No controller detected", "No se detecta ningún mando", "Aucune manette détectée" },
   new[] { "Press the confirm button on a controller to assign it.", "Pulsa el botón de confirmar en un mando para asignarlo.", "Appuyez sur le bouton de validation d'une manette pour l'assigner." },
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
   new[] { "Auto · {0} FPS", "Auto · {0} FPS", "Auto · {0} FPS" },
   new[] { "FPS target", "Objetivo de FPS", "Objectif FPS" },
   new[] { "Displays: split the primary display", "Pantallas: dividir la principal", "Écrans : diviser l'écran principal" },
   new[] { "Displays: one per player", "Pantallas: una por jugador", "Écrans : un par joueur" },
   new[] { "Display map", "Mapa de pantallas", "Carte des écrans" },
   new[] { "Display {0}", "Pantalla {0}", "Écran {0}" },
   new[] { "primary", "principal", "principal" },
   new[] { "Player {0}: {1} × {2}", "Jugador {0}: {1} × {2}", "Joueur {0} : {1} × {2}" },
   new[] { "Player {0}: {1} × {2} at {3} FPS", "Jugador {0}: {1} × {2} a {3} FPS", "Joueur {0} : {1} × {2} à {3} FPS" },
   new[] { "Each player sets this on the Play page while there is one display per player.", "Cada jugador lo ajusta en la página Jugar mientras haya una pantalla por jugador.", "Chaque joueur le règle sur la page Jouer tant qu'il y a un écran par joueur." },
   new[] { "Not available", "No disponible", "Indisponible" },
   new[] { "(window size)", "(tamaño de ventana)", "(taille de la fenêtre)" },
   new[] { "(sharper)", "(más nítido)", "(plus net)" },
   new[] { "(faster)", "(más rápido)", "(plus rapide)" },

   new[] { "Starting...", "Iniciando...", "Démarrage..." },
   new[] { "Stopping...", "Deteniendo...", "Arrêt..." },
   new[] { "Ready.", "Listo.", "Prêt." },
   new[] { "Session running.", "Sesión en marcha.", "Session en cours." },
   new[] { "Copying the shader cache for player 2 (first time only)...", "Copiando la caché de shaders del jugador 2 (solo la primera vez)...", "Copie du cache de shaders du joueur 2 (la première fois seulement)..." },
   new[] { "Shared shader cache is off for this session: {0}", "La caché de shaders compartida está desactivada en esta sesión: {0}", "Le cache de shaders partagé est désactivé pour cette session : {0}" },
   new[] { "Start failed: {0}", "No se pudo iniciar: {0}", "Échec du démarrage : {0}" },
   new[] { "The display settings could not be prepared: {0}", "No se pudo preparar la configuración de pantalla: {0}", "Les paramètres d'affichage n'ont pas pu être préparés : {0}" },
   new[] { "Turn both screens back upright", "Volver a poner las dos pantallas en posición normal", "Remettre les deux écrans à l'endroit" },
   new[] { "Turn the screen of player {0} a quarter turn clockwise", "Girar un cuarto de vuelta, en sentido horario, la pantalla del jugador {0}", "Faire pivoter l'écran du joueur {0} d'un quart de tour dans le sens horaire" },
   new[] { "Turning a screen needs Vulkan. Choose Vulkan in the graphics settings.", "Girar una pantalla requiere Vulkan. Elige Vulkan en los ajustes de gráficos.", "Faire pivoter un écran nécessite Vulkan. Choisissez Vulkan dans les paramètres graphiques." },
   new[] { "Turning a screen needs ReShade. Copy ReShade64.dll and ReShade64.json to {0}.", "Girar una pantalla requiere ReShade. Copia ReShade64.dll y ReShade64.json en {0}.", "Faire pivoter un écran nécessite ReShade. Copiez ReShade64.dll et ReShade64.json dans {0}." },
   new[] { "The dedicated server could not start: {0}", "El servidor dedicado no pudo iniciarse: {0}", "Le serveur dédié n'a pas pu démarrer : {0}" },
   new[] { "The start script failed.", "El script de inicio falló.", "Le script de démarrage a échoué." },
   new[] { "Cemu A: wait for the main menu, then press Continue within about a minute.", "Cemu A: espera al menú principal y pulsa Continuar en aproximadamente un minuto.", "Cemu A : attendez le menu principal, puis appuyez sur Continuer dans la minute environ." },
   new[] { "Switch to single player", "Cambiar a un jugador", "Passer à un joueur" },
   new[] { "Switch to local multiplayer", "Cambiar a multijugador local", "Passer au multijoueur local" },
   new[] { "Play alone: one game on the whole primary display.", "Juega solo: un juego en toda la pantalla principal.", "Jouez seul : un jeu sur tout l'écran principal." },
   new[] { "Two players on this PC, with the screen split or one display each.", "Dos jugadores en este PC, con la pantalla dividida o una pantalla para cada uno.", "Deux joueurs sur ce PC, avec l'écran partagé ou un écran chacun." },
   new[] { "One player · the whole display", "Un jugador · toda la pantalla", "Un joueur · tout l'écran" },
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
   new[] { "Use recommended settings", "Usar ajustes recomendados", "Utiliser les réglages recommandés" },
   new[] { "Sets the render resolution, shadows, anti-aliasing, the Cemu options and the FPS target to the values recommended for this PC. Applied the next time a session starts. It is a starting point, not a benchmark: adjust it if the game runs badly.", "Pone la resolución de renderizado, las sombras, el suavizado, las opciones de Cemu y el objetivo de FPS en los valores recomendados para este PC. Se aplica la próxima vez que inicies una sesión. Es un punto de partida, no una prueba de rendimiento, así que ajústalo si el juego va mal.", "Règle la résolution de rendu, les ombres, l'anticrénelage, les options de Cemu et l'objectif FPS sur les valeurs recommandées pour ce PC. Appliqué au prochain démarrage d'une session." },
   new[] { "Use recommended rules", "Usar reglas recomendadas", "Utiliser les règles recommandées" },
   new[] { "Selects the Game Completion preset with no special mode, the set that shares the most for playing the whole game together. It does not depend on this PC's hardware.", "Elige el preset Game Completion sin modo especial, el que más comparte para jugar el juego entero en equipo. No depende del hardware de este PC.", "Choisit le preset Game Completion sans mode spécial, celui qui partage le plus pour jouer au jeu entier ensemble. Il ne dépend pas du matériel de ce PC." },
   new[] { "Basic", "Básica", "Basique" },
   new[] { "Balanced", "Equilibrada", "Équilibrée" },
   new[] { "High", "Alta", "Élevée" },
   new[] { "Ultra", "Ultra", "Ultra" },
   new[] { "Disabled", "Desactivado", "Désactivé" },
   new[] { "Normal FXAA", "FXAA normal", "FXAA normal" },
   new[] { "Low", "Baja", "Faible" },
   new[] { "Medium", "Media", "Moyenne" },
   new[] { "Very High", "Muy alta", "Très élevée" },
   new[] { "Extreme", "Extrema", "Extrême" },
   new[] { "Default", "Predeterminado", "Par défaut" },
   new[] { "Recommended", "Recomendado", "Recommandé" },
   new[] { "Unstable", "Inestable", "Instable" },
   new[] { "Experimental", "Experimental", "Expérimental" },
   new[] { "For playing the whole game together. Defeated enemies, Korok seeds, towers, shrines and map locations are shared; quests and Divine Beasts stay separate for each player.", "Para jugar el juego entero en equipo. Se comparten los enemigos derrotados, las semillas Kolog, las torres, los santuarios y los lugares del mapa; las misiones y las Bestias Divinas son separadas para cada jugador.", "Pour jouer au jeu entier ensemble. Les ennemis vaincus, les graines Korogu, les tours, les sanctuaires et les lieux de la carte sont partagés ; les quêtes et les Créatures divines restent séparées pour chaque joueur." },
   new[] { "A chase game: the speedrunner races through the game while the hunters try to stop them. What one player clears, activates or discovers counts for both.", "Un juego de persecución: el speedrunner avanza a toda velocidad mientras los cazadores intentan detenerlo. Lo que uno despeja, activa o descubre cuenta para ambos.", "Un jeu de poursuite : le speedrunner fonce à travers le jeu pendant que les chasseurs tentent de l'arrêter. Ce qu'un joueur termine, active ou découvre compte pour les deux." },
   new[] { "Built for speedruns: what one player defeats, activates or discovers counts for both, except quests, Korok seeds and Divine Beasts.", "Pensado para speedruns: lo que uno derrota, activa o descubre cuenta para ambos, salvo las misiones, las semillas Kolog y las Bestias Divinas.", "Conçu pour les speedruns : ce qu'un joueur bat, active ou découvre compte pour les deux, sauf les quêtes, les graines Korogu et les Créatures divines." },
   new[] { "Only defeated enemies are shared. Everything else stays separate for each player, which suits hide and seek in the same world.", "Solo se comparten los enemigos derrotados. Todo lo demás es separado para cada jugador, lo que va bien para el escondite en el mismo mundo.", "Seuls les ennemis vaincus sont partagés. Tout le reste reste séparé pour chaque joueur, ce qui convient à un cache-cache dans le même monde." },
   new[] { "A game mode where the players are swapped around the world at intervals. Nothing else is shared.", "Un modo de juego en el que los jugadores se intercambian de posición por el mundo cada cierto tiempo. No se comparte nada más.", "Un mode de jeu où les joueurs sont échangés de position dans le monde à intervalles réguliers. Rien d'autre n'est partagé." },
   new[] { "Your own mix: choose below what the players share.", "Tu propia mezcla: elige abajo lo que comparten los jugadores.", "Votre propre mélange : choisissez ci-dessous ce que les joueurs partagent." },
   new[] { "Adds a chase: one player speedruns while the others hunt them. Best with the Hunter Vs Speedrunner preset.", "Añade una persecución: un jugador hace speedrun mientras los demás lo cazan. Mejor con el preset Hunter Vs Speedrunner.", "Ajoute une poursuite : un joueur fait un speedrun pendant que les autres le chassent. Idéal avec le preset Hunter Vs Speedrunner." },
   new[] { "Adds Death Swap: the players are swapped around the world at intervals. Best with the Death Swap preset.", "Añade Death Swap: los jugadores se intercambian de posición por el mundo cada cierto tiempo. Mejor con el preset Death Swap.", "Ajoute Death Swap : les joueurs sont échangés de position dans le monde à intervalles réguliers. Idéal avec le preset Death Swap." },
   new[] { "Normal co-op: only the rules above decide what the players share.", "Cooperativo normal: solo las reglas de arriba deciden lo que comparten los jugadores.", "Coopératif normal : seules les règles ci-dessus décident de ce que les joueurs partagent." },
   new[] { "Nothing", "Nada", "Rien" },
   new[] { "Shared: {0}", "Compartido: {0}", "Partagé : {0}" },
   new[] { "Not shared: {0}", "No compartido: {0}", "Non partagé : {0}" },
   new[] { "Hyrule Online is already open.", "Hyrule Online ya está abierto.", "Hyrule Online est déjà ouvert." },
   new[] { "{0} is running and uses the same controller routing. Close it first.", "{0} se está ejecutando y usa la misma redirección de mandos. Ciérralo primero.", "{0} est en cours d'exécution et utilise la même redirection des manettes. Fermez-le d'abord." },
   new[] { "Run Hyrule Online from the project's .artifacts\\app folder.", "Ejecuta Hyrule Online desde la carpeta .artifacts\\app del proyecto.", "Lancez Hyrule Online depuis le dossier .artifacts\\app du projet." },
   new[] { "Controller input could not start.\n\n{0}", "No se pudo iniciar la entrada de los mandos.\n\n{0}", "L'entrée des manettes n'a pas pu démarrer.\n\n{0}" },
   new[] { "The action could not be completed.\n\n{0}", "No se pudo completar la acción.\n\n{0}", "L'action n'a pas pu être terminée.\n\n{0}" },
   new[] { "The BOTW Graphics pack has an unexpected format.", "El pack gráfico de BOTW tiene un formato inesperado.", "Le pack graphique de BOTW a un format inattendu." },
   new[] { "The BOTW Graphics pack is missing from {0}.", "Falta el pack gráfico de BOTW en {0}.", "Le pack graphique de BOTW est absent de {0}." },
   new[] { "Cemu settings are missing from {0}.", "Faltan los ajustes de Cemu en {0}.", "Les paramètres de Cemu sont absents de {0}." },
   new[] { "Cemu already has its own ReShade setup in {0}.", "Cemu ya tiene su propia configuración de ReShade en {0}.", "Cemu a déjà sa propre configuration ReShade dans {0}." },
   new[] { "ReShade64.dll and ReShade64.json are missing from {0}.", "Faltan ReShade64.dll y ReShade64.json en {0}.", "ReShade64.dll et ReShade64.json sont absents de {0}." },
   new[] { "The dedicated server is missing.", "Falta el servidor dedicado.", "Le serveur dédié est absent." },
   new[] { "Hyrule Online cannot start from this folder:", "Hyrule Online no puede iniciarse desde esta carpeta:", "Hyrule Online ne peut pas démarrer depuis ce dossier :" },
   new[] { "Hyrule Online may have problems in this folder:", "Hyrule Online puede tener problemas en esta carpeta:", "Hyrule Online peut avoir des problèmes dans ce dossier :" },
   new[] { "The folder path is {0} characters long and the limit is {1}. BCML, Cemu and the Milk Bar launcher fail on long paths.", "La ruta de la carpeta tiene {0} caracteres y el límite es {1}. BCML, Cemu y el launcher de Milk Bar fallan con rutas largas.", "Le chemin du dossier compte {0} caractères et la limite est de {1}. BCML, Cemu et le launcher de Milk Bar échouent avec les chemins longs." },
   new[] { "The folder is on a network location. The game and its tools need a local drive.", "La carpeta está en una ubicación de red. El juego y sus herramientas necesitan una unidad local.", "Le dossier est sur un emplacement réseau. Le jeu et ses outils ont besoin d'un lecteur local." },
   new[] { "The folder is under Program Files, which Windows protects from changes by regular programs.", "La carpeta está dentro de Archivos de programa, que Windows protege contra los cambios de programas normales.", "Le dossier est dans Program Files, que Windows protège contre les modifications par les programmes ordinaires." },
   new[] { "The folder is synced by OneDrive, which can lock or rewrite files while the game uses them.", "La carpeta está sincronizada con OneDrive, que puede bloquear o reescribir archivos mientras el juego los usa.", "Le dossier est synchronisé par OneDrive, qui peut verrouiller ou réécrire des fichiers pendant que le jeu les utilise." },
   new[] { "The folder path has characters that are not plain ASCII, which some of the tools may not handle.", "La ruta de la carpeta tiene caracteres que no son ASCII simple, y algunas herramientas podrían no manejarlos.", "Le chemin du dossier contient des caractères qui ne sont pas de l'ASCII simple, que certains outils pourraient ne pas gérer." },
   new[] { "Move the whole folder to a short local path such as {0} and start it from there.", "Mueve toda la carpeta a una ruta local corta como {0} e inicia desde allí.", "Déplacez tout le dossier vers un chemin local court comme {0} et lancez-le depuis là." },
   new[] { "Continue anyway?", "¿Continuar de todos modos?", "Continuer quand même ?" }
  };

  // The help is one text per language, in sections separated by a blank line ("TITLE: text"), so it is kept outside the table.
  public static readonly string[] HelpText = {
   "SESSION: Start session opens the server and both Cemu windows. When a game reaches its main menu, press Continue in that window (about a minute). If you change graphics, FPS, displays, names, avatars, the game language or world rules while a game runs, Start becomes Restart session. The launcher language and live controls apply at once.\n\nPLAYERS: The switch under Player 1 toggles one or two players. One player runs a single game on the whole display, without the swap, the split or one display per player. The controller that picks one player becomes Player 1; going back to local multiplayer restores the old assignment. Your choice is remembered.\n\nCONTROLLERS: Choose a player, then press confirm on a controller to assign it. Each Cemu window sees only its own controller. Vibration depends on the controller. Names need a keyboard.\n\nMOVING AROUND: The D-pad or left stick moves a cursor over this launcher. Confirm presses or opens; back goes back and closes this help. LB and RB (or Ctrl+Tab) change page. The Start button presses Start session; Select toggles one or two players on Play. Nintendo pads confirm with the right button; Xbox, PlayStation and Steam Input pads with the bottom one. Menu controls on Graphics can force one layout; it never changes the game. On Play, each player's controller gets its own cursor (green for Player 1, orange for Player 2); other controllers share a white one. The avatar slot opens a mosaic, and both players can pick at once.\n\nMENU SHORTCUT: In a game, hold Start + LT on any controller for 1.5 seconds to bring this launcher forward. The game ignores that controller meanwhile. Press back on the first screen, or hold the shortcut again, to return. Change or turn it off under Menu controls on Graphics.\n\nDISPLAY: Both Cemu windows share the primary display. Split puts them side by side or top and bottom; Auto goes side by side on wide displays. A window you move by hand stays where you put it. With two or more displays, each player can get a whole display with its own render resolution and FPS target.\n\nGRAPHICS: The left options come from the game's graphics pack. Advanced (Cemu) sets the API (Auto picks Vulkan when available, otherwise OpenGL), the filters, VSync, shader compilation, accurate barriers, the FPS and CPU/RAM/VRAM counters and an experimental option that copies player 1's shader cache to player 2 when it has none. Recommended for this PC is a starting point, not a benchmark. Changes apply at the next session start.\n\nONLINE: Choose how this PC takes part. Play on this PC keeps the server private. Host a server opens it to friends: set the port, password and description, copy the invite and allow it through the firewall. Join a server connects one or two players on this PC to a friend's server with their invite; use the switch on Play to choose. Changes apply at the next session start.\n\nWORLD: World rules choose what everyone on the server shares (enemies, quests, koroks, towers, shrines, locations, Divine Beasts) and apply when the server starts. When you join a friend's server their rules apply and these are locked. Use recommended rules selects Game Completion. Live controls change the time, weather and name tags, pause sync or bring one player to the other while the server runs.\n\nLANGUAGE: The launcher language changes at once. The game language applies to both players at the next session start, and the two can differ.\n\nKEYBOARD: Alt+Tab returns here from a game. Esc minimizes the launcher while a game is open and closes it otherwise. The preview shows the split layout, not a game screenshot.",
   "SESIÓN: Iniciar sesión abre el servidor y las dos ventanas de Cemu. Cuando un juego llegue a su menú principal, pulsa Continuar en esa ventana (tarda aproximadamente un minuto). Si con el juego en marcha cambias gráficos, FPS, pantallas, nombres, avatares, el idioma del juego o las reglas del mundo, Iniciar se convierte en Reiniciar sesión. El idioma del launcher y los controles en vivo se aplican al instante.\n\nJUGADORES: El botón bajo el Jugador 1 cambia entre uno y dos jugadores. Con un jugador se inicia un solo juego en toda la pantalla, sin intercambio, división ni una pantalla por jugador. El mando que elige un jugador pasa a ser el Jugador 1; al volver a multijugador local se restaura la asignación anterior. Tu elección se recuerda.\n\nMANDOS: Elige un jugador y pulsa confirmar en un mando para asignarlo. Cada ventana de Cemu solo ve su propio mando. La vibración depende del mando. Los nombres se escriben con teclado.\n\nNAVEGACIÓN: La cruceta o el stick izquierdo mueven un cursor por este launcher. Confirmar pulsa o abre; volver retrocede y cierra esta ayuda. LB y RB (o Ctrl+Tab) cambian de página. El botón Start pulsa Iniciar sesión; Select alterna uno o dos jugadores en Jugar. Los mandos de Nintendo confirman con el botón derecho; los de Xbox, PlayStation y Steam Input, con el de abajo. Control de menús, en Gráficos, puede forzar una distribución; nunca cambia el juego. En Jugar, el mando de cada jugador tiene su propio cursor (verde para el Jugador 1, naranja para el Jugador 2); los demás mandos comparten uno blanco. La casilla de avatar abre un mosaico y los dos jugadores pueden elegir a la vez.\n\nATAJO DEL MENÚ: Con un juego en marcha, mantén Start + LT en cualquier mando durante 1,5 segundos para traer este launcher al frente. Mientras tanto, el juego ignora ese mando. Pulsa volver en la primera pantalla, o mantén el atajo otra vez, para regresar. Cámbialo o desactívalo en Control de menús, en Gráficos.\n\nPANTALLA: Las dos ventanas de Cemu comparten la pantalla principal. División las pone lado a lado o arriba y abajo; Auto las pone lado a lado en pantallas anchas. Una ventana que muevas a mano se queda donde la dejes. Con dos o más pantallas, cada jugador puede tener una entera, con su propia resolución de renderizado y objetivo de FPS.\n\nGRÁFICOS: Las opciones de la izquierda vienen del paquete gráfico del juego. Avanzado (Cemu) ajusta la API (Auto elige Vulkan si está disponible y, si no, OpenGL), los filtros, VSync, la compilación de shaders, las barreras precisas, los contadores de FPS y de CPU/RAM/VRAM y una opción experimental que copia la caché de shaders del jugador 1 al jugador 2 cuando este no tiene ninguna. Recomendado para este PC es un punto de partida, no una prueba de rendimiento. Los cambios se aplican al iniciar la próxima sesión.\n\nEN LÍNEA: Elige cómo participa este PC. Jugar en este PC mantiene el servidor privado. Alojar un servidor lo abre a tus amigos: define el puerto, la contraseña y la descripción, copia la invitación y permítelo en el firewall. Unirse a un servidor conecta a uno o dos jugadores de este PC al servidor de un amigo con su invitación; elige con el interruptor de Jugar. Los cambios se aplican al iniciar la próxima sesión.\n\nMUNDO: Las reglas del mundo eligen lo que comparte todo el servidor (enemigos, misiones, koroks, torres, santuarios, lugares, Bestias Divinas) y se aplican al arrancar el servidor. Si te unes al servidor de un amigo, valen sus reglas y estas quedan bloqueadas. Usar reglas recomendadas elige Game Completion. Los controles en vivo cambian la hora, el clima y los nombres, pausan la sincronización o llevan a un jugador junto al otro mientras el servidor está en marcha.\n\nIDIOMA: El idioma del launcher cambia al instante. El del juego se aplica a ambos jugadores al iniciar la próxima sesión, y pueden ser distintos.\n\nTECLADO: Alt+Tab vuelve aquí desde un juego. Esc minimiza el launcher con un juego abierto y lo cierra si no. La vista previa muestra la división, no una captura del juego.",
   "SESSION : Démarrer la session ouvre le serveur et les deux fenêtres Cemu. Quand un jeu arrive à son menu principal, appuyez sur Continuer dans cette fenêtre (environ une minute). Si vous changez les graphismes, les FPS, les écrans, les noms, les avatars, la langue du jeu ou les règles du monde pendant que le jeu tourne, Démarrer devient Redémarrer la session. La langue du launcher et les commandes en direct s'appliquent tout de suite.\n\nJOUEURS : Le bouton sous le joueur 1 bascule entre un et deux joueurs. Avec un joueur, un seul jeu occupe tout l'écran, sans inversion, division ni écran par joueur. La manette qui choisit un joueur devient le joueur 1 ; en revenant au multijoueur local, l'ancienne attribution est rétablie. Votre choix est mémorisé.\n\nMANETTES : Choisissez un joueur, puis appuyez sur valider sur une manette pour l'assigner. Chaque fenêtre Cemu ne voit que sa propre manette. La vibration dépend de la manette. Les noms se saisissent au clavier.\n\nNAVIGATION : La croix directionnelle ou le stick gauche déplacent un curseur dans ce launcher. Valider appuie ou ouvre ; retour revient en arrière et ferme cette aide. LB et RB (ou Ctrl+Tab) changent de page. Le bouton Start appuie sur Démarrer la session ; Select bascule entre un et deux joueurs sur Jouer. Les manettes Nintendo valident avec le bouton de droite ; Xbox, PlayStation et Steam Input avec celui du bas. Commandes des menus, dans Graphismes, peut imposer une disposition ; le jeu n'est jamais touché. Sur Jouer, la manette de chaque joueur a son propre curseur (vert pour le joueur 1, orange pour le joueur 2) ; les autres en partagent un blanc. L'emplacement d'avatar ouvre une mosaïque et les deux joueurs peuvent choisir en même temps.\n\nRACCOURCI DU MENU : Pendant un jeu, maintenez Start + LT sur n'importe quelle manette pendant 1,5 seconde pour amener ce launcher au premier plan. Entre-temps, le jeu ignore cette manette. Appuyez sur retour sur le premier écran, ou maintenez de nouveau le raccourci, pour revenir. Changez-le ou désactivez-le dans Commandes des menus, sur Graphismes.\n\nÉCRAN : Les deux fenêtres Cemu se partagent l'écran principal. Division les place côte à côte ou l'une au-dessus de l'autre ; Auto les place côte à côte sur les écrans larges. Une fenêtre déplacée à la main reste où vous l'avez mise. Avec deux écrans ou plus, chaque joueur peut avoir un écran entier, avec sa propre résolution de rendu et son objectif FPS.\n\nGRAPHISMES : Les options de gauche viennent du pack graphique du jeu. Avancé (Cemu) règle l'API (Auto choisit Vulkan si disponible, sinon OpenGL), les filtres, la VSync, la compilation des shaders, les barrières précises, les compteurs de FPS et de CPU/RAM/VRAM et une option expérimentale qui copie le cache de shaders du joueur 1 vers le joueur 2 quand celui-ci n'en a pas. Recommandé pour ce PC est un point de départ, pas un test de performance. Les changements s'appliquent au prochain démarrage d'une session.\n\nEN LIGNE : Choisissez comment ce PC participe. Jouer sur ce PC garde le serveur privé. Héberger un serveur l'ouvre à vos amis : réglez le port, le mot de passe et la description, copiez l'invitation et autorisez-la dans le pare-feu. Rejoindre un serveur connecte un ou deux joueurs de ce PC au serveur d'un ami avec son invitation ; choisissez avec le commutateur de Jouer. Les changements s'appliquent au prochain démarrage d'une session.\n\nMONDE : Les règles du monde choisissent ce que tout le serveur partage (ennemis, quêtes, korogus, tours, sanctuaires, lieux, Créatures divines) et s'appliquent au démarrage du serveur. Si vous rejoignez le serveur d'un ami, ses règles s'appliquent et celles-ci sont verrouillées. Utiliser les règles recommandées choisit Game Completion. Les commandes en direct changent l'heure, la météo et les noms, mettent la synchro en pause ou amènent un joueur près de l'autre pendant que le serveur fonctionne.\n\nLANGUE : La langue du launcher change immédiatement. Celle du jeu s'applique aux deux joueurs au prochain démarrage d'une session, et elles peuvent différer.\n\nCLAVIER : Alt+Tab ramène ici depuis un jeu. Échap réduit le launcher pendant qu'un jeu est ouvert et le ferme sinon. L'aperçu montre la division, pas une capture du jeu."
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
