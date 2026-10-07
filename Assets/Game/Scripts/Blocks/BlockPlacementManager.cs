using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StoneSignal
{
    public sealed class BlockPlacementManager : MonoBehaviour
    {
        public RunModifiers Modifiers { get; set; }
        private GridManager grid;
        private Camera viewCamera;
        private VisualPalette palette;
        private readonly List<GameObject> ghost = new List<GameObject>();
        private Func<bool> canBuild;
        private Transform placedRoot;
        private int rotation;
        private bool toolActive = true;
        public BlockDeckManager Deck { get; private set; }
        public BlockHandManager Hand { get; } = new BlockHandManager();
        public BlockShapeData CurrentShape => Hand.Current;
        public int Remaining => Hand.Cards.Count;
        public string Status { get; private set; } = "Move mouse over the board";
        public bool PreviewValid { get; private set; }
        public Vector2Int PreviewAnchor { get; private set; }
        public event Action Changed;
        public event Action<string> Notice;
        public Func<IReadOnlyList<Vector2Int>, string> ValidateAdditional;

        public void Initialize(GridManager map, Camera camera, VisualPalette colors, BlockShapeData[] data, Func<bool> allowed)
        {
            grid = map; viewCamera = camera; palette = colors; Deck = new BlockDeckManager(data); canBuild = allowed;
            placedRoot = new GameObject("Placed blocks").transform; placedRoot.SetParent(transform);
        }
        public void Refill(int count, bool first = false)
        {
            Hand.Draw(Deck, count);
            rotation = 0; RebuildGhost(); Changed?.Invoke();
        }
        public void SelectCard(int index) { if (Hand.Select(index)) { rotation=0; RebuildGhost(); Changed?.Invoke(); } }
        public void SetToolActive(bool active) { toolActive = active; SetGhostVisible(false); Changed?.Invoke(); }
        public void Rotate() { rotation = (rotation + 1) % 4; Changed?.Invoke(); }
        public List<Vector2Int> CellsAt(Vector2Int anchor)
        {
            var result = new List<Vector2Int>();
            if (CurrentShape == null) return result;
            foreach (Vector2Int p in CurrentShape.Rotated(rotation)) result.Add(anchor + p);
            return result;
        }
        public string ValidatePlacement(Vector2Int anchor)
        {
            if (CurrentShape == null) return "No blocks left. Start the next wave.";
            List<Vector2Int> cells = CellsAt(anchor);
            foreach (Vector2Int p in cells)
            {
                if (!grid.InBounds(p)) return "Outside the board";
                if (!grid.CanPlace(p)) return "Cell occupied / protected";
            }
            return ValidateAdditional?.Invoke(cells);
        }
        public bool CommitPlacement(Vector2Int anchor)
        {
            if (!canBuild() || Remaining <= 0) return false;
            string reason = ValidatePlacement(anchor);
            if (reason != null) { Notice?.Invoke(reason); return false; }
            List<Vector2Int> cells = CellsAt(anchor);
            grid.Commit(cells, CurrentShape.placedState);
            foreach (Vector2Int p in cells)
                ArtVisual.Wall(palette, placedRoot, grid.ToWorld(p), grid.cellSize);
            if (Modifiers != null && Modifiers.BonusSlotShape == CurrentShape) {
                foreach(var cell in cells) { Vector2Int slot=cell+Vector2Int.right;
                    if(grid.InBounds(slot) && grid.CanPlace(slot) && ValidateAdditional?.Invoke(new[]{slot})==null) {
                        grid.Commit(new[]{slot},CellState.Blocked);
                        ArtVisual.Wall(palette, placedRoot, grid.ToWorld(slot), grid.cellSize); break;
                    }
                }
            }
            Hand.Consume();
            rotation = 0; RebuildGhost(); Changed?.Invoke();
            Notice?.Invoke("Placed. Route recalculated.");
            return true;
        }
        public void Preview(Vector2Int anchor)
        {
            PreviewAnchor = anchor;
            string reason = ValidatePlacement(anchor);
            PreviewValid = reason == null;
            Status = PreviewValid ? "Route clear - left click to place" : reason;
            List<Vector2Int> cells = CellsAt(anchor);
            for (int i = 0; i < ghost.Count; i++)
            {
                ghost[i].transform.position = grid.ToWorld(cells[i]) + Vector3.up * .34f;
                ghost[i].GetComponent<Renderer>().sharedMaterial = PreviewValid ? palette.valid : palette.invalid;
            }
            SetGhostVisible(true);
        }
        private void Update()
        {
            if (!toolActive || canBuild == null || !canBuild() || Remaining <= 0 || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) { SetGhostVisible(false); return; }
            if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1)) Rotate();
            var plane = new Plane(Vector3.up, grid.transform.position);
            if (!plane.Raycast(viewCamera.ScreenPointToRay(Input.mousePosition), out float distance)) { SetGhostVisible(false); return; }
            Vector3 point = viewCamera.ScreenPointToRay(Input.mousePosition).GetPoint(distance);
            Vector2Int cell = grid.ToCell(point);
            if (!grid.InBounds(cell)) { SetGhostVisible(false); return; }
            Preview(cell);
            if (Input.GetMouseButtonDown(0)) CommitPlacement(cell);
        }
        private void RebuildGhost()
        {
            foreach (GameObject obj in ghost) if (obj != null) Destroy(obj);
            ghost.Clear();
            if (CurrentShape == null) return;
            foreach (Vector2Int unused in CurrentShape.cells)
                ghost.Add(PrimitiveVisual.Create("Block ghost", PrimitiveType.Cube, transform, Vector3.zero, new Vector3(.89f, .64f, .89f) * grid.cellSize, palette.valid));
            SetGhostVisible(false);
        }
        private void SetGhostVisible(bool visible) { foreach (GameObject obj in ghost) if (obj != null) obj.SetActive(visible); }
    }
}
