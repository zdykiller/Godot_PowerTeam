# Power Team Prototype Architecture

## Goal

Build a top-down 3D squad battler where one player commands an entire squad through a radial control stick. The same input device should produce different tactical behavior depending on squad archetype.

The first playable slice should answer only three questions:

1. Does moving a squad through a radial drag feel responsive?
2. Does lancer momentum create satisfying charge windows?
3. Does archer "hold ground and aim" create a distinct tactical rhythm?

## Core Play Loop

1. Drag the command wheel to move or orient the selected squad.
2. Build a squad-specific advantage window.
3. Commit into contact or ranged pressure.
4. Reposition before the enemy exploits your recovery.

The long-term game can expand into multiple squads, hero passives, and team-vs-team play, but the prototype should stay focused on one selected squad at a time.

## System Layout

- `GameRoot`
  - Bootstraps the prototype scene.
  - Manages squad selection.
  - Pulls command state from the wheel and routes it to the active squad.
  - Writes a readable debug HUD so tuning is visible.
- `CommandWheelControl`
  - Converts mouse drag into a normalized 2D command vector.
  - Draws the wheel so the input has clear feedback.
  - Stays generic. It does not know squad rules.
- `SquadController`
  - Owns one squad's locomotion, facing, and role logic.
  - Translates the shared command vector into role-specific behavior.
  - Exposes status text for HUD and tuning.

## Shared Command Semantics

All squads receive the same values:

- `vector`: normalized stick direction and strength
- `active`: whether the wheel is currently engaged

Each role interprets those values differently:

- `Lancer`
  - Strong outer drag means commit to movement.
  - Sustained speed builds charge power.
  - The design center is approach angle, momentum, and hit timing.
- `Archer`
  - Mid-strength drag means stand, face, and aim.
  - Large drag means reposition at low speed.
  - The design center is sight line, facing, and timing while stationary.

This is the critical design principle for the project: one input grammar, different tactical verbs.

## Godot Structure

- `scenes/main/Main.tscn`
  - Prototype entry scene with floor, light, camera, two squads, and HUD.
- `scripts/game/GameRoot.cs`
  - Orchestrates scene-level behavior.
- `scripts/input/CommandWheelControl.cs`
  - Visual radial control.
- `scripts/squads/SquadController.cs`
  - Shared squad motor and role logic.

## Recommended Next Milestones

1. Add enemy target dummies and impact resolution.
2. Replace debug-only role states with actual attack payloads.
3. Add formation footprint and collision response.
4. Split visuals from logic so one squad can spawn multiple unit actors.
5. Add network-safe command snapshots only after local feel is proven.

## Technical Notes

- Keep the prototype deterministic enough for replay-style tuning, but do not optimize for networking yet.
- Use `Node3D` squad roots at first; do not over-commit to per-unit physics before the control feel is validated.
- Introduce data resources only when role parameters start multiplying. Until then, exported fields are faster to tune.
