"""Run inside Blender through Blender MCP. Original modular art, no external assets."""
import bpy, math, random, json, os
from mathutils import Vector

ROOT = r'C:/Users/Admin/Documents/Codex/2026-10-06/unity-6-unity-emberward-3d-roguelite/outputs/StoneSignal'
OUT = ROOT + '/Assets/Game/Art/Models'
random.seed(731)
scene = bpy.data.scenes.new('StoneSignal Art Workshop')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
materials = {}
manifest = {'materials': [], 'models': []}

def mat(name, color, metallic=0, rough=.7, emission=0):
    m = bpy.data.materials.new('SS_' + name)
    m.use_nodes = True
    n = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value = (*color, 1)
    n.inputs['Metallic'].default_value = metallic
    n.inputs['Roughness'].default_value = rough
    n.inputs['Emission Color'].default_value = (*color, 1)
    n.inputs['Emission Strength'].default_value = emission
    m.diffuse_color = (*color, 1)
    materials[name] = m
    manifest['materials'].append(dict(name=m.name, color=list(color), metallic=metallic, roughness=rough, emission=emission))
    return m

mat('Basalt', (.12,.19,.23)); mat('Floor',(.24,.32,.34)); mat('FloorLight',(.30,.38,.39))
mat('Stone',(.57,.54,.44)); mat('StoneLight',(.73,.69,.57)); mat('StoneDark',(.34,.36,.32))
mat('Crack',(.095,.13,.15)); mat('Iron',(.065,.11,.15),.7,.32); mat('Steel',(.24,.31,.32),.7,.35)
mat('Gold',(.78,.43,.12),.65,.27); mat('Cyan',(.035,.78,1),.2,.25,2.8)
mat('Ice',(.25,.85,1),.15,.18,1.5); mat('Purple',(.49,.12,1),.2,.22,2.3)
mat('Amber',(1,.39,.025),.25,.25,2.2); mat('WarmWhite',(1,.8,.38),0,.3,2)
mat('Leaf',(.21,.36,.10)); mat('LeafLight',(.37,.47,.16)); mat('Vine',(.12,.23,.08))
mat('Red',(.72,.035,.075),0,.4); mat('Coral',(.95,.12,.1),0,.4); mat('Pink',(.91,.14,.43),0,.4)
mat('Eye',(1,.91,.67),0,.3,1); mat('Void',(.012,.017,.025),.1,.4)

def finish(o, name, material):
    o.name = name; o.data.materials.append(materials[material]); return o
