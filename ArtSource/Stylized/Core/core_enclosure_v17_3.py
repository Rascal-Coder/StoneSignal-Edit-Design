"""StoneSignal v17.3 core enclosure (procedural, Blender 4.2+ headless).

Usage:
  blender --background --factory-startup --python core_enclosure_v17_3.py -- <projectRoot> [--render <png>] [--no-export]

Builds SM_Core_Enclosure_Intact / _Cracked / _Broken, each ONE merged mesh object (1 material slot, 1 draw call):
  * shared palette texture T_Env_Palette_D (16 px cells, UV = palette cell centre, same as build_stylized_batch1.py),
  * vertex colour R = crack/rune glow mask (0..1) read by StoneSignal/ToonLit `_VColorEmission` (v17.3), G = AO (1),
    B = layer (1), A = phase (1). M_Core_Enclosure has _WindStrength 0, so R is never used as wind here.
    v17.5: stone R = 0 (verified in the exported FBX), rune/crack/fissure R = GLOW, obelisk ember caps R = GLOW["cap"] (new);
    ToonCore blends to the ember colour (lerp, not add) so accents peak ~1.0 and only R >= 0.95 (Broken) blooms slightly.
  * pivot = enclosure base = tile top (GridView places PF_Core_Enclosure at ArtCatalog.tileTop above the core pivot),
    fits the 2x2 core footprint (|x|,|y| <= 1.0), front = Blender -Y (-> Unity +Z); camera side = Blender +Y (low wall).
Exports FBX (-Z forward / Y up / bake space transform / linear vertex colours) into Assets/Game/Art/Stylized/Environment/Core/
(same filenames -> existing .meta GUIDs stay valid). --render draws the 4 states (Intact/Cracked/Broken/Critical) around
the real SM_Prop_Core_01 crystal.
"""
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = os.path.abspath(argv[0] if argv and not argv[0].startswith("--") else os.getcwd())
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
EXPORT = "--no-export" not in argv
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Stylized")
OUT = os.path.join(ART, "Environment", "Core")

PALETTE = [  # must match build_stylized_batch1.py (T_Env_Palette_D)
    ("LeafOrange", "D27A4A"), ("LeafRed", "B05442"), ("LeafGold", "DCA060"), ("LeafHighlight", "E6C98A"),
    ("LeafCore", "8E3436"), ("LeafShadow", "64273B"), ("Trunk", "6D3646"), ("Wood", "B07A55"),
    ("StoneTopWarm", "B9B0C8"), ("StoneTopCool", "B0A8C0"), ("StoneSide", "A38E89"), ("StoneDark", "503E5D"),
    ("Ground", "A4514C"), ("GroundDark", "874A4A"), ("Sand", "F6D692"), ("Brick", "E9A47B"),
    ("MechWhite", "D9D4DA"), ("Indigo", "2B3A63"), ("AllyYellow", "F2B330"), ("OutlineIndigo", "1E1A3A"),
    ("Skin", "F3C9A0"), ("Metal", "6E6A80"), ("WaterDeep", "0754A0"), ("WaterShallow", "1C7CD0"),
    ("Foam", "E6F0FF"), ("GrassDry", "D9A441"),
    ("GroundLavTop", "8C6C54"), ("GroundLavSide", "5E4236"), ("EnemyRed", "D21C36"), ("EnemyDark", "3B1E2E"),
    ("EnemyWhite", "F0F0F0"), ("EnemyEye", "FFE45C"), ("SlotKinetic", "FFE7A0"), ("SlotHE", "FFB52E"),
    ("SlotFire", "FF7A1F"), ("SlotIce", "7FE3FF"), ("SlotElec", "C77DFF"), ("SteelBlue", "C8D6FF"),
    ("GroundLavTop2", "84664F"), ("Dirt", "967259"), ("DirtDark", "7A5444"), ("Moss", "A4784C"),
    ("WallSide", "9A90AC"), ("WallSideDark", "7A7090"), ("StrataA", "9A5A4E"), ("StrataB", "7A4A52"), ("StrataC", "B07A68"), ("GrassTop", "B4844A"),
    ("CoreCrystal", "61A1D5"),   # v17.5 cell 48: SM_Prop_Core_01 crystal (mockup blue; StylizedModelPostprocessor remaps the SlotIce faces)
]
PIDX = {n: i for i, (n, _) in enumerate(PALETTE)}
def pal_uv(i): cx, cy = i % 16, i // 16; return ((cx + .5) / 16.0, 1.0 - (cy + .5) / 16.0)

