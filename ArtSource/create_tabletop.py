"""Kenney CC0 modular pieces + original toy meshes. Execute through Blender MCP."""
import bpy, math, random, json, os
from mathutils import Vector
ROOT = r'C:/Users/Admin/Documents/Codex/2026-10-06/unity-6-unity-emberward-3d-roguelite/outputs/StoneSignal'
OUT = ROOT + '/Assets/Game/Art/Models'
random.seed(731)
scene = bpy.data.scenes.new('StoneSignal Tabletop Workshop')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
materials = {}
manifest = {'materials': [], 'models': []}

def mat(name,color,emission=0):
    m=bpy.data.materials.get('SST_'+name) or bpy.data.materials.new('SST_'+name)
    m.use_nodes=True
    n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value=(*color,1)
    n.inputs['Metallic'].default_value=0
    n.inputs['Roughness'].default_value=.78
    n.inputs['Emission Color'].default_value=(*color,1)
    n.inputs['Emission Strength'].default_value=emission
    m.diffuse_color=(*color,1); materials[name]=m
    manifest['materials'].append(dict(name=m.name,color=list(color),metallic=0,roughness=.78,emission=emission))

for name,color in {
 'Earth':(.63,.32,.14),'EarthLight':(.72,.40,.20),'EarthEdge':(.38,.19,.095),
 'Wood':(.45,.245,.12),'Sand':(.88,.65,.34),'Stone':(.46,.53,.39),'StoneLight':(.65,.69,.56),
 'StoneDark':(.29,.36,.32),'Navy':(.085,.17,.25),'Blue':(.035,.48,.88),'BlueLight':(.21,.74,.96),
 'Yellow':(.98,.65,.09),'YellowLight':(1,.85,.35),'Leaf':(.25,.46,.21),'LeafLight':(.43,.63,.31),
 'Purple':(.48,.22,.70),'Lilac':(.67,.37,.86),'Plum':(.31,.13,.49),'Cream':(.99,.91,.74),
 'Void':(.045,.06,.09),'Skin':(.91,.63,.39),'Cape':(.04,.33,.63)
}.items():mat(name,color)
mat('Ice',(.28,.8,1),.45);mat('Amber',(1,.31,.015),1.3);mat('FlameYellow',(1,.77,.11),1.5)

def finish(o,name,material):o.name=name;o.data.materials.append(materials[material]);return o
def cube(name,loc,size,material='Stone',bevel=.045,rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if rot:o.rotation_euler=rot
    if bevel:
        mod=o.modifiers.new('Soft toy bevel','BEVEL');mod.width=bevel;mod.segments=2
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o,name,material)
def cylinder(name,loc,radius,depth,material='Navy',verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,location=loc)
    o=bpy.context.object
    mod=o.modifiers.new('Soft edge','BEVEL');mod.width=min(.025,depth*.18);mod.segments=2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o,name,material)
def ball(name,loc,size,material,subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions,radius=1,location=loc)
    o=bpy.context.object;o.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,material)
def ring(name,loc,radius,thickness,material):
    bpy.ops.mesh.primitive_torus_add(major_segments=16,minor_segments=6,location=loc,major_radius=radius,minor_radius=thickness)
    return finish(bpy.context.object,name,material)
def beam(name,a,b,width,material):
    a=Vector(a);b=Vector(b);o=cube(name,(a+b)/2,(width,width,(b-a).length),material,.018)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def crystal(name,loc,radius,height,material='BlueLight',sides=5):
    vs=[(0,0,height)]
    for z,r in [(height*.65,radius),(height*.15,radius*.72)]:
        vs.extend((r*math.cos(i*2*math.pi/sides),r*math.sin(i*2*math.pi/sides),z) for i in range(sides))
    vs.append((0,0,0));faces=[]
    for i in range(sides):
        j=(i+1)%sides;faces.extend([(0,1+i,1+j),(1+i,1+sides+i,1+sides+j,1+j),(len(vs)-1,1+sides+j,1+sides+i)])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);o.location=loc;return finish(o,name,material)
