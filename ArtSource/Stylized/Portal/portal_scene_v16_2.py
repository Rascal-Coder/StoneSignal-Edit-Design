import bpy, bmesh, math, random, sys, os
from mathutils import Vector, Euler
random.seed(5)
D=os.path.dirname(os.path.abspath(__file__)) if '__file__' in dir() else '/workspace/v16/p2'
D='/workspace/v16/p2'
argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
FRAME=argv[0] if argv else 'idle'   # idle | f0 | f1 | f2 | f3
bpy.ops.wm.read_factory_settings(use_empty=True)
sc=bpy.context.scene
try: sc.render.engine='BLENDER_EEVEE_NEXT'
except: sc.render.engine='BLENDER_EEVEE'
sc.render.resolution_x=720; sc.render.resolution_y=560; sc.render.film_transparent=False
sc.view_settings.view_transform='Standard'
w=bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True; w.node_tree.nodes['Background'].inputs[0].default_value=(.06,.16,.4,1); w.node_tree.nodes['Background'].inputs[1].default_value=.9
def lin(c): return tuple(v**2.2 for v in c[:3])+(1,)
def toon(name,col,shadow=None,emit=None,es=0):
    col=lin(col); shadow=lin(shadow) if shadow else None
    m=bpy.data.materials.new(name); m.use_nodes=True; nt=m.node_tree; n=nt.nodes; l=nt.links
    for x in list(n): n.remove(x)
    out=n.new('ShaderNodeOutputMaterial'); bs=n.new('ShaderNodeBsdfDiffuse'); s2r=n.new('ShaderNodeShaderToRGB'); ramp=n.new('ShaderNodeValToRGB')
    ramp.color_ramp.interpolation='CONSTANT'; ramp.color_ramp.elements[0].position=0; ramp.color_ramp.elements[1].position=.22
    sh=shadow or tuple(c*.55 for c in col[:3])+(1,)
    ramp.color_ramp.elements[0].color=(sh[0]*.9,sh[1]*.8,sh[2]*1.1,1); ramp.color_ramp.elements[1].color=col
    l.new(bs.outputs[0],s2r.inputs[0]); l.new(s2r.outputs[0],ramp.inputs[0])
    if emit:
        e=n.new('ShaderNodeEmission'); e.inputs[0].default_value=emit; e.inputs[1].default_value=es
        add=n.new('ShaderNodeAddShader'); em2=n.new('ShaderNodeEmission'); l.new(ramp.outputs[0],em2.inputs[0])
        l.new(em2.outputs[0],add.inputs[0]); l.new(e.outputs[0],add.inputs[1]); l.new(add.outputs[0],out.inputs[0])
    else:
        em2=n.new('ShaderNodeEmission'); l.new(ramp.outputs[0],em2.inputs[0]); l.new(em2.outputs[0],out.inputs[0])
    return m
def texmat(name,img,strength=1,crack=None,crack_k=0,blend=True):
    m=bpy.data.materials.new(name); m.use_nodes=True; nt=m.node_tree; n=nt.nodes; l=nt.links
    for x in list(n): n.remove(x)
    out=n.new('ShaderNodeOutputMaterial'); t=n.new('ShaderNodeTexImage'); t.image=bpy.data.images.load(D+'/'+img)
    e=n.new('ShaderNodeEmission'); tr=n.new('ShaderNodeBsdfTransparent'); mix=n.new('ShaderNodeMixShader')
    l.new(t.outputs[0],e.inputs[0]); e.inputs[1].default_value=strength
    if crack:
        c=n.new('ShaderNodeTexImage'); c.image=bpy.data.images.load(D+'/'+crack); c.image.colorspace_settings.name='Non-Color'
        mixc=n.new('ShaderNodeMix'); mixc.data_type='RGBA'; mixc.inputs['B'].default_value=(4.0,1.4,.3,1)
        mf=n.new('ShaderNodeMath'); mf.operation='MULTIPLY'; mf.inputs[1].default_value=crack_k
        l.new(c.outputs[0],mf.inputs[0]); l.new(mf.outputs[0],mixc.inputs['Factor']); l.new(t.outputs[0],mixc.inputs['A']); l.new(mixc.outputs['Result'],e.inputs[0])
        mx=n.new('ShaderNodeMath'); mx.operation='MAXIMUM'; l.new(t.outputs[1],mx.inputs[0]); l.new(mf.outputs[0],mx.inputs[1]); l.new(mx.outputs[0],mix.inputs[0])
    else: l.new(t.outputs[1],mix.inputs[0])
    l.new(tr.outputs[0],mix.inputs[1]); l.new(e.outputs[0],mix.inputs[2]); l.new(mix.outputs[0],out.inputs[0])
    m.blend_method='BLEND' if hasattr(m,'blend_method') else None
    try: m.surface_render_method='BLENDED'
    except: pass
    return m
