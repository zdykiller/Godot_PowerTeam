# AGENTS.md

## Project Scope

This is a Godot 4 C# prototype for a top-down 3D squad tactics game.

The current core loop is:

- Single-player battle first.
- 3v3 squad combat.
- The player directly controls one squad with the command wheel.
- Allied squads are AI-controlled and can receive simple tactic commands.
- Enemy squads are AI-controlled.
- Each side has a base; destroying the enemy base wins, losing the player base loses.

Do not pivot the project into a full RTS with box selection or direct multi-squad micro unless explicitly requested.

## Gameplay Direction

Preserve these design decisions:

- The command wheel expresses intent, not direct per-soldier control.
- The player should feel the controlled squad's movement, charge, disengage, and positioning.
- Allies should be directed through broad tactics such as Assault, Hold, Focus, and Regroup.
- Squad survival uses aggregate HP and morale.
- Visual casualties can hide individual soldiers, but combat logic should remain squad-level.
- HP reaching 0 or morale reaching 0 causes routing and regrouping at base.
- Movement and positioning should matter through charge buildup, flank/rear morale pressure, engagement stickiness, and enemy warning telegraphs.

Prefer changes that improve readable combat feedback before adding more systems.

## Technical Direction

- Use C# for current development.
- Keep Godot scene-bound scripts in the main project.
- Pure logic can be extracted later, but do not split into multiple assemblies prematurely.
- Prefer small, clear systems over complex abstractions.
- Keep runtime-generated UI acceptable for prototype features; move to scenes only when UI stabilizes.

## Assets

- Keep committed assets small enough for normal Git.
- Do not introduce Git LFS unless explicitly requested.
- Avoid committing large raw/source assets.
- KayKit assets are acceptable and currently used for prototype units.
- If adding third-party assets, include license files where applicable.

## Verification

After code changes, run:

```sh
dotnet build
```

When practical, also run Godot headless project load:

```sh
'/Applications/Godot/Godot_v4.6.2-stable_mono_macos.universal/Godot_mono.app/Contents/MacOS/Godot' --headless --path /Users/admin/WorkProjects/GodotProjects/power-team --quit
```

## Git

- Commit completed work.
- Push after each successful commit.
- Do not rewrite history unless explicitly requested.
- Do not commit generated build folders such as `.godot/`, `bin/`, or `obj/`.
- `*.csproj` and `*.sln` should be committed for this Godot C# project.
