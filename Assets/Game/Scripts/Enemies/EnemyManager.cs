using System;
using System.Collections.Generic;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    public enum TargetMode { First, Last, Strong, Close }
    public sealed class EnemyManager : MonoBehaviour
    {
        // Strike target priority for every tower (HUD "TARGET" button).
        public TargetMode Targeting = TargetMode.First;
        private GridManager grid;
        private VisualPalette palette;
        private readonly List<Enemy> active = new List<Enemy>();
        public IReadOnlyList<Enemy> Active => active;
        public PathfindingManager Paths { get; private set; }
        public Func<bool> CanMove { get; private set; }
        public event Action<Enemy, EnemyResolution> Resolved;
        public event Action<Enemy> Spawned;
        public event Action<int> ChildrenAdded;
        /// Spawn portal at an entry cell (GridView, v16.2); split children never use it.
        public Func<Vector2Int, StoneSignal.VFX.SpawnPortal> PortalAt;
        bool splitting; public bool IsSplitting => splitting;
        public Func<float> SpeedMultiplier = () => 1;
        private int nextSpawn;
        public void Initialize(GridManager map, PathfindingManager paths, VisualPalette colors, Func<bool> allowed)
        {
            grid = map; Paths = paths; palette = colors; CanMove = allowed;
            Paths.PathChanged += RepathAll;
        }
        // spawnIndex < 0 rotates through the board's spawn points; otherwise picks that entry (wrapped).
        public Enemy Spawn(EnemyData data, float hpScale = 1, float speedScale = 1, int spawnIndex = -1)
        {
            var entries = grid.Spawns;
            Vector2Int from = entries.Count == 0 ? grid.spawn : entries[(spawnIndex >= 0 ? spawnIndex : nextSpawn++) % entries.Count];
            GameObject obj;
            Enemy enemy = null;
            // Play mode: art enemies are pooled per EnemyData (no Instantiate/Destroy per spawn/death).
            if (data.visualPrefab != null && Application.isPlaying && pools.TryGetValue(data, out var stack))
                while (stack.Count > 0 && enemy == null) { var e = stack.Pop(); pooled.Remove(e); if (e != null && !e.Alive && !active.Contains(e)) enemy = e; }
            if (enemy != null) {
                obj = enemy.gameObject;
                obj.transform.SetParent(transform, false); obj.transform.localScale = Vector3.one; obj.transform.position = grid.ToWorld(from);
                obj.SetActive(true);
            }
            else if(data.visualPrefab!=null) {
                obj=new GameObject(data.displayName);obj.transform.SetParent(transform);obj.transform.position=grid.ToWorld(from);
                // Logical enemy height is +0.45 over the grid plane; the visual stands on the ground tile top.
                float ground = palette != null && palette.art != null ? palette.art.tileTop : 0;
                ArtVisual.Create(data.visualPrefab,obj.transform,obj.transform.position+Vector3.up*(ground-.45f));
                if (ArtSteps.On(1) && Application.isPlaying && palette != null && palette.art != null && palette.art.enemyGround != null)
                {
                    // v16.2 footprints: per-enemy blob + reporter to the single scene emitter (PF_VFX_EnemyGroundSystem, cap 64)
                    var g = ArtVisual.Create(palette.art.enemyGround, obj.transform, obj.transform.position);
                    var fx = g.GetComponent<StoneSignal.VFX.EnemyGroundFx>() ?? g.GetComponentInChildren<StoneSignal.VFX.EnemyGroundFx>();
                    if (fx != null) { StoneSignal.VFX.EnemyGroundFx.ClaimEnemy(obj.transform, fx); fx.groundY = ground - .45f; fx.SetFlying(data.flying); } // v17.2: one ground fx per enemy
                }
            } else obj=PrimitiveVisual.Create(data.displayName, data.kind == EnemyKind.Tank ? PrimitiveType.Cube : data.kind == EnemyKind.Splitter ? PrimitiveType.Sphere : PrimitiveType.Capsule, transform, grid.ToWorld(from), Vector3.one * (data.kind == EnemyKind.Tank ? .7f : .45f), data.fast ? palette.fastEnemy : palette.enemy);
            if (enemy == null) enemy = obj.AddComponent<Enemy>();
            enemy.Initialize(this, grid, data, palette, hpScale, speedScale, from);
            { var gfx = obj.GetComponentInChildren<StoneSignal.VFX.EnemyGroundFx>(false); if (gfx != null) gfx.SetFlying(data.flying); } // pooled reuse keeps the flag right (v17.2: active one only = the claimed runtime fx)
            if (!splitting && Application.isPlaying)
            {
                if (ArtSteps.On(3)) StoneSignal.VFX.SpawnRipple.Play(grid.TryPortalPoint(from, out _) ? grid.BridgeWaterPoint(from) : grid.ToWorld(from)); // ripple max radius 1.76 m: at the portal (island centre) it never reaches water
                if (ArtSteps.On(2) && PortalAt != null) { var portal = PortalAt(from); if (portal != null) { var held = enemy; held.HeldBySpawn = true; portal.PlaySpawn(obj.transform, .6f, () => { if (held != null) held.HeldBySpawn = false; }); } }
            }
            progress.Remove(enemy); // pooled reuse: never inherit the previous life's best distance (false ENEMY STUCK)
            active.Add(enemy); Spawned?.Invoke(enemy);
            return enemy;
        }
        public void Resolve(Enemy enemy, EnemyResolution reason)
        {
            if (!active.Remove(enemy)) return;
            progress.Remove(enemy);
            if(reason==EnemyResolution.Killed && enemy.Data.splitChild!=null && enemy.Data.splitCount>0) {
                // Reserve child count before parent resolution can complete a wave.
                ChildrenAdded?.Invoke(enemy.Data.splitCount);
                splitting = true;
                for(int i=0;i<enemy.Data.splitCount;i++) {
                    var child=Spawn(enemy.Data.splitChild,enemy.HPScale,enemy.SpeedScale,0);
                    child.ResumeFrom(enemy.transform.position,enemy.NavigationAnchor);
                    child.transform.localScale *= enemy.Data.splitChildScale;
                }
                splitting = false;
            }
            Resolved?.Invoke(enemy, reason);
        }
        private readonly Dictionary<EnemyData, Stack<Enemy>> pools = new Dictionary<EnemyData, Stack<Enemy>>();
        private readonly HashSet<Enemy> pooled = new HashSet<Enemy>();
        private readonly List<Enemy> scratch = new List<Enemy>();
        // Called by Enemy once its death presentation has finished (or immediately on escape/removal).
        public void Recycle(Enemy enemy)
        {
            if (enemy == null) return;
            if (!Application.isPlaying || enemy.Data == null || enemy.Data.visualPrefab == null) { PrimitiveVisual.DestroyObject(enemy.gameObject); return; }
            if (enemy.Alive || active.Contains(enemy) || !pooled.Add(enemy)) { Debug.LogWarning("Enemy recycle ignored (alive, active or already pooled): " + enemy.DebugState); return; }
            enemy.gameObject.SetActive(false);
            if (!pools.TryGetValue(enemy.Data, out var stack)) pools[enemy.Data] = stack = new Stack<Enemy>();
            stack.Push(enemy);
        }
        private void RepathAll()
        {
            progress.Clear(); // a longer detour is legitimate progress
            foreach (Enemy enemy in active) if (!enemy.Repath()) Debug.LogError("A live enemy was stranded by an invalid commit.");
        }
        public string ValidatePlacement(HashSet<Vector2Int> simulated)
        {
            foreach (Enemy enemy in active)
            {
                if (simulated.Contains(enemy.CurrentCell) || simulated.Contains(enemy.NavigationAnchor)) return "Enemy is crossing this cell";
                if (Paths.FindPath(enemy.NavigationAnchor, grid.goal, simulated).Count == 0) return "An enemy would be trapped";
            }
            return null;
        }
        public Enemy ClosestToGoal(Vector3 position, float range)
        {
            Enemy best = null; float bestScore = float.MaxValue;
            foreach (Enemy enemy in active)
            {
                float d2 = (enemy.transform.position - position).sqrMagnitude;
                if (!enemy.Alive || d2 > range * range) continue;
                float score = Targeting == TargetMode.First ? enemy.RemainingDistance : Targeting == TargetMode.Last ? -enemy.RemainingDistance : Targeting == TargetMode.Strong ? -enemy.HP : d2;
                if (score < bestScore) { best = enemy; bestScore = score; }
            }
            return best;
        }
        public void DamageArea(Vector3 position, float radius, float damage) => DamageArea(position, radius, damage, DamageKind.Physical, false);
        public void DamageArea(Vector3 position, float radius, float damage, DamageKind kind, bool crit)
        {
            // Damage can remove entries immediately; iterate a small snapshot.
            scratch.Clear(); scratch.AddRange(active); // no per-hit array allocation
            foreach (Enemy enemy in scratch)
                if (enemy.Alive && (enemy.transform.position - position).sqrMagnitude <= radius * radius) enemy.TakeDamage(damage, kind, crit);
        }
        // Regression guard: an enemy that makes no movement for StuckSeconds of movement time is logged
        // ("ENEMY STUCK"), re-routed, and removed if no route exists, so a wave can never hang forever.
        public const float StuckSeconds = 5f;
        private readonly Dictionary<Enemy, (float best, float idle)> progress = new Dictionary<Enemy, (float, float)>();
        public int StuckEvents { get; private set; }
        private void Update()
        {
            if (CanMove == null || !CanMove()) return;
            scratch.Clear(); scratch.AddRange(active);
            foreach (var e in scratch)
            {
                if (e == null || !e.Alive || !e.gameObject.activeInHierarchy)
                {
                    // registry entry without a live, active body: release it so the wave count can complete
                    StuckEvents++; Debug.LogWarning("ENEMY STUCK orphan entry " + (e != null ? e.DebugState : "null"));
                    active.Remove(e); if (e != null) Resolved?.Invoke(e, EnemyResolution.Removed); continue;
                }
                // progress = remaining route distance must keep shrinking (catches frozen, ping-ponging and off-route enemies)
                float rem = e.RemainingDistance;
                float best = progress.TryGetValue(e, out var s) ? s.best : float.MaxValue;
                float idle = rem < best - .02f ? 0 : s.idle + Time.deltaTime;
                progress[e] = (Mathf.Min(best, rem), idle);
                if (idle < StuckSeconds) continue;
                StuckEvents++; progress[e] = (rem, 0);
                Debug.LogWarning("ENEMY STUCK " + e.DebugState);
                if (!e.RecoverRoute()) { Debug.LogWarning("ENEMY STUCK removed"); e.ForceRemove(); }
            }
            if (progress.Count > active.Count * 2 + 16) { var keep = new HashSet<Enemy>(active); foreach (var k in new List<Enemy>(progress.Keys)) if (!keep.Contains(k)) progress.Remove(k); }
        }
        private void OnDestroy() { if (Paths != null) Paths.PathChanged -= RepathAll; }
    }
}
