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
            UnityEngine.Random.InitState(seedIndex>=0 && seedIndex+1<args.Length ? int.Parse(args[seedIndex+1]) : 37);
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
                Require(session.config.palette.art.block!=null && session.config.palette.art.spawnPortal!=null,"Art catalog environment assigned");
                Require(session.config.palette.art.tile!=null && session.config.palette.art.tilePath!=null && session.config.palette.art.tileGoal!=null,"Board tile, road and goal assigned");
                foreach(var tower in session.config.towers) Require(tower.visualPrefab!=null && tower.icon!=null,"Tower model and icon "+tower.displayName);
                Require(session.GetComponentsInChildren<BoardEnvironment>().Length==1,"Board decoration initialized");
            }
            Require(session.Paths.CurrentPath.Count==16 && session.Game.State==GameState.Build,"Initial scene, route and Build state");
            session.Blocks.Refill(5);
            for(int i=0;i<5;i++) if(session.Blocks.Hand.Cards[i].displayName=="L") session.Blocks.SelectCard(i);
            Require(session.config.waves[0].Total==5 && session.config.waves[1].Total==8 && session.config.waves[2].Total==12,"Configured 5 / 8 / 12 initial enemies");
            Require(session.config.towers.Length==4 && session.config.rewards.Length==13,"Four towers and thirteen reward assets");
            var barrier=new List<Vector2Int>();
            for(int y=0;y<10;y++) if(y<4 || y>6) barrier.Add(new Vector2Int(8,y));
            session.grid.Commit(barrier,CellState.Blocked);
            var testVisuals=new GameObject("Validation test wall");
            foreach(var cell in barrier) PrimitiveVisual.Create("Test wall",PrimitiveType.Cube,testVisuals.transform,session.grid.ToWorld(cell)+Vector3.up*.3f,new Vector3(.9f,.6f,.9f),session.config.palette.wall);
            session.Blocks.Preview(new Vector2Int(8,4));
            Require(!session.Blocks.PreviewValid && !session.Blocks.CommitPlacement(new Vector2Int(8,4)),"L cannot seal the last three-cell passage");
            Require(session.grid.Get(new Vector2Int(8,4))==CellState.Empty && session.Blocks.Remaining==5,"Failed placement is side-effect free");
            yield return Capture("01-rejected-route");
            Destroy(testVisuals); session.grid.Initialize(); session.Paths.Recalculate();
            session.Blocks.Preview(new Vector2Int(15,9));
            Require(!session.Blocks.PreviewValid,"Out-of-bounds ghost invalid");
            session.Blocks.Rotate(); session.Blocks.Preview(new Vector2Int(6,3));
            Require(session.Blocks.PreviewValid,"Rotated L ghost valid");
            session.Blocks.Rotate(); session.Blocks.Rotate(); session.Blocks.Rotate();
            Require(session.Blocks.CommitPlacement(new Vector2Int(6,3)) && session.Paths.CurrentPath.Count>16,"Legal L placement creates detour");
            Require(session.Towers.TryBuild(new Vector2Int(3,3),0),"Arrow beacon built");
            Require(session.Towers.TryBuild(new Vector2Int(9,3),1),"Rapid beacon built");
            Require(session.Economy.Gold==80,"Tower costs deducted exactly once");
            Require(!session.Towers.TryBuild(session.grid.spawn,0) && session.Economy.Gold==80,"Protected tower placement does not spend gold");
            session.Blocks.Preview(new Vector2Int(1,7));
            yield return Capture("02-build");
            int[] spawnCounts=new int[3], childCounts=new int[3]; int killed=0;
            session.Enemies.ChildrenAdded+=n=>{if(session.Waves.WaveIndex<3) childCounts[session.Waves.WaveIndex]+=n;};
            session.Enemies.Spawned+=(enemy) => { if(session.Waves.WaveIndex<3) spawnCounts[session.Waves.WaveIndex]++; };
            session.Enemies.Resolved+=(enemy,reason) => { if(reason==EnemyResolution.Killed) killed++; };
            for(int wave=0;wave<3;wave++)
            {
                if(wave==1)
                {
                    Require(session.Economy.Gold>=100 && session.Towers.TryBuild(new Vector2Int(9,5),2),"Cannon built with earned gold");
                    session.Towers.Select(-1);
                }
                if(wave==2) {
                    Require(session.Towers.TryBuild(new Vector2Int(5,5),3),"Chill beacon built for third wave");
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
                    for(int x=8;x<13 && !placed;x++) for(int y=0;y<7 && !placed;y++)
                    {
                        var anchor=new Vector2Int(x,y); var cells=session.Blocks.CellsAt(anchor);
                        if(session.Blocks.ValidatePlacement(anchor)!=null || !cells.Exists(p=>session.Paths.CurrentPath.Contains(p))) continue;
                        placed=session.Blocks.CommitPlacement(anchor);
                    }
                    Require(placed && live.transform.position==before,"Live wall placement repaths without teleport");
                    yield return new WaitForSecondsRealtime(1.3f);
                    yield return Capture("03-combat");
                }
                Time.timeScale=4;
                float deadline=Time.realtimeSinceStartup+45;
                while(session.Game.State==GameState.Combat && Time.realtimeSinceStartup<deadline) yield return null;
                Time.timeScale=1;
                Require(session.Game.State==GameState.Reward && session.Waves.Remaining==0 && session.Enemies.Active.Count==0,"Wave "+(wave+1)+" completed through real combat");
                Require(spawnCounts[wave]==session.config.waves[wave].Total+childCounts[wave],"Wave "+(wave+1)+" exact spawn count");
                Require(session.Economy.Gold>goldBefore,"Wave "+(wave+1)+" kills earn gold");
                Require(session.Rewards.Choices.Count==3 && new HashSet<RewardData>(session.Rewards.Choices).Count==3,"Three unique reward cards");
                Require(!session.Waves.StartWave(),"Combat cannot start during Reward");
                if(wave==0) yield return Capture("04-rewards");
                Require(session.Rewards.Choose(0) && !session.Rewards.Choose(0),"Only one reward can be selected");
                Require(session.Game.State==GameState.Build && session.Blocks.Remaining>=session.config.blocksPerBuild,"Next Build refills blocks");
            }
            Require(killed>0 && session.Economy.HP>0,"Three waves survived with automatic tower combat");
            yield return Capture("05-next-build");
            int hp=session.Economy.HP;
            EnemyData leak=session.config.waves[0].groups[0].enemy;
            for(int i=0;i<(hp+leak.damageToBase-1)/leak.damageToBase;i++) session.Enemies.Spawn(leak).Advance(100);
            Require(session.Game.State==GameState.GameOver && session.Economy.HP==0,"Base exhaustion reaches GameOver");
            Require(!session.Waves.StartWave() && !session.Blocks.CommitPlacement(new Vector2Int(1,8)),"GameOver blocks gameplay actions");
            yield return Capture("06-game-over");
            Finish();
        }
        private void Finish()
        {
            Time.timeScale=1;
            results.Add("CHECKS: "+checks+"; RESULT: "+(failed?"FAIL":"PASS"));
            File.WriteAllLines(Path.Combine(captureDirectory,"play-verification.txt"),results);
            Debug.Log(failed?"PROTOTYPE SMOKE FAILED":"PROTOTYPE SMOKE PASSED");
            Application.Quit(failed?1:0);
        }
        private void OnDestroy() { Application.logMessageReceived-=OnLog; }
    }
}
#endif
