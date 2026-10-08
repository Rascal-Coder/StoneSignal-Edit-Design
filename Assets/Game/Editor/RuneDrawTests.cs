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
            Case("ExtraDraw granted mid-intermission: 2nd draw still open -> free now; 2nd draw used / already free -> carried over", () =>
            {
                var d = new DrawRules(); d.BeginIntermission(); d.Consume(false);
                bool open = d.Next == DrawRules.Offer.Ad && d.GrantFreeSecond() && d.Next == DrawRules.Offer.Free && !d.GrantFreeSecond();
                d.Consume(false); bool used = d.Next == DrawRules.Offer.None && !d.GrantFreeSecond();
                return open && used;
            });
            Case("2nd draw without ad or ExtraDraw: not consumable (ads unavailable -> no 2nd draw)", () =>
            {
                var d = new DrawRules(); d.BeginIntermission(); d.Consume(false); return !d.Consume(false) && d.Used == 1 && d.Next == DrawRules.Offer.Ad;
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
            Case("input: symmetric (1x1/2x2) drag places on valid release, cancels on invalid / over hand", () =>
            {
                var m = new PlacementInput(); var cell = new Vector2Int(3, 4);
                m.CardDown(true, 0, false, new Vector2(100, 50));
                bool notYet = m.Move(new Vector2(105, 55), false, cell, true) == PlacementAction.None && m.State == PlacementState.Pressed;
                bool drag = m.Move(new Vector2(300, 400), false, cell, true) == PlacementAction.Preview && m.State == PlacementState.Dragging;
                bool noDir = m.Move(new Vector2(300, 400), false, cell, true, 1f) == PlacementAction.Preview; // holding never enters direction
                bool place = m.Up(new Vector2(300, 400), false) == PlacementAction.Place && m.State == PlacementState.Idle && m.Cell == cell;
                m.CardDown(true, 0, false, Vector2.zero); m.Move(new Vector2(0, 300), false, cell, false);
                bool invalid = m.Up(new Vector2(0, 300), false) == PlacementAction.Cancel;
                m.CardDown(true, 1, false, Vector2.zero); m.Move(new Vector2(0, 300), false, cell, true);
                bool hide = m.Move(new Vector2(0, 40), true, null, false) == PlacementAction.HidePreview;
                bool hand = m.Up(new Vector2(0, 40), true) == PlacementAction.Cancel && m.State == PlacementState.Idle;
                return notYet && drag && noDir && place && invalid && hide && hand;
            });
            Case("input: directional drag -> release -> direction; swipe picks 0/90/180/270; invalid keeps direction; valid commits", () =>
            {
                var m = new PlacementInput(); var cell = new Vector2Int(5, 5); bool[] ok = { false, true, false, false }; m.ValidFor = d => ok[d];
                m.CardDown(false, 0, true, Vector2.zero, 0); m.Move(new Vector2(0, 300), false, cell, true, .01f);
                bool enter = m.Up(new Vector2(0, 300), false) == PlacementAction.EnterDirection && m.State == PlacementState.Direction && !m.Held && m.Cell == cell;
                var c = new Vector2(500, 500); m.SetCentre(c);
                bool centre = m.BoardDown(c, cell, true) == PlacementAction.None && m.Held;
                bool up = m.Move(c + new Vector2(0, 60), false, null, false) == PlacementAction.SetDirection && m.Dir == 0;
                bool stay = m.Up(c + new Vector2(0, 60), false) == PlacementAction.None && m.State == PlacementState.Direction; // invalid direction
                bool dirs = PlacementInput.DirOf(new Vector2(0, 1)) == 0 && PlacementInput.DirOf(new Vector2(1, 0)) == 1 && PlacementInput.DirOf(new Vector2(0, -1)) == 2 && PlacementInput.DirOf(new Vector2(-1, 0)) == 3;
                m.BoardDown(c, cell, true); m.Move(c + new Vector2(60, 5), false, null, false);
                bool commit = m.Dir == 1 && m.Up(c + new Vector2(60, 5), false) == PlacementAction.Place && m.State == PlacementState.Idle;
                return enter && centre && up && stay && dirs && commit;
            });
            Case("input: hold-still after snap enters direction in-gesture; centre release / outside radius / hand cancel", () =>
            {
                var m = new PlacementInput(); var cell = new Vector2Int(2, 2);
                m.CardDown(false, 0, true, Vector2.zero, 0);
                m.Move(new Vector2(0, 300), false, cell, true, .01f);
                bool early = m.Move(new Vector2(2, 301), false, cell, true, .1f) == PlacementAction.Preview;
                bool held = m.Move(new Vector2(2, 301), false, cell, true, .2f) == PlacementAction.EnterDirection && m.Held && m.Centre == new Vector2(2, 301);
                bool dead = m.Up(new Vector2(5, 305), false) == PlacementAction.Cancel; // released in the deadzone
                m.CardDown(false, 0, true, Vector2.zero, 0); m.Move(new Vector2(0, 300), false, cell, true, .01f); m.Move(new Vector2(0, 300), false, cell, true, .3f);
                bool outer = m.Move(new Vector2(0, 300 + 200), false, null, false) == PlacementAction.Cancel && m.State == PlacementState.Idle;
                m.CardDown(false, 0, true, Vector2.zero, 0); m.Move(new Vector2(0, 300), false, cell, true, .01f); m.Move(new Vector2(0, 300), false, cell, true, .3f);
                bool hand = m.Move(new Vector2(0, 250), true, null, false) == PlacementAction.Cancel;
                m.Scale = 2; m.CardDown(false, 0, true, Vector2.zero, 0); m.Move(new Vector2(0, 300), false, cell, true, .01f); m.Move(new Vector2(0, 300), false, cell, true, .3f);
                bool scaled = m.Move(new Vector2(60, 300), false, null, false) == PlacementAction.None && m.Dir == -1; // 60 < 40*2
                return early && held && dead && outer && hand && scaled;
            });
            Case("input: tap-tap -> tap cell enters direction, tap arrow commits, tap elsewhere cancels; re-tap card deselects", () =>
            {
                var m = new PlacementInput(); var cell = new Vector2Int(4, 1); m.ValidFor = d => d == 2;
                m.CardDown(false, 0, true, Vector2.zero); m.Up(Vector2.zero, true);
                bool armed = m.State == PlacementState.Armed;
                bool enter = m.BoardDown(new Vector2(400, 400), cell, true) == PlacementAction.EnterDirection && !m.Held;
                bool badArrow = m.BoardDown(new Vector2(400, 400) + new Vector2(0, 90), cell, true) == PlacementAction.SetDirection && m.State == PlacementState.Direction;
                bool commit = m.BoardDown(new Vector2(400, 400) + new Vector2(0, -90), cell, true) == PlacementAction.Place;
                m.CardDown(false, 0, true, Vector2.zero); m.Up(Vector2.zero, true); m.BoardDown(new Vector2(400, 400), cell, true);
                bool cancel = m.BoardDown(new Vector2(1200, 400), null, false) == PlacementAction.Cancel && m.State == PlacementState.Idle;
                m.CardDown(true, 0, false, Vector2.zero); m.Up(Vector2.zero, true);
                bool sym = m.BoardDown(new Vector2(10, 10), cell, true) == PlacementAction.Place && m.Cell == cell;
                m.CardDown(true, 2, false, Vector2.zero); m.Up(Vector2.zero, true);
                m.CardDown(true, 2, false, Vector2.zero); bool desel = m.Up(Vector2.zero, true) == PlacementAction.Deselect;
                return armed && enter && badArrow && commit && cancel && sym && desel;
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
            // ---- v18.2 rarity-weighted reward roll (RewardRoll; 玩法策划: 30% rune, then 600/300/90/10 permille, no duplicates per pick)
            bool Within(int obs, int n, double p) { double sd = Math.Sqrt(n * p * (1 - p)); return Math.Abs(obs - n * p) <= 5 * sd + 1; }
            int[] Single(int[] tierList, int chance, int n, int seed, out int runeCount, out int[] perEntry)
            {
                var rng = new RngStream(seed); var idx = new List<int>(); var rn = new List<int>(); var byTier = new int[4]; perEntry = new int[tierList.Length]; runeCount = 0;
                for (int k = 0; k < n; k++)
                {
                    RewardRoll.Offer(tierList, chance, r => RuneRules.RollType(c, r), rng, idx, rn, 1);
                    if (rn[0] != RuneRules.NoRune) runeCount++; else { byTier[tierList[idx[0]]]++; perEntry[idx[0]]++; }
                }
                return byTier;
            }
            Case("reward roll: 100k options -> rune 300 / 普通 420 / 精良 210 / 稀有 63 / 传说 7 permille (5 sigma), equal odds within a tier", () =>
            {
                int[] pool = { 0, 0, 0, 0, 1, 1, 1, 2, 2, 3 }; const int N = 100000;
                var t = Single(pool, 300, N, 11, out int runes, out var per);
                bool ok = Within(runes, N, .3) && Within(t[0], N, .42) && Within(t[1], N, .21) && Within(t[2], N, .063) && Within(t[3], N, .007);
                for (int i = 0; i < 4; i++) ok &= Within(per[i], N, .42 / 4); for (int i = 4; i < 7; i++) ok &= Within(per[i], N, .21 / 3); ok &= Within(per[7], N, .063 / 2) && Within(per[8], N, .063 / 2);
                log.Add("  roll 100k: rune " + runes + " C " + t[0] + " R " + t[1] + " E " + t[2] + " L " + t[3] + " | per entry " + string.Join(",", per));
                return ok;
            });
            Case("reward roll: no Legendary in pool -> its 10 permille goes to 稀有 (C 600 / R 300 / E 100)", () =>
            {
                int[] pool = { 0, 0, 1, 1, 2, 2 }; const int N = 100000;
                var t = Single(pool, 0, N, 12, out int runes, out _);
                log.Add("  legendary merge 100k: C " + t[0] + " R " + t[1] + " E " + t[2] + " L " + t[3]);
                return runes == 0 && t[3] == 0 && Within(t[0], N, .6) && Within(t[1], N, .3) && Within(t[2], N, .1);
            });
            // ---- v18.3 planner tier table (RewardTiers) on the shipped pool
            RewardEffect[] shipped = { RewardEffect.AllDamage, RewardEffect.AllAttackSpeed, RewardEffect.AllRange, RewardEffect.BaseHP, RewardEffect.CannonRadius, RewardEffect.AddBlock, RewardEffect.NextDraw, RewardEffect.PathSlow, RewardEffect.BonusSlot, RewardEffect.KillGold, RewardEffect.WaveGold, RewardEffect.TowerDiscount, RewardEffect.WaveHeal, RewardEffect.ExtraDraw };
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Game/Settings/GameConfig.asset");
            var poolFx = new List<RewardEffect>(); if (cfg != null && cfg.rewards != null) foreach (var r in cfg.rewards) if (r != null) poolFx.Add(r.effect); if (poolFx.Count == 0) poolFx.AddRange(shipped);
            Case("tier table v18.3: 普通 WaveGold KillGold WaveHeal AddBlock NextDraw CannonRadius / 精良 AllRange PathSlow TowerDiscount ExtraDraw / 稀有 AllDamage AllAttackSpeed BonusSlot; unlisted -> 普通; runes 精良, 共鸣 稀有", () =>
            {
                bool ok = true; RewardTier T(RewardEffect e) => RewardTiers.For(e);
                foreach (var e in new[] { RewardEffect.WaveGold, RewardEffect.KillGold, RewardEffect.WaveHeal, RewardEffect.AddBlock, RewardEffect.NextDraw, RewardEffect.CannonRadius }) ok &= T(e) == RewardTier.Common && RewardTiers.Listed(e);
                foreach (var e in new[] { RewardEffect.AllRange, RewardEffect.PathSlow, RewardEffect.TowerDiscount, RewardEffect.ExtraDraw }) ok &= T(e) == RewardTier.Rare && RewardTiers.Listed(e);
                foreach (var e in new[] { RewardEffect.AllDamage, RewardEffect.AllAttackSpeed, RewardEffect.BonusSlot }) ok &= T(e) == RewardTier.Epic && RewardTiers.Listed(e);
                foreach (var e in new[] { RewardEffect.BaseHP, RewardEffect.ArrowRange, RewardEffect.NextWaveGold }) ok &= T(e) == RewardTier.Common && !RewardTiers.Listed(e);
                for (int i = 0; i < 5; i++) ok &= RewardTiers.RuneTier(i) == RewardTier.Rare; ok &= RewardTiers.RuneTier(5) == RewardTier.Epic;
                var unl = new List<string>(); foreach (var e in poolFx) if (!RewardTiers.Listed(e)) unl.Add(e.ToString());
                log.Add("  pool (" + poolFx.Count + "): " + string.Join(" ", poolFx) + " | in pool but not in the planner table (-> 普通): " + (unl.Count > 0 ? string.Join(", ", unl) : "none"));
                return ok;
            });
            Case("tier table applied to the reward assets in GameConfig (PrototypeBuild.EnsureRewardPool)", () =>
            {
                if (cfg == null) return false; bool ok = true; var bad = new List<string>();
                foreach (var r in cfg.rewards) if (r != null && r.tier != RewardTiers.For(r.effect)) { ok = false; bad.Add(r.effect + "=" + r.tier); }
                if (!ok) log.Add("  asset tier mismatch: " + string.Join(", ", bad));
                return ok;
            });
            Case("reward roll v18.3 shipped pool, 100k picks of three: ExtraDraw ~5.25% per option (1st option, 5 sigma), ~15% per pick; tiers 1st option rune 300 / 普通 420 / 精良 210 / 稀有 70 (传说 empty -> 稀有); no duplicates", () =>
            {
                var tiers = new List<int>(); foreach (var e in poolFx) tiers.Add((int)RewardTiers.For(e));
                const int N = 100000; var rng = new RngStream(183); var idx = new List<int>(); var rn = new List<int>();
                int P = poolFx.Count; var perOpt = new int[3, P + 6]; var perPick = new int[P + 6]; var tierOpt = new int[3, 6]; int dup = 0, shortPick = 0;
                for (int k = 0; k < N; k++)
                {
                    RewardRoll.Offer(tiers, 300, r => RuneRules.RollType(c, r), rng, idx, rn, 3);
                    if (idx.Count != 3) shortPick++;
                    var seen = new HashSet<int>();
                    for (int i = 0; i < idx.Count; i++)
                    {
                        int key = idx[i] >= 0 ? idx[i] : P + rn[i];
                        if (!seen.Add(key)) dup++;
                        perOpt[i, key]++;
                        int tb = idx[i] >= 0 ? tiers[idx[i]] : 4 + (rn[i] == 5 ? 1 : 0); tierOpt[i, tb]++;   // 4 = rune 精良, 5 = rune 稀有 (display)
                    }
                    foreach (var key in seen) perPick[key]++;
                }
                string Pct(int v) => (100.0 * v / N).ToString("F2") + "%";
                var sb = new System.Text.StringBuilder("  v18.3 distribution (100k picks of three, seed 183, rune gate 300 permille):\n  item | tier | option1 | option2 | option3 | per pick\n");
                string[] cn = { "普通", "精良", "稀有", "传说" };
                for (int i = 0; i < P + 6; i++)
                {
                    string name = i < P ? poolFx[i].ToString() : "rune " + RuneRules.Names[i - P]; string tn = i < P ? cn[tiers[i]] : (i - P == 5 ? "稀有(rune)" : "精良(rune)");
                    sb.Append("  ").Append(name).Append(" | ").Append(tn).Append(" | ").Append(Pct(perOpt[0, i])).Append(" | ").Append(Pct(perOpt[1, i])).Append(" | ").Append(Pct(perOpt[2, i])).Append(" | ").Append(Pct(perPick[i])).Append("\n");
                }
                string[] tl = { "普通", "精良", "稀有", "传说", "rune 精良", "rune 稀有" };
                for (int t = 0; t < 6; t++) sb.Append("  tier ").Append(tl[t]).Append(" | option1 ").Append(Pct(tierOpt[0, t])).Append(" | option2 ").Append(Pct(tierOpt[1, t])).Append(" | option3 ").Append(Pct(tierOpt[2, t])).Append("\n");
                sb.Append("  duplicates within a pick: ").Append(dup).Append(", picks with fewer than 3 options: ").Append(shortPick);
                log.Add(sb.ToString());
                int ed = poolFx.IndexOf(RewardEffect.ExtraDraw); if (ed < 0) return false;
                double pick = (double)perPick[ed] / N;
                return dup == 0 && shortPick == 0 && Within(perOpt[0, ed], N, .7 * .3 / 4) && pick > .14 && pick < .17
                    && Within(tierOpt[0, 4] + tierOpt[0, 5], N, .3) && Within(tierOpt[0, 0], N, .42) && Within(tierOpt[0, 1], N, .21) && Within(tierOpt[0, 2], N, .07) && tierOpt[0, 3] == 0;
            });
            Case("reward roll: empty-tier weights (down first, else up)", () =>
            {
                var w = new int[4];
                RewardRoll.Weights(new[] { 0, 0, 0, 2 }, w); bool a = w[0] == 0 && w[1] == 0 && w[2] == 0 && w[3] == 1000;
                RewardRoll.Weights(new[] { 5, 0, 0, 0 }, w); bool b = w[0] == 1000 && w[1] + w[2] + w[3] == 0;
                RewardRoll.Weights(new[] { 0, 3, 0, 1 }, w); bool d = w[0] == 0 && w[1] == 990 && w[2] == 0 && w[3] == 10;
                RewardRoll.Weights(new[] { 1, 1, 1, 1 }, w); bool e = w[0] == 600 && w[1] == 300 && w[2] == 90 && w[3] == 10;
                return a && b && d && e;
            });
            Case("reward roll: 50k picks of three never repeat a reward or a rune type (small pool, re-roll/exclude)", () =>
            {
                int[] pool = { 0, 0, 1, 3 }; var rng = new RngStream(21); var idx = new List<int>(); var rn = new List<int>(); bool ok = true;
                for (int k = 0; k < 50000 && ok; k++)
                {
                    RewardRoll.Offer(pool, 300, r => RuneRules.RollType(c, r), rng, idx, rn, 3);
                    var a = new HashSet<int>(); var b = new HashSet<int>(); ok &= idx.Count == 3 && rn.Count == 3;
                    for (int i = 0; i < idx.Count; i++) ok &= idx[i] >= 0 ? rn[i] == RuneRules.NoRune && a.Add(idx[i]) : rn[i] != RuneRules.NoRune && b.Add(rn[i]);
                }
                return ok;
            });
            Case("reward roll: pool of 2, no runes -> 2 options, no duplicate, no crash", () =>
            {
                var rng = new RngStream(3); var idx = new List<int>(); var rn = new List<int>(); bool ok = true;
                for (int k = 0; k < 1000; k++) { RewardRoll.Offer(new[] { 0, 1 }, 0, r => RuneRules.NoRune, rng, idx, rn, 3); ok &= idx.Count == 2 && idx[0] != idx[1]; }
                return ok;
            });
            Case("reward roll: deterministic per seed (same seed -> same 2000 picks; another seed differs)", () =>
            {
                int[] pool = { 0, 0, 0, 0, 0, 1, 1, 2, 3 };
                string Seq(int seed) { var rng = new RngStream(seed); var idx = new List<int>(); var rn = new List<int>(); var sb = new System.Text.StringBuilder(); for (int k = 0; k < 2000; k++) { RewardRoll.Offer(pool, 300, r => RuneRules.RollType(c, r), rng, idx, rn, 3); for (int i = 0; i < 3; i++) sb.Append(idx[i]).Append(':').Append(rn[i]).Append(','); } return sb.ToString(); }
                return Seq(37) == Seq(37) && Seq(37) != Seq(38);
            });
            // ---- v18.4 block-row fan geometry (HandFanLayout, ref px; card 150, step 162, budget = 1920 - 24 - 512 - 24 - 24)
            Case("fan: <= 4 slots stay a normal row; 5..7 slots fan into a fixed 4-slot width (636 px), covered cards expose >= 44 px, newest (rightmost) fully visible", () =>
            {
                var st = new HandFanStyle(); bool ok = true; float need = (Mathf.Max(38f, st.fanCountBadgePos.x + 56f) + 10f) * 150f / 128f + 2f;
                for (int n = 1; n <= 4; n++) { var r = HandFanLayout.Compute(n, 150, 162, 1336, st, need); ok &= !r.fanned && r.collapsedStep == 162 && r.exposed == 150; }
                for (int n = 5; n <= 7; n++)
                {
                    var r = HandFanLayout.Compute(n, 150, 162, 1336, st, need);
                    ok &= r.fanned && Mathf.Abs(r.collapsedWidth - (4 * 162 - 12)) < .01f && r.collapsedStep >= 44f && r.collapsedStep >= need && r.expandedStep >= r.collapsedStep && r.expandedWidth <= 1336 + .01f;
                    log.Add("  n=" + n + ": collapsed step " + r.collapsedStep.ToString("F1") + " px (exposed strip, badges need " + need.ToString("F1") + "), width " + r.collapsedWidth.ToString("F0") + "; expanded step " + r.expandedStep.ToString("F1") + ", width " + r.expandedWidth.ToString("F0"));
                }
                return ok;
            });
            Case("fan: the 44 px floor and the badge strip win over the fixed width (narrow row / many cards); expanded row clamped to the budget", () =>
            {
                var st = new HandFanStyle { rowSlots = 1.5f }; var r = HandFanLayout.Compute(7, 150, 162, 1336, st, 30f);
                var st2 = new HandFanStyle(); var r2 = HandFanLayout.Compute(7, 150, 162, 1336, st2, 100f);
                var r3 = HandFanLayout.Compute(7, 150, 162, 700, st2, 30f);
                return r.collapsedStep == 44f && r2.collapsedStep == 100f && Mathf.Abs(r3.expandedWidth - 700f) < .01f && r3.expandedStep >= r3.collapsedStep;
            });
            Case("fan style: gameplay N = 4, 4 slots, 44 px, 2 s idle, 0.35 s hold unchanged; art v18.4 spec values (EaseOutCubic = 1-(1-t)^3, shadow #0D1229 a.5 (-5,-3), covered 0.88, xN (-12,100), tilt 3/2, arc 6/8, lift 20, stagger 0.008, press 1.04 / 8, pill gap 28)", () =>
            {
                var st = new HandFanStyle(); bool ok = st.collapseAbove == 4 && st.rowSlots == 4f && st.minExposedPx == 44f && st.idleCollapseSeconds == 2f && st.holdExpandSeconds == .35f && st.expandedStepPx == 0f;
                ok &= st.expandSeconds == .15f && st.collapseSeconds == .15f && st.expandedLiftPx == 20f && st.coveredBrightness == .88f && st.fanCountBadgePos == new Vector2(-12f, 100f) && st.shadowOffset == new Vector2(-5f, -3f);
                ok &= ColorUtility.ToHtmlStringRGB(st.shadowColor) == "0D1229" && Mathf.Abs(st.shadowColor.a - .5f) < .001f;
                ok &= st.tiltDeg == 3f && st.arcSagPx == 6f && st.expandedTiltDeg == 2f && st.expandedArcSagPx == 8f && st.fixedRowIgnoresStackSpacing && st.staggerSeconds == .008f && st.relayoutSeconds == .15f;
                ok &= st.enterSeconds == .2f && st.enterOffsetPx == new Vector2(48f, -24f) && st.enterScaleFrom == .85f && st.countBumpScale == 1.25f && st.countBumpSeconds == .18f && st.pressScale == 1.04f && st.pressLiftPx == 8f && st.handCountGapPx == 28f;
                float err = 0f; for (int i = 0; i <= 20; i++) { float t = i / 20f, want = 1f - Mathf.Pow(1f - t, 3f); err = Mathf.Max(err, Mathf.Abs(st.expandEase.Evaluate(t) - want), Mathf.Abs(st.collapseEase.Evaluate(t) - want)); }
                log.Add("  EaseOutCubic curve max error vs 1-(1-t)^3: " + err.ToString("F5"));
                return ok && err < .002f;
            });
            UnityEngine.Object.DestroyImmediate(c);
            int cases = 0; foreach (var l in log) if (l.StartsWith("PASS ") || l.StartsWith("FAIL ")) cases++; // info lines (indented) are not test cases
            string result = string.Join("\n", log) + "\nRUNE/DRAW TESTS: " + (cases - fail) + "/" + cases + " passed";
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
