using System.Collections.Generic;
using UnityEngine;
using StoneSignal.VFX;

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
        // Quaternius monsters ship skeletal walk/death clips on the visual prefab.
        private Animator animator;
        private float deathHold;
        private static readonly int DeathTrigger = Animator.StringToHash(EnemyVisualContract.DieTrigger);
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
        public string DebugState => $"{(Data != null ? Data.name : "?")} alive={Alive} active={gameObject.activeInHierarchy} pos={transform.position} cell={(grid != null ? CurrentCell : default)} anchor={(grid != null ? NavigationAnchor : default)} node={node}/{(path != null ? path.Count : -1)} hp={HP:0.#} enabled={enabled} speed={speed:0.##} slow={slowMultiplier:0.##}/{slowRemaining:0.##} mult={(owner!=null?owner.SpeedMultiplier():-1):0.##} target={(path!=null&&node<path.Count?World(path[node]):Vector3.zero)} ts={Time.timeScale} lastAdv={lastAdvance:0.##} now={Time.time:0.##} step={lastStep:0.###} ext={extWrites} lastExt={lastExt} dt={Time.deltaTime:0.###}";
        float lastAdvance, lastStep; Vector3 lastEnd, lastExt; bool hasEnd; int extWrites;
        // Stuck guard: re-route from the current cell; false when no route exists (caller removes the enemy).
        public bool RecoverRoute()
        {
            var from = grid.ToCell(transform.position);
            var p = owner.Paths.FindPath(grid.Walkable(from) ? from : NavigationAnchor, grid.goal);
            if (p.Count == 0) return false;
            path = p; node = 0; return true;
        }
        /// Tower whose shot last hit this enemy (rune bounty); cleared on spawn.
        public Tower LastAttacker { get; set; }
        public void ForceRemove() => Resolve(EnemyResolution.Removed);
        public event System.Action<Enemy, float> Damaged;
        // Presentation info about the most recent hit, read by CombatFeedback inside Damaged.
        public DamageKind LastHitKind { get; private set; }
        public bool LastHitCrit { get; private set; }
        public EnemyHitFeedback Feedback { get; private set; }

        public void Initialize(EnemyManager manager, GridManager map, EnemyData data, VisualPalette palette, float hpScale, float speedScale, Vector2Int? from = null)
        {
            LastAttacker = null; hasEnd = false; extWrites = 0; heldBySpawn = false; barFade = -1; flyChecked = false; fly = null; spawnFrame = Time.frameCount;
            owner = manager; grid = map; Data = data; HPScale=hpScale; SpeedScale=speedScale;
            HP = maxHP = data.hp * hpScale; speed = data.moveSpeed * speedScale; Alive = true;
            Vector2Int start = from ?? grid.spawn;
            transform.position = World(start);
            if (owner != null && !owner.IsSplitting && Application.isPlaying) { if (grid.TryPortalPoint(start, out var pp)) transform.position = new Vector3(pp.x, transform.position.y + (pp.y - grid.ToWorld(start).y), pp.z); }
            Damaged = null; slowMultiplier = 1; slowRemaining = 0; flashUntil = 0; LastHitCrit = false;
            if (hpBar == null)
            {
                bodies = GetComponentsInChildren<Renderer>();
                tint = new MaterialPropertyBlock();
                var back = PrimitiveVisual.Create("HP background", PrimitiveType.Cube, owner.transform, transform.position + Vector3.up * BarHeight(), new Vector3(.65f,.055f,.08f), palette.invalid);
                healthFill = PrimitiveVisual.Create("HP", PrimitiveType.Cube, back.transform, back.transform.position + Vector3.up * .005f, new Vector3(.65f,.055f,.08f), palette.valid).transform;
                hpBar = back.transform; barScale = hpBar.localScale;
                foreach (var r in back.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; if (ScreenSpaceBars && Application.isPlaying) r.enabled = false; }
            }
            else { hpBar.gameObject.SetActive(true); hpBar.localScale = barScale; hpBar.position = transform.position + Vector3.up * BarHeight(); } // pooled reuse
            SetFill(1); // pooled reuse resets to full
            path = owner.Paths.FindPath(start, grid.goal); node = 0;
            FaceNextNode();
            BindAnimator();
        }
        private void BindAnimator()
        {
            Feedback = GetComponentInChildren<EnemyHitFeedback>();
            if (Feedback != null) Feedback.ResetState();
            // Death VFX are data-driven via EnemyData; avoid a second copy from the prefab field.
            if (Feedback != null) Feedback.deathVfx = null;
            if (animator != null) { animator.Rebind(); animator.Update(0); return; } // pooled reuse: back to locomotion
            animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var controller = animator.runtimeAnimatorController;
            if (controller != null)
                foreach (var clip in controller.animationClips)
                    if (clip != null && clip.name == EnemyVisualContract.DeathClip) deathHold = clip.length;
        }
        public void ResumeFrom(Vector3 position,Vector2Int anchor) {
            transform.position=position; hasEnd=false; path=owner.Paths.FindPath(anchor,grid.goal); node=0;
            if(hpBar!=null) hpBar.position=position+Vector3.up*BarHeight();
            FaceNextNode(); // split children face their own route from the spawn point
        }
        // Models are authored facing +Z with identity root rotation: no per-model yaw offsets anywhere.
        private void FaceNextNode()
        {
            if (path == null) return;
            for (int i = node; i < path.Count; i++)
            {
                Vector3 d = World(path[i]) - transform.position; d.y = 0;
                if (d.sqrMagnitude > .0001f) { transform.rotation = Quaternion.LookRotation(d); return; }
            }
        }
        public void ApplySlow(float fraction,float duration) {
            if(!Alive || duration<=0) return;
            slowMultiplier=Mathf.Min(slowMultiplier,1-Mathf.Clamp01(fraction)); slowRemaining=Mathf.Max(slowRemaining,duration);
        }
        private Transform hpBar;
        /// v18.6: the HP bar is drawn by EnemyHpBarsUI (screen-space canvas above every floating number); the 3D bar cubes stay as the
        /// invisible anchor (position / spawn scale-in / fill), their renderers are off. Set by GameBootstrap from CombatHudStyle.
        public static bool ScreenSpaceBars;
        /// World anchor of the HP bar (centre), or null.
        public Transform HpBarAnchor => hpBar;
        /// 0..1 spawn scale-in of the bar (Enemy.BarFadeTime), 0 = hidden.
        public float HpBarScale01 => hpBar != null && hpBar.gameObject.activeInHierarchy && barScale.x > 0f ? hpBar.localScale.x / barScale.x : 0f;
        /// 0..1 health fraction shown by the bar.
        public float HpFraction => maxHP > 0f ? Mathf.Clamp01(HP / maxHP) : 0f;
        // HP bar height follows the model (and a FlyingMotion lift if any; EnemyGroundFx.SetFlying adds it after Initialize).
        private StoneSignal.VFX.FlyingMotion fly; private bool flyChecked; private float modelTop;
        private float BarHeight()
        {
            // Bar sits at max(0.75, model top + 0.05) above the root, so tall ground meshes (e.g. the legged Skimmer) keep it above the model.
            // A flyer (FlyingMotion active; no enemy in this version) adds its live lift on top.
            if (!flyChecked && Application.isPlaying && Time.frameCount > spawnFrame)
            {
                fly = GetComponentInChildren<StoneSignal.VFX.FlyingMotion>(); flyChecked = true; modelTop = 0;
                var refT = fly != null ? fly.transform : transform;
                foreach (var r in ModelRenderers(transform)) modelTop = Mathf.Max(modelTop, MeshTop(r) - refT.position.y);
            }
            bool flying = fly != null && fly.isActiveAndEnabled;
            float lift = flying ? fly.transform.position.y - transform.position.y : 0f;
            return Mathf.Max(.75f + (flying ? fly.CurrentHeight : 0f), lift + modelTop + .05f);
        }
        // The creature model only: skinned meshes if any (blob shadows / fx quads are MeshRenderers).
        public static List<Renderer> ModelRenderers(Transform root)
        {
            var all = new List<Renderer>(); var sk = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.name.StartsWith("Blob") || r.name.StartsWith("HP")) continue;
                if (r is SkinnedMeshRenderer) sk.Add(r); else if (r is MeshRenderer && !r.name.StartsWith("HP")) all.Add(r);
            }
            return sk.Count > 0 ? sk : all;
        }
        // World top of a renderer's mesh. SkinnedMeshRenderer.bounds is a loose animation box, so use the rest mesh.
        public static float MeshTop(Renderer r)
        {
            Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : (r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null);
            if (m == null) return r.bounds.max.y;
            var b = m.bounds; float top = float.MinValue;
            for (int i = 0; i < 8; i++) top = Mathf.Max(top, r.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)).y);
            return top;
        }
        private int spawnFrame;
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
        private void Update()
        {
            if (!Alive) return; // dead: leave the animator running the death clip
            bool moving = Alive && owner != null && owner.CanMove();
            if (animator != null) animator.speed = moving ? 1 : 0;
            if (Feedback != null && Alive && Data != null) Feedback.SetMoveSpeed(speed * owner.SpeedMultiplier() * slowMultiplier / Mathf.Max(.01f, Data.moveSpeed));
            if (moving && !HeldBySpawn) Advance(Time.deltaTime); // spawn portal rise: hold until onDone
            if (barFade >= 0 && hpBar != null)
            {
                barFade += Time.unscaledDeltaTime; float k = Mathf.Clamp01(barFade / BarFadeTime); k = 1 - (1 - k) * (1 - k); // ease-out
                hpBar.localScale = barScale * k; if (barFade >= BarFadeTime) { hpBar.localScale = barScale; barFade = -1; }
            }
        }
        /// Spawn portal rise: no movement until the portal's onDone. The HP bar is hidden while held and eases in over
        /// barFadeTime once released (art-approved v17.1). The bar cubes use shared opaque palette materials, so the "fade"
        /// is a scale-in (no per-enemy material / no transparent pass / no extra draw calls).
        public bool HeldBySpawn
        {
            get => heldBySpawn;
            set
            {
                if (heldBySpawn == value) return; heldBySpawn = value;
                if (hpBar == null || !Application.isPlaying) return;
                if (value) { hpBar.gameObject.SetActive(false); barFade = -1; }
                else if (Alive) { hpBar.gameObject.SetActive(true); barFade = 0; hpBar.localScale = Vector3.zero; hpBar.position = transform.position + Vector3.up * BarHeight(); FaceBar(); }
            }
        }
        private bool heldBySpawn; private float barFade = -1; private Vector3 barScale = new Vector3(.65f, .055f, .08f);
        public const float BarFadeTime = .2f;
        public bool HpBarVisible => hpBar != null && hpBar.gameObject.activeInHierarchy && hpBar.localScale.x > .001f;
        public void Advance(float deltaTime)
        {
            if (!Alive || path == null || path.Count == 0) return;
            Vector3 previousPosition=transform.position; lastAdvance=Time.time;
            if (hasEnd && (previousPosition - lastEnd).sqrMagnitude > 1e-6f) { extWrites++; lastExt = previousPosition - lastEnd; }
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
            // Yaw-only, smooth turn toward the travel direction (EnemyData.turnSpeed deg/s); no snapping at corners.
            if(heading.sqrMagnitude>.00001f)
                transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(heading),Data.turnSpeed*deltaTime);
            if (hpBar != null) { hpBar.position = transform.position + Vector3.up * BarHeight(); FaceBar(); }
            lastStep = speed * owner.SpeedMultiplier() * slowMultiplier * deltaTime; lastEnd = transform.position; hasEnd = true;
            if (node >= path.Count) Resolve(EnemyResolution.Escaped);
        }
        // HP fill is anchored at the bar's left edge and shrinks from the right (scale + offset in the background's local space);
        // the bar yaws with the camera so "left" is always screen-left.
        private void SetFill(float r)
        {
            if (healthFill == null) return; r = Mathf.Clamp01(r);
            healthFill.localScale = new Vector3(r, 1.02f, 1.1f);
            healthFill.localPosition = new Vector3(-(1 - r) * .5f, .1f, -.05f);
            healthFill.gameObject.SetActive(r > 0);
            FaceBar();
        }
        private static Transform barCamera;
        private void FaceBar()
        {
            if (hpBar == null) return;
            if (barCamera == null && Camera.main != null) barCamera = Camera.main.transform;
            if (barCamera != null) hpBar.rotation = Quaternion.Euler(0, barCamera.eulerAngles.y, 0);
        }
        public void TakeDamage(float damage) => TakeDamage(damage, DamageKind.Physical, false);
        public void TakeDamage(float damage, DamageKind kind, bool crit)
        {
            if (!Alive || damage <= 0) return;
            LastHitKind = kind; LastHitCrit = crit;
            if (Feedback != null) Feedback.OnHit();
            HP = Mathf.Max(0, HP - damage);
            flashUntil = Time.time + .09f;
            SetFill(maxHP > 0 ? HP / maxHP : 0);
            Damaged?.Invoke(this, damage);
            if (HP <= 0) Resolve(EnemyResolution.Killed);
        }
        static StoneSignal.VFX.EnemyDeathFx deathFx; static int deathFxFrame = -1000;
        public static bool DeathFxAvailable
        {
            get { if (deathFx == null && Time.frameCount - deathFxFrame > 60) { deathFxFrame = Time.frameCount; deathFx = FindObjectOfType<StoneSignal.VFX.EnemyDeathFx>(); } return deathFx != null; }
        }
        private void Resolve(EnemyResolution reason)
        {
            if (!Alive) return;
            Alive = false;
            owner.Resolve(this, reason);
            if (hpBar != null) { if (Application.isPlaying) hpBar.gameObject.SetActive(false); else { PrimitiveVisual.DestroyObject(hpBar.gameObject); hpBar = null; } }
            // v16.2: EnemyDeathFx (PF_VFX_EnemyDeath in scene) covers the vanish; CombatFeedback plays it and the enemy is hidden this frame.
            if (Application.isPlaying && reason == EnemyResolution.Killed && DeathFxAvailable) { owner.Recycle(this); return; }
            if (Application.isPlaying && reason == EnemyResolution.Killed && Feedback != null)
            {
                if (animator != null) animator.speed = 1;
                // Registry already released this enemy; only the visual lingers until the dissolve finishes.
                Feedback.PlayDeath(() => { if (this != null && owner != null) owner.Recycle(this); });
            }
            else if (Application.isPlaying && reason == EnemyResolution.Killed) StartCoroutine(DeathRoutine());
            else if (owner != null && Application.isPlaying) owner.Recycle(this);
            else PrimitiveVisual.DestroyObject(gameObject);
        }
        private System.Collections.IEnumerator DeathRoutine()
        {
            // Play the authored death clip first, then keep the existing shrink so kill timing stays readable.
            if (animator != null)
            {
                animator.speed = 1;
                animator.SetTrigger(DeathTrigger);
                if (deathHold > 0) yield return new WaitForSeconds(deathHold);
            }
            yield return StartCoroutine(ShrinkDeath());
        }
        private void LateUpdate()
        {
            if (fly != null && hpBar != null && Alive) hpBar.position = transform.position + Vector3.up * BarHeight(); // follow the flyer bob
            // Stylized enemies flash through EnemyHitFeedback; writing a property block here would erase it.
            if (bodies == null || Feedback != null) return;
            bool flash=Time.time<flashUntil;
            tint.SetColor("_BaseColor",Color.white*2);
            foreach(var body in bodies) if(body!=null) body.SetPropertyBlock(flash ? tint : null);
        }
        private System.Collections.IEnumerator ShrinkDeath()
        {
            Vector3 initial = transform.localScale;
            float elapsed = 0;
            while (elapsed < .22f) { elapsed += Time.deltaTime; transform.localScale = initial * Mathf.Max(0,1-elapsed/.22f); yield return null; }
            transform.localScale = initial;
            if (owner != null) owner.Recycle(this); else Destroy(gameObject);
        }
        private void OnDestroy()
        {
            if (hpBar != null) PrimitiveVisual.DestroyObject(hpBar.gameObject);
            if (Alive && owner != null) { Alive = false; owner.Resolve(this, EnemyResolution.Removed); }
        }
    }
}
