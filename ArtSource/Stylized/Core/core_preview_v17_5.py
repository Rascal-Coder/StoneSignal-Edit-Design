"""StoneSignal v17.5 core preview: renders the core (SM_Prop_Core_01 + SM_Core_Enclosure_*) with a node emulation of
StoneSignalToonCore.hlsl under the Game.unity lighting, so the panels read like the game frame (not like the v17.3 Blender mockup).

Usage:
  blender --background --factory-startup --python core_preview_v17_5.py -- <projectRoot> <outDir> <panels.json> <oldProjectRoot>
  (panels.json: list of {name, mesh, enc_src old|new, crystal_remap, core{...}, enc{...}} - see toon() for the keys;
   oldProjectRoot = a checkout with the v17.4 enclosure FBX for before/after panels)

Emulated (per fragment, same order as ToonFrag):
  Game.unity sun (1, .96, .88) x 1.35 (linear colour space), rotation q(.391, -.252, .112, .878); flat ambient (.56, .62, .66);
  half-lambert x lerp(1, shadow, _ShadowStrength .85) -> T_Ramp_Toon_3Step (0 / .6 / 1 at .46 / .78);
  diffuse = baseCol * lerp(_ShadowColor, _LitColor, ramp) * light; + baseCol * ambient * _AmbientStrength .35 * vertex G;
  + rim smoothstep(.55,.75,fresnel) * sat(ndl + .3) * #FFE6C7 * _RimIntensity; mottle; base AO;
  vertex-R glow ("add": v17.3 c += baseCol*R*E, "lerp": v17.5 c = lerp(c, baseCol*E*hot, R)); _HiColor ring (v17.4 CoreDamageFx);
  _StatusTint / _StatusRim (v17.5 CoreDamageFx); no tonemap (Neutral is in the profile but the v17.4 frames match the untonemapped
  values: MechWhite top #FFF2E8, SlotIce side #96FFFF); bloom ~ compositor fog glow above 1.1.
Calibration against stonesignal/v174/vfx/core_live_broken.png: MechWhite top #FFF2E8 (model #FFF2E8), crystal side #96FFFF (#96FFFF).
"""
import bpy, bmesh, math, os, sys, json
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
ROOT, OUTD, PANELS = argv[0], argv[1], json.load(open(argv[2]))
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Stylized")
os.makedirs(OUTD, exist_ok=True)

def s2l(c): return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4
def hexl(h): return tuple(s2l(int(h[i:i + 2], 16) / 255) for i in (0, 2, 4))
def u2b(v): return Vector((-v[0], -v[2], v[1]))           # Unity (x,y,z) -> Blender (-x,-z,y)
def qrot(q, v):
    x, y, z, w = q; u = Vector((x, y, z)); v = Vector(v)
    return 2 * u.dot(v) * u + (w * w - u.dot(u)) * v + 2 * w * u.cross(v)
LDIR = u2b(-qrot((0.39098036, -0.25180724, 0.112111814, 0.87815624), (0, 0, 1))).normalized()
VDIR = u2b(-qrot((0.34071866, 0.081899606, -0.029809017, 0.9361168), (0, 0, 1))).normalized()
LCOL = tuple(s2l(c) * 1.35 for c in (1, .96, .88))       # Game.unity Directional Light colour (gamma) x intensity
AMB = tuple(s2l(c) for c in (.56, .62, .66))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x, sc.render.resolution_y = 760, 600
sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
world = bpy.data.worlds.new("W"); world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1); sc.world = world
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sun.data.energy = math.pi; sun.data.angle = math.radians(1.5)
sun.rotation_euler = (-LDIR).to_track_quat('-Z', 'Y').to_euler(); sc.collection.objects.link(sun)
try: sc.eevee.shadow_resolution_scale = 1.0
except Exception: pass
img = bpy.data.images.load(os.path.join(ART, "Textures", "T_Env_Palette_D.png")); img.name = "T_Env_Palette_D"

def N(nt, t, **kw):
    n = nt.nodes.new(t)
    for k, v in kw.items(): setattr(n, k, v)
    return n
def math_node(nt, op, a, b=None, clamp=False):
    n = N(nt, "ShaderNodeMath", operation=op); n.use_clamp = clamp
    for i, x in enumerate((a, b)):
        if x is None: continue
        if isinstance(x, (int, float)): n.inputs[i].default_value = x
        else: nt.links.new(x, n.inputs[i])
    return n.outputs[0]
