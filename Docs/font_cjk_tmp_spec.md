# CJK TMP font — StoneSignal Rounded CN Heavy (v17.1)

## Files (`Assets/Game/Art/Fonts/`)
| File | Notes |
|---|---|
| `StoneSignalRoundedCN-Heavy.ttf` | 1,461 KB (1,495,716 B). 4,188 glyphs + .notdef. TrueType outlines, no hinting, GSUB/GPOS kept only for `kern`/`palt`/`halt`. fsType 0 (installable embedding). |
| `StoneSignal_CJK_Charset.txt` | The same 4,188 characters, UTF-8, **no newline**, so it can go straight into TMP's *Characters from File*. |
| `StoneSignalRoundedCN - OFL.txt` | SIL OFL 1.1 plus the copyright notices. **Must ship with the font. Keep it in the repo.** |

### Source and license
- Source font: **Resource Han Rounded CN Heavy 0.990** by Cyano Hao (https://github.com/CyanoHao/Resource-Han-Rounded). It is a rounded derivative of Adobe Source Han Sans.
- The fonts are licensed under **SIL Open Font License 1.1** (repo LICENSE.md; name ID 13 in the font).
- What OFL 1.1 allows:
  - Commercial use: yes.
  - Bundling or embedding in a game build, including inside Unity asset bundles and TMP atlases: yes.
  - Subsetting or modifying: yes.
- What OFL 1.1 requires:
  - You may not sell the font file *by itself*.
  - The license text and copyright notice must travel with the font.
  - A Modified Version may not use the Reserved Font Name. Here that name is Adobe's **"Source"**.
- Compliance for this subset:
  - A subset counts as a Modified Version under OFL, so the family was renamed to **StoneSignal Rounded CN** (PostScript name `StoneSignalRoundedCN-Heavy`).
  - The copyright string is kept.
  - Generated TMP SDF atlases are fine to ship; they are font "documents"/derived bitmaps.

### Charset (4,188)
- 3,500 characters: 通用规范汉字表 一级字表 (the 2013 "3500 common characters" list).
- Plus GB2312 level-1 (3,755 characters; together with the list above this makes 3,874 hanzi).
- Plus 5 more hanzi from the GDD that are in neither list: 咚弩曜藓阈.
- Plus every CJK character in `Assets/**/*.cs|*.asset|*.json|*.prefab|*.unity`, `Docs/**`, and `ArtSource/**`. There are 33 such characters, all already covered.
- Plus every CJK character and punctuation mark in `StoneSignal_玩法设计文档.md` (1,150).
- Plus ASCII U+0020–007E.
- Plus CJK symbols U+3000–301F, full-width forms U+FF01–FF5E, GB2312 symbol rows A1/A3, and `·–—‘’“”…※←↑→↓★☆●○■□◆◇▲△▼▽√×÷±°©®™€¥￥`.
- Every character exists in the font (0 missing).
- **When you add new Chinese UI text**, regenerate from the full source font with the command below and rebake the atlas. For a quick fix you can also add the missing characters to a small dynamic fallback (see §3).

```
# regenerate (box: /workspace/font)
pyftsubset ResourceHanRoundedCN-Heavy.ttf --unicodes-file=unicodes.txt --layout-features=kern,palt,halt \
  --no-hinting --notdef-outline --name-IDs='*' --drop-tables+=DSIG,vhea,vmtx,VORG --output-file=out.ttf
# then rename name IDs 1/3/4/6/16/17 to "StoneSignal Rounded CN" (see the rename script in the v17.1 report)
```

## 1. TTF import settings
Select `StoneSignalRoundedCN-Heavy.ttf` and set:
- Character = *Dynamic*.
- Include Font Data = **on**. Needed only if you also create the dynamic fallback in §3.
- Leave everything else at its default.

## 2. Generate the static TMP SDF asset (Window ▸ TextMeshPro ▸ Font Asset Creator, TMP 3.0.9)
| Setting | Value |
|---|---|
| Source Font File | `StoneSignalRoundedCN-Heavy` |
| Sampling Point Size | **Custom 48**. "Auto Sizing" would land at about 49–51; 48 leaves headroom. |
| Padding | **6** |
| Packing Method | **Optimum** |
| Atlas Resolution | **4096 × 4096** |
| Character Set | **Characters from File** → `StoneSignal_CJK_Charset.txt` |
| Render Mode | **SDFAA** |
| Get Kerning Pairs | on |

Then **Save as** `Assets/Game/Art/Fonts/StoneSignalRoundedCN-Heavy SDF.asset`.

Check the result:
- The creator log should read *"Characters packed: 4188 / 4188"*. Missing should be 0, apart from control characters it may skip, such as U+3000 shown as a space.
- If it reports any missing characters, drop the sampling size to 46.

Why 4096: I simulated the packing with these exact glyphs and padding 6 (shelf packer).
- 4096² fits all characters at a sampling size of about 51.
- 2048² only fits at about 18 px. That is too soft for Heavy strokes, and the outline would eat the counters.
- Memory: the Alpha8 4096² atlas is 16 MB uncompressed.
  - Mobile option if that's too big: open the generated asset and set *Atlas Population Mode = Dynamic*, *Multi Atlas Textures = on*, *Atlas Width/Height = 2048*, sampling 48, padding 6, SDFAA.
  - Then click *Update Atlas Texture* with the charset file. TMP 3.0.9 supports multi-atlas in Dynamic mode, so this gives 4 × 2048² pages at the same quality.

After generation, on the font asset:
- Face Info ▸ Line Height: leave as generated.
- Atlas Population Mode: **Static** for the 4096 option.

## 3. Fallback chain (keep LiberationSans as the primary Latin face)
Open `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` ▸ *Fallback Font Assets*:
1. **`StoneSignalRoundedCN-Heavy SDF`** (new): add it first.
2. `LiberationSans SDF - Fallback` (the existing dynamic one): move it below.

Also add `StoneSignalRoundedCN-Heavy SDF` to `Assets/TextMesh Pro/Resources/TMP Settings.asset` ▸ *Fallback Font Assets*. This covers labels that use a different primary font.

Optional safety net for runtime-only text such as player names:
- Duplicate the SDF asset, set it to *Dynamic*, *Clear Dynamic Data on Build*, atlas 1024.
- Put it last in the chain. It will rasterise only characters that are in the TTF. A subset TTF cannot render anything outside the charset; for that, point this fallback at the full source TTF instead (not committed, 13.7 MB).

Note: the Latin and digits in the CJK font are rounded heavy too. For a uniform look on all-Chinese screens such as the reward cards, you can assign `StoneSignalRoundedCN-Heavy SDF` directly as the primary font of those TMP labels. `RewardPickUI.font` already exposes this.

## 4. Material presets
TMP creates `StoneSignalRoundedCN-Heavy SDF Material` (the default; leave it untouched for plain text). Duplicate it into the same folder:

**`StoneSignalRoundedCN-Heavy SDF - Outline.mat`** (recommended for HUD, card titles, floating numbers):
| Property | Value |
|---|---|
| Shader | TextMeshPro/Distance Field (Mobile: *TextMeshPro/Mobile/Distance Field*) |
| Face ▸ Color | #FFFAF4 (cream; or leave white and tint per label with vertex colour) |
| Face ▸ Dilate | 0.05 |
| Outline ▸ Color | **#1E1A3A** (navy, same as the card/HUD frame stroke) |
| Outline ▸ Thickness | **0.25** |
| Underlay (optional drop) | Color #1E1A3A A=180, Offset X 0 / Y −0.6, Dilate 0.25, Softness 0 |
| Debug ▸ Gradient Scale | leave as generated (padding 6 + 1 = 7) |

- Assign it as the label's *Material Preset*.
- On fallback glyphs, TMP mirrors the primary material's properties onto the fallback material automatically. So a LiberationSans label using `LiberationSans SDF - Outline` (set to the same values) renders matching outlines on the Chinese glyphs.
- For a perfect match, also set `LiberationSans SDF - Outline.mat` to outline #1E1A3A / 0.25.
- An outline of 0.25 at padding 6 / sampling 48 stays inside the SDF spread, so there is no clipping. Don't push it past ~0.35 without raising padding to 8.

Font sizes: Heavy reads well from 18 px up. Below 16 px, use the face without outline or with outline 0.15.

Preview: `/workspace/previews/font_cjk_v17_1.png` (PIL stroke approximation of the outline preset).
