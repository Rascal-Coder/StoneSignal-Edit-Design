using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class GridView : MonoBehaviour
    {
        private GridManager grid;
        private PathfindingManager pathfinding;
        private LineRenderer route;
        private Mesh directionMesh;
        private ArtCatalog art;
        private Transform groundRoot;
        private readonly Dictionary<Vector2Int, GameObject> ground = new Dictionary<Vector2Int, GameObject>();
        private readonly HashSet<Vector2Int> roaded = new HashSet<Vector2Int>();

        public void Initialize(GridManager map, PathfindingManager paths, VisualPalette palette)
        {
            grid = map; pathfinding = paths; art = palette.art;
            if (art != null)
            {
                groundRoot = new GameObject("Board ground").transform;
                groundRoot.SetParent(transform, false);
                for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
                {
                    var p = new Vector2Int(x, y);
                    if (p == grid.spawn) SetGround(p, art.tileSpawn);
                    else if (p == grid.goal) SetGround(p, art.tileGoal);
                    else SetGround(p, art.tile);
                }
                ArtVisual.Create(art.spawnPortal, transform, grid.ToWorld(grid.spawn));
                ArtVisual.Create(art.signalCore, transform, grid.ToWorld(grid.goal));
                var environment = new GameObject("Board decoration");
                environment.transform.SetParent(transform);
                environment.AddComponent<BoardEnvironment>().Initialize(grid, art);
            }
            else
            {
                for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
                {
                    var p = new Vector2Int(x, y);
                    Material mat = p == grid.spawn ? palette.spawn : p == grid.goal ? palette.goal : ((x + y) % 2 == 0 ? palette.tileA : palette.tileB);
                    PrimitiveVisual.Create("Tile " + x + "," + y, PrimitiveType.Cube, transform, grid.ToWorld(p) - Vector3.up * .13f, new Vector3(.95f, .2f, .95f) * grid.cellSize, mat);
                }
                PrimitiveVisual.Create("Signal core", PrimitiveType.Cylinder, transform, grid.ToWorld(grid.goal) + Vector3.up * .45f, new Vector3(.62f, .45f, .62f), palette.goal);
                PrimitiveVisual.Create("Spawn gate", PrimitiveType.Cylinder, transform, grid.ToWorld(grid.spawn) + Vector3.up * .35f, new Vector3(.7f, .35f, .7f), palette.spawn);
            }
            foreach (var endpoint in new[] { grid.spawn, grid.goal })
            {
                var label = new GameObject(endpoint == grid.spawn ? "SPAWN" : "CORE");
                label.transform.SetParent(transform);
                label.transform.position = grid.ToWorld(endpoint) + Vector3.up * 1.65f;
                label.transform.rotation = Quaternion.Euler(48, 34, 0);
                var text = label.AddComponent<TextMesh>();
                text.text = label.name; text.fontSize = 36; text.characterSize = .055f;
                text.anchor = TextAnchor.MiddleCenter;
                text.color = art != null ? new Color(.13f, .2f, .12f) : new Color(1, .94f, .77f);
            }
            route = new GameObject("Current route").AddComponent<LineRenderer>();
            route.transform.SetParent(transform);
            route.sharedMaterial = palette.path;
            route.startWidth = route.endWidth = art != null ? .07f : .11f;
            route.numCapVertices = 3;
            var directions = new GameObject("Route direction chevrons", typeof(MeshFilter), typeof(MeshRenderer));
            directions.transform.SetParent(transform, false);
            directionMesh = new Mesh { name = "Current route arrows" };
            directions.GetComponent<MeshFilter>().sharedMesh = directionMesh;
            directions.GetComponent<MeshRenderer>().sharedMaterial = palette.path;
            pathfinding.PathChanged += OnPathChanged;
            OnPathChanged();
        }

        // The route decides which cells render as dirt road, so the board always matches the live path.
        private void OnPathChanged()
        {
            if (art != null) RefreshRoad();
            DrawPath();
        }
        private void RefreshRoad()
        {
            var wanted = new HashSet<Vector2Int>();
            foreach (Vector2Int cell in pathfinding.CurrentPath)
            {
                if (cell == grid.spawn || cell == grid.goal) continue;
                if (grid.InBounds(cell)) wanted.Add(cell);
            }
            var stale = new List<Vector2Int>();
            foreach (Vector2Int cell in roaded) if (!wanted.Contains(cell)) stale.Add(cell);
            foreach (Vector2Int cell in stale) { roaded.Remove(cell); SetGround(cell, art.tile); }
            foreach (Vector2Int cell in wanted) if (!roaded.Contains(cell)) { roaded.Add(cell); SetGround(cell, art.tilePath); }
        }
        private void SetGround(Vector2Int cell, GameObject prefab)
        {
            if (prefab == null) return;
            if (ground.TryGetValue(cell, out var existing) && existing != null) PrimitiveVisual.DestroyObject(existing);
            ground[cell] = ArtVisual.Create(prefab, groundRoot, grid.ToWorld(cell), grid.cellSize);
        }
        private void DrawPath()
        {
            route.positionCount = pathfinding.CurrentPath.Count;
            for (int i = 0; i < pathfinding.CurrentPath.Count; i++) route.SetPosition(i, grid.ToWorld(pathfinding.CurrentPath[i]) + Vector3.up * (art != null ? art.tileTop + .02f : .03f));
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float lift = art != null ? .25f : .05f;
            for (int i = 0; i < pathfinding.CurrentPath.Count - 1; i += 2)
            {
                Vector3 a = grid.ToWorld(pathfinding.CurrentPath[i]), b = grid.ToWorld(pathfinding.CurrentPath[i + 1]);
                Vector3 forward = (b - a).normalized, side = Vector3.Cross(Vector3.up, forward);
                Vector3 center = Vector3.Lerp(a, b, .55f) + Vector3.up * lift;
                int index = vertices.Count;
                vertices.Add(transform.InverseTransformPoint(center + forward * .2f));
                vertices.Add(transform.InverseTransformPoint(center - forward * .13f - side * .15f));
                vertices.Add(transform.InverseTransformPoint(center - forward * .13f + side * .15f));
                triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 1);
            }
            directionMesh.Clear(); directionMesh.SetVertices(vertices); directionMesh.SetTriangles(triangles, 0); directionMesh.RecalculateNormals();
        }
        private void OnDestroy()
        {
            if (pathfinding != null) pathfinding.PathChanged -= OnPathChanged;
            if (directionMesh != null) PrimitiveVisual.DestroyObject(directionMesh);
        }
    }
}
