"""Procedural fog-machine flipbook bake (no fluid sim needed).

Volumetric density = spherical falloff x two counter-rotating fBM noise
fields. The noise-space rotation completes exactly 2*pi over the 64 frames,
so the flipbook loops perfectly with no crossfade.

Renders each frame once with six axis-aligned suns split into Cycles light
groups -> per-frame EXRs per direction + alpha, for pack_fog.py.
"""
import bpy
import math
import os
import sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
EXR = os.path.join(OUT, "exr")
os.makedirs(EXR, exist_ok=True)

FRAMES = 64
RES = 512

log = lambda *a: (print("[bake]", *a), sys.stdout.flush())

for _o in list(bpy.data.objects):
    bpy.data.objects.remove(_o, do_unlink=True)
scene = bpy.context.scene
scene.frame_start, scene.frame_end = 1, FRAMES

# Black world: light-group passes must contain sun light only.
world = bpy.data.worlds.new("world")
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs["Color"].default_value = (0, 0, 0, 1)
bg.inputs["Strength"].default_value = 0.0
scene.world = world

# ------------------------------------------------------------- volume object
bpy.ops.mesh.primitive_cube_add(size=3.2, location=(0, 0, 0))
vol = bpy.context.active_object
vol.name = "FogVolume"

mat = bpy.data.materials.new("FogMat")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()

def node(t, **kw):
    n = nt.nodes.new(t)
    for k, v in kw.items():
        setattr(n, k, v)
    return n

texco = node("ShaderNodeTexCoord")

# Two counter-rotating noise lookups; a full 2*pi turn over FRAMES = seamless loop.
theta = node("ShaderNodeValue", label="theta")
theta.outputs[0].default_value = 0.0

rot1 = node("ShaderNodeVectorRotate")            # around Z
rot1.inputs["Axis"].default_value = (0, 0, 1)
nt.links.new(texco.outputs["Object"], rot1.inputs["Vector"])
nt.links.new(theta.outputs[0], rot1.inputs["Angle"])

neg = node("ShaderNodeMath", operation="MULTIPLY")
neg.inputs[1].default_value = -1.0
nt.links.new(theta.outputs[0], neg.inputs[0])

rot2 = node("ShaderNodeVectorRotate")            # counter-rotation, tilted axis
rot2.inputs["Axis"].default_value = (0.6, 0.2, 0.77)
nt.links.new(texco.outputs["Object"], rot2.inputs["Vector"])
nt.links.new(neg.outputs[0], rot2.inputs["Angle"])

n1 = node("ShaderNodeTexNoise")
n1.inputs["Scale"].default_value = 1.1           # big billows
n1.inputs["Detail"].default_value = 3.0
n1.inputs["Roughness"].default_value = 0.55
nt.links.new(rot1.outputs["Vector"], n1.inputs["Vector"])

n2 = node("ShaderNodeTexNoise")
n2.inputs["Scale"].default_value = 2.3           # medium churn
n2.inputs["Detail"].default_value = 2.0
n2.inputs["Roughness"].default_value = 0.5
nt.links.new(rot2.outputs["Vector"], n2.inputs["Vector"])

mixn = node("ShaderNodeMath", operation="MULTIPLY_ADD")   # 0.65*n1 + 0.35*n2
mixn.inputs[1].default_value = 0.65
nt.links.new(n1.outputs["Fac"], mixn.inputs[0])
scale2 = node("ShaderNodeMath", operation="MULTIPLY")
scale2.inputs[1].default_value = 0.35
nt.links.new(n2.outputs["Fac"], scale2.inputs[0])
nt.links.new(scale2.outputs[0], mixn.inputs[2])

# Spherical falloff so the puff sits centered with soft edges.
length = node("ShaderNodeVectorMath", operation="LENGTH")
nt.links.new(texco.outputs["Object"], length.inputs[0])
fall = node("ShaderNodeMapRange")
fall.inputs["From Min"].default_value = 0.25     # small solid core
fall.inputs["From Max"].default_value = 1.55     # very wide feather: no bubble rim
fall.inputs["To Min"].default_value = 1.0
fall.inputs["To Max"].default_value = 0.0
nt.links.new(length.outputs["Value"], fall.inputs["Value"])

