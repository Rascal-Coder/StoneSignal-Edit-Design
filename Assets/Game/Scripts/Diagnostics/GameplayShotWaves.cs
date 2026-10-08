#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace StoneSignal
{
    // v19 wave composition check (-waveshots [-seed N]): the seeded battle board, wave 1 fast-forwarded, then waves 2 and 3 at x1 with
    // spawn-time frames (the waves that used to contain the retired winged fast enemy) and the spawned kinds / timing per wave.
    public sealed partial class GameplayShot
    {
        IEnumerator WaveShots()
        {
            var sb = new StringBuilder(); int seed = ArgInt("-seed", 37);
            for (int w = 0; w < s.config.waves.Length; w++) { sb.Append("config wave " + (w + 1) + " interval " + s.config.waves[w].spawnInterval + " s:"); foreach (var g in s.config.waves[w].groups) sb.Append(" " + (g.enemy != null ? g.enemy.name : "NULL") + " x" + g.count); sb.Append(" (total " + s.config.waves[w].Total + ")\n"); }
            var kinds = new SortedDictionary<string, int>(); var times = new List<float>(); float waveT0 = 0f; bool bad = false;
            s.Enemies.Spawned += e => { string n = e.Data != null ? e.Data.name : "?"; kinds[n] = kinds.TryGetValue(n, out var c) ? c + 1 : 1; times.Add(Time.time - waveT0); if (e.Data == null || e.Data.flying || (e.Data.visualPrefab != null && e.Data.visualPrefab.name.Contains("Skimmer"))) bad = true; };
            yield return BattleSetup(sb, seed);
            for (int wave = 1; wave <= 3; wave++)
            {
                if (s.Game.State == GameState.Reward) { s.Rewards.Choose(0); yield return WaitRt(1.2f); }
                if (s.Game.State != GameState.Build) { sb.Append("wave " + wave + ": state " + s.Game.State + "\n"); break; }
                kinds.Clear(); times.Clear(); TimeController.ResetAll(); TimeController.SetSpeed(1); waveT0 = Time.time; bool ok = s.Waves.StartWave(); float r0 = Time.realtimeSinceStartup;
                if (wave >= 2)
                    foreach (var at in new[] { 3.0f, 6.0f })
                    {
                        while (Time.realtimeSinceStartup - r0 < at) yield return null;
                        yield return Clean(); yield return new WaitForEndOfFrame(); Capture(Path.Combine(Dir, "wave" + wave + "_t" + Mathf.RoundToInt(at) + "s_" + Res + ".png"));
                        var on = new SortedDictionary<string, int>(); foreach (var e in s.Enemies.Active) if (e != null && e.Alive) { string n = e.Data.name; on[n] = on.TryGetValue(n, out var c) ? c + 1 : 1; }
                        sb.Append("  wave " + wave + " frame t+" + at + " s: on screen"); foreach (var kv in on) sb.Append(" " + kv.Key + " x" + kv.Value); sb.Append("\n");
                    }
                TimeController.SetSpeed(4); float dl = Time.realtimeSinceStartup + 120f;
                while (s.Game.State == GameState.Combat && Time.realtimeSinceStartup < dl) yield return null;
                TimeController.SetSpeed(1);
                sb.Append("wave " + wave + " started=" + ok + " -> " + s.Game.State + ", spawned:"); foreach (var kv in kinds) sb.Append(" " + kv.Key + " x" + kv.Value);
                sb.Append(" | spawn times (game s):"); foreach (var t in times) sb.Append(" " + t.ToString("F1")); sb.Append(" | HP " + s.Economy.HP + " gold " + s.Economy.Gold + "\n");
                yield return WaitRt(.6f);
            }
            sb.Insert(0, (bad ? "WAVES FAIL: a flying / retired-enemy spawn was seen" : "WAVES PASS: only scheduled ground enemies spawned") + " (seed " + seed + ", " + Res + ")\n");
            File.WriteAllText(Path.Combine(Dir, "waves_" + Res + ".txt"), sb.ToString()); Debug.Log("WAVE SHOTS\n" + sb);
        }
    }
}
#endif