# glow levels (vertex R) per state. Stone / walls / obelisk bodies always R = 0 (no emission).
# v17.5: ToonCore blends towards baseCol * _VColorEmission (1.45) by R, so every accent peaks ~1.0 (no bloom); only R >= 0.95
# (Broken crack + fissure = 1.0) gets the 1.35x HDR boost -> slight bloom. cap = obelisk ember caps (SlotHE), were R = 0 in v17.3.
GLOW = {"Intact": dict(rune=.45, crack=0, fissure=0, cap=.35), "Cracked": dict(rune=.7, crack=.85, fissure=.6, cap=.45),
        "Broken": dict(rune=.9, crack=1., fissure=1., cap=.55)}

rng = random.Random(173)

def _finish(bm, color, vc, smooth=False, bevel=0.0, matrix=None, top=None, angle=30):
    for f in bm.faces:
        f.material_index = PIDX[color]; f.smooth = smooth
    if bevel > 0:
        edges = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > math.radians(angle)]
        if edges: bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type='OFFSET', segments=1, profile=.5, affect='EDGES', clamp_overlap=True)
    if top:
        for f in bm.faces:
            if f.normal.z > .5: f.material_index = PIDX[top]
    if matrix is not None: bm.transform(matrix)
    layer = bm.loops.layers.float_color.get("Color") or bm.loops.layers.float_color.new("Color")
    for f in bm.faces:
        for l in f.loops: l[layer] = vc
    return bm

def box_m(size, matrix, color, vc=(0, 1, 1, 1), bevel=0.0, top=None, jitter_top=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if jitter_top:  # jagged / broken top: push top verts down by random amounts (local, before transform)
        for v in bm.verts:
            if v.co.z > 0: v.co.z -= rng.uniform(0, jitter_top); v.co.x += rng.uniform(-.015, .015); v.co.y += rng.uniform(-.015, .015)
    return _finish(bm, color, vc, bevel=bevel, matrix=matrix, top=top)

def box(size, center, color, rot=(0, 0, 0), **kw):
    return box_m(size, Matrix.Translation(Vector(center)) @ Euler(rot).to_matrix().to_4x4(), color, **kw)

def ico(radius, center, scale=(1, 1, 1), rot=(0, 0, 0), color="StoneSide", jitter=0.0, vc=(0, 1, 1, 1), top=None):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=radius)
    for v in bm.verts: v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * jitter
    m = Matrix.Translation(Vector(center)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal(Vector(scale)).to_4x4()
    return _finish(bm, color, vc, matrix=m, top=top)

def cone(r1, r2, h, segs, center, color, rot=(0, 0, 0), vc=(0, 1, 1, 1)):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r1, radius2=r2, depth=h)
    m = Matrix.Translation(Vector(center)) @ Euler(rot).to_matrix().to_4x4()
    return _finish(bm, color, vc, matrix=m)

def strip(p0, p1, normal, width, depth, color, vc):
    """Thin box from p0 to p1 lying on a surface with the given outward normal (crack / rune groove)."""
    p0, p1, n = Vector(p0), Vector(p1), Vector(normal).normalized()
    a = (p1 - p0); L = a.length; a.normalize(); b = n.cross(a).normalized()
    m = Matrix((a, b, n)).transposed().to_4x4(); m.translation = (p0 + p1) / 2 + n * (depth * .5 - .006)
    return box_m((L + width * .6, width, depth), m, color, vc=vc)

def crack_path(origin, u, v, n, pts, width, glow):
    """Zigzag glowing crack: SlotFire core (vertex R = glow) inside a darker StoneDark rim."""
    o, u, v = Vector(origin), Vector(u), Vector(v)
    P = [o + u * x + v * y for x, y in pts]; parts = []
    for a, b in zip(P, P[1:]):
        parts.append(strip(a, b, n, width * 1.9, .010, "StoneDark", (0, 1, 1, 1)))
        parts.append(strip(a, b, n, width, .016, "SlotFire", (glow, 1, 1, 1)))
    return parts

