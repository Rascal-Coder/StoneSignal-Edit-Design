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
        private Transform visual, head;
        private float cooldown, visualElevation;
        private Vector3 aimDirection = Vector3.forward;
        public TowerData Data { get; private set; }
        public float Range => modifiers.Range(Data);
        public float Damage => Data.damage * modifiers.Damage;
        public float AttackRate => Data.attacksPerSecond * modifiers.AttackSpeed;
        public float SplashRadius => modifiers.Radius(Data);
        public Transform Head => head;
        // elevation = world height of the surface the tower stands on (ground tile top or wall block top).
        public void Initialize(TowerData data, EnemyManager registry, VisualPalette colors, RunModifiers upgrades, Func<bool> allowed, Transform missiles, float elevation = 0)
        {
            Data = data; enemies = registry; palette = colors; modifiers = upgrades; canAttack = allowed; projectileRoot = missiles;
            visualElevation=elevation;
            if(data.visualPrefab!=null) {
                visual=ArtVisual.Create(data.visualPrefab,transform,transform.position+Vector3.up*elevation).transform;
                head=FindHead(visual,data.headName);
                return;
            }
            Material mat = data.kind == TowerKind.Arrow ? colors.arrow : data.kind == TowerKind.Rapid ? colors.rapid : data.kind == TowerKind.Chill ? colors.path : colors.cannon;
            PrimitiveVisual.Create("Foundation", PrimitiveType.Cylinder, transform, transform.position + Vector3.up * .25f, new Vector3(.8f,.25f,.8f), colors.towerBase);
            head = PrimitiveVisual.Create(data.displayName, data.kind == TowerKind.Cannon ? PrimitiveType.Cube : PrimitiveType.Cylinder, transform, transform.position + Vector3.up * .75f, new Vector3(.48f,.25f,.48f), mat).transform;
            visual = head;
            PrimitiveVisual.Create("Barrel", PrimitiveType.Cube, head, head.position + Vector3.forward * .3f, new Vector3(.13f,.13f,.6f), mat);
        }
        // Stylized tower prefabs expose their rotatable turret as a child named "*_Head" (Tesla has none).
        private static Transform FindHead(Transform root, string explicitName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.IsNullOrEmpty(explicitName) ? t.name == explicitName : t.name.EndsWith("_Head")) return t;
            }
            return null;
        }
        private void Update()
        {
            if (canAttack == null || !canAttack()) return;
            cooldown -= Time.deltaTime;
            if (cooldown > 0) return;
            Enemy target = enemies.ClosestToGoal(transform.position + Vector3.up * .45f, Range);
            if (target == null) { cooldown = .1f; return; }
            Vector3 aim = target.transform.position - transform.position; aim.y = 0;
            if (aim.sqrMagnitude > .001f) { aimDirection = aim.normalized; if (head != null) head.rotation = Quaternion.LookRotation(aimDirection); }
            Fire(target);
            cooldown = 1 / AttackRate;
        }
        private (Vector3 pos, Quaternion rot) Muzzle()
        {
            Vector3 basePos = transform.position + Vector3.up * visualElevation;
            if (Data.lobbedShot && head != null) return MortarBarrelTip(head, aimDirection);
            Vector3 pos = basePos + Vector3.up * Data.muzzleHeight + aimDirection * Data.muzzleForward;
            return (pos, Quaternion.LookRotation(aimDirection));
        }
        // Runtime copy of StylizedVFXBuilder.MortarBarrelTip (editor-only): top of head bounds, aimed up along a 60 degree lob.
        public static (Vector3 pos, Quaternion rot) MortarBarrelTip(Transform head, Vector3 dir)
        {
            var rs = head.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return (head.position + Vector3.up, Quaternion.LookRotation(Vector3.up));
            Bounds b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            var aim = (dir * Mathf.Cos(60 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(60 * Mathf.Deg2Rad)).normalized;
            return (new Vector3(b.center.x, b.max.y, b.center.z) + dir * b.extents.x * .35f, Quaternion.LookRotation(aim));
        }
        public Projectile Fire(Enemy target)
        {
            var (muzzlePos, muzzleRot) = Muzzle();
            if (Data.muzzleVfx != null) StylizedVfx.Play(Data.muzzleVfx, muzzlePos, muzzleRot);
            bool crit = Data.critChance > 0 && UnityEngine.Random.value < Data.critChance;
            float damage = Damage * (crit ? Data.critMultiplier : 1);
            GameObject obj;
            bool arc = Data.projectileVfx != null && Data.projectileVfx.GetComponent<TeslaArc>() != null;
            if (Data.projectileVfx != null && !arc)
            {
                obj = new GameObject(Data.displayName + " shot"); obj.transform.SetParent(projectileRoot); obj.transform.position = muzzlePos;
                var body = StylizedVfx.Play(Data.projectileVfx, muzzlePos, muzzleRot, obj.transform);
                if (body != null) body.transform.localPosition = Vector3.zero;
            }
            else if (arc)
            {
                // Lightning is a short-lived arc; the gameplay projectile itself stays invisible so hit timing is unchanged.
                var bolt = StylizedVfx.Play(Data.projectileVfx, muzzlePos);
                if (bolt != null) bolt.GetComponent<TeslaArc>().SetEndpoints(muzzlePos, target.transform.position + Vector3.up * .15f);
                obj = new GameObject(Data.displayName + " arc"); obj.transform.SetParent(projectileRoot); obj.transform.position = muzzlePos;
            }
            else obj = PrimitiveVisual.Create("Signal bolt", PrimitiveType.Sphere, projectileRoot, transform.position + Vector3.up * (.9f+visualElevation), Vector3.one * (Data.kind == TowerKind.Cannon ? .25f : .12f), Data.kind==TowerKind.Chill ? palette.path : Data.kind==TowerKind.Rapid ? palette.rapid : Data.kind==TowerKind.Cannon ? palette.cannon : palette.arrow);
            Projectile projectile = obj.AddComponent<Projectile>();
            projectile.Initialize(target, enemies, Data.projectileSpeed, damage, SplashRadius, canAttack, Data.slowFraction, Data.slowDuration);
            projectile.SetPresentation(Data, crit, Data.lobbedShot ? Data.lobHeight : 0);
            return projectile;
        }
    }
}
