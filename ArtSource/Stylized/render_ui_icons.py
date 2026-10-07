import bpy, sys, os, math
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1]
sc = bpy.context.scene
towers = {"Needle": "SM_Tower_Gatling_1x1_01", "Pulse": "SM_Tower_Tesla_1x1_01", "Seismic": "SM_Tower_Mortar_2x2_01", "Chill": "SM_Tower_Frost_1x1_01"}
try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
except Exception: sc.render.engine = 'BLENDER_EEVEE'
sc.render.film_transparent = True; sc.render.resolution_x = sc.render.resolution_y = 512
sc.view_settings.view_transform = 'Standard'
for o in list(sc.objects):
    if o.type in ('LIGHT', 'CAMERA'): o.hide_render = True
sun = bpy.data.objects.new("IcoSun", bpy.data.lights.new("IcoSun", 'SUN')); sun.data.energy = 3.2
sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
if sc.world is None: sc.world = bpy.data.worlds.new("W")
sc.world.use_nodes = True; bg = sc.world.node_tree.nodes.get("Background")
if bg: bg.inputs[0].default_value = (0.85, 0.82, 0.9, 1); bg.inputs[1].default_value = 0.9
cam = bpy.data.objects.new("IcoCam", bpy.data.cameras.new("IcoCam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
def unex(lc):
    lc.exclude = False; lc.hide_viewport = False
    for c in lc.children: unex(c)
unex(bpy.context.view_layer.layer_collection)
for c in bpy.data.collections: c.hide_render = False; c.hide_viewport = False
for o in bpy.data.objects:
    if o.name.startswith("SM_Tower") and o.name not in sc.objects: sc.collection.objects.link(o)
for name, col in towers.items():
    for o in sc.objects:
        if o.type == 'MESH': o.hide_render = not o.name.startswith(col); o.hide_viewport = False
    objs = [o for o in sc.objects if o.type == 'MESH' and o.name.startswith(col)]
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (lo + hi) / 2; size = max((hi - lo).length, .5)
    d = Vector((-1.0, -1.25, .95)).normalized()
    cam.location = c + d * 10; cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = size * 1.05
    sc.render.filepath = os.path.join(out, "icon_%s.png" % name); bpy.ops.render.render(write_still=True)
    print("ICON", name, len(objs), [round(x, 2) for x in c], round(size, 2))
