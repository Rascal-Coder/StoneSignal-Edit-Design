"""StoneSignal stylized art batch 1 (procedural, Blender 5.x headless).

Usage:
  blender.exe --background --factory-startup --python build_stylized_batch1.py -- <projectRoot> [--no-render]

Follows Docs/ArtSpec/美术规范.md: 1 unit = 1 m = 1 cell, pivot at base centre, front = Blender -Y
(-> Unity +Z), FBX -Z forward / Y up / apply transform, palette texture T_Env_Palette_D (16x16 cells
of 16 px), vertex colour R=wind G=AO B=layer/variation A=phase (linear export).
Outputs: Assets/Game/Art/Stylized/{Environment,Towers,Enemies}/*.fbx, Textures/T_Env_Palette_D.png,
ArtSource/Stylized/StoneSignal_Stylized_Batch1.blend, stylized_manifest.json, previews.
"""
import bpy, bmesh, math, random, os, sys, json, struct, zlib
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = os.path.abspath(argv[0] if argv else os.getcwd())
DO_RENDER = "--no-render" not in argv
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Stylized")
SRC = os.path.join(ROOT, "ArtSource", "Stylized")
PREVIEW_DIR = os.path.join(ROOT, "Verification", "StylizedArt1")
for d in (ART, SRC, PREVIEW_DIR, os.path.join(ART, "Environment"), os.path.join(ART, "Towers"),
          os.path.join(ART, "Enemies"), os.path.join(ART, "Textures")):
    os.makedirs(d, exist_ok=True)

# ------------------------------------------------------------------ palette (spec ch.2 / 7.2)
PALETTE = [
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
]
PIDX = {n: i for i, (n, _) in enumerate(PALETTE)}
def hex_rgb(h): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))

def write_palette_png(path):
    size, cell = 256, 16
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            i = (y // cell) * 16 + (x // cell)
            r, g, b = hex_rgb(PALETTE[i][1]) if i < len(PALETTE) else (240, 240, 240)
            row += bytes((r, g, b))
        rows.append(bytes(row))
    raw = zlib.compress(b"".join(rows), 9)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)) + chunk(b"IDAT", raw) + chunk(b"IEND", b"")
    with open(path, "wb") as f: f.write(png)

def pal_uv(i):  # cell centre; image row 0 is top, UV v=1 is top
    cx, cy = i % 16, i // 16
    return ((cx + .5) / 16.0, 1.0 - (cy + .5) / 16.0)

# ------------------------------------------------------------------ geometry helpers
rng = random.Random(37)

class Part:
    """bmesh fragment: every face carries palette index (material_index) + vertex colour."""
    def __init__(self): self.bm = bmesh.new()

def _finish(bm, color, vc=(0, 1, 1, 1), smooth=False, bevel=0.0, seg=1, angle=30, matrix=None):
    for f in bm.faces:
        f.material_index = PIDX[color]; f.smooth = smooth
    if bevel > 0:
        edges = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > math.radians(angle)]
        if edges:
            bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type='OFFSET', segments=seg, profile=.5,
                            affect='EDGES', clamp_overlap=True)
    if matrix is not None: bm.transform(matrix)
    layer = bm.loops.layers.float_color.get("Color") or bm.loops.layers.float_color.new("Color")
    for f in bm.faces:
        for l in f.loops: l[layer] = vc
    return bm

def box(size, center=(0, 0, 0), color="StoneSide", bevel=0.0, seg=1, rot=None, vc=(0, 1, 1, 1), smooth=False):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    m = Matrix.Translation(Vector(center)) @ (Euler(rot).to_matrix().to_4x4() if rot else Matrix())
    return _finish(bm, color, vc, smooth, bevel, seg, matrix=m)

def cyl(r1, r2, h, segs=8, center=(0, 0, 0), color="Wood", rot=None, bevel=0.0, vc=(0, 1, 1, 1), smooth=False, cap=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segs, radius1=r1, radius2=r2, depth=h)
    m = Matrix.Translation(Vector(center)) @ (Euler(rot).to_matrix().to_4x4() if rot else Matrix())
    return _finish(bm, color, vc, smooth, bevel, angle=50, matrix=m)

def ico(radius, sub=1, center=(0, 0, 0), scale=(1, 1, 1), rot=(0, 0, 0), color="LeafOrange", jitter=0.0,
        vc=(0, 1, 1, 1), smooth=False, colorfn=None):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=radius)
    for v in bm.verts:
        if jitter: v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * jitter
    m = Matrix.Translation(Vector(center)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal(Vector(scale)).to_4x4()
    _finish(bm, color, vc, smooth, matrix=m)
    if colorfn:
        for f in bm.faces: f.material_index = PIDX[colorfn(f)]
    return bm

def uv_sphere(radius, segs=12, rings=8, center=(0, 0, 0), scale=(1, 1, 1), color="Skin", vc=(0, 1, 1, 1), smooth=True, hemi=False):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=radius)
    if hemi:
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context='VERTS')
        edges = [e for e in bm.edges if e.is_boundary]
        if edges: bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal(Vector(scale)).to_4x4()
    return _finish(bm, color, vc, smooth, matrix=m)

def to_object(name, parts, origin=(0, 0, 0), merge=True):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    for p in parts:
        tmp = bpy.data.meshes.new("tmp"); p.to_mesh(tmp); p.free()
        bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
    if origin != (0, 0, 0): bm.transform(Matrix.Translation(-Vector(origin)))
    bm.to_mesh(me); bm.free()
    # palette UVs from material index, then collapse to one shared material
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        u = pal_uv(poly.material_index)
        for li in poly.loop_indices: uv.data[li].uv = u
    me.materials.append(MAT)
    for poly in me.polygons: poly.material_index = 0
    obj = bpy.data.objects.new(name, me)
    obj.location = origin
    bpy.context.scene.collection.objects.link(obj)
    return obj

def tri_count(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)

def jitter_bm(bm, amount):
    for v in bm.verts: v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * amount

# ------------------------------------------------------------------ assets
def rough_tile(bm, ztop):
    """Uneven, rounded, gently undulating tile top (visual only; stays within +-2 cm so walls sit stably)."""
    top_e = [e for e in bm.edges if all(v.co.z > ztop - .05 for v in e.verts)]
    bmesh.ops.subdivide_edges(bm, edges=top_e, cuts=1, use_grid_fill=True)
    # grid fill can leave the inner top n-gon open (read as a dark 'hole'): close any open boundary on the top
    holes = [e for e in bm.edges if e.is_boundary and all(v.co.z > ztop - .05 for v in e.verts)]
    if holes:
        new = bmesh.ops.holes_fill(bm, edges=holes, sides=0)["faces"]
        for f in new: f.material_index = PIDX["GroundLavTop"]
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ph = rng.uniform(0, 6)
    for f in bm.faces:  # v8 tile variation: worn tone patches inside each tile (low contrast, ArtDirection rule 1)
        if f.normal.z > .9 and rng.random() < .3: f.material_index = PIDX[rng.choice(["GroundLavTop2", "GroundLavTop2", "Dirt"])]
    for v in bm.verts:
        if v.co.z > ztop - .06:
            v.co.z += .012 * math.sin(v.co.x * 5 + ph) * math.cos(v.co.y * 4 - ph) + rng.uniform(-.004, .004)
        if abs(abs(v.co.x) - .49) < .06 or abs(abs(v.co.y) - .49) < .06:  # irregular edges
            v.co.x += rng.uniform(-.015, .015); v.co.y += rng.uniform(-.015, .015)
def tile_detail(parts, ztop):
    for k in range(rng.randint(2, 5)):  # pebbles
        parts.append(ico(rng.uniform(.03, .06), 1, (rng.uniform(-.4, .4), rng.uniform(-.4, .4), ztop), (1.3, 1, .5), (0, 0, rng.random() * 6),
                         rng.choice(["StoneSide", "DirtDark", "GroundLavSide"]), jitter=.01))
    if rng.random() < .6:  # dirt patch
        parts.append(ico(rng.uniform(.12, .22), 1, (rng.uniform(-.3, .3), rng.uniform(-.3, .3), ztop - .005), (1.5, 1, .08), (0, 0, rng.random() * 6), "Dirt", jitter=.02))
    if rng.random() < .5:  # crack
        parts.append(box((rng.uniform(.18, .35), .014, .01), (rng.uniform(-.2, .2), rng.uniform(-.2, .2), ztop + .002), "GroundLavSide", rot=(0, 0, rng.uniform(0, 3.14))))

TILE_VARIANTS = "AABCDE"
def cell_hash(x, y):
    h = (x * 73856093) ^ (y * 19349663) ^ 0x5bd1e995
    h = (h ^ (h >> 13)) * 0x27d4eb2d & 0xffffffff
    return h ^ (h >> 15)

def tile(variant):
    rng.seed(ord(variant) * 31 + 17)  # each variant: its own chip/crack/pit placement
    top = "GroundLavTop2" if variant == "D" else "GroundLavTop"
    bm = box((.98, .98, .25), (0, 0, .125), "GroundLavSide")
    for f in bm.faces:
        if f.normal.z > .9: f.material_index = PIDX[top]
    top_edges = [e for e in bm.edges if all(v.co.z > .24 for v in e.verts)]
    bmesh.ops.bevel(bm, geom=top_edges, offset=.04, offset_type='OFFSET', segments=1, profile=.5, affect='EDGES', clamp_overlap=True)
    for f in bm.faces:
        if f.normal.z > .5: f.material_index = PIDX[top]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -.9], context='FACES_ONLY')
    rough_tile(bm, .25)
    parts = [bm]; tile_detail(parts, .255)
    if variant == "E":  # small shallow pits/dents in random spots (subtle, not holes)
        for k in range(2):
            parts.append(ico(rng.uniform(.05, .08), 1, (rng.uniform(-.32, .32), rng.uniform(-.32, .32), .252), (1.4, 1, .12), (0, 0, rng.random() * 6), "GroundLavTop2", jitter=.01))
    if variant == "B":  # worn tile: a chipped inset slab
        parts.append(box((.36, .30, .015), (.18, -.2, .252), "GroundLavTop2", bevel=.01))
    return [to_object(f"SM_Env_Tile_Stone_{variant}_01", parts)]

TETRIS = {"1x1": [(0, 0)], "TetrisI": [(0, 0), (0, 1), (0, 2), (0, 3)], "TetrisO": [(0, 0), (1, 0), (0, 1), (1, 1)],
          "TetrisT": [(0, 0), (1, 0), (2, 0), (1, 1)], "TetrisS": [(0, 0), (1, 0), (1, 1), (2, 1)],
          "TetrisZ": [(0, 1), (1, 1), (1, 0), (2, 0)], "TetrisL": [(0, 0), (0, 1), (0, 2), (1, 0)],
          "TetrisJ": [(1, 0), (1, 1), (1, 2), (0, 0)]}