def zigzag(length, amp, n=4):
    xs = [length * (i / n) for i in range(n + 1)]
    return [(x, (rng.uniform(-amp, amp) if 0 < i < n else 0)) for i, x in enumerate(xs)]

def to_object(name, parts):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    for p in parts:
        tmp = bpy.data.meshes.new("tmp"); p.to_mesh(tmp); p.free(); bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
    bm.to_mesh(me); bm.free()
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        u = pal_uv(poly.material_index)
        for li in poly.loop_indices: uv.data[li].uv = u
    me.materials.append(MAT)
    for poly in me.polygons: poly.material_index = 0
    obj = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(obj)
    return obj

# ------------------------------------------------------------------ enclosure design
D, T = .875, .17                    # wall centre-line distance from the core centre, wall thickness
SIDES = [  # (outward normal, tangent, wall height): Blender +Y = camera side (Unity -Z) -> lowest wall
    (Vector((0, 1, 0)), Vector((1, 0, 0)), .19), (Vector((0, -1, 0)), Vector((-1, 0, 0)), .33),
    (Vector((1, 0, 0)), Vector((0, -1, 0)), .26), (Vector((-1, 0, 0)), Vector((0, 1, 0)), .26)]
CORNERS = [(sx, sy) for sx in (-1, 1) for sy in (-1, 1)]

def wall_blocks(state, glow):
    parts = []
    for si, (n, t, h) in enumerate(SIDES):
        blocks = [(-.47, .40), (0.0, .44), (.47, .40)]   # (centre along tangent, length)
        for bi, (c, L) in enumerate(blocks):
            r = random.Random(si * 10 + bi)
            hh = h * r.uniform(.9, 1.08); yaw = r.uniform(-.04, .04); ctr = n * D + t * c
            jag, drop, tilt, gone = 0, 0, (0, 0, 0), False
            if state == "Cracked" and (si * 3 + bi) % 4 == 1: jag = .05                     # chipped tops
            if state == "Broken":
                k = (si * 3 + bi) % 5
                if k == 0: gone = True                                                      # knocked out block
                elif k in (1, 3): jag, drop = .12, hh * .45; tilt = (n.y * .14, -n.x * .14, yaw * 3); ctr = ctr - n * .04   # leaning, jagged
                else: jag, drop = .09, hh * .3                                             # lowered, jagged
                if bi == 1 and si in (0, 1): gone = True                                    # breaches in the near (camera) and far walls
            if gone:   # stub + rubble where the block stood
                parts.append(box((L * .9 if abs(t.x) else T, T if abs(t.x) else L * .9, .07), (ctr.x, ctr.y, .035), "StoneSide", top="StoneDark", jitter_top=.03))
                parts += crack_path(Vector((ctr.x, ctr.y, .07)) - t * (L * .4), t, n, (0, 0, 1), zigzag(L * .8, .03, 3), .05, glow["crack"])   # molten seam in the breach
                for k in range(3):
                    off = n * r.uniform(-.04, .06) + t * r.uniform(-.25, .25); q = ctr + off
                    parts.append(ico(r.uniform(.05, .085), (max(-.9, min(.9, q.x)), max(-.9, min(.9, q.y)), .05), (1.3, 1, .7), (0, 0, r.random() * 6), "StoneSide", jitter=.012, top="StoneTopWarm"))
                continue
            hh2 = hh - drop
            size = (L, T, hh2) if abs(t.x) > .5 else (T, L, hh2)
            m = Matrix.Translation(Vector((ctr.x, ctr.y, hh2 / 2 - .02))) @ Euler((tilt[0], tilt[1], yaw + tilt[2])).to_matrix().to_4x4()
            parts.append(box_m(size, m, "StoneSide", bevel=.025, top="StoneTopWarm", jitter_top=jag))
            # rune groove on the middle block's top (ember, all states; brighter as damage rises)
            if bi == 1 and state != "Broken" or (state == "Broken" and bi == 1 and si != 1):
                top = hh2 - .02 - (jag if state == "Broken" else 0) * .5
                a = Vector((ctr.x, ctr.y, top)) - t * .12; b = Vector((ctr.x, ctr.y, top)) + t * .12
                parts.append(strip(a, b, (0, 0, 1), .035, .014, "SlotFire", (glow["rune"], 1, 1, 1)))
                for s in (-1, 1):
                    q = Vector((ctr.x, ctr.y, top)) + t * (s * .17)
                    parts.append(strip(q - n * .03, q + n * .03, (0, 0, 1), .03, .014, "SlotFire", (glow["rune"], 1, 1, 1)))
            # cracks over the top and down the outward face
            if tilt == (0, 0, 0) and (state != "Intact" and (si * 3 + bi) % 2 == (0 if state == "Cracked" else 1) or (state == "Broken" and bi != 1)):
                top = hh2 - .02 - jag * .6
                o = Vector((ctr.x, ctr.y, top)) - t * (L * .42) + n * rng.uniform(-.03, .03)
                parts += crack_path(o, t, n, (0, 0, 1), zigzag(L * .84, .035, 4), .028, glow["crack"])
                o2 = Vector((ctr.x, ctr.y, 0)) + n * (T / 2) + t * rng.uniform(-.1, .1)
                parts += crack_path(o2 + Vector((0, 0, top - .01)), Vector((0, 0, -1)), t, n, zigzag(max(.08, top - .05), .03, 3), .024, glow["crack"])
    return parts

