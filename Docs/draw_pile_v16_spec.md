# DRAW pile v16 spec (ref 1920x1080, CanvasScaler ScaleWithScreenSize match=1 height)
Rule: 2 draws/wave: #1 FREE, #2 rewarded video (placeholder), no gold draws. Hand persists, max 7.
Root DrawPileRoot 200x268 (whole rect = touch target, >=88), anchor/pivot bottom-right, pos (-312,+24) inside safe area
 (BATTLE 256x104 at (-24,+24) -> 32px gap). Card 124x160 at (30,8) from top-left, layers step (+4,+7), 3-5 visible.
Pill ui9_draw_bubble 172x60 (9-slice L30 R30 T28 B28), bottom-centre y=34, tail ui_draw_bubble_tail 26x15 above it; icon 40x40 left (x=36), label TMP 24 bold #1E1A3A.
States: Free=ui_icon_free+"FREE"; Ad=ui_badge_video_ad+"DRAW"; Used="0 / 2" greyed, non-interactive, no bob; Full=hand 7/7 "FULL" greyed, no bob.
Bob: +-4px, 1.6 s, unscaled. PlayDrawPulse: 0.35 s squash/hop 14px + pill 1.08.
Hand count: navy pill ui9_panel_navy 108x52 left-aligned above block row (bottom-left anchor, pos (+24+safeL, block row top +8)), TMP 28 "5/7", red #E5484D at 7/7.
Hand row width budget = refWidth - 24 - safeL - 512 - 24; card w = min(150, budget/7 - 12):
 16:9 ref 1920 -> budget 1360 -> 150 (7 cards = 1134);  19.5:9 ref 2340, safe 132/side -> 150;  20:9 ref 2400 -> 150;  4:3 ref 1440 -> budget 880 -> 113 (keep >=88).
Mockup: /workspace/previews/draw_pile_v16.png