def stone_block(shape, warm):
    """Player tetromino wall: one chunky pillowy stone per cell (visible seams), size/rotation jitter, chipped corners,
    cracks, moss/dirt patches, per-cell colour variation (vertex B) - shader adds world-space mottling (_Mottle)."""
    parts = []
    top = "StoneTopWarm" if warm else "StoneTopCool"
    for (x, y) in TETRIS[shape]:
        sz = rng.uniform(.86, .91); h = rng.uniform(.56, .62)
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector((sz, sz, h)), verts=bm.verts)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=.11, offset_type='OFFSET', segments=3, profile=.62, affect='EDGES', clamp_overlap=True)
        for v in bm.verts:  # pillowy bulge + weathering jitter, chipped corners
            v.co.x *= 1 + .04 * (1 - abs(v.co.z) / (h / 2)); v.co.y *= 1 + .04 * (1 - abs(v.co.z) / (h / 2))
            v.co += Vector((rng.uniform(-.012, .012), rng.uniform(-.012, .012), rng.uniform(-.01, .01)))
        for k in range(rng.randint(1, 3)):  # chips: push a random corner region inward
            c = Vector((rng.choice((-1, 1)) * sz / 2, rng.choice((-1, 1)) * sz / 2, h / 2))
            for v in bm.verts:
                d = (v.co - c).length
                if d < .16: v.co += (Vector((0, 0, 0)) - v.co).normalized() * (.16 - d) * .5
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(rng.uniform(-4, 4)), 3, 'Z'))
        bmesh.ops.translate(bm, vec=Vector((x + rng.uniform(-.015, .015), -y + rng.uniform(-.015, .015), h / 2)), verts=bm.verts)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z < .02 and f.normal.z < -.9], context='FACES_ONLY')
        mossy = rng.random() < .45; vb = rng.uniform(.25, 1.0)
        layer = bm.loops.layers.float_color.new("Color")
        for f in bm.faces:
            n = f.normal; cz = f.calc_center_median().z
            if n.z > .6: f.material_index = PIDX[top]  # v7: no brown moss lids, mottle comes from the shader
            elif cz < .12: f.material_index = PIDX["DirtDark"] if rng.random() < .5 else PIDX["WallSideDark"]  # dirt at the foot
            else: f.material_index = PIDX["WallSideDark"] if (n.x > .5 or n.y > .5) else PIDX["WallSide"]
            for l in f.loops: l[layer] = (0, 1, vb, rng.random())
        parts.append(bm)
        for k in range(rng.randint(0, 2)):  # cracks: thin dark slivers on a side face
            side = rng.choice([(1, 0), (-1, 0), (0, 1), (0, -1)])
            cx = x + side[0] * (sz / 2 + .004); cy = -y + side[1] * (sz / 2 + .004)
            off = rng.uniform(-.2, .2)
            parts.append(box((.012 if side[0] else .18, .18 if side[0] else .012, .012), (cx + (0 if side[0] else off), cy + (off if side[0] else 0), rng.uniform(.2, .45)),
                             "StoneDark", rot=(0, 0, 0) if True else None))
            parts[-1].transform(Matrix.Translation((cx, cy, 0)) @ Matrix.Rotation(math.radians(rng.uniform(-50, 50)), 4, 'X' if side[0] else 'Y') @ Matrix.Translation((-cx, -cy, 0)))
    return [to_object(f"SM_Env_Rock_{shape}_01", parts)]

