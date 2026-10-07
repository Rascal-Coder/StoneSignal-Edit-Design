using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StoneSignal
{
    /// Unified pointer: Input.touches when present, mouse otherwise; smoke tests can inject a pointer.
    public static class PointerInput
    {
        public struct Sample { public bool Down, Held, Up; public Vector2 Position; public int Count; public bool SecondBegan; }
        static bool injected; static Sample inject;
        /// Touch mode: mobile / WeChat builds, or any real touch seen this session (sticky). ForceTouch for tests.
        public static bool ForceTouch;
        static bool touchSeen;
        public static bool TouchMode => ForceTouch || touchSeen || Application.isMobilePlatform
#if UNITY_WEIXINMINIGAME || WEIXINMINIGAME
            || true
#endif
            ;
        public static void Inject(Vector2 pos, bool down, bool held, bool up) { injected = true; inject = new Sample { Down = down, Held = held, Up = up, Position = pos, Count = held || up ? 1 : 0 }; }
        public static void ClearInjection() { injected = false; }
        public static Sample Read()
        {
            if (injected) { var s = inject; if (s.Down) inject.Down = false; if (s.Up) { inject.Up = false; inject.Held = false; } return s; }
            if (Input.touchCount > 0)
            {
                touchSeen = true;
                var t = Input.GetTouch(0); var r = new Sample { Position = t.position, Count = Input.touchCount };
                r.Down = t.phase == TouchPhase.Began; r.Up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled; r.Held = !r.Up;
                for (int i = 1; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) r.SecondBegan = true;
                return r;
            }
            return new Sample { Position = Input.mousePosition, Down = Input.GetMouseButtonDown(0), Held = Input.GetMouseButton(0), Up = Input.GetMouseButtonUp(0), Count = Input.GetMouseButton(0) || Input.GetMouseButtonUp(0) ? 1 : 0 };
        }
    }

    /// Card hook: pointer-down on a hand card starts a tap/drag (works for touch and mouse via the EventSystem).
    public sealed class CardPointer : MonoBehaviour, IPointerDownHandler
    {
        public bool Tower; public int Index; public System.Func<bool> Enabled;
        public void OnPointerDown(PointerEventData e) { if (Enabled == null || Enabled()) PlacementInputController.Instance?.CardDown(Tower, Index, e.position); }
    }

    /// Drives touch placement (Arknights-style drag -> direction swipe; tap-tap -> tap arrow) on top of the managers.
    /// Desktop keeps hover + click + R/RMB/1-4 in the managers; this takes over only while a card drag is active, or always in touch mode.
    /// The camera is fixed (no pan/zoom code exists); extra fingers are ignored during placement.
    public sealed class PlacementInputController : MonoBehaviour
    {
        public static PlacementInputController Instance { get; private set; }
        public readonly PlacementInput Machine = new PlacementInput();
        /// Ghost is previewed this many reference pixels above the finger (scaled by the HUD canvas).
        public float DragOffset = 110f;
        public float CanvasScale = 1f;
        /// Screen-space top of the hand band (set by GameUI on layout); releasing below it = "back over the hand".
        public float HandTop;
        TowerManager towers; BlockPlacementManager blocks; GridManager grid; DirectionIndicator indicator;

        public void Initialize(TowerManager t, BlockPlacementManager b, GridManager g, ArtCatalog art = null)
        {
            if (art != null)
            {
                DirectionIndicator.RingSprite = art.UiSprite("ui_dir_ring"); DirectionIndicator.ArrowSprite = art.UiSprite("ui_dir_arrow");
                DirectionIndicator.PressedSprite = art.UiSprite("ui_dir_arrow_pressed"); DirectionIndicator.PulseSprite = art.UiSprite("ui_dir_pulse");
            }
            towers = t; blocks = b; grid = g; Instance = this;
            Machine.ValidFor = d => Reason(Machine.Cell, d) == null;
            indicator = DirectionIndicator.Create(transform);
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        bool NeedsDirection => Machine.Tower ? towers.SelectedNeedsDirection : true; // every wall block uses direction select
        public void CardDown(bool tower, int index, Vector2 pos)
        {
            if (tower) towers.Select(index); else { towers.Select(-1); blocks.SelectCard(index); }
            Machine.CardDown(tower, index, tower ? towers.SelectedNeedsDirection : true, pos, Time.unscaledTime);
            Drive();
        }
        bool OverHand(Vector2 pos) => pos.y <= HandTop;
        Vector2Int? CellAt(Vector2 screen)
        {
            var cam = Camera.main; if (cam == null || grid == null) return null;
            var plane = new Plane(Vector3.up, grid.transform.position);
            var ray = cam.ScreenPointToRay(screen);
            if (!plane.Raycast(ray, out float d)) return null;
            var c = grid.ToCell(ray.GetPoint(d)); return grid.InBounds(c) ? c : (Vector2Int?)null;
        }
        string Reason(Vector2Int anchor, int dir)
        {
            if (Machine.Tower) return towers.ReasonFor(anchor, dir);
            return blocks.ValidatePlacement(anchor, dir);
        }
        bool AnchorValid(Vector2Int anchor)
        {
            if (!NeedsDirection) return Reason(anchor, Machine.Tower ? towers.Rotation : blocks.Rotation) == null;
            for (int d = 0; d < 4; d++) if (Reason(anchor, d) == null) return true;
            return false;
        }
        int FirstValidDir(Vector2Int anchor) { for (int d = 0; d < 4; d++) if (Reason(anchor, d) == null) return d; return 0; }
        void Show(Vector2Int anchor, int dir)
        {
            if (Machine.Tower) { towers.SetRotation(dir); towers.PreviewOrigin(towers.OriginFor(anchor, dir)); }
            else { blocks.SetRotation(dir); blocks.Preview(anchor); }
        }
        void ShowFixed(Vector2Int anchor) { if (Machine.Tower) towers.PreviewOrigin(towers.OriginFor(anchor, towers.Rotation)); else blocks.Preview(anchor); }
        void Hide() { towers.HidePreview(); blocks.HidePreview(); indicator.Hide(); }
        bool Place()
        {
            var a = Machine.Cell; int dir = Machine.Tower ? towers.Rotation : blocks.Rotation;
            bool ok = Machine.Tower ? towers.TryBuild(towers.OriginFor(a, dir), towers.SelectedIndex, dir) : blocks.CommitPlacement(a);
            bool showedDir = indicator.gameObject.activeSelf; Vector2 at = Machine.Centre;
            Hide(); towers.Select(-1);
            if (ok && showedDir) indicator.Pulse(at);
            return ok;
        }
        void Cancel() { Hide(); towers.Select(-1); }
        Vector2 CellScreen(Vector2Int c) { var cam = Camera.main; return cam != null ? (Vector2)cam.WorldToScreenPoint(grid.ToWorld(c)) : Vector2.zero; }

        void Drive()
        {
            bool touch = PointerInput.TouchMode;
            towers.ExternalDrive = blocks.ExternalDrive = touch || Machine.State == PlacementState.Pressed || Machine.State == PlacementState.Dragging || Machine.State == PlacementState.Direction;
            Machine.Scale = CanvasScale;
        }
        void Handle(PlacementAction a, Vector2 finger)
        {
            switch (a)
            {
                case PlacementAction.Preview:
                    if (Machine.State == PlacementState.Dragging || Machine.State == PlacementState.Armed) { var c = CellAt(finger + Vector2.up * DragOffset * CanvasScale); if (c.HasValue) ShowFixed(c.Value); else Hide(); }
                    break;
                case PlacementAction.HidePreview: Hide(); break;
                case PlacementAction.EnterDirection:
                    if (!Machine.Held) Machine.SetCentre(CellScreen(Machine.Cell)); // released: indicator around the ghost cell
                    { int d = FirstValidDir(Machine.Cell); Show(Machine.Cell, d); indicator.Show(Machine.Centre, Machine.OuterRadius * CanvasScale, Machine.SwipeThreshold * CanvasScale, -1, true); }
                    break;
                case PlacementAction.SetDirection:
                    Show(Machine.Cell, Machine.Dir); indicator.Show(Machine.Centre, Machine.OuterRadius * CanvasScale, Machine.SwipeThreshold * CanvasScale, Machine.Dir, Reason(Machine.Cell, Machine.Dir) == null, Machine.Held);
                    break;
                case PlacementAction.Place: Place(); break;
                case PlacementAction.Cancel: case PlacementAction.Deselect: Cancel(); break;
            }
        }

        void Update()
        {
            if (towers == null) return;
            Drive();
            var p = PointerInput.Read(); float now = Time.unscaledTime;
            bool touch = PointerInput.TouchMode;
            if (p.Count > 1 && Machine.State != PlacementState.Idle) return; // ignore multi-touch during placement
            var st = Machine.State;
            Vector2 aim = p.Position + Vector2.up * DragOffset * CanvasScale; // cell is picked above the finger (ghost raised)
            bool overHand = OverHand(p.Position);
            bool fingerDriven = st == PlacementState.Pressed || st == PlacementState.Dragging || (st == PlacementState.Direction && Machine.Held);
            if (fingerDriven)
            {
                if (p.Held && !p.Up)
                {
                    var c = st == PlacementState.Direction ? (Vector2Int?)null : CellAt(aim);
                    bool ok = c.HasValue && AnchorValid(c.Value);
                    Handle(Machine.Move(p.Position, overHand, c, ok, now), p.Position);
                }
                if (p.Up || !p.Held) Handle(Machine.Up(p.Position, overHand), p.Position);
                Drive(); return;
            }
            // board touches: tap-tap (Armed) and released Direction. Touch mode only; desktop uses the managers' hover/click.
            bool armedOrDir = st == PlacementState.Armed || st == PlacementState.Direction;
            if (armedOrDir && (touch || st == PlacementState.Direction))
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(Input.touchCount > 0 ? Input.GetTouch(0).fingerId : -1);
                if (p.Down && !overUi)
                {
                    var c = CellAt(p.Position); // tap-tap: the tapped cell itself
                    Handle(Machine.BoardDown(p.Position, c, c.HasValue && AnchorValid(c.Value)), p.Position);
                    if (Machine.State == PlacementState.Armed && c.HasValue) ShowFixed(c.Value);
                }
            }
            else if (st == PlacementState.Armed && !touch && towers.SelectedIndex < 0 && blocks.Hand.Selected < 0) Machine.Reset();
            Drive();
        }
    }

    /// Placeholder direction indicator: centre ring + 4 arrows (up/right/down/left) on its own overlay canvas.
    /// Sprites are optional (art: ui_dir_ring, ui_dir_arrow); without them it draws flat diamonds.
    public sealed class DirectionIndicator : MonoBehaviour
    {
        public static Sprite RingSprite, ArrowSprite, PressedSprite, PulseSprite;
        UnityEngine.UI.Image pulse; float pulseT = -1;
        RectTransform root, ring; readonly UnityEngine.UI.Image[] arrows = new UnityEngine.UI.Image[4];
        static readonly Color Idle = new Color(1, 1, 1, .85f), Valid = new Color32(0x5B, 0xD1, 0x6A, 255), Invalid = new Color32(0xE5, 0x48, 0x4D, 255);
        public static DirectionIndicator Create(Transform parent)
        {
            var go = new GameObject("Direction indicator", typeof(Canvas)); go.transform.SetParent(parent, false);
            var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 50;
            var di = go.AddComponent<DirectionIndicator>(); di.Build(); di.Hide(); return di;
        }
        UnityEngine.UI.Image Img(string n, Sprite s, float size)
        {
            var g = new GameObject(n, typeof(RectTransform), typeof(UnityEngine.UI.Image)); g.transform.SetParent(root, false);
            var im = g.GetComponent<UnityEngine.UI.Image>(); im.sprite = s; im.raycastTarget = false; im.rectTransform.sizeDelta = new Vector2(size, size); return im;
        }
        void Build()
        {
            root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(transform, false);
            root.anchorMin = root.anchorMax = Vector2.zero; root.pivot = new Vector2(.5f, .5f);
            var r = Img("Ring", RingSprite, 1); ring = r.rectTransform; r.color = new Color(1, 1, 1, RingSprite ? .9f : .18f);
            for (int i = 0; i < 4; i++) arrows[i] = Img("Arrow" + i, ArrowSprite, 1);
            pulse = Img("Pulse", PulseSprite, 128); pulse.gameObject.SetActive(false);
        }
        public void Show(Vector2 centre, float outer, float dead, int dir, bool valid, bool pressed = false)
        {
            gameObject.SetActive(true); ring.gameObject.SetActive(true); foreach (var a in arrows) a.gameObject.SetActive(true);
            float k = GetComponent<Canvas>().scaleFactor; if (k <= 0) k = 1;
            root.anchoredPosition = centre / k; float o = outer / k, d = dead / k;
            ring.sizeDelta = RingSprite ? new Vector2(96, 96) : new Vector2(d * 2.2f, d * 2.2f); ring.localRotation = RingSprite ? Quaternion.identity : Quaternion.Euler(0, 0, 45);
            for (int i = 0; i < 4; i++)
            {
                var a = arrows[i]; float ang = -90f * i; var v = Quaternion.Euler(0, 0, ang) * Vector3.up;
                a.rectTransform.sizeDelta = new Vector2(88, 88); // >= 88 px touch target
                a.rectTransform.anchoredPosition = (Vector2)v * (o * .62f);
                a.rectTransform.localRotation = Quaternion.Euler(0, 0, ang + (ArrowSprite ? 0 : 45));
                if (!ArrowSprite) a.rectTransform.localScale = new Vector3(.55f, .55f, 1);
                a.sprite = i == dir && pressed && PressedSprite ? PressedSprite : ArrowSprite; // pressed while the finger is on that arrow
                a.color = i == dir ? (valid ? Valid : Invalid) : Idle;
            }
        }
        public void Hide() { if (pulseT < 0) gameObject.SetActive(false); else { ring.gameObject.SetActive(false); foreach (var a in arrows) a.gameObject.SetActive(false); } }
        /// Commit feedback: ui_dir_pulse expands and fades (0.3 s, unscaled) at the indicator centre.
        public void Pulse(Vector2 centre)
        {
            if (PulseSprite == null) return;
            gameObject.SetActive(true); ring.gameObject.SetActive(false); foreach (var a in arrows) a.gameObject.SetActive(false);
            float k = GetComponent<Canvas>().scaleFactor; if (k <= 0) k = 1;
            root.anchoredPosition = centre / k; pulse.sprite = PulseSprite; pulse.gameObject.SetActive(true); pulseT = 0;
        }
        void Update()
        {
            if (pulseT < 0) return;
            pulseT += Time.unscaledDeltaTime; float u = pulseT / .3f;
            pulse.rectTransform.localScale = Vector3.one * Mathf.Lerp(.6f, 1.4f, u); pulse.color = new Color(1, 1, 1, 1 - u);
            if (u >= 1) { pulseT = -1; pulse.gameObject.SetActive(false); ring.gameObject.SetActive(true); foreach (var a in arrows) a.gameObject.SetActive(true); gameObject.SetActive(false); }
        }
    }
}
