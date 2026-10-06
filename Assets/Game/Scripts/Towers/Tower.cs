using System;
using UnityEngine;

namespace StoneSignal
{
    public sealed class Tower : MonoBehaviour
    {
        private EnemyManager enemies;
        private VisualPalette palette;
        private RunModifiers modifiers;
        private Func<bool> canAttack;
        private Transform projectileRoot;
        private Transform turret;
        private float cooldown, visualElevation;
        public TowerData Data { get; private set; }
        public float Range => modifiers.Range(Data);
        public float Damage => Data.damage * modifiers.Damage;
        public float AttackRate => Data.attacksPerSecond * modifiers.AttackSpeed;
        public float SplashRadius => modifiers.Radius(Data);
        public void Initialize(TowerData data, EnemyManager registry, VisualPalette colors, RunModifiers upgrades, Func<bool> allowed, Transform missiles, float elevation = 0)
        {
            Data = data; enemies = registry; palette = colors; modifiers = upgrades; canAttack = allowed; projectileRoot = missiles;
            visualElevation=elevation;
            if(data.visualPrefab!=null) {
                turret=ArtVisual.Create(data.visualPrefab,transform,transform.position+Vector3.up*elevation).transform;
                return;
            }
            Material mat = data.kind == TowerKind.Arrow ? colors.arrow : data.kind == TowerKind.Rapid ? colors.rapid : data.kind == TowerKind.Chill ? colors.path : colors.cannon;
            PrimitiveVisual.Create("Foundation", PrimitiveType.Cylinder, transform, transform.position + Vector3.up * .25f, new Vector3(.8f,.25f,.8f), colors.towerBase);
            turret = PrimitiveVisual.Create(data.displayName, data.kind == TowerKind.Cannon ? PrimitiveType.Cube : PrimitiveType.Cylinder, transform, transform.position + Vector3.up * .75f, new Vector3(.48f,.25f,.48f), mat).transform;
            PrimitiveVisual.Create("Barrel", PrimitiveType.Cube, turret, turret.position + Vector3.forward * .3f, new Vector3(.13f,.13f,.6f), mat);
        }
        private void Update()
        {
            if (canAttack == null || !canAttack()) return;
            cooldown -= Time.deltaTime;
            if (cooldown > 0) return;
            Enemy target = enemies.ClosestToGoal(transform.position + Vector3.up * .45f, Range);
            if (target == null) { cooldown = .1f; return; }
            Vector3 aim = target.transform.position - turret.position; aim.y = 0;
            if (aim.sqrMagnitude > .001f) turret.rotation = Quaternion.LookRotation(aim);
            Fire(target);
            cooldown = 1 / AttackRate;
        }
        public Projectile Fire(Enemy target)
        {
            GameObject obj = PrimitiveVisual.Create("Signal bolt", PrimitiveType.Sphere, projectileRoot, transform.position + Vector3.up * (.9f+visualElevation), Vector3.one * (Data.kind == TowerKind.Cannon ? .25f : .12f), Data.kind==TowerKind.Chill ? palette.path : Data.kind==TowerKind.Rapid ? palette.rapid : Data.kind==TowerKind.Cannon ? palette.cannon : palette.arrow);
            Projectile projectile = obj.AddComponent<Projectile>();
            projectile.Initialize(target, enemies, Data.projectileSpeed, Damage, SplashRadius, canAttack, Data.slowFraction, Data.slowDuration);
            return projectile;
        }
    }
}
