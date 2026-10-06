using UnityEngine;

namespace StoneSignal
{
    // Decoration uses a deterministic arithmetic pattern, never gameplay Random.
    public sealed class RuinEnvironment : MonoBehaviour
    {
        public void Initialize(GridManager grid, ArtCatalog art)
        {
            for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
            {
                if (x != 0 && y != 0 && x != grid.width - 1 && y != grid.height - 1) continue;
                var position = grid.ToWorld(new Vector2Int(x, y));
                var cliff = ArtVisual.Create(art.cliff, transform, position, grid.cellSize);
                if (cliff != null) cliff.transform.rotation = Quaternion.Euler(0, ((x * 7 + y * 3) % 4) * 90, 0);
                if ((x * 3 + y) % 4 == 0 && new Vector2Int(x, y) != grid.spawn && new Vector2Int(x, y) != grid.goal)
                {
                    var offset = new Vector3(x == 0 ? -.4f : x == grid.width - 1 ? .4f : 0, 0, y == 0 ? -.4f : y == grid.height - 1 ? .4f : 0);
                    ArtVisual.Create(art.foliage, transform, position + offset, .9f);
                }
            }
            foreach (var corner in new[] { new Vector2Int(-1, -1), new Vector2Int(-1, grid.height), new Vector2Int(grid.width, -1), new Vector2Int(grid.width, grid.height) })
            {
                var position = grid.ToWorld(corner);
                ArtVisual.Create(art.cliff, transform, position);
                ArtVisual.Create(art.ruinPillar, transform, position, .9f);
                ArtVisual.Create(art.foliage, transform, position + new Vector3(.3f, 0, -.3f));
            }
            foreach (int x in new[] { 2, 6, 10, 14 })
            {
                var position = grid.ToWorld(new Vector2Int(x, grid.height));
                ArtVisual.Create(art.cliff, transform, position);
                ArtVisual.Create(art.brazier, transform, position);
                ArtVisual.Create(art.foliage, transform, position + Vector3.right * .3f);
            }
            if (art.backdrop != null)
                PrimitiveVisual.Create("Distant blue mist", PrimitiveType.Cube, transform, grid.ToWorld(new Vector2Int(grid.width / 2, grid.height / 2)) + Vector3.down * 3.7f, new Vector3(45, .1f, 40), art.backdrop);
        }
    }
}
