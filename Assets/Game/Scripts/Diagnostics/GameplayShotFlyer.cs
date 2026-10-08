#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace StoneSignal
{
    // v19 flyer bird check (-flyershot [-seed N]). No wave schedules a flyer yet, so this spawns the diagnostics-only
    // Resources/DiagFlyerPreview (PF_Enemy_Flyer, flying) on the left bridge during wave 1 of the seeded battle board.
    public sealed partial class GameplayShot
    {
        IEnumerator FlyerShot()
        {
            var sb = new StringBuilder(); int seed = ArgInt("-seed", 37);
            var preview = Resources.Load<EnemyData>("DiagFlyerPreview");
            sb.Append("preview data: " + (preview != null ? preview.name + " visual " + (preview.visualPrefab != null ? preview.visualPrefab.name : "NULL") + " flying " + preview.flying : "MISSING") + "\n");
            if (preview == null || preview.visualPrefab == null) { File.WriteAllText(Path.Combine(Dir, "flyer_" + Res + ".txt"), sb.ToString()); yield break; }
            foreach (var sm in preview.visualPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { string bones = ""; foreach (var b in sm.bones) if (b != null) bones += b.name + " "; sb.Append("  mesh " + (sm.sharedMesh != null ? sm.sharedMesh.name + " " + sm.sharedMesh.vertexCount + " v, bounds " + sm.sharedMesh.bounds.min.ToString("F2") + ".." + sm.sharedMesh.bounds.max.ToString("F2") : "NULL") + " bones " + bones + "\n"); }
            yield return BattleSetup(sb, seed);
            int left = -1; for (int i = 0; i < s.grid.Spawns.Count; i++) if (s.grid.Spawns[i].x == 0) left = i;
            // towers hold fire for the whole shot (the preview must fly the maze un-hit: SS_Move at its nominal speed, not SS_Hit)
            var held = new System.Collections.Generic.List<Tower>(); foreach (var t in s.Towers.Towers) if (t != null && t.enabled) { t.enabled = false; held.Add(t); }
            s.Waves.StartWave(); yield return WaitRt(1f);
            var en = s.Enemies.Spawn(preview, 40, 1, Mathf.Max(0, left)); float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f && en != null && (en.HeldBySpawn || !s.grid.InBounds(s.grid.ToCell(en.transform.position)))) yield return null;
            var fly = en.GetComponentInChildren<StoneSignal.VFX.FlyingMotion>(); var anim = en.GetComponentInChildren<Animator>();
            sb.Append("spawned " + en.name + " at spawn " + left + ", on tiles after " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s; FlapScale " + en.FlapScale.ToString("F3") + "\n");
            // motion stats while it walks the maze (turns): height, bob, roll range, flap loop
            float hMin = 99, hMax = -99, rMin = 0, rMax = 0, loopMin = 99, loopMax = 0; string clipName = "";
            float m0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - m0 < 1.4f && en != null && en.Alive)
            {
                yield return null;
                if (fly != null && fly.isActiveAndEnabled) { hMin = Mathf.Min(hMin, fly.CurrentHeight); hMax = Mathf.Max(hMax, fly.CurrentHeight); rMin = Mathf.Min(rMin, fly.Roll); rMax = Mathf.Max(rMax, fly.Roll); }
                if (anim != null) { var ci = anim.GetCurrentAnimatorClipInfo(0); var st = anim.GetCurrentAnimatorStateInfo(0); if (ci.Length > 0) { clipName = ci[0].clip.name; float eff = st.speed * st.speedMultiplier * anim.speed; if (eff > .01f) { float loop = ci[0].clip.length / eff; loopMin = Mathf.Min(loopMin, loop); loopMax = Mathf.Max(loopMax, loop); } } }
            }
            yield return Clean(); yield return new WaitForEndOfFrame(); Capture(Path.Combine(Dir, "flyer_ingame_" + Res + ".png"));
            sb.Append("in-game frame: " + Describe(en) + "\n" + Prints(en) + "\n");
            var cam = s.viewCamera; var home = cam.transform.position; float ortho = cam.orthographicSize, fov = cam.fieldOfView;
            while (Time.realtimeSinceStartup - m0 < 7f && en != null && en.Alive)
            {
                yield return null;
                if (fly != null && fly.isActiveAndEnabled) { hMin = Mathf.Min(hMin, fly.CurrentHeight); hMax = Mathf.Max(hMax, fly.CurrentHeight); rMin = Mathf.Min(rMin, fly.Roll); rMax = Mathf.Max(rMax, fly.Roll); }
                if (anim != null) { var ci = anim.GetCurrentAnimatorClipInfo(0); var st = anim.GetCurrentAnimatorStateInfo(0); if (ci.Length > 0) { clipName = ci[0].clip.name; float eff = st.speed * st.speedMultiplier * anim.speed; if (eff > .01f) { float loop = ci[0].clip.length / eff; loopMin = Mathf.Min(loopMin, loop); loopMax = Mathf.Max(loopMax, loop); } } }
            }
            sb.Append("MOTION (7 s on the maze): lift " + hMin.ToString("F2") + ".." + hMax.ToString("F2") + " m (spec 1.2 +/- 0.12), roll " + rMin.ToString("F1") + ".." + rMax.ToString("F1") + " deg (bankMax " + (fly != null ? fly.bankMax.ToString("F0") : "-") + "), clip " + clipName + " loop " + loopMin.ToString("F3") + ".." + loopMax.ToString("F3") + " s (spec 2 flaps / 0.6 s; MoveSpeed " + (anim != null ? anim.GetFloat("MoveSpeed").ToString("F3") : "-") + ")\n");
            if (en == null || !en.Alive) { sb.Append("flyer died before the close-ups\n"); File.WriteAllText(Path.Combine(Dir, "flyer_" + Res + ".txt"), sb.ToString()); yield break; }
            // close-ups, UI hidden, game at 1/4 speed: flap frames 0 / 0.1 / 0.2 s game time (one flap = 0.3 s), then hit and death
            captureNoUi = true; if (cam.orthographic) cam.orthographicSize = ortho * .3f; else cam.fieldOfView = fov * .34f;
            TimeController.SetSpeed(.25f);
            System.Func<Vector3> focus = () => fly != null ? fly.transform.position + Vector3.up * .5f : en.transform.position;
            float g0 = Time.time; int k = 0;
            foreach (var at in new[] { 0f, .1f, .2f })
            {
                while (Time.time - g0 < at) { cam.transform.position = focus() - cam.transform.forward * 8f; yield return null; }
                cam.transform.position = focus() - cam.transform.forward * 8f; yield return null; yield return new WaitForEndOfFrame();
                Capture(Path.Combine(Dir, (k == 0 ? "flyer_closeup_" : "flyer_flap" + k + "_") + Res + ".png"));
                var st = anim != null ? anim.GetCurrentAnimatorStateInfo(0) : default; sb.Append("  flap frame " + k + " t+" + (Time.time - g0).ToString("F3") + " s game, state norm " + st.normalizedTime.ToString("F3") + ", roll " + (fly != null ? fly.Roll.ToString("F1") : "-") + "\n"); k++;
            }
            en.TakeDamage(1f); float h0 = Time.time; while (Time.time - h0 < .09f) { cam.transform.position = focus() - cam.transform.forward * 8f; yield return null; }
            yield return new WaitForEndOfFrame(); Capture(Path.Combine(Dir, "flyer_hit_" + Res + ".png"));
            { var ci = anim != null ? anim.GetCurrentAnimatorClipInfo(0) : new AnimatorClipInfo[0]; sb.Append("  hit frame: clip " + (ci.Length > 0 ? ci[0].clip.name : "-") + "\n"); }
            en.TakeDamage(1e6f); float d0 = Time.time; Vector3 dp = focus();
            foreach (var at in new[] { .25f, .6f })
            {
                while (Time.time - d0 < at) { cam.transform.position = dp - cam.transform.forward * 8f; yield return null; }
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(Dir, "flyer_death_t" + Mathf.RoundToInt(at * 1000) + "_" + Res + ".png"));
                var ci = anim != null ? anim.GetCurrentAnimatorClipInfo(0) : new AnimatorClipInfo[0]; sb.Append("  death frame t+" + at + ": clip " + (ci.Length > 0 ? ci[0].clip.name : "-") + "\n");
            }
            // In-game kills vanish through EnemyDeathFx (v16.2 puff / skull / coin, Enemy.cs Resolve) so SS_Death never plays on a killed enemy.
            // Show the FBX's SS_Death tumble itself: a bare visual copy (scripts off) at the flight height, the clip sampled at 0.25 / 0.6 s.
            {
                var vis = Instantiate(preview.visualPrefab, dp - Vector3.up * .5f, Quaternion.Euler(0, 200, 0));
                foreach (var mb in vis.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                var an = vis.GetComponentInChildren<Animator>(); AnimationClip dc = null;
                if (an != null && an.runtimeAnimatorController != null) foreach (var c in an.runtimeAnimatorController.animationClips) if (c != null && c.name == "SS_Death") dc = c;
                if (an != null) an.enabled = false;
                foreach (var gf in vis.GetComponentsInChildren<Transform>(true)) if (gf.name == "GroundFx" || gf.name == "Blob") gf.gameObject.SetActive(false);
                sb.Append("  SS_Death sample: " + (dc != null ? dc.name + " " + dc.length.ToString("F2") + " s" : "clip not found") + "\n");
                if (dc != null)
                    foreach (var at in new[] { 0f, .25f, .6f, .95f })
                    {
                        dc.SampleAnimation(an.gameObject, at); cam.transform.position = vis.transform.position + Vector3.up * .3f - cam.transform.forward * 8f;
                        yield return null; dc.SampleAnimation(an.gameObject, at); yield return new WaitForEndOfFrame();
                        Capture(Path.Combine(Dir, "flyer_deathclip_t" + Mathf.RoundToInt(at * 1000) + "_" + Res + ".png"));
                    }
                Destroy(vis);
            }
            foreach (var t in held) if (t != null) t.enabled = true;
            TimeController.ResetAll(); TimeController.SetSpeed(1); captureNoUi = false; cam.transform.position = home; cam.orthographicSize = ortho; cam.fieldOfView = fov;
            File.WriteAllText(Path.Combine(Dir, "flyer_" + Res + ".txt"), sb.ToString()); Debug.Log("FLYER SHOT\n" + sb);
        }
    }
}
#endif
