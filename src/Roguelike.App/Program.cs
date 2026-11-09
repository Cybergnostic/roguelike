using Roguelike.App;
using SadConsole;
using SadConsole.Configuration;

Settings.WindowTitle = "Roguelike";

// Usage: Roguelike.App [--seed N] [--class soldier|ranger|rogue|mystic] [--spectate] [--follow FILE [--surface] [--pace SECONDS]] [--dungeon]
int seedIndex = Array.IndexOf(args, "--seed");
if (seedIndex >= 0 && seedIndex + 1 < args.Length && ulong.TryParse(args[seedIndex + 1], out var seed))
    GameScreen.StartSeed = seed;
GameScreen.StartSpectating = args.Contains("--spectate");
int classIndex = Array.IndexOf(args, "--class");
if (classIndex >= 0 && classIndex + 1 < args.Length)
    GameScreen.StartClass = args[classIndex + 1];
int followIndex = Array.IndexOf(args, "--follow");
if (followIndex >= 0 && followIndex + 1 < args.Length)
    GameScreen.FollowFile = Path.GetFullPath(args[followIndex + 1]);
// Following starts in the dungeon unless --surface says the valley.
GameScreen.StartInDungeon = args.Contains("--dungeon") || (GameScreen.FollowFile is not null && !args.Contains("--surface"));
int paceIndex = Array.IndexOf(args, "--pace");
if (paceIndex >= 0 && paceIndex + 1 < args.Length && double.TryParse(args[paceIndex + 1], System.Globalization.CultureInfo.InvariantCulture, out var pace))
    GameScreen.FollowSecondsPerTurn = pace;

var config = new Builder()
    .SetWindowSizeInCells(GameScreen.Width, GameScreen.Height, zoom: false)
    .SetStartingScreen<GameScreen>()
    .IsStartingScreenFocused(true)
    .ConfigureFonts((fonts, _) =>
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fonts");
        fonts.UseCustomFont(Path.Combine(folder, "ui.font"));
        fonts.AddExtraFonts(Path.Combine(folder, "map.font"));
    });

Settings.ClearColor = Theme.Void;

Game.Create(config);
Game.Instance.Run();
Game.Instance.Dispose();
