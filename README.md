# Roguelike

A classical roguelike with a living dungeon: factions, squads, history-driven levels.
Project handbook (vision, architecture, roadmap): [docs/PROJECT.md](docs/PROJECT.md).
Long-term world and exploration direction: [docs/WORLD_DESIGN_VISION.md](docs/WORLD_DESIGN_VISION.md).
Modern UI and visual direction: [docs/UI_DESIGN_VISION.md](docs/UI_DESIGN_VISION.md).
Ongoing design ideas and brainstorming: [docs/IDEAS.md](docs/IDEAS.md).

## Layout

| Project | Role |
|---|---|
| `src/Roguelike.Core` | The simulation. Uses GoRogue for algorithms (map generation, FOV, pathing). No rendering, input or text: actions in, events out. Deterministic from a seed. |
| `src/Roguelike.App` | SadConsole front end. `Theme.cs` decides how things look; `EventText.cs` turns events into messages. Replaceable. |
| `tools/Roguelike.Sim` | Headless simulation report: plays many seeded games with a bot and prints statistics. |
| `tests/Roguelike.Core.Tests` | Headless tests: determinism, map connectivity, squad behaviour. |

## Run

Requires the .NET 10 SDK (on Linux, `~/.dotnet` via Microsoft's `dotnet-install.sh` works without sudo).

```sh
dotnet run --project src/Roguelike.App                         # play
dotnet run --project src/Roguelike.App -- --seed 640 --spectate # watch the dungeon run itself
dotnet run --project tools/Roguelike.Sim -c Release -- --seeds 50
dotnet test
```

Controls: arrows / numpad / `hjklyubn` to move and attack, `.` `5` or `Space` to wait,
`R` to restart after death, `Esc` to quit.

Items: `g` pick up, `i` inventory, `e` equip, `q` drink, `d` drop, `f` fire bow.
Character: `c` opens the sheet; press a letter to spend XP on an attribute or skill (free action).

Debug views: `F1` reveal map, `F2` squads, `F3` heatmaps, `F5` spectator mode, `P` autoplay (the sim's bot plays for you; while spectating, time passes).