def leaf_tuft(center, normal, r, color, vc):
    """Serrated maple-leaf tuft: 5-point star card, cupped outward (10 tris)."""
    bm = bmesh.new()
    n = Vector(normal).normalized()
    t = n.orthogonal().normalized(); bt = n.cross(t)
    spin = rng.uniform(0, math.tau)
    c = bm.verts.new(Vector(center) + n * r * .35)
    ring = []
    for k in range(10):
        a = spin + k * math.tau / 10
        rr = r * (1.0 if k % 2 == 0 else .55) * rng.uniform(.85, 1.1)
        ring.append(bm.verts.new(Vector(center) + (t * math.cos(a) + bt * math.sin(a)) * rr - n * r * .12))
    for k in range(10):
        bm.faces.new((c, ring[k], ring[(k + 1) % 10]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.dot(n) < 0: f.normal_flip()
    return _finish(bm, color, vc)

def tree(variant):
    # Layered canopy per reference: stacked tiers of fluffy leaf tufts over a dark inner core.
    cfg = {"A": dict(trunk_h=1.3, tiers=[(1.55, .62, .42), (2.15, .52, .40), (2.7, .36, .34)], off=(0, 0), lean=0),
           "B": dict(trunk_h=.95, tiers=[(1.3, .85, .45), (1.85, .72, .42), (2.35, .45, .34)], off=(0, 0), lean=0),
           "C": dict(trunk_h=1.15, tiers=[(1.45, .7, .42), (2.0, .58, .4), (2.5, .4, .32)], off=(.3, 0), lean=12)}[variant]
    parts = []
    lean = math.radians(cfg["lean"])
    trunk = cyl(.13, .08, cfg["trunk_h"] + .4, 8, (0, 0, (cfg["trunk_h"] + .4) / 2), "Trunk", vc=(0, 1, 0, 0))
    if lean: bmesh.ops.rotate(trunk, verts=trunk.verts, cent=Vector((0, 0, 0)), matrix=Matrix.Rotation(lean, 3, 'Y'))
    parts.append(trunk)
    ox, oy = cfg["off"]
    for i in range(3):
        a = i * 2.1 + .4
        d = Vector((math.cos(a) * .4, math.sin(a) * .4, .55)).normalized()
        br = cyl(.05, .025, .55, 4, (0, 0, .27), "Trunk", vc=(.3, 1, 0, 0))
        br.transform(Matrix.Translation(Vector((ox * .6, 0, cfg["trunk_h"] - .1))) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4())
        parts.append(br)
    tier_cols = [("LeafShadow", "LeafRed", "LeafOrange"), ("LeafRed", "LeafOrange", "LeafGold"), ("LeafOrange", "LeafGold", "LeafHighlight")]
    for ti, (z, rad, hz) in enumerate(cfg["tiers"]):
        cx = ox * (1 + ti * .2)
        # dark inner core of each tier (shadow volume)
        parts.append(ico(rad * .82, 1, (cx, oy, z), (1, 1, hz / rad * 1.0), (0, 0, rng.random()), "LeafCore", jitter=.03,
                         vc=(.3, 1, 0, rng.random())))
        # tufts over the upper/outer shell (golden-spiral distribution)
        count = int(30 * rad / .6) + 10  # v9 WebGL budget: ~half the tufts, larger
        layer_b = (0.0, .5, 1.0)[ti]
        for k in range(count):
            u = (k + .5) / count
            zz = 1 - u * 1.25           # cover top down to ~-0.25 (bottom stays dark core)
            if zz < -1: continue
            phi = k * 2.39996
            rr = math.sqrt(max(0, 1 - zz * zz))
            nrm = Vector((rr * math.cos(phi), rr * math.sin(phi), zz))
            pos = Vector((cx, oy, z)) + Vector((nrm.x * rad, nrm.y * rad, nrm.z * hz)) * rng.uniform(.92, 1.08)
            lo, mid, hi = tier_cols[ti]
            col = hi if zz > .55 else (mid if zz > -.05 else lo)
            if ti == 2 and zz > .8 and rng.random() < .5: col = "LeafHighlight"
            # lit side (upper-left toward camera, -X -Y) gets the brighter tone
            if col == mid and (nrm.x + nrm.y) < -.6: col = hi
            sz = rng.uniform(.15, .21) * (1 + rad * .2)
            parts.append(leaf_tuft(pos, nrm + Vector((0, 0, .25)), sz, col, (.6 + .4 * ti / 2, 1, layer_b, rng.random())))
    objs = [to_object(f"SM_Env_Tree_Maple_{variant}_01", parts)]
    o = objs[0]; mz = min(v.co.z for v in o.data.vertices)
    for v in o.data.vertices: v.co.z -= mz
    return objs

def rock_small():
    bm = ico(.28, 1, (0, 0, .14), (1.2, .9, .6), (0, 0, .4), "StoneSide", jitter=.05)
    for f in bm.faces:
        if f.normal.z > .55: f.material_index = PIDX["StoneTopCool"]
        elif f.normal.z < -.2: f.material_index = PIDX["StoneDark"]
    small = ico(.13, 1, (.3, -.12, .06), (1, .9, .7), (0, 0, 1), "StoneSide", jitter=.025)
    for f in small.faces:
        if f.normal.z > .55: f.material_index = PIDX["StoneTopWarm"]
    objs = [to_object("SM_Env_Rock_Small_01", [bm, small])]
    # pivot at base: shift so min z = 0
    o = objs[0]; mz = min(v.co.z for v in o.data.vertices)
    for v in o.data.vertices: v.co.z -= mz
    return objs

def barrel():
    parts = [cyl(.22, .27, .32, 12, (0, 0, .16), "Wood", vc=(0, 1, rng.random(), 1), cap=True),
             cyl(.27, .22, .32, 12, (0, 0, .48), "Wood", vc=(0, 1, rng.random(), 1)),
             cyl(.235, .235, .05, 12, (0, 0, .08), "Trunk"), cyl(.28, .28, .05, 12, (0, 0, .32), "Trunk"),
             cyl(.235, .235, .05, 12, (0, 0, .58), "Trunk"), cyl(.19, .19, .02, 12, (0, 0, .64), "Trunk")]
    for p in parts:
        for f in p.faces: f.smooth = abs(f.normal.z) < .5
    return [to_object("SM_Prop_Barrel_01", parts)]

def crate():
    s = .56
    parts = [box((s, s, s), (0, 0, s / 2), "Wood", bevel=.02)]
    t = .07
    for x in (-1, 1):
        for y in (-1, 1):
            parts.append(box((t, t, s + .01), (x * (s / 2 - t / 2 + .005), y * (s / 2 - t / 2 + .005), s / 2), "Trunk", bevel=.012))
    for z in (t / 2, s - t / 2):
        for (dx, dy, lx, ly) in ((0, -1, s + .01, t), (0, 1, s + .01, t), (-1, 0, t, s + .01), (1, 0, t, s + .01)):
            parts.append(box((lx, ly, t), (dx * (s / 2 - t / 2 + .006), dy * (s / 2 - t / 2 + .006), z), "Trunk", bevel=.012))
    # diagonal brace on the front face
    parts.append(box((s * 1.18, .03, .07), (0, -s / 2 - .012, s / 2), "Trunk", rot=(0, math.radians(45), 0)))
    return [to_object("SM_Prop_Crate_01", parts)]

def grass_tuft():
    parts = []
    for i in range(9):
        a = i * math.tau / 9 + rng.uniform(-.2, .2); r = rng.uniform(.03, .12); h = rng.uniform(.22, .4)
        b = cyl(.035, 0.0, h, 3, (0, 0, h / 2), "GrassDry" if i % 3 else "LeafGold", vc=(1, 1, rng.random(), rng.random()))
        tilt = Matrix.Rotation(rng.uniform(.15, .45), 4, Vector((math.sin(a), -math.cos(a), 0)))
        b.transform(Matrix.Translation((math.cos(a) * r, math.sin(a) * r, 0)) @ tilt)
        parts.append(b)
    return [to_object("SM_Env_Grass_Tuft_01", parts)]

# ---------------------------------------------------------------- turrets (mount on block top; pivot = footprint base centre)
def plinth(fx, fy, h=.16):
    return [box((fx * .9, fy * .9, h), (0, 0, h / 2), "StoneSide", bevel=.03),
            box((fx * .9 + .02, fy * .9 + .02, .04), (0, 0, h - .02), "Metal", bevel=.01)]

def turret(name, base, head, pivot, barrel=None, bpivot=None, muzzle=None):
    """Hierarchy (Unity): <SM>_Base (static) and <SM>_Head (yaw pivot, centred) > <SM>_Barrel (pitch pivot) > <SM>_Muzzle (empty).
    Blender keeps Head parented to Base for export convenience; StylizedArtIntegration re-parents Base/Head as siblings."""
    b = to_object(name + "_Base", base)
    objs = [b]; last = b
    if head:
        h = to_object(name + "_Head", head, origin=tuple(pivot)); h.parent = b; objs.append(h); last = h
        if barrel:
            br = to_object(name + "_Barrel", barrel, origin=tuple(bpivot)); br.parent = b; br.matrix_parent_inverse = b.matrix_world.inverted()  # flat under Base (FBX bake breaks depth-3); Unity re-nests
            objs.append(br); last = br
    if muzzle:
        m = bpy.data.objects.new(name + "_Muzzle", None); m.empty_display_size = .1
        bpy.context.scene.collection.objects.link(m); m.location = muzzle
        m.parent = b; m.matrix_parent_inverse = b.matrix_world.inverted(); objs.append(m)
    return objs

def cannon():
    base = plinth(1, 1) + [box((.58, .58, .3), (0, 0, .31), "MechWhite", bevel=.07, seg=2)]
    for (x, y) in ((-.29, -.15), (-.29, .15), (.29, -.15), (.29, .15)):
        base.append(uv_sphere(.04, 8, 5, (x * 1.02, y, .34), (.35, 1, 1), "Indigo"))
    head = [uv_sphere(.27, 14, 8, (0, 0, .62), (1, 1, .82), "MechWhite"), cyl(.2, .2, .08, 12, (0, 0, .52), "Indigo")]
    barrel = [cyl(.11, .12, .5, 12, (0, -.38, .64), "Indigo", rot=(math.radians(90), 0, 0), smooth=True),
              cyl(.15, .15, .1, 12, (0, -.62, .64), "OutlineIndigo", rot=(math.radians(90), 0, 0), bevel=.015),
              cyl(.125, .125, .06, 12, (0, -.2, .64), "SlotHE", rot=(math.radians(90), 0, 0))]
    for side in (-1, 1): head.append(box((.08, .3, .14), (side * .27, 0, .62), "MechWhite", bevel=.025))
    return turret("SM_Tower_Cannon_1x1_01", base, head, (0, 0, .46), barrel, (0, -.15, .64), (0, -.68, .64))

def gatling():
    base = plinth(1, 1) + [box((.5, .5, .26), (0, 0, .29), "MechWhite", bevel=.06, seg=2)]
    head = [box((.42, .42, .26), (0, .02, .58), "MechWhite", bevel=.06, seg=2),
            cyl(.15, .15, .1, 10, (0, -.22, .58), "SlotKinetic", rot=(math.radians(90), 0, 0)),
            cyl(.16, .16, .05, 10, (0, -.5, .58), "Indigo", rot=(math.radians(90), 0, 0))]
    for k in range(6):
        a = k * math.tau / 6
        head.append(cyl(.035, .035, .42, 6, (math.cos(a) * .09, -.42, .58 + math.sin(a) * .09), "Metal", rot=(math.radians(90), 0, 0)))
    head.append(box((.1, .16, .06), (.12, .1, .74), "AllyYellow", bevel=.015))
    return turret("SM_Tower_Gatling_1x1_01", base, head, (0, 0, .46), muzzle=(0, -.66, .58))

def tesla():  # static base; Head = coil stack (may spin about Y)
    base = plinth(1, 1) + [cyl(.3, .24, .22, 10, (0, 0, .27), "MechWhite", bevel=.02)]
    head = []; z = .38
    for k in range(4):
        head.append(cyl(.17 - k * .02, .17 - k * .02, .06, 10, (0, 0, z), "Metal" if k % 2 else "Indigo")); z += .06
        head.append(cyl(.1, .1, .07, 8, (0, 0, z), "MechWhite")); z += .07
    head.append(cyl(.03, .03, .2, 6, (0, 0, z + .08), "Metal"))
    head.append(uv_sphere(.14, 12, 8, (0, 0, z + .26), (1, 1, 1), "SlotElec"))
    head.append(cyl(.2, .2, .03, 12, (0, 0, z + .26), "Metal"))
    return turret("SM_Tower_Tesla_1x1_01", base, head, (0, 0, .38), muzzle=(0, 0, z + .26))

def frost():
    base = plinth(1, 1) + [cyl(.32, .26, .24, 6, (0, 0, .28), "MechWhite", bevel=.02)]
    head = [cyl(.22, .22, .06, 6, (0, 0, .43), "Indigo"),
            cyl(.13, 0.0, .5, 6, (0, 0, .71), "SlotIce"), cyl(.13, .13, .12, 6, (0, 0, .4 + .12), "SlotIce"),
            cyl(.06, 0.0, .26, 6, (.17, -.06, .6), "SlotIce", rot=(0, math.radians(25), 0)),
            cyl(.06, 0.0, .24, 6, (-.16, .05, .58), "SlotIce", rot=(0, math.radians(-28), 0)),
            box((.1, .3, .08), (0, -.24, .48), "MechWhite", bevel=.02)]
    return turret("SM_Tower_Frost_1x1_01", base, head, (0, 0, .42), muzzle=(0, -.4, .48))

def flamer():  # 1x2 footprint, long axis = Blender Y
    base = plinth(1, 2) + [box((.7, 1.5, .24), (0, 0, .28), "MechWhite", bevel=.07, seg=2)]
    for side in (-1, 1):  # fuel tanks at the back
        base.append(cyl(.14, .14, .7, 10, (side * .2, .45, .55), "SlotFire", rot=(math.radians(90), 0, 0), smooth=True))
        base.append(cyl(.15, .15, .05, 10, (side * .2, .45, .55), "Indigo", rot=(math.radians(90), 0, 0)))
    head = [box((.42, .5, .26), (0, -.3, .56), "MechWhite", bevel=.06, seg=2),
            cyl(.08, .2, .34, 10, (0, -.7, .58), "Indigo", rot=(math.radians(90), 0, 0)),
            cyl(.15, .15, .04, 10, (0, -.86, .58), "SlotFire", rot=(math.radians(90), 0, 0)),
            cyl(.04, .04, .3, 6, (0, -.1, .6), "Metal", rot=(math.radians(90), 0, 0))]
    return turret("SM_Tower_Flamer_1x2_01", base, head, (0, -.3, .44), muzzle=(0, -.9, .58))

def mortar():  # 2x2
    base = plinth(2, 2, .18) + [cyl(.75, .7, .26, 12, (0, 0, .31), "MechWhite", bevel=.04)]
    for k in range(8):
        a = k * math.tau / 8
        base.append(uv_sphere(.05, 8, 5, (math.cos(a) * .73, math.sin(a) * .73, .3), (1, 1, 1), "AllyYellow"))
    head = [uv_sphere(.55, 16, 8, (0, 0, .44), (1, 1, .6), "MechWhite", hemi=True), cyl(.58, .58, .06, 16, (0, 0, .46), "Indigo")]
    br = [cyl(.26, .3, .7, 14, (0, 0, .35), "Indigo", smooth=True), cyl(.3, .3, .1, 14, (0, 0, .7), "OutlineIndigo"),
          cyl(.27, .27, .08, 14, (0, 0, .3), "SlotHE")]
    for bm_ in br:  # barrel leans FORWARD (-Y = Unity +Z) 28 deg
        bm_.transform(Matrix.Translation((0, .1, .6)) @ Matrix.Rotation(math.radians(28), 4, 'X'))
    for side in (-1, 1): head.append(box((.14, .34, .3), (side * .34, .05, .72), "MechWhite", bevel=.03))
    tip = (0, .1 - .78 * math.sin(math.radians(28)), .6 + .78 * math.cos(math.radians(28)))
    return turret("SM_Tower_Mortar_2x2_01", base, head, (0, 0, .44), br, (0, .1, .6), tip)

# ---------------------------------------------------------------- enemies: skeletal rigs + SS_Move / SS_Hit / SS_Death
FPS = 30

def eyes(y, z, dx, r, s=(1, .5, 1.2)):
    return [uv_sphere(r, 8, 6, (side * dx, y, z), s, "EnemyEye") for side in (-1, 1)]

def rigged_enemy(name, parts, bones, clips):
    """parts: [(bmesh, bone)], bones: {bone: (head, tail, parent)}, clips: {clip: (frames, loop, fn(frame)->{bone: (loc, rotDeg, scale)})}.
    One skinned mesh (rigid weights per part), armature root at the ground pivot, clips stored as NLA strips -> FBX takes."""
    arm_d = bpy.data.armatures.new(name + "_Rig")
    arm = bpy.data.objects.new(name + "_Armature", arm_d)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.view_layer.objects: o.select_set(False)
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for bn, (h, t, par) in bones.items():
        e = arm_d.edit_bones.new(bn); e.head = h; e.tail = t; e.roll = 0
        if par: e.parent = eb[par]
        eb[bn] = e
    bpy.ops.object.mode_set(mode='OBJECT')
    # mesh with one vertex group per bone
    me = bpy.data.meshes.new(name)
    bm = bmesh.new(); groups = []
    for (p, bn) in parts:
        n0 = len(bm.verts)
        tmp = bpy.data.meshes.new("tmp"); p.to_mesh(tmp); p.free(); bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
        groups.append((bn, n0, len(bm.verts)))
    bm.to_mesh(me); bm.free()
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        u = pal_uv(poly.material_index)
        for li in poly.loop_indices: uv.data[li].uv = u
    me.materials.append(MAT)
    for poly in me.polygons: poly.material_index = 0
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    vg = {}
    for (bn, a0, a1) in groups:
        if bn not in vg: vg[bn] = ob.vertex_groups.new(name=bn)
        vg[bn].add(list(range(a0, a1)), 1.0, 'REPLACE')
    ob.parent = arm
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
    # clips
    arm.animation_data_create()
    ad = arm.animation_data
    actions = {}
    for clip, (frames, loop, fn) in clips.items():
        act = bpy.data.actions.new(f"{name}|{clip}"); act.use_fake_user = True
        ad.action = act
        for f in range(0, frames + 1, 2 if frames > 6 else 1):
            pose = fn(f / frames)
            for bn in bones:
                pb = arm.pose.bones[bn]; pb.rotation_mode = 'XYZ'
                loc, rot, sc = pose.get(bn, ((0, 0, 0), (0, 0, 0), (1, 1, 1)))
                pb.location = loc; pb.rotation_euler = [math.radians(r) for r in rot]; pb.scale = sc
                pb.keyframe_insert("location", frame=f); pb.keyframe_insert("rotation_euler", frame=f); pb.keyframe_insert("scale", frame=f)
        actions[clip] = act
        ad.action = None
        tr = ad.nla_tracks.new(); tr.name = clip
        st = tr.strips.new(clip, 0, act); st.name = clip
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    arm["ss_actions"] = ",".join(actions.keys())
    RIGS[name] = (arm, actions)
    return [arm, ob]

RIGS = {}
S = math.sin; PI2 = math.tau

def walk_clips(step=25, bob=.04, roll=6, hit_back=-18, extra=None):
    """Generic biped/blob clips; bone local axes: X = pitch (forward/back), Z = side roll, Y = along bone (up)."""
    extra = extra or (lambda t, clip: {})
    def move(t):
        a = S(t * PI2)
        d = {"legL": ((0, 0, 0), (step * a, 0, 0), (1, 1, 1)), "legR": ((0, 0, 0), (-step * a, 0, 0), (1, 1, 1)),
             "body": ((0, abs(S(t * PI2)) * bob, 0), (3, 0, roll * S(t * PI2)), (1, 1 - .05 * abs(a), 1)),
             "head": ((0, 0, 0), (-2 * S(t * PI2 * 2), 0, -roll * .5 * S(t * PI2)), (1, 1, 1))}
        d.update(extra(t, "move")); return d
    def hit(t):
        k = S(min(t * 2, 1) * math.pi)
        d = {"body": ((0, 0, 0), (hit_back * k, 0, 0), (1 + .12 * k, 1 - .15 * k, 1 + .12 * k)),
             "head": ((0, 0, 0), (-10 * k, 0, 8 * S(t * PI2 * 2)), (1, 1, 1))}
        d.update(extra(t, "hit")); return d
    def death(t):
        e = min(1, t * 1.4)
        d = {"body": ((0, -.25 * e * e, 0), (-75 * e, 0, 12 * e), (1, 1 - .3 * e, 1)),
             "head": ((0, 0, 0), (-25 * e, 0, 0), (1, 1, 1)),
             "legL": ((0, 0, 0), (30 * e, 0, 0), (1, 1, 1)), "legR": ((0, 0, 0), (-20 * e, 0, 0), (1, 1, 1))}
        d.update(extra(t, "death")); return d
    return lambda fr: {"SS_Move": (fr, True, move), "SS_Hit": (8, False, hit), "SS_Death": (24, False, death)}

def biped_bones(leg_x, hip_z, neck_z, top_z, extras=None):
    b = {"root": ((0, 0, 0), (0, 0, .1), None),
         "body": ((0, 0, hip_z), (0, 0, neck_z), "root"),
         "head": ((0, 0, neck_z), (0, 0, top_z), "body"),
         "legL": ((-leg_x, 0, hip_z), (-leg_x, 0, 0), "root"), "legR": ((leg_x, 0, hip_z), (leg_x, 0, 0), "root")}
    b.update(extras or {}); return b

def drifter():  # Normal, ~1.0 m round blob bot
    P = [(box((.14, .2, .08), (-.14, -.02, .04), "EnemyDark", bevel=.025), "legL"), (box((.14, .2, .08), (.14, -.02, .04), "EnemyDark", bevel=.025), "legR"),
         (uv_sphere(.36, 14, 10, (0, 0, .46), (1, .95, .92), "EnemyRed"), "body"), (uv_sphere(.22, 12, 8, (0, -.22, .4), (1, .5, 1), "EnemyWhite"), "body"),
         (cyl(.02, .02, .18, 6, (0, 0, .88), "EnemyDark"), "head"), (uv_sphere(.05, 8, 6, (0, 0, .98), (1, 1, 1), "EnemyEye"), "head")]
    P += [(e, "head") for e in eyes(-.31, .55, .12, .06)]
    P += [(uv_sphere(.08, 8, 6, (side * .36, -.02, .4), (1, 1, 1), "EnemyDark"), "armL" if side < 0 else "armR") for side in (-1, 1)]
    bones = biped_bones(.14, .12, .5, 1.0, {"armL": ((-.3, 0, .45), (-.4, 0, .35), "body"), "armR": ((.3, 0, .45), (.4, 0, .35), "body")})
    ex = lambda t, c: {"armL": ((0, 0, 0), (30 * S(t * PI2), 0, 0), (1, 1, 1)), "armR": ((0, 0, 0), (-30 * S(t * PI2), 0, 0), (1, 1, 1))} if c == "move" else {}
    return rigged_enemy("SM_Enemy_Drifter_01", P, bones, walk_clips(28, .05, 7, extra=ex)(24))

def skimmer():  # Fast, ~0.7 m runner with ears/tail
    P = [(box((.08, .2, .06), (-.1, 0, .03), "EnemyDark", bevel=.02), "legL"), (box((.08, .2, .06), (.1, 0, .03), "EnemyDark", bevel=.02), "legR"),
         (uv_sphere(.25, 12, 8, (0, .02, .36), (.8, 1.3, .8), "EnemyRed"), "body"),
         (cyl(.0, .14, .32, 4, (0, -.36, .38), "EnemyWhite", rot=(math.radians(-90), 0, 0)), "head"),
         (cyl(.04, 0, .3, 4, (0, .3, .58), "EnemyDark", rot=(math.radians(60), 0, 0)), "tail")]
    P += [(e, "head") for e in eyes(-.25, .42, .08, .045)]
    P += [(cyl(.0, .1, .22, 3, (side * .2, .12, .5), "EnemyWhite", rot=(0, side * math.radians(55), 0)), "body") for side in (-1, 1)]
    bones = biped_bones(.1, .1, .36, .6, {"tail": ((0, .25, .45), (0, .42, .7), "body")})
    ex = lambda t, c: {"tail": ((0, 0, 0), (0, 0, 25 * S(t * PI2 * 2)), (1, 1, 1))} if c == "move" else {}
    return rigged_enemy("SM_Enemy_Skimmer_01", P, bones, walk_clips(40, .04, 5, extra=ex)(16))

def bulwark():  # Tank, ~1.1 m armoured tracked mech (tracks = legs)
    P = []
    for side, bn in ((-1, "legL"), (1, "legR")):
        P.append((box((.24, .9, .26), (side * .36, 0, .13), "EnemyDark", bevel=.05, seg=2), bn))
        for k in range(3): P.append((cyl(.1, .1, .26, 8, (side * .36, -.3 + k * .3, .12), "Metal", rot=(0, math.radians(90), 0)), bn))
    P += [(box((.72, .74, .5), (0, .02, .55), "EnemyRed", bevel=.08, seg=2), "body"), (box((.8, .1, .56), (0, -.4, .55), "EnemyWhite", bevel=.04), "body"),
          (box((.12, .03, .3), (0, -.46, .55), "EnemyRed", bevel=.01), "body"), (box((.5, .5, .32), (0, .05, .95), "EnemyRed", bevel=.08, seg=2), "head"),
          (box((.3, .06, .08), (0, -.2, 1.0), "EnemyEye", bevel=.01), "head")]
    P += [(box((.22, .46, .2), (side * .44, .05, .78), "EnemyWhite", bevel=.05), "body") for side in (-1, 1)]
    bones = biped_bones(.36, .28, .8, 1.12)
    return rigged_enemy("SM_Enemy_Bulwark_01", P, bones, walk_clips(4, .025, 2, hit_back=-8)(32))

def splitter():  # Splitter: twin lobes on separate bones so the death clip tears them apart
    P = [(box((.15, .2, .08), (-.2, -.02, .04), "EnemyDark", bevel=.025), "legL"), (box((.15, .2, .08), (.2, -.02, .04), "EnemyDark", bevel=.025), "legR"),
         (uv_sphere(.26, 12, 8, (-.14, 0, .44), (1, .95, 1), "EnemyRed"), "lobeL"), (uv_sphere(.26, 12, 8, (.14, 0, .48), (1, .95, 1), "EnemyRed"), "lobeR"),
         (box((.04, .42, .5), (0, 0, .46), "EnemyEye", bevel=.01), "body")]
    P += [(e, "head") for e in eyes(-.23, .52, .14, .05)]
    P += [(cyl(0, .08, .2, 4, (side * .2, 0, .78), "EnemyDark", rot=(0, side * math.radians(-25), 0)), "lobeL" if side < 0 else "lobeR") for side in (-1, 1)]
    bones = biped_bones(.2, .12, .55, .8, {"lobeL": ((-.14, 0, .2), (-.14, 0, .7), "body"), "lobeR": ((.14, 0, .2), (.14, 0, .7), "body")})
    def ex(t, c):
        if c == "death":
            e = min(1, t * 1.6)
            return {"lobeL": ((0, 0, 0), (0, 0, 40 * e), (1, 1, 1)), "lobeR": ((0, 0, 0), (0, 0, -40 * e), (1, 1, 1)), "body": ((0, 0, 0), (0, 0, 0), (1, 1 - .5 * e, 1))}
        if c == "move": return {"lobeL": ((0, 0, 0), (0, 0, 6 * S(t * PI2)), (1, 1, 1)), "lobeR": ((0, 0, 0), (0, 0, -6 * S(t * PI2)), (1, 1, 1))}
        return {}
    return rigged_enemy("SM_Enemy_Splitter_01", P, bones, walk_clips(26, .05, 9, extra=ex)(20))

def flyer():  # hover drone: sway + wing flap
    P = [(cyl(.16, .16, .02, 10, (0, 0, .01), "EnemyDark"), "root"),
         (uv_sphere(.26, 14, 8, (0, 0, .8), (1, 1, .85), "EnemyRed"), "body"), (uv_sphere(.16, 10, 6, (0, -.18, .76), (1, .5, .9), "EnemyWhite"), "body"),
         (cyl(.08, .0, .2, 8, (0, 0, .52), "SlotHE", rot=(math.radians(180), 0, 0)), "body"),
         (cyl(.3, .3, .05, 16, (0, 0, 1.02), "EnemyDark"), "head"), (cyl(.25, .25, .06, 16, (0, 0, 1.02), "EnemyWhite"), "head")]
    P += [(e, "body") for e in eyes(-.24, .82, .08, .05)]
    for side, bn in ((-1, "wingL"), (1, "wingR")):
        P.append((box((.42, .2, .03), (side * .45, .02, .82), "EnemyWhite", bevel=.01, rot=(0, side * math.radians(-10), 0)), bn))
        P.append((box((.3, .08, .035), (side * .42, -.07, .83), "EnemyRed", bevel=.008), bn))
    bones = {"root": ((0, 0, 0), (0, 0, .1), None), "body": ((0, 0, .55), (0, 0, .95), "root"), "head": ((0, 0, .95), (0, 0, 1.1), "body"),
             "wingL": ((-.24, 0, .82), (-.66, 0, .82), "body"), "wingR": ((.24, 0, .82), (.66, 0, .82), "body")}
    def move(t):
        f = S(t * PI2 * 4) * 35
        return {"body": ((0, .08 * S(t * PI2), 0), (6 * S(t * PI2), 0, 5 * S(t * PI2 + 1)), (1, 1, 1)), "head": ((0, 0, 0), (0, 360 * t, 0), (1, 1, 1)),
                "wingL": ((0, 0, 0), (f, 0, 0), (1, 1, 1)), "wingR": ((0, 0, 0), (-f, 0, 0), (1, 1, 1))}
    def hit(t):
        k = S(min(t * 2, 1) * math.pi)
        return {"body": ((0, -.08 * k, 0), (-20 * k, 0, 10 * k), (1.1, .9, 1.1)), "wingL": ((0, 0, 0), (40 * k, 0, 0), (1, 1, 1)), "wingR": ((0, 0, 0), (-40 * k, 0, 0), (1, 1, 1))}
    def death(t):
        e = min(1, t * 1.3)
        return {"body": ((0, -.6 * e * e, 0), (-50 * e, 0, 120 * e), (1, 1, 1)), "wingL": ((0, 0, 0), (-60 * e, 0, 0), (1, 1, 1)), "wingR": ((0, 0, 0), (60 * e, 0, 0), (1, 1, 1))}
    return rigged_enemy("SM_Enemy_Flyer_01", P, bones, {"SS_Move": (36, True, move), "SS_Hit": (8, False, hit), "SS_Death": (24, False, death)})

def boss():  # 2x2, ~2.6 m red/white mech: legs, arms, head
    P = []
    for side, bn in ((-1, "legL"), (1, "legR")):
        P += [(box((.42, .6, .3), (side * .45, -.05, .15), "EnemyDark", bevel=.06, seg=2), bn), (cyl(.17, .15, .5, 8, (side * .45, 0, .5), "Metal"), bn)]
    P += [(box((1.3, 1.0, .9), (0, 0, 1.2), "EnemyRed", bevel=.12, seg=2), "body"), (box((1.0, .1, .5), (0, -.52, 1.15), "EnemyWhite", bevel=.04), "body"),
          (box((.75, .7, .55), (0, -.05, 1.9), "EnemyRed", bevel=.1, seg=2), "head"), (box((.55, .06, .1), (0, -.42, 1.95), "EnemyEye", bevel=.02), "head")]
    for side, bn in ((-1, "armL"), (1, "armR")):
        P.append((cyl(.0, .1, .45, 4, (side * .3, 0, 2.35), "EnemyWhite", rot=(0, side * math.radians(-20), 0)), "head"))
        P += [(uv_sphere(.3, 10, 8, (side * .82, 0, 1.45), (1, 1, 1), "EnemyWhite"), bn), (box((.3, .34, .6), (side * .85, -.05, .95), "EnemyDark", bevel=.06), bn),
              (cyl(.16, .16, .35, 10, (side * .85, -.35, .7), "Metal", rot=(math.radians(90), 0, 0)), bn)]
    bones = biped_bones(.45, .75, 1.62, 2.4, {"armL": ((-.82, 0, 1.45), (-.85, 0, .7), "body"), "armR": ((.82, 0, 1.45), (.85, 0, .7), "body")})
    ex = lambda t, c: {"armL": ((0, 0, 0), (-20 * S(t * PI2), 0, 0), (1, 1, 1)), "armR": ((0, 0, 0), (20 * S(t * PI2), 0, 0), (1, 1, 1))} if c == "move" else (
        {"armL": ((0, 0, 0), (0, 0, -30 * min(1, t * 1.4)), (1, 1, 1)), "armR": ((0, 0, 0), (0, 0, 30 * min(1, t * 1.4)), (1, 1, 1))} if c == "death" else {})
    return rigged_enemy("SM_Enemy_Boss_01", P, bones, walk_clips(18, .06, 3, hit_back=-8, extra=ex)(40))

def shard():  # swarm: static mesh, animated by the vertex-wobble shader (no bones for performance)
    parts = []
    for side in (-1, 1):
        for k in (-1, 1):
            parts.append(cyl(.025, .02, .18, 4, (side * .13, k * .08, .07), "EnemyDark", rot=(k * math.radians(20), side * math.radians(40), 0)))
    parts += [uv_sphere(.17, 10, 8, (0, .03, .24), (1, 1.2, .85), "EnemyRed"), uv_sphere(.1, 8, 6, (0, -.17, .25), (1, 1, 1), "EnemyDark")]
    parts += eyes(-.25, .28, .05, .035)
    return [to_object("SM_Enemy_Shard_01", parts)]

# ---------------------------------------------------------------- level environment pieces
def tile_c():
    bm = box((.98, .98, .25), (0, 0, .125), "GroundLavSide")
    top_edges = [e for e in bm.edges if all(v.co.z > .24 for v in e.verts)]
    bmesh.ops.bevel(bm, geom=top_edges, offset=.04, offset_type='OFFSET', segments=1, profile=.5, affect='EDGES', clamp_overlap=True)
    for f in bm.faces:
        if f.normal.z > .5: f.material_index = PIDX["GroundLavTop2"]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -.9], context='FACES_ONLY')
    rough_tile(bm, .25); parts = [bm, box((.2, .14, .02), (-.22, .25, .255), "GroundLavTop", bevel=.006)]; tile_detail(parts, .255)
    return [to_object("SM_Env_Tile_Stone_C_01", parts)]

