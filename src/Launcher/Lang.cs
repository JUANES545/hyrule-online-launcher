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
   "ServerText", "AText", "BText", "Status", "Prompt", "HelpText", "Log", "ServerLog", "LanguageList", "LauncherLanguageList", "TimeBox", "GraphicsHint", "MultiplayerHint", "Start", "HardwareText", "RecommendedText", "PresetInfo", "PresetShares", "SpecialInfo", "PadHint1", "PadHint2", "PadLayoutInfo", "StartText", "Mode"
  };

  // Text that reads the same in every language.
  public static readonly string[] Neutral = { "Hyrule Online", "THE LEGEND OF ZELDA", "BREATH OF THE WILD", "LT / RT", "LB", "RB", "Hunter vs Speedrunner", "Death Swap" };

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
   new[] { "Applied to both games the next time a session starts.", "Se aplica a ambos juegos la próxima vez que inicies una sesión.", "S'applique aux deux jeux au prochain démarrage d'une session." },
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
   new[] { "No controller detected: menus use the {0} layout.", "Ningún mando detectado: los menús usan la distribución {0}.", "Aucune manette détectée : les menus utilisent la disposition {0}." },
   new[] { "How many pixels each game renders. Higher is sharper but needs a stronger graphics card.", "Cuántos píxeles renderiza cada juego. Más es más nítido, pero exige una tarjeta gráfica más potente.", "Nombre de pixels rendus par chaque jeu. Plus c'est élevé, plus c'est net, mais il faut une carte graphique plus puissante." },
   new[] { "Quality of the shadows in the game.", "Calidad de las sombras del juego.", "Qualité des ombres dans le jeu." },
   new[] { "How far from the player shadows are drawn.", "A qué distancia del jugador se dibujan las sombras.", "Jusqu'où les ombres sont dessinées autour du joueur." },
   new[] { "Smooths jagged edges.", "Suaviza los bordes dentados.", "Adoucit les bords crénelés." },
   new[] { "For advanced users: the defaults suit most PCs. The card below picks values for this one.", "Para usuarios avanzados: los valores por defecto sirven para la mayoría de PC. La tarjeta de abajo elige valores para este.", "Pour utilisateurs avancés : les valeurs par défaut conviennent à la plupart des PC. La carte ci-dessous choisit des valeurs pour celui-ci." },
   new[] { "Graphics API", "API gráfica", "API graphique" },
   new[] { "The graphics backend Cemu uses. Vulkan is usually the better choice on modern graphics cards; OpenGL is the older backend, useful if Vulkan crashes or glitches on this PC. Auto picks Vulkan when this PC supports it and OpenGL otherwise.", "El motor gráfico que usa Cemu. Vulkan suele ser mejor en tarjetas gráficas modernas; OpenGL es el motor antiguo y sirve si Vulkan se cuelga o da fallos en este PC. Auto elige Vulkan si este PC lo admite y OpenGL en caso contrario.", "Le moteur graphique utilisé par Cemu. Vulkan est en général le meilleur choix sur les cartes graphiques récentes ; OpenGL est l'ancien moteur, utile si Vulkan plante ou provoque des défauts sur ce PC. Auto choisit Vulkan si ce PC le gère, sinon OpenGL." },
   new[] { "After changing the API the first start can take longer while shaders compile again.", "Tras cambiar la API, el primer inicio puede tardar más mientras se vuelven a compilar los shaders.", "Après un changement d'API, le premier démarrage peut être plus long pendant la recompilation des shaders." },
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
   new[] { "Switch to multiplayer", "Cambiar a multijugador", "Passer au multijoueur" },
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
   new[] { "RECOMMENDED FOR THIS PC", "RECOMENDADO PARA ESTE PC", "RECOMMANDÉ POUR CE PC" },
   new[] { "A starting point chosen from the graphics card, memory and processor. It is not a benchmark, so adjust it if the game runs badly.", "Un punto de partida elegido según la tarjeta gráfica, la memoria y el procesador. No es una prueba de rendimiento, así que ajústalo si el juego va mal.", "Un point de départ choisi d'après la carte graphique, la mémoire et le processeur. Ce n'est pas un test de performance : ajustez-le si le jeu tourne mal." },
   new[] { "Use recommended settings", "Usar ajustes recomendados", "Utiliser les réglages recommandés" },
   new[] { "Sets the render resolution, shadows, anti-aliasing, the Cemu options and the FPS target to the values recommended for this PC. Applied the next time a session starts.", "Pone la resolución de renderizado, las sombras, el suavizado, las opciones de Cemu y el objetivo de FPS en los valores recomendados para este PC. Se aplica la próxima vez que inicies una sesión.", "Règle la résolution de rendu, les ombres, l'anticrénelage, les options de Cemu et l'objectif FPS sur les valeurs recommandées pour ce PC. Appliqué au prochain démarrage d'une session." },
   new[] { "Use recommended rules", "Usar reglas recomendadas", "Utiliser les règles recommandées" },
   new[] { "Selects the Game Completion preset with no special mode, the set that shares the most for playing the whole game together. It does not depend on this PC's hardware.", "Elige el preset Game Completion sin modo especial, el que más comparte para jugar el juego entero en equipo. No depende del hardware de este PC.", "Choisit le preset Game Completion sans mode spécial, celui qui partage le plus pour jouer au jeu entier ensemble. Il ne dépend pas du matériel de ce PC." },
   new[] { "Graphics card not detected", "Tarjeta gráfica no detectada", "Carte graphique non détectée" },
   new[] { "{0} · {1} threads", "{0} · {1} hilos", "{0} · {1} threads" },
   new[] { "{0} GB of memory", "{0} GB de memoria", "{0} Go de mémoire" },
   new[] { "Performance class: {0}", "Clase de rendimiento: {0}", "Classe de performance : {0}" },
   new[] { "Basic", "Básica", "Basique" },
   new[] { "Balanced", "Equilibrada", "Équilibrée" },
   new[] { "High", "Alta", "Élevée" },
   new[] { "Ultra", "Ultra", "Ultra" },
   new[] { "Recommended: render {0}, shadows {1}, shadow distance {2}, anti-aliasing {3}.", "Recomendado: renderizado {0}, sombras {1}, distancia de sombras {2}, suavizado {3}.", "Recommandé : rendu {0}, ombres {1}, distance des ombres {2}, anticrénelage {3}." },
   new[] { "Cemu: automatic API, {0} upscale filter, asynchronous shaders on, VSync off. FPS target Auto.", "Cemu: API automática, filtro de ampliación {0}, shaders asíncronos activados, VSync desactivado. Objetivo de FPS Auto.", "Cemu : API automatique, filtre d'agrandissement {0}, shaders asynchrones activés, VSync désactivée. Objectif FPS Auto." },
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
   new[] { "A session is still open.\n\nYes: stop the session and close.\nNo: close only the launcher. The game keeps running and the controllers fall back to Windows XInput.\nCancel: keep this window open.",
    "Todavía hay una sesión abierta.\n\nSí: detener la sesión y cerrar.\nNo: cerrar solo el launcher. El juego sigue en marcha y los mandos vuelven a XInput de Windows.\nCancelar: mantener esta ventana abierta.",
    "Une session est encore ouverte.\n\nOui : arrêter la session et fermer.\nNon : fermer uniquement le launcher. Le jeu continue et les manettes repassent sur XInput de Windows.\nAnnuler : garder cette fenêtre ouverte." },
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

  // The help panel is one long text, so it is kept per language instead of in the table.
  public static readonly string[] HelpText = {
   "SESSION: Start session opens the dedicated server, then Player 1 and Player 2 in their own Cemu windows. When each game reaches the main menu, press Continue in that window within about a minute. While the games run, changing an option that needs a restart (graphics, FPS, display layout, names, avatars, game language, world rules) turns Start into Restart session; the launcher language and the live controls apply at once.\n\nPLAYERS: the switch under the Player 1 card changes between one player and two players (it says Switch to single player or Switch to multiplayer). 1 player starts one game on the whole primary display, without the second player, the swap, the split or the one-display-per-player option. 2 players is the shared-screen session. Your choice is remembered, and changing it while the game runs turns Start into Restart session.\n\nPAGES: LB and RB on any controller (or Ctrl+Tab) move between Play, Graphics, Multiplayer, Language and Details while this launcher is in front. The small centre buttons also work then: the Start button (Menu on Xbox, Options on PlayStation, + on Nintendo) presses Start session or Restart session, and the Select button (View, Create or −) presses the 1 or 2 players switch on the Play page. The icons next to those buttons follow the first controller.\n\nNAVIGATION: every controller moves a cursor over this launcher with the D-pad or the left stick; the confirm button presses or opens and the back button goes back (it also closes this help). By default each controller follows its own layout: Nintendo pads confirm with the right button; Xbox, PlayStation and Steam Input pads with the bottom one. The Graphics page can force one layout for every controller; it only affects these menus, never the game. On Play each player's controller has its own cursor (green for Player 1, orange for Player 2); the other controllers share a white one. The avatar slot opens a picture mosaic of avatars, and both players can choose at the same time. Names cannot be typed with a controller.\n\nCONTROLLERS: choose a player, then press the confirm button on a controller to assign it. Each Cemu window only sees its own controller. Vibration support depends on the controller.\n\nDISPLAY: the two Cemu windows share the primary display. Split chooses side by side or top and bottom; Auto splits wide displays side by side. Windows are placed when they open, so moving one by hand is kept. With two or more displays, Displays can give each player a whole display of their own, with its own render resolution and FPS target.\n\nGRAPHICS: the left choices come from the game's own graphics pack. Advanced (Cemu) sets the graphics API (Auto picks Vulkan when this PC has it, otherwise OpenGL), the scaling filters, VSync, asynchronous shader compilation and accurate barriers. The Recommended for this PC card picks settings from the graphics card, memory and processor (a heuristic, not a benchmark), and its button applies them. Everything applies the next time a session starts. Hover an option, or move the focus to it, to see what it does.\n\nMULTIPLAYER: the world rules choose what the two players share (enemies, quests, koroks, towers, shrines, locations, Divine Beasts) and are used when the server starts. Each preset and special mode is explained under its box, and Use recommended rules selects Game Completion. The live controls change the time, weather and name tags, pause sync or bring one player to the other while the server runs.\n\nLANGUAGE: the launcher language changes this launcher right away. The game language is applied to both players the next time a session starts, and the two can differ.\n\nThe preview shows the split layout, not a game screenshot. Use Alt+Tab to come back to this launcher while the game is open. Esc minimizes the launcher while a game is open and closes it otherwise.",
   "SESIÓN: Iniciar sesión abre el servidor dedicado y después al jugador 1 y al jugador 2 en sus propias ventanas de Cemu. Cuando cada juego llegue al menú principal, pulsa Continuar en esa ventana en aproximadamente un minuto. Con los juegos en marcha, cambiar una opción que necesita reinicio (gráficos, FPS, disposición de pantallas, nombres, avatares, idioma del juego, reglas del mundo) convierte Iniciar en Reiniciar sesión; el idioma del launcher y los controles en vivo se aplican al instante.\n\nJUGADORES: el botón bajo la tarjeta del jugador 1 cambia entre un jugador y dos jugadores (dice Cambiar a un jugador o Cambiar a multijugador). 1 jugador inicia un solo juego en toda la pantalla principal, sin el segundo jugador, el intercambio, la división ni la opción de una pantalla por jugador. 2 jugadores es la sesión con pantalla compartida. Tu elección se recuerda y, si la cambias con el juego en marcha, Iniciar se convierte en Reiniciar sesión.\n\nPÁGINAS: LB y RB en cualquier mando (o Ctrl+Tab) cambian entre Jugar, Gráficos, Multijugador, Idioma y Detalles mientras este launcher esté al frente. Los botones pequeños del centro también funcionan: el botón Start (Menú en Xbox, Options en PlayStation, + en Nintendo) pulsa Iniciar sesión o Reiniciar sesión, y el botón Select (View, Create o −) pulsa el botón de 1 o 2 jugadores en la página Jugar. Los iconos junto a esos botones siguen al primer mando.\n\nNAVEGACIÓN: cada mando mueve un cursor por este launcher con la cruceta o el stick izquierdo; el botón de confirmar pulsa o abre y el de volver retrocede (también cierra esta ayuda). Por defecto cada mando sigue su propia distribución: los de Nintendo confirman con el botón derecho; los de Xbox, PlayStation y Steam Input, con el de abajo. La página Gráficos puede forzar una distribución para todos los mandos; solo afecta a estos menús, nunca al juego. En Jugar cada mando de jugador tiene su propio cursor (verde para el jugador 1, naranja para el jugador 2); los demás mandos comparten uno blanco. La casilla de avatar abre un mosaico de imágenes de avatares y los dos jugadores pueden elegir a la vez. Los nombres no se pueden escribir con un mando.\n\nMANDOS: elige un jugador y pulsa el botón de confirmar en un mando para asignarlo. Cada ventana de Cemu solo ve su propio mando. La vibración depende del mando.\n\nPANTALLA: las dos ventanas de Cemu comparten la pantalla principal. División elige lado a lado o arriba y abajo; Auto divide las pantallas anchas lado a lado. Las ventanas se colocan al abrirse, así que si mueves una a mano se respeta. Con dos o más pantallas, Pantallas puede dar a cada jugador una pantalla entera, con su propia resolución de renderizado y objetivo de FPS.\n\nGRÁFICOS: las opciones de la izquierda vienen del propio paquete gráfico del juego. Avanzado (Cemu) ajusta la API gráfica (Auto elige Vulkan si este PC lo tiene y, si no, OpenGL), los filtros de escalado, VSync, la compilación asíncrona de shaders y las barreras precisas. La tarjeta Recomendado para este PC elige ajustes según la tarjeta gráfica, la memoria y el procesador (una estimación, no una prueba de rendimiento), y su botón los aplica. Todo se aplica la próxima vez que inicies una sesión. Pasa el cursor por una opción, o mueve el foco hasta ella, para ver qué hace.\n\nMULTIJUGADOR: las reglas del mundo eligen lo que comparten los dos jugadores (enemigos, misiones, kologs, torres, santuarios, lugares, Bestias Divinas) y se usan cuando arranca el servidor. Cada preset y modo especial se explica bajo su casilla, y Usar reglas recomendadas elige Game Completion. Los controles en vivo cambian la hora, el clima y los nombres, pausan la sincronización o llevan a un jugador junto al otro mientras el servidor está en marcha.\n\nIDIOMA: el idioma del launcher cambia este launcher al instante. El idioma del juego se aplica a ambos jugadores la próxima vez que inicies una sesión, y pueden ser distintos.\n\nLa vista previa muestra la división, no una captura del juego. Usa Alt+Tab para volver a este launcher con el juego abierto. Esc minimiza el launcher mientras hay un juego abierto y lo cierra si no.",
   "SESSION : Démarrer la session ouvre le serveur dédié, puis le joueur 1 et le joueur 2 dans leurs propres fenêtres Cemu. Quand chaque jeu arrive au menu principal, appuyez sur Continuer dans cette fenêtre dans la minute environ. Quand les jeux tournent, changer une option qui demande un redémarrage (graphismes, FPS, disposition des écrans, noms, avatars, langue du jeu, règles du monde) transforme Démarrer en Redémarrer la session ; la langue du launcher et les commandes en direct s'appliquent tout de suite.\n\nJOUEURS : le bouton sous la carte du joueur 1 bascule entre un joueur et deux joueurs (Passer à un joueur ou Passer au multijoueur). 1 joueur lance un seul jeu sur tout l'écran principal, sans le deuxième joueur, l'inversion, la division ni l'option d'un écran par joueur. 2 joueurs est la session à écran partagé. Votre choix est mémorisé et, si vous le changez pendant que le jeu tourne, Démarrer devient Redémarrer la session.\n\nPAGES : LB et RB sur n'importe quelle manette (ou Ctrl+Tab) passent de Jouer à Graphismes, Multijoueur, Langue et Détails tant que ce launcher est au premier plan. Les petits boutons du centre fonctionnent aussi : le bouton Start (Menu sur Xbox, Options sur PlayStation, + sur Nintendo) appuie sur Démarrer la session ou Redémarrer la session, et le bouton Select (View, Create ou −) appuie sur le bouton 1 ou 2 joueurs sur la page Jouer. Les icônes à côté de ces boutons suivent la première manette.\n\nNAVIGATION : chaque manette déplace un curseur dans ce launcher avec la croix directionnelle ou le stick gauche ; le bouton de validation appuie ou ouvre et le bouton retour revient en arrière (et ferme cette aide). Par défaut, chaque manette suit sa propre disposition : les manettes Nintendo valident avec le bouton de droite ; Xbox, PlayStation et Steam Input avec celui du bas. La page Graphismes peut imposer une disposition à toutes les manettes ; elle ne concerne que ces menus, jamais le jeu. Dans Jouer, la manette de chaque joueur a son propre curseur (vert pour le joueur 1, orange pour le joueur 2) ; les autres manettes en partagent un blanc. L'emplacement d'avatar ouvre une mosaïque d'images d'avatars et les deux joueurs peuvent choisir en même temps. Les noms ne se saisissent pas à la manette.\n\nMANETTES : choisissez un joueur, puis appuyez sur le bouton de validation d'une manette pour l'assigner. Chaque fenêtre Cemu ne voit que sa propre manette. La vibration dépend de la manette.\n\nÉCRAN : les deux fenêtres Cemu se partagent l'écran principal. Division choisit côte à côte ou haut et bas ; Auto place les écrans larges côte à côte. Les fenêtres sont placées à leur ouverture, donc un déplacement manuel est conservé. Avec deux écrans ou plus, Écrans peut donner à chaque joueur un écran entier, avec sa propre résolution de rendu et son objectif FPS.\n\nGRAPHISMES : les choix de gauche viennent du pack graphique du jeu. Avancé (Cemu) règle l'API graphique (Auto choisit Vulkan si ce PC l'a, sinon OpenGL), les filtres de mise à l'échelle, la VSync, la compilation asynchrone des shaders et les barrières précises. La carte Recommandé pour ce PC choisit des réglages d'après la carte graphique, la mémoire et le processeur (une estimation, pas un test de performance), et son bouton les applique. Tout s'applique au prochain démarrage d'une session. Survolez une option, ou placez le focus dessus, pour voir ce qu'elle fait.\n\nMULTIJOUEUR : les règles du monde choisissent ce que les deux joueurs partagent (ennemis, quêtes, korogus, tours, sanctuaires, lieux, Créatures divines) et sont utilisées au démarrage du serveur. Chaque preset et mode spécial est expliqué sous sa liste, et Utiliser les règles recommandées choisit Game Completion. Les commandes en direct changent l'heure, la météo et les noms, mettent la synchro en pause ou amènent un joueur près de l'autre pendant que le serveur fonctionne.\n\nLANGUE : la langue du launcher change ce launcher immédiatement. La langue du jeu s'applique aux deux joueurs au prochain démarrage d'une session, et les deux peuvent différer.\n\nL'aperçu montre la division, pas une capture du jeu. Utilisez Alt+Tab pour revenir à ce launcher pendant que le jeu est ouvert. Échap réduit le launcher tant qu'un jeu est ouvert et le ferme sinon."
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
