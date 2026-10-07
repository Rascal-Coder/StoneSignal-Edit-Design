using UnityEngine;

namespace StoneSignal.VFX
{
    /// Dashed animated range ring (quad, StoneSignal/PlaceFX mode 1). SetRadius in world units.
    public class RangeRing : MonoBehaviour
    {
        public void SetRadius(float r) { transform.localScale = new Vector3(r * 2, 1, r * 2); }
    }
}