def tile_dirt():
    bm = box((1.0, 1.0, .22), (0, 0, .11), "DirtDark")
    for f in bm.faces:
        if f.normal.z > .5: f.material_index = PIDX["Dirt"]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -.9], context='FACES_ONLY')
    rough_tile(bm, .22)
    parts = [bm]
    for k in range(4):  # pebbles
        parts.append(ico(.05, 1, (rng.uniform(-.35, .35), rng.uniform(-.35, .35), .23), (1.3, 1, .5), (0, 0, rng.random()), "DirtDark"))
    return [to_object("SM_Env_Tile_Dirt_01", parts)]

def island():
    """4x4 m island chunk: leafy top at z 0.55, 3 jittered rock strata down to -1.6 (pivot = centre at water level)."""
    parts = []
    def slab(w, z0, z1, top, side, jit):
        bm = bmesh.new()
        bmesh.ops.create_circle(bm, cap_ends=True, segments=26, radius=1.0)
        for v in bm.verts:
            a = math.atan2(v.co.y, v.co.x)
            sq = 1 / max(abs(math.cos(a)), abs(math.sin(a))) ** .75  # squircle
            r = sq * rng.uniform(1 - jit, 1 + jit) if v.co.length > .01 else 0
            v.co.x, v.co.y = math.cos(a) * r * w / 2, math.sin(a) * r * w / 2
        bm.faces.ensure_lookup_table(); f = bm.faces[0]
        ext = bmesh.ops.extrude_face_region(bm, geom=[f])
        top_verts = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
        for v in bm.verts: v.co.z = z0
        for v in top_verts: v.co.z = z1
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for fc in bm.faces:
            fc.material_index = PIDX[top] if fc.normal.z > .5 else (PIDX[side] if fc.normal.z > -.5 else PIDX["StoneDark"])
        layer = bm.loops.layers.float_color.new("Color")
        for fc in bm.faces:
            for l in fc.loops: l[layer] = (0, 1, rng.random(), 1)
        return bm
    # leafy/grassy top with uneven rim, then 5 jittered rock strata stepping in/out (spec: layered cliff, strata)
    top = slab(4.15, .3, .55, "GrassTop", "StrataC", .07)
    for v in top.verts:
        if v.co.z > .5: v.co.z += rng.uniform(-.05, .12) if v.co.xy.length > .3 else 0
    parts.append(top)
    global ISLAND_TOP
    ISLAND_TOP = [(v.co.x, v.co.y) for v in top.verts if v.co.z > .4]
    parts.append(cyl(4.8, 2.5, 2.4, 20, (0, 0, -1.65), "StrataB", cap=False))  # v9: sloped underwater skirt -> smooth depth gradient (no flat shelf polygon)
    for (w, z0, z1, c1, c2, j) in [(4.05, .02, .32, "StrataA", "StrataA", .08), (4.2, -.3, .04, "StrataB", "StrataC", .1),
                                    (3.85, -.7, -.28, "StrataC", "StrataA", .1), (3.95, -1.05, -.68, "StrataA", "StrataB", .12),
                                    (3.4, -1.6, -1.03, "StoneDark", "StoneDark", .12)]:
        parts.append(slab(w, z0, z1, c1, c2, j))
    for k in range(14):  # rock chunks sticking out of the strata
        a = rng.uniform(0, math.tau); z = rng.uniform(-1.1, .15)
        parts.append(ico(rng.uniform(.18, .36), 1, (math.cos(a) * 1.98, math.sin(a) * 1.98, z), (1.4, 1, .7), (0, 0, a), rng.choice(["StoneSide", "StrataC", "StrataB"]), jitter=.05))
    for k in range(16):  # grass / leaf mounds -> height variation on top
        a = rng.uniform(0, math.tau); r = rng.uniform(0, 1.75)
        parts.append(ico(rng.uniform(.3, .65), 1, (math.cos(a) * r, math.sin(a) * r, .55), (1.2, 1, .32), (0, 0, a),
                         rng.choice(["GrassTop", "Moss", "LeafOrange", "LeafRed", "GrassDry"]), jitter=.06))
    for k in range(40):  # leaf litter
        a = rng.uniform(0, math.tau); r = rng.uniform(.2, 1.95)
        parts.append(ico(rng.uniform(.05, .1), 1, (math.cos(a) * r, math.sin(a) * r, .62), (1.4, 1, .25), (0, 0, a),
                         rng.choice(["LeafGold", "LeafOrange", "LeafHighlight", "LeafRed"])))
    return [to_object("SM_Env_Island_Cliff_4x4_01", parts)]

