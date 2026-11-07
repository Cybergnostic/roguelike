# Roguelike: agent instructions

**Read [docs/PROJECT.md](docs/PROJECT.md) first.** It has the vision, decisions, architecture
rules, current state, roadmap and known issues. Update it when any of those change.

Essentials:
- C# / .NET 10, GoRogue (algorithms) + SadConsole (display). The .NET SDK is at `~/.dotnet` and may
  not be on PATH: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- `src/Roguelike.Core` must never reference SadConsole or produce display text/colours.
  Actions in, events out. All randomness via `world.Rng`. Keep it deterministic.
- Build: `dotnet build Roguelike.slnx`. Test: `dotnet test`.
  Balance: `dotnet run --project tools/Roguelike.Sim -c Release -- --seeds 50`.
- Commit at milestones (feature works + tests pass) without asking. Don't push without asking.
