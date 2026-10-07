using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal.EditorTools
{
    // Hooks the stylized prefabs and VFX into the gameplay ScriptableObjects (data-driven; no scene edits).
    // Idempotent. Re-run after re-importing art: StoneSignal > Stylized art > Wire gameplay.
    // Note: "Import third-party art pack" rewrites the same fields back to Kenney/Quaternius; run this afterwards.
    public static class StylizedGameplayWiring
    {
        const string P = "Assets/Game/Prefabs/Stylized/", V = "Assets/Game/VFX/Stylized/", D = "Assets/Game/ScriptableObjects/";
        public const float GroundTop = .25f, WallTop = .6f;
        // level_layout.json: tile pivots rest at z~0.55 on the cliff (pivot 0), bridges at z 0.
        public const float CliffTop = .55f;
        // Art v7 framing (Docs/ArtDirection.md): Game.unity Main Camera orthographic size.
        public const float GameCamOrthoV7 = 6.4f;
        const string LayoutPath = "Assets/Game/ScriptableObjects/Board/StylizedBoard.asset", ScenePath = "Assets/Game/Scenes/Game.unity";

        // Demo layout (ArtSource/Stylized/level_layout.json, Unity = (-x, z, -y)), board 16x12 centred on origin:
        // bridges at Blender W/E/N -> cells (15,3) / (0,8) / (7,0); core (0,0) -> 2x2 at (7,5).
        static void WireLayout()
        {
            var layout = AssetDatabase.LoadAssetAtPath<BoardLayoutData>(LayoutPath);
            if (layout == null)
            {
                System.IO.Directory.CreateDirectory("Assets/Game/ScriptableObjects/Board");
                layout = ScriptableObject.CreateInstance<BoardLayoutData>(); AssetDatabase.CreateAsset(layout, LayoutPath);
            }
            layout.width = 16; layout.height = 12; layout.cellSize = 1;
            layout.spawns = new[] { new Vector2Int(15, 3), new Vector2Int(0, 8), new Vector2Int(7, 0) };
            layout.coreOrigin = new Vector2Int(7, 5); layout.coreSize = new Vector2Int(2, 2);
            layout.notes = "From ArtSource/Stylized/level_layout.json (Blender W/E/N bridges, Ember core at centre).";
            WireSurroundings(layout);
            EditorUtility.SetDirty(layout);
            var config = Load<GameConfig>("Assets/Game/Settings/GameConfig.asset");
            config.layout = layout; EditorUtility.SetDirty(config);
            foreach (var wave in config.waves) { foreach (var g in wave.groups) g.spawnIndex = -1; EditorUtility.SetDirty(wave); }
            AssetDatabase.SaveAssets();

            // Game.unity: centre the grid on the origin (camera already frames the origin) and apply the v7 camera framing.
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var grid = UnityEngine.Object.FindObjectOfType<GridManager>();
            if (grid == null) throw new Exception("Game.unity has no GridManager");
            grid.layout = layout; grid.width = layout.width; grid.height = layout.height; grid.cellSize = layout.cellSize;
            // v10: the level dressing sits at world origin with its board cliff top at 0.55, so the grid plane (tile pivots) is raised to match the art preview.
            float gridY = layout.levelDressing != null ? CliffTop : 0;
            grid.transform.position = new Vector3(-layout.width * layout.cellSize * .5f, gridY, -layout.height * layout.cellSize * .5f);
            grid.gameObject.name = "Grid " + layout.width + " x " + layout.height;
            EditorUtility.SetDirty(grid); EditorUtility.SetDirty(grid.transform);
            var cam = UnityEngine.Object.FindObjectOfType<GameBootstrap>()?.viewCamera;
            if (cam != null)
            {
                // Art v10 framing (StylizedArtIntegration.GameCam*V10): camera = look-at - forward * 30.
                var rot = Quaternion.Euler(StylizedArtIntegration.GameCamPitchV10, StylizedArtIntegration.GameCamYawV10, 0);
                cam.orthographic = true; cam.orthographicSize = StylizedArtIntegration.GameCamOrthoV10;
                cam.transform.SetPositionAndRotation(StylizedArtIntegration.GameCamTargetV10 - rot * Vector3.forward * 30f, rot);
                var urp = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (urp != null) { urp.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On; EditorUtility.SetDirty(urp); }
                EditorUtility.SetDirty(cam); EditorUtility.SetDirty(cam.transform);
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        [Serializable] class LayoutItem { public string asset; public float[] pos; public float rotZ; public float rotX; public float rotY; public float[] scale; }
        [Serializable] class LayoutFile { public LayoutItem[] items; }
        // Visual-only categories outside the playable board (bridges reach the shore islands with the authored segment counts).
        static readonly string[] SurroundKinds = { "Bridge_Plank", "Dock_Post" }; // islands/trees/water come from the art-authored level dressing prefab
        // Demo diorama: Unity = (-x, z, -y); tile top at 0.80 there vs 0.25 in game -> y offset -0.55 (cliff top 0.55 = tile top - 0.25).
        public const float DemoToGameY = GroundTop - .80f;
        static void WireSurroundings(BoardLayoutData layout)
        {
            var file = JsonUtility.FromJson<LayoutFile>(System.IO.File.ReadAllText("ArtSource/Stylized/level_layout.json"));
            var tints = new[] { null, "M_Foliage_Warm", "M_Foliage_Gold", "M_Foliage_Deep" }
                .Select(n => n == null ? null : AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/" + n + ".mat")).ToArray();
            var list = new System.Collections.Generic.List<BoardSurroundItem>();
            foreach (var it in file.items)
            {
                if (!SurroundKinds.Any(k => it.asset.Contains(k))) continue;
                string name = "PF_" + it.asset.Substring(3); if (name.EndsWith("_01")) name = name.Substring(0, name.Length - 3);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P + name + ".prefab");
                if (prefab == null) { Debug.LogWarning("Surroundings: no prefab for " + it.asset); continue; }
                Material tint = null;
                if (name.Contains("Tree")) tint = tints[Mathf.Abs((int)(it.pos[0] * 73 + it.pos[1] * 131)) % 4]; // same hash as the demo builder
                list.Add(new BoardSurroundItem
                {
                    prefab = prefab,
                    position = new Vector3(-it.pos[0], it.pos[2] + DemoToGameY, -it.pos[1]),
                    euler = new Vector3(-it.rotX, -it.rotZ, -it.rotY),
                    scale = it.scale != null && it.scale.Length == 3 ? new Vector3(it.scale[0], it.scale[2], it.scale[1]) : Vector3.one,
                    material = tint,
                });
            }
            layout.surroundings = list.ToArray();
            // Data-driven dressing slot: art agent ships PF_Env_LevelDressing_16x12; until then keep cliff + water + bridges.
            layout.levelDressing = AssetDatabase.LoadAssetAtPath<GameObject>(P + "PF_Env_LevelDressing_16x12.prefab");
            // v8: the dressing is authored at Game world origin (= board centre) and already contains BoardCliff + EntryBridges.
            layout.levelDressingOffset = Vector3.zero; // v10 dressing is authored in game space at world origin layout.levelDressingReplacesCliff = true;
            if (layout.levelDressing != null) layout.surroundings = new BoardSurroundItem[0];
            if (layout.levelDressing == null) Debug.Log("Level dressing: PF_Env_LevelDressing_16x12 not found, using cliff fallback");
            layout.waterMaterial = layout.levelDressing != null ? null : AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/M_Env_Water_Flat.mat");
            layout.waterY = DemoToGameY; layout.waterSize = 120;
        }

        static string CheckLayout()
        {
            var layout = Load<BoardLayoutData>(LayoutPath);
            var go = new GameObject("layout check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var grid = go.AddComponent<GridManager>(); grid.layout = layout; grid.Initialize();
                var paths = go.AddComponent<PathfindingManager>(); paths.Initialize(grid);
                var validator = go.AddComponent<PlacementValidator>(); validator.Initialize(grid, paths);
                int problems = 0;
                if (grid.Spawns.Count != layout.spawns.Length || grid.CoreCells.Count != layout.coreSize.x * layout.coreSize.y) problems++;
                if (!paths.AllSpawnsReachCore()) problems++;
                var ring = new System.Collections.Generic.List<Vector2Int>();
                foreach (var c in grid.CoreCells) foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        if (grid.CanPlace(c + d) && !ring.Contains(c + d)) ring.Add(c + d);
                if (validator.ValidatePlacement(ring) == null) problems++;          // sealing the core must fail
                if (validator.ValidatePlacement(new[] { grid.Spawns[1] }) == null) problems++; // spawns protected
                string lens = string.Join("/", paths.CurrentPaths.Select(p => p.Count));
                var planks = layout.surroundings == null ? new BoardSurroundItem[0] : layout.surroundings.Where(s => s.prefab && s.prefab.name.Contains("Bridge")).ToArray();
                // Segments per bridge by side of the board (Unity x/z relative to the board centre).
                int w = planks.Count(s => s.position.x > layout.width * .5f), e = planks.Count(s => s.position.x < -layout.width * .5f), n = planks.Count(s => s.position.z < -layout.height * .5f);
                if (layout.levelDressing == null && (w != 4 || e != 4 || n != 3)) problems++; // dressing ships its own bridges
                if (layout.levelDressing == null && layout.waterMaterial == null) problems++;
                lens += " surroundings=" + (layout.surroundings?.Length ?? 0) + " bridgeSegments=W" + w + "/E" + e + "/N" + n + " dressing=" + (layout.levelDressing ? layout.levelDressing.name : "none(cliff)");
                return "layout " + layout.width + "x" + layout.height + " spawns=" + grid.Spawns.Count + " core=" + grid.CoreCells.Count + " routes=" + lens + " layoutProblems=" + problems;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static BoardLayoutData layout0() => Load<BoardLayoutData>(LayoutPath);
        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) throw new Exception("Missing asset " + path);
            return a;
        }
        static GameObject Pf(string n) => Load<GameObject>(P + n + ".prefab");
        static GameObject Fx(string n) => Load<GameObject>(V + n + ".prefab");

        [MenuItem("StoneSignal/Stylized art/Wire gameplay (towers, enemies, core, VFX)")]
        public static void Wire()
        {
            BuildUiAtlas(); // sprite reimports first, so no loaded asset reference goes stale below
            Tower("Needle", "PF_Tower_Gatling_1x1", DamageKind.Physical, "FX_Muzzle_Gatling", "FX_Proj_Bullet", "FX_Hit_Kinetic", "FX_Explosion_Kinetic", t =>
            { t.explosionOnCrit = true; t.critChance = .1f; t.critMultiplier = 1.5f; t.impactHitStop = .03f; });
            Tower("Pulse", "PF_Tower_Tesla_1x1", DamageKind.Lightning, "FX_Muzzle_Tesla", "FX_Proj_Tesla_Arc", "FX_Hit_Lightning", "FX_Explosion_Lightning", t =>
            { t.explosionOnKill = true; t.muzzleHeight = .9f; t.muzzleForward = 0; });
            Tower("Seismic", "PF_Tower_Mortar_2x2", DamageKind.Explosive, "FX_Muzzle_Mortar", "FX_Proj_MortarShell", null, "FX_Explosion_HE", t =>
            { t.footprint = new Vector2Int(2, 2); t.explosionOnEveryHit = true; t.lobbedShot = true; t.lobHeight = 1.5f; t.impactShake = FeedbackShake.Light; });
            Tower("Chill", "PF_Tower_Frost_1x1", DamageKind.Ice, "FX_Muzzle_Frost", "FX_Proj_FrostShard", "FX_Hit_Ice", "FX_Explosion_Ice", t =>
            { t.explosionOnKill = true; });

            Enemy("Drifter", "PF_Enemy_Drifter", null);
            Enemy("Skimmer", "PF_Enemy_Skimmer", null);
            Enemy("Bulwark", "PF_Enemy_Bulwark", null);
            Enemy("Shard", "PF_Enemy_Shard", null);
            Enemy("Splitter", "PF_Enemy_Splitter", e => { e.splitVfx = Fx("FX_Enemy_SplitBurst"); e.splitChildScale = 1; });

            var art = Load<ArtCatalog>("Assets/Game/Settings/ArtCatalog.asset");
            art.signalCore = Pf("PF_Prop_Core");
            art.coreHitVfx = Fx("FX_Hit_Core");
            art.block = Pf("PF_Env_Rock_1x1");
            art.tile = Pf("PF_Env_Tile_Stone_A");
            art.tilePath = Pf("PF_Env_Tile_Dirt");
            art.tileSpawn = Pf("PF_Env_Tile_Stone_B");
            art.tileGoal = Pf("PF_Env_Tile_Stone_B");
            art.tileTop = GroundTop; art.blockTop = WallTop;
            art.spawnPortal = null; // entries are marked by plank bridges on the island edge
            art.boardCliff = Pf("PF_Env_Board_Cliff_16x12"); art.boardCliffSize = new Vector2(16, 12);
            art.boardCliffOffsetY = DemoToGameY; // cliff pivot -0.55; its top (0.55 demo / 0 game) is the ground plane under tile pivots
            art.entryBridge = Pf("PF_Env_Bridge_Plank");
            art.entryBridgePlanks = 4; art.entryBridgeOffsetY = -CliffTop;
            // v8 optional assets (null-safe until the art import has run).
            GameObject Opt(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(P + n + ".prefab");
            if (Opt("PF_Env_LevelDressing_16x12") != null) { art.boardCliff = null; art.entryBridge = null; } // dressing owns cliff + bridges
            var variants = "AABCDE".Select(c => Opt("PF_Env_Tile_Stone_" + c)).ToArray();
            art.tileVariants = variants.All(v => v != null) ? variants : new GameObject[0];
            art.tileUndulation = true;
            art.pathFlowSegment = Opt("PF_Path_FlowSegment"); art.pathFlowY = GroundTop + .09f; // above the +0.06 tile undulation (demo 0.82 would sit inside raised tiles)
            art.placeGhostBlock = Opt("PF_UI_PlaceGhost_Block"); art.placeGhostTower = Opt("PF_UI_PlaceGhost_Tower");
            art.slotHighlight = Opt("PF_UI_SlotHighlight");
            // instanced board pieces (MeshMerge/InstancedBatch): tiles, walls, slot highlights need GPU instancing on their materials
            foreach (var pf in new[] { art.slotHighlight, art.block, art.tile, art.tilePath }.Concat(art.tileVariants ?? new GameObject[0]))
                if (pf != null) foreach (var r in pf.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials)
                    if (m != null && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
            // v11 HUD sprites (ui_mockup_v3)
            art.uiOrbCore = UiSprite("ui_orb_core"); art.uiCardTower = UiSprite("ui_card_tower_frame"); art.uiCardBlueprint = UiSprite("ui_card_blueprint");
            art.uiSprites = System.IO.Directory.GetFiles(UiDir, "*.png").Select(p => UiSprite(System.IO.Path.GetFileNameWithoutExtension(p))).Where(x => x != null).ToArray();
            art.uiBadgeHotkey = UiSprite("ui_badge_hotkey"); art.uiBadgeSize2x2 = UiSprite("ui_badge_size_2x2"); art.uiBadgeSize1x2 = UiSprite("ui_badge_size_1x2");
            EditorUtility.SetDirty(art);
            WireLayout();
            AssetDatabase.SaveAssets();
            Debug.Log("STYLIZED WIRING: " + Check());
        }

        const string UiDir = "Assets/Game/Art/Stylized/UI/";
        // Imports a HUD png as an uncompressed-on-desktop / ASTC-on-mobile Sprite (once) and returns it.
        static Sprite UiSprite(string name)
        {
            string path = UiDir + name + ".png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter; if (imp == null) return null;
            if (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled)
            {
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true; imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        // One atlas for every HUD sprite => the hand/HUD batch into few draw calls.
        static void BuildUiAtlas()
        {
            const string atlasPath = "Assets/Game/Art/Stylized/UI/HUD.spriteatlas";
            var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                atlas = new UnityEngine.U2D.SpriteAtlas();
                UnityEditor.U2D.SpriteAtlasExtensions.SetPackingSettings(atlas, new UnityEditor.U2D.SpriteAtlasPackingSettings { enableRotation = false, enableTightPacking = false, padding = 4 });
                UnityEditor.U2D.SpriteAtlasExtensions.SetTextureSettings(atlas, new UnityEditor.U2D.SpriteAtlasTextureSettings { generateMipMaps = false, filterMode = FilterMode.Bilinear, sRGB = true });
                AssetDatabase.CreateAsset(atlas, atlasPath);
                UnityEditor.U2D.SpriteAtlasExtensions.Add(atlas, new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(UiDir.TrimEnd((char)47)) });
            }
            foreach (var png in System.IO.Directory.GetFiles(UiDir, "*.png")) UiSprite(System.IO.Path.GetFileNameWithoutExtension(png));
            AssetDatabase.Refresh();
            UnityEditor.U2D.SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
        }
        static void Tower(string asset, string prefab, DamageKind kind, string muzzle, string proj, string hit, string boom, Action<TowerData> extra)
        {
            var t = Load<TowerData>(D + "Towers/" + asset + ".asset");
            t.visualPrefab = Pf(prefab); t.damageKind = kind; t.headName = "";
            t.muzzleVfx = Fx(muzzle); t.projectileVfx = Fx(proj); t.hitVfx = hit != null ? Fx(hit) : null; t.explosionVfx = boom != null ? Fx(boom) : null;
            t.explosionOnEveryHit = t.explosionOnCrit = t.explosionOnKill = t.lobbedShot = false;
            t.critChance = 0; t.critMultiplier = 1.5f; t.muzzleHeight = .65f; t.muzzleForward = .45f; t.lobHeight = 1.5f;
            t.impactShake = FeedbackShake.None; t.impactHitStop = 0;
            t.footprint = Vector2Int.one; t.footprintRotates = false; // Gatling/Tesla/Frost/Cannon 1x1, Flamer 1x2 (rotates), Mortar 2x2
            t.icon = UiSprite("ui_icon_tower_" + asset) ?? t.icon;
            extra?.Invoke(t);
            EditorUtility.SetDirty(t);
        }
        static void Enemy(string asset, string prefab, Action<EnemyData> extra)
        {
            var e = Load<EnemyData>(D + "Enemies/" + asset + ".asset");
            e.visualPrefab = Pf(prefab);
            e.deathVfx = Fx("FX_Enemy_DeathPuff"); e.coinVfx = Fx("FX_Enemy_CoinPop"); e.splitVfx = null; e.splitChildScale = .65f;
            extra?.Invoke(e);
            EditorUtility.SetDirty(e);
        }

        [MenuItem("StoneSignal/Stylized art/Check gameplay wiring")]
        public static string Check()
        {
            int problems = 0; var log = new System.Text.StringBuilder();
            foreach (var n in new[] { "Needle", "Pulse", "Seismic", "Chill" })
            {
                var t = Load<TowerData>(D + "Towers/" + n + ".asset");
                var head = t.visualPrefab ? t.visualPrefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith("_Head")) : null;
                log.Append(n + "=" + (t.visualPrefab ? t.visualPrefab.name : "NULL") + " head=" + (head ? head.name : "none") + "; ");
                if (!t.visualPrefab || !t.muzzleVfx || !t.projectileVfx) problems++;
                if (head == null) problems++; // v6: Tesla has a rotatable coil head too
            }
            foreach (var n in new[] { "Drifter", "Skimmer", "Bulwark", "Splitter", "Shard" })
            {
                var e = Load<EnemyData>(D + "Enemies/" + n + ".asset");
                bool fb = e.visualPrefab && e.visualPrefab.GetComponent<EnemyHitFeedback>();
                var anim = e.visualPrefab ? e.visualPrefab.GetComponentInChildren<Animator>(true) : null;
                bool ac = anim && anim.runtimeAnimatorController;
                log.Append(n + "=" + (e.visualPrefab ? e.visualPrefab.name : "NULL") + " feedback=" + fb + " animator=" + ac + "; ");
                if (!fb || (!ac && n != "Shard")) problems++; // Shard ships without an AnimatorController (flash/squash/dissolve only)
            }
            if (!Resources.Load<GameObject>("StylizedVFX/PF_FX_DamageNumber")) { problems++; log.Append("DamageNumber prefab missing; "); }
            var lay = CheckLayout(); log.Append(lay + "; ");
            if (!lay.EndsWith("layoutProblems=0")) problems++;
            var artCheck = Load<ArtCatalog>("Assets/Game/Settings/ArtCatalog.asset");
            if ((layout0().levelDressing == null && (!artCheck.boardCliff || !artCheck.entryBridge)) || !artCheck.coreHitVfx) problems++;
            log.Append("v8 tiles=" + (artCheck.tileVariants?.Length ?? 0) + " flow=" + (artCheck.pathFlowSegment ? "y" : "n") + " ghosts=" + (artCheck.placeGhostBlock && artCheck.placeGhostTower ? "y" : "n") + " slot=" + (artCheck.slotHighlight ? "y" : "n") + "; ");
            foreach (var n in new[] { "Needle", "Pulse", "Seismic", "Chill" })
            {
                var t = Load<TowerData>(D + "Towers/" + n + ".asset");
                var m = t.visualPrefab ? t.visualPrefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith("_Muzzle")) : null;
                log.Append(n + " muzzle=" + (m ? m.name : "none") + "; ");
                if (m == null) problems++;
            }
            log.Append("problems=" + problems);
            return log.ToString();
        }

        // Tuanjie.exe -batchmode -nographics -projectPath <p> -executeMethod StoneSignal.EditorTools.StylizedGameplayWiring.BatchWire -logFile <f>
        public static void BatchWire()
        {
            try { Wire(); EditorApplication.Exit(Check().EndsWith("problems=0") ? 0 : 2); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
