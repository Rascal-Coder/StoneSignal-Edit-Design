using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace StoneSignal
{
    public sealed class GameUI : MonoBehaviour
    {
        private GameBootstrap session;
        private Font font;
        private Text hp, wave, remaining, gold, phase, block, status, selection, upgrades;
        private Text[] towerLabels = new Text[4];
        private Button[] towerButtons = new Button[4];
        private Button start, rotate;
        private RectTransform handRoot;
        private readonly System.Collections.Generic.List<GameObject> handCards = new System.Collections.Generic.List<GameObject>();
        private GameObject rewardPanel, overPanel;
        private Text[] rewardNames = new Text[3], rewardDescriptions = new Text[3], rewardEffects = new Text[3];
        private float noticeUntil, refreshAt;
        private string notice;
        private readonly Color ink = new Color(.86f,.94f,.94f);
        private readonly Color muted = new Color(.48f,.65f,.69f);
        private readonly Color accent = new Color(.39f,.91f,.79f);

        public void Initialize(GameBootstrap game)
        {
            session = game; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (EventSystem.current == null) { var es = new GameObject("Event system"); es.transform.SetParent(transform); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }
            var canvasObject = new GameObject("HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600,900); scaler.matchWidthOrHeight = .5f;
            RectTransform root = canvasObject.GetComponent<RectTransform>();
            var header = Panel(root,"Header",new Color(.025f,.055f,.085f,.98f)); Stretch(header,0,1,1,1,new Vector2(0,96),new Vector2(0,0),new Vector2(.5f,1));
            Label(header,"STONE  /  SIGNAL",26,accent,24,18,320,32);
            Label(header,"RUINS OF THE LIVING SIGNAL",13,muted,24,54,330,24);
            hp = Label(header,"",24,ink,460,20,210,32);
            wave = Label(header,"",24,ink,720,20,210,32);
            remaining = Label(header,"",24,ink,945,20,250,32);
            gold = Label(header,"",26,new Color(1,.79f,.38f),1300,20,280,36);
            Label(header,"CORE INTEGRITY",11,muted,460,56,210,20);
            Label(header,"CURRENT WAVE",11,muted,720,56,210,20);
            Label(header,"INCLUDING UNSPAWNED",11,muted,945,56,270,20);
            phase = Label(root,"",16,accent,26,116,650,28);
            var tools = Panel(root,"Tower tools",new Color(.035f,.075f,.11f,.97f)); TopRight(tools,-288,-116,270,640);
            Label(tools,"DEFENSE BEACONS",16,accent,18,18,230,28);
            Label(tools,"Select a tower, then click a cell.\nWalls can host a tower.",14,muted,18,54,232,52);
            for (int i=0; i<session.config.towers.Length; i++)
            {
                int captured = i; TowerData data = session.config.towers[i];
                towerButtons[i] = MakeButton(tools,"",18,18,115+i*85,234,78,() => session.Towers.Select(captured));
                towerLabels[i] = towerButtons[i].GetComponentInChildren<Text>();
                towerLabels[i].fontSize=13;
                if(data.icon!=null) {
                    var icon=Panel(towerButtons[i].transform,"Model icon",Color.white);Position(icon,6,8,60,60);
                    icon.GetComponent<Image>().sprite=data.icon;icon.GetComponent<Image>().preserveAspect=true;icon.GetComponent<Image>().raycastTarget=false;
                    Position(towerLabels[i].rectTransform,72,6,150,66);
                }
                towerLabels[i].text = (i+1) + "  " + data.displayName + "\n" + data.cost + " gold  |  " + data.damage + " dmg  |  " + data.attacksPerSecond.ToString("0.0") + "/s";
            }
            selection = Label(tools,"",16,ink,18,464,234,62);
            MakeButton(tools,"B  /  WALL BLOCK",15,18,536,234,30,() => session.Towers.Select(-1));
            upgrades = Label(tools,"",13,muted,18,566,238,58);
            var footer = Panel(root,"Footer",new Color(.025f,.055f,.085f,.98f)); Stretch(footer,0,0,1,0,new Vector2(0,112),Vector2.zero,new Vector2(.5f,0));
            block = Label(footer,"",19,accent,24,15,380,30);
            Label(footer,"LMB place  |  R / RMB rotate  |  1-4 tower  |  B wall",14,muted,24,54,575,32);
            rotate = MakeButton(footer,"R  ROTATE",16,625,24,150,58,() => session.Blocks.Rotate());
            start = MakeButton(footer,"SPACE  /  BATTLE",18,800,24,300,58,() => session.Waves.StartWave());
            start.GetComponent<Image>().color=new Color(.025f,.30f,.38f);
            status = Label(footer,"",14,ink,1130,20,445,72);
            handRoot=Panel(root,"Block hand",new Color(.025f,.055f,.085f,.96f));
            handRoot.anchorMin=handRoot.anchorMax=new Vector2(0,0); handRoot.pivot=Vector2.zero; handRoot.anchoredPosition=new Vector2(24,118); handRoot.sizeDelta=new Vector2(900,68);
            BuildRewardPanel(root); BuildOverPanel(root);
            session.Economy.Changed += Refresh;
            session.Waves.Changed += Refresh;
            session.Blocks.Changed += Refresh;
            session.Towers.Changed += Refresh;
            session.Game.StateChanged += OnState;
            session.Rewards.Offered += ShowRewards;
            session.Blocks.Notice += ShowNotice; session.Towers.Notice += ShowNotice;
            Refresh();
        }
        private void BuildRewardPanel(RectTransform root)
        {
            var panel = Panel(root,"Three reward choices",new Color(.018f,.035f,.06f,.96f)); Fill(panel); rewardPanel=panel.gameObject;
            var title = Label(panel,"CHOOSE YOUR NEXT SIGNAL",32,accent,0,0,1050,50); Center(title.rectTransform,0,-245,1050,50); title.alignment=TextAnchor.MiddleCenter;
            var subtitle = Label(panel,"One upgrade. A new route. The next wave awaits.",17,muted,0,0,1000,35); Center(subtitle.rectTransform,0,-192,1000,35); subtitle.alignment=TextAnchor.MiddleCenter;
            for (int i=0; i<3; i++)
            {
                int captured=i;
                var card=Panel(panel,"Reward " + i,new Color(.05f,.105f,.14f)); Center(card,(i-1)*336,12,308,320);
                Label(card,"0"+(i+1)+"  /  RUN UPGRADE",13,accent,22,22,265,25);
                rewardNames[i]=Label(card,"",25,ink,22,65,265,70);
                rewardDescriptions[i]=Label(card,"",17,muted,22,140,265,65);
                rewardEffects[i]=Label(card,"",20,accent,22,210,265,40);
                MakeButton(card,"CHOOSE",17,22,264,264,38,() => session.Rewards.Choose(captured));
            }
            rewardPanel.SetActive(false);
        }
        private void BuildOverPanel(RectTransform root)
        {
            var panel=Panel(root,"Game over",new Color(.03f,.035f,.055f,.97f)); Fill(panel); overPanel=panel.gameObject;
            var text=Label(panel,"THE CORE WENT DARK",38,ink,0,0,800,70); Center(text.rectTransform,0,-85,800,70); text.alignment=TextAnchor.MiddleCenter;
            var tip=Label(panel,"Extend the route and spread your beacons along it.",19,muted,0,0,850,44); Center(tip.rectTransform,0,-10,850,44); tip.alignment=TextAnchor.MiddleCenter;
            var restart=MakeButton(panel,"RESTART RUN",19,0,0,270,60,() => { StoneSignal.VFX.HitStop.Cancel(); Time.timeScale=1; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }); Center((RectTransform)restart.transform,0,90,270,60);
            overPanel.SetActive(false);
        }
        private void Refresh()
        {
            hp.text=session.Economy.HP + " / " + session.Economy.MaxHP;
            wave.text=(session.Waves.WaveIndex+1).ToString("00"); remaining.text=session.Waves.Remaining.ToString("00"); gold.text=session.Economy.Gold + "  GOLD";
            phase.text=session.Game.State==GameState.Build ? "BUILD  /  Shape the route. Protect the core." : session.Game.State==GameState.Combat ? "COMBAT  /  Defend the core. Remaining includes queued enemies." : session.Game.State.ToString().ToUpper();
            block.text="BLOCK  " + (session.Blocks.CurrentShape == null ? "EMPTY" : session.Blocks.CurrentShape.displayName) + "   /   " + session.Blocks.Remaining + " LEFT";
            selection.text=session.Towers.SelectedIndex<0 ? "Selected: Wall block" : "Selected: " + session.config.towers[session.Towers.SelectedIndex].displayName;
            foreach(var card in handCards) Destroy(card); handCards.Clear();
            handRoot.sizeDelta=new Vector2(900,Mathf.Max(1,Mathf.CeilToInt(session.Blocks.Hand.Cards.Count/7f))*84);
            for(int i=0;i<session.Blocks.Hand.Cards.Count;i++) {
                int index=i; var shape=session.Blocks.Hand.Cards[i];
                var card=MakeButton(handRoot,shape.displayName,16,(i%7)*126,8+(i/7)*84,118,70,()=>{session.Towers.Select(-1);session.Blocks.SelectCard(index);});
                card.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleLeft;
                if(shape.icon!=null) {
                    var icon=Panel(card.transform,"Block model",Color.white);Position(icon,24,2,88,66);
                    icon.GetComponent<Image>().sprite=shape.icon;icon.GetComponent<Image>().preserveAspect=true;icon.GetComponent<Image>().raycastTarget=false;
                } else foreach(var cell in shape.cells) { var tile=Panel(card.transform,"Shape cell",ink); Position(tile,48+cell.x*10,6+(3-cell.y)*10,8,8); tile.GetComponent<Image>().raycastTarget=false; }
                card.GetComponent<Image>().color=i==session.Blocks.Hand.Selected && session.Towers.SelectedIndex<0 ? accent : new Color(.075f,.19f,.23f);
                if(i==session.Blocks.Hand.Selected && session.Towers.SelectedIndex<0) card.GetComponentInChildren<Text>().color=new Color(.025f,.055f,.085f);
                card.interactable=session.Game.State==GameState.Build || (session.config.allowCombatBlocks && session.Game.State==GameState.Combat);
                handCards.Add(card.gameObject);
            }
            bool build=session.Game.State==GameState.Build;
            start.interactable=build; rotate.interactable=session.Blocks.Remaining>0 && (build || (session.config.allowCombatBlocks && session.Game.State==GameState.Combat));
            for (int i=0;i<session.config.towers.Length;i++) {
                var data=session.config.towers[i]; towerButtons[i].interactable=build;
                towerButtons[i].GetComponent<Image>().color=session.Towers.SelectedIndex==i ? accent : new Color(.075f,.19f,.23f);
                towerLabels[i].color=session.Towers.SelectedIndex==i ? new Color(.025f,.055f,.085f) : ink;
                towerLabels[i].text=(i+1)+"  "+data.displayName+"\n"+session.Towers.Cost(data)+" gold | "+(data.damage*session.Modifiers.Damage).ToString("0.##")+" dmg | "+(data.attacksPerSecond*session.Modifiers.AttackSpeed).ToString("0.##")+"/s";
            }
            if(session.Towers.SelectedIndex>=0) selection.text=session.config.towers[session.Towers.SelectedIndex].role;
            upgrades.text="RUN: dmg x"+session.Modifiers.Damage.ToString("0.00")+"  |  rate x"+session.Modifiers.AttackSpeed.ToString("0.00")+"\nAll range x"+session.Modifiers.AllRange.ToString("0.00")+"  |  blast x"+session.Modifiers.CannonRadius.ToString("0.00");
            if (session.Game.State!=GameState.Reward) rewardPanel.SetActive(false);
            overPanel.SetActive(session.Game.State==GameState.GameOver);
        }
        private void ShowRewards()
        {
            for(int i=0;i<3;i++) { var reward=session.Rewards.Choices[i]; rewardNames[i].text=reward.displayName; rewardDescriptions[i].text=reward.description; rewardEffects[i].text=reward.effectText; }
            rewardPanel.SetActive(true); Refresh();
        }
        private void OnState(GameState state) { Refresh(); }
        private void ShowNotice(string message) { notice=message; noticeUntil=Time.unscaledTime+2.4f; }
        private void Update()
        {
            if (session==null || Time.unscaledTime<refreshAt) return;
            refreshAt=Time.unscaledTime+.1f;
            status.text=Time.unscaledTime<noticeUntil ? notice : session.Towers.SelectedIndex<0 ? (session.Blocks.Remaining<=0 ? "No blocks left. Start the next wave." : session.Blocks.Status) : session.Towers.Status;
        }
        private RectTransform Panel(Transform parent,string name,Color color)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Image)); obj.transform.SetParent(parent,false); obj.GetComponent<Image>().color=color; return obj.GetComponent<RectTransform>();
        }
        private Text Label(Transform parent,string value,int size,Color color,float x,float y,float width,float height=30)
        {
            var obj=new GameObject("Label",typeof(RectTransform),typeof(Text)); obj.transform.SetParent(parent,false);
            var text=obj.GetComponent<Text>(); text.font=font; text.fontSize=size; text.color=color; text.text=value; text.raycastTarget=false; text.verticalOverflow=VerticalWrapMode.Overflow;
            Position(text.rectTransform,x,y,width,height); return text;
        }
        private Button MakeButton(Transform parent,string label,int size,float x,float y,float width,float height,Action action)
        {
            var panel=Panel(parent,label,new Color(.075f,.19f,.23f)); Position(panel,x,y,width,height);
            var outline=panel.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.05f,.65f,.84f,.65f);outline.effectDistance=new Vector2(1.5f,-1.5f);
            var button=panel.gameObject.AddComponent<Button>(); button.targetGraphic=panel.GetComponent<Image>();
            var colors=button.colors; colors.highlightedColor=new Color(.5f,1,.84f); colors.pressedColor=new Color(.3f,.75f,.65f); colors.disabledColor=new Color(.4f,.4f,.4f,.6f); button.colors=colors;
            var text=Label(panel,label,size,ink,8,4,width-16,height-8); text.alignment=TextAnchor.MiddleCenter;
            button.onClick.AddListener(() => action()); return button;
        }
        private static void Position(RectTransform r,float x,float y,float width,float height) { r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(width,height); }
        private static void TopRight(RectTransform r,float x,float y,float width,float height) { r.anchorMin=r.anchorMax=new Vector2(1,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,y); r.sizeDelta=new Vector2(width,height); }
        private static void Center(RectTransform r,float x,float y,float width,float height) { r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(width,height); }
        private static void Fill(RectTransform r) { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero; }
        private static void Stretch(RectTransform r,float ax,float ay,float bx,float by,Vector2 size,Vector2 position,Vector2 pivot) { r.anchorMin=new Vector2(ax,ay); r.anchorMax=new Vector2(bx,by); r.pivot=pivot; r.anchoredPosition=position; r.sizeDelta=size; }
        private void OnDestroy()
        {
            if (session==null) return;
            if(session.Economy!=null) session.Economy.Changed-=Refresh;
            if(session.Waves!=null) session.Waves.Changed-=Refresh;
            if(session.Blocks!=null) { session.Blocks.Changed-=Refresh; session.Blocks.Notice-=ShowNotice; }
            if(session.Towers!=null) { session.Towers.Changed-=Refresh; session.Towers.Notice-=ShowNotice; }
            if(session.Game!=null) session.Game.StateChanged-=OnState;
            if(session.Rewards!=null) session.Rewards.Offered-=ShowRewards;
        }
    }
}
