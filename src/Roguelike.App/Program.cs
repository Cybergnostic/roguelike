using Roguelike.App;
using SadConsole;
using SadConsole.Configuration;

Settings.WindowTitle = "Roguelike";

// Usage: Roguelike.App [--seed N]
int seedIndex = Array.IndexOf(args, "--seed");
if (seedIndex >= 0 && seedIndex + 1 < args.Length && ulong.TryParse(args[seedIndex + 1], out var seed))
    GameScreen.StartSeed = seed;

var config = new Builder()
    .SetWindowSizeInCells(GameScreen.Width, GameScreen.Height, zoom: true)
    .SetStartingScreen<GameScreen>()
    .IsStartingScreenFocused(true)
    .ConfigureFonts(true);

Game.Create(config);
Game.Instance.Run();
Game.Instance.Dispose();
