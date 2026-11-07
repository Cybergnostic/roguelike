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

    public static Look ForTile(Tile tile) => tile switch
    {
        Tile.Wall => new('#', new Color(140, 130, 120)),
        Tile.Floor => new('.', new Color(90, 90, 100)),
        _ => new('?', Color.Magenta),
    };

    public static Look ForKind(string kind) => kind switch
    {
        "player" => new('@', Color.White),
        "goblin" => new('g', new Color(110, 200, 70)),
        _ => new('?', Color.Magenta),
    };

    /// <summary>How remembered-but-not-visible cells are drawn.</summary>
    public static Color Remembered(Color c) => new((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 2));
}