def vmath(nt, op, a, b=None):
    n = N(nt, "ShaderNodeVectorMath", operation=op)
    for i, x in enumerate((a, b)):
        if x is None: continue
        if isinstance(x, (tuple, list, Vector)): n.inputs[i].default_value = tuple(x)
        else: nt.links.new(x, n.inputs[i])
    return n.outputs["Value"] if op in ('DOT_PRODUCT', 'LENGTH') else n.outputs[0]
def vscale(nt, v, s):
    n = N(nt, "ShaderNodeVectorMath", operation='SCALE'); nt.links.new(v, n.inputs[0])
    if isinstance(s, (int, float)): n.inputs["Scale"].default_value = s
    else: nt.links.new(s, n.inputs["Scale"])
    return n.outputs[0]
def smooth(nt, a, b, x):
    n = N(nt, "ShaderNodeMapRange", interpolation_type='SMOOTHSTEP'); n.clamp = True
    nt.links.new(x, n.inputs[0]); n.inputs[1].default_value = a; n.inputs[2].default_value = b; return n.outputs[0]

def toon(name, p):
    """p: basecolor(hex), rim, ao, aoh, mottle, vce, mode('add'|'lerp'), hi(rgb hex, amount), st(rgb hex, a), stb(st multiplier), sr(linear rgb, a), remap_crystal"""
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = N(nt, "ShaderNodeOutputMaterial")
    tex = N(nt, "ShaderNodeTexImage", image=img, interpolation='Closest')
    bc = hexl(p.get("basecolor", "FFFFFF"))
    base = vmath(nt, 'MULTIPLY', tex.outputs[0], bc)
    geo = N(nt, "ShaderNodeNewGeometry"); nrm = geo.outputs["Normal"]
    ndl = vmath(nt, 'DOT_PRODUCT', nrm, tuple(LDIR))
    # shadow attenuation from an EEVEE sun: S2R(white diffuse, energy pi) = max(ndl,0) * shadow
    d = N(nt, "ShaderNodeBsdfDiffuse"); d.inputs[0].default_value = (1, 1, 1, 1); s2r = N(nt, "ShaderNodeShaderToRGB"); nt.links.new(d.outputs[0], s2r.inputs[0])
    irr = N(nt, "ShaderNodeRGBToBW"); nt.links.new(s2r.outputs[0], irr.inputs[0])
    att = math_node(nt, 'DIVIDE', irr.outputs[0], math_node(nt, 'MAXIMUM', ndl, .02), clamp=True)
    att = math_node(nt, 'MULTIPLY', att, math_node(nt, 'GREATER_THAN', ndl, 0.0))
    satt = math_node(nt, 'ADD', math_node(nt, 'MULTIPLY', att, .85), .15)                        # lerp(1, att, .85)
    lt = math_node(nt, 'MULTIPLY', math_node(nt, 'ADD', math_node(nt, 'MULTIPLY', ndl, .5), .5), satt)
    ramp = N(nt, "ShaderNodeValToRGB"); cr = ramp.color_ramp; cr.interpolation = 'CONSTANT'
    cr.elements[0].position = 0; cr.elements[0].color = (0, 0, 0, 1); cr.elements[1].position = .785; cr.elements[1].color = (1, 1, 1, 1)
    e = cr.elements.new(.455); e.color = (.6, .6, .6, 1)
    nt.links.new(lt, ramp.inputs[0])
    sh = hexl("6E5A8C")
    mix = N(nt, "ShaderNodeMix", data_type='RGBA'); nt.links.new(ramp.outputs[0], mix.inputs[0]); mix.inputs[6].default_value = (*sh, 1); mix.inputs[7].default_value = (1, 1, 1, 1)
    diff = vmath(nt, 'MULTIPLY', vmath(nt, 'MULTIPLY', base, mix.outputs[2]), LCOL)
    vc = N(nt, "ShaderNodeVertexColor"); sep = N(nt, "ShaderNodeSeparateColor"); nt.links.new(vc.outputs[0], sep.inputs[0])
    amb = vscale(nt, vmath(nt, 'MULTIPLY', base, tuple(a * .35 for a in AMB)), sep.outputs[1])
    fres = math_node(nt, 'SUBTRACT', 1.0, math_node(nt, 'MAXIMUM', vmath(nt, 'DOT_PRODUCT', nrm, tuple(VDIR)), 0.0))
    rimk = math_node(nt, 'MULTIPLY', smooth(nt, .55, .75, fres), math_node(nt, 'ADD', ndl, .3, clamp=True))
    rc = hexl("FFE6C7"); rcn = N(nt, "ShaderNodeCombineXYZ"); [setattr(rcn.inputs[i], "default_value", rc[i] * p.get("rim", .25)) for i in range(3)]
    rim = vscale(nt, rcn.outputs[0], rimk)
    c = vmath(nt, 'ADD', vmath(nt, 'ADD', diff, amb), rim)
    tc = N(nt, "ShaderNodeTexCoord"); osep = N(nt, "ShaderNodeSeparateXYZ"); nt.links.new(tc.outputs["Object"], osep.inputs[0])
    if p.get("mottle", 0) > 0:
        n1 = N(nt, "ShaderNodeTexNoise"); n1.inputs["Scale"].default_value = 2.3; nt.links.new(geo.outputs["Position"], n1.inputs["Vector"])
        c = vscale(nt, c, math_node(nt, 'ADD', 1.0, math_node(nt, 'MULTIPLY', math_node(nt, 'SUBTRACT', n1.outputs[0], .5), p["mottle"])))
    if p.get("ao", 0) > 0:   # lerp(1-AO, 1, sat(posOS.y / H)); object up = Blender Z
        k = math_node(nt, 'DIVIDE', osep.outputs[2], p["aoh"], clamp=True)
        c = vscale(nt, c, math_node(nt, 'ADD', 1 - p["ao"], math_node(nt, 'MULTIPLY', k, p["ao"])))
    R = sep.outputs[0]
    if p.get("vce", 0) > 0:
        E = p["vce"]
        if p.get("mode") == "add":
            c = vmath(nt, 'ADD', c, vscale(nt, vscale(nt, base, R), E))
        else:
            hot = N(nt, "ShaderNodeMapRange"); hot.clamp = True; nt.links.new(R, hot.inputs[0])
            hot.inputs[1].default_value = .95; hot.inputs[2].default_value = 1; hot.inputs[3].default_value = E; hot.inputs[4].default_value = E * 1.35
            tgt = vscale(nt, base, hot.outputs[0])
            lm = N(nt, "ShaderNodeMix", data_type='VECTOR'); nt.links.new(R, lm.inputs[0]); nt.links.new(c, lm.inputs[4]); nt.links.new(tgt, lm.inputs[5]); c = lm.outputs[1]
    if p.get("hi"):          # v17.4 CoreDamageFx: wall-highlight ring on upward faces 0.34-0.46 m from the object centre
        hc, ha = p["hi"]; ax = math_node(nt, 'MAXIMUM', math_node(nt, 'ABSOLUTE', osep.outputs[0]), math_node(nt, 'ABSOLUTE', osep.outputs[1]))
        nz = N(nt, "ShaderNodeSeparateXYZ"); nt.links.new(nrm, nz.inputs[0])
        ering = math_node(nt, 'MULTIPLY', smooth(nt, .34, .46, ax), math_node(nt, 'SUBTRACT', math_node(nt, 'MULTIPLY', nz.outputs[2], 2), .6, clamp=True))
        k = math_node(nt, 'MULTIPLY', math_node(nt, 'ADD', math_node(nt, 'MULTIPLY', ering, 1.8), .15), ha)
        h = p["hi_rgb"]; hn = N(nt, "ShaderNodeCombineXYZ"); [setattr(hn.inputs[i], "default_value", h[i]) for i in range(3)]
        c = vmath(nt, 'ADD', c, vscale(nt, hn.outputs[0], k))
    if p.get("st"):          # v17.5 _StatusTint: c = lerp(c, c*.55 + st*.45, a); st = colour.linear x stb (CoreDamageFx.tintBoost)
        st, a = p["st"]; stl = tuple(x * p.get("stb", 1.0) for x in hexl(st))
        t = vmath(nt, 'ADD', vscale(nt, c, .55), tuple(x * .45 for x in stl))
        lm = N(nt, "ShaderNodeMix", data_type='VECTOR'); lm.inputs[0].default_value = a; nt.links.new(c, lm.inputs[4]); nt.links.new(t, lm.inputs[5]); c = lm.outputs[1]
    if p.get("sr"):          # _StatusRim: c += rgb * a * smoothstep(.45,.95,fres)
        srgb, a = p["sr"]; sn = N(nt, "ShaderNodeCombineXYZ"); [setattr(sn.inputs[i], "default_value", srgb[i] * a) for i in range(3)]
        c = vmath(nt, 'ADD', c, vscale(nt, sn.outputs[0], smooth(nt, .45, .95, fres)))
    dbg = {"att": att, "lt": lt, "ndl": ndl, "irr": irr.outputs[0]}.get(p.get("debug"))
    em = N(nt, "ShaderNodeEmission"); nt.links.new(dbg if dbg is not None else c, em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])
    return m

