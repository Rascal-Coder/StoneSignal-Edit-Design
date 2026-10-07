import sys, math
MODE=sys.argv[-1]
src=open('/workspace/v17/run_flyer.py').read()
exec(compile(src,'run_flyer','exec'))
import bpy, mathutils
sc=bpy.context.scene
def mk(loc,rz,bank=0,frame_t=.25,flap_override=None):
    objs=flyer(); arm=[o for o in objs if o.type=='ARMATURE'][0]
    arm.location=loc; arm.rotation_euler=(bank,0,rz)
    act=[a for a in bpy.data.actions if a.name.startswith(arm.data.name.replace('_Rig',''))]
    acts=RIGS[[k for k in RIGS][-1]][1]
    arm.animation_data.use_nla=False; arm.animation_data.action=acts['SS_Move']
    for o in objs:
        if o.type=='MESH':
            if OUTLINE.name not in o.data.materials: o.data.materials.append(OUTLINE)
            m=o.modifiers.new('Outline','SOLIDIFY'); m.thickness=-.022; m.use_flip_normals=True; m.material_offset=1; m.use_rim=False
    return arm
def flatm(c,a=1.0):
    m=bpy.data.materials.new('f'); m.use_nodes=True; nt=m.node_tree; n=nt.nodes; n.clear(); o=n.new('ShaderNodeOutputMaterial')
    e=n.new('ShaderNodeEmission'); e.inputs[0].default_value=c+(1,)
    if a<1:
        tr=n.new('ShaderNodeBsdfTransparent'); mx=n.new('ShaderNodeMixShader'); mx.inputs[0].default_value=a
        nt.links.new(tr.outputs[0],mx.inputs[1]); nt.links.new(e.outputs[0],mx.inputs[2]); nt.links.new(mx.outputs[0],o.inputs[0])
        try: m.surface_render_method='BLENDED'
        except: pass
    else: nt.links.new(e.outputs[0],o.inputs[0])
    return m
# hide the exported original
for o in list(sc.objects): o.hide_render=True
def cam(res,ortho,pos,tgt):
    sc.render.engine='BLENDER_EEVEE_NEXT'; sc.render.resolution_x,sc.render.resolution_y=res
    sc.world=bpy.data.worlds.new('w'); sc.world.color=(.09,.08,.16)
    c=bpy.data.cameras.new('c'); c.type='ORTHO'; c.ortho_scale=ortho; co=bpy.data.objects.new('c',c); sc.collection.objects.link(co)
    co.location=pos; co.rotation_euler=(mathutils.Vector(tgt)-mathutils.Vector(pos)).to_track_quat('-Z','Y').to_euler(); sc.camera=co
    s=bpy.data.lights.new('s','SUN'); s.energy=3; so=bpy.data.objects.new('s',s); so.rotation_euler=(math.radians(40),0,math.radians(-30)); sc.collection.objects.link(so)
if MODE=='turn':
    # front (faces -Y toward cam), 3/4, side, back ; frames: flap mid-down
    for k,rz in enumerate([0,math.radians(-40),math.radians(-90),math.radians(180)]): mk((k*2.0-3.0,0,0),rz)
    cam((1700,560),8.6,(0,-14,3.0),(0,0,.5)); sc.frame_set(4); sc.render.filepath='/workspace/v17/bird_turn.png'; bpy.ops.render.render(write_still=True)
    sc.frame_set(9); sc.render.filepath='/workspace/v17/bird_turn_up.png'; bpy.ops.render.render(write_still=True)
else:
    T=.8; t1=flatm((.42,.35,.48)); t2=flatm((.48,.41,.54)); pm=flatm((.62,.42,.36))
    for x in range(-5,6):
        for y in range(-3,4):
            bpy.ops.mesh.primitive_cube_add(size=1,location=(x,y,.4)); o=bpy.context.object; o.scale=(.95,.95,.8); o.data.materials.append(pm if y==0 else (t1 if (x+y)%2 else t2))
    def blob(x,y,r,a,soft):
        for k,(rr,aa) in enumerate([(r,a)]+([(r*1.3,a*.45),(r*1.6,a*.2)] if soft else [])):
            bpy.ops.mesh.primitive_circle_add(vertices=24,radius=rr,fill_type='NGON',location=(x,y,T+.01-k*.001)); bpy.context.object.data.materials.append(flatm((.1,.06,.16),aa))
    # ground enemy reference: drifter built by the same generator
    objs=drifter()
    for o in objs:
        if o.parent is None: o.location=(-3,0,T); o.rotation_euler=(0,0,math.radians(-90))
        if o.type=='MESH':
            o.data.materials.append(OUTLINE); m=o.modifiers.new('Outline','SOLIDIFY'); m.thickness=-.022; m.use_flip_normals=True; m.material_offset=1; m.use_rim=False
    blob(-3,0,.42,.6,False)
    for x,bob,bank,rz in [(-.8,.1,0,-90),(1.2,-.12,22,-70),(3.2,.05,-14,-105)]:
        mk((x,0,T+1.2+bob),math.radians(rz),math.radians(bank)); blob(x,0,.42*.55,.35,True)
        bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.012,depth=1.2,location=(x,-.7,T+.6)); bpy.context.object.data.materials.append(flatm((1,.85,.4),.7))
    # Unity game cam pitch 40 yaw 10; Unity(x,y,z)->Blender(x,z,y)... Unity +Z forward = Blender +Y
    p,yw=math.radians(40),math.radians(10)
    f=mathutils.Vector((math.sin(yw)*math.cos(p),math.cos(yw)*math.cos(p),-math.sin(p)))   # blender coords
    tgt=mathutils.Vector((.3,0,1.2)); cam((1600,900),2*3.2*16/9,tgt-f*30,tgt)
    sc.frame_set(4); sc.render.filepath='/workspace/v17/bird_game.png'; bpy.ops.render.render(write_still=True)
