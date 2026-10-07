using System;
using System.Linq;
using UnityEditor;
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
            Tower("Needle", "PF_Tower_Gatling_1x1", DamageKind.Physical, "FX_Muzzle_Gatling", "FX_Proj_Bullet", "FX_Hit_Kinetic", "FX_Explosion_Kinetic", t =>
            { t.explosionOnCrit = true; t.critChance = .1f; t.critMultiplier = 1.5f; t.impactHitStop = .03f; });
            Tower("Pulse", "PF_Tower_Tesla_1x1", DamageKind.Lightning, "FX_Muzzle_Tesla", "FX_Proj_Tesla_Arc", "FX_Hit_Lightning", "FX_Explosion_Lightning", t =>
            { t.explosionOnKill = true; t.muzzleHeight = .9f; t.muzzleForward = 0; });
            Tower("Seismic", "PF_Tower_Mortar_2x2", DamageKind.Explosive, "FX_Muzzle_Mortar", "FX_Proj_MortarShell", null, "FX_Explosion_HE", t =>
            { t.explosionOnEveryHit = true; t.lobbedShot = true; t.lobHeight = 1.5f; t.impactShake = FeedbackShake.Light; });
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
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("STYLIZED WIRING: " + Check());
        }

        static void Tower(string asset, string prefab, DamageKind kind, string muzzle, string proj, string hit, string boom, Action<TowerData> extra)
        {
            var t = Load<TowerData>(D + "Towers/" + asset + ".asset");
            t.visualPrefab = Pf(prefab); t.damageKind = kind; t.headName = "";
            t.muzzleVfx = Fx(muzzle); t.projectileVfx = Fx(proj); t.hitVfx = hit != null ? Fx(hit) : null; t.explosionVfx = boom != null ? Fx(boom) : null;
            t.explosionOnEveryHit = t.explosionOnCrit = t.explosionOnKill = t.lobbedShot = false;
            t.critChance = 0; t.critMultiplier = 1.5f; t.muzzleHeight = .65f; t.muzzleForward = .45f; t.lobHeight = 1.5f;
            t.impactShake = FeedbackShake.None; t.impactHitStop = 0;
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
                if (head == null && n != "Pulse") problems++;
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
