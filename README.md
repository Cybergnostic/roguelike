# Roguelike

A classical roguelike with a living dungeon: factions, squads, history-driven levels.

## Layout

| Project | Role |
|---|---|
| `src/Roguelike.Core` | The simulation. Uses GoRogue for algorithms (map generation, FOV, pathing). No rendering, input or text: actions in, events out. Deterministic from a seed. |
| `src/Roguelike.App` | SadConsole front end. `Theme.cs` decides how things look; `EventText.cs` turns events into messages. Replaceable. |
| `tests/Roguelike.Core.Tests` | Headless tests: determinism, map connectivity, long simulated runs. |

## Run

Requires the .NET 10 SDK (on Linux, `~/.dotnet` via Microsoft's `dotnet-install.sh` works without sudo).

```sh
dotnet run --project src/Roguelike.App
dotnet test
```

Controls: arrows / numpad / `hjklyubn` to move and attack, `.` or `5` to wait, `R` to restart after death, `Esc` to quit.
