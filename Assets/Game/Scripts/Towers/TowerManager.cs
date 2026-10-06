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
        public bool TryBuild(Vector2Int cell, int index)
        {
            if (!canBuild() || index < 0 || index >= Data.Length) return false;
            string reason = validator.ValidateTower(cell);
            if (reason != null) { Notice?.Invoke(reason); return false; }
            if (!spend(Cost(Data[index]))) { Notice?.Invoke("Not enough gold"); return false; }
            float elevation=grid.Get(cell)==CellState.Blocked && palette.art!=null ? .62f : 0;
            grid.CommitTower(cell);
            var obj = new GameObject(Data[index].displayName); obj.transform.SetParent(transform); obj.transform.position = grid.ToWorld(cell);
            var tower = obj.AddComponent<Tower>(); tower.Initialize(Data[index],enemies,palette,modifiers,canAttack,missiles,elevation); towers.Add(tower);
            Changed?.Invoke(); Notice?.Invoke("Tower ready"); return true;
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
            Vector2Int cell = grid.ToCell(ray.GetPoint(distance));
            if (!grid.InBounds(cell)) { HideGhost(); return; }
            string reason = validator.ValidateTower(cell);
            if (reason == null && balance() < Cost(Data[SelectedIndex])) reason = "Not enough gold";
            Status = reason ?? "Left click to build";
            ghost.SetActive(true); rangeView.gameObject.SetActive(true);
            ghost.transform.position = grid.ToWorld(cell) + Vector3.up * .45f;
            ghost.GetComponent<Renderer>().sharedMaterial = reason == null ? palette.valid : palette.invalid;
            float range = modifiers.Range(Data[SelectedIndex]);
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                rangeView.SetPosition(i,grid.ToWorld(cell) + new Vector3(Mathf.Cos(angle)*range,.05f,Mathf.Sin(angle)*range));
            }
            if (Input.GetMouseButtonDown(0)) TryBuild(cell,SelectedIndex);
        }
        private void HideGhost() { if (ghost != null) ghost.SetActive(false); if (rangeView != null) rangeView.gameObject.SetActive(false); }
    }
}
