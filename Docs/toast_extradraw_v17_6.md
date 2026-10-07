# v17.6 — ExtraDraw reward icon, chunky toon toast

Art round for 程序开发 on code 7831afa (v18.1) / art dc5ff38.
Preview: `toast_extradraw_v17_6.png` (toast on the real notice captures, both icon variants, motion frames, ExtraDraw card next to NextDraw) and `reward_icons_v17_6.png` (icon sheet).

## 1. ExtraDraw icon — `ui_reward_extra_draw.png`

- Same generator and style as the v17.4 reward icons: `ArtSource/Stylized/UI/reward_icons_v17_4.py`, new `extra_draw()`. Re-running it reproduces the 15 v17.4 icons byte for byte.
- Motif:
  - the ember-rune draw-pile card back (the `ui_draw_pile` look: #3A3268 fill, gloss band, bottom shade, gold border with corner studs, gold ring with diamond studs, orange ember flame);
  - a second back fanning off to the right (+18°);
  - a gold **+1** badge.
- No video-ad badge.
- It reads apart from `ui_reward_next_draw` (purple deck + up arrow + plus cross): there's no arrow, the pile is tilted, and the badge says +1 instead of showing a cross.
- `RewardIconMap.For(RewardEffect.ExtraDraw)` → `"ui_reward_extra_draw"`.
  - GameUI's reward icon lookup already prefers `RewardIconMap`, so the `ui_draw_pile` placeholder fallback stops being used. No GameUI change is needed.
  - The baked shadow is detected by name (`HasBakedShadow`), so the soft IconShadow stays hidden.
- `reward_icons_v17_4.json`: new ExtraDraw entry (rarity 精良). `reward_icons_v17_4_preview.py`: ExtraDraw added; paths are now arguments.

## 2. Toast — `ui9_toast.png`, `ui_icon_warn.png`, `ui_icon_info.png`

Painter: `ArtSource/Stylized/UI/toast_paint_v17_6.py` (4× paint, LANCZOS down).

| Sprite | Size | Notes |
|---|---|---|
| `ui9_toast` | 128×78 | 9-slice L40 B41 R43 T35 (pinned in `StylizedFxV14.BuildAtlas`; no hand-written meta). The pill is the top-left 125×72; the bottom 6 px / right 3 px are the baked hard shadow (navy 50 %). Fill #2A2F5A, top gloss #404780, bottom toon shade #1F2346, navy ink stroke 2.5 px, cream #FFFAF4 outline 4 px, navy #1E1A3A edge 3 px. Same family as `ui9_pill_counter`. |
| `ui_icon_warn` | 88×88, shown at 44 | Red #E5484D rounded triangle with a white `!`, cream outline + navy edge + hard shadow. For invalid actions. |
| `ui_icon_info` | 88×88, shown at 44 | Gold #FABE2C circle with a navy `i` (navy for contrast on gold). For neutral info. |

### Layout (1920×1080 reference, canvas scale 1)

- Rect height **78** (72 pill + 6 shadow).
- Label rect: left 28 (+44 icon +10 gap when an icon is shown), right 28+3, bottom 6, top 0. This centres the text on the pill, not the shadow.
- Icon 44×44 at x 28, centred on the pill.
- Width = text preferred width + 28·2 + 3 (+54 with an icon), clamped 200–900.
- Text: StoneSignalRoundedCN-Heavy (the label already goes through `UseCn`), 30 px, white, outline #1E1A3A 0.25.

### Motion (`ToastFx`, unscaled time)

- Pop-in: scale 0.85 → 1.05 (at 60 %) → 1, alpha 0 → 1 (ease-out), over **0.18 s**. Scale is about the pill centre, whatever anchor GameUI uses.
- Hold **1.2 s**.
- Fade-out **0.2 s** while rising **12 px**.
- Total 1.58 s.
- Restarts on a new text and whenever GameUI re-shows the pill. There is still one toast at a time (GameUI owns that).

### Icon choice (`ToastStyle.KindFor`)

- Info (gold): 建造完成 / 已放置，路线已更新 / 没有墙牌了，开始下一波吧 (raw English keys accepted too).
- Everything else is warn (red): 这里不能放, 金币不足, 先选一座塔, 不能堵死通往核心的路线, 已有塔, 超出棋盘, ….
- `noticeFx.SetKind(Kind.None)` hides the icon.

## 3. Hookup (GameUI is dev-owned — not edited)

The toast lives in `GameUI` (committed, `noticePill` + `hint`).
`Scripts/UI/ToastStyle.cs` (new) restyles it from outside. Changes to make in `GameUI`:

```csharp
// field
private StoneSignal.UI.ToastFx noticeFx;

// Build(), right after the notice pill is created (after UseCn(hint), before SetActive(false)):
noticeFx = StoneSignal.UI.ToastStyle.Apply(noticePill, hint, S);   // v17.6 toast: ui9_toast, 78 px rect, icon slot, 30 px text, ToastFx

// timing: let the fade-out finish (0.18 pop + 1.2 hold + 0.2 fade)
private const float NoticeSeconds = 1.58f, NoticeRepeatGap = 1f, NoticeClearance = 48f;
```

Recommended, so `PlaceNotice` measures the final width:

```csharp
// Update(): "+ 72, 240, 900" ->
noticePill.sizeDelta = new Vector2(Mathf.Clamp(hint.GetPreferredValues(notice).x + StoneSignal.UI.ToastStyle.ExtraWidth(true),
    StoneSignal.UI.ToastStyle.MinWidth, StoneSignal.UI.ToastStyle.MaxWidth), noticePill.sizeDelta.y);
// ShowNotice(): after "notice = text; noticeShownAt = now; ...":
noticeFx?.Restart();   // needed once the width matches: ToastFx then can't see GameUI's re-show from the width change
```

Without these two lines it still works: `ToastFx.LateUpdate` re-sizes the pill in the same frame, and GameUI's width reset (+72) marks a re-show.

### Notes

- **Repeat rule:** the art spec says a repeat of the same message restarts the timer. The planner rule in GameUI ignores the same text within 1 s, and `GameplayShot -cnshots3` asserts that. ToastFx restarts whenever GameUI re-shows the toast, so both work; dropping the 1 s gap is a planner decision.
- **NoticeSeconds:** 1.58 keeps the `GameplayShot` toast checks valid (still shown at 1.42 s, gone at 1.62 s). To keep exactly 1.5 s instead, set `noticeFx.hold = 1.12f`.
- **Position:** keep the current slot logic (the free top slot with clearance from the core and route arrows, inside the safe area).
- **Hand fade:** fading the drag hand to 35 % is fine with art.
- **DC:** the icon is in HUD.spriteatlas and batches with the pill; the CanvasGroup adds no draw call. The label already had its own material instance, so the toast stays pill + label (unchanged).

## 4. BatchImport / BatchWire (程序开发)

1. Let the editor import the 4 new PNGs in `Assets/Game/Art/Stylized/UI`. Tuanjie writes the metas.
2. **BatchImport** (`StylizedFxV14.BuildAll` → `BuildAtlas`):
   - sprite import settings;
   - pins `ui9_toast` border (40, 41, 43, 35) and zero borders for the icons;
   - repacks `HUD.spriteatlas`.
   
   By area the UI folder goes from ~56.5 % to ~58.6 % of one 2048 page, so it should stay **1 page**. Please confirm in the atlas check.
3. **BatchWire**: refreshes `ArtCatalog.uiSprites` from the UI folder, so `UiSprite("ui_reward_extra_draw" / "ui9_toast" / "ui_icon_warn" / "ui_icon_info")` resolve.

If BatchWire runs before BatchImport, the sprites still resolve, but `ui9_toast` has no border until BuildAtlas runs.

Checks: the `GameplayShot` reward icon audit should now find every RewardEffect icon, ExtraDraw included. Compile: `ToastStyle.cs` only uses UnityEngine.UI + TMPro (same as GameUI).

## 5. v17.6b — NextDraw icon 「符文保底」

- **Why:** in the v18 glossary `RewardEffect.NextDraw` is 「符文保底」 = 下次抽牌至少 1 张带符文. The v17.4 icon (deck + up arrow) showed the old "draw +N" text.
- **New `ui_reward_next_draw.png`** (`reward_icons_v17_4.py` → `next_draw()`, same 256 px chunky toon style):
  - a blue blueprint wall card (#365CAA, like the hand cards) with a light-blue T piece;
  - a neutral rune socket on the T's stem: gold-rimmed hex, navy face, white star, pale-gold glow halo. No specific rune colour;
  - a gold shield with a cream check (guarantee) at the bottom right.
  - At 96 px it reads apart from ExtraDraw (fanned dark draw-pile backs + gold +1) and AddBlock (navy card, grey O piece, gold plus).
- **Glossary audit (16 icons):**
  - Only NextDraw was wrong.
  - Text-only fixes in the icon JSON, `RewardIconMap` comments and the sheet labels: 石牌补给 → 墙牌补给, 下次抽牌 +N → 符文保底, 塔价折扣 → 建塔折扣.
  - The other 15 match their glossary names and effects, including ExtraDraw 免广告再抽一次, which the reward card from dev commit 7831afa (v18.1) grants.
- **Hookup:** same file name, already in `HUD.spriteatlas` and `ArtCatalog`, so no code change is needed. Let the editor reimport the PNG; a BatchImport run repacks the atlas.
- **Preview:** `next_draw_v17_6b.png` (`ArtSource/Stylized/UI/next_draw_preview_v17_6b.py`).
