using System;
using System.Collections.Generic;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    public sealed class EnemyManager : MonoBehaviour
    {
        private GridManager grid;
        private VisualPalette palette;
        private readonly List<Enemy> active = new List<Enemy>();
        public IReadOnlyList<Enemy> Active => active;
        public PathfindingManager Paths { get; private set; }
        public Func<bool> CanMove { get; private set; }
        public event Action<Enemy, EnemyResolution> Resolved;
        public event Action<Enemy> Spawned;
        public event Action<int> ChildrenAdded;
        public Func<float> SpeedMultiplier = () => 1;
        public void Initialize(GridManager map, PathfindingManager paths, VisualPalette colors, Func<bool> allowed)
        {
            grid = map; Paths = paths; palette = colors; CanMove = allowed;
            Paths.PathChanged += RepathAll;
        }
        public Enemy Spawn(EnemyData data, float hpScale = 1, float speedScale = 1)
        {
            GameObject obj;
            if(data.visualPrefab!=null) {
                obj=new GameObject(data.displayName);obj.transform.SetParent(transform);obj.transform.position=grid.ToWorld(grid.spawn);
                // Logical enemy height is +0.45 over the grid plane; the visual stands on the ground tile top.
                float ground = palette != null && palette.art != null ? palette.art.tileTop : 0;
                ArtVisual.Create(data.visualPrefab,obj.transform,obj.transform.position+Vector3.up*(ground-.45f));
            } else obj=PrimitiveVisual.Create(data.displayName, data.kind == EnemyKind.Tank ? PrimitiveType.Cube : data.kind == EnemyKind.Splitter ? PrimitiveType.Sphere : PrimitiveType.Capsule, transform, grid.ToWorld(grid.spawn), Vector3.one * (data.kind == EnemyKind.Tank ? .7f : .45f), data.fast ? palette.fastEnemy : palette.enemy);
            Enemy enemy = obj.AddComponent<Enemy>();
            enemy.Initialize(this, grid, data, palette, hpScale, speedScale);
            active.Add(enemy); Spawned?.Invoke(enemy);
            return enemy;
        }
        public void Resolve(Enemy enemy, EnemyResolution reason)
        {
            if (!active.Remove(enemy)) return;
            if(reason==EnemyResolution.Killed && enemy.Data.splitChild!=null && enemy.Data.splitCount>0) {
                // Reserve child count before parent resolution can complete a wave.
                ChildrenAdded?.Invoke(enemy.Data.splitCount);
                for(int i=0;i<enemy.Data.splitCount;i++) {
                    var child=Spawn(enemy.Data.splitChild,enemy.HPScale,enemy.SpeedScale);
                    child.ResumeFrom(enemy.transform.position,enemy.NavigationAnchor);
                    child.transform.localScale *= enemy.Data.splitChildScale;
                }
            }
            Resolved?.Invoke(enemy, reason);
        }
        private void RepathAll()
        {
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
            Enemy best = null; float remaining = float.MaxValue;
            foreach (Enemy enemy in active)
                if (enemy.Alive && (enemy.transform.position - position).sqrMagnitude <= range * range && enemy.RemainingDistance < remaining)
                { best = enemy; remaining = enemy.RemainingDistance; }
            return best;
        }
        public void DamageArea(Vector3 position, float radius, float damage) => DamageArea(position, radius, damage, DamageKind.Physical, false);
        public void DamageArea(Vector3 position, float radius, float damage, DamageKind kind, bool crit)
        {
            // Damage can remove entries immediately; iterate a small snapshot.
            foreach (Enemy enemy in active.ToArray())
                if (enemy.Alive && (enemy.transform.position - position).sqrMagnitude <= radius * radius) enemy.TakeDamage(damage, kind, crit);
        }
        private void OnDestroy() { if (Paths != null) Paths.PathChanged -= RepathAll; }
    }
}