ISLAND_TOP = []
def rect_poly(w, h, step, out_lo, out_hi):
    """Closed rectangle perimeter (CCW), sampled every ~step, each point pushed outward by U(out_lo, out_hi); corners pushed diagonally."""
    corners = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
    pts = []
    for i in range(4):
        (ax, ay), (bx, by) = corners[i], corners[(i + 1) % 4]
        L = math.hypot(bx - ax, by - ay); nx, ny = (by - ay) / L, -(bx - ax) / L  # outward normal for CCW
        n = max(1, int(round(L / step)))
        for k in range(n):
            t = k / n; o = rng.uniform(out_lo, out_hi); x, y = ax + (bx - ax) * t, ay + (by - ay) * t
            if k == 0: pts.append((x + math.copysign(o, x), y + math.copysign(o, y)))
            else: pts.append((x + nx * o, y + ny * o))
    return pts
def prism(poly, z0, z1, top, side):
    bm = bmesh.new(); vs = [bm.verts.new((x, y, z1)) for x, y in poly]; f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for g in ext["geom"]:
        if isinstance(g, bmesh.types.BMVert): g.co.z = z0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for fc in bm.faces: fc.material_index = PIDX[top] if fc.normal.z > .5 else (PIDX[side] if fc.normal.z > -.5 else PIDX["StoneDark"])
    layer = bm.loops.layers.float_color.new("Color")
    for fc in bm.faces:
        for l in fc.loops: l[layer] = (0, 1, rng.random(), 1)
    return bm
def frustum_rect(w0, h0, w1, h1, ztop, zbot, color):
    """Open sloped skirt (top rect -> wider bottom rect) so water depth grows smoothly with distance from shore."""
    bm = bmesh.new()
    def ring(w, h, z): return [bm.verts.new((x, y, z)) for x, y in [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]]
    a, b = ring(w0, h0, ztop), ring(w1, h1, zbot)
    for k in range(4): f = bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4])); f.material_index = PIDX[color]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.z < 0: f.normal_flip()
    return bm
BOARD_W, BOARD_H = 16.0, 12.0
BOARD_TOP = []
def board_cliff():
    """Base under the 16x12 tile grid: top outline always outside the grid (+0.12..0.3 m), jittered strata below, underwater shelf."""
    global BOARD_TOP
    parts = []
    BOARD_TOP = rect_poly(BOARD_W, BOARD_H, .5, .12, .3)
    parts.append(prism(BOARD_TOP, .2, .55, "GrassTop", "StrataC"))
    for (dw, z0, z1, lo, hi, c) in [(0, -.2, .22, -.02, .3, "StrataA"), (.2, -.6, -.18, -.1, .3, "StrataB"), (-.2, -1.0, -.58, -.15, .2, "StrataC"),
                                    (0, -1.4, -.98, -.2, .25, "StrataA"), (-.6, -1.8, -1.38, -.2, .1, "StoneDark")]:
        parts.append(prism(rect_poly(BOARD_W + dw, BOARD_H + dw, .6, lo, hi), z0, z1, c, c))
    for k in range(30):
        side = rng.randrange(4); t = rng.uniform(-.48, .48)
        x, y = [(t * BOARD_W, -BOARD_H / 2 - .15), (BOARD_W / 2 + .15, t * BOARD_H), (t * BOARD_W, BOARD_H / 2 + .15), (-BOARD_W / 2 - .15, t * BOARD_H)][side]
        parts.append(ico(rng.uniform(.2, .4), 1, (x, y, rng.uniform(-1.2, .1)), (1.4, 1, .7), (0, 0, rng.random() * 6), rng.choice(["StoneSide", "StrataC", "StrataB"]), jitter=.05))
    parts.append(frustum_rect(BOARD_W + .4, BOARD_H + .4, BOARD_W + 7, BOARD_H + 7, -.35, -2.9, "StrataB"))  # v9: sloped skirt
    return [to_object("SM_Env_Board_Cliff_16x12_01", parts)]

def bridge():  # 1 m segment, walk axis = X, deck top z 0.8
    parts = []
    for k in range(5):
        parts.append(box((.17, 1.1, .06), (-.4 + k * .2, 0, .77), "Wood" if k % 2 else "Trunk", bevel=.012, rot=(0, 0, rng.uniform(-.05, .05))))
    for side in (-1, 1):
        parts.append(box((1.0, .07, .07), (0, side * .5, .7), "Trunk", bevel=.01))
        parts.append(box((1.0, .06, .06), (0, side * .55, 1.15), "Wood", bevel=.01))
        parts.append(cyl(.06, .06, 2.0, 6, (-.45, side * .55, .2), "Trunk"))
    return [to_object("SM_Env_Bridge_Plank_01", parts)]

def dock_post():
    return [to_object("SM_Env_Dock_Post_01", [cyl(.09, .1, 1.6, 6, (0, 0, .2), "Trunk"), cyl(.1, .1, .08, 6, (0, 0, .5), "Wood"),
                                              cyl(.105, .105, .05, 6, (0, 0, .02), "Foam")])]

def brazier():
    parts = [cyl(.25, .3, .1, 8, (0, 0, .05), "StoneDark", bevel=.02), cyl(.08, .12, .45, 8, (0, 0, .32), "Metal"),
             cyl(.3, .18, .2, 8, (0, 0, .62), "Metal", bevel=.015), cyl(.26, .26, .03, 8, (0, 0, .71), "Trunk")]
    for k in range(5):
        a = k * math.tau / 5
        parts.append(cyl(.09, 0, rng.uniform(.25, .4), 5, (math.cos(a) * .12, math.sin(a) * .12, .88), "SlotFire"))
    parts.append(cyl(.1, 0, .5, 5, (0, 0, .95), "SlotHE"))
    return [to_object("SM_Prop_Brazier_01", parts)]

def campfire():
    parts = []
    for k in range(7):
        a = k * math.tau / 7
        parts.append(ico(.09, 1, (math.cos(a) * .32, math.sin(a) * .32, .05), (1.2, 1, .7), (0, 0, a), "StoneSide"))
    for k in range(3):
        parts.append(cyl(.05, .05, .5, 6, (0, 0, .08), "Trunk", rot=(math.radians(80), 0, k * 2.1)))
    for k in range(4):
        a = k * math.tau / 4
        parts.append(cyl(.08, 0, rng.uniform(.25, .4), 5, (math.cos(a) * .07, math.sin(a) * .07, .25), "SlotFire"))
    parts.append(cyl(.09, 0, .5, 5, (0, 0, .3), "SlotHE"))
    return [to_object("SM_Prop_Campfire_01", parts)]

def lantern():
    return [to_object("SM_Prop_Lantern_01", [cyl(.04, .05, 1.2, 6, (0, 0, .6), "Trunk"), box((.32, .05, .05), (.12, 0, 1.18), "Trunk"),
            box((.16, .16, .2), (.25, 0, 1.0), "SlotHE", bevel=.02), box((.2, .2, .04), (.25, 0, 1.12), "Trunk", bevel=.01),
            cyl(.02, .02, .08, 4, (.25, 0, 1.15), "Metal")])]

def rock_pile():
    parts = []
    for k in range(5):
        r = rng.uniform(.14, .3)
        bm = ico(r, 1, (rng.uniform(-.35, .35), rng.uniform(-.3, .3), r * .55), (1.2, 1, .75), (0, 0, rng.random() * 6), "StoneSide", jitter=.03)
        for f in bm.faces:
            if f.normal.z > .55: f.material_index = PIDX["StoneTopCool" if k % 2 else "StoneTopWarm"]
        parts.append(bm)
    return [to_object("SM_Env_RockPile_01", parts)]