# --- ground: low-poly tiles
gA=toon('Grass',(0.38,.68,.38,1)); gB=toon('Grass2',(0.34,.62,.35,1)); dirt=toon('Side',(0.45,.33,.25,1))
for i in range(-3,4):
    for j in range(-3,4):
        bpy.ops.mesh.primitive_cube_add(size=1,location=(i,j,-.25)); o=bpy.context.object; o.scale=(.985,.985,.5)
        bpy.ops.object.modifier_add(type='BEVEL'); o.modifiers[0].width=.04; o.modifiers[0].segments=1
        o.data.materials.append(gA if (i+j)%2 else gB)
# --- decal + rune quads
def quad(name,size,z,mat):
    bpy.ops.mesh.primitive_plane_add(size=size,location=(0,0,z)); o=bpy.context.object; o.name=name; o.data.materials.append(mat); return o
P={'idle':(0.0,.45,0,0),'f0':(1.0,1.6,0,0),'f1':(.75,1.3,.33,1),'f2':(.45,1.0,.66,2),'f3':(.2,.75,1.0,3)}[FRAME]
crackK,runeS,rise,fi=P
quad('Scorch',3.6,.005,texmat('M_Scorch','T_Portal_Scorch.png',1.0,'T_Portal_CrackMask.png',crackK))
quad('Rune',2.5,.012,texmat('M_Rune','T_Portal_RuneCircle.png',runeS))
# --- runestones (low-poly, merged into one mesh)
stone=toon('Stone',(.70,.68,.72,1),(.48,.45,.55,1),emit=(1.0,.45,.12,1),es=0)
pieces=[]
specs=[(1.55,40,1.0,.32,'pillar'),(1.6,150,.55,.36,'broken'),(1.5,235,.8,.3,'stone'),(1.65,310,.45,.4,'broken')]
for R,a,h,wd,kind in specs:
    x,y=R*math.cos(math.radians(a)),R*math.sin(math.radians(a))
    bpy.ops.mesh.primitive_cylinder_add(vertices=6 if kind!='stone' else 5,radius=wd,depth=h,location=(x,y,h/2))
    o=bpy.context.object; o.rotation_euler=(random.uniform(-.12,.12),random.uniform(-.12,.12),random.uniform(0,6.28))
    bm=bmesh.new(); bm.from_mesh(o.data)
    for v in bm.verts:
        v.co.x+=random.uniform(-.04,.04); v.co.y+=random.uniform(-.04,.04)
        if v.co.z>0: v.co.z+=random.uniform(-.12,.06) if kind=='broken' else random.uniform(-.03,.03); v.co.x*=.85; v.co.y*=.85
    bm.to_mesh(o.data); bm.free(); o.data.materials.append(stone); pieces.append(o)
    # glowing rune notch
    bpy.ops.mesh.primitive_cube_add(size=1,location=(x*.93,y*.93,h*.55)); g=bpy.context.object; g.scale=(.07,.07,.26); g.rotation_euler=(0,0,math.radians(a))
    gm=toon('Glow',(1,.6,.2,1),emit=(1,.5,.15,1),es=1.5+3*crackK); g.data.materials.append(gm); pieces.append(g)
bpy.ops.object.select_all(action='DESELECT')
for p in pieces: p.select_set(True)
bpy.context.view_layer.objects.active=pieces[0]; bpy.ops.object.join(); stones=bpy.context.object; stones.name='SM_Portal_Runestones'
# --- rubble chunks
rub=toon('Rubble',(.55,.5,.5,1))
if fi>0:
    for k in range(7):
        a=k*51+random.uniform(-10,10); r=random.uniform(.4,1.0); zt=[0,.35,.5,.15][fi]
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=random.uniform(.07,.13),location=(r*math.cos(math.radians(a)),r*math.sin(math.radians(a)),zt*random.uniform(.6,1.3)))
        o=bpy.context.object; o.rotation_euler=(random.random()*3,random.random()*3,0); o.data.materials.append(rub)
