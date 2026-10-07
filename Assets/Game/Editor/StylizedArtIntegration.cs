using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using StoneSignal;

/// <summary>
/// Stylized art batch 1 (original, procedural Blender art per Docs/ArtSpec/美术规范.md).
/// Source: ArtSource/Stylized/build_stylized_batch1.py -> Assets/Game/Art/Stylized/**.fbx.
/// This tool only builds materials, prefabs and a separate demo scene; it does NOT touch
/// Game.unity, ArtCatalog or any gameplay data, so the current Kenney/Quaternius runtime is unchanged.
/// Idempotent.
/// </summary>
public static class StylizedArtIntegration
{
    public const string ArtDir = "Assets/Game/Art/Stylized/";
    private const string MatDir = "Assets/Game/Materials/Stylized/";
    internal const string PrefabDir = "Assets/Game/Prefabs/Stylized/";
    private const string ScenePath = "Assets/Game/Scenes/StylizedArtDemo.unity";
    private const string RampPath = ArtDir + "Textures/T_Ramp_Toon_3Step.png";
    private const string PalettePath = ArtDir + "Textures/T_Env_Palette_D.png";
    private const string PreviewDir = "Verification/StylizedArt1/";

    // name -> (folder, prefab name, material key)
    private static readonly (string folder, string fbx, string prefab, string mat)[] Assets =
    {
        ("Environment", "SM_Env_Tile_Stone_A_01", "PF_Env_Tile_Stone_A", "Env"),
        ("Environment", "SM_Env_Tile_Stone_B_01", "PF_Env_Tile_Stone_B", "Env"),
        ("Environment", "SM_Env_Rock_1x1_01", "PF_Env_Rock_1x1", "Block"),
        ("Environment", "SM_Env_Rock_TetrisI_01", "PF_Env_Rock_TetrisI", "Block"),
        ("Environment", "SM_Env_Rock_TetrisO_01", "PF_Env_Rock_TetrisO", "Block"),
        ("Environment", "SM_Env_Rock_TetrisT_01", "PF_Env_Rock_TetrisT", "Block"),
        ("Environment", "SM_Env_Rock_TetrisS_01", "PF_Env_Rock_TetrisS", "Block"),
        ("Environment", "SM_Env_Rock_TetrisZ_01", "PF_Env_Rock_TetrisZ", "Block"),
        ("Environment", "SM_Env_Rock_TetrisL_01", "PF_Env_Rock_TetrisL", "Block"),
        ("Environment", "SM_Env_Rock_TetrisJ_01", "PF_Env_Rock_TetrisJ", "Block"),
        ("Environment", "SM_Env_Tree_Maple_A_01", "PF_Env_Tree_Maple_A", "Foliage"),
        ("Environment", "SM_Env_Tree_Maple_B_01", "PF_Env_Tree_Maple_B", "Foliage"),
        ("Environment", "SM_Env_Tree_Maple_C_01", "PF_Env_Tree_Maple_C", "Foliage"),
        ("Environment", "SM_Env_Rock_Small_01", "PF_Env_Rock_Small", "Env"),
        ("Environment", "SM_Prop_Barrel_01", "PF_Prop_Barrel", "Env"),
        ("Environment", "SM_Prop_Crate_01", "PF_Prop_Crate", "Env"),
        ("Environment", "SM_Env_Grass_Tuft_01", "PF_Env_Grass_Tuft", "Grass"),
        ("Towers", "SM_Tower_Cannon_1x1_01", "PF_Tower_Cannon_1x1", "Tower"),
        ("Towers", "SM_Tower_Gatling_1x1_01", "PF_Tower_Gatling_1x1", "Tower"),
        ("Towers", "SM_Tower_Tesla_1x1_01", "PF_Tower_Tesla_1x1", "Tower"),
        ("Towers", "SM_Tower_Frost_1x1_01", "PF_Tower_Frost_1x1", "Tower"),
        ("Towers", "SM_Tower_Flamer_1x2_01", "PF_Tower_Flamer_1x2", "Tower"),
        ("Towers", "SM_Tower_Mortar_2x2_01", "PF_Tower_Mortar_2x2", "Tower"),
        ("Enemies", "SM_Enemy_Drifter_01", "PF_Enemy_Drifter", "Enemy"),
        ("Enemies", "SM_Enemy_Skimmer_01", "PF_Enemy_Skimmer", "Enemy"),
        ("Enemies", "SM_Enemy_Bulwark_01", "PF_Enemy_Bulwark", "Enemy"),
        ("Enemies", "SM_Enemy_Flyer_01", "PF_Enemy_Flyer", "Enemy"),
        ("Enemies", "SM_Enemy_Shard_01", "PF_Enemy_Shard", "Shard"),
        ("Enemies", "SM_Enemy_Boss_01", "PF_Enemy_Boss", "Enemy"),
        ("Environment", "SM_Env_Tile_Stone_C_01", "PF_Env_Tile_Stone_C", "Env"),
        ("Environment", "SM_Env_Tile_Dirt_01", "PF_Env_Tile_Dirt", "Env"),
        ("Environment", "SM_Env_Island_Cliff_4x4_01", "PF_Env_Island_Cliff_4x4", "Env"),
        ("Environment", "SM_Env_Board_Cliff_16x12_01", "PF_Env_Board_Cliff_16x12", "Env"),
        ("Environment", "SM_Env_Bridge_Plank_01", "PF_Env_Bridge_Plank", "Env"),
        ("Environment", "SM_Env_Dock_Post_01", "PF_Env_Dock_Post", "Env"),
        ("Environment", "SM_Prop_Brazier_01", "PF_Prop_Brazier", "Env"),
        ("Environment", "SM_Prop_Campfire_01", "PF_Prop_Campfire", "Env"),
        ("Environment", "SM_Prop_Lantern_01", "PF_Prop_Lantern", "Env"),
        ("Environment", "SM_Env_RockPile_01", "PF_Env_RockPile", "Env"),
        ("Environment", "SM_Env_Stump_01", "PF_Env_Stump", "Env"),
        ("Environment", "SM_Env_Log_01", "PF_Env_Log", "Env"),
        ("Environment", "SM_Env_Leaves_01", "PF_Env_Leaves", "Grass"),
        ("Environment", "SM_Env_FoamRing_01", "PF_Env_FoamRing", "Grass"),
        ("Towers", "SM_Prop_Core_01", "PF_Prop_Core", "Tower"),
        ("Enemies", "SM_Enemy_Splitter_01", "PF_Enemy_Splitter", "Enemy"),
    };

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    [MenuItem("StoneSignal/Stylized art/Import batch 1")]
    public static void Import()
    {
        foreach (var d in new[] { MatDir, PrefabDir }) Directory.CreateDirectory(d);
        Directory.CreateDirectory(PreviewDir);
        if (!File.Exists(RampPath) && File.Exists("Docs/ArtSpec/美术规范_assets/T_Ramp_Toon_3Step.png"))
            File.Copy("Docs/ArtSpec/美术规范_assets/T_Ramp_Toon_3Step.png", RampPath);
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ArtDir.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        var mats = BuildMaterials();
        foreach (var a in Assets) BuildPrefab(a, mats[a.mat]);
        AssetDatabase.SaveAssets();
        BuildDemoScene(mats);
        Debug.Log("STYLIZED ART IMPORT DONE");
    }

