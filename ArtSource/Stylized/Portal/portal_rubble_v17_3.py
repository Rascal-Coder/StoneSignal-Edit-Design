"""v17.3 fix: SM_Portal_Rubble.fbx re-export with palette UVs.

Root cause of the white chunks around the spawn ring: portal_scene_v16_2.py exported a plain primitive ico sphere with
Blender's auto-generated UV map. PF_VFX_SpawnPortal renders the rubble with the shared toon stone material (palette
texture T_Env_Palette_D, point filtered), and all 20 faces sampled the unused palette cells (240,240,240) -> white.
This rebuilds the chunk as a jittered low-poly rock whose faces carry palette-cell UVs (StoneTopWarm top, StoneSide /
StoneDark sides) and vertex colour (0,1,1,1) like every other stylized prop. Same file name -> .meta GUID kept.

Usage: blender --background --factory-startup --python portal_rubble_v17_3.py -- <projectRoot>
"""
import bpy, bmesh, os, sys, random
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = os.path.abspath(argv[0] if argv else os.getcwd())
OUT = os.path.join(ROOT, "Assets", "Game", "Art", "Stylized", "FX", "Portal", "SM_Portal_Rubble.fbx")
PIDX = {"StoneTopWarm": 8, "StoneSide": 10, "StoneDark": 11}      # build_stylized_batch1.py PALETTE order
def pal_uv(i): return ((i % 16 + .5) / 16.0, 1.0 - (i // 16 + .5) / 16.0)
bpy.ops.wm.read_factory_settings(use_empty=True)
rng = random.Random(1731)
bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=.1)
for v in bm.verts:
    v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * .018
    v.co.z *= .72                                                   # flatter chunk, sits on the sand
bm.normal_update()
uvl = bm.loops.layers.uv.new("UVMap"); col = bm.loops.layers.float_color.new("Color")
for f in bm.faces:
    n = f.normal
    key = "StoneTopWarm" if n.z > .55 else ("StoneDark" if n.z < -.3 else "StoneSide")
    for l in f.loops: l[uvl].uv = pal_uv(PIDX[key]); l[col] = (0, 1, 1, 1)
me = bpy.data.meshes.new("SM_Portal_Rubble"); bm.to_mesh(me); bm.free()
me.materials.append(bpy.data.materials.new("M_Stylized_Palette"))
ob = bpy.data.objects.new("SM_Portal_Rubble", me); bpy.context.scene.collection.objects.link(ob)
ob.select_set(True); bpy.context.view_layer.objects.active = ob
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE', colors_type='LINEAR',
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', use_tspace=False)
print("RUBBLE v17.3 exported", OUT, "faces", len(me.polygons))
