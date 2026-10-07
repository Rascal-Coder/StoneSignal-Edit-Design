using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal.EditorTools
{
    // Edit-mode rules checks: runes, DRAW allowance, hand stacking, TimeController layering.
    // Menu: StoneSignal > Tests > Runes and draw. Batch: -executeMethod StoneSignal.EditorTools.RuneDrawTests.RunBatch
    public static class RuneDrawTests
    {
        [MenuItem("StoneSignal/Tests/Runes and draw")]
        public static string Run()
        {
            var log = new List<string>(); int fail = 0;
            var c = RuneConfig.CreateDefault();
            void Case(string name, Func<bool> body)
            {
                bool ok; try { ok = body(); } catch (Exception e) { ok = false; name += " (" + e.Message + ")"; }
                if (!ok) fail++; log.Add((ok ? "PASS " : "FAIL ") + name);
            }
            int R(RuneId r) => (int)r;
            Case("no runes -> neutral stats", () => { var s = RuneRules.Compute(c, new int[0], false); return s.damage == 0 && s.DamageMul == 1 && s.AttackSpeedMul == 1 && s.RangeCells == 0 && s.bounty == 0; });
            Case("锋 blade: damage +25%", () => RuneRules.Compute(c, new[] { R(RuneId.Blade) }, false).damage == 250);
            Case("疾 swift: attack speed +20%", () => RuneRules.Compute(c, new[] { R(RuneId.Swift) }, false).attackSpeed == 200);
            Case("望 sight: range +1 cell", () => RuneRules.Compute(c, new[] { R(RuneId.Sight) }, false).rangeCells == 1000);
            Case("霜 frost: slow 15% for 1.5 s", () => { var s = RuneRules.Compute(c, new[] { R(RuneId.Frost) }, false); return s.slow == 150 && s.slowMs == 1500; });
            Case("丰 bounty: +1 gold per kill", () => RuneRules.Compute(c, new[] { R(RuneId.Bounty) }, false).bounty == 1000);
            Case("same rune stacks 100/50/25%, 4th copy no effect", () =>
                RuneRules.Compute(c, new[] { 0, 0 }, false).damage == 375 && RuneRules.Compute(c, new[] { 0, 0, 0 }, false).damage == 437 && RuneRules.Compute(c, new[] { 0, 0, 0, 0 }, false).damage == 437);
            Case("different runes do not diminish each other", () => { var s = RuneRules.Compute(c, new[] { 0, 1 }, false); return s.damage == 250 && s.attackSpeed == 200; });
            Case("resonance: other rune effects +50%", () => RuneRules.Compute(c, new[] { 0 }, true).damage == 375 && RuneRules.Compute(c, new[] { 3 }, true).slow == 225);
            Case("resonance rune itself has no direct stat", () => { var s = RuneRules.Compute(c, new[] { R(RuneId.Resonance) }, false); return s.damage == 0 && s.attackSpeed == 0 && s.rangeCells == 0; });
            Case("resonance area default = 8 neighbouring cells (Chebyshev 1)", () =>
                RuneRules.InResonance(c, new Vector2Int(5, 5), Vector2Int.one, new Vector2Int(6, 6), Vector2Int.one) &&
                !RuneRules.InResonance(c, new Vector2Int(5, 5), Vector2Int.one, new Vector2Int(7, 5), Vector2Int.one) &&
                RuneRules.InResonance(c, new Vector2Int(5, 5), new Vector2Int(2, 2), new Vector2Int(7, 7), Vector2Int.one));
            Case("resonance radius is data-driven (8 -> radius 8)", () =>
            { var w = RuneConfig.CreateDefault(); w.resonanceRadiusCells = 8; bool r = RuneRules.InResonance(w, new Vector2Int(0, 0), Vector2Int.one, new Vector2Int(8, 3), Vector2Int.one) && !RuneRules.InResonance(w, new Vector2Int(0, 0), Vector2Int.one, new Vector2Int(9, 0), Vector2Int.one); UnityEngine.Object.DestroyImmediate(w); return r; });
            Case("rune roll deterministic per seed + respects chance", () =>
            {
                var a = new RngStream(5); var b = new RngStream(5); int runes = 0; bool same = true;
                for (int i = 0; i < 2000; i++) { int x = RuneRules.Roll(c, a), y = RuneRules.Roll(c, b); same &= x == y; if (x != RuneRules.NoRune) runes++; }
                return same && runes > 350 && runes < 650; // 25% of 2000
            });
            Case("rune roll chance 0 -> never", () => { var z = RuneConfig.CreateDefault(); z.runeChance = 0; var r = new RngStream(1); bool none = true; for (int i = 0; i < 200; i++) none &= RuneRules.Roll(z, r) == RuneRules.NoRune; UnityEngine.Object.DestroyImmediate(z); return none; });
            Case("DRAW: 1st free, 2nd needs ad, no 3rd", () =>
            {
                var d = new DrawRules();
                bool a = d.Next == DrawRules.Offer.Free && d.Consume(false);
                bool b = d.Next == DrawRules.Offer.Ad && !d.Consume(false) && d.Consume(true);
                bool e = d.Next == DrawRules.Offer.None && !d.Consume(true) && !d.Consume(false);
                d.BeginIntermission(); return a && b && e && d.Next == DrawRules.Offer.Free;
            });
            Case("roll table: none 750 / rune 250; weights 180x5 + resonance 100", () =>
            {
                var r = new RngStream(9); var n = new int[6]; int none = 0;
                for (int i = 0; i < 20000; i++) { int x = RuneRules.Roll(c, r); if (x < 0) none++; else n[x]++; }
                bool w = true; for (int i = 0; i < 5; i++) w &= n[i] > n[5] * 1.4f;
                return c.runeChance == 250 && none > 14400 && none < 15600 && w && n[5] > 0;
            });
            Case("ExtraDraw: 2nd draw of this intermission is free (no ad); resets next intermission", () =>
            {
                var d = new DrawRules(); d.BeginIntermission(true);
                bool a = d.Consume(false) && d.Next == DrawRules.Offer.Free && d.Consume(false) && d.Next == DrawRules.Offer.None;
                d.BeginIntermission(); d.Consume(false); return a && d.Next == DrawRules.Offer.Ad;
            });
            Case("NextDraw: drawn cards contain at least one rune", () =>
            {
                var t = ScriptableObject.CreateInstance<BlockShapeData>(); t.cells = new[] { Vector2Int.zero };
                var deck = new BlockDeckManager(new[] { t }); var h = new BlockHandManager();
                h.Add(deck, 3, () => RuneRules.NoRune, () => (int)RuneId.Frost); bool g = h.Runes[0] == (int)RuneId.Frost && h.Runes[1] == -1;
                h.Add(deck, 3, () => (int)RuneId.Blade, () => (int)RuneId.Frost); g &= h.Runes[3] == 0 && h.Runes[4] == 0; // already had runes: untouched
                h.Add(deck, 1, () => RuneRules.NoRune); g &= h.Runes[6] == -1; // no guarantee
                UnityEngine.Object.DestroyImmediate(t); return g;
            });
            Case("flow line runs entry -> core (material scrolls toward line start: first point = core)", () =>
            {
                var path = new[] { new Vector2Int(15, 3), new Vector2Int(14, 3), new Vector2Int(8, 5) }; // entry ... core
                var p = GridView.FlowOrder(path, true); var q = GridView.FlowOrder(path, false);
                return p[0] == path[2] && p[p.Count - 1] == path[0] && q[0] == path[0] && path[0] == new Vector2Int(15, 3);
            });
            Case("flow chevrons move toward the core (sampled over two frames)", () =>
            {
                float speed = GridView.FlowScrollSpeed(.8f, true); // lines run core (u=0) -> entry
                float u0 = GridView.ChevronU(10f, speed, 4, 20), u1 = GridView.ChevronU(10f + 1 / 60f, speed, 4, 20);
                float w0 = GridView.ChevronU(10f, GridView.FlowScrollSpeed(.8f, false), 4, 20), w1 = GridView.ChevronU(10f + 1 / 60f, GridView.FlowScrollSpeed(.8f, false), 4, 20);
                return speed < 0 && u1 < u0 && w1 > w0; // core->entry lines: u decreases = toward the core; entry->core lines: u increases = toward the core
            });
            Case("placeholder ad succeeds and releases the pause", () =>
            {
                bool got = false; new PlaceholderAdService().ShowRewarded("test", ok => got = ok && TimeController.AdPaused);
                return got && !TimeController.AdPaused;
            });
            Case("hand cap 7: draws stop at the cap", () =>
            {
                var t = ScriptableObject.CreateInstance<BlockShapeData>(); t.cells = new[] { Vector2Int.zero };
                var deck = new BlockDeckManager(new[] { t }); var h = new BlockHandManager();
                h.Add(deck, 3); h.Add(deck, 3); int six = h.Cards.Count; h.Add(deck, 3); bool ok = six == 6 && h.Cards.Count == 7 && h.IsFull; h.Add(deck, 3);
                ok &= h.Cards.Count == 7; UnityEngine.Object.DestroyImmediate(t); return ok;
            });
            Case("hand stacks identical cards as one xN group", () =>
            {
                var t = ScriptableObject.CreateInstance<BlockShapeData>(); var o = ScriptableObject.CreateInstance<BlockShapeData>();
                var h = new BlockHandManager(); h.AddCard(t, -1); h.AddCard(o, -1); h.AddCard(t, -1); h.AddCard(t, 0);
                var g = h.Groups(); h.Select(2); h.Consume();
                bool ok = g.Count == 3 && g[0].count == 2 && g[0].first == 0 && g[2].rune == 0 && h.Cards.Count == 3 && h.Runes[2] == 0;
                UnityEngine.Object.DestroyImmediate(t); UnityEngine.Object.DestroyImmediate(o); return ok;
            });
            Case("TimeController: pause/ad -> 0, hit-stop never exceeds speed, cleared hit-stop restores speed", () =>
                TimeController.Evaluate(3, true, false, false, 1) == 0 && TimeController.Evaluate(3, false, true, true, .05f) == 0 &&
                TimeController.Evaluate(3, false, false, true, .05f) == .05f && TimeController.Evaluate(.01f, false, false, true, .05f) == .01f &&
                TimeController.Evaluate(2, false, false, false, .05f) == 2);
            UnityEngine.Object.DestroyImmediate(c);
            string result = string.Join("\n", log) + "\nRUNE/DRAW TESTS: " + (log.Count - fail) + "/" + log.Count + " passed";
            Debug.Log(result);
            return fail == 0 ? result : result + " FAILED";
        }
        public static void RunBatch()
        {
            try { EditorApplication.Exit(Run().EndsWith("FAILED") ? 2 : 0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
