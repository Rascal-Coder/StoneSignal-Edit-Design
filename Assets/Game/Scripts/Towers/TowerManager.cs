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
            Changed?.Invoke(); Notice?.Invoke("Tower ready"); return true;
        }
        // Removal / selling frees every covered cell (walls remain).
        public bool Remove(Tower tower)
        {
            if (tower == null || !towers.Remove(tower)) return false;
            grid.ReleaseTower(tower.Cells);
            PrimitiveVisual.DestroyObject(tower.gameObject);
            Changed?.Invoke(); return true;
        }
        public Tower TowerAt(Vector2Int cell)
        {
            foreach (var t in towers) foreach (var c in t.Cells) if (c == cell) return t;
            return null;
        }
        private void Update()
        {
            if (Data == null) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Select(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) Select(3);
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Escape)) Select(-1);
            if (SelectedIndex < 0 || !canBuild() || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) { HideGhost(); return; }
            Camera camera = Camera.main;
            var plane = new Plane(Vector3.up,grid.transform.position);
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!plane.Raycast(ray,out float distance)) { HideGhost(); return; }
            var data = Data[SelectedIndex];
            if (data.footprintRotates && Input.GetKeyDown(KeyCode.R)) RotateFootprint();
            var size = SizeOf(data, Rotation);
            Vector2Int cell = grid.FootprintOrigin(ray.GetPoint(distance), size);
            if (!grid.InBounds(grid.ToCell(ray.GetPoint(distance)))) { HideGhost(); return; }
            string reason = validator.ValidateTower(cell, size);
            Vector3 centre = grid.FootprintCenter(cell, size);
            if (reason == null && balance() < Cost(Data[SelectedIndex])) reason = "Not enough gold";
            Status = reason ?? "Left click to build";
            ghost.SetActive(true); rangeView.gameObject.SetActive(true);
            ghost.transform.position = centre + Vector3.up * .45f;
            ghost.transform.localScale = new Vector3(.85f * size.x * grid.cellSize, .45f, .85f * size.y * grid.cellSize);
            ghost.GetComponent<Renderer>().sharedMaterial = reason == null ? palette.valid : palette.invalid;
            float range = modifiers.Range(Data[SelectedIndex]);
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                rangeView.SetPosition(i,centre + new Vector3(Mathf.Cos(angle)*range,.05f,Mathf.Sin(angle)*range));
            }
            if (Input.GetMouseButtonDown(0)) TryBuild(cell,SelectedIndex);
        }
        private void HideGhost() { if (ghost != null) ghost.SetActive(false); if (rangeView != null) rangeView.gameObject.SetActive(false); }
    }
}
