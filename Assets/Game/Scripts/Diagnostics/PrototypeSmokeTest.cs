#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StoneSignal
{
    // Only activated by an explicit command line flag in a development build.
    public sealed class PrototypeSmokeTest : MonoBehaviour
    {
        private GameBootstrap session;
        private string captureDirectory;
        private bool failed;
        private int checks;
        private readonly List<string> results=new List<string>();
        public void Initialize(GameBootstrap game)
        {
            session=game;
            string[] args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"-captureDir");
            captureDirectory=index>=0 && index+1<args.Length ? args[index+1] : Application.persistentDataPath;
            Directory.CreateDirectory(captureDirectory);
            session.Blocks.enabled=false; // Programmatic previews stay stable during captures.
            int seedIndex=Array.IndexOf(args,"-seed");
            int seed=seedIndex>=0 && seedIndex+1<args.Length ? int.Parse(args[seedIndex+1]) : 37; UnityEngine.Random.InitState(seed); GameRng.SetSeed(seed); // split deterministic streams
            Application.logMessageReceived+=OnLog;
            StartCoroutine(Run());
        }
        private void OnLog(string message,string stack,LogType type)
        {
            if(type==LogType.Exception || type==LogType.Error) { failed=true; results.Add("ERROR: "+message); }
        }
        private void Require(bool condition,string description)
        {
            if(!condition) { failed=true; Debug.LogError("SMOKE CHECK FAILED: "+description); Finish(); throw new Exception(description); }
            checks++; results.Add("PASS: "+description); Debug.Log("SMOKE PASS: "+description);
        }
        private IEnumerator Capture(string name)
        {
            yield return null;
            // A hidden Windows player does not present a swap chain. Submit a real
            // URP render to a texture and temporarily include UGUI in that camera.
            var camera=session.viewCamera;
            var canvases=session.GetComponentsInChildren<Canvas>(true);
            var texture=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32);
            texture.Create();
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=.3f; }
            Canvas.ForceUpdateCanvases();
            InstancedBatch.SubmitAll(); // tiles/walls are LateUpdate instanced draws: queue them for this Update-time render
            if(name=="03-combat") RenderDiag.Log(name);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=texture });
            var previous=RenderTexture.active; RenderTexture.active=texture;
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(captureDirectory,name+".png"),image.EncodeToPNG());
            RenderTexture.active=previous;
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; }
            Destroy(image); texture.Release(); Destroy(texture);
            yield return null;
        }
        private IEnumerator Run()
        {
            yield return null; yield return null;
            if(session.config.palette.art!=null) {
                Require(session.config.palette.art.block!=null && session.config.palette.art.signalCore!=null,"Art catalog environment assigned");
                Require(session.config.palette.art.tile!=null && session.config.palette.art.tilePath!=null && session.config.palette.art.tileGoal!=null,"Board tile, road and goal assigned");
                foreach(var tower in session.config.towers) Require(tower.visualPrefab!=null && tower.icon!=null,"Tower model and icon "+tower.displayName);
                Application.runInBackground=true; // unfocused smoke window must keep running
                Require(session.config.palette.art.boardCliff!=null || (session.grid.layout!=null && session.grid.layout.levelDressing!=null) || session.GetComponentsInChildren<BoardEnvironment>().Length==1,"Board island / decoration initialized");
            }
            var grid=session.grid;
            Require(session.Paths.CurrentPaths.Count==grid.Spawns.Count && session.Paths.CurrentPaths.TrueForAll(p=>p.Count>1) && session.Game.State==GameState.Build,"Initial scene, a route from every spawn and Build state");
            session.Blocks.Refill(5);
            for(int i=0;i<5;i++) if(session.Blocks.Hand.Cards[i].displayName=="L") session.Blocks.SelectCard(i);
            Require(session.config.waves[0].Total==5 && session.config.waves[1].Total==8 && session.config.waves[2].Total==12,"Configured 5 / 8 / 12 initial enemies");
            Require(session.config.towers.Length==4 && session.config.rewards.Length==14,"Four towers and fourteen reward assets (incl. ExtraDraw)");
            Require(System.Array.Exists(session.config.rewards,r=>r!=null && r.effect==RewardEffect.ExtraDraw && r.tier==RewardTier.Rare),"ExtraDraw reward tier is Rare (精良)");
            // Sealing the core: every empty ring cell around the core at once must be rejected.
            var ring=new List<Vector2Int>();
            foreach(var c in grid.CoreCells) foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                if(grid.CanPlace(c+d) && !ring.Contains(c+d)) ring.Add(c+d);
            Require(ring.Count>0 && session.Validator.ValidatePlacement(ring)!=null,"Players cannot seal the core");
            Require(grid.CoreCells.Count==(session.config.layout!=null ? session.config.layout.coreSize.x*session.config.layout.coreSize.y : 1),"Core footprint matches layout");
            session.Blocks.Preview(new Vector2Int(grid.width-1,grid.height-1));
            Require(!session.Blocks.PreviewValid,"Out-of-bounds ghost invalid");
            yield return Capture("01-board");
            int routeBefore=TotalRoute();
            bool detour=false;
            for(int r=0;r<4 && !detour;r++)
            {
                for(int x=0;x<grid.width && !detour;x++) for(int y=0;y<grid.height && !detour;y++)
                {
                    var anchor=new Vector2Int(x,y); var cells=session.Blocks.CellsAt(anchor);
                    if(session.Blocks.ValidatePlacement(anchor)!=null || !cells.Exists(OnAnyRoute)) continue;
                    detour=session.Blocks.CommitPlacement(anchor);
                }
                if(!detour) session.Blocks.Rotate();
            }
            Require(detour && TotalRoute()>routeBefore && session.Paths.AllSpawnsReachCore(),"Legal L placement creates a detour and keeps every spawn connected");
            Require(TryTower(0,false),"Needle built");
            Require(TryTower(1,false),"Pulse built");
            Require(session.Economy.Gold==80,"Tower costs deducted exactly once");
            Require(!session.Towers.TryBuild(grid.spawn,0) && !session.Towers.TryBuild(grid.goal,0) && session.Economy.Gold==80,"Protected tower placement does not spend gold");
            Require(session.Towers.Towers.Count==2 && session.Towers.Towers[0].Cells.Count==1,"1x1 tower occupies one wall cell");
            yield return Capture("02-build");
            int[] spawnCounts=new int[3], childCounts=new int[3]; int killed=0;
            session.Enemies.ChildrenAdded+=n=>{if(session.Waves.WaveIndex<3) childCounts[session.Waves.WaveIndex]+=n;};
            session.Enemies.Spawned+=(enemy) => { if(session.Waves.WaveIndex<3) spawnCounts[session.Waves.WaveIndex]++; };
            session.Enemies.Resolved+=(enemy,reason) => { if(reason==EnemyResolution.Killed) killed++; };
            for(int wave=0;wave<3;wave++)
            {
                if(wave==1)
                {
                    Require(session.Economy.Gold>=100 && TryTower(2,false),"Seismic built with earned gold");
                    session.Towers.Select(-1);
                }
                if(wave==2) {
                    Require(TryTower(3,false),"Chill beacon built for third wave");
                    session.Towers.Select(3); yield return Capture("05-chill-selection"); session.Towers.Select(-1);
                }
                int goldBefore=session.Economy.Gold;
                Require(session.Waves.StartWave() && !session.Waves.StartWave(),"Wave starts once from Build");
                if(wave==0)
                {
                    Enemy live=session.Enemies.Active[0]; Vector3 before=live.transform.position;
                    var animator=live.GetComponentInChildren<Animator>();
                    Require(animator!=null && animator.runtimeAnimatorController!=null && animator.runtimeAnimatorController.animationClips.Length>=2,"Enemy prefab carries locomotion and death animation");
                    Require(session.Validator.ValidatePlacement(new[] { live.NavigationAnchor })!=null,"Live movement edge protected");
                    bool placed=false;
                    for(int x=0;x<grid.width && !placed;x++) for(int y=0;y<grid.height && !placed;y++)
                    {
                        var anchor=new Vector2Int(x,y); var cells=session.Blocks.CellsAt(anchor);
                        if(session.Blocks.ValidatePlacement(anchor)!=null || !cells.Exists(OnAnyRoute)) continue;
                        placed=session.Blocks.CommitPlacement(anchor);
                    }
                    Require(placed && live.transform.position==before,"Live wall placement repaths without teleport");
                    yield return new WaitForSecondsRealtime(1.3f);
                    yield return Capture("03-combat");
                }
                TimeController.SetSpeed(4);
                float deadline=Time.realtimeSinceStartup+90;
                float tick=Time.realtimeSinceStartup+10;
                while(session.Game.State==GameState.Combat && Time.realtimeSinceStartup<deadline)
                {
                    if(Time.realtimeSinceStartup>tick) { tick+=10; Debug.Log("SMOKE TICK wave="+(wave+1)+" timeScale="+Time.timeScale+" hitStop="+StoneSignal.VFX.HitStop.Active+" remaining="+session.Waves.Remaining+" "+(session.Enemies.Active.Count>0?session.Enemies.Active[0].DebugState:"")); }
                    yield return null;
                }
                TimeController.SetSpeed(1);
                Require(session.Game.State==GameState.Reward && session.Waves.Remaining==0 && session.Enemies.Active.Count==0,"Wave "+(wave+1)+" completed through real combat (state="+session.Game.State+" remaining="+session.Waves.Remaining+" active="+session.Enemies.Active.Count+" timeScale="+Time.timeScale+" stuckEvents="+session.Enemies.StuckEvents+" "+(session.Enemies.Active.Count>0?session.Enemies.Active[0].DebugState:"")+")");
                Require(spawnCounts[wave]==session.config.waves[wave].Total+childCounts[wave],"Wave "+(wave+1)+" exact spawn count");
                Require(session.Economy.Gold>goldBefore,"Wave "+(wave+1)+" kills earn gold");
                { var rc=session.Rewards.Choices; var rr=session.Rewards.RuneChoices; var seenR=new HashSet<RewardData>(); var seenRune=new HashSet<int>(); bool uniq=rc.Count==3 && rr.Count==3; string desc="";
                  for(int i=0;i<rc.Count && i<rr.Count;i++){ if(rr[i]!=RuneRules.NoRune){ uniq&=rc[i]==null && seenRune.Add(rr[i]); desc+=" rune"+rr[i]; } else { uniq&=rc[i]!=null && seenR.Add(rc[i]); desc+=" "+(rc[i]!=null?rc[i].effect+"("+rc[i].tier+")":"null"); } }
                  Require(uniq,"Three unique reward options (weighted roll):"+desc); }
                Require(!session.Waves.StartWave(),"Combat cannot start during Reward");
                if(wave==0) yield return Capture("04-rewards");
                Require(session.Rewards.Choose(0) && !session.Rewards.Choose(0),"Only one reward can be selected");
                while(session.Blocks.Remaining>1) session.Blocks.Hand.Consume(); // hand carries across waves (cap 7): start the draw checks from 1 card
                int handBefore=session.Blocks.Remaining;
                Require(session.Game.State==GameState.Build && session.Draws.Next==DrawRules.Offer.Free,"Intermission offers one FREE draw");
                Require(session.Draw() && session.Blocks.Remaining==System.Math.Min(BlockHandManager.MaxCards,handBefore+GameBootstrap.CardsPerDraw),"Free DRAW adds blocks");
                bool freeSecond=session.Draws.FreeSecond; // ExtraDraw picked as this wave's reward: the 2nd draw is free instead of an ad
                Require(session.Draws.Next==(freeSecond?DrawRules.Offer.Free:DrawRules.Offer.Ad) && session.Draw() && session.Blocks.Remaining==BlockHandManager.MaxCards && session.Draws.Next==DrawRules.Offer.None && Time.timeScale>0,"2nd DRAW via "+(freeSecond?"ExtraDraw (free)":"placeholder ad")+" (hand reaches cap 7); ad pause released");
                Require(!session.Draw(),"No 3rd DRAW");
            }
            Require(killed>0 && session.Economy.HP>0,"Three waves survived with automatic tower combat");
            Require(session.Enemies.StuckEvents==0,"No ENEMY STUCK events (guard is only a fallback)");
            { var fit=CameraFit.Instance; Require(fit!=null && fit.Fits>0 && fit.Size>=fit.BaseSize && !fit.Report.Contains("FALLBACK"),"Camera fit: board + entry bridges inside the safe area, clear of the HUD (ortho "+(fit!=null?fit.Size.ToString("F2")+" pan "+fit.Pan.ToString("F2"):"-")+")"); }
            yield return TouchDragCheck();
            AdsAndExtraDrawCheck();
            yield return Capture("05-next-build");
            int hp=session.Economy.HP;
            EnemyData leak=session.config.waves[0].groups[0].enemy;
            for(int i=0;i<(hp+leak.damageToBase-1)/leak.damageToBase;i++) session.Enemies.Spawn(leak).Advance(100);
            Require(session.Game.State==GameState.GameOver && session.Economy.HP==0,"Base exhaustion reaches GameOver");
            Require(!session.Waves.StartWave() && !session.Blocks.CommitPlacement(new Vector2Int(1,1)),"GameOver blocks gameplay actions");
            yield return Capture("06-game-over");
            Finish();
        }
        private int TotalRoute() { int n=0; foreach(var p in session.Paths.CurrentPaths) n+=p.Count; return n; }
        private bool OnAnyRoute(Vector2Int cell) => session.Paths.CurrentPaths.Exists(p=>p.Contains(cell));
        // Towers go near a route (within range) on a cell that keeps every spawn connected.
        // Towers stand on walls: pick a footprint of empty cells that keeps every route open, wall it, then build.
        private bool TryTower(int index,bool onRoute)
        {
            var grid=session.grid; var core=grid.CoreCenter; var size=TowerManager.SizeOf(session.config.towers[index],0);
            Vector2Int best=new Vector2Int(-1,-1); float bestScore=float.MaxValue;
            for(int x=0;x<grid.width;x++) for(int y=0;y<grid.height;y++)
            {
                var o=new Vector2Int(x,y); var cells=grid.Footprint(o,size);
                if(!cells.TrueForAll(grid.CanPlace) || cells.Exists(OnAnyRoute)!=onRoute || session.Validator.ValidatePlacement(cells)!=null) continue;
                float score=(grid.FootprintCenter(o,size)-core).sqrMagnitude;
                if(score<bestScore && score>1.5f) { bestScore=score; best=o; }
            }
            if(best.x<0) return false;
            grid.Commit(grid.Footprint(best,size),CellState.Blocked);
            return session.Towers.TryBuild(best,index,0);
        }
        // Scripted touch drag: finger down on tower card 0, drag above a prepared wall, release -> one tower, gold spent once.
        private IEnumerator TouchDragCheck()
        {
            var pic=PlacementInputController.Instance; var grid=session.grid;
            if(pic==null){ Require(false,"Touch drag: placement input controller present"); yield break; }
            var size=TowerManager.SizeOf(session.config.towers[0],0); Vector2Int best=new Vector2Int(-1,-1);
            for(int x=0;x<grid.width && best.x<0;x++) for(int y=0;y<grid.height && best.x<0;y++)
            {
                var o=new Vector2Int(x,y); var cells=grid.Footprint(o,size);
                if(cells.TrueForAll(grid.CanPlace) && !cells.Exists(OnAnyRoute) && session.Validator.ValidatePlacement(cells)==null && Camera.main.WorldToScreenPoint(grid.FootprintCenter(o,size)).y>pic.HandTop+pic.DragOffset*pic.CanvasScale+30) best=o;
            }
            if(best.x<0){ Require(false,"Touch drag: found a wall spot"); yield break; }
            grid.Commit(grid.Footprint(best,size),CellState.Blocked);
            session.Economy.AddGold(session.Towers.Cost(session.config.towers[0]));
            int towersBefore=session.Towers.Towers.Count, goldBefore=session.Economy.Gold;
            bool force=PointerInput.ForceTouch; PointerInput.ForceTouch=true;
            Vector2 target=Camera.main.WorldToScreenPoint(grid.FootprintCenter(best,size)+Vector3.up*grid.wallTop); // aim at the visible wall top
            Vector2 finger=target-Vector2.up*pic.DragOffset*pic.CanvasScale; // ghost sits above the finger
            Vector2 start=new Vector2(target.x,10), cardLow=new Vector2(target.x,5), cardHigh=new Vector2(target.x,15);
            foreach(var rt in FindObjectsOfType<RectTransform>()) if(rt.name=="Tower card "+session.config.towers[0].displayName){ var k=new Vector3[4]; rt.GetWorldCorners(k); start=(k[0]+k[2])*.5f; cardLow=new Vector2(start.x,Mathf.Lerp(k[0].y,k[1].y,.25f)); cardHigh=new Vector2(start.x,Mathf.Lerp(k[0].y,k[1].y,.55f)); break; } // finger starts on the card
            PointerInput.Inject(start,true,true,false); pic.CardDown(true,0,start); yield return null;
            for(int i=1;i<=6;i++){ PointerInput.Inject(Vector2.Lerp(start,finger,i/6f),false,true,false); yield return null; }
            bool dragging=pic.Machine.State==PlacementState.Dragging;
            string dbg="best="+best+" target="+target+" finger="+finger+" handTop="+pic.HandTop+" scale="+pic.CanvasScale+" state="+pic.Machine.State+" sel="+session.Towers.SelectedIndex+" reason="+session.Towers.ReasonFor(best,0)+" gameState="+session.Game.State;
            PointerInput.Inject(finger,false,false,true); yield return null; yield return null;
            PointerInput.ClearInjection(); PointerInput.ForceTouch=force;
            Require(dragging && session.Towers.Towers.Count==towersBefore+1 && session.Economy.Gold==goldBefore-session.Towers.Cost(session.config.towers[0]) && pic.Machine.State==PlacementState.Idle,"Scripted touch drag places a tower on a wall (ghost offset above finger)"+(dragging && session.Towers.Towers.Count==towersBefore+1?"":" ["+dbg+" after="+pic.Machine.State+" towers="+session.Towers.Towers.Count+"/"+towersBefore+"]"));
            // drag back over the hand cancels
            towersBefore=session.Towers.Towers.Count; PointerInput.ForceTouch=true;
            PointerInput.Inject(start,true,true,false); pic.CardDown(true,0,start); yield return null;
            PointerInput.Inject(finger,false,true,false); yield return null;
            PointerInput.Inject(cardLow,false,true,false); yield return null; // back onto the lower half of the card (cancel zone)
            PointerInput.Inject(cardLow,false,false,true); yield return null; yield return null;
            PointerInput.ClearInjection(); PointerInput.ForceTouch=force;
            Require(session.Towers.Towers.Count==towersBefore && session.Towers.SelectedIndex<0,"Touch drag released on the lower half of a hand card cancels");
            // v18.3 hand pass-through: plain DragOffset everywhere (no lift over the cards); cancel zone = lower half of the bottom (tower) row only,
            // a block card's lower half is NOT a cancel zone (cells under the block row stay reachable)
            float baseOff=pic.DragOffset*pic.CanvasScale; Vector2 blockLow=new Vector2(-1,-1);
            foreach(var rt in FindObjectsOfType<RectTransform>()) if(rt.name.StartsWith("Block card")&&rt.gameObject.activeInHierarchy){ var k=new Vector3[4]; rt.GetWorldCorners(k); blockLow=new Vector2((k[0].x+k[2].x)*.5f,Mathf.Lerp(k[0].y,k[1].y,.25f)); break; }
            Require(pic.HandPassThrough && pic.InCancelZone(cardLow) && !pic.InCancelZone(cardHigh) && (blockLow.x<0||!pic.InCancelZone(blockLow)) && Mathf.Abs(pic.OffsetFor(cardHigh)-baseOff)<.5f && Mathf.Abs(pic.OffsetFor(finger)-baseOff)<.5f,
                "Hand pass-through: base drag offset over the hand (no lift); cancel = lower half of a tower card only, block card lower half not a cancel zone"+(blockLow.x<0?" (no block card on screen)":""));
        }
        sealed class NoAds : IAdService { public bool IsReady => false; public void ShowRewarded(string placement, System.Action<bool> completed) => completed?.Invoke(false); }
        // 玩法策划 v18: ads unavailable -> the 2nd draw exists only via ExtraDraw (免广告再抽); ExtraDraw while the 2nd draw is used carries over.
        private void AdsAndExtraDrawCheck()
        {
            var ads=AdServices.Current; AdServices.Current=new NoAds(); var hand=session.Blocks.Hand;
            try
            {
                session.Modifiers.ExtraDraw=0; session.BeginIntermission(); while(hand.Cards.Count>1) hand.Consume();
                bool first=session.Draw(); int n=hand.Cards.Count; while(hand.Cards.Count>1) hand.Consume(); n=hand.Cards.Count;
                Require(first && session.Draws.Next==DrawRules.Offer.Ad && session.DrawOffer==DrawRules.Offer.None && !session.Draw() && hand.Cards.Count==n,"Ads unavailable: free 1st draw only, the ad 2nd draw is not offered (pile shows used)");
                bool now=session.GrantExtraDraw();
                Require(now && session.DrawOffer==DrawRules.Offer.Free && session.Draw() && session.Draws.Next==DrawRules.Offer.None,"Ads unavailable: ExtraDraw during Build makes this wave's 2nd draw free");
                while(hand.Cards.Count>1) hand.Consume();
                bool carried=!session.GrantExtraDraw() && session.Modifiers.ExtraDraw==1 && session.DrawOffer==DrawRules.Offer.None;
                session.BeginIntermission(); bool f1=session.Draw(); while(hand.Cards.Count>1) hand.Consume();
                Require(carried && f1 && session.Modifiers.ExtraDraw==0 && session.Draws.FreeSecond && session.DrawOffer==DrawRules.Offer.Free && session.Draw(),"ExtraDraw after the 2nd draw was used carries over: next wave's 2nd draw is free (no ad)");
            }
            finally { AdServices.Current=ads; session.Modifiers.ExtraDraw=0; session.BeginIntermission(); }
        }
        private void Finish()
        {
            TimeController.SetSpeed(1);
            results.Add("CHECKS: "+checks+"; RESULT: "+(failed?"FAIL":"PASS"));
            File.WriteAllLines(Path.Combine(captureDirectory,"play-verification.txt"),results);
            Debug.Log(failed?"PROTOTYPE SMOKE FAILED":"PROTOTYPE SMOKE PASSED");
            Application.Quit(failed?1:0);
        }
        private void OnDestroy() { Application.logMessageReceived-=OnLog; }
    }
}
#endif