def stump():
    return [to_object("SM_Env_Stump_01", [cyl(.22, .18, .3, 8, (0, 0, .15), "Trunk"), cyl(.17, .17, .02, 8, (0, 0, .305), "Wood"),
            cyl(.06, 0, .25, 4, (.2, 0, .05), "Trunk", rot=(0, math.radians(70), 0)), cyl(.05, 0, .2, 4, (-.15, .14, .05), "Trunk", rot=(math.radians(60), math.radians(-40), 0))])]

def log():
    return [to_object("SM_Env_Log_01", [cyl(.13, .13, 1.1, 8, (0, 0, .13), "Trunk", rot=(0, math.radians(90), 0)),
            cyl(.11, .11, .02, 8, (.555, 0, .13), "Wood", rot=(0, math.radians(90), 0)), cyl(.11, .11, .02, 8, (-.555, 0, .13), "Wood", rot=(0, math.radians(90), 0)),
            cyl(.05, 0, .25, 4, (.1, .1, .25), "Trunk", rot=(math.radians(-50), 0, 0))])]

def leaves():  # fallen leaf scatter, 1.5 m patch
    parts = []
    for k in range(26):
        p = (rng.uniform(-.75, .75), rng.uniform(-.75, .75), .015)
        parts.append(leaf_tuft(p, (rng.uniform(-.2, .2), rng.uniform(-.2, .2), 1), rng.uniform(.05, .08),
                               rng.choice(["LeafOrange", "LeafRed", "LeafGold"]), (0, 1, rng.random(), 1)))
    return [to_object("SM_Env_Leaves_01", parts)]

