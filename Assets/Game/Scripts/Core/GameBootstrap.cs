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
        public GridView MapView { get; private set; }
        public RunEconomy Economy { get; private set; }
        public RunModifiers Modifiers { get; private set; }
        public RuneConfig Runes { get; private set; }
        /// DRAW allowance for the current intermission (1st free, 2nd rewarded ad, then none).
        public DrawRules Draws { get; } = new DrawRules();
        public event System.Action DrawsChanged;
        public int GoldInFlight { get; private set; }
        private System.Action<string, int> arrived;
        private void OnDestroy() { if (arrived != null) StoneSignal.VFX.RewardFlyFx.Arrived -= arrived; }
        public const int CardsPerDraw = 3;
        // NextDraw reward: the next draw's 3 cards contain at least one rune block (one charge per reward).
        private bool TakeNextDrawRune() { if (Modifiers.NextDraw <= 0) return false; Modifiers.NextDraw--; return true; }
        /// DRAW pile click (max 2 per wave, 3 cards each): 1st FREE, 2nd after the rewarded ad. Hand full (7) -> no draw is spent.
        public bool Draw()
        {
            if (Game.State != GameState.Build || Blocks.Hand.IsFull) return false;
            var offer = Draws.Next;
            if (offer == DrawRules.Offer.Free)
            {
                Draws.Consume(false); Blocks.DrawCards(CardsPerDraw, TakeNextDrawRune());
                DrawsChanged?.Invoke(); return true;
            }
            if (offer != DrawRules.Offer.Ad || AdServices.Current == null || !AdServices.Current.IsReady) return false;
            bool granted = false;
            AdServices.Current.ShowRewarded("draw", ok => { if (ok && Draws.Consume(true)) { granted = true; Blocks.DrawCards(CardsPerDraw, TakeNextDrawRune()); } DrawsChanged?.Invoke(); });
            return granted;
        }
        /// Ads usable right now (IAdService present and ready). When false, the 2nd draw exists only as a FREE draw from ExtraDraw (免广告再抽).
        public bool AdAvailable => AdServices.Current != null && AdServices.Current.IsReady;
        /// What the DRAW pile offers now: an ad draw while ads are unavailable is no offer at all (pile shows 已用完, not interactable).
        public DrawRules.Offer DrawOffer { get { var o = Draws.Next; return o == DrawRules.Offer.Ad && !AdAvailable ? DrawRules.Offer.None : o; } }
        /// New Build intermission: draws reset; one pending ExtraDraw charge (if any) makes its 2nd draw free. Extra charges stay
        /// pending (carry over), they are no longer zeroed.
        public void BeginIntermission()
        {
            bool free = Modifiers.ExtraDraw > 0; if (free) Modifiers.ExtraDraw--;
            Draws.BeginIntermission(free); DrawsChanged?.Invoke();
        }
        /// ExtraDraw obtained during Build: applies to this wave if its 2nd draw is still open, else carries over to the next wave.
        /// (Reward picks happen between waves, so RewardData.Apply just adds a pending charge that the next Build takes.)
        public bool GrantExtraDraw()
        {
            bool now = Game.State == GameState.Build && Draws.GrantFreeSecond();
            if (!now) Modifiers.ExtraDraw++;
            DrawsChanged?.Invoke(); return now;
        }
        private T Service<T>(string label) where T : Component
        {
            var obj = new GameObject(label); obj.transform.SetParent(transform); return obj.AddComponent<T>();
        }
        private void Awake()
        {
            if (config == null || grid == null || viewCamera == null) { Debug.LogError("Game scene configuration is missing. Run StoneSignal > Create / repair game scene."); enabled = false; return; }
            Application.targetFrameRate = 60;
            GameRng.BeginRun(); // v18 P0: fresh seed per real run (logged); tests/diagnostics stay deterministic
            Modifiers = new RunModifiers();
            TimeController.ResetAll();
            Runes = config.runes != null ? config.runes : RuneConfig.CreateDefault();
            var art = config.palette != null ? config.palette.art : null;
            if (art != null && art.runeAtlas != null) StoneSignal.VFX.RuneInlay.Atlas = art.runeAtlas;
            StoneSignal.VFX.WallHighlight.ClearRegistry();
            if (FindObjectOfType<StoneSignal.VFX.WallHighlight>() == null) Service<StoneSignal.VFX.WallHighlight>("Wall highlight").cellSize = grid.cellSize;
            if (art != null && art.enemyGroundSystem != null && FindObjectOfType<StoneSignal.VFX.EnemyGroundFxSystem>() == null)
                Instantiate(art.enemyGroundSystem, transform).name = "Enemy ground FX system"; // single global dust emitter (cap 64)
            Game = Service<GameManager>("Game state");
            if (config.layout != null) grid.layout = config.layout;
            grid.Initialize();
            if (art != null) { grid.tileTop = art.tileTop; grid.wallTop = art.blockTop; } // touch / mouse picking on the visible surface
            Paths = Service<PathfindingManager>("A star paths"); Paths.Initialize(grid);
            var mapView = Service<GridView>("Map visuals"); mapView.Initialize(grid,Paths,config.palette); MapView = mapView;
            Validator = Service<PlacementValidator>("Placement validation"); Validator.Initialize(grid,Paths);
            Enemies = Service<EnemyManager>("Enemy registry"); Enemies.Initialize(grid,Paths,config.palette,() => Game.State == GameState.Combat);
            Validator.ValidateActors = Enemies.ValidatePlacement;
            Economy = Service<RunEconomy>("Run resources"); Economy.Initialize(config,Enemies,Modifiers);
            Game.Initialize(Economy);
            Blocks = Service<BlockPlacementManager>("Block placement");
            Blocks.Initialize(grid,viewCamera,config.palette,config.blocks,() => Game.State == GameState.Build || (config.allowCombatBlocks && Game.State == GameState.Combat));
            Blocks.Runes = Runes; Blocks.Refill(config.blocksPerBuild,true); Blocks.ValidateAdditional = Validator.ValidatePlacement;
            Towers = Service<TowerManager>("Tower placement");
            Towers.Initialize(grid,Validator,Enemies,config.palette,Modifiers,config.towers,Blocks,() => Game.State == GameState.Build,() => Game.State == GameState.Combat,Economy.Spend,() => Economy.Gold);
            Towers.RuneRulesConfig = Runes;
            // v16.2: spawn portals (rise before walking, active only in combat) + core enclosure damage states
            Enemies.PortalAt = MapView.PortalAt;
            Game.StateChanged += st => MapView.SetPortalsActive(st == GameState.Combat);
            Economy.Changed += () => { if (MapView.CoreFx != null) MapView.CoreFx.SetHealth01(Economy.MaxHP > 0 ? Economy.HP / (float)Economy.MaxHP : 1); };
            Enemies.Resolved += (e, r) => { if (r == EnemyResolution.Escaped && MapView.CoreFx != null) MapView.CoreFx.PlayHit(); };
            { var pic = Service<PlacementInputController>("Placement input"); pic.Initialize(Towers, Blocks, grid, config.palette != null ? config.palette.art : null); }
            var spawner = Service<EnemySpawner>("Enemy spawner"); spawner.Initialize(Enemies);
            Waves = Service<WaveManager>("Waves"); Waves.Initialize(Game,config,Enemies,spawner,Modifiers);
            Game.StateChanged += state => { if(state==GameState.Combat) Economy.AddGold(Modifiers.WaveGold); if(state==GameState.Build) BeginIntermission(); }; // ExtraDraw reward: this wave's 2nd draw needs no ad
            Waves.Completed += () => Economy.Heal(Modifiers.WaveHeal);
            Enemies.SpeedMultiplier = () => Modifiers.EnemySpeed;
            Blocks.Modifiers = Modifiers;
            Rewards = Service<RewardManager>("Reward choices"); Rewards.Initialize(Game,config,Waves,Blocks,Towers,Modifiers,Economy); Rewards.Runes = Runes;
            var ui = Service<GameUI>("UGUI"); ui.Initialize(this);
            Service<CameraFit>("Camera fit").Initialize(viewCamera, grid, ui, this); // v18.2: aspect-adaptive framing (board + entry bridges clear of the HUD)
            DrawsChanged += () => Blocks.NotifyChanged();
            if (art != null && art.rewardFlyGold != null)
            {
                // kill gold: icons pop at the enemy and fly into the gold counter, which counts up on arrival
                var fly = Instantiate(art.rewardFlyGold, transform).GetComponent<StoneSignal.VFX.RewardFlyFx>();
                if (fly != null)
                {
                    fly.cam = viewCamera; fly.BindTarget(ui.GoldTarget, ui.GoldCounter);
                    Economy.KillReward += (pos, g) => { if (g <= 0) return; GoldInFlight += g; fly.Play(pos, g, Vector3.zero); };
                    arrived = (id, v) => { GoldInFlight = Mathf.Max(0, GoldInFlight - v); }; StoneSignal.VFX.RewardFlyFx.Arrived += arrived;
                    Game.StateChanged += st => { if (st != GameState.Combat && GoldInFlight > 0 && fly.InFlight == 0) GoldInFlight = 0; };
                }
            }
            Service<CombatFeedback>("Combat feedback").Initialize(Enemies,config.palette,viewCamera);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-touch") >= 0) { PointerInput.ForceTouch = true; Debug.Log("TOUCH MODE: forced by -touch (diagnostics)"); }
            { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a,"-camfit"); if (i >= 0 && i + 1 < a.Length) { CameraFit.Mode = a[i + 1].ToLowerInvariant() == "hudfree" ? CameraFitMode.HudFree : CameraFitMode.Screen; Debug.Log("CAMERA FIT MODE: " + CameraFit.Mode + " (-camfit)"); } } // v18.3 HudFree experiment
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-stonesignal-smoke") >= 0)
                Service<PrototypeSmokeTest>("Automated play verification").Initialize(this);
            { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a,"-stonesignal-shot");
              if (i >= 0 && i + 1 < a.Length) Service<GameplayShot>("Gameplay screenshot").Initialize(this, a[i + 1]);
              int j = System.Array.IndexOf(a,"-stonesignal-drag");
              if (j >= 0 && j + 1 < a.Length) Service<DragOffsetProbe>("Drag offset probe").Initialize(this, a[j + 1]); }
#endif
        }
    }
}
