using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Simulation;

/// <summary>Blows each way between a fresh character of a class and one monster, on average, nobody crowding anybody.</summary>
public sealed record LethalityRow(string Class, string Monster, string Rank, double PlayerHits, double PlayerSwings, double MonsterHits, double MonsterSwings, string Weapon);

/// <summary>
/// The hits-to-kill table behind the combat model (COMBAT_AND_MAGIC.md §1): how many landed blows (and how
/// many tries, with misses) each side needs, with the starting kit at level 1. Pure arithmetic on the rules.
/// </summary>
public static class Lethality
{
    public static List<LethalityRow> Table(IEnumerable<string>? monsters = null)
    {
        var content = Content.ContentDatabase.Default;
        var kinds = (monsters ?? content.Monsters.Values.Where(m => m.Npc is null && m.Role != AI.Squads.SquadRole.Civilian).Select(m => m.Id)).ToList();
        var rows = new List<LethalityRow>();
        foreach (var cls in content.Classes)
        {
            var world = World.Create(1, classId: cls.Id);
            foreach (var other in world.Actors.Where(a => a != world.Player))
                other.Hp = 0;
            world.Step(new WaitAction());
            var player = world.Player;
            var spot = world.Map.Tiles.Positions().First(p => world.IsFree(p) && World.Distance(p, player.Position) >= 3);
            foreach (var kind in kinds)
            {
                var monster = world.SpawnMonster(kind, spot);
                var squad = world.SquadOf(monster)!;
                squad.State = AI.Squads.SquadState.Engage;
                // The player's best plain attack: the bow at range if it has one, else melee; a Mystic's bolt.
                var bolt = player.Character!.KnownAbilities.FirstOrDefault(a => a.Kind == Content.AbilityKind.Bolt);
                (double avg, int chance, string weapon) best = (0, 0, "");
                foreach (var (ranged, power, name) in new (bool, int, string)[]
                         {
                             (false, player.Attack, "melee"),
                             (true, player.Equipment?.Bow is null ? 0 : player.RangedAttack, "bow"),
                             (true, bolt is null ? 0 : bolt.Power + player.Bonus(Progression.Stat.Magic), "bolt"),
                         })
                {
                    if (power <= 0) continue;
                    double avg = AverageBlow(world, power, monster.Defense + (ranged ? monster.Equipment?.Armor?.Def.ArrowGuard ?? 0 : 0));
                    // Two blades strike twice a turn, about.
                    if (!ranged && player.Equipment?.OffHand is not null)
                        avg *= 1.6;
                    int chance = world.HitChance(player, monster, ranged && name == "bow");
                    if (avg * chance > best.avg * best.chance)
                        best = (avg, chance, name);
                }
                double playerHits = Math.Ceiling(monster.MaxHp / best.avg);
                double monsterAvg = AverageBlow(world, monster.Attack, player.Defense);
                double monsterHits = Math.Ceiling(player.MaxHp / monsterAvg);
                int monsterChance = world.HitChance(monster, player, ranged: false);
                rows.Add(new LethalityRow(cls.Id, kind, monster.Rank.ToString(), playerHits, playerHits * 100 / best.chance,
                    monsterHits, monsterHits * 100 / monsterChance, best.weapon));
                monster.Hp = 0;
                world.Step(new WaitAction());
            }
        }
        return rows;
    }

    /// <summary>The mean of attack ± spread, less armour in full, at least the minimum.</summary>
    private static double AverageBlow(World world, int power, int armour)
    {
        var rules = world.Content.Combat;
        double total = 0;
        int n = 0;
        for (int spread = 100 - rules.DamageSpread; spread <= 100 + rules.DamageSpread; spread++, n++)
        {
            int blow = (int)Math.Round(power * spread / 100.0);
            total += Math.Max(rules.MinDamage, blow - Math.Min(armour, blow * rules.ArmourStopsAtMostPercent / 100));
        }
        return total / n;
    }
}