def obelisks(state, glow):
    parts = []
    for ci, (sx, sy) in enumerate(CORNERS):
        x, y = sx * .83, sy * .83
        near = sy > 0
        h = .52 if near else .74
        snapped = state == "Broken" and ci == 2          # far obelisk on screen-left (Blender +x,-y) snapped
        hb = h * .42 if snapped else h
        parts.append(box((.28, .28, .07), (x, y, .015), "StoneDark", top="WallSideDark", bevel=.015))         # footing
        parts.append(box((.2, .2, hb), (x, y, hb / 2), "WallSideDark", bevel=.02, top="WallSide", jitter_top=.08 if snapped else (.03 if state == "Broken" else 0)))
        if not snapped:
            parts.append(cone(.15, .0, .14, 4, (x, y, h + .07), "SlotHE", rot=(0, 0, math.pi / 4), vc=(glow["cap"], 1, 1, 1)))   # ember cap (v17.5 R glow)
            parts.append(box((.24, .24, .035), (x, y, h + .005), "StoneDark"))                               # collar
        else:  # the broken top lies on the ground, tipped away from the core
            parts.append(box((.2, .2, h * .5), (x - sx * .07, y - sy * .40, .12), "WallSideDark", rot=(sy * 1.35, 0, .25), bevel=.02, top="WallSide"))
            parts.append(cone(.15, 0, .14, 4, (x - sx * .07, y - sy * .70, .1), "SlotHE", rot=(sy * 1.7, 0, math.pi / 4 + .25), vc=(glow["cap"], 1, 1, 1)))
        # rune slit on the two outward faces (vertex R glow)
        top = (hb - .1) if not snapped else hb - .06
        for n in (Vector((sx, 0, 0)), Vector((0, sy, 0))):
            c = Vector((x, y, 0)) + n * .1
            parts.append(strip(c + Vector((0, 0, .12)), c + Vector((0, 0, max(.16, top))), n, .045, .016, "SlotFire", (glow["rune"], 1, 1, 1)))
        if state != "Intact":  # crack across the obelisk face facing the camera (+Y in Blender)
            c = Vector((x, y + .1, 0))
            parts += crack_path(c + Vector((-.08, 0, hb * .8)), Vector((1, 0, 0)), Vector((0, 0, -1)), (0, 1, 0), [(0, 0), (.06, .1), (.11, .14), (.16, .26)], .022, glow["crack"])
    return parts

