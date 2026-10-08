#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace StoneSignal
{
    // v19 (patch r2/01 outline + v19 tower text) and v18.6b (art follow-ups) diagnostics: -v19shots [-seed N]
    public sealed partial class GameplayShot
    {
        IEnumerator V19() => Arg("-v19shots") ? V19Shots() : null;

        static Rect ScreenBounds(Camera cam, Bounds b)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = cam.WorldToScreenPoint(c); x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }
        static Bounds RendBounds(GameObject go)
        {
            bool any = false; var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }
        static Rect Pad(Rect r, float px, float minSize)
        {
            var c = r.center; float w = Mathf.Max(minSize, r.width + 2 * px), h = Mathf.Max(minSize, r.height + 2 * px);
            return new Rect(c.x - w * .5f, c.y - h * .5f, w, h);
        }
        static Texture2D SideBySide(Texture2D a, Texture2D b, int gap)
        {
            int w = a.width + gap + b.width, h = Mathf.Max(a.height, b.height); var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            var fill = new Color[w * h]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color(.08f, .08f, .1f); t.SetPixels(fill);
            t.SetPixels(0, 0, a.width, a.height, a.GetPixels()); t.SetPixels(a.width + gap, 0, b.width, b.height, b.GetPixels()); t.Apply(false); return t;
        }
        /// mean Rec.601 luminance and HSV saturation over a rect (0..1)
        static (float lum, float sat) LumSat(Texture2D t, Rect r)
        {
            var c = Crop(t, r, 1); var px = c.GetPixels(); Object.Destroy(c); double l = 0, sa = 0;
            foreach (var p in px) { l += .299 * p.r + .587 * p.g + .114 * p.b; float mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b)), mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b)); sa += mx > 1e-4f ? (mx - mn) / mx : 0; }
            return px.Length > 0 ? ((float)(l / px.Length), (float)(sa / px.Length)) : (0f, 0f);
        }
        /// TMP texts under root: lines + orphan check (last line of a wrapped text with a single visible character).
        static string OrphanAudit(Transform root, string label, ref int orphans)
        {
            var sb = new StringBuilder("text / orphan audit (" + label + "):\n");
            foreach (var t in root.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.alpha <= 0f) continue;
                t.ForceMeshUpdate(); var ti = t.textInfo; int lines = ti.lineCount; string lastLine = "", ol = "";
                if (lines > 1) { var li = ti.lineInfo[lines - 1]; for (int k = li.firstCharacterIndex; k <= li.lastCharacterIndex && k < ti.characterCount; k++) if (ti.characterInfo[k].isVisible) lastLine += ti.characterInfo[k].character; if (lastLine.Length <= 1) { ol = " ORPHAN['" + lastLine + "']"; orphans++; } }
                uint[] miss = null; bool missing = t.font != null && !t.font.HasCharacters(t.text, out miss, true, true) && miss != null && miss.Length > 0; string mc = ""; if (missing) foreach (var u in miss) mc += char.ConvertFromUtf32((int)u);
                sb.Append("  ").Append(t.name).Append(" '").Append(t.text.Replace("\n", "\\n")).Append("' size ").Append(t.fontSize.ToString("0.#")).Append(" lines ").Append(lines)
                  .Append(lines > 1 ? " last line '" + lastLine + "'" : "").Append(ol).Append(t.isTextOverflowing ? " OVERFLOW" : "").Append(missing ? " MISSING[" + mc + "]" : "").Append('\n');
            }
            return sb.ToString();
        }

        IEnumerator V19Shots()
        {
            var sb = new StringBuilder(); int nPass = 0, nFail = 0; int seed = ArgInt("-seed", 37); int orphans = 0;
            void Check(bool ok, string what) { if (ok) nPass++; else nFail++; sb.Append(ok ? "PASS " : "FAIL ").Append(what).Append('\n'); }
            var ui = FindObjectOfType<GameUI>(); var ch = s.config.combatHud ?? new CombatHudStyle(); var cam = s.viewCamera;
            float hs = HudScaler.ScaleFor(Screen.width, Screen.height);
            yield return BattleSetup(sb, seed);
            // ---- v19 tower text (玩法策划 2026-10-08): names / blurbs / roles; DamageKind unchanged
            sb.Append("towers (Loc.Tower / Loc.TowerBlurb / TowerData.role / damageKind):\n");
            foreach (var d in s.config.towers) sb.Append("  " + d.name + ": '" + StoneSignal.UI.Loc.Tower(d) + "' / '" + StoneSignal.UI.Loc.TowerBlurb(d) + "' / '" + d.role + "' / " + d.damageKind + " (lobbed " + d.lobbedShot + ")\n");
            {
                var want = new Dictionary<string, (string name, string blurb, StoneSignal.VFX.DamageKind kind)> {
                    { "Needle", ("针角犀", "角尖射出光针，重创单个敌人，偶尔暴击", StoneSignal.VFX.DamageKind.Physical) }, { "Pulse", ("雷铃兽", "快速放出电弧，清理成群小怪", StoneSignal.VFX.DamageKind.Lightning) },
                    { "Chill", ("霜宝龙", "吐出冰晶，减速敌人", StoneSignal.VFX.DamageKind.Ice) }, { "Seismic", ("岩甲兽", "抛出熔岩石，大范围重爆", StoneSignal.VFX.DamageKind.Explosive) } };
                bool ok = true; string bad = "";
                foreach (var d in s.config.towers) foreach (var kv in want) if (d.name.StartsWith(kv.Key) && (StoneSignal.UI.Loc.Tower(d) != kv.Value.name || StoneSignal.UI.Loc.TowerBlurb(d) != kv.Value.blurb || d.damageKind != kv.Value.kind || !d.role.Contains(":"))) { ok = false; bad += d.name + " "; }
                Check(ok, "v19 tower names / blurbs / roles, DamageKind unchanged (Needle 针角犀 Physical, Pulse 雷铃兽 Lightning, Chill 霜宝龙 Ice, Seismic 岩甲兽 Explosive) " + bad);
            }
            foreach (var r in s.config.rewards) if (r != null && (r.effect == RewardEffect.CannonRadius || r.effect == RewardEffect.ArrowRange)) sb.Append("  reward " + r.effect + ": '" + StoneSignal.UI.Loc.RewardTitle(r) + "' / '" + StoneSignal.UI.Loc.RewardDesc(r) + "'\n");
            { string cn = CnAtlas(); sb.Append(cn); Check(cn.Contains("missing=[]"), "CN font covers every Loc.All() + reward string (dynamic TTF)"); }
            sb.Append("damage colours: Fire " + (Color32)StoneSignal.VFX.DamageNumbers.ColorFor(StoneSignal.VFX.DamageKind.Fire) + " Lightning " + (Color32)StoneSignal.VFX.DamageNumbers.ColorFor(StoneSignal.VFX.DamageKind.Lightning) + "\n");
            // ---- materials (patch 01 + spec values) on the live renderers
            {
                var seen = new HashSet<Material>();
                foreach (var r in FindObjectsOfType<Renderer>()) foreach (var m in r.sharedMaterials) if (m != null && m.shader != null && m.shader.name == "StoneSignal/ToonLitOutline" && seen.Add(m))
                    sb.Append("  outline material " + m.name + ": width " + m.GetFloat("_OutlineWidthPx") + " px zOff " + m.GetFloat("_OutlineZOffset") + " fromBase " + m.GetFloat("_OutlineFromBase") + " darken " + m.GetFloat("_OutlineDarken") + " tint " + m.GetFloat("_OutlineTint") + " facingFade " + m.GetFloat("_OutlineFacingFade") + " colour #" + ColorUtility.ToHtmlStringRGB(m.GetColor("_OutlineColor")) + " keywords [" + string.Join(",", m.shaderKeywords) + "]\n");
                var names = new List<string>(); foreach (var m in seen) names.Add(m.name);
                Check(!names.Exists(n => n.Contains("Env") || n.Contains("Foliage") || n.Contains("Water") || n.Contains("Block") || n.Contains("Grass")), "outlines only on towers / core / enemies (outline materials in scene: " + string.Join(", ", names) + ")");
            }
            // ---- Build: tower card row (names are not printed on the cards), outline close-up, wall row
            { var NR = RuneRules.NoRune; var spec = new List<(int, int)>(); for (int i = 0; i < Mathf.Min(6, s.config.blocks.Length); i++) spec.Add((i, NR)); yield return SetHand(spec); yield return WaitRt(.8f); }
            if (ui.FanActive) { ui.DebugSetFan(false); yield return WaitRt(.4f); }
            yield return Clean(); yield return new WaitForEndOfFrame(); var texBuild = Grab();
            Rect towerRow = Rect.zero, blockRow = Rect.zero; bool tr0 = false, br0 = false; var blockFaces = new List<Rect>(); RectTransform face1 = null;
            foreach (var h in ui.DebugHandCards())
            {
                var r = ScreenRect(h.rt);
                if (h.tower) { towerRow = tr0 ? Rect.MinMaxRect(Mathf.Min(towerRow.xMin, r.xMin), Mathf.Min(towerRow.yMin, r.yMin), Mathf.Max(towerRow.xMax, r.xMax), Mathf.Max(towerRow.yMax, r.yMax)) : r; tr0 = true; if (h.index == 1) face1 = h.rt.Find("Frame") as RectTransform; }
                else { blockRow = br0 ? Rect.MinMaxRect(Mathf.Min(blockRow.xMin, r.xMin), Mathf.Min(blockRow.yMin, r.yMin), Mathf.Max(blockRow.xMax, r.xMax), Mathf.Max(blockRow.yMax, r.yMax)) : r; br0 = true; blockFaces.Add(Rect.MinMaxRect(r.xMin + r.width * .2f, r.yMin + r.height * .2f, r.xMax - r.width * .2f, r.yMax - r.height * .2f)); }
            }
            { var cz = Crop(texBuild, Pad(towerRow, 24 * hs, 0), 1); Save(cz, "tower_cards_build_" + Res + ".png"); Destroy(cz); }
            sb.Append(OrphanAudit(ui.transform, "HUD in Build", ref orphans));
            // v18.6b tower-card outline: 4 ref px white outside the face, silhouette-following; measure on card 1 (smallest tilt)
            if (face1 != null)
            {
                var fd = face1.GetComponent<UiDisable>(); var fr = ScreenRect(face1); float sf = face1.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
                var cz = Crop(texBuild, Pad(fr, 30 * sf, 0), 3); Save(cz, "outline_towercard_" + Res + "_closeup.png"); Destroy(cz);
                var cz2 = Crop(texBuild, Pad(towerRow, 16 * sf, 0), 2); Save(cz2, "outline_towercards_" + Res + "_row.png"); Destroy(cz2);
                // scan outward from inside the left edge at mid height: count near-white px just outside the face
                int y = Mathf.RoundToInt(fr.center.y); int white = 0, firstWhite = -1; var row = new StringBuilder();
                for (int x = Mathf.RoundToInt(fr.xMin + 6 * sf); x >= Mathf.RoundToInt(fr.xMin - 12 * sf); x--) { var c = texBuild.GetPixel(x, y); bool w = c.r > .9f && c.g > .9f && c.b > .9f; if (w) { white++; if (firstWhite < 0) firstWhite = x; } row.Append(w ? 'W' : '.'); }
                Check(fd != null && Mathf.Abs(fd.OutlinePx - ch.towerCardOutlinePx) < .01f && fd.OutlineColor == ch.towerCardOutlineColor && white >= Mathf.Floor(3f * sf) && white <= Mathf.Ceil(5.5f * sf),
                    "tower card outline: UiDisable outline " + (fd != null ? fd.OutlinePx + " ref px colour " + fd.OutlineColor : "missing") + ", " + UiDisable.OutlineDirs + " directions; scan left edge of card 1 at y " + y + " (inside -> outside): " + row + " -> " + white + " white px (expect ~" + (4 * sf).ToString("F1") + " = 4 x canvas scale " + sf.ToString("F2") + ")");
            }
            // ---- planner: 岩甲兽范围 three-pick. ArrowRange (针弩射程) is NOT granted and NOT added to the pool.
            var poolBefore = s.config.rewards; bool arInPool = System.Array.Exists(poolBefore, r => r != null && r.effect == RewardEffect.ArrowRange);
            sb.Append("ArrowRange (针弩射程) in the live reward pool: " + arInPool + " (pool " + poolBefore.Length + " rewards)\n");
            Check(!arInPool, "ArrowRange (针弩射程) is not in the live reward pool and was not granted");
            {
                var tag = "cannon_radius"; var effs = new[] { RewardEffect.AllDamage, RewardEffect.CannonRadius, RewardEffect.WaveGold };
                var rar = new StoneSignal.UI.RewardRarity[effs.Length]; for (int i = 0; i < effs.Length; i++) rar[i] = GameUI.RarityOf(effs[i]);
                if (!ui.DebugShowRewardPick(rar, effs)) Check(false, "reward pick UI unavailable");
                else
                {
                    yield return WaitRt(1.0f); yield return new WaitForEndOfFrame(); Capture(Path.Combine(Dir, "reward_pick_" + tag + "_" + Res + ".png"));
                    string audit = OrphanAudit(ui.PickUi.transform, "reward pick " + tag, ref orphans); sb.Append(audit);
                    Check(audit.Contains("'岩甲兽范围'"), "three-pick shows '岩甲兽范围' (" + string.Join(",", effs) + ", tiers " + string.Join(",", rar) + ")");
                    ui.PickUi.gameObject.SetActive(false); yield return WaitRt(.3f);
                }
            }
            Check(orphans == 0, "no orphan (single-character last line) in card / reward text: " + orphans);
            // ---- combat: wall row comparison, draw pile, tower cards with a disabled one
            s.Waves.StartWave(); yield return WaitRt(.8f);
            yield return Clean(); yield return new WaitForEndOfFrame(); var texCombat = Grab();
            {
                var a = Crop(texBuild, Pad(blockRow, 16 * hs, 0), 1); var b = Crop(texCombat, Pad(blockRow, 16 * hs, 0), 1); var sbs = SideBySide(a, b, Mathf.RoundToInt(24 * hs)); Save(sbs, "wallrow_build_vs_combat_" + Res + ".png"); Destroy(a); Destroy(b); Destroy(sbs);
                double lb = 0, sbld = 0, lc = 0, sc = 0; foreach (var r in blockFaces) { var x = LumSat(texBuild, r); var y = LumSat(texCombat, r); lb += x.lum; sbld += x.sat; lc += y.lum; sc += y.sat; }
                int n = Mathf.Max(1, blockFaces.Count); string dims = ""; float g = -1, br = -1; foreach (var d in ui.DebugDims()) if (!d.tower) { g = d.gray; br = d.brightness; dims += "B" + d.index + " " + d.gray.ToString("F2") + "/" + d.brightness.ToString("F2") + " "; }
                sb.Append("WALL ROW (" + blockFaces.Count + " card faces, inner 60 %): Build mean luminance " + (lb / n).ToString("F3") + " saturation " + (sbld / n).ToString("F3") + " | combat luminance " + (lc / n).ToString("F3") + " saturation " + (sc / n).ToString("F3") +
                          " | ratio lum " + (lc / System.Math.Max(1e-4, lb)).ToString("F3") + " sat " + (sc / System.Math.Max(1e-4, sbld)).ToString("F3") + " | applied grey/brightness " + dims + "\n");
                Check(Mathf.Abs(g - .7f) < .01f && Mathf.Abs(br - .8f) < .01f && Mathf.Abs(ch.disabledGray - .7f) < .001f && Mathf.Abs(ch.disabledBrightness - .8f) < .001f, "combat wall row applies grayscale 70 % / brightness 0.8 (config " + ch.disabledGray + " / " + ch.disabledBrightness + ", live " + g.ToString("F2") + " / " + br.ToString("F2") + ")");
            }
            {
                var pile = ui.transform.GetComponentInChildren<StoneSignal.VFX.DrawPileUI>(true); var pr = ScreenRect((RectTransform)pile.transform); var crop = Pad(Rect.MinMaxRect(pr.xMin, pr.yMin, pr.xMax, pr.yMax + 70 * hs), 20 * hs, 0);
                var a = Crop(texBuild, crop, 2); var b = Crop(texCombat, crop, 2); var sbs = SideBySide(a, b, 24); Save(sbs, "drawpile_build_vs_combat_" + Res + ".png"); Destroy(a); Destroy(b); Destroy(sbs);
                float pa = pile.pillImage != null ? pile.pillImage.color.a : -1f, ta = pile.tail != null ? pile.tail.color.a : 0f, la = pile.statusLabel != null ? pile.statusLabel.alpha : -1f;
                Check(pa < .01f && ta < .01f && la < .01f, "COMBAT draw pile: pill alpha " + pa.ToString("F2") + ", tail alpha " + ta.ToString("F2") + ", label alpha " + la.ToString("F2") + " (pill fades with its label, no empty bar)");
            }
            {
                int g0 = s.Economy.Gold; int spend = Mathf.Max(0, g0 - 60); s.Economy.Spend(spend); yield return WaitRt(.4f); yield return Clean(); yield return new WaitForEndOfFrame(); var tx = Grab();
                var cz = Crop(tx, Pad(towerRow, 24 * hs, 0), 1); Save(cz, "tower_cards_combat_disabled_" + Res + ".png"); Destroy(cz); Destroy(tx);
                string dims = ""; foreach (var d in ui.DebugDims()) if (d.tower) dims += "T" + d.index + " k " + d.k.ToString("F2") + " grey " + d.gray.ToString("F2") + " bright " + d.brightness.ToString("F2") + "; ";
                sb.Append("tower cards in combat with gold " + s.Economy.Gold + ": " + dims + "(the outline copies take the same UiDisable grey / brightness)\n");
                s.Economy.AddGold(spend);
            }
            // ---- outline close-ups (no UI) at in-game scale, x4 nearest
            {
                float wt = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - wt < 8f && s.Enemies.Active.Count < 4) yield return null;
                yield return WaitRt(1.2f); TimeController.SetPaused(true); yield return null; yield return null;
                captureNoUi = true; yield return Clean(); yield return new WaitForEndOfFrame(); var tex = Grab(); captureNoUi = false;
                Save(tex, "outline_board_" + Res + "_noui.png");
                foreach (var t in s.Towers.Towers) if (t != null)
                {
                    var r = ScreenBounds(cam, RendBounds(t.gameObject)); var cz = Crop(tex, Pad(r, 14 * hs, 64 * hs), 4);
                    string nm = t.Data.name; Save(cz, "outline_tower_" + nm + "_" + Res + "_x4.png"); Destroy(cz); sb.Append("tower " + nm + " screen rect " + r + "\n");
                }
                GameObject core = null; foreach (var r in FindObjectsOfType<Renderer>()) if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_Core_Prop")) { core = r.gameObject; break; }
                if (core != null) { var r = ScreenBounds(cam, RendBounds(core)); var cz = Crop(tex, Pad(r, 14 * hs, 64 * hs), 4); Save(cz, "outline_core_" + Res + "_x4.png"); Destroy(cz); sb.Append("core screen rect " + r + "\n"); }
                var kinds = new HashSet<string>(); int ne = 0;
                foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn && kinds.Add(e.Data.name) && ne < 4)
                {
                    var r = ScreenBounds(cam, RendBounds(e.gameObject)); if (r.xMin < 0 || r.yMin < 0 || r.xMax > Screen.width || r.yMax > Screen.height) continue;
                    var cz = Crop(tex, Pad(r, 12 * hs, 56 * hs), 4); Save(cz, "outline_enemy_" + e.Data.name + "_" + Res + "_x4.png"); Destroy(cz); ne++; sb.Append("enemy " + e.Data.name + " screen rect " + r + "\n");
                }
                Destroy(tex);
            }
            // ---- damage numbers: Fire / Lightning colours, queue (3 hits), 4th push, crit '!'
            {
                var bars = EnemyHpBarsUI.Instance; var mid = new Vector2(Screen.width * .5f, Screen.height * .55f);
                var picks = new List<Enemy>(); var cand = new List<(Enemy e, float d)>();
                foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn && e.HP > 25f && bars != null && bars.TryGetBar(e, out var br)) cand.Add((e, (br.center - mid).sqrMagnitude));
                cand.Sort((a, b) => a.d.CompareTo(b.d)); var used = new List<Vector2>();
                bool Inner(Rect br) => br.center.x > 160f * hs && br.center.x < Screen.width - 160f * hs && br.yMin > 60f * hs && br.yMax < Screen.height - 160f * hs; // numbers are clamped on-screen near the edges
                for (int pass = 0; pass < 2 && picks.Count < 3; pass++)
                    foreach (var c in cand) { bars.TryGetBar(c.e, out var br); if (picks.Contains(c.e) || (pass == 0 && !Inner(br)) || used.Exists(u => (u - br.center).magnitude < 260 * hs)) continue; used.Add(br.center); picks.Add(c.e); if (picks.Count == 3) break; }
                // picks[0] (most central, clear of the edges) takes the queue test; Lightning and the crit use the next targets, re-using one (after its numbers expire) when fewer are on screen
                int nPk = picks.Count; Enemy pkL = nPk > 0 ? picks[Mathf.Min(1, nPk - 1)] : null, pkQ = nPk > 0 ? picks[0] : null, pkC = nPk > 0 ? picks[Mathf.Min(2, nPk - 1)] : null;
                sb.Append("number targets " + picks.Count + "\n");
                Texture2D Close(Enemy e, Texture2D src) { bars.TryGetBar(e, out var bar); return Crop(src, Rect.MinMaxRect(bar.center.x - 170 * hs, bar.yMin - 50 * hs, bar.center.x + 170 * hs, bar.yMax + 150 * hs), 3); }
                if (pkL != null)
                {
                    pkL.TakeDamage(4f, StoneSignal.VFX.DamageKind.Lightning, false); yield return WaitRt(.12f);
                    pkL.TakeDamage(5f, StoneSignal.VFX.DamageKind.Lightning, false); yield return WaitRt(.08f);
                    string cols = ""; foreach (var n in FindObjectsOfType<StoneSignal.VFX.UiDamageNumber>()) if (n.gameObject.activeSelf && n.target == pkL) cols += "lightning " + (Color32)n.text.color + " '" + n.text.text + "'; ";
                    yield return new WaitForEndOfFrame(); var tex = Grab();
                    var b = Close(pkL, tex); Save(b, "numbers_lightning_" + Res + "_closeup.png"); Destroy(b); Destroy(tex);
                    Check(cols.Contains("lightning RGBA(237, 255, 140"), "Lightning (雷铃兽 / Pulse) number colour lemon-white #EDFF8C: " + cols + " | Fire (unused) defined as #" + ColorUtility.ToHtmlStringRGB(StoneSignal.VFX.DamageNumbers.ColorFor(StoneSignal.VFX.DamageKind.Fire)));
                }
                if (pkQ != null && pkQ.Alive)
                {
                    var e = pkQ; if (e == pkL) yield return WaitRt(1.8f); float t0 = Time.unscaledTime;
                    e.TakeDamage(3f); while (Time.unscaledTime - t0 < .1f) yield return null; e.TakeDamage(4f); while (Time.unscaledTime - t0 < .2f) yield return null; e.TakeDamage(5f);
                    while (Time.unscaledTime - t0 < .26f) yield return null;
                    var q = StoneSignal.VFX.DamageNumbers.DebugQueue(e); string ql = ""; bool rise = q.Count == 3, alt = q.Count == 3, xs = true; float s1 = DamageNumbers186Scale();
                    for (int i = 0; i < q.Count; i++)
                    {
                        var r = ScreenRect(q[i].rt); ql += "#" + i + " '" + q[i].text.text + "' side " + q[i].side + " lift " + q[i].liftPx.ToString("F1") + " cur " + q[i].CurrentLiftPx.ToString("F1") + " y " + q[i].rt.position.y.ToString("F1") + " x " + q[i].rt.position.x.ToString("F1") + "; ";
                        if (i > 0) { if (q[i].side == q[i - 1].side) alt = false; if (q[i].rt.position.y < q[i - 1].rt.position.y + (ch.numberQueueStepPx - 1f) * s1) rise = false; }
                    }
                    bars.TryGetBar(e, out var bar); foreach (var n in q) { float dx = Mathf.Abs(n.rt.position.x - bar.center.x); if (dx > (ch.numberDriftXPx + 1f) * s1) xs = false; }
                    yield return new WaitForEndOfFrame(); var tex = Grab(); // positions read above: world corners are stale in the frame a Grab() switches the canvases
                    var cz = Close(e, tex); Save(cz, "numbers_queue3_" + Res + "_closeup.png"); Destroy(cz); Save(tex, "numbers_queue3_" + Res + "_full.png"); Destroy(tex);
                    Check(rise && alt && xs && Mathf.Abs(ch.numberDriftXPx - 28f) < .01f, "queue: 3 hits in 0.2 s -> each newer number above the previous one's current position (+" + ch.numberQueueStepPx + " px), sides alternate, |x| <= " + ch.numberDriftXPx + " px | " + ql);
                    var oldest = q.Count > 0 ? q[0] : null; e.TakeDamage(6f); yield return null;
                    var q2 = StoneSignal.VFX.DamageNumbers.DebugQueue(e);
                    Check(oldest != null && oldest.Fading && !q2.Contains(oldest) && q2.Count == 3, "4th number pushes the oldest into its fade-out (oldest fading " + (oldest != null && oldest.Fading) + ", alpha " + (oldest != null ? oldest.text.alpha.ToString("F2") : "-") + ", queue now " + q2.Count + ")");
                }
                if (pkC != null && pkC.Alive && pkC.HP > 15f)
                {
                    var e = pkC; // prefer the most central live target clear of the screen edges (cand is sorted by distance to the centre)
                    foreach (var c in cand) if (c.e != null && c.e.Alive && c.e.HP > 15f && c.e != pkQ && bars.TryGetBar(c.e, out var cbr) && Inner(cbr)) { e = c.e; break; }
                    if (e == pkQ || e == pkL) yield return WaitRt(1.8f); e.TakeDamage(14f, StoneSignal.VFX.DamageKind.Physical, true); yield return WaitRt(.12f);
                    StoneSignal.VFX.UiDamageNumber cn = null; foreach (var n in FindObjectsOfType<StoneSignal.VFX.UiDamageNumber>()) if (n.gameObject.activeSelf && n.target == e && n.crit) cn = n;
                    float numR = 0f, bangL = 0f; if (cn != null && cn.Bang != null) { cn.text.ForceMeshUpdate(); cn.Bang.ForceMeshUpdate(); numR = cn.text.rectTransform.TransformPoint(new Vector3(cn.text.textBounds.max.x, 0, 0)).x; bangL = cn.Bang.rectTransform.TransformPoint(new Vector3(cn.Bang.textBounds.min.x, 0, 0)).x; }
                    yield return new WaitForEndOfFrame(); var tex = Grab();
                    var cz = Close(e, tex); Save(cz, "numbers_needle_crit_" + Res + "_closeup.png"); Destroy(cz); Destroy(tex);
                    if (cn != null && cn.Bang != null)
                    {
                        // number glyph bounds vs '!' glyph bounds (screen px): the '!' starts >= gap right of the number, no overlap
                        float gapPx = bangL - numR;
                        Check(cn.Bang.gameObject.activeSelf && !cn.text.text.Contains("!") && gapPx >= (ch.numberCritGapPx - 1.5f) * DamageNumbers186Scale(), "crit '!' is its own glyph right of the number: number '" + cn.text.text + "' right edge " + numR.ToString("F1") + ", '!' left edge " + bangL.ToString("F1") + " -> glyph gap " + gapPx.ToString("F1") + " px (layout gap " + ch.numberCritGapPx + " ref px + glyph bearings)");
                    }
                    else Check(false, "crit number / '!' not found");
                }
                TimeController.SetPaused(false);
            }
            // ---- gold ring: radius cap 48 ref px
            {
                var coin = ui.GoldTarget; var ring = ui.DebugGoldRing; var counter = ui.GoldCounter; var orb = ui.DebugCoreOrb; var cvs = coin.GetComponentInParent<Canvas>().rootCanvas; float sf = cvs.scaleFactor;
                var cr = ScreenRect(coin); var orr = ScreenRect(orb); float gap = (cr.center - orr.center).magnitude - orr.width * .5f;
                float rMax = ring.rect.width * .5f * .92f * counter.ringMaxScale, rMin = ring.rect.width * .5f * .92f * counter.ringMinScale;
                Check(rMax <= ch.goldRingMaxRadiusPx + .05f && rMax * sf < gap, "gold ring visible radius " + rMin.ToString("F1") + " -> " + rMax.ToString("F1") + " ref px (cap " + ch.goldRingMaxRadiusPx + " = " + (ch.goldRingMaxRadiusPx / ui.DebugGoldPill.rect.height).ToString("F2") + " x pill; scale " + counter.ringMinScale.ToString("F3") + " -> " + counter.ringMaxScale.ToString("F3") + "); orb gap " + (gap / sf).ToString("F1") + " ref px -> clear by " + (gap / sf - rMax).ToString("F1"));
                s.Economy.AddGold(5); counter.Add(5); float tr = Time.unscaledTime;
                foreach (var at in new[] { .1f, .2f, .3f })
                {
                    while (Time.unscaledTime - tr < at) yield return null; yield return new WaitForEndOfFrame(); var g = Grab();
                    var cz = Crop(g, Rect.MinMaxRect(orr.xMin - 10, Mathf.Min(orr.yMin, cr.yMin) - 60, cr.xMax + 260 * sf, orr.yMax + 10), 2); Save(cz, "goldring_" + Res + "_t" + Mathf.RoundToInt(at * 1000) + "ms.png"); Destroy(cz); Destroy(g);
                    sb.Append("  gold ring t+" + at + " s: scale " + ring.localScale.x.ToString("F3") + " -> visible radius " + (ring.rect.width * .5f * .92f * ring.localScale.x).ToString("F1") + " ref px, alpha " + ring.GetComponent<UnityEngine.UI.Image>().color.a.ToString("F2") + "\n");
                }
            }
            // ---- Seismic recoil hook + fire FX (one forced shot)
            {
                Tower seis = null; foreach (var t in s.Towers.Towers) if (t != null && t.Data.lobbedShot) seis = t;
                Enemy tgt = null; foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn) tgt = e;
                if (seis != null && tgt != null)
                {
                    var rc = seis.FireAnimation as TowerRecoil; Vector3 rest = rc != null && rc.Part != null ? rc.Part.position : Vector3.zero;
                    seis.Fire(tgt); float f0 = Time.time, mx = 0f, back = 0f; float at = 0f; bool sampled = false;
                    while (Time.time - f0 < .2f) { yield return null; if (rc != null) { float o = rc.Offset; if (o > mx) { mx = o; at = Time.time - f0; } if (Time.time - f0 > .11f && !sampled) { back = o; sampled = true; } } }
                    Check(rc != null && mx > .045f && mx < .062f && back < .004f, "Seismic recoil hook: " + (seis.FireAnimation != null ? seis.FireAnimation.GetType().Name : "none") + " on '" + (rc != null && rc.Part != null ? rc.Part.name : "-") + "' max " + mx.ToString("F3") + " m at t+" + at.ToString("F3") + " s (first sampled frame), " + back.ToString("F4") + " m after 0.11 s (spec 0.06 m back, 0.1 s EaseOutCubic)");
                }
                else Check(false, "Seismic recoil: no Seismic tower / target");
            }
            Destroy(texBuild); Destroy(texCombat);
            sb.Insert(0, "V19 / V18.6b CHECKS: " + nPass + " pass, " + nFail + " fail (" + Res + ", seed " + seed + ")\n");
            File.WriteAllText(Path.Combine(Dir, "v19shots_" + Res + ".txt"), sb.ToString()); Debug.Log("V19 / V18.6b CHECKS: " + nPass + " pass, " + nFail + " fail");
        }
        static float DamageNumbers186Scale() => StoneSignal.VFX.DamageNumbers.PxScale;
    }
}
#endif
