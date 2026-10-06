using UnityEngine;

namespace StoneSignal
{
    public sealed class FloatingDamage : MonoBehaviour
    {
        private Camera viewCamera;
        private TextMesh text;
        private float age;
        public void Initialize(Camera camera) { viewCamera=camera; text=GetComponent<TextMesh>(); }
        private void Update()
        {
            age+=Time.deltaTime; transform.position+=Vector3.up*Time.deltaTime*.8f;
            transform.rotation=viewCamera.transform.rotation;
            Color color=text.color; color.a=Mathf.Clamp01(1-age/.7f); text.color=color;
            if(age>=.7f) Destroy(gameObject);
        }
    }
}
