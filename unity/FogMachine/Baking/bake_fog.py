"""Headless fog-machine flipbook bake.

Builds a Mantaflow smoke sim tuned for fog-machine thickness, bakes it,
then renders every frame once with six axis-aligned suns split into Cycles
light groups (one render = all six directional lighting passes + alpha).

Output: per-frame single-layer EXRs per light group under out/exr/.
Post-processing (loop crossfade, channel packing, montage) happens in
pack_fog.py.
"""
import bpy
import math
import os
import sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
CACHE = os.path.join(OUT, "cache")
EXR = os.path.join(OUT, "exr")
os.makedirs(CACHE, exist_ok=True)
os.makedirs(EXR, exist_ok=True)

FRAME_START = 1
FRAME_END = 88          # sim length; pack step uses frames 16..87 (72 frames)
RES = 512               # render size per frame
SIM_RES = 112           # voxel divisions on the largest axis

log = lambda *a: (print("[bake]", *a), sys.stdout.flush())

# ---------------------------------------------------------------- scene reset
for _o in list(bpy.data.objects):
    bpy.data.objects.remove(_o, do_unlink=True)
scene = bpy.context.scene
scene.frame_start = FRAME_START
scene.frame_end = FRAME_END

# Black world: light-group passes must contain sun light only.
world = bpy.data.worlds.new("world")
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs["Color"].default_value = (0, 0, 0, 1)
bg.inputs["Strength"].default_value = 0.0
scene.world = world

# ---------------------------------------------------------------- domain
bpy.ops.mesh.primitive_cube_add(size=1)
domain = bpy.context.active_object
domain.name = "FogDomain"
domain.scale = (1.6, 1.6, 1.6)          # 3.2 m cube
domain.location = (0, 0, 1.6)
bpy.ops.object.transform_apply(scale=True)

dmod = domain.modifiers.new(name="Fluid", type="FLUID")
dmod.fluid_type = "DOMAIN"
ds = dmod.domain_settings
ds.domain_type = "GAS"
ds.resolution_max = SIM_RES
ds.use_adaptive_domain = True
ds.additional_res = 0
ds.use_dissolve_smoke = False           # fog hangs around
ds.alpha = -0.08                        # density buoyancy: chilled fog sinks a touch
ds.beta = 0.0                           # no heat rise
ds.vorticity = 0.2                      # lazy billowing roll
ds.use_noise = False                    # thick fog = low-frequency; save the CPU
ds.cache_type = "ALL"
ds.cache_directory = CACHE
ds.cache_frame_start = FRAME_START
ds.cache_frame_end = FRAME_END

# ---------------------------------------------------------------- emitter
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.28, location=(0, 0, 0.55))
emitter = bpy.context.active_object
emitter.name = "FogEmitter"
emitter.hide_render = True
emod = emitter.modifiers.new(name="Fluid", type="FLUID")
emod.fluid_type = "FLOW"
fs = emod.flow_settings
fs.flow_type = "SMOKE"
fs.flow_behavior = "INFLOW"
fs.density = 1.0
fs.temperature = 0.0
fs.use_initial_velocity = True
fs.velocity_coord = (0.0, 0.0, 0.6)     # gentle upward push; vorticity does the rest

# ---------------------------------------------------------------- volume look
mat = bpy.data.materials.new("FogVolume")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()
pv = nt.nodes.new("ShaderNodeVolumePrincipled")
outn = nt.nodes.new("ShaderNodeOutputMaterial")
pv.inputs["Color"].default_value = (1, 1, 1, 1)   # white scatter; tint in Unity
pv.inputs["Density"].default_value = 28.0
pv.inputs["Density Attribute"].default_value = "density"
nt.links.new(pv.outputs["Volume"], outn.inputs["Volume"])
domain.data.materials.append(mat)

# ---------------------------------------------------------------- camera
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 3.4
cam = bpy.data.objects.new("cam", cam_data)
cam.location = (0, -6.0, 1.55)
cam.rotation_euler = (math.radians(90), 0, 0)     # look along +Y (front view)
scene.collection.objects.link(cam)
scene.camera = cam

# ------------------------------------------------- six-light rig, light groups
# Image space: right = Blender +X, up = Blender +Z, camera sits on -Y.
# Unity six-way: pos map RGB = right,up,back / neg map RGB = left,bottom,front.
SUNS = {
    "px": (1, 0, 0),    # lit from screen right
    "py": (0, 0, 1),    # lit from screen top
    "pz": (0, 1, 0),    # lit from behind the sprite (far side from camera)
    "nx": (-1, 0, 0),
    "ny": (0, 0, -1),
    "nz": (0, -1, 0),   # lit from the camera side
}
from mathutils import Vector
view_layer = bpy.context.view_layer
for name, axis in SUNS.items():
    lg = view_layer.lightgroups.add(name=name)
    ld = bpy.data.lights.new(name, type="SUN")
    ld.energy = 1.0
    ld.angle = 0.35                                   # slightly soft sun
    lo = bpy.data.objects.new(name, ld)
    # sun shines along its -Z; aim it from `axis` toward the origin
    lo.rotation_euler = Vector((-axis[0], -axis[1], -axis[2])).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(lo)
    lo.lightgroup = name

# ---------------------------------------------------------------- render conf
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 64
scene.cycles.use_adaptive_sampling = True
scene.cycles.use_denoising = True
scene.cycles.denoiser = "OPENIMAGEDENOISE"
scene.cycles.volume_step_rate = 1.0
scene.cycles.volume_bounces = 1                       # cheap multi-scatter feel
scene.render.resolution_x = RES
scene.render.resolution_y = RES
scene.render.film_transparent = True                  # volume extinction -> alpha
scene.view_settings.view_transform = "Standard"       # data, not beauty

# ------------------------------------------------ compositor: per-pass EXRs
scene.use_nodes = True
ctree = scene.node_tree
ctree.nodes.clear()
rl = ctree.nodes.new("CompositorNodeRLayers")
fo = ctree.nodes.new("CompositorNodeOutputFile")
fo.base_path = EXR
fo.format.file_format = "OPEN_EXR"
fo.format.color_depth = "16"
fo.format.exr_codec = "ZIP"
fo.file_slots.clear()
for name in list(SUNS) + ["alpha"]:
    fo.file_slots.new(name + "_")
for name in SUNS:
    ctree.links.new(rl.outputs["Combined_LG_" + name], fo.inputs[name + "_"])
ctree.links.new(rl.outputs["Alpha"], fo.inputs["alpha_"])

# ---------------------------------------------------------------- bake sim
log("baking fluid sim,", SIM_RES, "divisions, frames", FRAME_START, "-", FRAME_END)
with bpy.context.temp_override(object=domain, active_object=domain,
                               selected_objects=[domain]):
    bpy.ops.fluid.bake_all()
log("sim bake done")

# ---------------------------------------------------------------- render
import time
t0 = time.time()
for f in range(FRAME_START, FRAME_END + 1):
    scene.frame_set(f)
    bpy.ops.render.render(write_still=False)          # File Output node writes
    if f % 4 == 0 or f == FRAME_START:
        el = time.time() - t0
        done = f - FRAME_START + 1
        total = FRAME_END - FRAME_START + 1
        log(f"frame {f}/{FRAME_END}  {el/done:.1f}s/frame  eta {(total-done)*el/done/60:.1f}min")
log("render done in %.1f min" % ((time.time() - t0) / 60))