def cube(name, loc, size, material='Stone', bevel=.04, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rot: o.rotation_euler=rot
    if bevel:
        mod=o.modifiers.new('Hand chipped edges','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o,name,material)
def cylinder(name, loc, radius, depth, material='Iron', verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
    return finish(bpy.context.object,name,material)
def ball(name, loc, size, material, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions,radius=1,location=loc)
    o=bpy.context.object; o.scale=size; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,material)
def ring(name, loc, radius, thickness, material):
    bpy.ops.mesh.primitive_torus_add(major_segments=16,minor_segments=4,location=loc,major_radius=radius,minor_radius=thickness)
    return finish(bpy.context.object,name,material)
def beam(name,a,b,width,material):
    a=Vector(a); b=Vector(b); o=cube(name,(a+b)/2,(width,width,(b-a).length),material,0)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def crystal(name, loc, radius, height, material='Cyan', sides=5):
    # Separate cap, waist and tapered foot form an actual faceted crystal, not a cone.
    vs=[(0,0,height)]
    for z,r in [(height*.64,radius),(height*.18,radius*.74)]:
        vs.extend((r*math.cos(i*2*math.pi/sides),r*math.sin(i*2*math.pi/sides),z) for i in range(sides))
    vs.append((0,0,0));faces=[]
    for i in range(sides):
        j=(i+1)%sides; faces.extend([(0,1+i,1+j),(1+i,1+sides+i,1+sides+j,1+j),(len(vs)-1,1+sides+j,1+sides+i)])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);o.location=loc;return finish(o,name,material)
def cracks(z, material='Crack', radius=.43):
    for pts in [[(-radius,.16,z),(-.09,.03,z),(.12,-.18,z),(.4,-.3,z)],[(-.09,.03,z),(-.2,-.3,z)]]:
        for a,b in zip(pts,pts[1:]):beam('Stone fissure',a,b,.009,material)
def rune_corners(z=.012):
    for x in [-1,1]:
        for y in [-1,1]:
            cube('Rune corner',(x*.38,y*.43,z),(.16,.018,.008),'Cyan',0)
            cube('Rune corner',(x*.43,y*.38,z),(.018,.16,.008),'Cyan',0)

models={}
def export(name, build):
    before=set(scene.objects); build()
    parts=[o for o in scene.objects if o not in before and o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=bpy.context.object;o.name=name
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    # Triangulate before export to keep Blender/Unity surfaces identical.
    mod=o.modifiers.new('Export triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.export_scene.fbx(filepath=OUT+'/'+name+'.fbx',use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
    models[name]=o
    manifest['models'].append(dict(name=name, triangles=len(o.data.polygons), materials=[m.name for m in o.data.materials], bounds=list(o.dimensions)))
    o.hide_set(True);o.hide_render=True
    return o

def tile(light=False):
    cube('Basalt footing',(0,0,-.15),(.98,.98,.26),'Basalt',.045)
    cube('Weathered slab',(0,0,-.045),(.93,.93,.09),'FloorLight' if light else 'Floor',.025)
    cracks(.003);rune_corners()
export('TileA',lambda:tile(False));export('TileB',lambda:tile(True))
def wall():
    cube('Wall bottom',(0,0,.2),(.89,.89,.4),'StoneDark',.045)
    cube('Chipped stone cap',(0,0,.49),(.92,.92,.24),'StoneLight',.075)
    cube('Seam front',(0,-.449,.25),(.72,.009,.023),'Crack',0)
    cracks(.612)
    for i in range(3):cube('Rune',( .16+i*.06,-.449,.13+i*.015),(.025,.009,.075),'Cyan',.005)
export('Wall',wall)
def cliff():
    cube('Foundation',(0,0,-.38),(1.01,1.01,.8),'Basalt',.1)
    for i in range(3):
        x=(i-1)*.29
        rock=ball('Fractured hanging rock',(x,-.08,-.9-random.random()*.25),(.24,.43,.65+random.random()*.3),'StoneDark' if i%2 else 'Basalt',1)
    for i in range(2):crystal('Buried luminous shard',(.13+i*.2,-.5,-1.1),.04,.21,'Cyan')
export('Cliff',cliff)
def foliage():
    for i in range(12):
        a=i*2.4; r=random.uniform(.08,.36); loc=(math.cos(a)*r,math.sin(a)*r,random.uniform(.04,.15))
        leaf=ball('Angular leaves',loc,(.16,.1,.045),'LeafLight' if i%3 else 'Leaf',1);leaf.rotation_euler.z=a
    for i in range(3):beam('Trailing vine',(.1*i,0,.06),(.1*i+.1,-.17,-.35),.02,'Vine')
export('Foliage',foliage)
def ruin():
    cube('Plinth',(0,0,.13),(.8,.8,.26),'StoneDark',.05)
    for z in range(4):
        for x in [-.19,.19]:cube('Ancient masonry',(x,0,.35+z*.32),(.35,.55,.29),'Stone' if z%2 else 'StoneLight',.035)
    cube('Broken crown',(-.12,.04,1.53),(.46,.58,.18),'StoneDark',.065,rot=(.06,.12,0))
    for i in range(3):cube('Ancient glyph',(.02,-.286,.5+i*.11),(.07,.012,.06),'Cyan',.005)
    for i in range(8):ball('Climbing leaves',(-.29,-.29,.15+i*.16),(.12,.06,.06),'LeafLight',1)
export('RuinPillar',ruin)
def brazier():
    cube('Stone pedestal',(0,0,.24),(.5,.5,.48),'Stone',.045)
    cylinder('Gold rim',(0,0,.53),.25,.07,'Gold',8)
    cylinder('Coal',(0,0,.57),.2,.04,'Void',8)
    for i in range(4):crystal('Flame',((i%2-.5)*.13,(i//2-.5)*.13,.59),.07,.23+(i%2)*.14,'Amber',5)
    crystal('Hot flame',(0,0,.6),.08,.31,'WarmWhite')
export('Brazier',brazier)
def portal():
    cube('Portal foundation',(0,0,.11),(.96,.65,.22),'StoneDark',.035)
    for x in [-.36,.36]:
        for z in range(4):cube('Gate masonry',(x,0,.3+z*.27),(.23,.39,.25),'Stone' if z%2 else 'StoneLight',.03)
        cube('Portal rails',(x*.72,-.22,.74),(.028,.025,1.03),'Cyan',.005)
    cube('Arch lintel',(0,0,1.36),(.93,.43,.24),'StoneDark',.055)
    crystal('Gate keystone',(0,-.26,1.27),.1,.23,'Cyan')
    cube('Portal energy',(0,.1,.69),(.48,.022,1.07),'Cyan',.04)
    for z in range(4):cube('Portal runes',(0,-.035,.38+z*.2),(.12,.022,.05),'Ice',.01)
export('SpawnPortal',portal)
def core():
    cube('Core terrace',(0,0,.09),(.95,.95,.18),'StoneDark',.04)
    cube('Gilded terrace',(0,0,.22),(.78,.78,.17),'Gold',.04)
    ring('Core glow',(0,0,.36),.31,.035,'Amber')
    cylinder('Core pedestal',(0,0,.39),.29,.27,'Iron',8)
    crystal('Living core',(0,0,.47),.23,1.03,'Amber',6)
    for i in range(4):
        a=i*math.pi/2;crystal('Core satellite',(math.cos(a)*.31,math.sin(a)*.31,.27),.07,.45,'WarmWhite')
export('SignalCore',core)

def tower_base(color):
    cylinder('Stone foundation',(0,0,.075),.45,.15,'StoneDark')
    cylinder('Metal lower lip',(0,0,.18),.4,.1,'Steel')
    ring('Lower energy band',(0,0,.23),.355,.025,'Cyan')
    cylinder('Signal housing',(0,0,.38),.31,.32,'Iron')
    cylinder('Top flange',(0,0,.57),.35,.07,'Steel')
    for i in range(6):
        a=i*math.pi/3; x=math.cos(a);y=math.sin(a)
        cube('Armored support',(x*.32,y*.32,.38),(.115,.115,.24),'Gold' if color=='Amber' else 'Steel',.02,rot=(0,0,a))
        ball('Luminous socket',(x*.335,y*.335,.41),(.047,.047,.06),color,1)
def needle():
    tower_base('Cyan');crystal('Needle prism',(0,0,.61),.16,.67,'Cyan',5)
    for i in range(3):
        a=i*2*math.pi/3;crystal('Focus shard',(math.cos(a)*.21,math.sin(a)*.21,.54),.055,.32,'Ice')
export('NeedleBeacon',needle)
def pulse():
    tower_base('Purple');ball('Pulse orb',(0,0,.91),(.21,.21,.21),'Purple',2)
    for i in range(2):
        o=ring('Orb gyroscope',(0,0,.91),.26,.027,'Gold' if i==0 else 'Purple');o.rotation_euler=(math.pi/2 if i==0 else .7,.5 if i==0 else -.5,0)
    for i in range(4):
        a=i*math.pi/2;beam('Conductive prong',(math.cos(a)*.24,math.sin(a)*.24,.58),(math.cos(a)*.23,math.sin(a)*.23,.78),.06,'Steel')
export('PulseBeacon',pulse)
def seismic():
    tower_base('Amber');cylinder('Mortar barrel',(0,0,.75),.235,.34,'Iron',10)
    ring('Gold muzzle',(0,0,.94),.225,.048,'Gold');cylinder('Dark bore',(0,0,.94),.19,.025,'Void',12)
    cylinder('Hot chamber',(0,0,.96),.1,.018,'Amber',8)
    for i in range(4):
        a=i*math.pi/2;cube('Reinforced breech',(math.cos(a)*.24,math.sin(a)*.24,.73),(.09,.09,.26),'Gold',.02)
export('SeismicBeacon',seismic)
def chill():
    tower_base('Ice');crystal('Frost spire',(0,0,.6),.13,.68,'Ice',6)
    for i in range(5):
        a=i*2*math.pi/5;crystal('Ice cluster',(math.cos(a)*.23,math.sin(a)*.23,.5),.07,.36+(i%2)*.16,'Cyan')
    ring('Frost halo',(0,0,.68),.29,.014,'Ice')
export('ChillBeacon',chill)

def monster(kind):
    color={'Normal':'Coral','Fast':'Pink','Tank':'Red','Splitter':'Purple'}[kind]
    radius=.24 if kind=='Fast' else .29
    ball('Body',(0,0,.41),(radius,radius*.85,.25),color,2)
    for x in [-1,1]:
        ball('Foot',(x*.14,-.04,.09),(.1,.13,.09),'Iron',1)
        ball('Hand',(x*.30,-.025,.31),(.075,.095,.095),color,1)
        ball('Eye',(x*.105,-.19,.45),(.066,.044,.078),'Eye',2)
        ball('Pupil',(x*.105,-.226,.455),(.023,.015,.04),'Void',1)
        crystal('Horn',(x*.17,.015,.58),.045,.17,'Gold' if kind=='Tank' else 'StoneLight')
    cube('Mouth',(0,-.221,.31),(.085,.015,.026),'Void',.008)
    if kind=='Tank':
        cube('Heavy armor',(0,.11,.43),(.59,.42,.37),'Steel',.085)
        for x in [-1,1]:cube('Shoulder plate',(x*.29,0,.47),(.18,.25,.22),'Gold',.035)
    if kind=='Fast':
        for x in [-1,1]:beam('Swept wing',(x*.23,.09,.41),(x*.41,.28,.51),.07,'Gold')
    if kind=='Splitter':
        for i in range(3):crystal('Split growth',((i-1)*.13,.08,.54),.065,.24,'Purple')
export('Drifter',lambda:monster('Normal'));export('Skimmer',lambda:monster('Fast'))
export('Bulwark',lambda:monster('Tank'));export('Splitter',lambda:monster('Splitter'))
export('Shard',lambda:monster('Fast'))

with open(ROOT+'/ArtSource/art_manifest.json','w') as f:json.dump(manifest,f,indent=2)
# Arrange non-export duplicates for a inspectable asset sheet.
for i,(name,original) in enumerate(models.items()):
    o=original.copy();o.data=original.data;scene.collection.objects.link(o)
    o.name='Preview '+name;o.hide_set(False);o.hide_render=False;o.location=((i%5)*2.2,(i//5)*2.2,0)

bpy.ops.object.camera_add(location=(11,-15,18));camera=bpy.context.object;camera.name='StoneSignal asset camera'
target=Vector((4.4,3.7,.2));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=14;scene.camera=camera
for loc,energy,size,color in [((2,-5,12),1800,8,(1,.8,.57)),((8,8,10),1400,7,(.28,.7,1))]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=size;o.data.color=color;o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('StoneSignal dusk');scene.world.use_nodes=True
bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.035,.07,.11,1);bg.inputs[1].default_value=.45
scene.render.engine=[x.identifier for x in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items][0]
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format=[x.identifier for x in bpy.types.ImageFormatSettings.bl_rna.properties['file_format'].enum_items if x.identifier=='PNG'][0]
scene.render.filepath=ROOT+'/Verification/Art/BlenderAssetSheet.png'
# Save a dedicated file, original Blender file stays intact on disk.
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/ArtSource/StoneSignal_Art.blend')
print('Created',len(models),'models;',sum(m['triangles'] for m in manifest['models']),'triangles across source set')
print([(m['name'],m['triangles']) for m in manifest['models']])

# Reproducible transparent icons, exported from the exact meshes used in the game.
for o in scene.objects:
    if o.type=='MESH':o.hide_render=True
scene.render.film_transparent=True;scene.render.image_settings.color_mode='RGBA'
scene.render.resolution_x=256;scene.render.resolution_y=256
camera.location=(2.5,-4,3);camera.rotation_euler=(Vector((0,0,.6))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=1.8
for name in ['NeedleBeacon','PulseBeacon','SeismicBeacon','ChillBeacon']:
    o=models[name];o.hide_render=False;scene.render.filepath=ROOT+'/Assets/Game/Art/Icons/'+name+'.png'
    bpy.ops.render.render(write_still=True);o.hide_render=True
shapes={'I':[(0,0),(0,1),(0,2),(0,3)],'O':[(0,0),(1,0),(0,1),(1,1)],'L':[(0,0),(0,1),(0,2),(1,0)],'T':[(0,0),(1,0),(2,0),(1,1)],'S':[(0,0),(1,0),(1,1),(2,1)]}
for name,cells in shapes.items():
    copies=[];cx=max(x for x,y in cells)/2;cy=max(y for x,y in cells)/2
    for x,y in cells:
        o=models['Wall'].copy();scene.collection.objects.link(o);o.hide_render=False;o.location=((x-cx)*.95,(y-cy)*.95,0);copies.append(o)
    camera.data.ortho_scale=4.1;camera.location=(4,-6,6);camera.rotation_euler=(Vector((0,0,.3))-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=ROOT+'/Assets/Game/Art/Icons/Block'+name+'.png';bpy.ops.render.render(write_still=True)
    for o in copies:bpy.data.objects.remove(o,do_unlink=True)
for o in scene.objects:
    if o.name.startswith('Preview '):o.hide_render=False
scene.render.film_transparent=False;scene.render.resolution_x=1400;scene.render.resolution_y=1000
camera.location=(11,-15,18);camera.rotation_euler=(Vector((4.4,3.7,.2))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=14
scene.render.filepath=ROOT+'/Verification/Art/BlenderAssetSheet.png';bpy.ops.render.render(write_still=True)
bpy.data.libraries.write(ROOT+'/ArtSource/StoneSignal_Art.blend',{scene},fake_user=False,compress=True)