def flame(loc,size,material):
    vs=[];rings=[(0,.21,0),(.27,.29,-.04),(.57,.17,.08),(.82,.10,.19),(1,.005,.11)]
    for z,r,x in rings:
        vs.extend(((x+r*math.cos(i*math.pi/4))*size,r*math.sin(i*math.pi/4)*size,z*size) for i in range(8))
    faces=[tuple(reversed(range(8)))]
    for level in range(4):
        for i in range(8):faces.append((level*8+i,level*8+(i+1)%8,(level+1)*8+(i+1)%8,(level+1)*8+i))
    faces.append(tuple(range(32,40)));mesh=bpy.data.meshes.new('Curved flame');mesh.from_pydata(vs,[],faces);mesh.update()
    o=bpy.data.objects.new('Curved magical flame',mesh);scene.collection.objects.link(o);o.location=loc;return finish(o,'Curved magical flame',material)
def kenney(name,size,loc,lower,upper=None,split=.45):
    # Import the user's selected kit, normalize proportions, replace atlas colors with shared flat materials.
    before=set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=ROOT+'/ArtSource/ThirdParty/Kenney/Source/Models/GLB format/'+name+'.glb')
    parts=[o for o in scene.objects if o not in before and o.type=='MESH']
    if len(parts)!=1:raise ValueError('Expected one modular mesh: '+name)
    o=parts[0];o.parent=None;o.data=o.data.copy()
    mins=[min(v.co[i] for v in o.data.vertices) for i in range(3)]
    maxs=[max(v.co[i] for v in o.data.vertices) for i in range(3)]
    for v in o.data.vertices:
        for i in range(3):v.co[i]=(v.co[i]-(mins[i]+maxs[i])*.5)/(maxs[i]-mins[i])*size[i]
    o.location=loc;o.data.materials.clear();o.data.materials.append(materials[lower]);o.data.materials.append(materials[upper or lower])
    for p in o.data.polygons:
        mean=sum(o.data.vertices[i].co.z for i in p.vertices)/len(p.vertices)
        p.material_index=int(mean>size[2]*(split-.5))
    mod=o.modifiers.new('Unified soft bevel','BEVEL');mod.width=.012;mod.segments=2
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
    o['source']='Kenney Tower Defense Kit CC0 / '+name
    return o
