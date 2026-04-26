# Art Asset Guide

Drop generated or authored Godot-ready models into these folders:

- `assets/units/lancer/lancer.glb`
- `assets/units/archer/archer.glb`
- `assets/buildings/base/base.glb`

Preferred format:

- `.glb`
- Low-poly is fine.
- One model per file.
- Materials should be simple and editable.

Scale and orientation:

- Unit models should be roughly `1.6` to `2.0` Godot units tall.
- Model origin should be at the feet center.
- Forward direction should face Godot `-Z`.
- Up direction should be `+Y`.

Optional animation names:

- `idle`
- `run`
- `attack`
- `hit`
- `rout`
- `death`

Minimum useful set:

- Static `lancer.glb`
- Static `archer.glb`
- Static `base.glb`

If animations are missing, the prototype will still use formation movement and simple visual pulses.
