# Reward pick cards v17 — style diagnosis + spec

## Why v16.2 clashed with the HUD (compared with ui_card_tower_frame, ui9_button_battle_orange, ui9_pill_gold, WAVE banner)
| v16.2 reward card | Current HUD language | Result |
|---|---|---|
| Thin 4–10 px rarity-coloured rim; body is navy | Tower card: thick **cream #FFFAF4 8 px outline** + navy 4 px ink edge, body is a **saturated solid fill** (red #C8283C) | Reward cards read as dark "panels", not cards; rarity colour is a hairline |
| Gold inlay line + 4 diamond studs + gold-ring icon socket with studs | No inlays or filigree anywhere; ornament = none | Ornate/"fantasy TCG" vs chunky mobile toon |
| Top bevel / bottom shade bands, sheen sweep, painted flame edge with glow | Flat fills; only a single light gloss strip near the top + a flat darker cost band | Gradients and glows look like a different art set |
| Notched ribbon + separate gem + tier text = 3 rarity cues | One cue per element (colour / small badge) | Noisy, competes with the title |
| Small cream text on navy, no stroke | Bold white text with navy stroke ("70", BATTLE, WAVE 1/3) | Low contrast, weak on phones |
| Rounded radius 30/160 at a thin rim | Radius ~22/160 behind a thick outline | Silhouette softer/thinner than every other card |

## v17 construction (sampled from ui_card_tower_frame)
- `ui9_reward_frame_{common,rare,epic,legendary}` 160×160, 9-slice border 36: navy #1E1A3A 4 px → cream #FFFAF4 8 px → flat fill → gloss strip (y 18–30).
- `ui9_reward_band_{rarity}` 96×96, border 12: dark text band + 3 px light top line (same as the cost strip).
- `ui9_reward_tier_pill` 120×60, border 29: navy pill with cream ring (like the ×3 / 2x2 / hotkey badges), TMP tier text 28.
- Tiers (GDD 8.2): 普通 grey #9692A4 · 精良 blue #3474D8 · 稀有 purple #8848D4 · 传说 orange-gold #F09628 (+3 flat white sparkles, twinkle).
- Text: title TMP 44 bold white, navy outline ≈5 px; desc TMP 28 white, outline ≈3 px, in the band.
- Card 400×560 (4:3: 380×540), gap 56 (4:3: 32), title y150, cards top y250 — unchanged from v16.2 layout.
- Removed from use: ui9_reward_ribbon, ui_reward_gem_*, ui_reward_icon_slot, ui_reward_sheen, ui9_reward_flame_edge (files deleted).
- Kept: ui_reward_ember (embers + legendary sparkles), ui_reward_burst (pick FX).

## API (unchanged)
`RewardPickUI.Show(RewardOption[] options, RewardRarity[] rarity)`, `PlayPick(int index, Vector2 targetScreen, Action onDone)`, `event OnPicked(int)`.
Inspector fields changed: `frames[4]`, `bands[4]`, `tierPill`, `ember`, `burst`, `font`.
