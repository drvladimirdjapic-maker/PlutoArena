PLUTO ARENA 2.0
===============

Pluto Arena is a launcher for playing StarCraft: Remastered against the
Pluto AI bot. Choose a race for yourself and for Pluto, a map and a
difficulty, press PLAY - Pluto Arena starts two StarCraft windows, creates
a LAN Melee game and brings Pluto into it. You only choose your color and
click Ok (Start Game).


WHAT YOU NEED
-------------

Pluto Arena is only a launcher. These must be on your PC:

 1. StarCraft: Remastered (Battle.net), version 1.23.10.13515
    - The Pluto bridge checks the exact StarCraft.exe version. If Blizzard
      releases a new patch, Pluto will not start until the bridge is
      updated. The Setup window shows whether your version is supported.
    - English game client (Pluto Arena reads the English menus).

 2. Pluto AI by tscmoo - only three files, in any folder:
      pluto.dll
      pluto\pluto_infer.exe
      pluto\pluto_weights.bin
    (the "pluto" folder must be next to pluto.dll)
    Download release "cog2026-2578600" (Pluto 1.0) from
      https://github.com/tscmoo/pluto/releases/tag/cog2026-2578600
    and unpack it to any folder. StarCraft 1.16.1 and BWAPI are NOT needed.

    Optional - Pluto v2.0 (experimental in Pluto Arena), unpacked into its
    OWN folder (do not mix the files with 1.0):
      https://github.com/tscmoo/pluto/releases/tag/v2.0
    Pluto 2.0 runs on the CPU: x86-64 with AVX2, 6+ cores recommended.
    Other Pluto releases will not pass the bridge's file check.

 3. Windows 10 or 11, 64-bit, with the English language installed
    (Settings > Time & language > Language). Pluto Arena uses the built-in
    Windows text recognition (OCR) to see the StarCraft menus.

 .NET is NOT needed - it is built into PlutoArena.exe.

Included with Pluto Arena (in this installer):
 - the Pluto Remastered bridge (native\bin + native\scripts), with the
   Pluto Arena difficulty system built in
 - difficulty DLLs (Original for rank S + tuned bridge for ranks A ... F)
 - arena maps (copied into StarCraft\Maps\000_PlutoArena)

StarCraft: Remastered and Pluto AI are NOT part of this package and must
be obtained separately.


INSTALLATION
------------

 1. Run PlutoArena-2.0-Setup.exe (no administrator rights needed).
 2. Start Pluto Arena. On the first start the SETUP window opens.
 3. Check the paths (most are found automatically) and click CHECK:
      green  = OK
      yellow = warning
      red    = missing, PLAY will not work
 4. Enter your LAN profile name. If your profile and "PlutoAi" do not
    exist in StarCraft yet, Pluto Arena creates them (New ID).
 5. Click SAVE. The maps are copied to StarCraft\Maps\000_PlutoArena.

The setup can be opened again later with the SETUP button.


STARCRAFT SETTINGS (ONCE)
-------------------------

 Options > Graphics > Display Mode = Windowed (Fullscreen)

 In plain "Fullscreen" mode Windows does not allow the game screen to be
 read, so Pluto Arena cannot see the menus and stops with a message.


HOW TO PLAY
-----------

 1. Close all StarCraft windows.
 2. In Pluto Arena choose your race, Pluto's race, the map and the
    difficulty.
 3. Click PLAY (about 45 seconds). While the program clicks through the
    StarCraft menus, the mouse and keyboard are temporarily locked so
    nobody can disturb it by accident. Esc cancels and unlocks at once.
    (Can be turned off in PlutoArena.ini: LockInput=0)
 4. When the status shows "READY - CLICK OK IN STARCRAFT TO START!",
    choose your color and click Ok in the lobby.
 5. After the game close your StarCraft - the Pluto window closes by
    itself and PLAY is ready for the next game.

 The option "Hide Pluto window during the game" hides the Pluto window
 while the game is running (experimental).

 The option "Mute Pluto's sound" (on by default) mutes only Pluto's
 StarCraft in the Windows volume mixer, so you do not hear what Pluto is
 doing. Your StarCraft keeps its sound. (PlutoArena.ini: MutePlutoSound)


DIFFICULTY
----------

 Ranks like on the ladder, from S (full strength) to F (beginner).
 Pluto's neural network stays the same. Lower ranks make Pluto react
 later and limit its actions per minute (APM). Commands over the limit
 wait briefly; commands that would arrive too late are skipped, so Pluto
 plays slower but still reacts to what is happening now.

   S  - Original, full strength, no limits
   A  - 0.08 s reaction delay, no APM limit
   B  - 0.17 s reaction delay, no APM limit
   C  - 0.25 s reaction delay, max 220 APM
   D  - 0.40 s reaction delay, max 170 APM
   E  - 0.60 s reaction delay, max 130 APM
   F  - 0.80 s reaction delay, max 100 APM (beginner)

 S to B differ only in reaction time (how fast Pluto answers an attack,
 pulls back units, picks targets). From C down the APM limit also cuts
 Pluto's bursts in fights, and from D down it slows its economy too.


