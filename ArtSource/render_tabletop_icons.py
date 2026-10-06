"""Run after create_tabletop.py in the same Blender Python environment."""
for o in scene.objects:
    if o.type=='MESH':o.hide_render=True
scene.render.film_transparent=True;scene.render.image_settings.color_mode='RGBA'
scene.render.resolution_x=256;scene.render.resolution_y=256
camera.location=(2.5,-4,3.4);camera.rotation_euler=(Vector((0,0,.65))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=1.95
for name in ['NeedleBeacon','PulseBeacon','SeismicBeacon','ChillBeacon']:
    o=models[name];o.hide_render=False;scene.render.filepath=ROOT+'/Assets/Game/Art/Icons/'+name+'.png'
    bpy.ops.render.render(write_still=True);o.hide_render=True
# Keep existing 3D block icons available in assets; runtime hand uses actual data cells in white.
shapes={'I':[(0,0),(0,1),(0,2),(0,3)],'O':[(0,0),(1,0),(0,1),(1,1)],'L':[(0,0),(0,1),(0,2),(1,0)],'T':[(0,0),(1,0),(2,0),(1,1)],'S':[(0,0),(1,0),(1,1),(2,1)]}
for name,cells in shapes.items():
    copies=[];cx=max(x for x,y in cells)/2;cy=max(y for x,y in cells)/2
    for x,y in cells:
        o=models['Wall'].copy();scene.collection.objects.link(o);o.hide_render=False;o.location=((x-cx)*.95,(y-cy)*.95,0);copies.append(o)
    camera.data.ortho_scale=4.1;camera.location=(4,-6,6);camera.rotation_euler=(Vector((0,0,.3))-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=ROOT+'/Assets/Game/Art/Icons/Block'+name+'.png';bpy.ops.render.render(write_still=True)
    for o in copies:bpy.data.objects.remove(o,do_unlink=True)
for o in scene.objects:
    if o.name.startswith('Tabletop preview '):o.hide_render=False
scene.render.film_transparent=False;scene.render.resolution_x=1400;scene.render.resolution_y=1000
camera.location=(11,-14,19);camera.rotation_euler=(Vector((4.2,3.2,.25))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=13.5
scene.render.filepath=ROOT+'/Verification/Tabletop/BlenderAssetSheet.png';bpy.ops.render.render(write_still=True)
bpy.data.libraries.write(ROOT+'/ArtSource/StoneSignal_Tabletop.blend',{scene},fake_user=False,compress=True)
print('TABLETOP ICONS / SHEET SAVED')
