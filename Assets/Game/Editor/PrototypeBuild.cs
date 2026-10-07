using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StoneSignal;

public static class PrototypeBuild
{
    private const string Root="Assets/Game/";
    private const string ScenePath=Root+"Scenes/Game.unity";
    private static T Asset<T>(string path,Action<T> initialize) where T:ScriptableObject
    {
        T existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing!=null) return existing;
        T data=ScriptableObject.CreateInstance<T>(); initialize(data); AssetDatabase.CreateAsset(data,path); return data;
    }
    private static Material Material(string name,Color color,bool unlit=false)
    {
        string path=Root+"Materials/"+name+".mat";
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path); if(existing!=null) return existing;
        var material=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit"));
        material.name=name; material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",.18f);
        AssetDatabase.CreateAsset(material,path); return material;
    }
    [MenuItem("StoneSignal/Create or open game scene")]
    public static void Create()
    {
        string[] folders={"Art","Materials","Prefabs","Scenes","Settings","ScriptableObjects/Blocks","ScriptableObjects/Towers","ScriptableObjects/Enemies","ScriptableObjects/Waves","ScriptableObjects/Rewards"};
        foreach(string folder in folders) Directory.CreateDirectory(Root+folder);
        AssetDatabase.Refresh();
        var renderer=Asset<UniversalRendererData>(Root+"Settings/ForwardRenderer.asset",r=>ResourceReloader.ReloadAllNullIn(r,UniversalRenderPipelineAsset.packagePath));
        string pipelinePath=Root+"Settings/StoneSignalURP.asset";
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if(pipeline==null)
        {
            pipeline=UniversalRenderPipelineAsset.Create(renderer); pipeline.supportsHDR=false; pipeline.msaaSampleCount=2; pipeline.shadowDistance=35;
            pipeline.supportsCameraDepthTexture=false; pipeline.supportsCameraOpaqueTexture=false;
            var serializedPipeline=new SerializedObject(pipeline);
            serializedPipeline.FindProperty("m_AdditionalLightsRenderingMode").intValue=(int)LightRenderingMode.Disabled;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(pipeline,pipelinePath);
        }
        GraphicsSettings.defaultRenderPipeline=pipeline;
        for(int i=0;i<QualitySettings.names.Length;i++) { QualitySettings.SetQualityLevel(i,false); QualitySettings.renderPipeline=pipeline; }
        QualitySettings.SetQualityLevel(0,false); QualitySettings.vSyncCount=1;
        var palette=Asset<VisualPalette>(Root+"Settings/Palette.asset",p=>
        {
            p.tileA=Material("Slate A",new Color(.15f,.24f,.28f)); p.tileB=Material("Slate B",new Color(.19f,.3f,.33f));
            p.wall=Material("Stone wall",new Color(.47f,.57f,.58f)); p.spawn=Material("Entry",new Color(.11f,.83f,.75f)); p.goal=Material("Signal core",new Color(1,.65f,.25f));
            p.valid=Material("Valid ghost",new Color(.2f,.95f,.55f),true); p.invalid=Material("Invalid ghost",new Color(.98f,.24f,.3f),true);
            p.path=Material("Route",new Color(.38f,.85f,.94f),true); p.projectile=Material("Bolt",new Color(1,.88f,.46f),true);
            p.enemy=Material("Drifter",new Color(.92f,.34f,.31f)); p.fastEnemy=Material("Skimmer",new Color(.95f,.5f,.82f)); p.towerBase=Material("Beacon plinth",new Color(.15f,.21f,.26f));
            p.arrow=Material("Needle",new Color(.23f,.83f,.68f)); p.rapid=Material("Pulse",new Color(.31f,.65f,1)); p.cannon=Material("Seismic",new Color(1,.69f,.26f));
            var particle=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); particle.name="Impact particles"; particle.SetColor("_BaseColor",Color.white);
            AssetDatabase.CreateAsset(particle,Root+"Materials/Impact particles.mat"); p.particle=particle;
        });
        string[] names={"I","O","L","T","S"};
        Vector2Int[][] cells={
            new[]{new Vector2Int(0,0),new Vector2Int(0,1),new Vector2Int(0,2),new Vector2Int(0,3)},
            new[]{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(0,1),new Vector2Int(1,1)},
            new[]{new Vector2Int(0,0),new Vector2Int(0,1),new Vector2Int(0,2),new Vector2Int(1,0)},
            new[]{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(2,0),new Vector2Int(1,1)},
            new[]{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(1,1),new Vector2Int(2,1)}
        };
        var blocks=new BlockShapeData[5];
        for(int i=0;i<5;i++) { int index=i; blocks[i]=Asset<BlockShapeData>(Root+"ScriptableObjects/Blocks/"+names[i]+".asset",s=>{s.displayName=names[index];s.cells=cells[index];}); }
        var normal=Asset<EnemyData>(Root+"ScriptableObjects/Enemies/Drifter.asset",e=>{e.displayName="Drifter";e.hp=32;e.moveSpeed=1.25f;e.reward=12;e.damageToBase=2;});
        var fast=Asset<EnemyData>(Root+"ScriptableObjects/Enemies/Skimmer.asset",e=>{e.displayName="Skimmer";e.hp=22;e.moveSpeed=2.3f;e.reward=16;e.damageToBase=3;e.fast=true;e.kind=EnemyKind.Fast;});
        var tank=Asset<EnemyData>(Root+"ScriptableObjects/Enemies/Bulwark.asset",e=>{e.displayName="Bulwark";e.kind=EnemyKind.Tank;e.hp=110;e.moveSpeed=.75f;e.reward=25;e.damageToBase=5;});
        var shard=Asset<EnemyData>(Root+"ScriptableObjects/Enemies/Shard.asset",e=>{e.displayName="Shard";e.kind=EnemyKind.Fast;e.fast=true;e.hp=12;e.moveSpeed=1.8f;e.reward=4;e.damageToBase=1;});
        var splitter=Asset<EnemyData>(Root+"ScriptableObjects/Enemies/Splitter.asset",e=>{e.displayName="Splitter";e.kind=EnemyKind.Splitter;e.hp=42;e.moveSpeed=1.1f;e.reward=14;e.damageToBase=3;e.splitCount=2;e.splitChild=shard;});
        var towers=new TowerData[4];
        towers[0]=Asset<TowerData>(Root+"ScriptableObjects/Towers/Needle.asset",t=>{t.displayName="Needle Beacon";t.kind=TowerKind.Arrow;t.cost=50;t.damage=12;t.attacksPerSecond=1.15f;t.range=4.4f;t.projectileSpeed=13;t.role="Focused single target DPS.";});
        towers[1]=Asset<TowerData>(Root+"ScriptableObjects/Towers/Pulse.asset",t=>{t.displayName="Pulse Beacon";t.kind=TowerKind.Rapid;t.cost=70;t.damage=4;t.attacksPerSecond=3.3f;t.range=3.6f;t.projectileSpeed=16;t.splashRadius=.65f;t.role="Rapid small-area shots clear Shards.";});
        towers[2]=Asset<TowerData>(Root+"ScriptableObjects/Towers/Seismic.asset",t=>{t.displayName="Seismic Beacon";t.kind=TowerKind.Cannon;t.cost=100;t.damage=30;t.attacksPerSecond=.55f;t.range=4.6f;t.projectileSpeed=9;t.splashRadius=1.35f;t.role="Heavy area burst. Pair with Chill.";});
        towers[3]=Asset<TowerData>(Root+"ScriptableObjects/Towers/Chill.asset",t=>{t.displayName="Chill Beacon";t.kind=TowerKind.Chill;t.cost=80;t.damage=3;t.attacksPerSecond=1.4f;t.range=4;t.projectileSpeed=12;t.slowFraction=.35f;t.slowDuration=2;t.role="Slow 35% for 2s. Combine with Seismic.";});
        var waves=new WaveData[3];
        waves[0]=Asset<WaveData>(Root+"ScriptableObjects/Waves/Wave01.asset",w=>w.groups=new[]{new EnemyGroup{enemy=normal,count=5}});
        waves[1]=Asset<WaveData>(Root+"ScriptableObjects/Waves/Wave02.asset",w=>w.groups=new[]{new EnemyGroup{enemy=normal,count=5},new EnemyGroup{enemy=fast,count=3}});
        waves[2]=Asset<WaveData>(Root+"ScriptableObjects/Waves/Wave03.asset",w=>w.groups=new[]{new EnemyGroup{enemy=normal,count=6},new EnemyGroup{enemy=fast,count=2},new EnemyGroup{enemy=tank,count=2},new EnemyGroup{enemy=splitter,count=2}});
        RewardEffect[] effects={RewardEffect.AllDamage,RewardEffect.AllAttackSpeed,RewardEffect.AllRange,RewardEffect.BaseHP,RewardEffect.CannonRadius,RewardEffect.AddBlock,RewardEffect.NextDraw,RewardEffect.PathSlow,RewardEffect.BonusSlot,RewardEffect.KillGold,RewardEffect.WaveGold,RewardEffect.TowerDiscount,RewardEffect.WaveHeal,RewardEffect.ExtraDraw};
        float[] amounts={.1f,.12f,.15f,10,.2f,1,1,.08f,1,.2f,30,.1f,2,1};
        string[] descriptions={"All tower damage +10%","All fire rate +12%","All range +15%","Core HP +10","Seismic blast radius +20%","Add 1 O wall card to the deck","Next draw has at least 1 rune card","Path enemy speed -8%","L gains safe adjacent platform","Kill gold +20%","Wave start gold +30","Tower costs -10%","End wave heal +2","2nd draw of the wave needs no ad"};
        var rewards=new RewardData[effects.Length];
        for(int i=0;i<effects.Length;i++) { int index=i; rewards[i]=Asset<RewardData>(Root+"ScriptableObjects/Rewards/"+effects[i]+".asset",r=>{r.displayName=effects[index].ToString();r.description=descriptions[index];r.effectText=descriptions[index];r.effect=effects[index];r.amount=amounts[index];if(r.effect==RewardEffect.AddBlock)r.blockShape=blocks[1];if(r.effect==RewardEffect.BonusSlot)r.blockShape=blocks[2];}); }
        var config=Asset<GameConfig>(Root+"Settings/GameConfig.asset",c=>{c.blocks=blocks;c.towers=towers;c.waves=waves;c.rewards=rewards;c.palette=palette;});
        if(!File.Exists(ScenePath))
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var map=new GameObject("Grid 16 x 10").AddComponent<GridManager>(); map.transform.position=new Vector3(-8,0,-5);
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera"; camera.orthographic=true; camera.orthographicSize=7;
            camera.transform.position=new Vector3(1.7f,15,-12); camera.transform.LookAt(new Vector3(1.7f,0,0)); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.045f,.07f,.095f); camera.nearClipPlane=.1f; camera.farClipPlane=80;
            camera.gameObject.AddComponent<AudioListener>(); camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var light=new GameObject("Directional Light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.5f; light.color=new Color(.91f,.96f,1); light.transform.rotation=Quaternion.Euler(50,-35,0); light.shadows=LightShadows.Soft;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.5f,.57f,.63f);
            var bootstrap=new GameObject("StoneSignal bootstrap").AddComponent<GameBootstrap>(); bootstrap.config=config; bootstrap.grid=map; bootstrap.viewCamera=camera;
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        else EditorSceneManager.OpenScene(ScenePath);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        PlayerSettings.companyName="Independent Prototype"; PlayerSettings.productName="StoneSignal"; PlayerSettings.defaultScreenWidth=1600; PlayerSettings.defaultScreenHeight=900; PlayerSettings.fullScreenMode=FullScreenMode.Windowed; PlayerSettings.resizableWindow=true;
        PlayerSettings.colorSpace=ColorSpace.Linear;
        AssetDatabase.SaveAssets();
        EnsureRewardPool();
        // Reopen after first-time asset imports so saved GUID references are resolved.
        EditorSceneManager.OpenScene(ScenePath);
        ValidateScene();
        Debug.Log("SCENE READY: "+ScenePath+"; URP "+pipeline.name);
    }
    [MenuItem("StoneSignal/Run core checks")]
    public static void Checks() { PrototypeChecks.RewardChecks(); ValidateScene(); }
    /// v18 玩法策划: 免广告再抽 (ExtraDraw, rarity 精良) joins the reward pool. Existing projects: creates Rewards/ExtraDraw.asset if missing
    /// and appends it to GameConfig.rewards (14 rewards). Called before the stage-2 checks and after Create().
    public static void EnsureRewardPool()
    {
        var config=AssetDatabase.LoadAssetAtPath<GameConfig>(Root+"Settings/GameConfig.asset"); if(config==null) return;
        var extra=Asset<RewardData>(Root+"ScriptableObjects/Rewards/ExtraDraw.asset",r=>{r.displayName="Ad-free Draw";r.description="2nd draw of the wave needs no ad";r.effectText="FREE 2ND DRAW";r.effect=RewardEffect.ExtraDraw;r.amount=1;});
        if(Array.IndexOf(config.rewards,extra)>=0) return;
        var list=new System.Collections.Generic.List<RewardData>(config.rewards){extra}; config.rewards=list.ToArray();
        EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); Debug.Log("REWARD POOL: ExtraDraw added ("+config.rewards.Length+" rewards)");
    }
    private static void ValidateScene()
    {
        var bootstrap=UnityEngine.Object.FindObjectOfType<GameBootstrap>();
        PrototypeChecks.Require(bootstrap!=null,"Scene bootstrap component");
        PrototypeChecks.Require(bootstrap.config!=null,"Scene configuration asset");
        PrototypeChecks.Require(bootstrap.grid!=null,"Scene grid reference");
        PrototypeChecks.Require(bootstrap.viewCamera!=null,"Scene camera reference");
        var config=bootstrap.config;
        PrototypeChecks.Require(config.blocks.Length==5 && config.towers.Length==4 && config.rewards.Length==14 && config.waves.Length==3,"All configuration assets");
        foreach(var wave in config.waves) foreach(var group in wave.groups) PrototypeChecks.Require(group.enemy!=null && group.count>0,"Wave enemy reference");
        foreach(GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach(Transform child in root.GetComponentsInChildren<Transform>(true)) PrototypeChecks.Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject)==0,"No missing scene scripts");
        PrototypeChecks.Require(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset,"URP configured");
        Debug.Log("SCENE REFERENCES PASS");
    }
    public static void Build()
    {
        Create(); Checks();
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../StoneSignal-PC/StoneSignal.exe")); Directory.CreateDirectory(Path.GetDirectoryName(output));
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Player build failed: "+result.summary.totalErrors+" errors");
        Debug.Log("PLAYER BUILD PASS: "+output);
    }
    public static void OpenGame() { EditorSceneManager.OpenScene(ScenePath); }
}