CHANGES IN 2.0
--------------

 - New look: a command console in metal, with a viewscreen in the header
   and green monitor text. The window takes the color of your race
   (Terran blue, Protoss gold, Zerg purple, Random silver).
 - Sounds when the mouse moves over a button and when you click it
   (switch UI SOUNDS off at the bottom left).
 - The status screen (top right) shows the Pluto version, the rank and
   your record on it.
 - The races you chose last time are remembered.
 - STATS (on the status screen): your results by Pluto's race, your race
   and map, with the best one marked.
 - New game summary: VICTORY / DEFEAT in chrome letters, results and the
   chart of your winning chances on a monitor panel.
 - Short start-up animation of the console (a click or any key skips it;
   PlutoArena.ini: ShowIntro=0 turns it off).
 - Game play is unchanged from 1.4.

CHANGES IN 1.4
--------------

 - Ladder mode: 2 wins in a row on a rank = one rank up, 2 losses = one
   rank down. Your record per Pluto version and rank is shown on the
   status screen (saved in PlutoArena-stats.txt).
 - REMATCH: after a game (score screen with Victory!/Defeat!) Pluto Arena
   records the result and offers a rematch - both StarCraft stay open,
   the next game is created automatically (same races and map; the rank
   follows ladder mode). Close your StarCraft to stop playing.
 - Pluto now acts after the 4 frames it was trained for in LAN games
   (it was 5): better micro, especially on rank S.
 - Game summary after each game; with Pluto 2.0 also a chart of your
   winning chances as Pluto saw them, and Pluto's opening.
 - Pluto's chat ("gl hf", "gg" ...) is shown on top of your StarCraft.
   (PlutoArena.ini: ShowPlutoChat=0 turns it off)
 - SETUP > RESET MEMORY: Pluto forgets its statistics against you.
 - Clear message when StarCraft was updated to a version the bridge does
   not support yet.
 - New icon, version number in the window.

CHANGES IN 1.3
--------------

 - Pluto v2.0 support (experimental): choose "Pluto 1.0" or "Pluto 2.0"
   next to the difficulty. Set the Pluto AI 2.0 path in SETUP.

CHANGES IN 1.2.1
----------------

 - Fixed: PLAY could stop at the Create Game screen when the screen text
   was read with small mistakes (e.g. "Titeset"). Small reading errors
   are now tolerated on all screens, also for the map names, and PLAY
   continues when the games list is gone even if the next screen is
   misread.

CHANGES IN 1.2
--------------

 - New difficulty ranks S, A, B, C, D, E, F (like the ladder) instead of
   Original ... Very Easy. More steps near full strength.
 - Pluto's StarCraft is muted, so you only hear your own game.
 - More arena maps.
 - If the Select Connection screen cannot be read, Pluto Arena continues
   anyway and selects LAN with the keyboard or a double-click.
 - Better text recognition on bright menu screens (Select Connection with
   the moon picture): a high-contrast copy is read when the normal
   reading fails or does not contain the expected menu text.

CHANGES IN 1.1.1
----------------

 - Windows OCR now uses English even when Windows is in another language;
   Setup shows an error if English text recognition is missing.

CHANGES IN 1.1
--------------

 - The Pluto Remastered bridge is included - PlutoRemasteredControlCenter
   is no longer needed.
 - New difficulty system: reaction delay + APM limit instead of dropping
   commands. Commands that would arrive too late are skipped, so Pluto
   always reacts to the current situation.


TROUBLESHOOTING
---------------

 - PlutoArena.log is written next to PlutoArena.exe: every step, what was
   read from the screen and where it stopped. Send it along with a short
   description of the problem.
 - Slow PC: set DelayFactor=1.5 (or 2) in PlutoArena.ini.
 - "StarCraft version is not 1.23.10.13515": StarCraft was patched, an
   updated Pluto bridge is needed.
 - "Pluto bridge failed" right after PLAY: often a different pluto.dll
   release - use "cog2026-2578600".
 - PLAY stops at "Select Connection" / the log shows garbage text, or Setup
   says "Windows OCR has no English": Windows text recognition needs the
   English language. Add English (United States) in Settings > Time &
   language > Language & region, or run in PowerShell as administrator:
     Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"
   Then restart Pluto Arena. (Windows itself can stay in your language.)
 - "cannot see the StarCraft screen": Display Mode is not
   Windowed (Fullscreen).
 - Antivirus (Windows Defender) may flag the Pluto bridge or Pluto Arena,
   because they start two game instances and load the bridge into the
   game. If needed, add an exclusion for the Pluto Arena folder and for
   the folder with the "native" bridge.
 - Melee only; Pluto does not support Top vs Bottom.

 Settings (paths, profile names) are stored in PlutoArena.ini next to the
 exe and can also be edited by hand (Notepad).


NOTE
----

 Pluto Arena is a fan project and is not affiliated with or endorsed by
 Blizzard Entertainment. StarCraft is a trademark of Blizzard
 Entertainment, Inc. Pluto Arena contains no StarCraft or Blizzard files.

 Pluto AI is by tscmoo (https://github.com/tscmoo/pluto) and is not
 included.

 The Pluto Remastered bridge is by lpflhh
 (https://github.com/lpflhh/Pluto-AI-Starcraft-Remaster), modified by
 Pluto Arena for the difficulty system. It contains BWAPI (LGPL v3) and
 MinHook (BSD license). License texts and the modified source files are
 in the native\licenses and native\source folders.
