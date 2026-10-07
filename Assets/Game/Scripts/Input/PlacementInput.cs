using System;
using UnityEngine;

namespace StoneSignal
{
    public enum PlacementState { Idle, Pressed, Dragging, Armed, Direction }
    public enum PlacementAction { None, Select, Deselect, Preview, HidePreview, EnterDirection, SetDirection, Place, Cancel }

    /// Arknights-style touch placement, pure C# (no Unity input calls; unit-tested in RuneDrawTests).
    ///  Drag:   card down -> Pressed -> (moved > DragThreshold) Dragging: ghost snaps to the cell under the finger.
    ///          Symmetric footprint (1x1 / 2x2): release on a valid cell = Place, otherwise Cancel.
    ///          Directional (walls, 1x2): release on a valid anchor -> Direction (released), or hold still
    ///          HoldSeconds on a valid anchor -> Direction in-gesture (centre = finger).
    ///  Direction: swipe from the centre past SwipeThreshold picks up/right/down/left (0/1/2/3 = 0/90/180/270 deg), previewed live.
    ///          Release on a direction: Place if valid, else stay in Direction (released). Release in the centre deadzone,
    ///          moving beyond OuterRadius, or over the hand = Cancel.
    ///          Released Direction: tap an arrow = pick + Place if valid; touch the centre = new swipe; tap elsewhere = Cancel.
    ///  Tap-tap: tap card -> Armed; tap a cell -> Direction (directional) or Place (symmetric, if valid); tap off board = Cancel.
    /// Distances are reference pixels; Scale multiplies them (canvas scale factor).
    public sealed class PlacementInput
    {
        public float DragThreshold = 24f, SwipeThreshold = 40f, OuterRadius = 170f, HoldSeconds = .15f, HoldStill = 8f, Scale = 1f;
        /// Validity of the locked anchor for a direction (0..3); set by the controller.
        public Func<int, bool> ValidFor = _ => true;

        public PlacementState State { get; private set; } = PlacementState.Idle;
        public bool Tower { get; private set; }
        public int Card { get; private set; } = -1;
        public bool NeedsDirection { get; private set; }
        public Vector2Int Cell { get; private set; }
        public Vector2 Centre { get; private set; }
        /// Picked direction 0..3, or -1 while in the centre deadzone.
        public int Dir { get; private set; } = -1;
        /// True while the finger that drives Direction is still down.
        public bool Held { get; private set; }

        Vector2 downPos, stillPos; float stillSince; bool wasArmed; Vector2Int? dragCell; bool dragValid;

        float D(float v) => v * Scale;

        public PlacementAction CardDown(bool tower, int card, bool needsDirection, Vector2 pos, float now = 0)
        {
            wasArmed = State == PlacementState.Armed && Tower == tower && Card == card;
            Tower = tower; Card = card; NeedsDirection = needsDirection; downPos = stillPos = pos; stillSince = now; Dir = -1; Held = true;
            State = PlacementState.Pressed; dragCell = null;
            return PlacementAction.Select;
        }

        /// Finger moved / held. cell = cell under the finger (null off board); anchorValid = cell valid in some direction
        /// (directional) or in the fixed footprint (symmetric).
        public PlacementAction Move(Vector2 pos, bool overHand, Vector2Int? cell, bool anchorValid, float now = 0)
        {
            if (State == PlacementState.Pressed && (pos - downPos).sqrMagnitude >= D(DragThreshold) * D(DragThreshold)) { State = PlacementState.Dragging; stillPos = pos; stillSince = now; }
            if (State == PlacementState.Dragging)
            {
                if (overHand || !cell.HasValue) { dragCell = null; return PlacementAction.HidePreview; }
                if (dragCell != cell || (pos - stillPos).sqrMagnitude > D(HoldStill) * D(HoldStill)) { stillPos = pos; stillSince = now; }
                dragCell = cell; dragValid = anchorValid;
                if (NeedsDirection && anchorValid && now - stillSince >= HoldSeconds) { Enter(cell.Value, pos, true); return PlacementAction.EnterDirection; }
                return PlacementAction.Preview;
            }
            if (State == PlacementState.Direction && Held)
            {
                if (overHand) { Reset(); return PlacementAction.Cancel; }
                var v = pos - Centre; float m = v.magnitude;
                if (m > D(OuterRadius)) { Reset(); return PlacementAction.Cancel; }
                int d = m < D(SwipeThreshold) ? -1 : DirOf(v);
                if (d != Dir) { Dir = d; return d >= 0 ? PlacementAction.SetDirection : PlacementAction.None; }
            }
            return PlacementAction.None;
        }

        public PlacementAction Up(Vector2 pos, bool overHand)
        {
            Held = false;
            switch (State)
            {
                case PlacementState.Pressed:
                    if (wasArmed) { Reset(); return PlacementAction.Deselect; }
                    State = PlacementState.Armed; return PlacementAction.None;
                case PlacementState.Dragging:
                    if (overHand || !dragCell.HasValue || !dragValid) { Reset(); return PlacementAction.Cancel; }
                    if (!NeedsDirection) { Cell = dragCell.Value; Reset(); return PlacementAction.Place; }
                    Enter(dragCell.Value, pos, false); return PlacementAction.EnterDirection;
                case PlacementState.Direction:
                    if (overHand) { Reset(); return PlacementAction.Cancel; }
                    if (Dir < 0) { Reset(); return PlacementAction.Cancel; } // released in the centre deadzone
                    if (ValidFor(Dir)) { Reset(); return PlacementAction.Place; }
                    return PlacementAction.None; // invalid: stay so another direction can be tried
            }
            return PlacementAction.None;
        }

        /// New finger down on the board while Armed or in released Direction (cell = cell under the finger).
        public PlacementAction BoardDown(Vector2 pos, Vector2Int? cell, bool anchorValid)
        {
            if (State == PlacementState.Armed)
            {
                if (!cell.HasValue) { Reset(); return PlacementAction.Cancel; }
                if (!NeedsDirection) { if (anchorValid) { Cell = cell.Value; Reset(); return PlacementAction.Place; } return PlacementAction.Preview; }
                if (!anchorValid) return PlacementAction.Preview;
                Enter(cell.Value, pos, false); return PlacementAction.EnterDirection;
            }
            if (State != PlacementState.Direction || Held) return PlacementAction.None;
            var v = pos - Centre; float m = v.magnitude;
            if (m > D(OuterRadius)) { Reset(); return PlacementAction.Cancel; }
            if (m < D(SwipeThreshold)) { Held = true; Dir = -1; return PlacementAction.None; } // start a new swipe from the centre
            Dir = DirOf(v); // tapped an arrow
            if (ValidFor(Dir)) { Reset(); return PlacementAction.Place; }
            return PlacementAction.SetDirection;
        }

        /// Controller may move the indicator centre (e.g. onto the ghost cell after a release).
        public void SetCentre(Vector2 c) => Centre = c;
        public void Reset() { State = PlacementState.Idle; Card = -1; wasArmed = false; Held = false; dragCell = null; }

        void Enter(Vector2Int cell, Vector2 centre, bool held) { State = PlacementState.Direction; Cell = cell; Centre = centre; Held = held; Dir = -1; }

        /// 0 up, 1 right, 2 down, 3 left (screen space, y up).
        public static int DirOf(Vector2 v) => Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? 1 : 3) : (v.y > 0 ? 0 : 2);
    }
}