# Carve billow shape: noise gates the falloff, smoothstep-ish contrast.
gate = node("ShaderNodeMapRange")
gate.interpolation_type = "SMOOTHSTEP"
gate.inputs["From Min"].default_value = 0.20     # noise eats deep into the edge
gate.inputs["From Max"].default_value = 0.95     # long translucent gradient, no hard shell
nt.links.new(mixn.outputs[0], gate.inputs["Value"])

dens = node("ShaderNodeMath", operation="MULTIPLY")
nt.links.new(fall.outputs["Result"], dens.inputs[0])
nt.links.new(gate.outputs["Result"], dens.inputs[1])

dscale = node("ShaderNodeMath", operation="MULTIPLY")
dscale.inputs[1].default_value = 15.0            # thick but translucent enough to overlap-blend
nt.links.new(dens.outputs[0], dscale.inputs[0])

pv = node("ShaderNodeVolumePrincipled")
pv.inputs["Color"].default_value = (1, 1, 1, 1)
pv.inputs["Anisotropy"].default_value = 0.2      # slight forward scatter
nt.links.new(dscale.outputs[0], pv.inputs["Density"])
outn = node("ShaderNodeOutputMaterial")
nt.links.new(pv.outputs["Volume"], outn.inputs["Volume"])
vol.data.materials.append(mat)

# Keyframe theta 0 -> 2*pi linearly across the loop (frame FRAMES+1 == frame 1).
theta.outputs[0].default_value = 0.0
theta.outputs[0].keyframe_insert("default_value", frame=1)
theta.outputs[0].default_value = 2 * math.pi
theta.outputs[0].keyframe_insert("default_value", frame=FRAMES + 1)
for fc in nt.animation_data.action.fcurves:
    for kp in fc.keyframe_points:
        kp.interpolation = "LINEAR"

# ------------------------------------------------------------------- camera
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 3.3
cam = bpy.data.objects.new("cam", cam_data)
cam.location = (0, -6.0, 0)
cam.rotation_euler = (math.radians(90), 0, 0)    # front view, +Y forward
scene.collection.objects.link(cam)
scene.camera = cam

# --------------------------------------------- six suns via light groups
# Image right = +X, image up = +Z, camera on -Y.
# Unity six-way: positive map RGB = right, up, back / negative = left, bottom, front.
SUNS = {
    "px": (1, 0, 0), "py": (0, 0, 1), "pz": (0, 1, 0),
    "nx": (-1, 0, 0), "ny": (0, 0, -1), "nz": (0, -1, 0),
}
from mathutils import Vector
view_layer = bpy.context.view_layer
for name, axis in SUNS.items():
    view_layer.lightgroups.add(name=name)
    ld = bpy.data.lights.new(name, type="SUN")
    ld.energy = 1.0
    ld.angle = 0.35
    lo = bpy.data.objects.new(name, ld)
    lo.rotation_euler = Vector((-axis[0], -axis[1], -axis[2])).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(lo)
    lo.lightgroup = name

# -------------------------------------------------------------- render conf
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 64
scene.cycles.use_adaptive_sampling = True
scene.cycles.use_denoising = True
scene.cycles.denoiser = "OPENIMAGEDENOISE"
scene.cycles.volume_bounces = 1
scene.render.resolution_x = RES
scene.render.resolution_y = RES
scene.render.film_transparent = True
scene.view_settings.view_transform = "Standard"

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
    ctree.links.new(rl.outputs["Combined_" + name], fo.inputs[name + "_"])
ctree.links.new(rl.outputs["Alpha"], fo.inputs["alpha_"])

# ------------------------------------------------------------------- render
import time
t0 = time.time()
for f in range(1, FRAMES + 1):
    scene.frame_set(f)
    bpy.ops.render.render(write_still=False)
    el = time.time() - t0
    log(f"frame {f}/{FRAMES}  {el/f:.1f}s/frame  eta {(FRAMES-f)*el/f/60:.1f}min")
log("render done in %.1f min" % ((time.time() - t0) / 60))
