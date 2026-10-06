using UnityEngine;

namespace StoneSignal
{
    public sealed class CombatFeedback : MonoBehaviour
    {
        private EnemyManager enemies;
        private VisualPalette palette;
        private Camera viewCamera;
        private Font font;
        public void Initialize(EnemyManager registry, VisualPalette colors, Camera camera)
        {
            enemies=registry; palette=colors; viewCamera=camera; font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            enemies.Spawned+=OnSpawn; enemies.Resolved+=OnResolved; Projectile.Impact+=OnImpact;
        }
        private void OnSpawn(Enemy enemy) { enemy.Damaged+=OnDamage; }
        private void OnDamage(Enemy enemy,float damage)
        {
            var obj=new GameObject("Damage number"); obj.transform.SetParent(transform); obj.transform.position=enemy.transform.position+Vector3.up;
            var text=obj.AddComponent<TextMesh>(); text.font=font; text.fontSize=42; text.characterSize=.07f; text.anchor=TextAnchor.MiddleCenter; text.alignment=TextAlignment.Center; text.color=new Color(1,.89f,.5f); text.text=Mathf.RoundToInt(damage).ToString();
            text.GetComponent<MeshRenderer>().sharedMaterial=font.material;
            obj.AddComponent<FloatingDamage>().Initialize(viewCamera);
        }
        private void OnResolved(Enemy enemy,EnemyResolution reason)
        {
            enemy.Damaged-=OnDamage;
            if(reason==EnemyResolution.Killed) Burst(enemy.transform.position,new Color(1,.65f,.25f),16);
        }
        private void OnImpact(Vector3 point,float radius) { Burst(point,radius>0 ? new Color(1,.6f,.25f) : new Color(.5f,1,.9f),radius>0?20:7); }
        private void Burst(Vector3 point,Color color,int count)
        {
            var obj=new GameObject("Impact particles"); obj.transform.SetParent(transform); obj.transform.position=point;
            var particles=obj.AddComponent<ParticleSystem>(); particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main; main.loop=false; main.duration=.3f; main.startLifetime=new ParticleSystem.MinMaxCurve(.2f,.5f); main.startSpeed=new ParticleSystem.MinMaxCurve(.5f,2); main.startSize=new ParticleSystem.MinMaxCurve(.04f,.13f); main.startColor=color; main.gravityModifier=.6f; main.simulationSpace=ParticleSystemSimulationSpace.World; main.maxParticles=32;
            var emission=particles.emission; emission.enabled=false;
            var shape=particles.shape; shape.shapeType=ParticleSystemShapeType.Sphere; shape.radius=.09f;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial=palette.particle;
            particles.Play(); particles.Emit(count); Destroy(obj,.7f);
        }
        private void OnDestroy()
        {
            Projectile.Impact-=OnImpact;
            if(enemies==null) return;
            enemies.Spawned-=OnSpawn; enemies.Resolved-=OnResolved;
            foreach(Enemy enemy in enemies.Active) if(enemy!=null) enemy.Damaged-=OnDamage;
        }
    }
}
