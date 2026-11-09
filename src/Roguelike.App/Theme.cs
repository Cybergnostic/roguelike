using System.Globalization;
using System.Text.Json;
using Roguelike.Core.Items;
using Roguelike.Core.Map;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// The only place that knows what things look like. Glyphs and colours for kinds and
/// tiles come from data/looks.json; swap that (or this class) for a tileset later
/// without touching the simulation.
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

    private static readonly Look Unknown = new('?', Color.Magenta);

    // Declared before Looks: LoadLooks fills it, and static fields initialise in order.
    private static readonly Dictionary<string, string> RangedVerbs = [];
    private static readonly (Dictionary<string, Look> Kinds, Dictionary<string, Look> Tiles, Dictionary<string, Look> Items, int CorpseGlyph) Looks =
        LoadLooks(Path.Combine(AppContext.BaseDirectory, "data", "looks.json"));

    /// <summary>A corpse: the "corpse" glyph in a dim version of the dead kind's colour.</summary>
    public static Look ForCorpse(string kind, bool withered = false)
    {
        var c = ForKind(kind).Foreground;
        return withered
            ? new Look(Looks.CorpseGlyph, new Color(95, 90, 80))
            : new Look(Looks.CorpseGlyph, new Color(c.R / 2, c.G / 2, c.B / 2));
    }

    public static Look ForTile(Tile tile) =>
        Looks.Tiles.GetValueOrDefault(tile.ToString().ToLowerInvariant(), Unknown);

    public static Look ForKind(string kind) => Looks.Kinds.GetValueOrDefault(kind, Unknown);

    /// <summary>A kind's own verb for ranged attacks ("rangedVerb" in looks.json), if it has one.</summary>
    public static string? RangedVerb(string kind) => RangedVerbs.GetValueOrDefault(kind);


    /// <summary>By item id first, then by category ("weapon", "potion", ...); magic and better items take their tier's colour.</summary>
    public static Look ForItem(Roguelike.Core.Items.Item item)
    {
        var look = Looks.Items.TryGetValue(item.Def.Id, out var own) ? own
            : Looks.Items.GetValueOrDefault(item.Def.Category.ToString().ToLowerInvariant(), Unknown);
        return item.Rarity == Rarity.Common ? look : look with { Foreground = ForRarity(item.Rarity) };
    }

    /// <summary>The tier colours (docs/PROJECT.md §8): white, green, orange, red, gold, violet.</summary>
    public static Color ForRarity(Rarity rarity) => rarity switch
    {
        Rarity.Magic => new Color(90, 220, 90),
        Rarity.Rare => new Color(245, 150, 45),
        Rarity.Legendary => new Color(235, 60, 55),
        Rarity.Unique => new Color(240, 210, 80),
        Rarity.Artifact => new Color(185, 120, 245),
        _ => Text,
    };

    /// <summary>Debug reveal: cells the player hasn't seen, dimmer than visible ones.</summary>
    public static Color Revealed(Color c) => new((byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.65));

    /// <summary>How remembered-but-not-visible cells are drawn.</summary>
    public static Color Remembered(Color c) => new((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 2));

    private static readonly Color[] SquadPalette =
    [
        new(230, 90, 90), new(90, 160, 240), new(240, 200, 70), new(200, 110, 230),
        new(80, 210, 160), new(240, 140, 60), new(150, 200, 90), new(230, 120, 180),
    ];

    public static Color SquadColor(int squadId) => SquadPalette[squadId % SquadPalette.Length];

    /// <summary>Dim background tint marking which squad an actor belongs to.</summary>
    public static Color SquadTint(int squadId)
    {
        var c = SquadColor(squadId);
        return new Color(c.R / 4, c.G / 4, c.B / 4);
    }

    /// <summary>Heatmap colour: 0 = low (hot, near goals) to 1 = high (cold, far).</summary>
    public static Color Heat(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var hot = new Color(150, 40, 30);
        var cold = new Color(15, 25, 80);
        return new Color(
            (int)(hot.R + (cold.R - hot.R) * t),
            (int)(hot.G + (cold.G - hot.G) * t),
            (int)(hot.B + (cold.B - hot.B) * t));
    }

    private static (Dictionary<string, Look>, Dictionary<string, Look>, Dictionary<string, Look>, int) LoadLooks(string path)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), options);
        var root = doc.RootElement;
        int corpseGlyph = root.TryGetProperty("corpse", out var corpse) ? ReadGlyph(corpse.GetProperty("glyph")) : '%';
        return (ReadSection(root, "kinds"), ReadSection(root, "tiles"), ReadSection(root, "items"), corpseGlyph);
    }

    private static int ReadGlyph(JsonElement g) => g.ValueKind == JsonValueKind.Number ? g.GetInt32() : g.GetString()![0];

    private static Dictionary<string, Look> ReadSection(JsonElement root, string name)
    {
        var result = new Dictionary<string, Look>();
        if (!root.TryGetProperty(name, out var section))
            return result;
        foreach (var entry in section.EnumerateObject())
        {
            int glyph = ReadGlyph(entry.Value.GetProperty("glyph"));
            if (entry.Value.TryGetProperty("rangedVerb", out var verb))
                RangedVerbs[entry.Name] = verb.GetString()!;
            result[entry.Name] = new Look(glyph, ParseColor(entry.Value.GetProperty("color").GetString()!));
        }
        return result;
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        int rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }
}
