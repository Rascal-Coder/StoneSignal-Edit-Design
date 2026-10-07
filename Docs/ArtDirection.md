# StoneSignal – Art Direction Rules (stylized diorama, v7)

Sources: value-analysis practice (Game Developer, "How to Reduce Visual Confusion"), Frozenbyte level-art colour/lighting wiki, hue-shift ramp practice (Cobo, Egmatic colour-theory guide).

1. **Value hierarchy (squint/grayscale test every pass).** Water/background darkest. Ground tiles are dark and low-contrast. Walls are mid-light lavender. Turrets, enemies and the core get the highest value and saturation. Decoration (trees, islands, props) is desaturated and never brighter than the play layer.
2. **60-30-10 palette.** 60% world (teal water + warm earth), 30% structure (lavender stone), 10% accent (turret white/metal + team colour, enemy red, core cyan glow). Saturated accents only on gameplay-relevant objects.
3. **Ramps with a hue shift.** Each material is a short palette ramp: warm in the light, cool violet/indigo in the shadow (toon shadow tint, outline OutlineIndigo `1E1A3A`, never black). Use no grey shadows.
4. **One shape language.** Chunky, pillowy, beveled forms. The bevel is ~0.04–0.11 m relative to a 1 m cell (tiles 0.04, walls 0.11, plinths 0.03). There's one outline width for all props. Detail density is highest on the play layer (walls, turrets) and lowest on far decoration.
5. **No big decals.** Weathering is small: pebbles, thin cracks, world-space `_Mottle` noise. Never use full-face colour lids or dark squares that read as holes.
6. **Turret plinths are stone/metal** (StoneSide + Metal trim) and have no gold base plates. The gold/teal glow is reserved for the core ring and VFX.
7. **Water stays subtle.** It uses fine small ripples (scale 2.4), faint caustics and a thin soft shore foam. Water should never compete with the board.
8. **Framing / depth.** Use only 3–4 decoration islands, at the screen corners and partly off-screen, with desaturated foliage. The board fills most of the frame (ortho size 6.4 recommended for the Game.unity camera; it is currently 9.2 and art can't edit it). Warm key light, cool ambient; post: mild bloom and vignette.
