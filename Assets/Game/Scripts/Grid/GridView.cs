using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class GridView : MonoBehaviour
    {
        private GridManager grid;
        private PathfindingManager pathfinding;
        private readonly List<LineRenderer> routes = new List<LineRenderer>();
        private Material routeMaterial;
        private float routeWidth;
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
                    var state = grid.Get(p);
                    if (state == CellState.Spawn) SetGround(p, art.tileSpawn);
                    else if (state == CellState.Goal) SetGround(p, art.tileGoal);
                    else SetGround(p, art.tile);
                }
                MergeGround();
                foreach (var s in grid.Spawns) ArtVisual.Create(art.spawnPortal, transform, grid.ToWorld(s));
                ArtVisual.Create(art.signalCore, transform, grid.CoreCenter);
                if (art.boardCliff != null || (grid.layout != null && grid.layout.levelDressing != null)) BuildIsland();
                else
                {
                    var environment = new GameObject("Board decoration");
                    environment.transform.SetParent(transform);
                    environment.AddComponent<BoardEnvironment>().Initialize(grid, art);
                }
            }
            else
            {
                for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
                {
                    var p = new Vector2Int(x, y);
                    var state = grid.Get(p);
                    Material mat = state == CellState.Spawn ? palette.spawn : state == CellState.Goal ? palette.goal : ((x + y) % 2 == 0 ? palette.tileA : palette.tileB);
                    PrimitiveVisual.Create("Tile " + x + "," + y, PrimitiveType.Cube, transform, grid.ToWorld(p) - Vector3.up * .13f, new Vector3(.95f, .2f, .95f) * grid.cellSize, mat);
                }
                PrimitiveVisual.Create("Signal core", PrimitiveType.Cylinder, transform, grid.CoreCenter + Vector3.up * .45f, new Vector3(.62f, .45f, .62f), palette.goal);
                foreach (var s in grid.Spawns) PrimitiveVisual.Create("Spawn gate", PrimitiveType.Cylinder, transform, grid.ToWorld(s) + Vector3.up * .35f, new Vector3(.7f, .35f, .7f), palette.spawn);
            }
            var labels = new List<(string, Vector3)>();
            if (art == null) {
            foreach (var s in grid.Spawns) labels.Add(("SPAWN", grid.ToWorld(s)));
            labels.Add(("CORE", grid.CoreCenter));
            foreach (var (name, at) in labels)
            {
                var label = new GameObject(name);
                label.transform.SetParent(transform);
                label.transform.position = at + Vector3.up * 1.65f;
                label.transform.rotation = Quaternion.Euler(48, 34, 0);
                var text = label.AddComponent<TextMesh>();
                text.text = label.name; text.fontSize = 36; text.characterSize = .055f;
                text.anchor = TextAnchor.MiddleCenter;
                text.color = art != null ? new Color(.13f, .2f, .12f) : new Color(1, .94f, .77f);
            }
            } // stylized board: no debug SPAWN/CORE labels
            routeMaterial = palette.path; routeWidth = art != null ? .07f : .11f;
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
            if (art != null && art.pathFlowSegment != null) { DrawFlow(); return; } // v8: flow replaces dirt road + debug lines
            if (art != null) RefreshRoad();
            DrawPath();
        }
        private void RefreshRoad()
        {
            var wanted = new HashSet<Vector2Int>();
            foreach (var path in pathfinding.CurrentPaths)
                foreach (Vector2Int cell in path)
                {
                    var state = grid.Get(cell);
                    if (state == CellState.Spawn || state == CellState.Goal) continue;
                    if (grid.InBounds(cell)) wanted.Add(cell);
                }
            var stale = new List<Vector2Int>();
            foreach (Vector2Int cell in roaded) if (!wanted.Contains(cell)) stale.Add(cell);
            foreach (Vector2Int cell in stale) { roaded.Remove(cell); SetGround(cell, art.tile); }
            foreach (Vector2Int cell in wanted) if (!roaded.Contains(cell)) { roaded.Add(cell); SetGround(cell, art.tilePath); }
            if (stale.Count > 0 || wanted.Count > 0) MergeGround();
        }
        private void SetGround(Vector2Int cell, GameObject prefab)
        {
            if (prefab == null) return;
            if (ground.TryGetValue(cell, out var existing) && existing != null) PrimitiveVisual.DestroyObject(existing);
            long h = BoardArt.GameCellHash(cell, grid.width, grid.height);
            // v8: stone tiles pick a variant by cell hash; dirt (legacy road) keeps its prefab.
            if (prefab == art.tile && art.tileVariants != null && art.tileVariants.Length > 0)
            {
                var v = art.tileVariants[BoardArt.Variant(h, art.tileVariants.Length)]; if (v != null) prefab = v;
            }
            var go = ArtVisual.Create(prefab, groundRoot, grid.ToWorld(cell), grid.cellSize);
            if (art.tileUndulation && art.tileVariants != null && art.tileVariants.Length > 0)
            {
                go.transform.rotation = Quaternion.Euler(0, BoardArt.Yaw(h), 0);
                if (!grid.IsCore(cell) && grid.Get(cell) != CellState.Blocked && grid.Get(cell) != CellState.TowerSlot)
                    go.transform.position += Vector3.up * BoardArt.HeightOffset(h); // visual only; gameplay heights unchanged
            }
            ground[cell] = go;
        }
        // Static tiles: one merged mesh per material, tiles never cast shadows (draw-call budget).
        private void MergeGround() { if (groundRoot != null) MeshMerge.Rebuild(groundRoot, "Merged ground", UnityEngine.Rendering.ShadowCastingMode.Off); }
        // Route flow: the art flow segment (PF_Path_FlowSegment, M_Path_Flow untouched) on every edge of every entry->core route,
        // drawn through MeshMerge/InstancedBatch (GPU instanced: one draw call per material). Shared route tails are drawn once.
        private Transform flowRoot;
        private void DrawFlow()
        {
            if (flowRoot != null) PrimitiveVisual.DestroyObject(flowRoot.gameObject);
            flowRoot = new GameObject("Route flow").transform; flowRoot.SetParent(transform, false);
            var seen = new HashSet<(Vector2Int, Vector2Int)>();
            foreach (var path in pathfinding.CurrentPaths)
                for (int i = 0; i < path.Count - 1; i++)
                {
                    if (!seen.Add((path[i], path[i + 1]))) continue;
                    Vector3 a = grid.ToWorld(path[i]), b = grid.ToWorld(path[i + 1]);
                    var seg = ArtVisual.Create(art.pathFlowSegment, flowRoot, (a + b) * .5f + Vector3.up * art.pathFlowY, grid.cellSize);
                    seg.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
                    foreach (var r in seg.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                }
            MeshMerge.Rebuild(flowRoot, "Route flow", UnityEngine.Rendering.ShadowCastingMode.Off);
        }
        private void DrawPath()
        {
            var paths = pathfinding.CurrentPaths;
            while (routes.Count < paths.Count)
            {
                var line = new GameObject("Current route " + routes.Count).AddComponent<LineRenderer>();
                line.transform.SetParent(transform);
                line.sharedMaterial = routeMaterial; line.startWidth = line.endWidth = routeWidth; line.numCapVertices = 3;
                routes.Add(line);
            }
            float height = art != null ? art.tileTop + .02f : .03f;
            for (int r = 0; r < routes.Count; r++)
            {
                var path = r < paths.Count ? paths[r] : null;
                routes[r].positionCount = path != null ? path.Count : 0;
                if (path != null) for (int i = 0; i < path.Count; i++) routes[r].SetPosition(i, grid.ToWorld(path[i]) + Vector3.up * height);
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float lift = art != null ? art.tileTop + .05f : .05f;
            foreach (var path in paths)
            for (int i = 0; i < path.Count - 1; i += 2)
            {
                Vector3 a = grid.ToWorld(path[i]), b = grid.ToWorld(path[i + 1]);
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
        // Stylized board: cliff island under the grid plus plank bridges leading out of each edge spawn.
        private void BuildIsland()
        {
            var layout = grid.layout;
            bool dressed = layout != null && layout.levelDressing != null;
            if (dressed)
            {
                // Authored around the world-space board centre at water level 0 (cliff top 0.55 = grid plane), independent of the grid height.
                var c = grid.BoardCenter; var dressing = ArtVisual.Create(layout.levelDressing, transform, new Vector3(c.x, 0, c.z) + layout.levelDressingOffset);
                dressing.transform.localRotation = Quaternion.identity; // prefab authored in game space
            }
            if (!dressed || !layout.levelDressingReplacesCliff)
            {
                var cliff = ArtVisual.Create(art.boardCliff, transform, grid.BoardCenter + Vector3.up * art.boardCliffOffsetY);
                var native = art.boardCliffSize;
                if (native.x > 0 && native.y > 0)
                    cliff.transform.localScale = new Vector3(grid.width * grid.cellSize / native.x, 1, grid.height * grid.cellSize / native.y);
            }
            if (!dressed && layout != null && layout.waterMaterial != null)
            {
                float s = layout.waterSize / 10f;
                var water = PrimitiveVisual.Create("Water", PrimitiveType.Plane, transform, grid.BoardCenter + Vector3.up * layout.waterY, new Vector3(s, 1, s), layout.waterMaterial);
                water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (layout != null && layout.surroundings != null && layout.surroundings.Length > 0)
            {
                // Authored bridges/docks (segment counts W4/E4/N3) come straight from level_layout.json.
                var root = new GameObject("Board surroundings").transform; root.SetParent(transform, false);
                foreach (var it in layout.surroundings)
                {
                    if (it.prefab == null) continue;
                    var go = ArtVisual.Create(it.prefab, root, grid.BoardCenter + it.position);
                    go.transform.rotation = Quaternion.Euler(it.euler);
                    go.transform.localScale = it.scale == Vector3.zero ? Vector3.one : it.scale;
                    if (it.material != null)
                        foreach (var r in go.GetComponentsInChildren<Renderer>())
                        {
                            var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = it.material; r.sharedMaterials = mats;
                        }
                }
                return;
            }
            if (art.entryBridge == null) return;
            foreach (var s in grid.Spawns)
            {
                Vector2Int n = grid.EdgeNormal(s);
                if (n == Vector2Int.zero) continue;
                var outward = new Vector3(n.x, 0, n.y);
                float yaw = Quaternion.LookRotation(outward).eulerAngles.y + 90; // plank long axis matches level_layout.json
                for (int k = 0; k < art.entryBridgePlanks; k++)
                {
                    var plank = ArtVisual.Create(art.entryBridge, transform, grid.ToWorld(s) + outward * (.68f + .82f * k) * grid.cellSize + Vector3.up * art.entryBridgeOffsetY);
                    plank.transform.rotation = Quaternion.Euler(0, yaw, 0);
                }
            }
        }
        private void OnDestroy()
        {
            if (pathfinding != null) pathfinding.PathChanged -= OnPathChanged;
            if (directionMesh != null) PrimitiveVisual.DestroyObject(directionMesh);
        }
    }
}
