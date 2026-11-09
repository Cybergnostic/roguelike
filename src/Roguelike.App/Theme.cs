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
    /// <summary>Variants: other glyphs a cell of this tile can show. Background: the tile's own ground colour.</summary>
    public readonly record struct Look(int Glyph, Color Foreground, IReadOnlyList<int>? Variants = null, Color? Background = null);

    // The palette of the Enhanced ASCII prototype.
    public static readonly Color Void = Hex("#07090b");
    public static readonly Color Ground = Hex("#0b0d10");
    public static readonly Color LitGround = Hex("#0d1216");
    public static readonly Color RememberedGround = Hex("#0a0c0f");
    public static readonly Color Background = LitGround;
    public static readonly Color PanelBackground = Hex("#101316");
    public static readonly Color SidebarBackground = PanelBackground;
    public static readonly Color LogBackground = Hex("#0c0f12");
    public static readonly Color PopupBackground = Hex("#171c21");
    public static readonly Color Line = Hex("#262c32");
    public static readonly Color Bright = Hex("#f1ece1");
    public static readonly Color Text = Hex("#d9d4c7");
    public static readonly Color TextDim = Hex("#c7c2b5");
    public static readonly Color Intent = Hex("#a7afb4");
    public static readonly Color Danger = Hex("#ff7a5c");
    public static readonly Color DangerSoft = Hex("#ff9a82");
    public static readonly Color Good = Hex("#9fc27a");
    public static readonly Color Warning = Hex("#e8a33d");
    public static readonly Color Wary = Hex("#ffc04d");
    public static readonly Color Teal = Hex("#7cc7c0");
    public static readonly Color Gold = Hex("#e8c98b");
    public static readonly Color Player = Hex("#fff4d6");
    public static readonly Color BarEmpty = Line;
    public static readonly Color FleeingBackground = Hex("#0d1a22");
    public static readonly Color HostileBackground = Hex("#4a1a12");

    private static readonly Color Firelight = Hex("#ffd79a");
    private static readonly Color Ember = Hex("#54300f");
    private static readonly Color Dusk = Hex("#5b656e");

    public static Color Hex(string hex) => ParseColor(hex);

    public static Color Mix(Color a, Color b, double t) =>
        new((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    public static Color Scale(Color c, double k) =>
        new(Math.Min(255, (int)Math.Round(c.R * k)), Math.Min(255, (int)Math.Round(c.G * k)), Math.Min(255, (int)Math.Round(c.B * k)));

    /// <summary>A cell in view, warmed by however much light reaches it.</summary>
    public static (Color Foreground, Color Background) Lit(Look look, double light) => (
        Scale(Mix(look.Foreground, Firelight, Math.Min(0.45, light * 0.5)), Math.Min(1.25, 0.72 + light * 0.6)),
        Mix(look.Background ?? LitGround, Ember, Math.Min(0.8, light * 0.55)));

    /// <summary>The same glyph in heavier type (the font atlas keeps bold copies 256 places on).</summary>
    public static int Bold(int glyph) => glyph + 256;

    /// <summary>Which of a tile's glyphs this cell shows; fixed per cell, so the ground doesn't shimmer.</summary>
    public static int GlyphAt(Look look, int x, int y) =>
        look.Variants is { Count: > 0 } variants
            ? variants[(int)(((uint)(x * 73856093) ^ (uint)(y * 19349663)) % 100) % variants.Count]
            : look.Glyph;

    /// <summary>Faded for older lines in the message log.</summary>
    public static Color Fade(Color c) => Mix(LogBackground, c, 0.78);

    private static readonly Look Unknown = new('?', Color.Magenta);

    // Declared before Looks: LoadLooks fills it, and static fields initialise in order.
    private static readonly Dictionary<string, string> RangedVerbs = [];
    private static readonly Dictionary<string, string> FactionSounds = [];
    private static readonly Dictionary<(string, string), List<string>> Grunts = [];
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

    /// <summary>A grunt for a popup, chosen by the number given; null if this faction has none of this kind.</summary>
    public static string? Grunt(string faction, string kind, int pick) =>
        Grunts.TryGetValue((faction, kind), out var lines) && lines.Count > 0 ? lines[Math.Abs(pick) % lines.Count] : null;

    /// <summary>What a faction sounds like in a fight (looks.json).</summary>
    public static string FactionSound(string faction) => FactionSounds.GetValueOrDefault(faction, "someone fighting");

    /// <summary>A kind's own verb for ranged attacks ("rangedVerb" in looks.json), if it has one.</summary>
    public static string? RangedVerb(string kind) => RangedVerbs.GetValueOrDefault(kind);


    /// <summary>By item id first, then by category ("weapon", "potion", ...); magic and better items take their tier's colour.</summary>
    public static Look ForItem(Roguelike.Core.Items.Item item)
    {
        var look = Looks.Items.TryGetValue(item.Def.Id, out var own) ? own
            : Looks.Items.GetValueOrDefault(item.Def.Category.ToString().ToLowerInvariant(), Unknown);
        return item.Rarity == Rarity.Common ? look : look with { Foreground = ForRarity(item.Rarity) };
    }

    /// <summary>A dim background behind elites and bosses, so they stand out from their kind.</summary>
    public static Color ForRank(Roguelike.Core.Entities.Rank rank) =>
        rank == Roguelike.Core.Entities.Rank.Boss ? new Color(80, 25, 95) : new Color(95, 65, 15);

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
    public static Color Remembered(Color c) => Scale(Mix(c, Dusk, 0.7), 0.66);

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
        if (root.TryGetProperty("grunts", out var grunts))
            foreach (var faction in grunts.EnumerateObject())
                foreach (var kind in faction.Value.EnumerateObject())
                    Grunts[(faction.Name, kind.Name)] = kind.Value.EnumerateArray().Select(e => e.GetString()!).ToList();
        if (root.TryGetProperty("factionSounds", out var sounds))
            foreach (var entry in sounds.EnumerateObject())
                FactionSounds[entry.Name] = entry.Value.GetString()!;
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
            var variants = entry.Value.TryGetProperty("glyphs", out var many) ? many.EnumerateArray().Select(ReadGlyph).ToList() : null;
            var ground = entry.Value.TryGetProperty("background", out var b) ? ParseColor(b.GetString()!) : (Color?)null;
            result[entry.Name] = new Look(glyph, ParseColor(entry.Value.GetProperty("color").GetString()!), variants, ground);
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
