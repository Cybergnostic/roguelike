using Roguelike.Core.Map;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// The only place that knows what things look like. Swap this for a tileset
/// (or load it from a data file) without touching the simulation.
/// </summary>
public static class Theme
{
    public readonly record struct Look(int Glyph, Color Foreground);

    public static readonly Color Background = Color.Black;
    public static readonly Color Text = new(200, 200, 200);
    public static readonly Color Danger = new(230, 80, 60);
    public static readonly Color Good = new(120, 220, 120);
    public static readonly Color Warning = new(235, 180, 60);
    public static readonly Color Intent = new(150, 160, 190);
    public static readonly Color SidebarBackground = new(18, 18, 26);
    public static readonly Color BarEmpty = new(40, 40, 50);
    public static readonly Color FleeingBackground = new(60, 40, 0);

    public static Look ForTile(Tile tile) => tile switch
    {
        Tile.Wall => new('#', new Color(140, 130, 120)),
        Tile.Floor => new('.', new Color(90, 90, 100)),
        _ => new('?', Color.Magenta),
    };

    public static Look ForKind(string kind) => kind switch
    {
        "player" => new('@', Color.White),
        "goblin_warrior" => new('g', new Color(110, 200, 70)),
        "goblin_archer" => new('g', new Color(220, 200, 80)),
        "goblin_shaman" => new('g', new Color(190, 110, 230)),
        "goblin_runner" => new('g', new Color(90, 200, 220)),
        _ => new('?', Color.Magenta),
    };

    /// <summary>How remembered-but-not-visible cells are drawn.</summary>
    public static Color Remembered(Color c) => new((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 2));
}
