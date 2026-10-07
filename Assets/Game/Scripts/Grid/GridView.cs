using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class GridView : MonoBehaviour
    {
        readonly System.Collections.Generic.Dictionary<Vector2Int, StoneSignal.VFX.SpawnPortal> portals = new System.Collections.Generic.Dictionary<Vector2Int, StoneSignal.VFX.SpawnPortal>();
        public StoneSignal.VFX.SpawnPortal PortalAt(Vector2Int cell) => portals.TryGetValue(cell, out var p) ? p : null;
        public void SetPortalsActive(bool on) { foreach (var p in portals.Values) if (p != null) p.SetActive(on); }
        public StoneSignal.VFX.CoreDamageFx CoreFx { get; private set; }

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
                foreach (var s in grid.Spawns)
                {
                    if (ArtSteps.On(2) && art.spawnPortalFx != null)
                    {
                        var go = ArtVisual.Create(art.spawnPortalFx, transform, PortalPoint(s)); portalGos[s] = go;
                        var sp = go.GetComponentInChildren<StoneSignal.VFX.SpawnPortal>(); if (sp != null) { portals[s] = sp; sp.SetActive(false); }
                        if (!art.portalShowRunestones) { Transform stones = null; foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Runestones") { stones = t; break; } if (stones != null) foreach (var r in stones.GetComponentsInChildren<Renderer>(true)) r.enabled = false; else Debug.LogWarning("SPAWN PORTAL: no 'Runestones' child to hide"); }
                    }
                    else
                    {
                        if (art.spawnPortalFx == null) Debug.LogError("SPAWN PORTAL: ArtCatalog.spawnPortalFx (PF_VFX_SpawnPortal) is MISSING - placeholder portal used. Run BatchWire.");
                        portalGos[s] = ArtVisual.Create(art.spawnPortal, transform, PortalPoint(s));
                    }
                }
                var coreGo = ArtVisual.Create(art.signalCore, transform, grid.CoreCenter);
                if (ArtSteps.On(4) && coreGo != null && art.coreEnclosureIntact != null)
                {
                    // v16.2 core enclosure (no generator): one MeshFilter swapped by CoreDamageFx at 0.70 / 0.40 / 0.15
                    var enc = new GameObject("Core enclosure", typeof(MeshFilter), typeof(MeshRenderer)); enc.transform.SetParent(coreGo.transform, false);
                    enc.GetComponent<MeshFilter>().sharedMesh = art.coreEnclosureIntact;
                    var mr = enc.GetComponent<MeshRenderer>(); mr.sharedMaterial = art.coreEnclosureMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var cores = new System.Collections.Generic.List<Renderer>(); foreach (var r in coreGo.GetComponentsInChildren<Renderer>()) if (r != mr) cores.Add(r);
                    CoreFx = coreGo.AddComponent<StoneSignal.VFX.CoreDamageFx>();
                    CoreFx.enclosure = enc.GetComponent<MeshFilter>(); CoreFx.intactMesh = art.coreEnclosureIntact; CoreFx.crackedMesh = art.coreEnclosureCracked; CoreFx.brokenMesh = art.coreEnclosureBroken;
                    CoreFx.coreRenderers = cores.ToArray(); // smoke/sparks: none delivered yet (null-safe)
                }
                if (art.boardCliff != null || (grid.layout != null && grid.layout.levelDressing != null)) BuildIsland();
                else
                {
                    var environment = new GameObject("Board decoration");
                    environment.transform.SetParent(transform);
                    environment.AddComponent<BoardEnvironment>().Initialize(grid, art);
                }
                /* after the island probe (fix: this loop used to sit between the if and its else, capturing the else) */
                foreach (var kv in portalGos) if (kv.Value != null) kv.Value.transform.position = PortalPoint(kv.Key);
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
        /// Line point order so M_Path_Flow animates entry -> core (enemy travel). path is entry -> core; the flow material
        /// scrolls toward the line start, so the points run core -> entry when scrollsTowardStart is set.
        public static readonly int FlowSpeedId = Shader.PropertyToID("_Speed");
        /// Scroll speed so the pattern moves toward the core: lines whose points run core -> entry need the pattern to move toward the
        /// line start (uv.x decreasing) = negative _Speed.
        public static float FlowScrollSpeed(float materialSpeed, bool linesRunCoreToEntry) => linesRunCoreToEntry ? -Mathf.Abs(materialSpeed) : Mathf.Abs(materialSpeed);
        /// Position along the line (uv.x, 0..1 per dash period) of one chevron at time t, per the shader phase uv.x*k - t*speed = n.
        public static float ChevronU(float t, float speed, float dashes, int n = 0) => (n + t * speed) / (dashes * .1f);
        public static List<Vector2Int> FlowOrder(IReadOnlyList<Vector2Int> entryToCore, bool scrollsTowardStart)
        {
            var l = new List<Vector2Int>(entryToCore); if (scrollsTowardStart) l.Reverse(); return l;
        }
        [Tooltip("Route flow line ends this far from the portal centre (rune ring edge) so the chevrons do not cross the ring.")] public float portalFlowStop = 1.2f;
        private Vector3 PortalPoint(Vector2Int s) => grid.TryPortalPoint(s, out var p) ? p : grid.ToWorld(s);
        private void DrawFlow()
        {
            if (flowRoot != null) PrimitiveVisual.DestroyObject(flowRoot.gameObject);
            flowRoot = new GameObject("Route flow").transform; flowRoot.SetParent(transform, false);
            // PF_Path_FlowSegment is a world-space LineRenderer (authored points (-2,.82,0)->(2,.82,0)): placing copies by transform left
            // every copy on the same short line next to the core. One line per route through its cells instead (art material/width unchanged).
            foreach (var path in pathfinding.CurrentPaths)
            {
                if (path.Count < 2) continue;
                var seg = ArtVisual.Create(art.pathFlowSegment, flowRoot, grid.ToWorld(path[0]), 1);
                var lr = seg.GetComponentInChildren<LineRenderer>(true);
                if (lr == null) continue;
                var pts = FlowOrder(path, art.flowScrollsTowardStart); lr.useWorldSpace = true; lr.positionCount = pts.Count;
                var wp = new List<Vector3>(); foreach (var c in pts) wp.Add(grid.ToWorld(c) + Vector3.up * art.pathFlowY);
                if (grid.TryPortalPoint(path[0], out var portal)) { var pv = new Vector3(portal.x, 0, portal.z); var e0 = grid.ToWorld(path[0]); e0.y = 0;
                    pv += (e0 - pv).normalized * portalFlowStop; pv.y = (pts[0] == path[0] ? wp[0] : wp[wp.Count - 1]).y; /* stop at the rune ring edge, not across it */ if (pts[0] == path[0]) wp.Insert(0, pv); else wp.Add(pv); }
                lr.positionCount = wp.Count; for (int i = 0; i < wp.Count; i++) lr.SetPosition(i, wp[i]);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                // SS_PlaceFX chevrons: phase = uv.x*k - _Time*_Speed, so the pattern travels toward +uv.x (line end) for _Speed > 0.
                // Lines run core -> entry (arrow orientation), so scroll with the opposite sign: per-renderer block, art material untouched.
                var mat = lr.sharedMaterial;
                if (mat != null && mat.HasProperty(FlowSpeedId))
                {
                    var mpb = new MaterialPropertyBlock(); lr.GetPropertyBlock(mpb);
                    mpb.SetFloat(FlowSpeedId, FlowScrollSpeed(mat.GetFloat(FlowSpeedId), art.flowScrollsTowardStart)); lr.SetPropertyBlock(mpb);
                }
            }
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
        private readonly Dictionary<Vector2Int, GameObject> portalGos = new Dictionary<Vector2Int, GameObject>();
        /// Spawn portals sit on the walkable top of the shore island past each entry bridge. The dressing is merged per quadrant, so the
        /// island is measured: temporary MeshColliders on the (readable) dressing meshes, a downward ray grid around the layout estimate,
        /// flood-fill of the flat top connected to the estimate, centroid xz + median top y. Fallback: layout estimate (logged).
        private void ProbeIslands(Transform dressing)
        {
            var cols = new List<MeshCollider>(); int unreadable = 0;
            foreach (var mf in dressing.GetComponentsInChildren<MeshFilter>())
            {
                var n = mf.gameObject.name.ToLowerInvariant();
                if (mf.sharedMesh == null || n.Contains("water") || n.Contains("fx") || n.Contains("snow")) continue;
                if (!mf.sharedMesh.isReadable) { unreadable++; continue; }
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; cols.Add(mc);
            }
            Physics.SyncTransforms();
            const float step = .2f; const int R = 16; // +-3.2 m sample grid
            foreach (var s in grid.Spawns)
            {
                var o = GridManager.OutwardOf(s, grid.width, grid.height); if (o == Vector3.zero || !grid.TryPortalPoint(s, out var est)) continue;
                var entry = grid.ToWorld(s); var hy = new float?[2 * R + 1, 2 * R + 1];
                for (int i = -R; i <= R; i++) for (int j = -R; j <= R; j++)
                {
                    var p = est + new Vector3(i * step, 0, j * step);
                    if (Vector3.Dot(p - entry, o) < 3.6f * grid.cellSize) continue; // past the bridge
                    var ray = new Ray(new Vector3(p.x, 30, p.z), Vector3.down); float best = float.NegativeInfinity; Vector3 nrm = Vector3.up;
                    foreach (var c in cols) if (c.Raycast(ray, out var hit, 60) && hit.point.y > best) { best = hit.point.y; nrm = hit.normal; }
                    if (best > .15f && nrm.y > .85f) hy[i + R, j + R] = best; // above water, flat
                }
                // flood fill from the sample nearest the estimate
                int si = -1, sj = -1; float sd = float.MaxValue;
                for (int i = 0; i <= 2 * R; i++) for (int j = 0; j <= 2 * R; j++) if (hy[i, j] != null) { float d = (i - R) * (i - R) + (j - R) * (j - R); if (d < sd) { sd = d; si = i; sj = j; } }
                if (si < 0) { Debug.Log("ISLAND PROBE " + s + ": no island top hit (colliders=" + cols.Count + ", unreadable=" + unreadable + "), using layout estimate " + est.ToString("F2")); continue; }
                var seen = new bool[2 * R + 1, 2 * R + 1]; var q = new Queue<(int, int)>(); q.Enqueue((si, sj)); seen[si, sj] = true;
                var ys = new List<float>(); Vector3 sum = Vector3.zero;
                while (q.Count > 0)
                {
                    var (i, j) = q.Dequeue(); float y = hy[i, j].Value; ys.Add(y); sum += new Vector3(est.x + (i - R) * step, 0, est.z + (j - R) * step);
                    foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int a = i + di, b = j + dj; if (a < 0 || b < 0 || a > 2 * R || b > 2 * R || seen[a, b] || hy[a, b] == null || Mathf.Abs(hy[a, b].Value - y) > .2f) continue;
                        seen[a, b] = true; q.Enqueue((a, b));
                    }
                }
                ys.Sort(); var c0 = sum / ys.Count; var top = new Vector3(c0.x, ys[ys.Count / 2], c0.z);
                // survey: what dressing geometry lies inside the portal decal radius (1.3 m) - object / material / height above the island top
                var survey = new Dictionary<string, (int n, float lo, float hi)>(); float lowMax = 0;
                for (float dx = -1.3f; dx <= 1.3f; dx += .1f) for (float dz = -1.3f; dz <= 1.3f; dz += .1f)
                {
                    if (dx * dx + dz * dz > 1.69f) continue;
                    var ray = new Ray(new Vector3(top.x + dx, 30, top.z + dz), Vector3.down);
                    foreach (var c in cols)
                    {
                        if (!c.Raycast(ray, out var hit, 60)) continue;
                        var mesh = c.sharedMesh; var mats = c.GetComponent<MeshRenderer>().sharedMaterials; string mat = "?";
                        int ti = hit.triangleIndex * 3; for (int sm = 0; sm < mesh.subMeshCount; sm++) { var d = mesh.GetSubMesh(sm); if (ti >= d.indexStart && ti < d.indexStart + d.indexCount) { mat = sm < mats.Length && mats[sm] ? mats[sm].name : "sub" + sm; break; } }
                        string key = c.name + " / " + mat; float h = hit.point.y - top.y; if (h < .3f) lowMax = Mathf.Max(lowMax, h); // ground mounds, not trees
                        survey[key] = survey.TryGetValue(key, out var v) ? (v.n + 1, Mathf.Min(v.lo, h), Mathf.Max(v.hi, h)) : (1, h, h);
                    }
                }
                var sb = new System.Text.StringBuilder("PORTAL AREA SURVEY " + s + " (r 1.3 m, heights rel. island top " + top.y.ToString("F2") + "; portal decal at +0.02):");
                foreach (var kv in survey) sb.Append("\n  " + kv.Key + ": hits=" + kv.Value.n + " h=" + kv.Value.lo.ToString("F3") + ".." + kv.Value.hi.ToString("F3"));
                // The island top carries flattened grass/leaf mounds (SM_Env_Island_Cliff_4x4_01, up to ~+0.14) that hide a flat decal laid at the
                // median height: lift the portal (decal + enemy start) to the highest ground mound inside the decal disc.
                top.y += lowMax; grid.SetPortalPoint(s, top);
                sb.Append("\n  -> portal lifted by " + lowMax.ToString("F3") + " to y " + top.y.ToString("F2"));
                Debug.Log(sb.ToString());
                Debug.Log("ISLAND PROBE " + s + ": estimate " + est.ToString("F2") + " -> island top centre " + top.ToString("F2") + " (" + ys.Count + " samples, y " + ys[0].ToString("F2") + ".." + ys[ys.Count - 1].ToString("F2") + ")");
            }
            foreach (var c in cols) PrimitiveVisual.DestroyObject(c);
        }
        private void BuildIsland()
        {
            var layout = grid.layout;
            bool dressed = layout != null && layout.levelDressing != null;
            if (dressed)
            {
                // Authored around the world-space board centre at water level 0 (cliff top 0.55 = grid plane), independent of the grid height.
                var c = grid.BoardCenter; var dressing = ArtVisual.Create(layout.levelDressing, transform, new Vector3(c.x, 0, c.z) + layout.levelDressingOffset);
                dressing.transform.localRotation = Quaternion.identity; // prefab authored in game space
                ProbeIslands(dressing.transform);
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
