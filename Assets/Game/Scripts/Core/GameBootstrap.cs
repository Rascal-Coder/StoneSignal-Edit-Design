using UnityEngine;
using UnityEngine.Rendering;

namespace StoneSignal
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        public GameConfig config;
        public GridManager grid;
        public Camera viewCamera;
        public GameManager Game { get; private set; }
        public PathfindingManager Paths { get; private set; }
        public BlockPlacementManager Blocks { get; private set; }
        public PlacementValidator Validator { get; private set; }
        public EnemyManager Enemies { get; private set; }
        public TowerManager Towers { get; private set; }
        public WaveManager Waves { get; private set; }
        public RewardManager Rewards { get; private set; }
        public RunEconomy Economy { get; private set; }
        public RunModifiers Modifiers { get; private set; }
        private T Service<T>(string label) where T : Component
        {
            var obj = new GameObject(label); obj.transform.SetParent(transform); return obj.AddComponent<T>();
        }
        private void Awake()
        {
            if (config == null || grid == null || viewCamera == null) { Debug.LogError("Game scene configuration is missing. Run StoneSignal > Create / repair game scene."); enabled = false; return; }
            Application.targetFrameRate = 60;
            Modifiers = new RunModifiers();
            Game = Service<GameManager>("Game state");
            grid.Initialize();
            Paths = Service<PathfindingManager>("A star paths"); Paths.Initialize(grid);
            Service<GridView>("Map visuals").Initialize(grid,Paths,config.palette);
            Validator = Service<PlacementValidator>("Placement validation"); Validator.Initialize(grid,Paths);
            Enemies = Service<EnemyManager>("Enemy registry"); Enemies.Initialize(grid,Paths,config.palette,() => Game.State == GameState.Combat);
            Validator.ValidateActors = Enemies.ValidatePlacement;
            Economy = Service<RunEconomy>("Run resources"); Economy.Initialize(config,Enemies,Modifiers);
            Game.Initialize(Economy);
            Blocks = Service<BlockPlacementManager>("Block placement");
            Blocks.Initialize(grid,viewCamera,config.palette,config.blocks,() => Game.State == GameState.Build || (config.allowCombatBlocks && Game.State == GameState.Combat));
            Blocks.Refill(config.blocksPerBuild,true); Blocks.ValidateAdditional = Validator.ValidatePlacement;
            Towers = Service<TowerManager>("Tower placement");
            Towers.Initialize(grid,Validator,Enemies,config.palette,Modifiers,config.towers,Blocks,() => Game.State == GameState.Build,() => Game.State == GameState.Combat,Economy.Spend,() => Economy.Gold);
            var spawner = Service<EnemySpawner>("Enemy spawner"); spawner.Initialize(Enemies);
            Waves = Service<WaveManager>("Waves"); Waves.Initialize(Game,config,Enemies,spawner,Modifiers);
            Game.StateChanged += state => { if(state==GameState.Combat) Economy.AddGold(Modifiers.WaveGold); };
            Waves.Completed += () => Economy.Heal(Modifiers.WaveHeal);
            Enemies.SpeedMultiplier = () => Modifiers.EnemySpeed;
            Blocks.Modifiers = Modifiers;
            Rewards = Service<RewardManager>("Reward choices"); Rewards.Initialize(Game,config,Waves,Blocks,Towers,Modifiers,Economy);
            Service<GameUI>("UGUI").Initialize(this);
            Service<CombatFeedback>("Combat feedback").Initialize(Enemies,config.palette,viewCamera);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-stonesignal-smoke") >= 0)
                Service<PrototypeSmokeTest>("Automated play verification").Initialize(this);
#endif
        }
    }
}
