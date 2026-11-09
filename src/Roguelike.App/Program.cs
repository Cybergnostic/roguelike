using Roguelike.App;
using SadConsole;
using SadConsole.Configuration;

Settings.WindowTitle = "Roguelike";

// Usage: Roguelike.App [--seed N] [--spectate] [--follow FILE] [--dungeon]
int seedIndex = Array.IndexOf(args, "--seed");
if (seedIndex >= 0 && seedIndex + 1 < args.Length && ulong.TryParse(args[seedIndex + 1], out var seed))
    GameScreen.StartSeed = seed;
GameScreen.StartSpectating = args.Contains("--spectate");
int followIndex = Array.IndexOf(args, "--follow");
if (followIndex >= 0 && followIndex + 1 < args.Length)
    GameScreen.FollowFile = Path.GetFullPath(args[followIndex + 1]);
GameScreen.StartInDungeon = args.Contains("--dungeon") || GameScreen.FollowFile is not null;

var config = new Builder()
    .SetWindowSizeInCells(GameScreen.Width, GameScreen.Height, zoom: true)
    .SetStartingScreen<GameScreen>()
    .IsStartingScreenFocused(true)
    .ConfigureFonts(true);

Game.Create(config);
Game.Instance.Run();
Game.Instance.Dispose();
