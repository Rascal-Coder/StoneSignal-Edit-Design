using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public enum EnemyResolution { Killed, Escaped, Removed }
    public sealed class Enemy : MonoBehaviour
    {
        private EnemyManager owner;
        private GridManager grid;
        private List<Vector2Int> path;
        private int node;
        private float speed;
        private float slowMultiplier=1, slowRemaining;
        public float HPScale { get; private set; }
        public float SpeedScale { get; private set; }
        private float maxHP;
        private Renderer[] bodies;
        private MaterialPropertyBlock tint;
        private float flashUntil;
        private Transform healthFill;
        public EnemyData Data { get; private set; }
        public float HP { get; private set; }
        public bool Alive { get; private set; }
        public Vector2Int NavigationAnchor => path != null && node < path.Count ? path[node] : grid.goal;
        public Vector2Int CurrentCell => grid.ToCell(transform.position);
        public float RemainingDistance
        {
            get
            {
                if (!Alive || path == null || node >= path.Count) return 0;
                return Vector3.Distance(transform.position, World(path[node])) + (path.Count - node - 1) * grid.cellSize;
            }
        }
        public event System.Action<Enemy, float> Damaged;

        public void Initialize(EnemyManager manager, GridManager map, EnemyData data, VisualPalette palette, float hpScale, float speedScale)
        {
            owner = manager; grid = map; Data = data; HPScale=hpScale; SpeedScale=speedScale;
            HP = maxHP = data.hp * hpScale; speed = data.moveSpeed * speedScale; Alive = true;
            transform.position = World(grid.spawn);
            bodies = GetComponentsInChildren<Renderer>();
            tint = new MaterialPropertyBlock();
            var back = PrimitiveVisual.Create("HP background", PrimitiveType.Cube, owner.transform, transform.position + Vector3.up * .75f, new Vector3(.65f,.055f,.08f), palette.invalid);
            healthFill = PrimitiveVisual.Create("HP", PrimitiveType.Cube, back.transform, back.transform.position + Vector3.up * .005f, new Vector3(.65f,.055f,.08f), palette.valid).transform;
            healthFill.localScale = Vector3.one;
            hpBar = back.transform;
            path = owner.Paths.FindPath(grid.spawn, grid.goal); node = 0;
        }
        public void ResumeFrom(Vector3 position,Vector2Int anchor) {
            transform.position=position; path=owner.Paths.FindPath(anchor,grid.goal); node=0;
            if(hpBar!=null) hpBar.position=position+Vector3.up*.75f;
        }
        public void ApplySlow(float fraction,float duration) {
            if(!Alive || duration<=0) return;
            slowMultiplier=Mathf.Min(slowMultiplier,1-Mathf.Clamp01(fraction)); slowRemaining=Mathf.Max(slowRemaining,duration);
        }
        private Transform hpBar;
        private Vector3 World(Vector2Int cell) => grid.ToWorld(cell) + Vector3.up * .45f;
        public bool Repath()
        {
            // Finish the current edge to its reserved destination; never relocate the transform.
            Vector2Int anchor = NavigationAnchor;
            List<Vector2Int> newPath = owner.Paths.FindPath(anchor, grid.goal);
            if (newPath.Count == 0) return false;
            path = newPath; node = 0;
            return true;
        }
        private void Update() { if (Alive && owner.CanMove()) Advance(Time.deltaTime); }
        public void Advance(float deltaTime)
        {
            if (!Alive || path == null || path.Count == 0) return;
            Vector3 previousPosition=transform.position;
            float distance = speed * owner.SpeedMultiplier() * slowMultiplier * deltaTime;
            slowRemaining-=deltaTime; if(slowRemaining<=0) slowMultiplier=1;
            while (distance > 0 && node < path.Count)
            {
                Vector3 target = World(path[node]);
                float remaining = Vector3.Distance(transform.position, target);
                if (remaining <= distance)
                {
                    transform.position = target; distance -= remaining; node++;
                }
                else { transform.position = Vector3.MoveTowards(transform.position, target, distance); distance = 0; }
            }
            Vector3 heading=transform.position-previousPosition; heading.y=0;
            if(heading.sqrMagnitude>.00001f) transform.rotation=Quaternion.LookRotation(heading);
            if (hpBar != null) hpBar.position = transform.position + Vector3.up * .75f;
            if (node >= path.Count) Resolve(EnemyResolution.Escaped);
        }
        public void TakeDamage(float damage)
        {
            if (!Alive || damage <= 0) return;
            HP = Mathf.Max(0, HP - damage);
            flashUntil = Time.time + .09f;
            if (healthFill != null) healthFill.localScale = new Vector3(HP / maxHP, 1, 1);
            Damaged?.Invoke(this, damage);
            if (HP <= 0) Resolve(EnemyResolution.Killed);
        }
        private void Resolve(EnemyResolution reason)
        {
            if (!Alive) return;
            Alive = false;
            owner.Resolve(this, reason);
            if (hpBar != null) PrimitiveVisual.DestroyObject(hpBar.gameObject);
            if (Application.isPlaying && reason == EnemyResolution.Killed) StartCoroutine(ShrinkDeath());
            else PrimitiveVisual.DestroyObject(gameObject);
        }
        private void LateUpdate()
        {
            if (bodies == null) return;
            bool flash=Time.time<flashUntil;
            tint.SetColor("_BaseColor",Color.white*2);
            foreach(var body in bodies) if(body!=null) body.SetPropertyBlock(flash ? tint : null);
        }
        private System.Collections.IEnumerator ShrinkDeath()
        {
            Vector3 initial = transform.localScale;
            float elapsed = 0;
            while (elapsed < .22f) { elapsed += Time.deltaTime; transform.localScale = initial * Mathf.Max(0,1-elapsed/.22f); yield return null; }
            Destroy(gameObject);
        }
        private void OnDestroy()
        {
            if (hpBar != null) PrimitiveVisual.DestroyObject(hpBar.gameObject);
            if (Alive && owner != null) { Alive = false; owner.Resolve(this, EnemyResolution.Removed); }
        }
    }
}
