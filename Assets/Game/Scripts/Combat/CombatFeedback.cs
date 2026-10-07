using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    // Presentation-only reactions to combat events: damage numbers, death/coin/split VFX and core hits.
    public sealed class CombatFeedback : MonoBehaviour
    {
        private EnemyManager enemies;
        private VisualPalette palette;
        private Camera viewCamera;
        public void Initialize(EnemyManager registry, VisualPalette colors, Camera camera)
        {
            enemies=registry; palette=colors; viewCamera=camera;
            if (viewCamera != null && viewCamera.GetComponent<CameraShake>() == null) viewCamera.gameObject.AddComponent<CameraShake>();
            enemies.Spawned+=OnSpawn; enemies.Resolved+=OnResolved; Projectile.Impact+=OnImpact;
        }
        private void OnSpawn(Enemy enemy) { enemy.Damaged+=OnDamage; }
        private void OnDamage(Enemy enemy,float damage)
        {
            DamageNumbers.Spawn(enemy.transform.position+Vector3.up*.6f,damage,enemy.LastHitKind,enemy.LastHitCrit,enemy);
        }
        private void OnResolved(Enemy enemy,EnemyResolution reason)
        {
            enemy.Damaged-=OnDamage;
            var data=enemy.Data; Vector3 at=enemy.transform.position;
            if(reason==EnemyResolution.Killed)
            {
                bool styled=data.deathVfx!=null || data.coinVfx!=null;
                if(Enemy.DeathFxAvailable)
                {
                    // v16.2: poof + skull (enemy hidden this frame by Enemy.Resolve); coin drop follows in onDone
                    var coin=data.coinVfx; styled=true;
                    StoneSignal.VFX.EnemyDeathFx.Play(at,Mathf.Max(.5f,enemy.transform.lossyScale.x),()=>{ if(coin!=null) StylizedVfx.Play(coin,at+Vector3.up*.3f); });
                }
                else
                {
                    if(data.deathVfx!=null) StylizedVfx.Play(data.deathVfx,at);
                    if(data.coinVfx!=null) StylizedVfx.Play(data.coinVfx,at+Vector3.up*.3f);
                }
                if(data.splitChild!=null && data.splitCount>0 && data.splitVfx!=null) StylizedVfx.Play(data.splitVfx,at);
                if(!styled) Burst(at,new Color(1,.65f,.25f),16);
            }
            else if(reason==EnemyResolution.Escaped)
            {
                var art=palette!=null ? palette.art : null;
                if(art!=null && art.coreHitVfx!=null) StylizedVfx.Play(art.coreHitVfx,at);
                CameraShake.Shake(CameraShake.Preset.Heavy);
            }
        }
        // Stylized towers play their own impact VFX; only primitive fallbacks use the old particle burst.
        private void OnImpact(Vector3 point,float radius) { if(palette!=null && palette.art!=null && palette.art.coreHitVfx!=null) return; Burst(point,radius>0 ? new Color(1,.6f,.25f) : new Color(.5f,1,.9f),radius>0?20:7); }
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
            HitStop.Cancel();
            if(enemies==null) return;
            enemies.Spawned-=OnSpawn; enemies.Resolved-=OnResolved;
            foreach(Enemy enemy in enemies.Active) if(enemy!=null) enemy.Damaged-=OnDamage;
        }
    }
}
