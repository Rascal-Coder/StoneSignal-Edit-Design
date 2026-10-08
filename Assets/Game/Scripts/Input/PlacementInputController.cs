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
        /// v18.6: card shown but its placement is off for this phase (block row in combat): a press neither selects nor moves the card;
        /// a drag start raises the notice, a tap / hold still expands the fan (PlacementInputController.BlockedDown).
        public System.Func<bool> Blocked;
        public void OnPointerDown(PointerEventData e)
        {
            if (Enabled == null || Enabled()) PlacementInputController.Instance?.CardDown(Tower, Index, e.position);
            else if (Blocked != null && Blocked()) PlacementInputController.Instance?.BlockedDown(Tower, Index, e.position);
        }
    }

    /// Drives touch placement (Arknights-style drag -> direction swipe; tap-tap -> tap arrow) on top of the managers.
    /// Desktop keeps hover + click + R/RMB/1-4 in the managers; this takes over only while a card drag is active, or always in touch mode.
    /// The camera is fixed (no pan/zoom code exists); extra fingers are ignored during placement.
    public sealed class PlacementInputController : MonoBehaviour
    {
        public static PlacementInputController Instance { get; private set; }
        public readonly PlacementInput Machine = new PlacementInput();
        /// Ghost is previewed this many reference pixels above the finger (scaled by the HUD canvas) away from the hand.
        public float DragOffset = 110f;
        /// Adaptive offset (玩法策划 v18 drag spec (b)): with the finger on / near a hand card the aim point is lifted to
        /// LiftMargin px above that card's top edge; LiftFeather px around the card blends the lift in and out (continuous,
        /// no jump); beyond it the offset is DragOffset again. Reference pixels x CanvasScale.
        public float LiftMargin = 28f, LiftFeather = 100f; // feather 100: smallest finger window for a covered corner cell 12 -> ~22 px (v18 drag report)
        public float CanvasScale = 1f;
        /// Hand card rects (set by GameUI on every hand rebuild). Finger on one: ghost lifted above it (upper half) or
        /// "back over the hand" = hide / cancel (lower half), see OffsetFor / InCancelZone.
        /// Was a full-width band below the block row (y <= 434 px at 1080p), which also covered open board on the right and,
        /// with the 110 px offset, made the lower board rows unreachable by drag; it also ignored the safe-area bottom inset.
        public readonly List<RectTransform> HandRects = new List<RectTransform>();
        float handTopFallback;
        static readonly Vector3[] corners = new Vector3[4];
        /// Screen-space top of the hand (highest hand card edge, safe area included).
        public float HandTop
        {
            get { float top = 0; bool any = false; foreach (var r in HandRects) if (r != null && r.gameObject.activeInHierarchy) { r.GetWorldCorners(corners); foreach (var c in corners) top = Mathf.Max(top, c.y); any = true; } return any ? top : handTopFallback; }
            set => handTopFallback = value;
        }
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
        /// v18.4 fanned block row (GameUI): a card press that ends as a tap / a still press held this long (seconds) can be taken by the
        /// fan (collapsed fan: tap or hold expands instead of arming the card). Return true = consumed (the gesture is cancelled).
        public System.Func<bool, int, bool> CardTapHook;
        public System.Func<bool, int, float, bool> CardHeldHook;
        /// Every pointer-down this controller reads (count + position) and the time of the last pointer input (down / held / up).
        public int DownSerial { get; private set; }
        public Vector2 DownPos { get; private set; }
        public float LastInputTime { get; private set; } = -100f;
        float pressAt;
        void Consume() { Machine.Reset(); Cancel(); HandFade = GhostOverHand = false; Drive(); }
        // ---- v18.6 (玩法策划): blocked card press (walls during combat) ----
        bool blockedOn, blockedMoved; bool blockedTower; int blockedCard; Vector2 blockedPos; float blockedAt;
        /// Notice raised when a blocked card drag starts (logic key, displayed via Loc.Notice; GameUI de-dupes the same text within 1 s).
        public string BlockedNotice = "Walls cannot be placed during combat";
        public int BlockedNotices { get; private set; }
        public bool BlockedPressActive => blockedOn;
        public void BlockedDown(bool tower, int index, Vector2 pos)
        {
            if (Machine.State != PlacementState.Idle) return;
            blockedOn = true; blockedMoved = false; blockedTower = tower; blockedCard = index; blockedPos = pos; blockedAt = pressAt = Time.unscaledTime;
        }
        /// true = the pointer is owned by a blocked card press this frame.
        bool UpdateBlocked(PointerInput.Sample p, float now)
        {
            if (!blockedOn) return false;
            float th = Machine.DragThreshold * Machine.Scale;
            if (p.Held && !p.Up)
            {
                if (!blockedMoved && (p.Position - blockedPos).sqrMagnitude >= th * th) { blockedMoved = true; BlockedNotices++; blocks.RaiseNotice(BlockedNotice); }
                if (!blockedMoved && CardHeldHook != null && CardHeldHook(blockedTower, blockedCard, now - blockedAt)) blockedOn = false;
                return true;
            }
            if (!blockedMoved && CardTapHook != null) CardTapHook(blockedTower, blockedCard);
            blockedOn = false; return true;
        }
        public void CardDown(bool tower, int index, Vector2 pos)
        {
            pressAt = Time.unscaledTime;
            if (tower) towers.Select(index); else { towers.Select(-1); blocks.SelectCard(index); }
            Machine.CardDown(tower, index, tower ? towers.SelectedNeedsDirection : true, pos, Time.unscaledTime);
            Drive();
        }
        static readonly List<Rect> handScreen = new List<Rect>();
        /// Screen-space AABBs of the active hand cards (fallback: a full-width band up to HandTop when GameUI gave none).
        List<Rect> HandScreenRects()
        {
            handScreen.Clear();
            foreach (var r in HandRects)
                if (r != null && r.gameObject.activeInHierarchy)
                {
                    r.GetWorldCorners(corners); float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    foreach (var c in corners) { x0 = Mathf.Min(x0, c.x); y0 = Mathf.Min(y0, c.y); x1 = Mathf.Max(x1, c.x); y1 = Mathf.Max(y1, c.y); }
                    handScreen.Add(Rect.MinMaxRect(x0, y0, x1, y1));
                }
            if (handScreen.Count == 0 && handTopFallback > 0) handScreen.Add(Rect.MinMaxRect(-1e5f, -1e5f, 1e5f, handTopFallback));
            return handScreen;
        }
        bool OverHand(Vector2 pos) { foreach (var r in HandScreenRects()) if (r.Contains(pos)) return true; return false; }
        /// Ghost offset (screen px) for a finger position. Per card: lifted = max(base, card top + margin), blended by
        /// w = 1 - clamp01(distance outside the card / feather) (cards the finger is above are ignored). The result is
        /// continuous in the finger position, so the ghost never jumps, and it only lifts relative to the card the finger
        /// is on or next to (never to the top of the whole hand stack: cells covered by the block row, e.g. the bottom-left
        /// corner at 1920x1080 / 2048x1536, must stay reachable).
        /// v18.3 (玩法策划) hand pass-through: while dragging, the faded hand (35%) does not block the board. The aim is always the plain
        /// DragOffset above the finger (no lift over the cards), so board cells under the hand can be targeted; the cancel zone shrinks to
        /// the lower half of the BOTTOM card row (tower cards) - the old "lower half of any hand card" rule made the top band of the block
        /// row unreachable (finger on a block card's lower half aims at that band). false = v18.2 behaviour (lift + any-card cancel).
        public bool HandPassThrough = true;
        public float OffsetFor(Vector2 finger)
        {
            float s = CanvasScale > 0 ? CanvasScale : 1f, b = DragOffset * s, m = Mathf.Min(LiftMargin * s, b), f = Mathf.Max(1f, LiftFeather * s);
            if (HandPassThrough) return b;
            float baseAim = finger.y + b, aim = baseAim;
            foreach (var r in HandScreenRects())
            {
                if (finger.y > r.yMax) continue;
                float dx = Mathf.Max(0f, Mathf.Max(r.xMin - finger.x, finger.x - r.xMax)), dy = Mathf.Max(0f, r.yMin - finger.y);
                float w = 1f - Mathf.Clamp01(Mathf.Max(dx, dy) / f); if (w <= 0f) continue;
                aim = Mathf.Max(aim, Mathf.Lerp(baseAim, Mathf.Max(baseAim, r.yMax + m), w));
            }
            return aim - finger.y;
        }
        public Vector2 AimFor(Vector2 finger) => finger + Vector2.up * OffsetFor(finger);
        /// "Back over the hand" = finger on the LOWER half of a hand card: ghost hidden, release cancels. The upper half
        /// lifts the ghost instead (adaptive offset), so a drag can still finish on cells just above / under the cards.
        public bool InCancelZone(Vector2 finger)
        {
            var rects = HandScreenRects(); float bottom = float.MaxValue;
            if (HandPassThrough) foreach (var r in rects) bottom = Mathf.Min(bottom, r.yMin);
            foreach (var r in rects) if (r.Contains(finger) && finger.y < r.center.y && (!HandPassThrough || r.yMin <= bottom + r.height * .25f)) return true; // bottom row (a selected card may sit a little higher)
            return false;
        }
        /// Diagnostics (drag report): the controller's own hand test and a hard cancel of any gesture in progress.
        public bool IsOverHand(Vector2 pos) => OverHand(pos);
        /// 玩法策划 v18 hand fade: true while a card drag is in progress (Dragging, or Direction with the finger still down) and the finger
        /// OR the ghost is over the hand area; GameUI then fades the other hand cards (CanvasGroup alpha) and keeps the dragged one opaque.
        public bool HandFade { get; private set; }
        public bool GhostOverHand { get; private set; }
        /// Ghost footprint in screen space: any ghost cell centre inside a hand rect grown by a quarter cell.
        bool GhostInHand(Vector2Int anchor)
        {
            List<Vector2Int> cells;
            if (Machine.Tower) { if (towers.SelectedIndex < 0) return false; cells = grid.Footprint(towers.OriginFor(anchor, towers.Rotation), TowerManager.SizeOf(towers.Data[towers.SelectedIndex], towers.Rotation)); }
            else cells = blocks.CellsAt(anchor);
            var rects = HandScreenRects();
            foreach (var c in cells)
            {
                Vector2 p = CellScreen(c), q = CellScreen(c + Vector2Int.right); float pad = (q - p).magnitude * .25f;
                foreach (var r in rects) if (p.x >= r.xMin - pad && p.x <= r.xMax + pad && p.y >= r.yMin - pad && p.y <= r.yMax + pad) return true;
            }
            return false;
        }
        public void CancelPlacement() { Machine.Reset(); if (towers != null) Cancel(); Drive(); }
        /// Anchor under a screen point: the visible cell (wall top / tile top, GridManager.RaycastCell). Symmetric multi-cell towers
        /// (2x2) return the footprint origin centred on the aim point and kept on the board, as the desktop hover does.
        Vector2Int? CellAt(Vector2 screen)
        {
            var cam = Camera.main; if (cam == null || grid == null) return null;
            if (!grid.RaycastCell(cam.ScreenPointToRay(screen), out var c, out var hit) || !grid.InBounds(c)) return null;
            if (Machine.Tower && towers.SelectedIndex >= 0 && !towers.SelectedNeedsDirection)
            {
                var size = TowerManager.SizeOf(towers.Data[towers.SelectedIndex], 0);
                if (size != Vector2Int.one) return grid.ClampOrigin(grid.FootprintOrigin(hit, size), size);
            }
            return c;
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
            var a = Machine.Cell;
            if (NeedsDirection && Machine.Dir >= 0) { if (Machine.Tower) towers.SetRotation(Machine.Dir); else blocks.SetRotation(Machine.Dir); } // a tapped arrow commits its own direction (was: the previewed one)
            int dir = Machine.Tower ? towers.Rotation : blocks.Rotation;
            bool ok = Machine.Tower ? towers.TryBuild(towers.OriginFor(a, dir), towers.SelectedIndex, dir) : blocks.CommitPlacement(a);
            bool showedDir = indicator.gameObject.activeSelf; Vector2 at = Machine.Centre;
            Hide(); towers.Select(-1);
            if (ok && showedDir) indicator.Pulse(at);
            return ok;
        }
        void Cancel() { Hide(); towers.Select(-1); }
        Vector2 CellScreen(Vector2Int c) { var cam = Camera.main; return cam != null ? (Vector2)cam.WorldToScreenPoint(grid.ToWorld(c) + Vector3.up * (Machine.Tower ? grid.wallTop : grid.tileTop)) : Vector2.zero; } // ghost height

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
                    if (Machine.State == PlacementState.Dragging || Machine.State == PlacementState.Armed) { var c = CellAt(AimFor(finger)); if (c.HasValue) ShowFixed(c.Value); else Hide(); }
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

        static readonly System.Collections.Generic.List<RaycastResult> uiHits = new System.Collections.Generic.List<RaycastResult>();
        static bool OverUi(Vector2 pos)
        {
            var es = EventSystem.current; if (es == null) return false;
            uiHits.Clear(); es.RaycastAll(new PointerEventData(es) { position = pos }, uiHits); return uiHits.Count > 0;
        }
        void Update()
        {
            if (towers == null) return;
            Drive();
            var p = PointerInput.Read(); float now = Time.unscaledTime;
            if (p.Down) { DownSerial++; DownPos = p.Position; }
            if (p.Down || p.Held || p.Up) LastInputTime = now;
            bool touch = PointerInput.TouchMode;
            if (p.Count > 1 && Machine.State != PlacementState.Idle) return; // ignore multi-touch during placement (fade state kept)
            if (Machine.State == PlacementState.Idle && UpdateBlocked(p, now)) { HandFade = GhostOverHand = false; return; } // v18.6 blocked card press
            var st = Machine.State;
            Vector2 aim = AimFor(p.Position); // cell is picked above the finger (ghost raised; lifted over the hand cards)
            // machine "over the hand" = cancel zone (lower half of a card) while dragging. Not in Direction: there the swipe is
            // relative to the locked centre, and a down-swipe from a cell just above the hand would otherwise land on a card
            // and cancel; Direction cancels by releasing in the centre or leaving the OuterRadius ring.
            bool overHand = st != PlacementState.Direction && InCancelZone(p.Position);
            bool fingerDriven = st == PlacementState.Pressed || st == PlacementState.Dragging || (st == PlacementState.Direction && Machine.Held);
            if (fingerDriven)
            {
                Vector2Int? ghost = null;
                if (p.Held && !p.Up)
                {
                    var c = st == PlacementState.Direction ? (Vector2Int?)null : CellAt(aim);
                    bool ok = c.HasValue && AnchorValid(c.Value);
                    Handle(Machine.Move(p.Position, overHand, c, ok, now), p.Position);
                    if (Machine.State == PlacementState.Pressed && CardHeldHook != null && CardHeldHook(Machine.Tower, Machine.Card, now - pressAt)) { Consume(); return; }
                    ghost = Machine.State == PlacementState.Direction ? Machine.Cell : Machine.State == PlacementState.Dragging && !overHand ? c : null; // ghost hidden in the cancel zone
                }
                if (p.Up || !p.Held)
                {
                    if (Machine.State == PlacementState.Pressed && CardTapHook != null && CardTapHook(Machine.Tower, Machine.Card)) { Consume(); return; }
                    Handle(Machine.Up(p.Position, overHand), p.Position);
                }
                bool drag = p.Held && !p.Up && (Machine.State == PlacementState.Dragging || (Machine.State == PlacementState.Direction && Machine.Held));
                GhostOverHand = drag && ghost.HasValue && GhostInHand(ghost.Value);
                HandFade = drag && (OverHand(p.Position) || GhostOverHand);
                Drive(); return;
            }
            HandFade = GhostOverHand = false;
            // board touches: tap-tap (Armed) and released Direction. Touch mode only; desktop uses the managers' hover/click.
            bool armedOrDir = st == PlacementState.Armed || st == PlacementState.Direction;
            if (armedOrDir && (touch || st == PlacementState.Direction))
            {
                if (p.Down && ((st == PlacementState.Direction && indicator.HitArrow(p.Position)) || !OverUi(p.Position))) // arrows draw above the HUD (sortingOrder 50), so a tap on an arrow wins even over a hand card (v18 drag report: 2048x1536 notch corner BL); UI hit test at the pointer itself (was IsPointerOverGameObject(fingerId/-1): with injected/forced touch that tested the mouse cursor, so arrow taps were dropped at random)
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
            for (int i = 0; i < 4; i++) { arrows[i] = Img("Arrow" + i, ArrowSprite, 1); arrows[i].raycastTarget = true; } // arrows (drawn on top) swallow the tap so a hand card underneath is not also pressed
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
        public bool HitArrow(Vector2 screen) { if (!isActiveAndEnabled) return false; foreach (var a in arrows) if (a != null && a.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.rectTransform, screen, null)) return true; return false; }
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
