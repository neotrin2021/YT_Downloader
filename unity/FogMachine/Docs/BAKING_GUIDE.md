# Baking Guide — Fog-Machine Flipbooks with 6-Way Lighting

The expensive fluid sim happens **once, offline, here**. Unity only ever plays
back textures. This guide covers what to bake, then how, per tool.

---

## What you're producing

Up to three textures, all 8×8 flipbook sheets (64 frames), 2048×2048
(256 px per cell — plenty for thick fog; it's low-frequency detail):

1. **Positive-axes lightmap** — RGB = the puff lit from **+X, +Y, +Z**
   (right, top, front), one axis per channel. **A = alpha/density.**
2. **Negative-axes lightmap** — RGB = lit from **−X, −Y, −Z**
   (left, bottom, back).
3. **Motion vectors** (optional but worth it) — RG = per-pixel 2D flow to the
   next frame. Lets Unity interpolate, so 16 baked fps plays like 60.

Unity's Six-Way Smoke Lit output consumes exactly the positive/negative pair.
(Channel order conventions occasionally shift between versions — if lighting
looks inverted on an axis, check the output's channel mapping options in the
graph inspector rather than re-baking.)

## Sim recipe for fog-machine thickness (any tool)

The look is: dense, cool, slow, ground-hugging.

| Setting | Value | Why |
|---------|-------|-----|
| Density/fuel emission | High | It's a fog *machine*, not a cigarette |
| Dissipation/density decay | Very low | Fog hangs around |
| Buoyancy / temperature | Zero to slightly **negative** | Chilled glycol fog sinks |
| Turbulence/noise | Large-scale, low strength | Billowy rolls, not wisps |
| Velocity | Low | The interesting motion is in the loopable roll, not travel |
| Sim resolution | 128–192 voxels | Thick fog doesn't need more; wispy would |
| Frames | 64, simmed to **loop** (see below) | One 8×8 sheet |

**Looping:** sim ~90 frames, use the last 64, and crossfade the first/last 8
frames in compositing — or use your tool's loop feature (EmberGen has one
built in; in Blender/Houdini do the crossfade trick).

**Frame the render** so the puff fills the cell without clipping at any frame.
Render square (512×512 per frame is fine, downscale to 256 when sheeting).

---

## Tool A — Blender (free, Mantaflow)

1. **Domain + emitter**: Quick Effects > Quick Smoke on a small emitter mesh,
   then set domain resolution 128–192, `Density 1.0`, `Temperature Diff.` ~0
   or slightly negative, `Dissolve` off or very slow, adaptive domain ON.
2. **Noise**: enable domain Noise (wavelet upres) at 2× — cheap extra billow.
3. **The 6-light rig** (this is how you get 6-way maps without native support):
   - Six **Sun** lamps aimed down each axis: +X, −X, +Y, −Y, +Z, −Z.
   - Render **6 passes**, one lamp enabled at a time (white smoke, black
     background, Cycles or Eevee-Next volumetrics).
   - Also render one **alpha/density pass** (emission-only or straight alpha).
   - Automate it: keyframe light visibility per pass, or 6 View Layers with
     per-layer light collections — one render click total.
4. **Composite** into the two sheets:
   - Positive map: pass(+X)→R, pass(+Y)→G, pass(+Z)→B, alpha→A.
   - Negative map: pass(−X)→R, pass(−Y)→G, pass(−Z)→B.
   - Do the channel packing in the Blender compositor, or export EXR
     sequences and pack with any tool.
5. **Sheet the frames** (ImageMagick):

   ```bash
   montage pos_*.png -tile 8x8 -geometry 256x256+0+0 -background none PositiveAxes_8x8.png
   montage neg_*.png -tile 8x8 -geometry 256x256+0+0 -background none NegativeAxes_8x8.png
   ```

6. Motion vectors: Blender's vector pass is camera-space and fiddly to convert;
   honestly, skip MVs on the Blender path and bake 64 frames at 30 fps playback
   instead. It still looks great for slow fog.

## Tool B — Houdini (Apprentice to learn; **Indie for anything commercial** — Apprentice output is non-commercial + watermarked)

1. Pyro sim: sparse pyro, density source, `dissipation` ~0.01,
   `buoyancy` ≈ 0/negative, gas resize on. 150–200 voxel div.
2. Same 6-light rig idea (six directional lights, one per render pass) in
   Karma/Mantra, or use COPs to pack channels.
3. Houdini Labs has flipbook/texture-sheet export tools (Labs "Flipbook
   Textures" / Sprite Sheet nodes) that automate sheeting — check the Labs
   shelf, it may also emit motion vectors for you.

## Tool C — EmberGen / BrazeFX (when you get access)

Both are purpose-built for this: pick the fog preset, and export
**6-way lightmaps + motion vectors + looping flipbook** directly from the
export panel. The whole guide above collapses to "click export." Your Unity
side doesn't change at all — same two textures, same graph.

---

## Import settings in Unity

- sRGB **off** for the lightmaps (they're data, linear).
- Alpha: from input texture; **Alpha Is Transparency: off** (premultiplied-ish
  data, let the shader interpret).
- Compression: BC7 (quality) — fog gradients band badly under BC1.
- Mipmaps ON, streaming optional.
- Wrap: Clamp. Filter: Bilinear/Trilinear.

Then assign the pair in the Six-Way output per VFXGRAPH_BUILD_GUIDE.md §6.