    static Material Mat(string name, bool outline)
    {
        string path = MatDir + name + ".mat";
        var shader = Shader.Find(outline ? "StoneSignal/ToonLitOutline" : "StoneSignal/ToonLit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath));
        m.SetTexture("_RampTex", AssetDatabase.LoadAssetAtPath<Texture2D>(RampPath));
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Dictionary<string, Material> BuildMaterials()
    {
        var env = Mat("M_Env_Palette", false);
        env.SetFloat("_ShadowSmooth", .02f); env.SetFloat("_RimIntensity", .25f);
        var fol = Mat("M_Foliage", false);
        fol.SetColor("_ShadowColor", Hex("7A3050")); fol.SetFloat("_RimIntensity", .45f);
        fol.SetFloat("_LayerTint", 1); fol.SetFloat("_WindStrength", 1);
        // LayerTint multiplies authored colours; keep it gentle so palette HEX stays readable.
        fol.SetColor("_InnerTint", Color.Lerp(Color.white, Hex("B07090"), .45f));
        fol.SetColor("_OuterTint", Color.Lerp(Color.white, Hex("FFF0C8"), .6f));
        var grass = Mat("M_Env_Grass", false);
        grass.SetFloat("_WindStrength", .6f); grass.SetFloat("_RimIntensity", .3f);
        var tower = Mat("M_Tower_Cannon", true);
        tower.SetFloat("_OutlineWidthPx", 4); tower.SetFloat("_OutlineZOffset", .0006f); tower.SetColor("_OutlineColor", Hex("1E1A3A"));
        tower.SetFloat("_ShadowSmooth", .04f); tower.SetFloat("_RimIntensity", .4f);
        var enemy = Mat("M_Enemy", true);
        enemy.SetFloat("_OutlineWidthPx", 3); enemy.SetFloat("_OutlineZOffset", .0004f); enemy.SetColor("_OutlineColor", Hex("3B0E1E"));
        enemy.SetFloat("_ShadowSmooth", .04f); enemy.SetFloat("_RimIntensity", .45f); enemy.SetFloat("_RimMin", .5f); enemy.SetFloat("_RimMax", .7f);
        var shard = Mat("M_Enemy_Shard", true);
        shard.CopyPropertiesFromMaterial(enemy); shard.SetFloat("_Wobble", 1); shard.SetFloat("_WobbleFreq", 6);
        var block = Mat("M_Env_Block", false);
        block.SetFloat("_ShadowSmooth", .02f); block.SetFloat("_RimIntensity", .35f);
        var cliff = Mat("M_Env_Island_Cliff", false);
        cliff.SetTexture("_BaseMap", null); cliff.SetColor("_BaseColor", Hex("874A4A")); cliff.SetFloat("_RimIntensity", .2f);
        string wp = MatDir + "M_Env_Water_Flat.mat";
        var water = AssetDatabase.LoadAssetAtPath<Material>(wp);
        if (water == null) { water = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(water, wp); }
        water.shader = Shader.Find("StoneSignal/Water"); water.renderQueue = 2950;
        water.SetColor("_ShallowColor", Hex("33C7C2")); water.SetColor("_DeepColor", Hex("0B4A9A")); water.SetFloat("_DepthRange", 4.5f); water.SetFloat("_CausticStrength", .45f); water.SetColor("_BaseColor", Hex("0754A0"));
        EditorUtility.SetDirty(water);
        block.SetFloat("_BaseAO", .5f); block.SetFloat("_BaseAOHeight", .45f); block.SetFloat("_Mottle", .35f);
        env.SetFloat("_Mottle", .18f); water.SetFloat("_FoamDepth", .22f); water.SetFloat("_RippleScale", 2.4f);
        env.SetFloat("_BaseAO", .2f); env.SetFloat("_BaseAOHeight", .22f);
        tower.SetFloat("_BaseAO", .3f); tower.SetFloat("_BaseAOHeight", .35f);
        foreach (var (n, tint) in new[] { ("M_Foliage_Warm", "FFC8A8"), ("M_Foliage_Gold", "FFF0A0"), ("M_Foliage_Deep", "E8A0A0") })
        {
            var fv = Mat(n, false); fv.CopyPropertiesFromMaterial(fol); fv.SetColor("_BaseColor", Hex(tint)); EditorUtility.SetDirty(fv);
        }
        return new Dictionary<string, Material> { ["Env"] = env, ["Foliage"] = fol, ["Grass"] = grass, ["Tower"] = tower,
            ["Enemy"] = enemy, ["Shard"] = shard, ["Block"] = block, ["Cliff"] = cliff, ["Water"] = water };
    }

    static void BuildPrefab((string folder, string fbx, string prefab, string mat) a, Material mat)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + a.folder + "/" + a.fbx + ".fbx");
        if (model == null) throw new Exception("Missing stylized FBX " + a.fbx);
        // Project contract (DESIGN): identity root -> Rig -> mesh; no gameplay colliders.
        var root = new GameObject(a.prefab);
        var rig = new GameObject("Rig"); rig.transform.SetParent(root.transform, false);
        var mesh = (GameObject)PrefabUtility.InstantiatePrefab(model);
        mesh.transform.SetParent(rig.transform, false);
        // Animated enemy FBX keep their converted root rotation (exported without baked space transform so the
        // walk clip stays valid); it sits under Rig, so the prefab root is still identity.
        foreach (var r in mesh.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            r.shadowCastingMode = a.mat == "Grass" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = false; }
        }
        if (a.folder == "Towers") RestructureTower(rig.transform, mesh, a.fbx);
        if (a.folder == "Enemies")
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(ArtDir + a.folder + "/" + a.fbx + ".fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).ToList();
            AnimationClip Clip(string n) => clips.FirstOrDefault(c => c.name == n);
            if (Clip(EnemyVisualContract.MoveClip) != null)
            {
                string ctrlPath = "Assets/Game/Animation/Stylized/AC_" + a.prefab.Substring(3) + ".controller";
                Directory.CreateDirectory("Assets/Game/Animation/Stylized");
                var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                ctrl.AddParameter(StoneSignal.VFX.EnemyAnimParams.MoveSpeed, AnimatorControllerParameterType.Float);
                var ps = ctrl.parameters; ps[0].defaultFloat = 1; ctrl.parameters = ps;
                ctrl.AddParameter(StoneSignal.VFX.EnemyAnimParams.Hit, AnimatorControllerParameterType.Trigger);
                ctrl.AddParameter(EnemyVisualContract.DieTrigger, AnimatorControllerParameterType.Trigger);
                var sm = ctrl.layers[0].stateMachine;
                var loco = sm.AddState(EnemyVisualContract.LocomotionState, new Vector3(300, 0));
                loco.motion = Clip(EnemyVisualContract.MoveClip); loco.speedParameterActive = true; loco.speedParameter = StoneSignal.VFX.EnemyAnimParams.MoveSpeed;
                sm.defaultState = loco;
                var hitClip = Clip(StoneSignal.VFX.EnemyAnimParams.HitClip);
                if (hitClip != null)
                {
                    var hit = sm.AddState(StoneSignal.VFX.EnemyAnimParams.HitState, new Vector3(300, 120)); hit.motion = hitClip;
                    var t1 = loco.AddTransition(hit); t1.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, StoneSignal.VFX.EnemyAnimParams.Hit);
                    t1.hasExitTime = false; t1.duration = .03f;
                    var t2 = hit.AddTransition(loco); t2.hasExitTime = true; t2.exitTime = .9f; t2.duration = .08f;
                    var t3 = hit.AddTransition(hit); t3.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, StoneSignal.VFX.EnemyAnimParams.Hit);
                    t3.hasExitTime = false; t3.duration = .02f;
                }
                var deathClip = Clip(EnemyVisualContract.DeathClip);
                if (deathClip != null)
                {
                    var death = sm.AddState(EnemyVisualContract.DeathState, new Vector3(560, 60)); death.motion = deathClip;
                    var td = sm.AddAnyStateTransition(death); td.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, EnemyVisualContract.DieTrigger);
                    td.hasExitTime = false; td.duration = .05f; td.canTransitionToSelf = false;
                }
                var anim = mesh.GetComponent<Animator>(); if (anim == null) anim = mesh.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            root.AddComponent<StoneSignal.VFX.EnemyHitFeedback>(); // visual-only: flash/squash/dissolve, no gameplay logic
        }
        PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + a.prefab + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    /// Tower contract for gameplay: PF_Tower_X > Rig > {fbx}_Base (static, grid aligned)
    ///                                              > {fbx}_Head (yaw pivot) > {fbx}_Barrel (pitch pivot, Cannon/Mortar) > {fbx}_Muzzle (+Z = fire dir)
    static void RestructureTower(Transform rig, GameObject mesh, string fbx)
    {
        PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Transform Find(string suf) => mesh.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith(suf));
        var head = Find("_Head"); var muzzle = Find("_Muzzle"); var baseT = Find("_Base");
        var barrel = Find("_Barrel");
        if (head != null && barrel != null) barrel.SetParent(head, true);
        if (muzzle != null) muzzle.SetParent(barrel != null ? barrel : head != null ? head : muzzle.parent, true);
        if (baseT == null) { baseT = mesh.transform; }
        if (head != null) head.SetParent(rig, true);
        if (baseT != mesh.transform) { baseT.SetParent(rig, true); if (mesh.transform.childCount == 0 && mesh.GetComponent<Renderer>() == null) UnityEngine.Object.DestroyImmediate(mesh); }
        baseT.name = fbx + "_Base";
        if (muzzle != null)
        {
            var dir = fbx.Contains("Mortar") ? new Vector3(0, Mathf.Cos(28 * Mathf.Deg2Rad), Mathf.Sin(28 * Mathf.Deg2Rad)) : fbx.Contains("Tesla") ? Vector3.up : Vector3.forward;
            muzzle.rotation = Quaternion.LookRotation(dir, dir == Vector3.up ? Vector3.forward : Vector3.up);
        }
    }

    static GameObject Place(string prefab, Transform parent, Vector3 pos, float yaw = 0, float scale = 1)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + prefab + ".prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static void BuildDemoScene(Dictionary<string, Material> mats)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Directional Light").AddComponent<Light>();
        // warm key light, cool sky fill (ambient trilight), reference golden-hour mood
        light.type = LightType.Directional; light.color = Hex("FFD3A0"); light.intensity = 1.45f; light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(48, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("86A6E8"); RenderSettings.ambientEquatorColor = Hex("A88C9C"); RenderSettings.ambientGroundColor = Hex("4A3A55");
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 45; RenderSettings.fogEndDistance = 90;
        RenderSettings.fogColor = Hex("6A8CC8");
        var cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("03357A"); cam.fieldOfView = 30;
        cam.transform.position = new Vector3(-6.5f, 13.5f, -14f); cam.transform.LookAt(new Vector3(0, .6f, 0));
        var camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true; camData.requiresDepthTexture = true; cam.allowHDR = true;
        BuildPostFx();

        var world = new GameObject("StylizedBatch1").transform;
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane); water.name = "Water";
        UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.SetParent(world); water.transform.localScale = Vector3.one * 8; water.GetComponent<Renderer>().sharedMaterial = mats["Water"];
        var cliff = GameObject.CreatePrimitive(PrimitiveType.Cube); cliff.name = "IslandCliff";
        UnityEngine.Object.DestroyImmediate(cliff.GetComponent<Collider>());
        cliff.transform.SetParent(world); cliff.transform.position = new Vector3(0, -.15f, 0); cliff.transform.localScale = new Vector3(8.3f, 1.4f, 6.3f);
        cliff.GetComponent<Renderer>().sharedMaterial = mats["Cliff"];
        // Full level diorama from the Blender layout (ArtSource/Stylized/level_layout.json), so Blender preview == Unity scene.
        UnityEngine.Object.DestroyImmediate(cliff);
        water.transform.localScale = Vector3.one * 10;
        var byFbx = Assets.ToDictionary(x => x.fbx, x => x.prefab);
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText("ArtSource/Stylized/level_layout.json"));
        foreach (var it in layout.items)
        {
            if (!byFbx.TryGetValue(it.asset, out var pf)) { Debug.LogWarning("Layout asset without prefab " + it.asset); continue; }
            bool tower = pf.StartsWith("PF_Tower");
            var go = Place(pf, world, new Vector3(-it.pos[0], it.pos[2], -it.pos[1]), tower ? 0 : -it.rotZ);
            if (it.rotX != 0 || it.rotY != 0) go.transform.rotation = Quaternion.Euler(-it.rotX, -it.rotZ, -it.rotY);
            go.transform.localScale = new Vector3(it.scale[0], it.scale[2], it.scale[1]);
            if (tower)  // base stays grid aligned; only the head yaws (and barrel pitches) - verifies the split
            {
                var head = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("_Head"));
                if (head) head.localRotation = Quaternion.Euler(0, -it.rotZ, 0) * head.localRotation;
                var barrel = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("_Barrel"));
                if (barrel) barrel.localRotation = barrel.localRotation * Quaternion.Euler(-8f, 0, 0);
            }
            // v7: no gold slot plates under turrets (turret plinth is stone/metal now)
            if (pf == "PF_Prop_Core") { SlotMarker(world, go.transform.position + Vector3.up * .02f, new Vector2(3.2f, 3.2f), Hex("5FD0FF"), 1); SlotMarker(world, go.transform.position + Vector3.up * .03f, new Vector2(2.2f, 2.2f), Hex("A8ECFF"), 1); }
            if (pf.Contains("Tree"))  // tint variation (material variants, serialized as prefab overrides)
            {
                int h = Mathf.Abs((int)(it.pos[0] * 73 + it.pos[1] * 131)) % 4;
                if (h > 0)
                {
                    var vm = AssetDatabase.LoadAssetAtPath<Material>(MatDir + new[] { "", "M_Foliage_Warm", "M_Foliage_Gold", "M_Foliage_Deep" }[h] + ".mat");
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(vm, r.sharedMaterials.Length).ToArray();
                }
            }
        }
        // snow (spec 8.x weather): soft white flakes over the whole level
        var snow = new GameObject("FX_Weather_Snow").AddComponent<ParticleSystem>();
        snow.transform.SetParent(world); snow.transform.position = new Vector3(0, 9, 0); snow.transform.rotation = Quaternion.Euler(90, 0, 0);
        var main = snow.main; main.loop = true; main.prewarm = true; main.startLifetime = 9; main.startSpeed = .9f; main.startSize = .035f;
        main.startColor = Hex("F4F8FF"); main.maxParticles = 1500; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = snow.emission; em.rateOverTime = 40;
        var sh = snow.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(36, 26, 1);
        var noise = snow.noise; noise.enabled = true; noise.strength = .4f; noise.frequency = .3f;
        string smp = MatDir + "M_FX_Snow.mat";
        var smat = AssetDatabase.LoadAssetAtPath<Material>(smp);
        if (smat == null) { smat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(smat, smp); }
        snow.GetComponent<ParticleSystemRenderer>().sharedMaterial = smat;
        snow.Simulate(8f, true, true);
        cam.fieldOfView = 32;
        cam.transform.position = new Vector3(9f, 21f, 25f); cam.transform.LookAt(new Vector3(0, .5f, .5f));
        EditorSceneManager.SaveScene(scene, ScenePath);
        foreach (var old in Directory.GetFiles(PreviewDir, "unity_demo*.png").Concat(Directory.GetFiles(PreviewDir, "level_*_v7.png"))) File.Delete(old);
        Capture(cam, PreviewDir + "unity_demo.png");
        var ls = light.shadows; light.shadows = LightShadows.None;
        Capture(cam, PreviewDir + "unity_demo_noshadow.png");
        light.shadows = ls;
        // Game.unity Main Camera (read-only copy): orthographic size 9.2, same rotation, same offset to the board centre
        var gRot = new Quaternion(0.3691325f, 0.13881999f, -0.05586581f, 0.91725093f); var gPos = new Vector3(-3.2f, 17.4f, -18.4f);
        var fwd = gRot * Vector3.forward; float tHit = (.8f - gPos.y) / fwd.y;
        var wide = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
        cam.orthographic = true; cam.orthographicSize = GameCamOrthoV7;  // tighter framing so the board fills the screen cam.transform.rotation = gRot; cam.transform.position = new Vector3(0, .8f, 0) - fwd * tHit;
        cam.farClipPlane = 200;
        Capture(cam, PreviewDir + "level_gamecam_v7.png");
        cam.orthographic = false; cam.transform.SetPositionAndRotation(wide.Item1, wide.Item2); cam.fieldOfView = wide.Item3;
        Capture(cam, PreviewDir + "level_wide_v7.png");
        cam.transform.position = new Vector3(3.2f, 3.4f, -5.2f); cam.transform.LookAt(new Vector3(-.3f, .2f, -8.6f)); cam.fieldOfView = 40;
        Capture(cam, PreviewDir + "shore_closeup_v7.png");
        cam.transform.SetPositionAndRotation(wide.Item1, wide.Item2); cam.fieldOfView = wide.Item3;
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    // Glowing slot / core ring decal: additive procedural ring quad lying on the surface (visual only, no collider).
    // Recommended Game.unity Main Camera orthographic size (Game.unity is read-only for art; 程序开发 to apply). Was 9.2.
    public const float GameCamOrthoV7 = 6.4f;

    static void SlotMarker(Transform parent, Vector3 pos, Vector2 size, Color c, int shape)
    {
        string mp = MatDir + (shape == 8 ? "M_FX_SlotPlate.mat" : "M_FX_SlotGlow.mat");
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null) { m = new Material(Shader.Find("StoneSignal/FXAdditive")); AssetDatabase.CreateAsset(m, mp); }
        m.SetFloat("_Shape", shape); m.SetFloat("_RingWidth", .14f); m.SetFloat("_Intensity", shape == 8 ? 3.2f : 1.6f); m.SetColor("_TintColor", Color.white);
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "FX_SlotGlow"; UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent); q.transform.position = pos + Vector3.up * .015f; q.transform.rotation = Quaternion.Euler(90, 0, 0);
        q.transform.localScale = new Vector3(size.x, size.y, 1);
        var mesh = UnityEngine.Object.Instantiate(q.GetComponent<MeshFilter>().sharedMesh); var cols = new Color[mesh.vertexCount];
        for (int i = 0; i < cols.Length; i++) cols[i] = c; mesh.colors = cols; q.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void BuildPostFx()
    {
        const string vpPath = "Assets/Game/Settings/Stylized/VP_StylizedDemo.asset";
        Directory.CreateDirectory("Assets/Game/Settings/Stylized");
        AssetDatabase.DeleteAsset(vpPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, vpPath);
        var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(.7f); bloom.threshold.Override(.9f); bloom.scatter.Override(.65f);
        var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(16); ca.contrast.Override(10); ca.postExposure.Override(.1f);
        var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(12); wb.tint.Override(4);
        var vg = prof.Add<Vignette>(true); vg.intensity.Override(.3f); vg.smoothness.Override(.45f); vg.color.Override(Hex("1A1030"));
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
        foreach (var c in prof.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, prof); }
        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        var vol = new GameObject("PostFX_Volume").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof;
    }

    [Serializable] class LayoutItem { public string asset; public float[] pos; public float rotZ; public float rotX; public float rotY; public float[] scale; }
    [Serializable] class Layout { public LayoutItem[] items; }

    internal static void Capture(Camera cam, string file)
    {
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render(); cam.Render(); // first render warms up shadow/SH state in batchmode
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); img.Apply();
        var png = img.EncodeToPNG();
        try { File.WriteAllBytes(file, png); }
        catch (IOException) { var alt = Path.ChangeExtension(file, null) + "_new.png"; File.WriteAllBytes(alt, png); Debug.LogWarning("Preview locked by another app, wrote " + alt); }
        RenderTexture.active = prev; cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(img); UnityEngine.Object.DestroyImmediate(rt);
        Debug.Log("STYLIZED CAPTURE " + file);
    }

    [MenuItem("StoneSignal/Stylized art/Check batch 1")]
    public static void Checks()
    {
        var errors = new List<string>();
        foreach (var s in new[] { "StoneSignal/ToonLit", "StoneSignal/ToonLitOutline" })
        {
            var sh = Shader.Find(s);
            if (sh == null) errors.Add("Shader missing " + s);
            else if (ShaderUtil.ShaderHasError(sh)) errors.Add("Shader has errors " + s);
        }
        foreach (var a in Assets)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + a.folder + "/" + a.fbx + ".fbx");
            if (model == null) { errors.Add("Missing " + a.fbx); continue; }
            var t = model.transform;
            if (a.folder != "Enemies" && Quaternion.Angle(t.localRotation, Quaternion.identity) > .01f || (t.localScale - Vector3.one).sqrMagnitude > 1e-6f)
                errors.Add($"{a.fbx} root not identity rot={t.localRotation.eulerAngles} scale={t.localScale}");
            var meshes = model.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh)
                .Concat(model.GetComponentsInChildren<SkinnedMeshRenderer>().Select(f => f.sharedMesh)).ToList();
            int tris = meshes.Sum(m => m.triangles.Length / 3);
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + a.prefab + ".prefab"));
            var b = probe.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            UnityEngine.Object.DestroyImmediate(probe);
            bool waterPivot = a.fbx.Contains("Island") || a.fbx.Contains("Board_Cliff") || a.fbx.Contains("Bridge") || a.fbx.Contains("Dock"); // pivot = water level by design
            if (!waterPivot && b.min.y < -.08f) errors.Add($"{a.fbx} pivot not at base (min y {b.min.y:F3})");
            bool outline = a.folder != "Environment";
            if (a.folder == "Enemies" && a.mat != "Shard")
            {
                var names = AssetDatabase.LoadAllAssetsAtPath(ArtDir + a.folder + "/" + a.fbx + ".fbx").OfType<AnimationClip>().Select(c => c.name).ToList();
                foreach (var need in new[] { EnemyVisualContract.MoveClip, StoneSignal.VFX.EnemyAnimParams.HitClip, EnemyVisualContract.DeathClip })
                    if (!names.Contains(need)) errors.Add($"{a.fbx} missing clip {need} (has {string.Join(",", names)})");
                if (model.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0) errors.Add(a.fbx + " is not skinned");
            }
            if (outline && meshes.Any(m => { var l = new List<Vector4>(); m.GetUVs(3, l); return l.Count == 0; }))
                errors.Add(a.fbx + " missing UV3 smoothed normals");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + a.prefab + ".prefab");
            if (prefab == null) errors.Add("Missing prefab " + a.prefab);
            else if (prefab.GetComponentsInChildren<Collider>().Length > 0) errors.Add(a.prefab + " has colliders");
            Debug.Log($"STYLIZED ASSET {a.fbx}: tris={tris} size={b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} m minY={b.min.y:F3}");
        }
        if (errors.Count > 0) throw new Exception("STYLIZED ART FAIL\n" + string.Join("\n", errors));
        Debug.Log("STYLIZED ART PASS");
    }

    /// Batchmode entry: Tuanjie.exe -batchmode -projectPath . -executeMethod StylizedArtIntegration.BatchImport -logFile stylized-art-import.log
    public static void BatchImport()
    {
        try
        {
            Import();
            StylizedVFXBuilder.BuildAll();
            Checks();
            StylizedVFXBuilder.Checks();
            ThirdPartyArtIntegration.Checks();   // existing runtime art untouched
            Stage2Validation.Run();              // gameplay logic still passes
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }
}