models={}
def export(name,build):
    before=set(scene.objects);build();parts=[o for o in scene.objects if o not in before and o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=bpy.context.object;o.name=name
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    mod=o.modifiers.new('Export triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.export_scene.fbx(filepath=OUT+'/'+name+'.fbx',use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
    models[name]=o
    manifest['models'].append(dict(name=name,triangles=len(o.data.polygons),materials=[m.name for m in o.data.materials],bounds=list(o.dimensions)))
    o.hide_set(True);o.hide_render=True

def tile(light):
    kenney('tile',(.975,.975,.26),(0,0,-.13),'Earth','EarthLight' if light else 'Earth',.6)
    # One quiet inset, no cracks or luminous clutter across the whole board.
    cube('Warm top',(0,0,-.017),(.84,.84,.045),'EarthLight' if light else 'Earth',.025)
export('TileA',lambda:tile(False));export('TileB',lambda:tile(True))
def wall():
    kenney('tower-square-bottom-a',(.90,.90,.56),(0,0,.28),'Stone','StoneLight',.78)
    cube('Soft gray cap',(0,0,.57),(.92,.92,.12),'StoneLight',.05)
    cube('Inset face',(0,-.453,.29),(.48,.016,.24),'StoneDark',.03)
    cube('Small blue marker',(0,-.465,.29),(.08,.018,.13),'BlueLight',.02)
export('Wall',wall)
export('Cliff',lambda:cube('Board edge chunk',(0,0,-.47),(1,1,.58),'EarthEdge',.07))
export('BoardBase',lambda:cube('Tabletop beveled plinth',(0,0,-.45),(1,1,.55),'Wood',.045))
def foliage():
    for i,(x,y,z,r) in enumerate([(-.22,0,.12,.22),(.17,.03,.14,.26),(0,.18,.13,.24)]):
        ball('Toy bush',(x,y,z),(r,r,r*.75),'LeafLight' if i%2 else 'Leaf',1)
export('Foliage',foliage)
def ruin():
    kenney('detail-tree',(.85,.85,1.28),(0,0,.64),'Wood','Leaf',.26)
    ball('Light crown',(-.13,-.04,1.13),(.38,.37,.34),'LeafLight',1)
export('RuinPillar',ruin)
def brazier():
    cube('Brazier foot',(0,0,.13),(.48,.48,.26),'Stone',.05)
    cylinder('Yellow bowl',(0,0,.29),.24,.13,'Yellow',8)
    flame((0,0,.35),.42,'Amber');flame((0,-.05,.35),.27,'FlameYellow')
export('Brazier',brazier)
def portal():
    cube('Entry step',(0,0,.08),(.98,.72,.16),'StoneLight',.06)
    for x in [-.37,.37]:
        cube('Entry column',(x,0,.67),(.24,.32,1.16),'Stone',.055)
        cube('Entry blue inlay',(x,-.18,.71),(.07,.02,.74),'BlueLight',.018)
    cube('Rounded entry lintel',(0,0,1.26),(.98,.4,.25),'StoneLight',.065)
    cube('Purple portal',(0,.06,.7),(.49,.025,1.02),'Lilac',.06)
    ring('Arrival arch emblem',(0,-.22,1.23),.13,.035,'Yellow')
export('SpawnPortal',portal)
def core():
    cube('Core platform',(0,0,.11),(.94,.94,.22),'StoneLight',.06)
    cylinder('Core orange bowl',(0,0,.29),.39,.19,'Yellow',8)
    cylinder('Core hearth',(0,0,.4),.27,.18,'Wood',8)
    flame((0,0,.49),1.08,'Amber');flame((0,-.12,.50),.79,'FlameYellow')
    for i in range(4):
        a=i*math.pi/2;ball('Magic ember',(math.cos(a)*.26,math.sin(a)*.26,.92+i*.13),(.045,.045,.075),'FlameYellow',1)
export('SignalCore',core)
def base():
    kenney('tower-round-base',(.87,.87,.18),(0,0,.09),'Stone','StoneLight',.4)
    cylinder('Blue lower platform',(0,0,.23),.39,.14,'Blue',8)
    cylinder('Yellow rim',(0,0,.32),.40,.07,'Yellow',8)
    cylinder('Simple housing',(0,0,.45),.29,.22,'Navy',8)
    for x in [-.23,.23]:cube('Blue side panels',(x,-.23,.45),(.14,.09,.16),'BlueLight',.03)
def needle():
    base();crystal('Oversized blue focus',(0,0,.54),.285,.94,'BlueLight',5)
    for x in [-.23,.23]:crystal('Yellow crystal holders',(x,0,.50),.08,.38,'Yellow')
export('NeedleBeacon',needle)
def pulse():
    base();cube('Mechanical lens body',(0,0,.88),(.64,.43,.61),'Yellow',.085)
    # Giant lens faces forward; its disc is unmistakable even at gameplay scale.
    o=cylinder('Lens navy surround',(0,-.25,.9),.32,.16,'Navy',16);o.rotation_euler.x=math.pi/2
    o=cylinder('Lens blue glass',(0,-.345,.9),.245,.045,'BlueLight',16);o.rotation_euler.x=math.pi/2
    o=cylinder('Lens bright center',(0,-.38,.9),.12,.035,'Cream',12);o.rotation_euler.x=math.pi/2
    for x in [-.38,.38]:cube('Lens ear',(x,0,.87),(.12,.32,.25),'Blue',.04)
export('PulseBeacon',pulse)
def seismic():
    base()
    # Barrel cylinder leans forward: exaggerated mortar, wide orange bore.
    axis=Vector((0,-.55,.85)).normalized();center=Vector((0,-.12,.90))
    o=cylinder('Oversized yellow mortar',center,.315,.72,'Yellow',12);o.rotation_euler=axis.to_track_quat('Z','Y').to_euler()
    tip=center+axis*.37
    o=ring('Large muzzle rim',tip,.29,.065,'YellowLight');o.rotation_euler=axis.to_track_quat('Z','Y').to_euler()
    o=cylinder('Dark mortar bore',tip+axis*.005,.238,.018,'Void',12);o.rotation_euler=axis.to_track_quat('Z','Y').to_euler()
    o=cylinder('Orange charge',tip+axis*.02,.11,.02,'Amber',8);o.rotation_euler=axis.to_track_quat('Z','Y').to_euler()
    for x in [-.34,.34]:cylinder('Barrel hinge',(x,0,.66),.12,.13,'Blue',8).rotation_euler.y=math.pi/2
export('SeismicBeacon',seismic)
def chill():
    base();cylinder('Frost drum',(0,0,.65),.30,.27,'Blue',8)
    for i in range(3):
        a=i*2*math.pi/3;crystal('Large ice crown',(math.cos(a)*.21,math.sin(a)*.21,.69),.16,.63+(i==0)*.17,'Ice',4)
    ring('Frost belt',(0,0,.76),.32,.035,'Cream')
export('ChillBeacon',chill)
def monster(kind):
    r=.31 if kind!='Tank' else .39
    h=.32 if kind!='Tank' else .39
    ball('Purple creature',(0,0,h),(r,r*.82,h),'Lilac' if kind=='Fast' else 'Plum' if kind=='Tank' else 'Purple')
    for x in [-r*.46,r*.46]:
        ball('Big cream eyes',(x,-r*.75,h+.07),(.086,.05,.10),'Cream')
        ball('Dark pupils',(x,-r*.89,h+.07),(.041,.025,.055),'Void',1)
        ball('Short feet',(x,-.06,.055),(.10,.15,.07),'Plum',1)
    if kind=='Fast':
        for x in [-1,1]:crystal('Swept runner fins',(x*.23,.09,h),.10,.31,'Lilac',4).rotation_euler.y=x*.7
    elif kind=='Tank':
        cube('Wide lavender armor',(0,.04,.48),(.76,.48,.27),'Purple',.075)
        for x in [-.38,.38]:ball('Huge armor shoulders',(x,0,.39),(.13,.18,.17),'Lilac',1)
    elif kind=='Splitter':
        for x in [-.16,.16]:crystal('Twin split horns',(x,.025,.54),.095,.28,'Lilac',4)
        cube('Visible split band',(0,-.245,.31),(.065,.03,.27),'Lilac',.012)
    else:
        for x in [-.16,.16]:crystal('Tiny creature ears',(x,.04,.54),.07,.16,'Lilac',4)
export('Drifter',lambda:monster('Normal'));export('Skimmer',lambda:monster('Fast'))
export('Bulwark',lambda:monster('Tank'));export('Splitter',lambda:monster('Splitter'));export('Shard',lambda:monster('Normal'))
def adventurer():
    for x in [-.12,.12]:cube('Cute boots',(x,-.025,.09),(.18,.24,.18),'Wood',.045)
    cube('Toy adventurer coat',(0,0,.36),(.35,.25,.44),'Cape',.065)
    cube('Yellow coat trim',(0,-.14,.35),(.12,.025,.36),'Yellow',.02)
    ball('Round face',(0,-.015,.68),(.205,.18,.205),'Skin',2)
    for x in [-.065,.065]:ball('Adventurer eyes',(x,-.18,.70),(.025,.023,.035),'Void',1)
    cylinder('Oversized hat brim',(0,0,.82),.29,.055,'Cream',12)
    crystal('Pointed wizard hat',(0,0,.84),.20,.32,'Blue',6)
    for x in [-.23,.23]:ball('Round hands',(x,-.005,.42),(.075,.075,.075),'Skin',1)
    beam('Tiny staff',(.31,0,.06),(.31,0,.78),.05,'Wood')
    crystal('Staff focus',(.31,0,.78),.09,.25,'YellowLight',4)
export('Adventurer',adventurer)
with open(ROOT+'/ArtSource/art_manifest.json','w') as f:json.dump(manifest,f,indent=2)
for index,(name,o) in enumerate(models.items()):
    p=o.copy();p.data=o.data;scene.collection.objects.link(p);p.name='Tabletop preview '+name
    p.hide_set(False);p.hide_render=False;p.location=((index%5)*2.1,(index//5)*2.1,0)
bpy.ops.object.camera_add(location=(11,-14,19));camera=bpy.context.object;camera.name='Tabletop asset camera'
camera.rotation_euler=(Vector((4.2,3.2,.25))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=13.5;scene.camera=camera
for loc,energy,size,color in [((2,-4,11),1700,8,(1,.89,.73)),((8,7,9),1250,7,(.76,.87,1))]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=size;o.data.color=color
    o.rotation_euler=(Vector((4,3,0))-o.location).to_track_quat('-Z','Y').to_euler()
world=bpy.data.worlds.new('Tabletop cream studio');world.use_nodes=True
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.55,.65,.70,1)
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=.5;scene.world=world
engines=[i.identifier for i in scene.render.bl_rna.properties['engine'].enum_items]
scene.render.engine=next(e for e in engines if 'EEVEE' in e)
formats=[i.identifier for i in scene.render.image_settings.bl_rna.properties['file_format'].enum_items]
scene.render.image_settings.file_format=next(f for f in formats if f=='PNG')
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.filepath=ROOT+'/Verification/Tabletop/BlenderAssetSheet.png'
bpy.data.libraries.write(ROOT+'/ArtSource/StoneSignal_Tabletop.blend',{scene},fake_user=False,compress=True)
print('TABLETOP GENERATED:',len(models),'meshes;',sum(m['triangles'] for m in manifest['models']),'source triangles')
