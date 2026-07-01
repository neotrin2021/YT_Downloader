# Fog Machine — Unity 6 / URP

A performance-first fog-machine system: one throttle from **turtle creep** to
**full jet blast**, discrete switchable presets with smooth crossfades, a
theatrical `Blast()`, and a governor that automatically backs emission off if
the GPU starts to sweat.

Design philosophy: **simulate offline, play back cheap.** The fluid sim is
baked to a 6-way-lit flipbook; the runtime is billboard particles in VFX
Graph — volumetric look, potato-friendly cost. The real enemy of thick fog is
**overdraw**, and every default here is tuned around that.

## Layout

```
unity/FogMachine/
├── Runtime/
│   ├── FogMachineController.cs    # throttle, presets, Blast(), Cut()
│   ├── FogMachinePreset.cs        # ScriptableObject preset + built-in five
│   └── FogPerformanceGovernor.cs  # frame-time watchdog → emission scale
├── Editor/
│   └── FogMachineControllerEditor.cs  # play-mode control desk
└── Docs/
    ├── VFXGRAPH_BUILD_GUIDE.md    # build the .vfx graph (10 min, exact recipe)
    └── BAKING_GUIDE.md            # bake 6-way flipbooks (Blender/Houdini/EmberGen)
```

## Quick start

1. Copy `unity/FogMachine/` into your project's `Assets/` folder
   (asmdefs included — it compiles in isolation).
2. Build the VFX Graph per `Docs/VFXGRAPH_BUILD_GUIDE.md`
   (a `.vfx` asset can't be shipped as text — the guide *is* the asset).
3. Bake or grab a flipbook per `Docs/BAKING_GUIDE.md`. To test the plumbing
   before you have a bake, any smoke flipbook + plain Lit output works;
   swap to Six-Way when the real textures land.
4. GameObject → **Visual Effect** (assign the graph) → **Fog Machine
   Controller** → optionally **Fog Performance Governor**.
5. Play. Use the inspector Control Desk, or from code:

```csharp
var fog = GetComponent<FogMachine.FogMachineController>();

fog.SetThrottle(1f);      // f***ing jet
fog.SetThrottle(0f);      // turtle
fog.ApplyPreset(2);       // "Steady Fill" (chain index, slow → fast)
fog.Blast(4f);            // slam to jet for 4 s, then ease back
fog.Cut();                // stop emitting; existing fog lives out its life
```

Built-in presets (used when none are assigned): **Turtle Creep, Lazy Drift,
Steady Fill, Heavy Pour, Jet Blast.** Make your own via
`Assets > Create > Fog Machine > Preset` — the controller sorts the chain by
`intensity`, and the throttle blends across the whole chain.

## The performance contract

- Graph capacity **640** particles — jets get their punch from **speed and
  turbulence**, not particle count.
- Presets keep `spawnRate × lifetime` under capacity.
- **Soft particles on**, few big puffs instead of many small ones, 2-octave
  turbulence, 16 fps flipbook + motion vectors.
- The governor trades spawn rate down (with slight size compensation) when
  smoothed frame time exceeds target by 15%, and creeps back only with clear
  headroom — no visible oscillation.
- If fog fills the screen and you need more: half-resolution transparent
  rendering in URP is the single biggest win. See the checklist at the end of
  the build guide.
