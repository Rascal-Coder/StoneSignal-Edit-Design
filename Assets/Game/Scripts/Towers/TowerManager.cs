using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StoneSignal
{
    public sealed class TowerManager : MonoBehaviour
    {
        private GridManager grid;
        private PlacementValidator validator;
        private EnemyManager enemies;
        private VisualPalette palette;
        private RunModifiers modifiers;
        private Func<bool> canBuild, canAttack;
        private Func<int, bool> spend;
        private Func<int> balance;
        private BlockPlacementManager blocks;
        private GameObject ghost;
        private LineRenderer rangeView;
        private Transform missiles;
        private readonly List<Tower> towers = new List<Tower>();
        public IReadOnlyList<Tower> Towers => towers;
        public TowerData[] Data { get; private set; }
        public int SelectedIndex { get; private set; } = -1;
        public string Status { get; private set; }
        public event Action Changed;
        public event Action<string> Notice;
        public void Initialize(GridManager map, PlacementValidator placement, EnemyManager registry, VisualPalette colors, RunModifiers upgrades, TowerData[] data, BlockPlacementManager blockTool, Func<bool> buildAllowed, Func<bool> attackAllowed, Func<int,bool> pay, Func<int> gold)
        {
            grid = map; validator = placement; enemies = registry; palette = colors; modifiers = upgrades; Data = data; blocks = blockTool;
            canBuild = buildAllowed; canAttack = attackAllowed; spend = pay; balance = gold;
            missiles = new GameObject("Projectiles").transform; missiles.SetParent(transform);
            ghost = PrimitiveVisual.Create("Tower ghost", PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(.85f,.45f,.85f), palette.valid);
            rangeView = new GameObject("Range preview").AddComponent<LineRenderer>(); rangeView.transform.SetParent(transform);
            rangeView.sharedMaterial = palette.path; rangeView.startWidth = rangeView.endWidth = .035f; rangeView.loop = true; rangeView.positionCount = 48;
            HideGhost();
            grid.Changed += () => hiKey = "?";
            blocks.RunesChanged += RecomputeRunes;
        }
        // ---- v8 presentation: art ghost (PF_UI_PlaceGhost_Tower), range ring, pooled slot highlights. ----
        private StoneSignal.VFX.PlacementGhost artGhost; private StoneSignal.VFX.RangeRing artRing; private int artGhostIndex = -1;
        private ArtCatalog Art => palette != null ? palette.art : null;
        private bool ShowArtGhost(TowerData data, Vector3 centre, int rotation, bool ok, float range, IReadOnlyList<Vector2Int> foot = null, bool[] footOk = null)
        {
            if (Art == null || Art.placeGhostTower == null || data.visualPrefab == null) return false;
            if (artGhost == null)
            {
                var g = ArtVisual.Create(Art.placeGhostTower, transform, centre);
                artGhost = g.GetComponent<StoneSignal.VFX.PlacementGhost>(); artRing = g.GetComponentInChildren<StoneSignal.VFX.RangeRing>(true);
                if (artGhost == null) { Destroy(g); return false; }
            }
            if (artGhostIndex != SelectedIndex) { artGhost.SetModel(data.visualPrefab); artGhostIndex = SelectedIndex; }
            artGhost.gameObject.SetActive(true);
            artGhost.transform.position = centre + Vector3.up * Art.blockTop;
            artGhost.transform.rotation = Quaternion.Euler(0, 90 * rotation, 0);
            artGhost.SetValid(ok);
            if (foot != null)
            {
                // v15 footprint ghost: one base cell per footprint cell, each valid (wall top) / invalid (#E5484D)
                var raw = new Vector2Int(Mathf.Max(1, data.footprint.x), Mathf.Max(1, data.footprint.y));
                if (artGhost.FootprintSize != raw || artGhost.FootprintRotation != rotation) artGhost.SetFootprint(raw, rotation);
                for (int i = 0; i < foot.Count; i++) artGhost.SetCellValid(i, ok || footOk[i]);
            }
            if (artRing != null) { artRing.SetRadius(range); artRing.transform.position = centre + Vector3.up * (Art.tileTop + .02f); }
            return true;
        }
        // Wall highlight (art WallHighlight: _HiAmount/_HiColor on the real wall renderers). While a tower is selected every free
        // wall top glows valid; the cells under the ghost show per-cell valid / invalid (#E5484D family). Applied only on change.
        private string hiKey = "";
        private void UpdateHighlight(bool show, IReadOnlyList<Vector2Int> foot, bool[] footOk)
        {
            var cells = new List<Vector2Int>(); var ok = new List<bool>();
            if (show)
            {
                // footprint cells only: lighting every free wall top made each one a non-instanced renderer (+1 draw call each)
                if (foot != null) for (int i = 0; i < foot.Count; i++) if (blocks.IsWall(foot[i])) { cells.Add(foot[i]); ok.Add(footOk[i]); }
            }
            var sb = new System.Text.StringBuilder(); for (int i = 0; i < cells.Count; i++) sb.Append(cells[i].x).Append(',').Append(cells[i].y).Append(ok[i] ? '+' : '-');
            string key = sb.ToString(); if (key == hiKey) return; hiKey = key;
            blocks.HighlightWalls(cells.ToArray(), ok.ToArray());
        }
        private static bool Contains(IReadOnlyList<Vector2Int> list, Vector2Int c) { foreach (var x in list) if (x == c) return true; return false; }
        private bool[] CellValidity(IReadOnlyList<Vector2Int> foot, bool allOk)
        {
            var r = new bool[foot.Count];
            for (int i = 0; i < foot.Count; i++) r[i] = allOk || (grid.InBounds(foot[i]) && grid.Get(foot[i]) == CellState.Blocked);
            return r;
        }
        private void RefreshSlots(bool show) { if (!show) UpdateHighlight(false, null, null); }

        // ---- runes: stats per tower from the runes under its footprint + resonance; buff icons / aura presentation ----
        public RuneConfig RuneRulesConfig { get; set; }
        private readonly Dictionary<Tower, GameObject> buffIcons = new Dictionary<Tower, GameObject>(), auras = new Dictionary<Tower, GameObject>();
        public void RecomputeRunes()
        {
            var cfg = RuneRulesConfig; if (cfg == null || blocks == null) return;
            foreach (var t in towers) { t.RuneList.Clear(); foreach (var c in t.Cells) { int r = blocks.RuneAt(c); if (r != RuneRules.NoRune) t.RuneList.Add(r); } }
            foreach (var t in towers)
            {
                bool res = false;
                foreach (var src in towers)
                    if (src != t && src.RuneList.Contains((int)StoneSignal.VFX.RuneId.Resonance) && RuneRules.InResonance(cfg, src.Origin, src.Size, t.Origin, t.Size)) { res = true; break; }
                t.SetRunes(RuneRules.Compute(cfg, t.RuneList, res), grid.cellSize);
                Present(t);
            }
        }
        private void Present(Tower t)
        {
            if (Art == null) return;
            if (Art.towerBuffIcons != null)
            {
                buffIcons.TryGetValue(t, out var go);
                if (t.RuneList.Count > 0 && go == null) { go = ArtVisual.Create(Art.towerBuffIcons, t.transform, t.transform.position + Vector3.up * Art.blockTop); buffIcons[t] = go; }
                var icons = go != null ? go.GetComponent<StoneSignal.VFX.TowerBuffIcons>() : null;
                if (icons != null)
                {
                    var ids = new List<StoneSignal.VFX.RuneId>(); var at = new List<Vector3>();
                    foreach (var c in t.Cells) { int r = blocks.RuneAt(c); if (r != RuneRules.NoRune) { ids.Add((StoneSignal.VFX.RuneId)r); at.Add(grid.ToWorld(c) + Vector3.up * Art.blockTop); } }
                    icons.Set(ids, at);
                }
            }
            bool isRes = t.RuneList.Contains((int)StoneSignal.VFX.RuneId.Resonance);
            auras.TryGetValue(t, out var aura);
            if (isRes && aura == null && Art.resonanceAura != null) auras[t] = ArtVisual.Create(Art.resonanceAura, t.transform, t.transform.position + Vector3.up * (Art.tileTop + .03f));
            else if (!isRes && aura != null) { Destroy(aura); auras.Remove(t); }
        }

        public int Cost(TowerData data) => Mathf.Max(1,Mathf.RoundToInt(data.cost*modifiers.TowerCost));
        public void Select(int index)
        {
            if (index < -1 || index >= Data.Length) return;
            SelectedIndex = index; blocks.SetToolActive(index < 0); HideGhost(); Changed?.Invoke();
        }
        public int Rotation { get; private set; }
        public static Vector2Int SizeOf(TowerData data, int rotation)
        {
            var s = new Vector2Int(Mathf.Max(1, data.footprint.x), Mathf.Max(1, data.footprint.y));
            return data.footprintRotates ? GridManager.RotatedSize(s, rotation) : s;
        }
        public void RotateFootprint() { Rotation = (Rotation + 1) & 3; Changed?.Invoke(); }
        public void SetRotation(int r) { r &= 3; if (r == Rotation) return; Rotation = r; Changed?.Invoke(); }
        /// Footprint origin (lower-left) for a footprint that starts at anchor and extends in direction dir (0 up,1 right,2 down,3 left).
        public Vector2Int OriginFor(Vector2Int anchor, int dir)
        {
            if (SelectedIndex < 0) return anchor; var size = SizeOf(Data[SelectedIndex], dir);
            return dir == 2 ? anchor - new Vector2Int(0, size.y - 1) : dir == 3 ? anchor - new Vector2Int(size.x - 1, 0) : anchor;
        }
        public string ReasonFor(Vector2Int anchor, int dir)
        {
            if (SelectedIndex < 0) return "No tower selected"; var data = Data[SelectedIndex]; if (!data.footprintRotates) dir = 0;
            string r = validator.ValidateTower(OriginFor(anchor, dir), SizeOf(data, dir));
            return r ?? (balance() < Cost(data) ? "Not enough gold" : null);
        }
        /// 1x1 and 2x2 (and any non-rotating footprint) skip direction select on touch.
        public bool SelectedNeedsDirection { get { if (SelectedIndex < 0) return false; var d = Data[SelectedIndex]; var s = SizeOf(d, 0); return d.footprintRotates && s.x != s.y; } }
        public string Validate(Vector2Int origin, int index, int rotation) => validator.ValidateTower(origin, SizeOf(Data[index], rotation));
        // origin = lower-left cell of the (rotated) footprint.
        public bool TryBuild(Vector2Int origin, int index) => TryBuild(origin, index, Rotation);
        public bool TryBuild(Vector2Int origin, int index, int rotation)
        {
            if (!canBuild() || index < 0 || index >= Data.Length) return false;
            var data = Data[index]; if (!data.footprintRotates) rotation = 0;
            var size = SizeOf(data, rotation);
            string reason = validator.ValidateTower(origin, size);
            if (reason != null) { Notice?.Invoke(reason); return false; }
            if (!spend(Cost(data))) { Notice?.Invoke("Not enough gold"); return false; }
            // Tower prefabs pivot at their base and stand on the wall block top face.
            float elevation=palette.art==null ? 0 : palette.art.blockTop;
            var cells = grid.Footprint(origin, size);
            grid.CommitTower(cells);
            var obj = new GameObject(data.displayName); obj.transform.SetParent(transform); obj.transform.position = grid.FootprintCenter(origin, size);
            var tower = obj.AddComponent<Tower>(); tower.SetFootprint(origin, size, rotation, cells);
            tower.Initialize(data,enemies,palette,modifiers,canAttack,missiles,elevation); towers.Add(tower);
            RecomputeRunes(); hiKey = "?";
            Changed?.Invoke(); Notice?.Invoke("Tower ready"); return true;
        }
        // Removal / selling frees every covered cell (walls remain).
        public bool Remove(Tower tower)
        {
            if (tower == null || !towers.Remove(tower)) return false;
            grid.ReleaseTower(tower.Cells);
            buffIcons.Remove(tower); auras.Remove(tower);
            PrimitiveVisual.DestroyObject(tower.gameObject);
            RecomputeRunes(); Changed?.Invoke(); return true;
        }
        public Tower TowerAt(Vector2Int cell)
        {
            foreach (var t in towers) foreach (var c in t.Cells) if (c == cell) return t;
            return null;
        }
        // Scripted presentation (GameplayShot): hold a tower ghost + slot highlights regardless of input/state.
        public bool Pinned { get; private set; }
        public string PinPreview(int index, Vector2Int origin)
        {
            Pinned = true; SelectedIndex = index; Changed?.Invoke();
            var data = Data[index]; var size = SizeOf(data, 0); string reason = validator.ValidateTower(origin, size);
            var foot = grid.Footprint(origin, size); var footOk = CellValidity(foot, reason == null);
            ShowArtGhost(data, grid.FootprintCenter(origin, size), 0, reason == null, modifiers.Range(data), foot, footOk);
            UpdateHighlight(true, foot, footOk);
            return reason;
        }
        /// Touch/drag input (PlacementInputController) drives the preview itself; desktop hover/click is skipped.
        public bool ExternalDrive { get; set; }
        private void Update()
        {
            if (Pinned) return;
            if (Data == null) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Select(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) Select(3);
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Escape)) Select(-1);
            RefreshSlots(SelectedIndex >= 0 && canBuild());
            if (ExternalDrive) return;
            if (SelectedIndex < 0 || !canBuild() || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) { HidePreview(); return; }
            if (Data[SelectedIndex].footprintRotates && Input.GetKeyDown(KeyCode.R)) RotateFootprint();
            PreviewScreen(Input.mousePosition, out var cell, out bool onBoard);
            if (onBoard && Input.GetMouseButtonDown(0)) TryBuild(cell, SelectedIndex);
        }
        public bool CanRotateSelected => SelectedIndex >= 0 && SelectedIndex < Data.Length && Data[SelectedIndex].footprintRotates;
        public void HidePreview() { HideGhost(); UpdateHighlight(SelectedIndex >= 0 && canBuild(), null, null); }
        /// Shows the ghost for the footprint under a screen point. Returns the rejection reason (null = valid).
        public string PreviewScreen(Vector2 screen, out Vector2Int cell, out bool onBoard)
        {
            cell = default; onBoard = false;
            if (SelectedIndex < 0 || !canBuild()) { HidePreview(); return "No tower selected"; }
            Camera camera = Camera.main;
            var plane = new Plane(Vector3.up,grid.transform.position);
            Ray ray = camera.ScreenPointToRay(screen);
            if (!plane.Raycast(ray,out float distance)) { HideGhost(); return "Off board"; }
            var data = Data[SelectedIndex];
            var size = SizeOf(data, Rotation);
            cell = grid.FootprintOrigin(ray.GetPoint(distance), size);
            if (!grid.InBounds(grid.ToCell(ray.GetPoint(distance)))) { HideGhost(); return "Off board"; }
            onBoard = true;
            return PreviewOrigin(cell);
        }
        /// Ghost + highlight for the selected tower at a footprint origin with the current Rotation. Returns reason (null = valid).
        public string PreviewOrigin(Vector2Int cell)
        {
            if (SelectedIndex < 0 || !canBuild()) { HidePreview(); return "No tower selected"; }
            var data = Data[SelectedIndex];
            var size = SizeOf(data, Rotation);
            string reason = validator.ValidateTower(cell, size);
            Vector3 centre = grid.FootprintCenter(cell, size);
            if (reason == null && balance() < Cost(Data[SelectedIndex])) reason = "Not enough gold";
            Status = reason ?? "Build";
            float range = modifiers.Range(Data[SelectedIndex]);
            var foot = grid.Footprint(cell, size); var footOk = CellValidity(foot, reason == null);
            UpdateHighlight(true, foot, footOk);
            if (ShowArtGhost(data, centre, data.footprintRotates ? Rotation : 0, reason == null, range, foot, footOk))
            {
                ghost.SetActive(false); rangeView.gameObject.SetActive(false);
                return reason;
            }
            ghost.SetActive(true); rangeView.gameObject.SetActive(true);
            ghost.transform.position = centre + Vector3.up * .45f;
            ghost.transform.localScale = new Vector3(.85f * size.x * grid.cellSize, .45f, .85f * size.y * grid.cellSize);
            ghost.GetComponent<Renderer>().sharedMaterial = reason == null ? palette.valid : palette.invalid;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                rangeView.SetPosition(i,centre + new Vector3(Mathf.Cos(angle)*range,.05f,Mathf.Sin(angle)*range));
            }
            return reason;
        }
        /// Footprint cells of the selected tower at origin (for "tap on the ghost" hit tests).
        public List<Vector2Int> SelectedFootprint(Vector2Int origin) => SelectedIndex < 0 ? new List<Vector2Int>() : new List<Vector2Int>(grid.Footprint(origin, SizeOf(Data[SelectedIndex], Rotation)));
        private void HideGhost() { if (ghost != null) ghost.SetActive(false); if (rangeView != null) rangeView.gameObject.SetActive(false); if (artGhost != null) artGhost.gameObject.SetActive(false); }
    }
}
