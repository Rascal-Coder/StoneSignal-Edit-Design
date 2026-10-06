using System;
using UnityEditor;
using UnityEngine;
using StoneSignal;

public static class PrototypeChecks
{
    public static void RewardChecks()
    {
        EconomyChecks();
        var root = new GameObject("Reward test");
        var palette = ScriptableObject.CreateInstance<VisualPalette>();
        var config = ScriptableObject.CreateInstance<GameConfig>();
        var reward = ScriptableObject.CreateInstance<RewardData>();
        try
        {
            var grid = root.AddComponent<GridManager>(); grid.Initialize();
            var paths = root.AddComponent<PathfindingManager>(); paths.Initialize(grid);
            var enemies = root.AddComponent<EnemyManager>(); enemies.Initialize(grid,paths,palette,() => true);
            var modifiers = new RunModifiers();
            var economy = root.AddComponent<RunEconomy>(); economy.Initialize(config,enemies,modifiers);
            reward.amount=.1f; reward.effect=RewardEffect.AllDamage; reward.Apply(modifiers,economy);
            Require(Mathf.Approximately(modifiers.Damage,1.1f), "Damage upgrade");
            reward.effect=RewardEffect.AllAttackSpeed; reward.Apply(modifiers,economy);
            Require(Mathf.Approximately(modifiers.AttackSpeed,1.1f), "Speed upgrade");
            reward.amount=.15f; reward.effect=RewardEffect.ArrowRange; reward.Apply(modifiers,economy);
            Require(Mathf.Approximately(modifiers.ArrowRange,1.15f), "Arrow range upgrade");
            reward.amount=10; reward.effect=RewardEffect.BaseHP; reward.Apply(modifiers,economy);
            Require(economy.HP==40 && economy.MaxHP==40, "Base upgrade raises current and maximum HP");
            reward.amount=.2f; reward.effect=RewardEffect.NextWaveGold; reward.Apply(modifiers,economy);
            modifiers.BeginWave(); Require(Mathf.Approximately(modifiers.CurrentWaveGold,1.2f), "Gold applies to next wave");
            modifiers.BeginWave(); Require(Mathf.Approximately(modifiers.CurrentWaveGold,1), "Gold bonus expires after one wave");
            reward.effect=RewardEffect.CannonRadius; reward.Apply(modifiers,economy);
            Require(Mathf.Approximately(modifiers.CannonRadius,1.2f), "Cannon radius upgrade");
            Debug.Log("MILESTONE 7 PASS: all six reward effects, single-wave gold bonus; choice gating verified in smoke test");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(palette); UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(reward); }
    }
    public static void EconomyChecks()
    {
        CombatChecks();
        var root = new GameObject("Economy test");
        var palette = ScriptableObject.CreateInstance<VisualPalette>();
        var data = ScriptableObject.CreateInstance<EnemyData>();
        var config = ScriptableObject.CreateInstance<GameConfig>();
        try
        {
            var grid = root.AddComponent<GridManager>(); grid.Initialize();
            var paths = root.AddComponent<PathfindingManager>(); paths.Initialize(grid);
            var enemies = root.AddComponent<EnemyManager>(); enemies.Initialize(grid,paths,palette,() => true);
            var modifiers = new RunModifiers();
            var economy = root.AddComponent<RunEconomy>(); economy.Initialize(config,enemies,modifiers);
            var game = root.AddComponent<GameManager>(); game.Initialize(economy);
            Require(economy.Gold == 200 && economy.HP == 30, "Starting resources configured");
            Require(economy.Spend(50) && economy.Gold == 150 && !economy.Spend(151), "Spend is atomic and cannot go negative");
            enemies.Spawn(data).TakeDamage(100);
            Require(economy.Gold == 162, "Kill awards gold once");
            enemies.Spawn(data).Advance(100);
            Require(economy.HP == 28 && economy.Gold == 162, "Escape hurts base, awards no gold");
            game.SetState(GameState.Combat); game.SetState(GameState.Reward); game.SetState(GameState.Build);
            Require(game.State == GameState.Build, "State cycle");
            for (int i=0; i<14; i++) enemies.Spawn(data).Advance(100);
            Require(game.State == GameState.GameOver && economy.HP == 0, "Base death transitions to GameOver");
            Debug.Log("MILESTONE 6 PASS: gold, base HP, atomic spend, state cycle, GameOver; live waves verified in final smoke test");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(palette); UnityEngine.Object.DestroyImmediate(data); UnityEngine.Object.DestroyImmediate(config); }
    }
    public static void CombatChecks()
    {
        EnemyChecks();
        var root = new GameObject("Combat test");
        var palette = ScriptableObject.CreateInstance<VisualPalette>();
        var data = ScriptableObject.CreateInstance<EnemyData>();
        try
        {
            var grid = root.AddComponent<GridManager>(); grid.Initialize();
            var paths = root.AddComponent<PathfindingManager>(); paths.Initialize(grid);
            var enemies = root.AddComponent<EnemyManager>(); enemies.Initialize(grid,paths,palette,() => true);
            var rear = enemies.Spawn(data); var front = enemies.Spawn(data); front.Advance(.3f);
            Require(enemies.ClosestToGoal(rear.transform.position, 4) == front, "Target prefers least remaining route");
            var bolt = new GameObject("Single test projectile").AddComponent<Projectile>();
            bolt.Initialize(front,enemies,100,10,0,() => true); bolt.Advance(1);
            Require(Mathf.Approximately(front.HP,25) && Mathf.Approximately(rear.HP,35), "Single shot damages only target");
            var cannon = new GameObject("AOE test projectile").AddComponent<Projectile>();
            cannon.Initialize(front,enemies,100,50,2,() => true); cannon.Advance(1);
            Require(enemies.Active.Count == 0, "AOE kills both without collection mutation errors");
            Debug.Log("MILESTONE 5 PASS: frontmost target, single target damage, AOE and registry removal");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(palette); UnityEngine.Object.DestroyImmediate(data); }
    }
    public static void EnemyChecks()
    {
        PlacementChecks();
        var root = new GameObject("Enemy test");
        var palette = ScriptableObject.CreateInstance<VisualPalette>();
        var data = ScriptableObject.CreateInstance<EnemyData>();
        try
        {
            var grid = root.AddComponent<GridManager>(); grid.Initialize();
            var path = root.AddComponent<PathfindingManager>(); path.Initialize(grid);
            var enemies = root.AddComponent<EnemyManager>(); enemies.Initialize(grid,path,palette,() => true);
            var validator = root.AddComponent<PlacementValidator>(); validator.Initialize(grid,path); validator.ValidateActors = enemies.ValidatePlacement;
            var enemy = enemies.Spawn(data); enemy.Advance(.3f);
            Vector3 before = enemy.transform.position;
            float oldDistance = enemy.RemainingDistance;
            Require(validator.ValidatePlacement(new[] { enemy.NavigationAnchor }) != null, "Current movement edge reserved");
            var wall = new[] { new Vector2Int(5,4), new Vector2Int(5,5), new Vector2Int(5,3) };
            Require(validator.ValidatePlacement(wall) == null, "Safe live repath placement legal");
            grid.Commit(wall,CellState.Blocked);
            Require(enemy.transform.position == before, "Repath does not teleport");
            Require(enemy.RemainingDistance > oldDistance, "Live enemy takes detour");
            int resolved = 0; enemies.Resolved += (e,r) => resolved++;
            enemy.Advance(100);
            Require(enemies.Active.Count == 0 && resolved == 1, "Goal resolves registry exactly once");
            Debug.Log("MILESTONE 4 PASS: continuous dynamic repath, occupied edge protection, goal resolution");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(palette); UnityEngine.Object.DestroyImmediate(data); }
    }
    public static void PlacementChecks()
    {
        ShapeChecks();
        var root = new GameObject("Placement test");
        try
        {
            var grid = root.AddComponent<GridManager>(); grid.Initialize();
            var path = root.AddComponent<PathfindingManager>(); path.Initialize(grid);
            var validator = root.AddComponent<PlacementValidator>(); validator.Initialize(grid, path);
            var wall = new System.Collections.Generic.List<Vector2Int>();
            for (int y = 0; y < 9; y++) wall.Add(new Vector2Int(8,y));
            Require(validator.ValidatePlacement(wall) == null, "Wall with gap is legal");
            grid.Commit(wall, CellState.Blocked);
            Require(path.CurrentPath.Count > 16, "Committed wall recalculates route");
            Require(validator.ValidatePlacement(new[] { new Vector2Int(8,9) }) != null, "Last gap cannot be sealed");
            Require(grid.Get(new Vector2Int(8,9)) == CellState.Empty, "Rejected placement leaves grid unchanged");
            Require(validator.ValidatePlacement(new[] { grid.spawn }) != null, "Cannot cover spawn");
            Require(validator.ValidatePlacement(new[] { new Vector2Int(-1,0) }) != null, "Cannot place outside grid");
            Require(validator.ValidateTower(new Vector2Int(8,9)) != null, "Tower cannot seal last gap either");
            Require(validator.ValidateTower(new Vector2Int(8,0)) == null, "Wall can host a tower");
            Debug.Log("MILESTONE 3 PASS: last gap protected, no live mutation, tower validation, route updates");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void ShapeChecks()
    {
        GridChecks();
        var shape = ScriptableObject.CreateInstance<BlockShapeData>();
        try
        {
            shape.cells = new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(1,0) };
            var original = shape.Rotated(0);
            Require(shape.Rotated(1)[1] != original[1], "L rotates by a quarter turn");
            Require(new System.Collections.Generic.HashSet<Vector2Int>(shape.Rotated(4)).SetEquals(original), "Four rotations restore shape");
            Require(new System.Collections.Generic.HashSet<Vector2Int>(shape.Rotated(1)).Count == 4, "Rotation preserves four unique cells");
            Require(shape.cells[2] == new Vector2Int(0,2), "Rotation does not mutate asset");
            Debug.Log("MILESTONE 2 PASS: normalized rotation, four turns, immutable shape cells");
        }
        finally { UnityEngine.Object.DestroyImmediate(shape); }
    }
    public static void GridChecks()
    {
        var root = new GameObject("Grid test");
        try
        {
            var grid = root.AddComponent<GridManager>(); grid.Initialize();
            var path = root.AddComponent<PathfindingManager>(); path.Initialize(grid);
            Require(path.CurrentPath.Count == 16, "Straight route is 16 nodes");
            Require(grid.ToCell(grid.ToWorld(new Vector2Int(3, 7))) == new Vector2Int(3, 7), "Coordinate roundtrip");
            Require(!grid.CanPlace(grid.spawn) && !grid.CanPlace(grid.goal), "Endpoints protected");
            var wall = new System.Collections.Generic.HashSet<Vector2Int>();
            for (int y = 0; y < 10; y++) wall.Add(new Vector2Int(8, y));
            Require(path.FindPath(grid.spawn, grid.goal, wall).Count == 0, "Full wall disconnects route");
            wall.Remove(new Vector2Int(8, 9));
            Require(path.FindPath(grid.spawn, grid.goal, wall).Count > 16, "A single gap preserves a detour");
            Debug.Log("MILESTONE 1 PASS: grid coordinates, endpoints, A*, disconnected route, detour");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("CHECK FAILED: " + description);
    }
}