def ground_fissures(state, glow):
    """Glowing cracks in the tiles around the core (flat, z ~ 0), readable from the 40-degree camera."""
    if state == "Intact": return []
    parts = []; rr = random.Random(7 if state == "Cracked" else 11)
    n = 5 if state == "Cracked" else 9
    for k in range(n):
        ang = (k + rr.uniform(-.3, .3)) * (2 * math.pi / n) + .3
        r0 = rr.uniform(.5, .62); r1 = .99 if state == "Broken" else rr.uniform(.82, .95)
        d = Vector((math.cos(ang), math.sin(ang), 0)); side = Vector((-d.y, d.x, 0))
        o = d * r0
        pts = [(0, 0), ((r1 - r0) * .35, rr.uniform(-.04, .04)), ((r1 - r0) * .7, rr.uniform(-.05, .05)), (r1 - r0, rr.uniform(-.03, .03))]
        # clamp inside the 2x2 footprint
        P = [o + d * a + side * b for a, b in pts]; P = [Vector((max(-.94, min(.94, p.x)), max(-.94, min(.94, p.y)), .0)) for p in P]
        for a, b in zip(P, P[1:]):
            parts.append(strip(a, b, (0, 0, 1), .075, .012, "GroundLavSide", (0, 1, 1, 1)))
            parts.append(strip(a, b, (0, 0, 1), .042, .018, "SlotFire", (glow["fissure"], 1, 1, 1)))
    if state == "Broken":  # scattered rubble chips
        for k in range(10):
            ang = rr.uniform(0, 6.28); r = rr.uniform(.6, .97)
            p = (max(-.9, min(.9, math.cos(ang) * r * 1.1)), max(-.9, min(.9, math.sin(ang) * r * 1.1)))
            parts.append(ico(rr.uniform(.035, .06), (p[0], p[1], .02), (1.4, 1, .6), (0, 0, rr.random() * 6), rr.choice(["StoneSide", "WallSideDark"]), jitter=.008, top="StoneTopWarm"))
    return parts

def build(state):
    rng.seed(1730 + len(state))
    glow = GLOW[state]
    parts = wall_blocks(state, glow) + obelisks(state, glow) + ground_fissures(state, glow)
    return to_object("SM_Core_Enclosure_" + state, parts)

# ------------------------------------------------------------------ scene
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
pal_path = os.path.join(ART, "Textures", "T_Env_Palette_D.png")
img = bpy.data.images.load(pal_path); img.name = "T_Env_Palette_D"

