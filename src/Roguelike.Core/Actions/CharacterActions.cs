using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Progression;

namespace Roguelike.Core.Actions;

/// <summary>
/// Spending XP. These never use up a turn (Perform returns false even on success), so
/// you can level up mid-fight without giving the monsters a free move.
/// </summary>
public sealed record RaiseAttributeAction(string AttributeId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Character is not { } character)
            return false;
        if (character.CheckRaise(AttributeId) is { } reason)
            return PickUpAction.Fail(world, actor, reason);

        CharacterActions.KeepingWounds(actor, () => character.Raise(AttributeId));
        world.Emit(new AttributeRaised(actor.Id, AttributeId, character.Score(AttributeId)));
        return false;
    }
}

public sealed record LearnSkillAction(string SkillId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Character is not { } character)
            return false;
        if (character.CheckLearn(SkillId) is { } reason)
            return PickUpAction.Fail(world, actor, reason);

        CharacterActions.KeepingWounds(actor, () => character.Learn(SkillId));
        world.Emit(new SkillLearned(actor.Id, SkillId, character.Rank(SkillId)));
        return false;
    }
}

internal static class CharacterActions
{
    /// <summary>Applies a change; extra max HP comes as extra current HP too (wounds stay wounds).</summary>
    public static void KeepingWounds(Actor actor, Action change)
    {
        int before = actor.MaxHp;
        change();
        actor.Hp = Math.Min(actor.MaxHp, actor.Hp + Math.Max(0, actor.MaxHp - before));
    }
}