# --- enemy (slime) rising, clipped at ground
if fi>0:
    body=toon('Slime',(.55,.9,.4,1),(.42,.72,.36,1))
    k=1-(1-rise)**3; z=-.9*(1-k)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=10,ring_count=7,radius=.45,location=(0,0,.38+z)); o=bpy.context.object; o.scale=(1,1,.85); o.data.materials.append(body)
    for sx in (-.15,.15):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=6,ring_count=4,radius=.06,location=(sx,-.4,.45+z)); e=bpy.context.object; e.data.materials.append(toon('Eye',(.1,.08,.18,1)))
    # dust puff
    dust=toon('Dust',(.82,.76,.66,1))
    for k2 in range(8 if fi<3 else 4):
        a=k2*45; r=.55+.25*fi
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.12+.05*fi,location=(r*math.cos(math.radians(a)),r*math.sin(math.radians(a)),.1+.05*fi)); bpy.context.object.data.materials.append(dust)
# flare billboard (painted flipbook frame)
if FRAME!='idle':
    cell=[3,6,9,12][['f0','f1','f2','f3'].index(FRAME)]
    bpy.ops.mesh.primitive_plane_add(size=2.6,location=(0,0,.9)); fl=bpy.context.object; fl.rotation_euler=(math.radians(62),0,0)
    fl.data.materials.append(texmat('Flare',f'_flare_{cell}.png',2.5))
if FRAME=='idle':
    bpy.ops.object.select_all(action='DESELECT'); stones.select_set(True); bpy.context.view_layer.objects.active=stones
    ex=stones.copy(); ex.data=stones.data.copy(); bpy.context.collection.objects.link(ex); ex.name='SM_Portal_Runestones'; stones.name='tmp'
    for p in ex.data.polygons: p.material_index=0
    ex.data.materials.clear(); ex.data.materials.append(stone)
    bpy.ops.object.select_all(action='DESELECT'); ex.select_set(True); bpy.context.view_layer.objects.active=ex
    bpy.ops.export_scene.fbx(filepath=D+'/SM_Portal_Runestones.fbx',use_selection=True,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=False,bake_space_transform=True,apply_scale_options='FBX_SCALE_ALL')
    bpy.data.objects.remove(ex); stones.name='SM_Portal_Runestones'
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.1); rb=bpy.context.object; rb.name='SM_Portal_Rubble'; rb.data.materials.append(stone)
    bpy.ops.object.select_all(action='DESELECT'); rb.select_set(True); bpy.ops.export_scene.fbx(filepath=D+'/SM_Portal_Rubble.fbx',use_selection=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',bake_space_transform=True); bpy.data.objects.remove(rb)
ol=bpy.data.materials.new('Outline'); ol.use_nodes=True; ol.use_backface_culling=True
nn=ol.node_tree.nodes; [nn.remove(x) for x in list(nn)]; o_=nn.new('ShaderNodeOutputMaterial'); e_=nn.new('ShaderNodeEmission'); e_.inputs[0].default_value=(.012,.01,.03,1); ol.node_tree.links.new(e_.outputs[0],o_.inputs[0])
for ob in list(bpy.data.objects):
    if ob.type!='MESH' or ob.name.startswith(('Scorch','Rune','Plane')) or any(m and m.name.startswith(('Flare','Dust')) for m in ob.data.materials): continue
    ob.data.materials.append(ol); md=ob.modifiers.new('OL','SOLIDIFY'); md.thickness=-.03 if not ob.name.startswith('Cube') else -.015; md.use_flip_normals=True; md.material_offset=len(ob.data.materials)-1; md.offset=1
# camera (game-like 3/4 top view) + sun
bpy.ops.object.camera_add(location=(0,-5.2,5.0)); cam=bpy.context.object; cam.location=(0,-6.4,4.6); cam.rotation_euler=(math.radians(56),0,0); cam.data.lens=70; sc.camera=cam
bpy.ops.object.light_add(type='SUN'); s=bpy.context.object; s.rotation_euler=(math.radians(45),math.radians(20),math.radians(-35)); s.data.energy=2.2; s.rotation_euler=(math.radians(55),math.radians(25),math.radians(-40))
if '--norender' not in sys.argv:
    sc.render.filepath=D+f'/render_{FRAME}.png'; bpy.ops.render.render(write_still=True)
