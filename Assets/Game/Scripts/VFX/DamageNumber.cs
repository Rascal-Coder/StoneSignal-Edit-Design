using TMPro;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// One pooled floating number (PF_FX_DamageNumber). Driven by DamageNumbers; pop -> arc -> fall -> fade, unscaled time.
    [RequireComponent(typeof(TextMeshPro))]
    public class DamageNumber : MonoBehaviour
    {
        public float lifetime = 0.9f, gravity = 6f, popScale = 1.45f, popTime = 0.14f;
        internal TextMeshPro text; internal float amount, age, baseScale = 1; internal int key; internal bool crit;
        Vector3 _vel; Color _color;

        void Awake() { text = GetComponent<TextMeshPro>(); }

        internal void Begin(Vector3 pos, float value, Color color, bool isCrit, float scale)
        {
            transform.position = pos; amount = value; crit = isCrit; _color = color; baseScale = scale; age = 0;
            _vel = new Vector3(StoneSignal.GameRng.Vfx.Range(-0.7f, 0.7f), isCrit ? 3.0f : 2.4f, StoneSignal.GameRng.Vfx.Range(-0.3f, 0.3f));
            Refresh(); gameObject.SetActive(true);
        }

        internal void Stack(float value)
        {
            amount += value; age = Mathf.Min(age, popTime * 0.5f);    // re-pop and extend
            _vel.y = Mathf.Max(_vel.y, 1.2f); Refresh();
        }

        void Refresh()
        {
            text.text = crit ? Mathf.RoundToInt(amount) + "!" : Mathf.RoundToInt(amount).ToString();
            text.color = _color;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime; age += dt;
            _vel.y -= gravity * dt; transform.position += _vel * dt;
            float pop = age < popTime ? Mathf.Lerp(0.3f, popScale, age / popTime) : Mathf.Lerp(popScale, 1f, Mathf.Clamp01((age - popTime) / 0.12f));
            transform.localScale = Vector3.one * baseScale * pop;
            var cam = Camera.main; if (cam) transform.rotation = cam.transform.rotation;
            float fade = Mathf.Clamp01((lifetime - age) / 0.25f);
            text.alpha = fade;
            if (age >= lifetime) DamageNumbers.Release(this);
        }
    }
}
