import bpy, bmesh, math, random, sys, mathutils
out=sys.argv[sys.argv.index('--')+1]
def lin(c): return tuple(v**2.2 for v in c)
def toon(name,col,emit=None,es=0):
    m=bpy.data.materials.new(name); m.use_nodes=True; nt=m.node_tree; n=nt.nodes; n.clear()
    o=n.new('ShaderNodeOutputMaterial')
    if emit:
        e=n.new('ShaderNodeEmission'); e.inputs[0].default_value=lin(emit)+(1,); e.inputs[1].default_value=es; nt.links.new(e.outputs[0],o.inputs[0]); return m
    d=n.new('ShaderNodeBsdfDiffuse'); d.inputs[0].default_value=lin(col)+(1,); s2r=n.new('ShaderNodeShaderToRGB'); cr=n.new('ShaderNodeValToRGB')
    cr.color_ramp.interpolation='CONSTANT'; cr.color_ramp.elements[0].color=tuple(v*.55 for v in lin(col))+(1,); cr.color_ramp.elements[1].position=.35; cr.color_ramp.elements[1].color=lin(col)+(1,)
    em=n.new('ShaderNodeEmission'); nt.links.new(d.outputs[0],s2r.inputs[0]); nt.links.new(s2r.outputs[1],cr.inputs[0]); nt.links.new(cr.outputs[0],em.inputs[0]); nt.links.new(em.outputs[0],o.inputs[0]); return m

def add(o,m): o.data.materials.append(m); return o
def cyl(v,r,d,loc,m,rot=0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=v,radius=r,depth=d,location=loc,rotation=(0,0,rot)); return add(bpy.context.object,m)
def box(s,loc,m,rot=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc,rotation=rot); o=bpy.context.object; o.scale=s; return add(o,m)
def outline(o,w=.03):
    md=o.modifiers.new('ol','SOLIDIFY'); md.thickness=w; md.use_flip_normals=True; md.material_offset=5
    k=bpy.data.materials.get('ink') or toon('ink',None,(.09,.07,.18),1)
    k.use_backface_culling=True
    while len(o.data.materials)<6: o.data.materials.append(k)
def build(state):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    global STONE,STD,RUNE,CRACK,SMOKE,SPARK,TILE,COREC,CORER
    STONE=toon('stone',(.58,.55,.66)); STD=toon('stoneD',(.40,.37,.50)); RUNE=toon('rune',None,(1,.55,.15),2.5); CRACK=toon('crack',None,(1,.45,.1),3)
    SMOKE=toon('smoke',(.35,.33,.4)); SPARK=toon('spark',None,(1,.75,.3),12); TILE=toon('tile',(.42,.62,.36)); COREC=toon('core',None,(1,.5,.15),2.2); CORER=toon('coreR',None,(1,.1,.06),2.6)
    random.seed(3); parts=[]
    for x in range(-2,3):
        for y in range(-2,3): parts.append(box((.96,.96,.3),(x,y,-.15),TILE))
    parts.append(cyl(8,1.15,.22,(0,0,.11),STONE,math.pi/8)); parts.append(cyl(8,.85,.16,(0,0,.3),STD,math.pi/8))
    # ring wall: 12 low blocks at r=1.55, 2 gaps (path in/out)
    for k in range(12):
        if k in (0,6): continue
        a=k/12*6.283; broken= state>=2 and k in (2,3,8,10)
        h=.32 if not broken else .14
        o=box((.42,.26,h),(math.cos(a)*1.55,math.sin(a)*1.55,h/2+.02),STONE,(random.uniform(-.06,.06),0,a+math.pi/2)); parts.append(o)
    # 4 rune pillars
    for k in range(4):
        a=k/4*6.283+math.pi/4; p=(math.cos(a)*1.5,math.sin(a)*1.5)
        top=1.05 if not (state>=2 and k==1) else .55
        parts.append(cyl(5,.17,top,(p[0],p[1],top/2),STD,a))
        if top>.6: parts.append(cyl(5,.21,.1,(p[0],p[1],top+.05),STONE,a))
        parts.append(box((.06,.05,top*.55),(p[0]-math.cos(a)*.16,p[1]-math.sin(a)*.16,top*.5),RUNE if state<3 or k%2==0 else STD,(0,0,a)))
    objs=list(bpy.context.scene.objects)
    for o in objs: outline(o)
    # core
    cm=CORER if state==3 else COREC
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=0,radius=.34,location=(0,0,.95)); c=bpy.context.object; c.scale=(1,1,1.6); add(c,cm)
    bpy.ops.mesh.primitive_cone_add(vertices=6,radius1=.3,depth=.25,location=(0,0,.48)); add(bpy.context.object,STD)
    if state>=1:   # cracks on dais top + wall
        for k in range(7 if state==1 else 12):
            a=random.uniform(0,6.283); r=random.uniform(.35,1.0)
            box((random.uniform(.22,.45),.025,.01),(math.cos(a)*r,math.sin(a)*r,.385 if r<.85 else .225),CRACK,(0,0,a+random.uniform(-.6,.6)))
    if state>=2:   # broken chunks + smoke
        for k in range(6):
            a=random.uniform(0,6.283); r=random.uniform(1.75,2.2)
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=random.uniform(.08,.15),location=(math.cos(a)*r,math.sin(a)*r,.08)); add(bpy.context.object,STONE)
        for k in range(5):
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2,radius=.18+k*.05,location=(.3+random.uniform(-.2,.2),.2,1.0+k*.28)); add(bpy.context.object,SMOKE)
    if state==3:
        for k in range(10):
            a=random.uniform(0,6.283); r=random.uniform(.3,.7)
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=0,radius=.035,location=(math.cos(a)*r,math.sin(a)*r,random.uniform(.6,1.6))); add(bpy.context.object,SPARK)
    cam=bpy.data.cameras.new('c'); cam.type='ORTHO'; cam.ortho_scale=5.2; co=bpy.data.objects.new('c',cam); bpy.context.scene.collection.objects.link(co)
    co.location=(4.2,-7.2,6.2); d=mathutils.Vector((0,0,.6))-co.location; co.rotation_euler=d.to_track_quat('-Z','Y').to_euler(); bpy.context.scene.camera=co
    s=bpy.data.lights.new('s','SUN'); s.energy=4; so=bpy.data.objects.new('s',s); so.rotation_euler=(math.radians(45),0,math.radians(35)); bpy.context.scene.collection.objects.link(so)
    sc=bpy.context.scene; sc.world=bpy.data.worlds.new('w'); sc.world.color=(.06,.05,.1)
    sc.render.engine='BLENDER_EEVEE_NEXT'; sc.render.resolution_x=640; sc.render.resolution_y=560; sc.eevee.use_bloom=True if hasattr(sc.eevee,'use_bloom') else None
    sc.render.filepath=f'{out}/core_s{state}.png'; bpy.ops.render.render(write_still=True)
    if state in (0,1,2):   # export enclosure mesh variant (no core/tiles/fx), outlines applied off
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            if o.data.materials and o.data.materials[0].name!='tile': o.modifiers.clear(); o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=f'{out}/SM_Core_Enclosure_{["Intact","Cracked","Broken"][state]}.fbx',use_selection=True,bake_space_transform=True,apply_scale_options='FBX_SCALE_ALL')
for st in range(4): build(st)