def toon_material(name, emission_strength=0.0, red=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = 'Closest'
    diff = nt.nodes.new("ShaderNodeBsdfDiffuse"); nt.links.new(tex.outputs[0], diff.inputs[0])
    s2r = nt.nodes.new("ShaderNodeShaderToRGB"); ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.color_ramp.interpolation = 'CONSTANT'
    ramp.color_ramp.elements[0].color = (0.30, 0.22, 0.42, 1); ramp.color_ramp.elements[1].position = .12; ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    e = ramp.color_ramp.elements.new(.03); e.color = (.55, .46, .66, 1)
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
    nt.links.new(diff.outputs[0], s2r.inputs[0]); nt.links.new(s2r.outputs[0], ramp.inputs[0])
    nt.links.new(tex.outputs[0], mul.inputs[6]); nt.links.new(ramp.outputs[0], mul.inputs[7])
    col = mul.outputs[2]
    if emission_strength > 0:   # == ToonCore v17.5: c = lerp(c, baseCol * _VColorEmission * hot, vertex.r)
        va = nt.nodes.new("ShaderNodeVertexColor"); va.layer_name = "Color"
        sep = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(va.outputs[0], sep.inputs[0])
        hot = nt.nodes.new("ShaderNodeMapRange"); hot.clamp = True; nt.links.new(sep.outputs[0], hot.inputs[0])   # 1 + 0.35 * saturate(R*20-19)
        hot.inputs[1].default_value = .95; hot.inputs[2].default_value = 1.0; hot.inputs[3].default_value = emission_strength; hot.inputs[4].default_value = emission_strength * 1.35
        em = nt.nodes.new("ShaderNodeMix"); em.data_type = 'RGBA'; em.blend_type = 'MULTIPLY'; em.inputs[0].default_value = 1
        nt.links.new(tex.outputs[0], em.inputs[6]); nt.links.new(hot.outputs[0], em.inputs[7])
        lrp = nt.nodes.new("ShaderNodeMix"); lrp.data_type = 'RGBA'; lrp.blend_type = 'MIX'; nt.links.new(sep.outputs[0], lrp.inputs[0])
        nt.links.new(col, lrp.inputs[6]); nt.links.new(em.outputs[2], lrp.inputs[7]); col = lrp.outputs[2]
    if red > 0:                 # CoreDamageFx critical: _HiColor (1,.12,.08) * _HiAmount on the core
        add = nt.nodes.new("ShaderNodeMix"); add.data_type = 'RGBA'; add.blend_type = 'ADD'; add.inputs[0].default_value = 1
        nt.links.new(col, add.inputs[6]); add.inputs[7].default_value = (red, red * .12, red * .08, 1); col = add.outputs[2]
    emi = nt.nodes.new("ShaderNodeEmission"); nt.links.new(col, emi.inputs[0]); nt.links.new(emi.outputs[0], out.inputs[0])
    return m

MAT = bpy.data.materials.new("M_Core_Enclosure")  # export material slot (Unity: material import off, M_Core_Enclosure assigned by the builder)
objs = {s: build(s) for s in ("Intact", "Cracked", "Broken")}
for s, o in objs.items():
    me = o.data
    vs = [v.co for v in me.vertices]
    mn = [min(v[i] for v in vs) for i in range(3)]; mx = [max(v[i] for v in vs) for i in range(3)]
    print("CORE ENCLOSURE %s: objects=1 slots=%d tris=%d verts=%d bounds=(%.2f..%.2f, %.2f..%.2f, %.2f..%.2f) colorAttr=%d uv=%d" % (
        s, len(me.materials), sum(len(p.vertices) - 2 for p in me.polygons), len(vs), mn[0], mx[0], mn[1], mx[1], mn[2], mx[2], len(me.color_attributes), len(me.uv_layers)))
    assert len(me.materials) == 1 and max(abs(mn[0]), abs(mx[0]), abs(mn[1]), abs(mx[1])) <= 1.005, "enclosure must fit the 2x2 core footprint"

if EXPORT:
    os.makedirs(OUT, exist_ok=True)
    for s, o in objs.items():
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, o.name + ".fbx"), use_selection=True, object_types={'MESH'},
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                                 bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True, colors_type='LINEAR',
                                 add_leaf_bones=False, bake_anim=False, path_mode='STRIP', use_tspace=False)
        print("EXPORTED", o.name)

