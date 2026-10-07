using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StoneSignal;

/// <summary>
/// Rebuilds the whole visual layer from two CC0 packs that already live in ArtSource/ThirdParty:
/// Kenney Tower Defense Kit (board, blocks, towers) and Quaternius Animated Monsters (enemies).
/// Idempotent: safe to re-run after the sources are re-extracted.
/// </summary>
public static class ThirdPartyArtIntegration
{
    private const string Root = "Assets/Game/";
    private const string KenneySource = "ArtSource/ThirdParty/extracted/kenney/Models";
    private const string MonsterSource = "ArtSource/ThirdParty/extracted/quaternius/Animated Monster Pack by @Quaternius";
    private const string KenneyDir = "Assets/Game/Art/ThirdParty/Kenney/";
    private const string MonsterDir = "Assets/Game/Art/ThirdParty/Monsters/";
    private const string MaterialDir = "Assets/Game/Materials/Art/";
    private const string PrefabDir = "Assets/Game/Prefabs/Art/";
    private const string AnimDir = "Assets/Game/Animation/Monsters/";
    private const string IconDir = "Assets/Game/Art/Icons/";
    private const string CatalogPath = "Assets/Game/Settings/ArtCatalog.asset";

    // Kenney tiles are exactly one cell wide and 0.2 tall, so every placed prefab carries
    // this lift on its Rig child instead of moving the gameplay transform.
    private const float TileTop = .2f;
    private const float BlockHeight = .5f;

    private static readonly string[] KenneyModels =
    {
        "tile", "tile-dirt", "tile-spawn", "tile-crystal", "wood-structure", "spawn-round",
        "tower-round-base", "tower-round-middle-a", "tower-round-middle-b", "tower-round-middle-c", "tower-round-crystals",
        "weapon-ballista", "weapon-turret", "weapon-cannon",
        "detail-tree", "detail-tree-large", "detail-rocks", "detail-crystal", "detail-dirt"
    };

    // enemy asset name, source monster, target height in cells
    private static readonly (string enemy, string monster, float height)[] Enemies =
    {
        ("Drifter", "Slime", 1.0f),
        ("Skimmer", "Bat", .9f),
        ("Bulwark", "Skeleton", 1.4f),
        ("Splitter", "Dragon", 1.15f),
        ("Shard", "Slime", .6f)
    };

    [MenuItem("StoneSignal/Import third-party art pack")]
    public static void Import()
    {
        PurgeLegacy();
        Directory.CreateDirectory(KenneyDir);
        Directory.CreateDirectory(MonsterDir);
        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(PrefabDir);
        CopySources();
        AssetDatabase.Refresh();

        var atlas = ImportAtlas();
        var tints = ImportTowerTints();
        var prefabs = new Dictionary<string, GameObject>();
        foreach (string model in KenneyModels) prefabs[model] = BuildKenneyPrefab(model, atlas);

        BuildTowerPrefabs(prefabs, atlas, tints);
        var controllers = ImportMonsters();
        WireCatalog(prefabs, atlas);
        ConfigureScene();
        AssetDatabase.SaveAssets();
        RenderTowerIcons();
        RenderBlockIcons();
        Checks();
        Debug.Log("THIRD PARTY ART IMPORT PASS: " + KenneyModels.Length + " Kenney prefabs, 4 tower prefabs, " +
                  Enemies.Length + " animated enemy prefabs, " + controllers.Count + " animation controllers");
    }

    // ---------------------------------------------------------------- assets in