/// Bakes smoothed outline normals into UV3 (tangent space xyz, width w) for stylized units/towers (spec 4.6.2),
/// and pins importer settings for every model under Assets/Game/Art/Stylized/.
public class StylizedModelPostprocessor : AssetPostprocessor
{
    bool IsStylized => assetPath.StartsWith(StylizedArtIntegration.ArtDir);
    bool IsEnemy => assetPath.Contains("/Stylized/Enemies/");
    bool NeedsOutline => IsEnemy || assetPath.Contains("/Stylized/Towers/");

    void OnPreprocessModel()
    {
        if (!IsStylized) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = IsEnemy; // enemies exported without baked space transform (animated)
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = NeedsOutline ? ModelImporterTangents.CalculateMikk : ModelImporterTangents.None;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = IsEnemy; mi.animationType = IsEnemy ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        mi.importCameras = false; mi.importLights = false; mi.addCollider = false; mi.isReadable = false;
    }

    void OnPreprocessTexture()
    {
        if (!IsStylized) return;
        var ti = (TextureImporter)assetImporter;
        ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
        if (assetPath.Contains("T_Ramp_")) { ti.sRGBTexture = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; }
        else if (assetPath.Contains("Palette")) { ti.sRGBTexture = true; ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Clamp; }
    }

    // Blender exports one FBX take per NLA strip (SS_Move / SS_Hit / SS_Death, possibly prefixed "Armature|").
    void OnPreprocessAnimation()
    {
        if (!IsStylized || !IsEnemy) return;
        var mi = (ModelImporter)assetImporter;
        var outClips = new List<ModelImporterClipAnimation>(); var seen = new HashSet<string>();
        foreach (var c in mi.defaultClipAnimations)
        {
            string n = new[] { "SS_Move", "SS_Hit", "SS_Death" }.FirstOrDefault(k => c.takeName.Contains(k) || c.name.Contains(k));
            if (n == null || !seen.Add(n)) continue;
            c.name = n; c.loopTime = n == "SS_Move"; c.loopPose = false; c.lockRootPositionXZ = true;
            outClips.Add(c);
        }
        if (outClips.Count > 0) mi.clipAnimations = outClips.ToArray();
    }

    void OnPostprocessModel(GameObject go)
    {
        if (!IsStylized || !NeedsOutline) return;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>()) Bake(f.sharedMesh);
        foreach (var f in go.GetComponentsInChildren<SkinnedMeshRenderer>()) Bake(f.sharedMesh);
    }

    static void Bake(Mesh m)
    {
        var v = m.vertices; var n = m.normals; var t = m.tangents;
        if (t == null || t.Length != v.Length) return;
        var avg = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < v.Length; i++) { avg.TryGetValue(v[i], out var s); avg[v[i]] = s + n[i]; }
        var uv3 = new List<Vector4>(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 sn = avg[v[i]].normalized, T = t[i], N = n[i];
            Vector3 B = Vector3.Cross(N, T) * t[i].w;
            uv3.Add(new Vector4(Vector3.Dot(sn, T), Vector3.Dot(sn, B), Vector3.Dot(sn, N), 1f));
        }
        m.SetUVs(3, uv3);
    }
}
