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
        public RuneConfig Runes { get; set; }
        // wall renderer + inlaid rune per grid cell (runes are gameplay data; RuneInlay is the art)
        private readonly Dictionary<Vector2Int, Renderer> walls = new Dictionary<Vector2Int, Renderer>();
        private readonly Dictionary<Vector2Int, int> runeCells = new Dictionary<Vector2Int, int>();
        public int RuneAt(Vector2Int cell) => runeCells.TryGetValue(cell, out int r) ? r : RuneRules.NoRune;
        public IReadOnlyDictionary<Vector2Int, int> RuneCells => runeCells;
        public bool IsWall(Vector2Int cell) => walls.ContainsKey(cell);
        public IEnumerable<Vector2Int> WallCells => walls.Keys;
        public event Action RunesChanged;
        int RollRune() => Runes != null ? RuneRules.Roll(Runes, GameRng.Rewards) : RuneRules.NoRune;
        public void Refill(int count, bool first = false)
        {
            Hand.Draw(Deck, count, RollRune);
            rotation = 0; RebuildGhost(); Changed?.Invoke();
        }
        /// DRAW pile: append cards to the hand (the intermission allowance lives in DrawRules / GameBootstrap.Draw).
        /// Rune reward: one wall block from the deck carrying the rune (dropped if the hand is full).
        public void AddRuneCard(int rune) { var shape = Deck.Draw(); if (shape != null) Hand.AddCard(shape, rune); RebuildGhost(); Changed?.Invoke(); }
        public void NotifyChanged() => Changed?.Invoke();
        public void DrawCards(int count, bool guaranteeRune = false) { Hand.Add(Deck, count, RollRune, guaranteeRune && Runes != null ? () => RuneRules.RollType(Runes, GameRng.Rewards) : (Func<int>)null); RebuildGhost(); Changed?.Invoke(); }
        /// Highlight the real wall blocks under a footprint (art WallHighlight) and keep them out of the instanced batch.
        public void HighlightWalls(Vector2Int[] cells, bool[] valid)
        {
            if (cells == null || cells.Length == 0) StoneSignal.VFX.WallHighlight.Clear(); else StoneSignal.VFX.WallHighlight.SetCells(cells, valid);
            if (!IsInvoking(nameof(MergeWalls))) MergeWalls();
        }
        public void SelectCard(int index) { if (Hand.Select(index)) { rotation=0; RebuildGhost(); Changed?.Invoke(); } }
        public void SetToolActive(bool active) { toolActive = active; SetGhostVisible(false); Changed?.Invoke(); }
        public int Rotation => rotation;
        public void SetRotation(int r) { r &= 3; if (r == rotation) return; rotation = r; if (artGhost != null) RebuildGhost(); Changed?.Invoke(); }
        public string ValidatePlacement(Vector2Int anchor, int rot) { int keep = rotation; rotation = rot & 3; try { return ValidatePlacement(anchor); } finally { rotation = keep; } }
        public void Rotate() { rotation = (rotation + 1) % 4; if (artGhost != null) RebuildGhost(); Changed?.Invoke(); }
        /// Cells for an explicit rotation (no state change).
        public List<Vector2Int> CellsAt(Vector2Int anchor, int rot) { int keep = rotation; rotation = rot & 3; try { return CellsAt(anchor); } finally { rotation = keep; } }
        /// True while the placement ghost is visible (diagnostics / drag report).
        public bool PreviewShown { get; private set; }
        public List<Vector2Int> CellsAt(Vector2Int anchor)
        {
            var result = new List<Vector2Int>();
            if (CurrentShape == null) return result;
            foreach (Vector2Int p in CurrentShape.TurnedAround(rotation)) result.Add(anchor + p); // anchor cell always covered; rotation = direction
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
        // Placed walls are static: bake them into one mesh per material (draw-call budget).
        public void MergeWalls() { if (placedRoot != null) MeshMerge.Rebuild(placedRoot, "Merged walls", UnityEngine.Rendering.ShadowCastingMode.On); }
        public bool CommitPlacement(Vector2Int anchor)
        {
            if (!canBuild() || Remaining <= 0) return false;
            string reason = ValidatePlacement(anchor);
            if (reason != null) { Notice?.Invoke(reason); return false; }
            List<Vector2Int> cells = CellsAt(anchor);
            grid.Commit(cells, CurrentShape.placedState);
            int rune = Hand.CurrentRune;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int p = cells[i];
                var wall = ArtVisual.Wall(palette, placedRoot, grid.ToWorld(p), grid.cellSize);
                RegisterWall(p, wall, i == 0 ? rune : RuneRules.NoRune); // rune sits on the card's first cell
                if (artGhost != null && wall != null) artGhost.PlayDrop(wall.transform);
            }
            if (rune != RuneRules.NoRune) RunesChanged?.Invoke();
            if (Modifiers != null && Modifiers.BonusSlotShape == CurrentShape) {
                foreach(var cell in cells) { Vector2Int slot=cell+Vector2Int.right;
                    if(grid.InBounds(slot) && grid.CanPlace(slot) && ValidateAdditional?.Invoke(new[]{slot})==null) {
                        grid.Commit(new[]{slot},CellState.Blocked);
                        RegisterWall(slot, ArtVisual.Wall(palette, placedRoot, grid.ToWorld(slot), grid.cellSize), RuneRules.NoRune); break;
                    }
                }
            }
            CancelInvoke(nameof(MergeWalls)); Invoke(nameof(MergeWalls), .9f); // after the drop animation
            Hand.Consume();
            rotation = 0; RebuildGhost(); Changed?.Invoke();
            Notice?.Invoke("Placed. Route recalculated.");
            return true;
        }
        private void RegisterWall(Vector2Int cell, GameObject wall, int rune)
        {
            var r = wall != null ? wall.GetComponentInChildren<MeshRenderer>() : null;
            if (r == null) return;
            walls[cell] = r; StoneSignal.VFX.WallHighlight.Register(cell, r);
            if (rune != RuneRules.NoRune) { runeCells[cell] = rune; StoneSignal.VFX.RuneInlay.Set(r, (StoneSignal.VFX.RuneId)rune); }
        }
        /// Scripted wall (presentation/tests): visual + registry for an already-committed cell.
        public void SpawnWall(Vector2Int cell, int rune = RuneRules.NoRune) => RegisterWall(cell, ArtVisual.Wall(palette, placedRoot, grid.ToWorld(cell), grid.cellSize), rune);
        /// Scripted/test placement of a rune on an existing wall.
        public void InlayRune(Vector2Int cell, int rune)
        {
            if (!walls.TryGetValue(cell, out var r)) return;
            runeCells[cell] = rune; StoneSignal.VFX.RuneInlay.Set(r, (StoneSignal.VFX.RuneId)rune); MergeWalls(); RunesChanged?.Invoke();
        }
        public void Preview(Vector2Int anchor)
        {
            PreviewAnchor = anchor; PreviewShown = CurrentShape != null;
            string reason = ValidatePlacement(anchor);
            PreviewValid = reason == null;
            Status = PreviewValid ? "Route clear - left click to place" : reason;
            List<Vector2Int> cells = CellsAt(anchor);
            if (artGhost != null)
            {
                // v8 ghost: cells are laid out relative to the anchor cell centre.
                artGhost.transform.position = grid.ToWorld(anchor) + Vector3.up * (palette.art != null ? palette.art.tileTop : 0);
                artGhost.SetValid(PreviewValid); ShowArtCells(true);
                return;
            }
            for (int i = 0; i < ghost.Count; i++)
            {
                ghost[i].transform.position = grid.ToWorld(cells[i]) + Vector3.up * .34f;
                ghost[i].GetComponent<Renderer>().sharedMaterial = PreviewValid ? palette.valid : palette.invalid;
            }
            SetGhostVisible(true);
        }
        public bool Pinned { get; set; } // scripted presentation keeps the current Preview()
        public Transform PlacedRoot => placedRoot;
        public bool ExternalDrive { get; set; }
        /// v18.6: walls may be placed in the current game state (Build; combat only with allowCombatBlocks).
        public bool PhaseAllows => canBuild != null && canBuild();
        /// v18.6: route a notice through the same toast path as placement notices (e.g. a wall drag in combat).
        public void RaiseNotice(string message) => Notice?.Invoke(message);
        public bool CanPlaceNow => toolActive && canBuild != null && canBuild() && Remaining > 0;
        private void Update()
        {
            if (Pinned) return;
            if (ExternalDrive) return;
            if (!CanPlaceNow || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) { SetGhostVisible(false); return; }
            if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1)) Rotate();
            if (PreviewScreen(Input.mousePosition, out var cell) && Input.GetMouseButtonDown(0)) CommitPlacement(cell);
        }
        public void HidePreview() => SetGhostVisible(false);
        /// Shows the ghost under a screen point; false when off board. Validity is PreviewValid.
        public bool PreviewScreen(Vector2 screen, out Vector2Int cell)
        {
            cell = default;
            if (!CanPlaceNow) { SetGhostVisible(false); return false; }
            if (!grid.RaycastCell(viewCamera.ScreenPointToRay(screen), out cell, out _)) { SetGhostVisible(false); return false; } // visible surface, not y = 0
            if (!grid.InBounds(cell)) { SetGhostVisible(false); return false; }
            Preview(cell);
            return true;
        }
        private void RebuildGhost()
        {
            foreach (GameObject obj in ghost) if (obj != null) Destroy(obj);
            ghost.Clear();
            if (CurrentShape == null) return;
            if (palette.art != null && palette.art.placeGhostBlock != null)
            {
                if (artGhost == null)
                {
                    var g = ArtVisual.Create(palette.art.placeGhostBlock, transform, Vector3.zero);
                    artGhost = g.GetComponent<StoneSignal.VFX.PlacementGhost>(); if (artGhost == null) Destroy(g);
                }
                if (artGhost != null)
                {
                    var shape = new List<Vector2Int>(CurrentShape.TurnedAround(rotation));
                    artGhost.cellSize = grid.cellSize; artGhost.SetCells(shape.ToArray());
                    SetGhostVisible(false); return;
                }
            }
            foreach (Vector2Int unused in CurrentShape.cells)
                ghost.Add(PrimitiveVisual.Create("Block ghost", PrimitiveType.Cube, transform, Vector3.zero, new Vector3(.89f, .64f, .89f) * grid.cellSize, palette.valid));
            SetGhostVisible(false);
        }
        private void SetGhostVisible(bool visible) { if (!visible) PreviewShown = false; foreach (GameObject obj in ghost) if (obj != null) obj.SetActive(visible); if (artGhost != null && !visible) ShowArtCells(false); }
        // Keep the ghost object active so PlayDrop's coroutine survives; only its cell renderers hide.
        private void ShowArtCells(bool on) { var c = artGhost.transform.Find("Cells"); if (c != null) c.gameObject.SetActive(on); }
        private StoneSignal.VFX.PlacementGhost artGhost;
    }
}