def import_fbx(path):
    before = set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']; o = objs[0]
    # bake the importer's axis rotation / unit scale into the mesh so Object coords = Unity posOS (up = Z here): base AO needs it
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return o

def remap_cell(obj, src, dst):
    me = obj.data; uv = me.uv_layers.active.data; n = 0
    for poly in me.polygons:
        for li in poly.loop_indices:
            u, v = uv[li].uv; cell = int(u * 16) + 16 * int((1 - v) * 16)
            if cell == src: uv[li].uv = ((dst % 16 + .5) / 16, 1 - (dst // 16 + .5) / 16); n += 1
    return n

# ground tiles around the core (same layout as the v17.3 mockup), M_Env_Palette-like
def tile_mesh():
    bm = bmesh.new(); col = bm.loops.layers.color.new("Color"); uvl = bm.loops.layers.uv.new("UVMap")
    import random
    def cell_uv(i): return ((i % 16 + .5) / 16, 1 - (i // 16 + .5) / 16)
    for ix in range(-3, 3):
        for iy in range(-3, 3):
            h = 0 if (ix in (-1, 0) and iy in (-1, 0)) else random.Random(ix * 7 + iy).uniform(-.03, .05)
            res = bmesh.ops.create_cube(bm, size=1.0); vs = res["verts"]
            for v in vs: v.co.x = v.co.x * .97 + ix + .5; v.co.y = v.co.y * .97 + iy + .5; v.co.z = v.co.z * .3 + h - .15
            faces = {f for v in vs for f in v.link_faces}
            for f in faces:
                top = f.normal.z > .5; ci = (26 if (ix + iy) % 3 else 38) if top else 27
                for l in f.loops: l[col] = (0, 1, 1, 1); l[uvl].uv = cell_uv(ci)
    me = bpy.data.meshes.new("Tiles"); bm.to_mesh(me); bm.free(); o = bpy.data.objects.new("Tiles", me); sc.collection.objects.link(o); return o

tiles = tile_mesh(); tiles.data.materials.append(toon("Env", dict(rim=.25, mottle=.18, ao=.2, aoh=.22)))
core = import_fbx(os.path.join(ART, "Towers", "SM_Prop_Core_01.fbx")); core.location.z -= .25
core_crystal = core.copy(); core_crystal.data = core.data.copy(); sc.collection.objects.link(core_crystal)
print("CRYSTAL REMAP loops:", remap_cell(core_crystal, 35, 48))
core_crystal.hide_render = True
encs = {}
for s in ("Intact", "Cracked", "Broken"):
    for tag in ("old", "new"):
        p = os.path.join(ROOT if tag == "new" else argv[3], "Assets", "Game", "Art", "Stylized", "Environment", "Core", "SM_Core_Enclosure_%s.fbx" % s)
        o = import_fbx(p); o.hide_render = True; encs[(s, tag)] = o

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.3
fwd = -VDIR; target = Vector((0, 0, .55)); cam.location = target - fwd * 12; cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
sc.use_nodes = True; cn = sc.node_tree; cn.nodes.clear()
rl = cn.nodes.new("CompositorNodeRLayers"); gl = cn.nodes.new("CompositorNodeGlare"); gl.glare_type = 'FOG_GLOW'; gl.threshold = 1.1; gl.size = 7; gl.mix = -.7
co = cn.nodes.new("CompositorNodeComposite"); cn.links.new(rl.outputs[0], gl.inputs[0]); cn.links.new(gl.outputs[0], co.inputs[0])

for i, pn in enumerate(PANELS):
    for o in encs.values(): o.hide_render = True
    encs[(pn["mesh"], pn["enc_src"])].hide_render = False
    useremap = pn.get("crystal_remap", False); core.hide_render = useremap; core_crystal.hide_render = not useremap
    cobj = core_crystal if useremap else core
    cobj.data.materials.clear(); cobj.data.materials.append(toon("Core%d" % i, pn["core"]))
    eo = encs[(pn["mesh"], pn["enc_src"])]; eo.data.materials.clear(); eo.data.materials.append(toon("Enc%d" % i, pn["enc"]))
    sc.render.filepath = os.path.join(OUTD, pn["name"] + ".png"); bpy.ops.render.render(write_still=True)
    print("PANEL", pn["name"])
