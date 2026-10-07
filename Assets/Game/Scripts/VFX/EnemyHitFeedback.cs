using System;
using System.Collections;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Visual-only enemy feedback (no gameplay state). Lives on the PF_Enemy_* root.
    /// OnHit(): white flash (shader _HitFlash via MaterialPropertyBlock) + squash on the "Rig" child + Animator "Hit" trigger.
    /// PlayDeath(): Animator "Die" trigger, then shader _Dissolve 0->1, optional coin pop prefab, then callback.
    [DisallowMultipleComponent]
    public class EnemyHitFeedback : MonoBehaviour
    {
        static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int MoveSpeedId = Animator.StringToHash(EnemyAnimParams.MoveSpeed);
        static readonly int HitId = Animator.StringToHash(EnemyAnimParams.Hit);

        public float flashDuration = 0.12f;
        [Range(0, 0.5f)] public float squashAmount = 0.22f;
        public float squashDuration = 0.18f;
        public float deathDissolveDelay = 0.35f;
        public float deathDissolveDuration = 0.6f;
        [Tooltip("Optional: spawned at death (e.g. FX_Enemy_CoinPop / FX_Enemy_DeathPuff).")]
        public GameObject deathVfx;

        Renderer[] _renderers; MaterialPropertyBlock _mpb; Transform _rig; Animator _anim;
        Vector3 _rigScale = Vector3.one; float _flashT = -1, _squashT = -1, _dissolve; bool _dead;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();
            _rig = transform.Find("Rig"); if (_rig == null) _rig = transform;
            _rigScale = _rig.localScale;
            _anim = GetComponentInChildren<Animator>();
        }

        /// Scales walk-cycle playback (pass actualSpeed / authoredSpeed, ~1 at BALANCE base speed).
        public void SetMoveSpeed(float multiplier) { if (_anim) _anim.SetFloat(MoveSpeedId, multiplier); }

        public void OnHit()
        {
            if (_dead) return;
            _flashT = 0; _squashT = 0;
            if (_anim) _anim.SetTrigger(HitId);
        }

        public void PlayDeath(Action onComplete = null)
        {
            if (_dead) return; _dead = true;
            if (_anim) _anim.SetTrigger(EnemyVisualContract.DieTrigger);
            if (deathVfx) StylizedVfx.Play(deathVfx, transform.position + Vector3.up * 0.4f);
            StartCoroutine(DeathRoutine(onComplete));
        }

        /// Pool reuse: clear death/dissolve/flash/squash so a recycled enemy looks fresh.
        public void ResetState()
        {
            if (_renderers == null) Awake();
            StopAllCoroutines();
            _dead = false; _dissolve = 0; _flashT = -1; _squashT = -1;
            if (_rig) _rig.localScale = _rigScale;
            Apply(0);
        }

        /// Editor/showcase helper: sets flash and dissolve directly (0..1).
        public void SetVisualState(float flash, float dissolve)
        {
            if (_renderers == null) Awake();
            _dissolve = dissolve; Apply(flash);
        }

        IEnumerator DeathRoutine(Action onComplete)
        {
            yield return new WaitForSeconds(deathDissolveDelay);
            for (float t = 0; t < deathDissolveDuration; t += Time.deltaTime)
            { _dissolve = t / deathDissolveDuration; Apply(0); yield return null; }
            _dissolve = 1; Apply(0);
            onComplete?.Invoke();
        }

        void Update()
        {
            if (_flashT >= 0)
            {
                _flashT += Time.deltaTime;
                float f = 1 - Mathf.Clamp01(_flashT / flashDuration);
                Apply(f * f);
                if (_flashT > flashDuration) _flashT = -1;
            }
            if (_squashT >= 0)
            {
                _squashT += Time.deltaTime;
                float k = Mathf.Clamp01(_squashT / squashDuration);
                float s = Mathf.Sin(k * Mathf.PI) * (1 - k * 0.5f) * squashAmount;   // squash, then small rebound
                _rig.localScale = Vector3.Scale(_rigScale, new Vector3(1 + s * 0.6f, 1 - s, 1 + s * 0.6f));
                if (k >= 1) { _rig.localScale = _rigScale; _squashT = -1; }
            }
        }

        void Apply(float flash)
        {
            foreach (var r in _renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(HitFlashId, flash); _mpb.SetFloat(DissolveId, _dissolve);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