# ------------------------------------------------------------------ preview render (4 states)
if RENDER:
    import tempfile
    core_fbx = os.path.join(ART, "Towers", "SM_Prop_Core_01.fbx")
    sc = scene; sc.render.engine = 'BLENDER_EEVEE_NEXT'; sc.render.resolution_x, sc.render.resolution_y = 760, 600
    sc.render.film_transparent = False; sc.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new("W"); world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (.05, .05, .07, 1); sc.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    envm = toon_material("Env", 0); encm = toon_material("Enc", 1.45)   # v17.5 _VColorEmission
    for o in objs.values(): o.data.materials[0] = encm
    # ground tiles: 4x4 cells around the 2x2 core, tile top = 0 (enclosure pivot)
    tiles = []
    for ix in range(-2, 2):
        for iy in range(-2, 2):
            h = 0 if (ix in (-1, 0) and iy in (-1, 0)) else random.Random(ix * 7 + iy).uniform(-.03, .05)
            tiles.append(box((.97, .97, .3), (ix + .5, iy + .5, h - .15), "GroundLavSide", top="GroundLavTop" if (ix + iy) % 3 else "GroundLavTop2", bevel=.03))
    tobj = to_object("Tiles", tiles); tobj.data.materials[0] = envm
    bpy.ops.import_scene.fbx(filepath=core_fbx)
    core = [o for o in bpy.context.selected_objects if o.type == 'MESH'][0]
    core.location.z -= .25          # core pivot sits ArtCatalog.tileTop below the tile top
    coremat_n = toon_material("Core", 0); coremat_r = toon_material("CoreRed", 0, red=.55)
    core.data.materials.clear(); core.data.materials.append(coremat_n)
    # FX stand-ins for the critical panel (8 smoke + 8 sparks max, as in PF_VFX_CoreSmoke / PF_VFX_CoreSparks)
    smk = bpy.data.materials.new("Smoke"); smk.use_nodes = True; smk.blend_method = 'BLEND'; snt = smk.node_tree
    bs = snt.nodes["Principled BSDF"]; bs.inputs["Base Color"].default_value = (.20, .17, .16, 1); bs.inputs["Alpha"].default_value = .38; bs.inputs["Roughness"].default_value = 1
    spk = bpy.data.materials.new("Spark"); spk.use_nodes = True; knt = spk.node_tree; knt.nodes.clear()
    ko = knt.nodes.new("ShaderNodeOutputMaterial"); ke = knt.nodes.new("ShaderNodeEmission"); ke.inputs[0].default_value = (1, .55, .12, 1); ke.inputs[1].default_value = 6
    knt.links.new(ke.outputs[0], ko.inputs[0])
    fx = []
    rr = random.Random(5)
    for k in range(7):
        t = k / 7; p = (rr.uniform(-.2, .2) + t * .25, rr.uniform(-.15, .15) - .8, .3 + t * 1.5)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=.11 + t * .17, location=p); o = bpy.context.object
        o.scale = (1, 1, .8); o.data.materials.append(smk); fx.append(o)
    for k in range(8):
        a = rr.uniform(0, 6.28); r = rr.uniform(.25, .8); p = (math.cos(a) * r, math.sin(a) * r, rr.uniform(.4, 1.3))
        bpy.ops.mesh.primitive_cube_add(size=1, location=p); o = bpy.context.object; o.scale = (.025, .025, .09)
        o.rotation_euler = (rr.uniform(-.6, .6), rr.uniform(-.6, .6), 0); o.data.materials.append(spk); fx.append(o)
    # game-like camera: pitch 40, yaw 10 (Unity) == looking toward Blender -Y, ortho
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.3
    fwd = Vector((-math.sin(math.radians(10)) * math.cos(math.radians(40)), -math.cos(math.radians(10)) * math.cos(math.radians(40)), -math.sin(math.radians(40))))
    # Unity +Z (camera forward) = Blender -Y; Unity +X = Blender -X
    fwd = Vector((math.sin(math.radians(10)) * math.cos(math.radians(40)) * -1, -math.cos(math.radians(10)) * math.cos(math.radians(40)), -math.sin(math.radians(40))))
    target = Vector((0, 0, .55)); cam.location = target - fwd * 12
    cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
    # bloom stand-in: compositor glare (threshold ~ Unity bloom 1.1)
    sc.use_nodes = True; cn = sc.node_tree; cn.nodes.clear()
    rl = cn.nodes.new("CompositorNodeRLayers"); gl = cn.nodes.new("CompositorNodeGlare"); gl.glare_type = 'FOG_GLOW'; gl.threshold = 1.0; gl.size = 7; gl.mix = -.55
    co = cn.nodes.new("CompositorNodeComposite"); cn.links.new(rl.outputs[0], gl.inputs[0]); cn.links.new(gl.outputs[0], co.inputs[0])
    tmpd = tempfile.mkdtemp(); shots = []
    for st in ("Intact", "Cracked", "Broken", "Critical"):
        mesh_state = "Broken" if st == "Critical" else st
        for s, o in objs.items(): o.hide_render = s != mesh_state
        for o in fx: o.hide_render = st != "Critical" and not (st == "Broken" and o.data.materials[0] == smk and fx.index(o) < 4)
        core.data.materials[0] = coremat_r if st == "Critical" else coremat_n
        sc.render.filepath = os.path.join(tmpd, st + ".png"); bpy.ops.render.render(write_still=True); shots.append((st, sc.render.filepath))
    import json
    with open(os.path.join(tmpd, "shots.json"), "w") as f: json.dump(shots, f)
    print("RENDER_SHOTS", os.path.join(tmpd, "shots.json"))
