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
- **The nozzle** — position + velocity, two blocks:
  - Add **Set Position Shape (Cone / Cylinder)**: Shape Cone, Height Mode
    Volume, **Position Mode Volume** (Surface gives a hollow ring),
    Spawn Mode Random. Base Radius **0.1** (nozzle mouth), Height **0.5**,
    Arc 6.28 (full 360°). In the block's Inspector make sure the
    **Direction** composition is written — the velocity block reads it.
  - The cone block has **no angle input** — its spread is its geometry, so
    compute Top Radius from `ConeAngle`:
    `Top Radius = 0.1 + 0.5 × tan(ConeAngle°)`, as nodes:
    `ConeAngle` → Multiply (×0.01745, deg→rad) → **Tangent** → Multiply
    (×0.5, the Height) → Add (+0.1, the Base Radius) → Top Radius port.
    (Jet 8° ≈ 0.17 pencil; Creep 60° ≈ 0.97 trumpet.)
  - Add **Velocity from Direction & Speed (Change Speed)** *below* the cone
    block (block order matters — direction must be written first). It reads
    the cone's `direction` attribute automatically; wire
    `EmitSpeed × Random(0.8–1.2)` into **Speed**.
    ⚠ These velocity blocks are hidden by default in Unity 6: enable
    **Edit > Preferences > Visual Effects > Experimental Operators/Blocks**
    first. Avoid the *(Tangent)* variant — that's orbital/vortex motion.
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
- **Size over life** — don't use the *Set Size Over Life* block (its curve
  port can't be scaled by an exposed property). Use a plain **Set Size**
  block fed by a hand-built chain:
  - `Get Attribute: age` and `Get Attribute: lifetime` → **Divide** (life
    ratio, 0→1)
  - Divide → the Time input of a **Sample Curve** node. Curve keys:
    (0, 0.3), (0.2, 1.0) with flat tangent, (1, 1.0) — fast growth in the
    first 20% of life, then flat.
  - Sample Curve output × `ParticleSize` (Multiply) → Set Size's Size port.
  This replaces any Set Size in Initialize.
- **Flipbook Player** block: Mode **Frame Rate**, Frame Rate **Constant 16**
  (or Random 13–19 for per-puff variety). Note this block only advances the
  frame index — frame *blending* / motion vectors are configured on the
  Output context's **UV Mode** (§6), not here. Without motion vectors, use
  ~30 fps instead.

## 6. Output — the part that matters

Delete the default output. Add:

> **Output Particle URP Lit Quad**

Then select the output context and, in the graph **Inspector** panel, change
**Material Type → Six Way Smoke Lit**. (There is no separate "Six-Way" output
node in Unity 6 — six-way is a material type on the URP Lit output. The
lightmap slots appear once you switch it.)

Settings:

- **Positive/Negative axes lightmaps**: your two baked 6-way textures
  (see BAKING_GUIDE.md).
- **UV Mode** (in the context Inspector): **Flipbook Motion Blend** if your
  bake has motion vectors (a motion-vector map slot appears), else
  **Flipbook Blend** (linear crossfade). Plain **Flipbook** hard-cuts frames
  and visibly steps at 16 fps — avoid. Flip Book Size = 8×8 (or whatever
  you baked).
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
