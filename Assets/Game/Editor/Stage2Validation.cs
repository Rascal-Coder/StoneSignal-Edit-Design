using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StoneSignal;
public static class Stage2Validation {
 static T Load<T>(string folder,string name) where T:UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/ScriptableObjects/"+folder+"/"+name+".asset");
 [MenuItem("StoneSignal/Migrate stage 2 asset references")]
 public static void Migrate() {
  AssetDatabase.Refresh();
  var config=AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Game/Settings/GameConfig.asset");
  config.towers=new[]{Load<TowerData>("Towers","Needle"),Load<TowerData>("Towers","Pulse"),Load<TowerData>("Towers","Seismic"),Load<TowerData>("Towers","Chill")};
  string[] names={"AllDamage","AllAttackSpeed","AllRange","BaseHP","CannonRadius","AddBlock","NextDraw","PathSlow","BonusSlot","KillGold","WaveGold","TowerDiscount","WaveHeal","ExtraDraw"};
  config.rewards=Array.ConvertAll(names,n=>Load<RewardData>("Rewards",n));
  var normal=Load<EnemyData>("Enemies","Drifter"); // v19: the winged fast enemy was retired to _Deprecated; each N of it became ceil(1.2N) Drifters. Shard keeps EnemyKind.Fast
  var tank=Load<EnemyData>("Enemies","Bulwark"); var splitter=Load<EnemyData>("Enemies","Splitter"); splitter.splitChild=Load<EnemyData>("Enemies","Shard");
  Load<RewardData>("Rewards","AddBlock").blockShape=Load<BlockShapeData>("Blocks","O");
  Load<RewardData>("Rewards","BonusSlot").blockShape=Load<BlockShapeData>("Blocks","L");
  config.waves[1].groups=new[]{new EnemyGroup{enemy=normal,count=9}};
  config.waves[2].groups=new[]{new EnemyGroup{enemy=normal,count=9},new EnemyGroup{enemy=tank,count=2},new EnemyGroup{enemy=splitter,count=2}};
  foreach(var item in config.rewards) { PrototypeChecks.Require(item!=null,"Reward asset"); EditorUtility.SetDirty(item); }
  foreach(var item in config.towers) PrototypeChecks.Require(item!=null,"Tower asset");
  EditorUtility.SetDirty(config); EditorUtility.SetDirty(splitter);
  foreach(var wave in config.waves) EditorUtility.SetDirty(wave);
  AssetDatabase.SaveAssets();
  Run();
 }
 [MenuItem("StoneSignal/Run stage 2 checks")]
 public static void Run() {
  var config=AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Game/Settings/GameConfig.asset");
  PrototypeBuild.EnsureRewardPool(); // v18: ExtraDraw (免广告再抽) in the pool before the 14-reward checks
  EditorSceneManager.OpenScene("Assets/Game/Scenes/Game.unity");
  PrototypeBuild.Checks();
  CheckGameplay(config);
  Debug.Log("STAGE 2 CHECKS PASS");
 }
 static void CheckGameplay(GameConfig config) {
  var deck=new BlockDeckManager(config.blocks);var hand=new BlockHandManager(); hand.Draw(deck,5);
  PrototypeChecks.Require(new System.Collections.Generic.HashSet<BlockShapeData>(hand.Cards).Count==5,"Five draws exhaust unique initial deck");
  hand.Select(3);var card=hand.Current;hand.Consume();PrototypeChecks.Require(hand.Cards.Count==4 && !new System.Collections.Generic.List<BlockShapeData>(hand.Cards).Contains(card),"Consume selected card");
  deck.Add(config.blocks[0]);PrototypeChecks.Require(deck.Cards.Count==6,"Deck expansion");
  var root=new GameObject("Stage2 checks");var palette=ScriptableObject.CreateInstance<VisualPalette>();
  try {
   var grid=root.AddComponent<GridManager>();grid.Initialize();var paths=root.AddComponent<PathfindingManager>();paths.Initialize(grid);
   var enemies=root.AddComponent<EnemyManager>();enemies.Initialize(grid,paths,palette,()=>true);
   var mods=new RunModifiers();var economy=root.AddComponent<RunEconomy>();economy.Initialize(config,enemies,mods);
   foreach(var reward in config.rewards) if(reward.effect!=RewardEffect.AddBlock) reward.Apply(mods,economy);
   PrototypeChecks.Require(Mathf.Approximately(mods.AllRange,1.15f) && Mathf.Approximately(mods.AttackSpeed,1.12f) && mods.WaveGold==30 && mods.WaveHeal==2 && mods.NextDraw==1 && mods.ExtraDraw==1,"New reward effects (ExtraDraw = 1 pending free 2nd draw)");
   PrototypeChecks.Require(Mathf.Approximately(mods.EnemySpeed,.92f) && Mathf.Approximately(mods.KillGold,1.2f) && Mathf.Approximately(mods.TowerCost,.9f) && mods.BonusSlotShape!=null,"Path/economy/platform reward data");
   var blocks=root.AddComponent<BlockPlacementManager>();blocks.Initialize(grid,null,palette,config.blocks,()=>true);blocks.Modifiers=mods;blocks.ValidateAdditional=cells=>{
       var simulated=new System.Collections.Generic.HashSet<Vector2Int>(cells);
       return paths.FindPath(grid.spawn,grid.goal,simulated).Count==0 ? "No route" : null;
   };
   int extraBefore=mods.ExtraDraw; Load<RewardData>("Rewards","AddBlock").Apply(mods,economy,blocks);
   PrototypeChecks.Require(blocks.Deck.Cards.Count==6 && mods.ExtraDraw==extraBefore,"AddBlock adds one O wall card (no draw-size / free-draw side effect)");
   blocks.Refill(5);for(int i=0;i<blocks.Hand.Cards.Count;i++) if(blocks.Hand.Cards[i]==mods.BonusSlotShape) {blocks.SelectCard(i);break;}
   if(blocks.CurrentShape!=mods.BonusSlotShape) {blocks.Deck.Add(mods.BonusSlotShape);blocks.Refill(7);for(int i=0;i<blocks.Hand.Cards.Count;i++) if(blocks.Hand.Cards[i]==mods.BonusSlotShape){blocks.SelectCard(i);break;}}
   PrototypeChecks.Require(blocks.CommitPlacement(new Vector2Int(5,0)) && grid.Get(new Vector2Int(6,1))==CellState.Blocked,"L reward adds validated adjacent platform");
   int remaining=1;enemies.ChildrenAdded+=n=>remaining+=n;enemies.Resolved+=(e,r)=>remaining--;
   var parent=enemies.Spawn(Load<EnemyData>("Enemies","Splitter"),1.2f);parent.Advance(.3f);var pos=parent.transform.position;parent.TakeDamage(1000);
   PrototypeChecks.Require(remaining==2 && enemies.Active.Count==2,"Splitter reserves children before parent settles");
   foreach(var child in enemies.Active) PrototypeChecks.Require(child.transform.position==pos && Mathf.Approximately(child.HP,14.4f),"Child position and HP scale");
   enemies.DamageArea(pos,3,1000);PrototypeChecks.Require(remaining==0 && enemies.Active.Count==0,"Children settle exactly once");
   var slowed=enemies.Spawn(config.waves[0].groups[0].enemy);slowed.ApplySlow(.35f,2);slowed.Advance(1);
   PrototypeChecks.Require(Mathf.Abs(Vector3.Distance(grid.ToWorld(grid.spawn)+Vector3.up*.45f,slowed.transform.position)-.8125f)<.001f,"Chill movement slow");
  } finally { UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(palette); }
 }
 public static void Build() { Run(); PrototypeBuild.Build(); }
}