    private static void PurgeLegacy()
    {
        // Dropping the imported folder also drops every ModelImporter remap, so a re-run
        // always starts from a clean import instead of stacking duplicate remap entries.
        if (AssetDatabase.IsValidFolder("Assets/Game/Art/ThirdParty")) AssetDatabase.DeleteAsset("Assets/Game/Art/ThirdParty");
        if (AssetDatabase.IsValidFolder("Assets/Game/Art/Models")) AssetDatabase.DeleteAsset("Assets/Game/Art/Models");
        if (AssetDatabase.IsValidFolder("Assets/Game/Animation")) AssetDatabase.DeleteAsset("Assets/Game/Animation");
        foreach (var folder in new[] { "Assets/Game/Prefabs/Art", "Assets/Game/Materials/Art" })
            foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                // Prefabs are always rebuilt. Materials are rebuilt too, except the ones Lit() owns
                // by name plus the backdrop; that drops the retired SS_* palette.
                if (folder.Contains("Materials") && (name.StartsWith("Kenney") || name.StartsWith("Q_") || name == "Backdrop")) continue;
                AssetDatabase.DeleteAsset(path);
            }
    }

    private static void CopySources()
    {
        foreach (string model in KenneyModels)
            File.Copy(ToSystemPath(KenneySource + "/FBX format/" + model + ".fbx"), ToSystemPath(KenneyDir + model + ".fbx"), true);
        // The model UVs address colormap.png, which ships beside the FBX folder. Textures/variation-a.png
        // is only a recolour sheet and must not be used as the base map.
        File.Copy(ToSystemPath(KenneySource + "/FBX format/Textures/colormap.png"), ToSystemPath(KenneyDir + "colormap.png"), true);
        foreach (string monster in new[] { "Slime", "Dragon", "Skeleton", "Bat" })
            File.Copy(ToSystemPath(MonsterSource + "/FBX/" + monster + ".fbx"), ToSystemPath(MonsterDir + monster + ".fbx"), true);
    }

    private static string ToSystemPath(string assetPath) => Path.GetFullPath(assetPath);

    private static ModelImporter Prepare(string path, bool animation)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.globalScale = 1;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.isReadable = false;
        importer.importConstraints = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.importAnimation = animation;
        if (animation)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.importAnimatedCustomProperties = false;
        }
        importer.SaveAndReimport();
        return importer;
    }

    /// <summary>Remaps every material embedded in the FBX so the model only ever renders through URP assets.</summary>
    private static void Remap(string path, Func<string, Material> resolve)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(asset is Material embedded) || string.IsNullOrEmpty(embedded.name)) continue;
            var target = resolve(embedded.name);
            if (target == null) continue;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), target);
            Debug.Log(path + " remap " + embedded.name + " -> " + target.name);
        }
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- materials

    private static Material Lit(string name, Color color, Texture map, float smoothness, float metallic)
    {
        string path = MaterialDir + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Universal Render Pipeline/Lit");
        if (map != null) material.SetTexture("_BaseMap", map);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material ImportAtlas()
    {
        string texturePath = KenneyDir + "colormap.png";
        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        textureImporter.textureType = TextureImporterType.Default;
        textureImporter.sRGBTexture = true;
        textureImporter.mipmapEnabled = true;
        textureImporter.wrapMode = TextureWrapMode.Clamp;
        textureImporter.filterMode = FilterMode.Bilinear;
        textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
        textureImporter.maxTextureSize = 512;
        textureImporter.SaveAndReimport();
        return Lit("KenneyAtlas", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), .1f, 0);
    }

    private static Dictionary<string, Material> ImportTowerTints()
    {
        return new Dictionary<string, Material>
        {
            ["Needle"] = Lit("KenneyNeedle", new Color(1.45f, 1.05f, .35f), null, .1f, 0),
            ["Pulse"] = Lit("KenneyPulse", new Color(.4f, 1.1f, 1.5f), null, .1f, 0),
            ["Seismic"] = Lit("KenneySeismic", new Color(1.5f, .6f, .3f), null, .1f, 0),
            ["Chill"] = Lit("KenneyChill", new Color(.85f, .8f, 1.6f), null, .1f, 0)
        };
    }

    private static Dictionary<string, Material> MonsterMaterials(string monster)
    {
        var result = new Dictionary<string, Material>();
        string mtl = MonsterSource + "/OBJ/" + monster + ".mtl";
        if (!File.Exists(ToSystemPath(mtl))) return result;
        string current = null;
        foreach (var raw in File.ReadAllLines(ToSystemPath(mtl)))
        {
            var line = raw.Trim();
            if (line.StartsWith("newmtl"))
            {
                current = line.Substring(6).Trim();
                result[current] = Lit("Q_" + monster + "_" + current, Color.white, null, .12f, 0);
            }
            else if (line.StartsWith("Kd ") && current != null)
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                var color = new Color(
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture), 1);
                result[current].SetColor("_BaseColor", color);
                EditorUtility.SetDirty(result[current]);
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- prefabs

    private static GameObject Save(GameObject wrapper, string name)
    {
        string path = PrefabDir + name + ".prefab";
        return PrefabUtility.SaveAsPrefabAsset(wrapper, path);
    }

    private static GameObject BuildKenneyPrefab(string model, Material atlas)
    {
        string fbx = KenneyDir + model + ".fbx";
        Prepare(fbx, false);
        Remap(fbx, _ => atlas);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var wrapper = new GameObject(model);
        var rig = new GameObject("Rig");
        rig.transform.SetParent(wrapper.transform, false);
        rig.transform.localPosition = new Vector3(0, TileTop, 0);
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = "Mesh";
        mesh.transform.SetParent(rig.transform, false);
        var prefab = Save(wrapper, model);
        UnityEngine.Object.DestroyImmediate(wrapper);
        return prefab;
    }

    private sealed class TowerPart
    {
        public string model;
        public float y;
        public Material material;
        public TowerPart(string model, float y, Material material) { this.model = model; this.y = y; this.material = material; }
    }

    private static void BuildTowerPrefabs(Dictionary<string, GameObject> parts, Material atlas, Dictionary<string, Material> tints)
    {
        // Kenney stacks base(0.21) -> body(0.60) -> weapon; the crystal tower has no weapon.
        var layouts = new (string tower, TowerPart[] pieces)[]
        {
            ("NeedleBeacon", new[] { new TowerPart("tower-round-base", 0, null), new TowerPart("tower-round-middle-a", .21f, null), new TowerPart("weapon-ballista", .81f, tints["Needle"]) }),
            ("PulseBeacon", new[] { new TowerPart("tower-round-base", 0, null), new TowerPart("tower-round-middle-b", .21f, null), new TowerPart("weapon-turret", .81f, tints["Pulse"]) }),
            ("SeismicBeacon", new[] { new TowerPart("tower-round-base", 0, null), new TowerPart("tower-round-middle-c", .21f, null), new TowerPart("weapon-cannon", .81f, tints["Seismic"]) }),
            ("ChillBeacon", new[] { new TowerPart("tower-round-base", 0, null), new TowerPart("tower-round-crystals", .21f, tints["Chill"]) })
        };
        foreach (var layout in layouts)
        {
            var wrapper = new GameObject(layout.tower);
            var rig = new GameObject("Rig");
            rig.transform.SetParent(wrapper.transform, false);
            rig.transform.localPosition = new Vector3(0, TileTop, 0);
            for (int i = 0; i < layout.pieces.Length; i++)
            {
                var piece = layout.pieces[i];
                var mesh = UnityEngine.Object.Instantiate(parts[piece.model]);
                mesh.name = piece.model;
                mesh.transform.SetParent(rig.transform, false);
                mesh.transform.localPosition = new Vector3(0, piece.y, 0);
                var material = piece.material ?? atlas;
                foreach (var renderer in mesh.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = material;
            }
            Save(wrapper, layout.tower);
            UnityEngine.Object.DestroyImmediate(wrapper);
        }
    }

    private static Dictionary<string, AnimatorController> ImportMonsters()
    {
        var controllers = new Dictionary<string, AnimatorController>();
        foreach (string monster in new[] { "Slime", "Dragon", "Skeleton", "Bat" })
        {
            string fbx = MonsterDir + monster + ".fbx";
            Prepare(fbx, true);
            var materials = MonsterMaterials(monster);
            Remap(fbx, name => materials.TryGetValue(name, out var material) ? material : null);
            controllers[monster] = BuildController(monster, fbx);
        }
        foreach (var entry in Enemies) BuildEnemyPrefab(entry.enemy, entry.monster, entry.height, controllers[entry.monster]);
        return controllers;
    }

    private static AnimationClip Pick(List<AnimationClip> clips, params string[] keys)
    {
        foreach (string key in keys)
            foreach (var clip in clips)
                if (clip.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return clip;
        return null;
    }

    private static AnimatorController BuildController(string monster, string fbx)
    {
        string folder = AnimDir + monster + "/";
        Directory.CreateDirectory(ToSystemPath(folder));
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
        Debug.Log(monster + " clips: " + string.Join(", ", clips.Select(c => c.name)));

        var move = Pick(clips, "walk", "run", "move", "idle", "fly") ?? clips.FirstOrDefault();
        var death = Pick(clips, "death", "die", "dead", "hit", "attack") ?? move;
        if (move == null || death == null) throw new InvalidOperationException(monster + " FBX carries no usable animation clip.");

        var moveClip = CopyClip(move, folder + EnemyVisualContract.MoveClip + ".anim", EnemyVisualContract.MoveClip, true);
        var deathClip = CopyClip(death, folder + EnemyVisualContract.DeathClip + ".anim", EnemyVisualContract.DeathClip, false);

        string controllerPath = folder + monster + ".controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter(EnemyVisualContract.DieTrigger, AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.AddState(EnemyVisualContract.LocomotionState, new Vector3(260, 0, 0));
        locomotion.motion = moveClip;
        var dying = machine.AddState(EnemyVisualContract.DeathState, new Vector3(520, 120, 0));
        dying.motion = deathClip;
        machine.defaultState = locomotion;
        var transition = dying.AddTransition(locomotion);
        transition.hasExitTime = false;
        transition.duration = 0;
        transition.AddCondition(AnimatorConditionMode.If, 0, EnemyVisualContract.DieTrigger);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip CopyClip(AnimationClip source, string path, string name, bool loop)
    {
        AssetDatabase.DeleteAsset(path);
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = name;
        copy.hideFlags = HideFlags.None;
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        AssetDatabase.CreateAsset(copy, path);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    private static void BuildEnemyPrefab(string enemy, string monster, float targetHeight, AnimatorController controller)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterDir + monster + ".fbx");
        var wrapper = new GameObject(enemy);
        var rig = new GameObject("Rig");
        rig.transform.SetParent(wrapper.transform, false);
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = monster;
        mesh.transform.SetParent(rig.transform, false);
        var renderers = mesh.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float scale = bounds.size.y > .0001f ? targetHeight / bounds.size.y : 1f;
        rig.transform.localScale = Vector3.one * scale;
        rig.transform.localPosition = new Vector3(-bounds.center.x * scale, TileTop, -bounds.center.z * scale);
        var animator = AttachAnimator(wrapper, mesh, controller);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Save(wrapper, enemy);
        UnityEngine.Object.DestroyImmediate(wrapper);
    }

    /// <summary>
    /// Puts the Animator on the node that actually owns the SkinnedMeshRenderer, which is the
    /// only arrangement Unity always binds correctly for imported Generic rigs.
    /// </summary>
    private static Animator AttachAnimator(GameObject wrapper, GameObject mesh, RuntimeAnimatorController controller)
    {
        var skinned = mesh.GetComponentInChildren<SkinnedMeshRenderer>();
        var host = skinned != null ? skinned.gameObject : mesh;
        foreach (var stray in wrapper.GetComponentsInChildren<Animator>())
            if (stray.gameObject != host) UnityEngine.Object.DestroyImmediate(stray, true);
        var animator = host.GetComponent<Animator>();
        if (animator == null) animator = host.AddComponent<Animator>();
        if (animator == null) throw new InvalidOperationException("Could not attach an Animator to " + mesh.name);
        animator.runtimeAnimatorController = controller;
        return animator;
    }

    // ---------------------------------------------------------------- wiring

    private static void WireCatalog(Dictionary<string, GameObject> prefabs, Material atlas)
    {
        var art = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<ArtCatalog>();
            AssetDatabase.CreateAsset(art, CatalogPath);
        }
        art.tile = prefabs["tile"];
        art.tilePath = prefabs["tile-dirt"];
        art.tileSpawn = prefabs["tile-spawn"];
        art.tileGoal = prefabs["tile-crystal"];
        art.block = prefabs["wood-structure"];
        art.spawnPortal = prefabs["spawn-round"];
        art.signalCore = prefabs["tower-round-crystals"];
        art.towerBase = prefabs["tower-round-base"];
        art.towerBodyA = prefabs["tower-round-middle-a"];
        art.towerBodyB = prefabs["tower-round-middle-b"];
        art.towerBodyC = prefabs["tower-round-middle-c"];
        art.towerCrystals = prefabs["tower-round-crystals"];
        art.weaponBallista = prefabs["weapon-ballista"];
        art.weaponTurret = prefabs["weapon-turret"];
        art.weaponCannon = prefabs["weapon-cannon"];
        art.detailTree = prefabs["detail-tree"];
        art.detailTreeLarge = prefabs["detail-tree-large"];
        art.detailRocks = prefabs["detail-rocks"];
        art.detailCrystal = prefabs["detail-crystal"];
        art.detailDirt = prefabs["detail-dirt"];
        art.atlas = atlas;
        art.tileTop = TileTop;
        art.blockTop = TileTop + BlockHeight;
        string backdropPath = MaterialDir + "Backdrop.mat";
        art.backdrop = AssetDatabase.LoadAssetAtPath<Material>(backdropPath);
        if (art.backdrop == null)
        {
            art.backdrop = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            art.backdrop.name = "Backdrop";
            AssetDatabase.CreateAsset(art.backdrop, backdropPath);
        }
        art.backdrop.SetColor("_BaseColor", new Color(.30f, .45f, .27f));
        EditorUtility.SetDirty(art.backdrop);
        EditorUtility.SetDirty(art);

        var config = AssetDatabase.LoadAssetAtPath<GameConfig>(Root + "Settings/GameConfig.asset");
        config.palette.art = art;
        EditorUtility.SetDirty(config.palette);
        string[] towers = { "NeedleBeacon", "PulseBeacon", "SeismicBeacon", "ChillBeacon" };
        for (int i = 0; i < config.towers.Length; i++)
        {
            var tower = config.towers[i];
            tower.visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + towers[i] + ".prefab");
            tower.icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + towers[i] + ".png");
            EditorUtility.SetDirty(tower);
        }
        foreach (string name in new[] { "Drifter", "Skimmer", "Bulwark", "Splitter", "Shard" })
        {
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(Root + "ScriptableObjects/Enemies/" + name + ".asset");
            enemy.visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + name + ".prefab");
            EditorUtility.SetDirty(enemy);
        }
        foreach (var shape in config.blocks)
        {
            shape.icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + "Block" + shape.displayName + ".png");
            EditorUtility.SetDirty(shape);
        }
    }

    private static void ConfigureScene()
    {
        var scene = EditorSceneManager.OpenScene(Root + "Scenes/Game.unity");
        var bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
        var camera = bootstrap.viewCamera;
        camera.orthographicSize = 9.2f;
        camera.transform.position = new Vector3(-3.2f, 17.4f, -18.4f);
        camera.transform.LookAt(new Vector3(2.5f, -1.1f, 0));
        camera.backgroundColor = new Color(.58f, .76f, .87f);
        camera.allowHDR = true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null) { pipeline.supportsHDR = true; EditorUtility.SetDirty(pipeline); }
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            if (light.type == LightType.Directional)
            {
                light.color = new Color(1, .96f, .88f);
                light.intensity = 1.35f;
                light.transform.rotation = Quaternion.Euler(48, -32, 0);
                light.shadows = LightShadows.Soft;
            }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.56f, .62f, .66f);
        string profilePath = Root + "Settings/BoardPostProcessing.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }
        if (!profile.TryGet<Bloom>(out var bloom)) { bloom = profile.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bloom, profile); }
        bloom.intensity.Override(.16f); bloom.threshold.Override(1.1f); bloom.scatter.Override(.5f); EditorUtility.SetDirty(bloom);
        if (!profile.TryGet<Tonemapping>(out var tone)) { tone = profile.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(tone, profile); }
        // Neutral keeps Kenney's flat candy palette readable; ACES washed the grass out.
        tone.mode.Override(TonemappingMode.Neutral); EditorUtility.SetDirty(tone); EditorUtility.SetDirty(profile);
        var volume = UnityEngine.Object.FindObjectOfType<Volume>();
        if (volume == null) volume = new GameObject("Board atmosphere").AddComponent<Volume>();
        volume.name = "Board atmosphere";
        volume.isGlobal = true; volume.sharedProfile = profile; volume.priority = 1;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ---------------------------------------------------------------- icons

    private static void RenderTowerIcons()
    {
        string[] towers = { "NeedleBeacon", "PulseBeacon", "SeismicBeacon", "ChillBeacon" };
        foreach (string tower in towers)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + tower + ".prefab");
            if (prefab == null) continue;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            RenderIcon(model, IconDir + tower + ".png");
            UnityEngine.Object.DestroyImmediate(model);
        }
    }

    private static void RenderBlockIcons()
    {
        var config = AssetDatabase.LoadAssetAtPath<GameConfig>(Root + "Settings/GameConfig.asset");
        var art = config.palette.art;
        if (art == null || art.block == null) return;
        foreach (var shape in config.blocks)
        {
            var holder = new GameObject("Block icon source");
            foreach (Vector2Int cell in shape.cells)
            {
                var piece = (GameObject)PrefabUtility.InstantiatePrefab(art.block);
                piece.transform.SetParent(holder.transform, false);
                piece.transform.localPosition = new Vector3(cell.x, 0, cell.y);
                piece.transform.localScale = Vector3.one * .86f;
            }
            RenderIcon(holder, IconDir + "Block" + shape.displayName + ".png");
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    private static void RenderIcon(GameObject model, string iconPath)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var camera = new GameObject("Icon camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0, 0, 0, 0);
        camera.orthographic = true;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 80f;
        var key = new GameObject("Icon key light").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.5f;
        key.transform.rotation = Quaternion.Euler(45, -35, 0);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.12f;
        camera.transform.position = bounds.center + new Vector3(1, 1.1f, -1.6f).normalized * radius * 4f;
        camera.transform.LookAt(bounds.center);
        camera.orthographicSize = radius;
        var target = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        image.Apply();
        File.WriteAllBytes(ToSystemPath(iconPath), image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(key);
        UnityEngine.Object.DestroyImmediate(camera);
        var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- checks

    [MenuItem("StoneSignal/Check art pack")]
    public static void Checks()
    {
        var config = AssetDatabase.LoadAssetAtPath<GameConfig>(Root + "Settings/GameConfig.asset");
        var art = config.palette.art;
        PrototypeChecks.Require(art != null, "Art catalog assigned");
        PrototypeChecks.Require(art.tile != null && art.tilePath != null && art.tileSpawn != null && art.tileGoal != null, "Board tiles assigned");
        PrototypeChecks.Require(art.block != null && art.spawnPortal != null && art.signalCore != null, "Block and landmarks assigned");
        PrototypeChecks.Require(art.atlas != null && art.backdrop != null, "Materials assigned");
        foreach (var tower in config.towers) PrototypeChecks.Require(tower.visualPrefab != null && tower.icon != null, "Tower prefab and icon");
        foreach (var shape in config.blocks) PrototypeChecks.Require(shape.icon != null, "Block icon");
        foreach (string path in Directory.GetFiles(ToSystemPath(PrefabDir), "*.prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ToAssetPath(path));
            PrototypeChecks.Require(prefab != null, "Prefab loads: " + Path.GetFileName(path));
            PrototypeChecks.Require(prefab.GetComponentsInChildren<Collider>().Length == 0, "Art has no gameplay colliders: " + prefab.name);
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            PrototypeChecks.Require(renderers.Length > 0, "Prefab has mesh: " + prefab.name);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            PrototypeChecks.Require(extent > .2f && extent < 4, "Imported metre scale: " + prefab.name + " = " + extent.ToString("0.00"));
            PrototypeChecks.Require(prefab.transform.localScale == Vector3.one && prefab.transform.localRotation == Quaternion.identity, "Prefab identity wrapper: " + prefab.name);
            foreach (var r in renderers)
                foreach (var mat in r.sharedMaterials)
                    PrototypeChecks.Require(mat != null && mat.shader.name.StartsWith("Universal Render Pipeline/"), "URP material remapped: " + prefab.name);
        }
        foreach (var entry in Enemies)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + entry.enemy + ".prefab");
            PrototypeChecks.Require(prefab != null, "Enemy prefab: " + entry.enemy);
            var animator = prefab.GetComponentInChildren<Animator>();
            PrototypeChecks.Require(animator != null, "Enemy animator present: " + entry.enemy);
            var controller = animator.runtimeAnimatorController;
            PrototypeChecks.Require(controller != null && controller.animationClips.Length >= 2, "Enemy walk and death clips: " + entry.enemy);
            bool death = false;
            foreach (var clip in controller.animationClips) death |= clip.name == EnemyVisualContract.DeathClip;
            PrototypeChecks.Require(death, "Death clip follows the runtime contract: " + entry.enemy);
        }
        Debug.Log("ART REFERENCES PASS");
    }

    private static string ToAssetPath(string systemPath)
    {
        string full = Path.GetFullPath(systemPath).Replace('\\', '/');
        string project = Path.GetFullPath(".").Replace('\\', '/') + "/";
        return full.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? full.Substring(project.Length) : full;
    }

    public static void Build() { Import(); Stage2Validation.Run(); PrototypeBuild.Build(); }
}
