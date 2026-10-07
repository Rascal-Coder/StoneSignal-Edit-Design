using UnityEngine;

namespace StoneSignal
{
    // Kenney board dressing. Decoration uses a deterministic arithmetic pattern and never
    // consumes gameplay random numbers, so a seeded run still reproduces exactly.
    public sealed class BoardEnvironment : MonoBehaviour
    {
        public void Initialize(GridManager grid, ArtCatalog art)
        {
            if (art == null) return;
            for (int y = -1; y <= grid.height; y++) for (int x = -1; x <= grid.width; x++)
            {
                if (x >= 0 && y >= 0 && x < grid.width && y < grid.height) continue;
                Vector3 position = grid.transform.position + new Vector3((x + .5f) * grid.cellSize, 0, (y + .5f) * grid.cellSize);
                ArtVisual.Create(art.tile, transform, position, grid.cellSize);
                int seed = (x * 7 + y * 13 + 1000) & 7;
                if (seed == 0) ArtVisual.Create(art.detailTree, transform, position, .85f);
                else if (seed == 1) ArtVisual.Create(art.detailRocks, transform, position, .9f);
                else if (seed == 2) ArtVisual.Create(art.detailCrystal, transform, position, .8f);
                else if (seed == 3) ArtVisual.Create(art.detailDirt, transform, position, .95f);
                else if (seed == 5 && art.detailTreeLarge != null) ArtVisual.Create(art.detailTreeLarge, transform, position + new Vector3(.12f, 0, .1f), .9f);
            }
            if (art.backdrop != null)
            {
                Vector3 middle = grid.ToWorld(new Vector2Int(grid.width / 2, grid.height / 2));
                PrimitiveVisual.Create("Surrounding meadow", PrimitiveType.Cube, transform,
                    new Vector3(middle.x, grid.transform.position.y - .12f, middle.z),
                    new Vector3(52, .1f, 46), art.backdrop);
            }
        }
    }
}