def foam_ring():  # 4x4 squircle band at water level, scale per island
    bm = bmesh.new(); outer, inner = [], []
    n = 32
    for k in range(n):
        a = k * math.tau / n
        sq = 1 / max(abs(math.cos(a)), abs(math.sin(a))) ** .75
        w = rng.uniform(.2, .45)
        outer.append(bm.verts.new((math.cos(a) * sq * (2.05 + w), math.sin(a) * sq * (2.05 + w), .02)))
        inner.append(bm.verts.new((math.cos(a) * sq * 2.0, math.sin(a) * sq * 2.0, .02)))
    for k in range(n):
        bm.faces.new((inner[k], outer[k], outer[(k + 1) % n], inner[(k + 1) % n]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.z < 0: f.normal_flip()
    return [to_object("SM_Env_FoamRing_01", [_finish(bm, "Foam")])]

def core():  # signal core the enemies walk to (2x2, ally colours, outlined)
    base = plinth(2, 2, .2) + [cyl(.8, .7, .3, 10, (0, 0, .35), "MechWhite", bevel=.04)]
    for k in range(6):
        a = k * math.tau / 6
        base.append(box((.16, .16, .7), (math.cos(a) * .62, math.sin(a) * .62, .7), "MechWhite", bevel=.03, rot=(0, 0, a)))
        base.append(box((.17, .17, .08), (math.cos(a) * .62, math.sin(a) * .62, 1.08), "AllyYellow", bevel=.02, rot=(0, 0, a)))
    base += [cyl(.18, .0, .9, 6, (0, 0, 1.4), "SlotIce"), cyl(.18, .18, .4, 6, (0, 0, .75), "SlotIce"), cyl(.32, .32, .08, 10, (0, 0, .55), "Indigo")]
    return [to_object("SM_Prop_Core_01", base)]

# ------------------------------------------------------------------ build
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1.0
pal_path = os.path.join(ART, "Textures", "T_Env_Palette_D.png")
write_palette_png(pal_path)
img = bpy.data.images.load(pal_path); img.name = "T_Env_Palette_D"

MAT = bpy.data.materials.new("M_Stylized_Palette")
MAT.use_nodes = True
nt = MAT.node_tree; nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")
tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = 'Closest'
diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
nt.links.new(tex.outputs[0], diff.inputs[0])
try:  # toon preview (EEVEE): ShaderToRGB -> constant ramp -> mix shadow colour (#6E5A8C) with base
    s2r = nt.nodes.new("ShaderNodeShaderToRGB")
    ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.color_ramp.interpolation = 'CONSTANT'
    ramp.color_ramp.elements[0].color = (0.16, 0.10, 0.27, 1); ramp.color_ramp.elements[1].position = .12
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    e = ramp.color_ramp.elements.new(.03); e.color = (.42, .34, .55, 1)
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
    mul.inputs[0].default_value = 1.0
    nt.links.new(diff.outputs[0], s2r.inputs[0]); nt.links.new(s2r.outputs[0], ramp.inputs[0])
    nt.links.new(tex.outputs[0], mul.inputs[6]); nt.links.new(ramp.outputs[0], mul.inputs[7])
    emi = nt.nodes.new("ShaderNodeEmission"); nt.links.new(mul.outputs[2], emi.inputs[0])
    nt.links.new(emi.outputs[0], out.inputs[0])
except Exception as ex:
    print("toon preview nodes failed, using diffuse:", ex); nt.links.new(diff.outputs[0], out.inputs[0])

OUTLINE = bpy.data.materials.new("M_Preview_Outline")
OUTLINE.use_nodes = True
OUTLINE.use_backface_culling = True
on = OUTLINE.node_tree; on.nodes.clear()
oo = on.nodes.new("ShaderNodeOutputMaterial"); oe = on.nodes.new("ShaderNodeEmission")
oe.inputs[0].default_value = (0.013, 0.010, 0.04, 1); on.links.new(oe.outputs[0], oo.inputs[0])

ASSETS = [
    ("Environment", "SM_Env_Tile_Stone_A_01", lambda: tile("A")), ("Environment", "SM_Env_Tile_Stone_B_01", lambda: tile("B")),
    ("Environment", "SM_Env_Tile_Stone_C_01", tile_c), ("Environment", "SM_Env_Tile_Stone_D_01", lambda: tile("D")), ("Environment", "SM_Env_Tile_Stone_E_01", lambda: tile("E")), ("Environment", "SM_Env_Tile_Dirt_01", tile_dirt),
    ("Environment", "SM_Env_Island_Cliff_4x4_01", island), ("Environment", "SM_Env_Board_Cliff_16x12_01", board_cliff), ("Environment", "SM_Env_Bridge_Plank_01", bridge),
    ("Environment", "SM_Env_Dock_Post_01", dock_post), ("Environment", "SM_Prop_Brazier_01", brazier),
    ("Environment", "SM_Prop_Campfire_01", campfire), ("Environment", "SM_Prop_Lantern_01", lantern),
    ("Environment", "SM_Env_RockPile_01", rock_pile), ("Environment", "SM_Env_Stump_01", stump), ("Environment", "SM_Env_Log_01", log),
    ("Environment", "SM_Env_Leaves_01", leaves), ("Environment", "SM_Env_FoamRing_01", foam_ring),
    ("Towers", "SM_Prop_Core_01", core), ("Enemies", "SM_Enemy_Splitter_01", splitter),
    ("Environment", "SM_Env_Rock_1x1_01", lambda: stone_block("1x1", False)),
    ("Environment", "SM_Env_Rock_TetrisI_01", lambda: stone_block("TetrisI", False)),
    ("Environment", "SM_Env_Rock_TetrisO_01", lambda: stone_block("TetrisO", True)),
    ("Environment", "SM_Env_Rock_TetrisT_01", lambda: stone_block("TetrisT", False)),
    ("Environment", "SM_Env_Rock_TetrisS_01", lambda: stone_block("TetrisS", True)),
    ("Environment", "SM_Env_Rock_TetrisZ_01", lambda: stone_block("TetrisZ", False)),
    ("Environment", "SM_Env_Rock_TetrisL_01", lambda: stone_block("TetrisL", True)),
    ("Environment", "SM_Env_Rock_TetrisJ_01", lambda: stone_block("TetrisJ", False)),
    ("Environment", "SM_Env_Tree_Maple_A_01", lambda: tree("A")), ("Environment", "SM_Env_Tree_Maple_B_01", lambda: tree("B")),
    ("Environment", "SM_Env_Tree_Maple_C_01", lambda: tree("C")),
    ("Environment", "SM_Env_Rock_Small_01", rock_small), ("Environment", "SM_Prop_Barrel_01", barrel),
    ("Environment", "SM_Prop_Crate_01", crate), ("Environment", "SM_Env_Grass_Tuft_01", grass_tuft),
    ("Towers", "SM_Tower_Cannon_1x1_01", cannon), ("Towers", "SM_Tower_Gatling_1x1_01", gatling),
    ("Towers", "SM_Tower_Tesla_1x1_01", tesla), ("Towers", "SM_Tower_Frost_1x1_01", frost),
    ("Towers", "SM_Tower_Flamer_1x2_01", flamer), ("Towers", "SM_Tower_Mortar_2x2_01", mortar),
    ("Enemies", "SM_Enemy_Drifter_01", drifter), ("Enemies", "SM_Enemy_Skimmer_01", skimmer),
    ("Enemies", "SM_Enemy_Bulwark_01", bulwark), ("Enemies", "SM_Enemy_Flyer_01", flyer),
    ("Enemies", "SM_Enemy_Shard_01", shard), ("Enemies", "SM_Enemy_Boss_01", boss),
]
manifest = {"generator": "ArtSource/Stylized/build_stylized_batch1.py", "blender": bpy.app.version_string, "assets": []}
built = {}
for folder, name, fn in ASSETS:
    rng.seed(zlib.crc32(name.encode()))
    objs = fn()
    coll = bpy.data.collections.new(name); scene.collection.children.link(coll)
    for o in objs:
        scene.collection.objects.unlink(o); coll.objects.link(o)
    built[name] = (folder, objs)
    mo = [o for o in objs if o.type == 'MESH']
    dims = [max(v.co[i] for o in mo for v in o.data.vertices) - min(v.co[i] for o in mo for v in o.data.vertices) for i in range(3)]
    manifest["assets"].append({"name": name, "folder": folder, "tris": tri_count(mo), "size_xyz_m": [round(d, 3) for d in dims],
                               "objects": [o.name for o in objs]})

def export(name):
    folder, objs = built[name]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(ART, folder, name + ".fbx")
    rig = name in RIGS
    if name.startswith("SM_Enemy_"):  # facing check: eyes/face must be on Blender -Y (= Unity +Z forward)
        eu = pal_uv(PIDX["EnemyEye"]); ys = []
        for o in objs:
            if o.type != 'MESH' or not o.data.uv_layers: continue
            uvl = o.data.uv_layers[0].data
            for poly in o.data.polygons:
                u = uvl[poly.loop_indices[0]].uv
                if abs(u[0] - eu[0]) < 1e-3 and abs(u[1] - eu[1]) < 1e-3: ys.append((o.matrix_world @ poly.center).y)
        fy = sum(ys) / len(ys) if ys else 0
        print("VALIDATE facing %s %s (eye y=%.3f, Unity forward +Z)" % (name, "OK" if fy <= .01 else "FAIL", fy))
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY', 'ARMATURE'},
                             primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=not rig, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             colors_type='LINEAR', add_leaf_bones=False, bake_anim=rig,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=rig, bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.0, path_mode='STRIP',
                             use_tspace=False)
    return path

for name in built:
    p = export(name); print("EXPORTED", p)

with open(os.path.join(SRC, "stylized_manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)

# ------------------------------------------------------------------ preview layout + render
def place(name, loc, rotz=0.0, scale=1.0, outline=0.0):
    folder, objs = built[name]
    root = objs[0]
    inst = root.copy(); inst.data = root.data.copy() if outline else root.data
    inst.location = Vector(loc) + root.location; inst.rotation_euler = (0, 0, rotz); inst.scale = (scale,) * 3
    pv.objects.link(inst)
    kids = []
    cmap = {root: inst}
    for c in objs[1:]:
        k = c.copy()
        if c.data is not None: k.data = c.data.copy() if outline else c.data
        k.parent = cmap.get(c.parent, inst); pv.objects.link(k); kids.append(k); cmap[c] = k
        for m in k.modifiers:
            if m.type == 'ARMATURE': m.object = inst
    if inst.type == 'ARMATURE' and inst.animation_data: inst.animation_data.use_nla = False
    if outline:
        for o in [inst] + kids:
            if o.type != 'MESH': continue
            if OUTLINE.name not in o.data.materials: o.data.materials.append(OUTLINE)
            m = o.modifiers.new("Outline", 'SOLIDIFY'); m.thickness = -outline; m.use_flip_normals = True
            m.material_offset = 1; m.use_rim = False
    return inst

TOWER_OUT, ENEMY_OUT = .02, .015
def outl(n): return TOWER_OUT if "Tower" in n else (ENEMY_OUT if "Enemy" in n else 0)

if DO_RENDER:
    pv = bpy.data.collections.new("Preview"); scene.collection.children.link(pv)
    for c in [c for c in scene.collection.children if c.name != "Preview"]:
        c.hide_render = True
    sun_d = bpy.data.lights.new("Sun", 'SUN'); sun_d.energy = 3.5; sun_d.color = (1, .91, .8); sun_d.angle = math.radians(3)
    sun = bpy.data.objects.new("Sun", sun_d); sun.rotation_euler = (math.radians(50), 0, math.radians(-40)); pv.objects.link(sun)
    world = bpy.data.worlds.new("World"); scene.world = world; world.use_nodes = True
    bg = world.node_tree.nodes.get("Background"); bg.inputs[0].default_value = (.35, .45, .75, 1); bg.inputs[1].default_value = .6
    cam_d = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_d); pv.objects.link(cam); scene.camera = cam
    wm = bpy.data.materials.new("Preview_Water"); wm.use_nodes = True
    wm.node_tree.nodes.clear(); wo = wm.node_tree.nodes.new("ShaderNodeOutputMaterial"); we = wm.node_tree.nodes.new("ShaderNodeEmission")
    we.inputs[0].default_value = (0.0024, 0.088, 0.35, 1); wm.node_tree.links.new(we.outputs[0], wo.inputs[0])
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0)); water = bpy.context.active_object; water.data.materials.append(wm)
    scene.collection.objects.unlink(water); pv.objects.link(water)
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try: scene.render.engine = eng; break
        except Exception: pass
    try: scene.view_settings.view_transform = 'Standard'
    except Exception: pass
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    def shoot(fname, loc, target, lens):
        cam.location = loc; cam_d.lens = lens
        d = Vector(target) - Vector(loc); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True); print("RENDERED", scene.render.filepath)
    def clear():
        for c in list(pv.objects): pv.objects.unlink(c)
        for o in (sun, cam, water): pv.objects.link(o)
    def lineup(names, fname, step, z=0.0, cam_loc=None, lens=38):
        clear(); x = -(len(names) - 1) * step / 2
        for n in names: place(n, (x, 0, z), outline=outl(n)); x += step
        w = len(names) * step
        shoot(fname, cam_loc or (-w * .25, -w * 1.0, w * .62), (0, 0, .5), lens)
    # ---- full level diorama; the same layout is written to level_layout.json for StylizedArtDemo.unity
    L = []
    def put(n, x, y, z, rz=0.0, sc=(1, 1, 1)):
        L.append({"asset": n, "pos": [round(x, 3), round(y, 3), round(z, 3)], "rotZ": round(math.degrees(rz), 2), "scale": list(sc)})
    lr = random.Random(7)
    # Core (Ember) 2x2 at board centre; 3 enemy paths enter from W / E / N edges (bridges from spawn islands) and wind to the core.
    def put(n, x, y, z, rz=0.0, sc=(1, 1, 1), rx=0.0, ry=0.0):
        d = {"asset": n, "pos": [round(x, 3), round(y, 3), round(z, 3)], "rotZ": round(math.degrees(rz), 2), "scale": list(sc)}
        if rx or ry: d["rotX"] = round(math.degrees(rx), 2); d["rotY"] = round(math.degrees(ry), 2)
        L.append(d)
    def poly_line(pts):
        out = []
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            n = int(abs(bx - ax) + abs(by - ay))
            for k in range(n): out.append((ax + (bx - ax) * k / n, ay + (by - ay) * k / n))
        out.append(pts[-1]); return out
    ROUTES = {"W": [(-7.5, 2.5), (-4.5, 2.5), (-4.5, -2.5), (-1.5, -2.5), (-1.5, -.5)],
              "E": [(7.5, -2.5), (4.5, -2.5), (4.5, 2.5), (1.5, 2.5), (1.5, .5)],
              "N": [(.5, 5.5), (.5, 4.5), (-2.5, 4.5), (-2.5, 1.5), (-1.5, 1.5), (-1.5, .5)]}
    PATHS = {k: poly_line(v) for k, v in ROUTES.items()}
    PATH = set(c for v in PATHS.values() for c in v)
    CORE = (0.0, 0.0); CORE_CELLS = {(-.5, -.5), (.5, -.5), (-.5, .5), (.5, .5)}
    put("SM_Env_Board_Cliff_16x12_01", 0, 0, 0)
    def inside(pt, poly):
        x, y = pt; c = False
        for (x1, y1), (x2, y2) in zip(poly, poly[1:] + poly[:1]):
            if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
        return c
    occ = set(PATH) | CORE_CELLS | {(CORE[0] + dx, CORE[1] + dy) for dx in (-1.5, 1.5) for dy in (-1.5, -.5, .5, 1.5)} | {(dx, dy) for dx in (-.5, .5) for dy in (-1.5, 1.5)}
    # walls first (tiles under walls stay perfectly flat so walls sit stably)
    def near_path(cells): return any(abs(cx - px) + abs(cy - py) <= 1 for (cx, cy) in cells for (px, py) in PATH)
    shapes = ["TetrisO", "TetrisT", "TetrisL", "TetrisI", "TetrisS", "TetrisJ", "TetrisZ", "TetrisO", "1x1"]
    walls = []; wr = random.Random(11); tries = 0; wallcells = set()
    while len(walls) < 22 and tries < 6000:
        tries += 1
        sh = shapes[len(walls) % len(shapes)]
        px, py = wr.randint(-8, 7) + .5, wr.randint(-6, 5) + .5
        if math.hypot(px, py) > 6.5 and wr.random() < .6: continue   # bias walls (and turrets) around the core
        cells = [(px + cx, py - cy) for (cx, cy) in TETRIS[sh]]
        if any(not (-8 < cx < 8 and -6 < cy < 6) for cx, cy in cells): continue
        if any(c in occ for c in cells) or not near_path(cells): continue
        occ |= set(cells); wallcells |= set(cells); walls.append((sh, px, py))
        put("SM_Env_Rock_%s_01" % sh, px, py, .8)
    tile_fail = 0
    for x in range(-8, 8):
        for y in range(-6, 6):
            c = (x + .5, y + .5)
            for corner in [(x, y), (x + 1, y), (x, y + 1), (x + 1, y + 1)]:
                if not inside(corner, BOARD_TOP): tile_fail += 1
            flat = c in wallcells or c in CORE_CELLS
            hsh = cell_hash(x, y)  # seeded per-cell hash: no clustering, same result every build
            mag = .03 + (hsh >> 4 & 255) / 255 * .03; dz = 0 if flat else (mag if hsh & 1 else -mag)  # +-0.03..0.06, mean 0 -> top 0.80
            tx = 0 if flat else math.radians(((hsh >> 12 & 255) / 255 - .5) * 3); ty = 0 if flat else math.radians(((hsh >> 20 & 255) / 255 - .5) * 3)
            rot = (hsh >> 8 & 3) * math.pi / 2
            if c in PATH: put("SM_Env_Tile_Dirt_01", c[0], c[1], .55 + dz * .5, rot, rx=tx * .5, ry=ty * .5)
            else: put("SM_Env_Tile_Stone_%s_01" % TILE_VARIANTS[(hsh >> 2) % len(TILE_VARIANTS)], c[0], c[1], .55 + dz, rot, rx=tx, ry=ty)
    assert tile_fail == 0, "tiles overhang board cliff: %d corners" % tile_fail
    print("VALIDATE tiles-on-terrain OK: 192 tiles, all corners inside board cliff top")
    T = 1.4; small = ["SM_Tower_Cannon_1x1_01", "SM_Tower_Gatling_1x1_01", "SM_Tower_Tesla_1x1_01", "SM_Tower_Frost_1x1_01"]
    walls.sort(key=lambda w: math.hypot(w[1], w[2]))
    for i, (sh, px, py) in enumerate(walls[:17]):  # closest walls to the core get turrets
        if sh == "TetrisO": n, x, y = "SM_Tower_Mortar_2x2_01", px + .5, py - .5
        elif sh == "TetrisI": n, x, y = "SM_Tower_Flamer_1x2_01", px, py - .5
        else: n, x, y = small[i % 4], px, py
        put(n, x, y, T, wr.uniform(0, math.tau))   # rotZ here = HEAD yaw (Unity keeps Base grid-aligned)
    put("SM_Prop_Core_01", CORE[0], CORE[1], .8)
    roster = ["SM_Enemy_Drifter_01", "SM_Enemy_Shard_01", "SM_Enemy_Skimmer_01", "SM_Enemy_Splitter_01", "SM_Enemy_Bulwark_01", "SM_Enemy_Flyer_01", "SM_Enemy_Drifter_01"]
    k = 0
    for route in PATHS.values():
        for i in range(0, len(route) - 2, 2):
            ex, ey = route[i]; nx, ny = route[i + 1]
            put(roster[k % len(roster)], ex + wr.uniform(-.12, .12), ey + wr.uniform(-.12, .12), .8, math.atan2(ny - ey, nx - ex) + math.pi / 2); k += 1
    # spawn islands + measured, validated bridges (board edge -> island edge, both ends on land)
    SPAWNS = {"W": (-12.6, 2.5, 1.0, .3), "E": (12.6, -2.5, 1.0, 2.0), "N": (.5, 10.6, 1.0, 1.1)}
    def island_poly(ix, iy, sc, rz):
        c, s_ = math.cos(rz), math.sin(rz)
        return [(ix + (x * c - y * s_) * sc * .96, iy + (x * s_ + y * c) * sc * .96) for (x, y) in ISLAND_TOP]
    bridge_ok = 0
    for key, (ix, iy, sc, rz) in SPAWNS.items():
        put("SM_Env_Island_Cliff_4x4_01", ix, iy, 0, rz, (sc, sc, 1))
        ipoly = island_poly(ix, iy, sc, rz)
        ex, ey = ROUTES[key][0]
        d = (ix - ex, iy - ey); dl = math.hypot(*d); d = (d[0] / dl, d[1] / dl)
        # board edge exit along d, then march to the island shore
        t = 0
        while inside((ex + d[0] * t, ey + d[1] * t), BOARD_TOP): t += .02
        start_t = t - .45
        while not inside((ex + d[0] * t, ey + d[1] * t), ipoly): t += .02
        end_t = t + .45
        p0 = (ex + d[0] * start_t, ey + d[1] * start_t); p1 = (ex + d[0] * end_t, ey + d[1] * end_t)
        assert inside(p0, BOARD_TOP) and inside(p1, ipoly), "bridge %s not grounded" % key
        Lb = end_t - start_t; n = max(1, math.ceil(Lb / 1.0)); seg = Lb / n; ang = math.atan2(d[1], d[0])
        for j in range(n):
            m = start_t + seg * (j + .5)
            put("SM_Env_Bridge_Plank_01", ex + d[0] * m, ey + d[1] * m, 0, ang, (seg, 1, 1))
        for side in (-1, 1):
            for tt in (start_t + .1, end_t - .1):
                put("SM_Env_Dock_Post_01", ex + d[0] * tt - d[1] * .62 * side, ey + d[1] * tt + d[0] * .62 * side, 0, lr.uniform(0, 6))
        put("SM_Prop_Lantern_01", ex + d[0] * (end_t + .3) - d[1] * .8, ey + d[1] * (end_t + .3) + d[0] * .8, .55, ang)
        print("VALIDATE bridge %s OK: len %.2f m, %d segments, ends on board and island" % (key, Lb, n)); bridge_ok += 1
        for q in range(2):  # trees on the far side of spawn islands
            a = ang + lr.uniform(-1.6, 1.6); r = lr.uniform(.6, 1.6)
            put("SM_Env_Tree_Maple_%s_01" % lr.choice("ABC"), ix + math.cos(a) * r, iy + math.sin(a) * r, .55, lr.uniform(0, 6), (lr.uniform(.8, 1.3),) * 3)
    put("SM_Enemy_Boss_01", SPAWNS["W"][0], SPAWNS["W"][1], .55, math.radians(90))
    put("SM_Prop_Campfire_01", SPAWNS["E"][0] + .6, SPAWNS["E"][1] - .9, .55)
    # surrounding islands with dense layered trees
    # v10 composition (Blender +x = screen left, +y = screen bottom/foreground):
    # TL/TR mid-distance small islands (maples, rocks, little dock), BL dark foreground trees + rocks, BR islet with stone ruin-lighthouse.
    def islet(ix, iy, sc, rz, ntrees, tscale=(.8, 1.2)):
        put("SM_Env_Island_Cliff_4x4_01", ix, iy, 0, rz, (sc, sc, 1))
        for k2 in range(ntrees):
            a = lr.uniform(0, math.tau); r = (1.0 - lr.random() ** 2) * 1.5 * sc
            put("SM_Env_Tree_Maple_%s_01" % lr.choice("ABC"), ix + math.cos(a) * r, iy + math.sin(a) * r, .55, lr.uniform(0, math.tau), (lr.uniform(*tscale),) * 3)
        put("SM_Env_RockPile_01", ix - 1.1 * sc, iy - .8 * sc, .55, lr.random() * 6)
    islet(10.2, -7.4, .75, .3, 3)                                     # TL
    put("SM_Env_Dock_Post_01", 9.0, -6.0, 0, 0); put("SM_Env_Bridge_Plank_01", 8.7, -6.3, .35, .6, (.55, 1, 1))
    islet(-10.0, -7.6, .7, 1.9, 3)                                    # TR
    put("SM_Env_Rock_Small_01", -8.6, -6.4, .0, 1.0, (2.2, 2.2, 2.2))
    islet(10.6, 8.0, 1.0, 2.2, 3, (1.4, 1.75))                        # BL foreground framing (big trees)
    put("SM_Env_RockPile_01", 9.0, 7.0, .1, 2.0, (1.6, 1.6, 1.6)); put("SM_Env_Rock_Small_01", 8.4, 8.6, 0, .4, (2.6, 2.6, 2.6))
    islet(-10.2, 7.6, .7, .9, 1)                                      # BR islet + ruin lighthouse (stacked stones + lantern)
    for k3, (dx, dy, z) in enumerate([(0, 0, .55), (.04, .03, 1.15), (-.03, .05, 1.75)]):
        put("SM_Env_Rock_1x1_01", -10.4 + dx, 7.3 + dy, z, .3 * k3, (.75 - .1 * k3, .75 - .1 * k3, 1))
    put("SM_Prop_Lantern_01", -10.4, 7.35, 2.35, 0, (1.6, 1.6, 1.6))
    put("SM_Env_Rock_TetrisI_01", -9.4, 8.4, .2, 1.2, (.6, .6, .6), rx=.35)  # toppled ruin wall
    for k4 in range(9):  # sparse floating leaves + small reefs on open water (kept off the board edges)
        while True:
            fx, fy = lr.uniform(-12, 12), lr.uniform(-9, 9)
            if abs(fx) > 9 or abs(fy) > 7: break
        put("SM_Env_Leaves_01" if k4 % 3 else "SM_Env_Rock_Small_01", fx, fy, .02 if k4 % 3 else -.05, lr.uniform(0, 6), (1, 1, 1))
    for x, y in [(-8.6, 5.6), (-8.7, -5.6), (8.6, 5.6), (8.7, -5.6), (-6.0, 6.7), (5.8, 6.7), (-5.5, -6.7), (3.5, -6.7)]:
        put("SM_Env_Tree_Maple_%s_01" % lr.choice("ABC"), x, y, .55, lr.uniform(0, 6), (lr.uniform(.9, 1.15),) * 3)
    put("SM_Prop_Brazier_01", 1.5, -1.5, .8); put("SM_Prop_Brazier_01", -1.5, 1.5 + 1, .8)
    with open(os.path.join(SRC, "level_layout.json"), "w") as f:
        json.dump({"note": "Blender coords (Z up). Unity: pos=(-x, z, -y), yaw=-rotZ, scale=(sx, sz, sy)", "items": L}, f, indent=1)
    clear()
    for it in L:
        n = it["asset"]; x, y, z = it["pos"]
        o = place(n, (x, y, z), math.radians(it["rotZ"]), 1.0, outline=outl(n))
        o.scale = (it["scale"][0], it["scale"][1], it["scale"][2])
    for k in range(260):  # snow flakes for the preview
        fl = ico(.03, 1, (lr.uniform(-16, 16), lr.uniform(-12, 12), lr.uniform(.5, 7)), color="Foam")
        ob = to_object("Snow_%d" % k, [fl]); scene.collection.objects.unlink(ob); pv.objects.link(ob)
    shoot("level_preview.png", (-9.0, -25.0, 21.0), (0, -.5, .5), 32)
    lineup(["SM_Tower_Gatling_1x1_01", "SM_Tower_Cannon_1x1_01", "SM_Tower_Tesla_1x1_01", "SM_Tower_Frost_1x1_01",
            "SM_Tower_Flamer_1x2_01", "SM_Tower_Mortar_2x2_01"], "turrets_lineup.png", 1.9)
    lineup(["SM_Enemy_Shard_01", "SM_Enemy_Skimmer_01", "SM_Enemy_Drifter_01", "SM_Enemy_Splitter_01", "SM_Enemy_Flyer_01", "SM_Enemy_Bulwark_01",
            "SM_Enemy_Boss_01"], "enemies_lineup.png", 1.9)
    lineup(["SM_Env_Rock_TetrisI_01", "SM_Env_Rock_TetrisO_01", "SM_Env_Rock_TetrisT_01", "SM_Env_Rock_TetrisS_01",
            "SM_Env_Rock_TetrisZ_01", "SM_Env_Rock_TetrisL_01", "SM_Env_Rock_TetrisJ_01", "SM_Env_Tile_Stone_A_01"], "blocks_lineup.png", 3.2)
    # close-up: per-cell weathered tetromino stones on rough ground tiles
    clear()
    for gx in range(-4, 4):
        for gy in range(-3, 3):
            place(lr_tile := random.choice(["SM_Env_Tile_Stone_A_01", "SM_Env_Tile_Stone_B_01", "SM_Env_Tile_Stone_C_01"]), (gx + .5, gy + .5, 0))
    for n, x, y in [("SM_Env_Rock_TetrisT_01", -2.5, 1.5), ("SM_Env_Rock_TetrisO_01", .5, .5), ("SM_Env_Rock_TetrisL_01", 2.5, 1.5), ("SM_Env_Rock_1x1_01", -1.5, -1.5), ("SM_Env_Rock_TetrisS_01", 1.5, -1.5)]:
        place(n, (x, y, .25))
    shoot("blocks_v8.png", (-2.5, -6.5, 4.2), (0, 0, .4), 42)
    # top-down tile variation (per-cell hash, same rule as the level)
    clear()
    for gx in range(-8, 8):
        for gy in range(-6, 6):
            hh = cell_hash(gx, gy)
            o = place("SM_Env_Tile_Stone_%s_01" % TILE_VARIANTS[(hh >> 2) % len(TILE_VARIANTS)], (gx + .5, gy + .5, 0), (hh >> 8 & 3) * math.pi / 2)
    shoot("tiles_topdown_v8.png", (0, -.3, 30), (0, 0, 0), 36)
    # enemy facing proof: blue arrow = Unity +Z (Blender -Y) in front of each enemy
    clear()
    ens = ["SM_Enemy_Drifter_01", "SM_Enemy_Skimmer_01", "SM_Enemy_Bulwark_01", "SM_Enemy_Splitter_01", "SM_Enemy_Shard_01", "SM_Enemy_Flyer_01", "SM_Enemy_Boss_01"]
    for k, n in enumerate(ens):
        x = (k - 3) * 2.4
        place(n, (x, 0, 0))
        ar = to_object("Arrow_%d" % k, [box((.07, 1.0, .04), (x, -1.2, .03), "WaterShallow"), box((.34, .14, .04), (x, -1.75, .03), "WaterShallow"), box((.18, .12, .04), (x, -1.88, .03), "WaterShallow")]); scene.collection.objects.unlink(ar); pv.objects.link(ar)
    shoot("enemy_facing_v8.png", (-3.0, -14.0, 7.0), (0, -.6, .6), 34)
    clear()
    for i, n in enumerate(["SM_Env_Tree_Maple_A_01", "SM_Env_Tree_Maple_B_01", "SM_Env_Tree_Maple_C_01"]):
        place(n, ((i - 1) * 2.4, 0, 0), rotz=i * .8)
    shoot("trees_v2.png", (-3.5, -8.5, 5.0), (0, 0, 1.7), 40)
    # ---- enemy animation contact sheet: rows = enemies, columns = Move x4, Hit x2, Death x3
    import numpy as np
    W, H = 240, 240
    scene.render.resolution_x, scene.render.resolution_y = W, H
    cols = [("SS_Move", t) for t in (0, .25, .5, .75)] + [("SS_Hit", t) for t in (.25, .5)] + [("SS_Death", t) for t in (.3, .6, 1.0)]
    names = list(RIGS.keys())
    sheet = np.zeros((H * len(names), W * len(cols), 4), dtype=np.float32)
    import tempfile; tmp = os.path.join(tempfile.gettempdir(), "ss_cell_%d.png" % os.getpid())  # v9: never collide with a locked preview file
    old_bg = tuple(bg.inputs[0].default_value); bg.inputs[0].default_value = (.80, .78, .86, 1)
    gm = bpy.data.materials.new("Sheet_Ground"); gm.use_nodes = True; gm.node_tree.nodes.clear()
    go_ = gm.node_tree.nodes.new("ShaderNodeOutputMaterial"); ge = gm.node_tree.nodes.new("ShaderNodeEmission")
    ge.inputs[0].default_value = (.62, .58, .70, 1); gm.node_tree.links.new(ge.outputs[0], go_.inputs[0])
    gmesh = bpy.data.meshes.new("SheetGround"); gmesh.from_pydata([(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [], [(0, 1, 2, 3)])
    gmesh.materials.append(gm); ground = bpy.data.objects.new("SheetGround", gmesh)
    for ri, n in enumerate(names):
        clear(); pv.objects.unlink(water); pv.objects.link(ground)
        inst = place(n, (0, 0, 0), math.radians(-30), 1.0, outline=ENEMY_OUT)
        acts = RIGS[n][1]
        h = 2.8 if "Boss" in n else 1.3
        for ci, (clip, t) in enumerate(cols):
            act = acts[clip]; ad = inst.animation_data; ad.use_nla = False; ad.action = act
            try:
                if hasattr(ad, "action_slot") and ad.action_slot is None and len(act.slots): ad.action_slot = act.slots[0]
            except Exception: pass
            fr = act.frame_range; scene.frame_set(int(round(fr[0] + (fr[1] - fr[0]) * t)))
            cam_d.lens = 50
            shoot(tmp, (-h * 1.6, -h * 2.6, h * 1.3), (0, 0, h * .4), 50)
            im = bpy.data.images.load(tmp); px = np.array(im.pixels[:], dtype=np.float32).reshape(H, W, 4)
            y0 = (len(names) - 1 - ri) * H
            sheet[y0:y0 + H, ci * W:(ci + 1) * W] = px
            bpy.data.images.remove(im)
    out = bpy.data.images.new("anim_sheet", W * len(cols), H * len(names), alpha=True)
    out.pixels = sheet.ravel(); out.file_format = 'PNG'
    dst = os.path.join(PREVIEW_DIR, "anim_contact_sheet.png")
    for attempt in range(5):
        try:
            if os.path.exists(dst): os.remove(dst)
            out.filepath_raw = dst; out.save(); break
        except Exception as ex:
            print("contact sheet save retry", attempt, ex); import time; time.sleep(1.5)
            out.filepath_raw = os.path.join(PREVIEW_DIR, "anim_contact_sheet_%d.png" % attempt)
    print("RENDERED anim_contact_sheet.png", names, [c[0] for c in cols])
    bg.inputs[0].default_value = old_bg
    if ground.name in pv.objects: pv.objects.unlink(ground)
    if water.name not in pv.objects: pv.objects.link(water)
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC, "StoneSignal_Stylized_Batch1.blend"))
print("STYLIZED BATCH1 DONE", json.dumps([(a["name"], a["tris"]) for a in manifest["assets"]]))
