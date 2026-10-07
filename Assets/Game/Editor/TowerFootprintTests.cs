using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StoneSignal.EditorTools
{
    // Edit-mode checks for tower footprints (com.unity.test-framework is not installed, so this is a self-contained runner).
    // Menu: StoneSignal > Tests > Tower footprints. Batch: -executeMethod StoneSignal.EditorTools.TowerFootprintTests.RunBatch
    public static class TowerFootprintTests
    {
        static readonly Vector2Int Two = new Vector2Int(2, 2);

        [MenuItem("StoneSignal/Tests/Tower footprints")]
        public static string Run()
        {
            var log = new List<string>(); int fail = 0;
            void Case(string name, Func<GridManager, PlacementValidator, bool> body)
            {
                var go = new GameObject("footprint test") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var layout = ScriptableObject.CreateInstance<BoardLayoutData>(); // 16x12, core 2x2 at (7,5), spawns (15,3)/(0,8)/(7,0)
                    var grid = go.AddComponent<GridManager>(); grid.layout = layout; grid.Initialize();
                    var paths = go.AddComponent<PathfindingManager>(); paths.Initialize(grid);
                    var v = go.AddComponent<PlacementValidator>(); v.Initialize(grid, paths);
                    bool ok = body(grid, v); if (!ok) fail++;
                    log.Add((ok ? "PASS " : "FAIL ") + name);
                    UnityEngine.Object.DestroyImmediate(layout);
                }
                catch (Exception e) { fail++; log.Add("FAIL " + name + " " + e.Message); }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            void Walls(GridManager g, Vector2Int o, Vector2Int s) => g.Commit(g.Footprint(o, s), CellState.Blocked);

            Case("2x2 on four walls is valid; commit marks 4 cells; release frees all", (g, v) =>
            {
                var o = new Vector2Int(2, 2); Walls(g, o, Two);
                if (v.ValidateTower(o, Two) != null) return false;
                var cells = g.Footprint(o, Two); g.CommitTower(cells);
                bool marked = cells.TrueForAll(c => g.Get(c) == CellState.TowerSlot);
                g.ReleaseTower(cells);
                return marked && cells.TrueForAll(c => g.Get(c) == CellState.Blocked) && v.ValidateTower(o, Two) == null;
            });
            Case("2x2 centre is the shared corner of its cells", (g, v) =>
                (g.FootprintCenter(new Vector2Int(2, 2), Two) - (g.ToWorld(new Vector2Int(2, 2)) + g.ToWorld(new Vector2Int(3, 3))) * .5f).sqrMagnitude < 1e-6f);
            Case("ghost origin snaps so the footprint centre is nearest the cursor", (g, v) =>
                g.FootprintOrigin(g.FootprintCenter(new Vector2Int(4, 6), Two) + new Vector3(.3f, 0, -.3f), Two) == new Vector2Int(4, 6)
                && g.FootprintOrigin(g.ToWorld(new Vector2Int(5, 5)), Vector2Int.one) == new Vector2Int(5, 5));
            Case("board edge: 2x2 hanging off the right/top edge is rejected", (g, v) =>
            {
                Walls(g, new Vector2Int(14, 10), Two);
                return v.ValidateTower(new Vector2Int(14, 10), Two) == null
                    && v.ValidateTower(new Vector2Int(15, 10), Two) == "Outside the board"
                    && v.ValidateTower(new Vector2Int(14, 11), Two) == "Outside the board"
                    && v.ValidateTower(new Vector2Int(-1, 0), Two) == "Outside the board";
            });
            Case("partial overlap: only 3 of 4 cells are walls -> rejected", (g, v) =>
            {
                g.Commit(new[] { new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(2, 3) }, CellState.Blocked);
                return v.ValidateTower(new Vector2Int(2, 2), Two) != null;
            });
            Case("partial overlap with an existing tower -> rejected, nothing changes", (g, v) =>
            {
                Walls(g, new Vector2Int(2, 2), new Vector2Int(3, 2));
                g.CommitTower(g.Footprint(new Vector2Int(2, 2), Two));
                return v.ValidateTower(new Vector2Int(3, 2), Two) == "Occupied by a tower" && g.Get(new Vector2Int(4, 3)) == CellState.Blocked;
            });
            Case("adjacent to core: walls touching the 2x2 core accept a tower, core cells never do", (g, v) =>
            {
                // core (7,5)-(8,6); footprint (9,5)-(10,6) is flush against its east side
                Walls(g, new Vector2Int(9, 5), Two);
                bool adjacent = v.ValidateTower(new Vector2Int(9, 5), Two) == null;
                bool overCore = v.ValidateTower(new Vector2Int(8, 5), Two) != null && v.ValidateTower(new Vector2Int(7, 5), Two) != null;
                return adjacent && overCore;
            });
            Case("spawn cell is never a tower cell", (g, v) => v.ValidateTower(new Vector2Int(14, 2), Two) != null);
            Case("1x2 rotates to 2x1", (g, v) => GridManager.RotatedSize(new Vector2Int(1, 2), 1) == new Vector2Int(2, 1) && GridManager.RotatedSize(new Vector2Int(1, 2), 2) == new Vector2Int(1, 2));
            Case("tower on bare ground is rejected (towers sit on walls)", (g, v) => v.ValidateTower(new Vector2Int(2, 2), Vector2Int.one) != null);

            string result = string.Join("\n", log) + "\nFOOTPRINT TESTS: " + (log.Count - fail) + "/" + log.Count + " passed";
            Debug.Log(result);
            return fail == 0 ? result : result + " FAILED";
        }
        public static void RunBatch()
        {
            try { EditorApplication.Exit(Run().EndsWith("FAILED") ? 2 : 0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
