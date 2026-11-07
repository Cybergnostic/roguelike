using Roguelike.App;
using SadConsole;
using SadConsole.Configuration;

Settings.WindowTitle = "Roguelike";

var config = new Builder()
    .SetWindowSizeInCells(GameScreen.Width, GameScreen.Height, zoom: true)
    .SetStartingScreen<GameScreen>()
    .IsStartingScreenFocused(true)
    .ConfigureFonts(true);

Game.Create(config);
Game.Instance.Run();
Game.Instance.Dispose();
