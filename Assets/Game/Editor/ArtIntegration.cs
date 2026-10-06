using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StoneSignal;

public static class ArtIntegration
{
    private const string Root = "Assets/Game/";
    [Serializable] private class Manifest { public MaterialEntry[] materials; public ModelEntry[] models; }
    [Serializable] private class MaterialEntry { public string name; public float[] color; public float metallic, roughness, emission; }
    [Serializable] private class ModelEntry { public string name; public int triangles; public string[] materials; }
    private static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/Art/"+name+".prefab");
    private static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Root+"Art/Icons/"+name+".png");

    [MenuItem("StoneSignal/Import Blender art pack")]
    public static void Import()
    {
        Directory.CreateDirectory(Root+"Materials/Art"); Directory.CreateDirectory(Root+"Prefabs/Art");
        AssetDatabase.Refresh();
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText("ArtSource/art_manifest.json"));
        var materials=new Dictionary<string,Material>();
        foreach(var entry in manifest.materials)
        {
            string path=Root+"Materials/Art/"+entry.name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) { mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,path); }
            var color=new Color(entry.color[0],entry.color[1],entry.color[2]);
            mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",entry.metallic);mat.SetFloat("_Smoothness",1-entry.roughness);
            mat.SetColor("_EmissionColor",color*entry.emission);
            if(entry.emission>0) mat.EnableKeyword("_EMISSION");else mat.DisableKeyword("_EMISSION");
            mat.enableInstancing=true; EditorUtility.SetDirty(mat);materials.Add(entry.name,mat);
        }
        foreach(var entry in manifest.models)
        {
            string path=Root+"Art/Models/"+entry.name+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale=1; importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;
            importer.addCollider=false;importer.isReadable=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            foreach(string name in entry.materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),materials[name]);
            importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            // Keep FBX axis/unit correction on a child; gameplay rotates/scales only the identity wrapper.
            var obj=new GameObject(entry.name);
            var mesh=UnityEngine.Object.Instantiate(model);mesh.name="Mesh";mesh.transform.SetParent(obj.transform,false);
            PrefabUtility.SaveAsPrefabAsset(obj,Root+"Prefabs/Art/"+entry.name+".prefab");UnityEngine.Object.DestroyImmediate(obj);
        }
        foreach(string path in Directory.GetFiles(Root+"Art/Icons","*.png"))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=256;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        string artPath=Root+"Settings/ArtCatalog.asset";
        var art=AssetDatabase.LoadAssetAtPath<ArtCatalog>(artPath);
        if(art==null) { art=ScriptableObject.CreateInstance<ArtCatalog>();AssetDatabase.CreateAsset(art,artPath); }
        art.tileA=Prefab("TileA");art.tileB=Prefab("TileB");art.wall=Prefab("Wall");art.cliff=Prefab("Cliff");art.foliage=Prefab("Foliage");
        art.ruinPillar=Prefab("RuinPillar");art.brazier=Prefab("Brazier");art.spawnPortal=Prefab("SpawnPortal");art.signalCore=Prefab("SignalCore");
        string backdropPath=Root+"Materials/Art/Backdrop.mat";
        art.backdrop=AssetDatabase.LoadAssetAtPath<Material>(backdropPath);
        if(art.backdrop==null) { art.backdrop=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(art.backdrop,backdropPath); }
        art.backdrop.SetColor("_BaseColor",new Color(.018f,.105f,.15f));EditorUtility.SetDirty(art.backdrop);EditorUtility.SetDirty(art);
        var config=AssetDatabase.LoadAssetAtPath<GameConfig>(Root+"Settings/GameConfig.asset");config.palette.art=art;EditorUtility.SetDirty(config.palette);
        string[] towers={"NeedleBeacon","PulseBeacon","SeismicBeacon","ChillBeacon"};
        for(int i=0;i<config.towers.Length;i++) { var tower=config.towers[i];tower.visualPrefab=Prefab(towers[i]);tower.icon=Icon(towers[i]);EditorUtility.SetDirty(tower); }
        foreach(string name in new[]{"Drifter","Skimmer","Bulwark","Splitter","Shard"})
        { var enemy=AssetDatabase.LoadAssetAtPath<EnemyData>(Root+"ScriptableObjects/Enemies/"+name+".asset");enemy.visualPrefab=Prefab(name);EditorUtility.SetDirty(enemy); }
        foreach(var shape in config.blocks) { shape.icon=Icon("Block"+shape.displayName);EditorUtility.SetDirty(shape); }
        ConfigureScene();AssetDatabase.SaveAssets();Checks();
        Debug.Log("BLENDER ART IMPORT PASS: "+manifest.models.Length+" prefabs, "+manifest.materials.Length+" shared URP materials, nine icons");
    }

    private static void ConfigureScene()
    {
        var scene=EditorSceneManager.OpenScene(Root+"Scenes/Game.unity");
        var bootstrap=UnityEngine.Object.FindObjectOfType<GameBootstrap>();var camera=bootstrap.viewCamera;
        camera.orthographicSize=8.8f;camera.transform.position=new Vector3(-3.5f,16.9f,-18);camera.transform.LookAt(new Vector3(2.5f,-1.1f,0));
        camera.backgroundColor=new Color(.015f,.045f,.075f);camera.allowHDR=true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var pipeline=GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if(pipeline!=null) {pipeline.supportsHDR=true;EditorUtility.SetDirty(pipeline);}
        foreach(var light in UnityEngine.Object.FindObjectsOfType<Light>()) if(light.type==LightType.Directional)
        {light.color=new Color(1,.84f,.66f);light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(48,-32,0);light.shadows=LightShadows.Soft;}
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.39f,.51f,.62f);
        string profilePath=Root+"Settings/RuinPostProcessing.asset";
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if(profile==null) {profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
        if(!profile.TryGet<Bloom>(out var bloom)) {bloom=profile.Add<Bloom>();AssetDatabase.AddObjectToAsset(bloom,profile);}
        bloom.intensity.Override(.3f);bloom.threshold.Override(1.2f);bloom.scatter.Override(.55f);EditorUtility.SetDirty(bloom);
        if(!profile.TryGet<Tonemapping>(out var tone)) {tone=profile.Add<Tonemapping>();AssetDatabase.AddObjectToAsset(tone,profile);}
        tone.mode.Override(TonemappingMode.ACES);EditorUtility.SetDirty(tone);EditorUtility.SetDirty(profile);
        var volume=UnityEngine.Object.FindObjectOfType<Volume>();
        if(volume==null) volume=new GameObject("Ruin atmosphere").AddComponent<Volume>();
        volume.isGlobal=true;volume.sharedProfile=profile;volume.priority=1;
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("StoneSignal/Check art pack")]
    public static void Checks()
    {
        var config=AssetDatabase.LoadAssetAtPath<GameConfig>(Root+"Settings/GameConfig.asset");
        PrototypeChecks.Require(config.palette.art!=null,"Art catalog assigned");
        foreach(var tower in config.towers) PrototypeChecks.Require(tower.visualPrefab!=null && tower.icon!=null,"Tower prefab and icon");
        foreach(var shape in config.blocks) PrototypeChecks.Require(shape.icon!=null,"Block icon");
        foreach(string path in Directory.GetFiles(Root+"Prefabs/Art","*.prefab"))
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\','/'));
            PrototypeChecks.Require(prefab.GetComponentsInChildren<Collider>().Length==0,"Art has no gameplay colliders");
            var renderers=prefab.GetComponentsInChildren<Renderer>();PrototypeChecks.Require(renderers.Length>0,"Prefab has mesh");
            var bounds=renderers[0].bounds;
            float extent=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
            PrototypeChecks.Require(extent>.2f && extent<4,"Imported metre scale");
            PrototypeChecks.Require(prefab.transform.localScale==Vector3.one && prefab.transform.localRotation==Quaternion.identity,"Prefab identity wrapper");
            foreach(var r in renderers) foreach(var mat in r.sharedMaterials)
                PrototypeChecks.Require(mat!=null && mat.shader.name.StartsWith("Universal Render Pipeline/"),"URP material remapped");
        }
        Debug.Log("ART REFERENCES PASS");
    }
    public static void Build() { Import();Stage2Validation.Run();PrototypeBuild.Build(); }
}
