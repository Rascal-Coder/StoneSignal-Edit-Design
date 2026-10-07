# DRAW pile spec v16.1 (ref 1920x1080, CanvasScaler Scale With Screen Size, match = 1 (height))
Rule: 2 draws per wave: #1 FREE, #2 rewarded video (placeholder), no gold draws. Block hand persists across waves, max 7.

## DrawPileUI (StoneSignal.VFX) — root 200x268 (whole rect = touch target), anchor/pivot bottom-right, pos (-312, +24) inside safe area
- BATTLE 256x104 at (-24, +24) -> 32 px gap.
- Pill ui9_draw_bubble 172x60 (9-slice L30 R30 T28 B28) top-centre (y 0..60).
  Content = HorizontalLayoutGroup (padding L14 R18, spacing 8, middle-centre, 4 px bottom bevel): icon 40x40 (LayoutElement) + TMP label 24 bold #1E1A3A (no wrap). Icon and label never overlap.
- Cards: 124x160 at (30, 72) from top-left (10-12 px gap under pill), layers step (+4, +7), 3-5 visible -> bottom 260 within 268.
- Option B (default) showTail=false: plain pill. Option A showTail=true: ui_draw_bubble_tail 26x15 flipped (scaleY -1) under pill centre, pointing down.
- States: Free = ui_icon_free + "FREE"; Ad = ui_badge_video_ad + "DRAW"; Used = "0 / 2" greyed, non-interactive, no bob; Full = hand 7/7 "FULL" greyed, no bob (icon hidden, label centred).
- Bob +-4 px / 1.6 s unscaled. PlayDrawPulse 0.35 s squash + 14 px hop, pill x1.08.

## HUD layout accepted from dev (v16.1)
- Top-left: WAVE banner/panel (24, 24); wave tooltip directly UNDER the WAVE banner (24, 132), navy panel, max width 500, 20 pt.
- Bottom-left, all left-aligned at x = 24 + safeL, rows from the bottom:
  1. Tower hand (bottom row): tower cards 150x190, step 166, bottom +24 (top at y = 24+190 = 214 from bottom).
  2. Block row ON ITS OWN ROW above the tower hand: block cards 104x104, step 116, bottom at 214+24 = 238 (top 342).
  3. Block hand count "5/7" above the block row: ui9_panel_navy 108x52, bottom at 342+8 = 350; TMP 28, #FFECBE, red #E5484D at 7/7 (DrawPileUI.SetHandCount).
- Right edge of both rows must stay left of the DRAW pile: maxRight = refWidth - 312 - 200 - 24.
- Block row 7 cards: width = 7*116-12 = 800 -> cell = min(104, (budget+12)/7 - 12); tower hand scales the same with step 166 / card 150.
  budget = refWidth - 24 - safeL - 536:
  | aspect | ref width | safeL | budget | block cell | tower card |
  | 16:9   | 1920 | 0   | 1360 | 104 | 150 |
  | 19.5:9 | 2340 | 132 | 1648 | 104 | 150 |
  | 20:9   | 2400 | 132 | 1708 | 104 | 150 |
  | 4:3    | 1440 | 0   | 880  | 104 (7*116-12=800 fits) | 150 for <=5 cards, else min(150,(880+16)/n-16) (>=88) |
- Touch targets: block cell >= 88 (104), tower card 150x190, DRAW 200x268, BATTLE 256x104.

Mockups: /workspace/previews/draw_pile_v16.png (v16), /workspace/previews/draw_pile_v16_1.png (v16.1, options A/B + HUD).
