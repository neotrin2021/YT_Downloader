# VFX Graph Build Guide — Fog Machine (Unity 6, URP)

This is the exact graph the C# controller expects. It takes ~10 minutes to
build in the editor. (`.vfx` assets are editor-serialized with internal GUIDs,
so they can't be shipped as hand-written text — this guide is the asset.)

Requirements: **Unity 6**, **URP**, **Visual Effect Graph** package installed
(Window > Package Manager > Visual Effect Graph).

---

## 1. Create the asset

1. `Assets > Create > Visual Effects > Visual Effect Graph` → name it `FogMachine.vfx`.
2. Open it. Delete nothing yet — we'll repurpose the default Spawn/Initialize/Update/Output stack.

## 2. Exposed properties (Blackboard)

Create these in the Blackboard, **exact names and types** — the controller
binds by name and will log which ones it can't find:

| Name               | Type  | Default | Drives |
|--------------------|-------|---------|--------|
| `SpawnRate`        | Float | 60      | Spawn context rate |
| `EmitSpeed`        | Float | 3.5     | Initial velocity magnitude |
| `ConeAngle`        | Float | 30      | Emission cone half-angle (degrees) |
| `ParticleLifetime` | Float | 6       | Lifetime |
| `ParticleSize`     | Float | 2.5     | Grown puff size (meters) |
| `Turbulence`       | Float | 1.2     | Turbulence block intensity |
| `Drag`             | Float | 2.5     | Linear drag |
| `Buoyancy`         | Float | -0.35   | Vertical force (negative sinks) |
| `Density`          | Float | 0.9     | Output alpha multiplier |
| `FogColor`         | Color | White   | Output tint |

Tick **Exposed** on every one.

## 3. Spawn context

- Keep a single **Constant Spawn Rate** block → connect `SpawnRate`.

## 4. Initialize Particle

- **Capacity: 640.** (Highest built-in preset is 220/s × 2.2 s life ≈ 484 alive;
  640 leaves headroom. Capacity is a hard GPU allocation — don't set it to 100k
  "just in case", that's exactly the choke we're avoiding.)
- **Set Lifetime** ← `ParticleLifetime` (add ±15% randomness with a
  `Random Number` multiplied in, optional but nicer).
- **Set Velocity from Direction & Speed (New Direction)** — or simplest:
  - Add **Set Position (Cone / Arc Cone)**: radius ~0.1 (nozzle mouth),
    arc 360°. This is the nozzle.
  - Add **Set Velocity from Direction & Speed**: direction = cone direction
    output; speed ← `EmitSpeed` (randomize ±20%).
  - Wire `ConeAngle` into the cone's angle input (convert degrees as needed —
    the cone block takes an angle input; multiply by `π/180` with a
    `Multiply` node if it wants radians).
- **Set Size** ← small start value (e.g. `ParticleSize × 0.3`) — puffs grow in Update.
- **Set Angle (Z)** ← `Random Number [0, 360]` so flipbook cells don't all face the same way.
- **Set Angular Velocity (Z)** ← `Random [-8, 8]` deg/s. Slow roll sells volume.
- **Set Tex Index** ← `Random [0, 63]` so puffs start on different flipbook frames.

## 5. Update Particle

- **Turbulence** block: intensity ← `Turbulence`, frequency ~0.4, octaves 2.
  (Two octaves is plenty for thick fog — it's low-frequency mass, not wisps.
  More octaves = more GPU for detail the look doesn't need.)
- **Linear Drag** ← `Drag`.
- **Force**: `(0, Buoyancy, 0)`. Negative = the fog sinks and pools like real
  chilled fog-machine output.
- **Set Size over Life**: curve from `ParticleSize × 0.3` → `ParticleSize`,
  fast growth in the first 20% of life, then flat. (Sample the curve, multiply
  by `ParticleSize`.)
- **Flipbook Player** block: mode Motion Vectors if your bake has them,
  frame rate ~16 (motion vectors make 16 look like 60; without MVs use ~30
  and Blend mode = Linear).

## 6. Output — the part that matters

Delete the default output. Add:

> **Output Particle Six-Way Smoke Lit Quad**
> (in the node search: "Six Way")

Settings:

- **Positive/Negative axes lightmaps**: your two baked 6-way textures
  (see BAKING_GUIDE.md). Flipbook layout: set **UV Mode = Flipbook**,
  Flip Book Size = 8×8 (or whatever you baked).
- **Use Motion Vectors**: on, if baked.
- **Blend Mode: Alpha.**
- **Soft Particles: ON**, fade distance ~0.5–1 m. Non-negotiable for fog —
  this is what melts it into floors and walls instead of hard-clipping.
- **Alpha multiplier** ← `Density`. **Color / tint** ← `FogColor`.
- Enable **Shadows: off** (receiving via 6-way is the point; casting from
  transparents is a perf trap).
- **Sort: Off** if the fog is a single homogeneous mass (cheaper); turn
  Auto sorting on only if you see popping.

URP note: in your URP Renderer settings, make sure **Depth Texture** is
enabled (Soft Particles need it).

## 7. Scene setup

1. Empty GameObject → add **Visual Effect** → assign `FogMachine.vfx`.
2. Add **Fog Machine Controller** (auto-requires the VisualEffect).
3. Optionally add **Fog Performance Governor** (same or any object) and drag
   it into the controller's Governor slot.
4. Point the object's forward/up axis where the nozzle should fire.
5. Enter Play mode → use the **Control Desk** in the inspector:
   preset buttons (Turtle Creep → Jet Blast), the throttle slider, BLAST, Cut.

No preset assets needed — the controller falls back to the built-in five.
To customize: `Assets > Create > Fog Machine > Preset`, tune, and add to the
controller's list (it sorts by `intensity` automatically).

---

## Performance checklist (the "don't choke my card" contract)

| Rule | Why |
|------|-----|
| Capacity 640, not thousands | Hard VRAM/compute allocation |
| Few big particles, not many small | **Overdraw is the real cost of thick fog** |
| Soft particles on | Avoids hard edges *and* full-quad waste at intersections |
| 2-octave turbulence | Thick fog is low-frequency; extra octaves buy nothing |
| Flipbook + 6-way, no raymarch | Volumetric look at billboard price |
| Motion vectors, 16 fps flipbook | Quarter the texture memory, same smoothness |
| Governor on | Auto-backs-off spawn rate if frame time blows budget |
| Half-res rendering (optional) | If fog fills the screen, render transparents at half res and upscale — biggest single overdraw win |

Profile with the **Frame Debugger** — if the fog pass is expensive, it's
overdraw: reduce spawn rate / size before touching anything else.
