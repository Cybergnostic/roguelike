using Roguelike.Core.Simulation;

namespace Roguelike.App;

/// <summary>
/// Follow mode (--follow FILE): the window plays the commands written to FILE, one per line, as
/// they are appended (the same commands as tools/Roguelike.Play, via CommandPlayer). This is how
/// you watch an agent play: it decides with the text tool and appends to the file; the window
/// animates each command at a watchable pace. Keyboard play is disabled while following.
/// </summary>
public sealed partial class GameScreen
{
    /// <summary>Command file to follow, from --follow.</summary>
    public static string? FollowFile { get; set; }

    private const double FollowSecondsPerTurn = 0.09;

    private CommandPlayer? _commands;
    private IEnumerator<Roguelike.Core.Actions.GameAction>? _following;
    private int _followedLines;
    private double _followTimer;

    private bool IsFollowing => FollowFile is not null;

    private void UpdateFollow(TimeSpan delta)
    {
        _followTimer += delta.TotalSeconds;
        if (_followTimer < FollowSecondsPerTurn || _world.IsOver)
            return;
        _followTimer = 0;
        _commands ??= new CommandPlayer(_world);

        // Free actions (spending XP, failed shots) don't take a turn: run through them at once.
        for (int guard = 0; guard < 50; guard++)
        {
            if (_following is null && !StartNextCommand())
                return;
            if (_following!.MoveNext())
            {
                int turn = _world.Turn;
                PlayTurn(_following.Current);
                Draw();
                if (_world.Turn != turn)
                    return;
                continue;
            }
            if (_commands.StopReason is { } why)
                AddMessage($"  [stopped: {why}]", Theme.Intent);
            _following = null;
            Draw();
        }
    }

    /// <summary>Reads the next unplayed line of the command file, if one has been written.</summary>
    private bool StartNextCommand()
    {
        string[] lines;
        try
        {
            lines = File.Exists(FollowFile) ? File.ReadAllLines(FollowFile!) : [];
        }
        catch (IOException)
        {
            return false; // being written; try again next tick
        }
        while (_followedLines < lines.Length)
        {
            var line = lines[_followedLines++].Split('#')[0].Trim();
            if (line.Length == 0)
                continue;
            AddMessage($"> {line}", Theme.Warning);
            _following = _commands!.Run(line).GetEnumerator();
            return true;
        }
        return false;
    }
}
