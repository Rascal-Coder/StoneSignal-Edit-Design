# Art sources and references

Verified 2026-10-06. Runtime meshes, materials and icons now come from two CC0 third-party
packs. The first-round Blender MCP art (`StoneSignal_Art.blend`, `StoneSignal_Tabletop.blend`,
`create_stonesignal.py`, `create_tabletop.py`, `render_tabletop_icons.py`, `art_manifest.json`)
is kept here as archive only; it is no longer imported and the editor tool that produced it
was removed.

## Imported packs

| Pack | Creator | Downloaded from | License | Shipped proof |
|---|---|---|---|---|
| Tower Defense Kit | Kenney | `https://kenney.nl/media/pages/assets/tower-defense-kit/a402493eaa-1726471567/kenney_tower-defense-kit.zip` (5.4 MB, identical pack to the itch.io page) | CC0 1.0 — commercial use allowed, attribution not required | itch.io page states "License: Creative Commons Zero v1.0 Universal"; the pack is published by Kenney on kenney.nl |
| Animated Monster Pack | Quaternius | `https://opengameart.org/sites/default/files/Animated%20Monster%20Pack%20by%20%40Quaternius.zip` (1.4 MB, mirror of `quaternius.itch.io/lowpoly-animated-monsters`) | CC0 1.0 — commercial use allowed, attribution appreciated | `License.txt` inside the archive: "CC0 1.0 Universal (CC0 1.0) Public Domain Dedication" |

Both packs are redistributed under their own CC0 terms. `ArtSource/ThirdParty/` keeps the
original archives and their extracted contents so the import stays reproducible; the
editor tool `StoneSignal/Import third-party art pack` copies from there.

## What the project actually uses

Kenney Tower Defense Kit ships 160 models in OBJ/FBX/DAE/STL/glTF plus one 512×512 atlas
(`variation-a.png`, UV-referenced by every model). Only these 19 FBX are imported:

- Board: `tile` (grass), `tile-dirt` (road), `tile-spawn`, `tile-crystal` (core ground)
- Placeable: `wood-structure` (player-placed wall block)
- Landmarks: `spawn-round` (spawn gate), `tower-round-crystals` (signal core)
- Towers: `tower-round-base`, `tower-round-middle-a/b/c`, `tower-round-crystals`,
  `weapon-ballista`, `weapon-turret`, `weapon-cannon`
- Decoration: `detail-tree`, `detail-tree-large`, `detail-rocks`, `detail-crystal`, `detail-dirt`

The `tile-wide-*` road pieces were measured and rejected: they are 2 cells long, so they do
not fit the 1×1 grid. Road readability instead comes from swapping the whole cell between
`tile` and `tile-dirt` on every path change.

Quaternius Animated Monster Pack ships 4 monsters in FBX/OBJ/Blend, each with 5–6 embedded
skeletal clips and flat-colour materials (no textures). All four are imported; enemy roles
map onto them as Drifter = Slime, Skimmer = Bat, Bulwark = Skeleton, Splitter = Dragon,
Shard = a smaller Slime. Material colours are parsed from the shipped `.mtl` files at
import time rather than retyped, so the runtime colours match the authored ones.

## Other references supplied by the user, not imported

| Reference | Published license | Status |
|---|---|---|
| [sjolle Low Poly Nature Pack](https://sjolle.itch.io/low-poly-nature-pack) | CC0 stated | Not imported |
| [RGS_Dev Modular Low Poly Dungeon](https://rgsdev.itch.io/free-modular-low-poly-dungeon-pack-by-rgsdev) | CC0 stated | Not imported |

If either is imported later, retain its shipped license/readme and exact asset paths in this
inventory, and recheck mesh scale, axes, URP conversion and silhouettes against the current
catalog before replacing prefabs.
