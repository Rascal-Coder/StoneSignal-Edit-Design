using System;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    public sealed class Tower : MonoBehaviour
    {
        private EnemyManager enemies;
        private VisualPalette palette;
        private RunModifiers modifiers;
        private Func<bool> canAttack;
        private Transform projectileRoot;
        private Transform visual, head, barrel, muzzle;
        private float cooldown, visualElevation;
        private Vector3 aimDirection = Vector3.forward;
        private GameObject arcSource; private bool isArc; // cached: no GetComponent per shot
        public TowerData Data { get; private set; }
        public Vector2Int Origin { get; private set; }
        public Vector2Int Size { get; private set; } = Vector2Int.one;
        public int Rotation { get; private set; }
        public System.Collections.Generic.IReadOnlyList<Vector2Int> Cells { get; private set; } = new Vector2Int[0];
        public void SetFootprint(Vector2Int origin, Vector2Int size, int rotation, System.Collections.Generic.IReadOnlyList<Vector2Int> cells)
        {
            Origin = origin; Size = size; Rotation = rotation & 3; Cells = cells;
            transform.rotation = Quaternion.Euler(0, 90 * Rotation, 0); // model stays centred on the footprint centre (transform.position)
        }
        // Runes inlaid under the footprint (TowerManager.RecomputeRunes): per-mille effects, already stacked/resonated.
        public RuneStats Runes { get; private set; }
        public System.Collections.Generic.List<int> RuneList { get; } = new System.Collections.Generic.List<int>();
        private float cellSize = 1;
        public void SetRunes(RuneStats stats, float cell) { Runes = stats; cellSize = cell; }
        public float Range => modifiers.Range(Data) + Runes.RangeCells * cellSize;
        public float Damage => Data.damage * modifiers.Damage * Runes.DamageMul;
        public float AttackRate => Data.attacksPerSecond * modifiers.AttackSpeed * Runes.AttackSpeedMul;
        public float SplashRadius => modifiers.Radius(Data);
        public Transform Head => head;
        // elevation = world height of the surface the tower stands on (ground tile top or wall block top).
        public void Initialize(TowerData data, EnemyManager registry, VisualPalette colors, RunModifiers upgrades, Func<bool> allowed, Transform missiles, float elevation = 0)
        {
            Data = data; enemies = registry; palette = colors; modifiers = upgrades; canAttack = allowed; projectileRoot = missiles;
            visualElevation=elevation;
            if(data.visualPrefab!=null) {
                visual=ArtVisual.Create(data.visualPrefab,transform,transform.position+Vector3.up*elevation).transform;
                head=FindPart(visual,data.headName,"_Head");
                barrel=FindPart(head!=null?head:visual,null,"_Barrel");
                muzzle=FindPart(head!=null?head:visual,null,"_Muzzle");
                return;
            }
            Material mat = data.kind == TowerKind.Arrow ? colors.arrow : data.kind == TowerKind.Rapid ? colors.rapid : data.kind == TowerKind.Chill ? colors.path : colors.cannon;
            PrimitiveVisual.Create("Foundation", PrimitiveType.Cylinder, transform, transform.position + Vector3.up * .25f, new Vector3(.8f,.25f,.8f), colors.towerBase);
            head = PrimitiveVisual.Create(data.displayName, data.kind == TowerKind.Cannon ? PrimitiveType.Cube : PrimitiveType.Cylinder, transform, transform.position + Vector3.up * .75f, new Vector3(.48f,.25f,.48f), mat).transform;
            visual = head;
            PrimitiveVisual.Create("Barrel", PrimitiveType.Cube, head, head.position + Vector3.forward * .3f, new Vector3(.13f,.13f,.6f), mat);
        }
        // Stylized tower hierarchy: Rig > *_Base (static) / *_Head (yaw) > [*_Barrel (pitch)] > *_Muzzle (+Z = fire direction).
        private static Transform FindPart(Transform root, string explicitName, string suffix)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.IsNullOrEmpty(explicitName) ? t.name == explicitName : t.name.EndsWith(suffix)) return t;
            }
            return null;
        }
        private void Aim(Enemy target)
        {
            Vector3 dir = target.transform.position - transform.position; dir.y = 0;
            if (dir.sqrMagnitude < .001f) return;
            aimDirection = dir.normalized;
            if (head == null) return;
            // Yaw the head so the muzzle's horizontal heading meets the target (muzzle may point straight up, e.g. Tesla).
            Vector3 facing = muzzle != null ? muzzle.forward : head.forward; facing.y = 0;
            if (facing.sqrMagnitude < .01f) { facing = head.forward; facing.y = 0; }
            if (facing.sqrMagnitude < .01f) { facing = head.right; facing.y = 0; }
            if (facing.sqrMagnitude > .0001f)
                head.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(facing, aimDirection, Vector3.up), Vector3.up) * head.rotation;
            // Direct-fire barrels pitch toward the target; lobbed barrels keep their authored elevation.
            if (barrel != null && muzzle != null && !Data.lobbedShot)
            {
                Vector3 axis = barrel.right;
                Vector3 want = Vector3.ProjectOnPlane(target.transform.position - muzzle.position, axis);
                Vector3 have = Vector3.ProjectOnPlane(muzzle.forward, axis);
                if (want.sqrMagnitude > .0001f && have.sqrMagnitude > .0001f)
                    barrel.rotation = Quaternion.AngleAxis(Mathf.Clamp(Vector3.SignedAngle(have, want, axis), -30, 30), axis) * barrel.rotation;
            }
        }
        private void Update()
        {
            if (canAttack == null || !canAttack()) return;
            cooldown -= Time.deltaTime;
            if (cooldown > 0) return;
            Enemy target = enemies.ClosestToGoal(transform.position + Vector3.up * .45f, Range);
            if (target == null) { cooldown = .1f; return; }
            Aim(target);
            Fire(target);
            cooldown = 1 / AttackRate;
        }
        private (Vector3 pos, Quaternion rot) Muzzle()
        {
            if (muzzle != null) return (muzzle.position, muzzle.rotation);
            Vector3 basePos = transform.position + Vector3.up * visualElevation;
            Vector3 pos = basePos + Vector3.up * Data.muzzleHeight + aimDirection * Data.muzzleForward;
            return (pos, Quaternion.LookRotation(aimDirection));
        }
        public Projectile Fire(Enemy target)
        {
            var (muzzlePos, muzzleRot) = Muzzle();
            if (Data.muzzleVfx != null) StylizedVfx.Play(Data.muzzleVfx, muzzlePos, muzzleRot);
            bool crit = GameRng.Gameplay.Permille(GameRng.ToPermille(Data.critChance));
            float damage = Damage * (crit ? Data.critMultiplier : 1);
            GameObject obj;
            if (arcSource != Data.projectileVfx) { arcSource = Data.projectileVfx; isArc = arcSource != null && arcSource.GetComponent<TeslaArc>() != null; }
            bool arc = isArc;
            if (Data.projectileVfx != null && !arc)
            {
                var shot = Projectile.Get(Data.displayName + " shot", projectileRoot, muzzlePos); obj = shot.gameObject;
                var body = StylizedVfx.Play(Data.projectileVfx, muzzlePos, muzzleRot, obj.transform);
                if (body != null) { body.transform.localPosition = Vector3.zero; shot.AttachBody(body); }
            }
            else if (arc)
            {
                // Lightning is a short-lived arc; the gameplay projectile itself stays invisible so hit timing is unchanged.
                var bolt = StylizedVfx.Play(Data.projectileVfx, muzzlePos);
                if (bolt != null) bolt.GetComponent<TeslaArc>().SetEndpoints(muzzlePos, target.transform.position + Vector3.up * .15f);
                obj = Projectile.Get(Data.displayName + " arc", projectileRoot, muzzlePos).gameObject;
            }
            else obj = PrimitiveVisual.Create("Signal bolt", PrimitiveType.Sphere, projectileRoot, transform.position + Vector3.up * (.9f+visualElevation), Vector3.one * (Data.kind == TowerKind.Cannon ? .25f : .12f), Data.kind==TowerKind.Chill ? palette.path : Data.kind==TowerKind.Rapid ? palette.rapid : Data.kind==TowerKind.Cannon ? palette.cannon : palette.arrow);
            Projectile projectile = obj.GetComponent<Projectile>(); if (projectile == null) projectile = obj.AddComponent<Projectile>();
            float slow = Mathf.Max(Data.slowFraction, Runes.SlowFraction), slowFor = Runes.slow > 0 ? Mathf.Max(Data.slowDuration, Runes.SlowSeconds) : Data.slowDuration;
            projectile.Initialize(target, enemies, Data.projectileSpeed, damage, SplashRadius, canAttack, slow, slowFor); projectile.Owner = this;
            projectile.SetPresentation(Data, crit, Data.lobbedShot ? Data.lobHeight : 0);
            return projectile;
        }
    }
}
