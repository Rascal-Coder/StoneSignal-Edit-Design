using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StoneSignal.VFX;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using PSS = UnityEngine.ParticleSystem;

/// Builds the stylized combat VFX set (Assets/Game/VFX/Stylized), the TMP damage-number prefab and the
/// StylizedVFXShowcase scene + preview captures. Procedural shaders only (StoneSignal/FXAdditive|FXAlpha), no textures.
public static class StylizedVFXBuilder
{
    public const string Dir = "Assets/Game/VFX/Stylized/";
    const string MatDir = Dir + "Materials/";
    public const string ResDir = Dir + "Resources/StylizedVFX/";
    const string ShowcasePath = "Assets/Game/Scenes/StylizedVFXShowcase.unity";
    const string PreviewDir = "Verification/StylizedArt1/";
    static readonly string[] ShapeNames = { "Dot", "Ring", "Streak", "Smoke", "Flash", "Scorch", "Coin", "Trail" };
    const int Dot = 0, Ring = 1, Streak = 2, Smoke = 3, Flash = 4, Scorch = 5, Coin = 6, Trail = 7;

    /// prefab name -> what uses it (documentation + check list)
    public static readonly List<(string prefab, string usedBy)> Built = new List<(string, string)>();

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
    static Color A(Color c, float a) { c.a = a; return c; }

    static Material M(bool add, int shape, float soft = .5f, float ring = .15f)
    {
        string name = $"M_FX_{(add ? "Add" : "Alpha")}_{ShapeNames[shape]}";
        string path = MatDir + name + ".mat";
        var sh = Shader.Find(add ? "StoneSignal/FXAdditive" : "StoneSignal/FXAlpha");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh; m.SetFloat("_Shape", shape); m.SetFloat("_Softness", soft); m.SetFloat("_RingWidth", ring);
        m.SetColor("_TintColor", Color.white); m.SetFloat("_Intensity", add ? 1.6f : 1f);
        m.renderQueue = add ? 3100 : 3000; EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- particle helpers
    static GameObject Root(string name, float duration)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.duration = duration; m.loop = false; m.playOnAwake = true; m.stopAction = ParticleSystemStopAction.Destroy;
        m.startLifetime = .1f; m.maxParticles = 1;
        var e = ps.emission; e.enabled = false; var s = ps.shape; s.enabled = false;
        go.GetComponent<ParticleSystemRenderer>().enabled = false;
        return go;
    }

    static ParticleSystem PS(GameObject parent, string name, Material mat, PSS.MinMaxCurve life, PSS.MinMaxCurve size, PSS.MinMaxCurve speed,
        Color c, int burst, float rate = 0, bool loop = false, float dur = 1f, float radius = .1f)
    {
        var go = new GameObject(name); go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        var m = ps.main; m.duration = dur; m.loop = loop; m.startLifetime = life; m.startSize = size; m.startSpeed = speed; m.startColor = c;
        m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.maxParticles = 200;
        var e = ps.emission; e.rateOverTime = rate; e.SetBursts(burst > 0 ? new[] { new PSS.Burst(0, (short)burst) } : new PSS.Burst[0]);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
        return ps;
    }
    static void Fade(ParticleSystem ps, Color a, Color b, float holdAlpha = .2f)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, holdAlpha), new GradientAlphaKey(0, 1) });
        col.color = g;
    }
    static void Size(ParticleSystem ps, float from, float to)
    { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new PSS.MinMaxCurve(1, AnimationCurve.EaseInOut(0, from, 1, to)); }
    static void SizeOut(ParticleSystem ps, float from, float to) // fast-out (shockwave)
    { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new PSS.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, from, 0, 4), new Keyframe(1, to, 0, 0))); }
    static void Flat(ParticleSystem ps, float y = 0)
    {
        ps.transform.localRotation = Quaternion.Euler(90, 0, 0); ps.transform.localPosition = new Vector3(0, y, 0);
        ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        var sh = ps.shape; sh.enabled = false;
    }
    static void Up(ParticleSystem ps, ParticleSystemShapeType t = ParticleSystemShapeType.Hemisphere, float angle = 25)
    { var sh = ps.shape; sh.shapeType = t; sh.angle = angle; sh.rotation = new Vector3(-90, 0, 0); }
    static void Stretch(ParticleSystem ps, float vel = .06f, float len = 1.2f)
    { var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = vel; r.lengthScale = len; }
    static void Gravity(ParticleSystem ps, float g) { var m = ps.main; m.gravityModifier = g; }
    static void Drag(ParticleSystem ps, float d) { var l = ps.limitVelocityOverLifetime; l.enabled = true; l.drag = d; l.multiplyDragByParticleSize = false; }
    static void Spin(ParticleSystem ps) { var m = ps.main; m.startRotation = new PSS.MinMaxCurve(0, Mathf.PI * 2); }
    static PSS.MinMaxCurve R(float a, float b) => new PSS.MinMaxCurve(a, b);

    // Readability scale vs the Game.unity camera (orthographic, size 9.2, ~44 deg pitch): one-shot effects are
    // authored at 1 and scaled on the prefab root (children use Hierarchy scaling). API/prefab names unchanged.
    static float ScaleFor(string n) => n.StartsWith("FX_Explosion") ? 1.7f : n.StartsWith("FX_Hit") ? 1.5f : n.StartsWith("FX_Enemy") ? 1.4f
        : n.StartsWith("FX_Muzzle") ? 1.4f : n.StartsWith("FX_Proj") ? 1.35f : 1f;
    static void Save(GameObject go, string usedBy, string dir = Dir)
    {
        go.transform.localScale = Vector3.one * ScaleFor(go.name);
        foreach (var tr in go.GetComponentsInChildren<TrailRenderer>()) tr.widthMultiplier *= ScaleFor(go.name);
        foreach (var lr in go.GetComponentsInChildren<LineRenderer>()) lr.widthMultiplier *= ScaleFor(go.name);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
        { var m = ps.main; m.playOnAwake = true; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        string n = go.name;
        PrefabUtility.SaveAsPrefabAsset(go, dir + n + ".prefab");
        UnityEngine.Object.DestroyImmediate(go);
        Built.Add((n, usedBy));
    }

    // ---------- explosions
    struct Elem { public string id, usedBy; public Color flash, core, smoke, debris, scorch; public bool embers, shards, arcs, dust; }
    static readonly Elem[] Elements =
    {
        new Elem { id = "HE", usedBy = "Seismic (PF_Tower_Mortar_2x2) shell impact / Cannon (spare) / Boss death", flash = H("FFF4C8"), core = H("FF9A2E"), smoke = H("4A3A48"), debris = H("6B5A60"), scorch = H("241A24") },
        new Elem { id = "Fire", usedBy = "Flamer (spare PF_Tower_Flamer_1x2) / fire-element upgrades", flash = H("FFE08A"), core = H("FF4A1A"), smoke = H("3A2A30"), debris = H("FF7A2A"), scorch = H("2A1418"), embers = true },
        new Elem { id = "Ice", usedBy = "Chill (PF_Tower_Frost_1x1) shard burst / freeze proc", flash = H("EFFFFF"), core = H("7FD8FF"), smoke = H("C8E8FF"), debris = H("BFF0FF"), scorch = H("9FD4F0"), shards = true },
        new Elem { id = "Lightning", usedBy = "Pulse (PF_Tower_Tesla_1x1) chain / overload proc", flash = H("F4F0FF"), core = H("9A7BFF"), smoke = H("5A4A80"), debris = H("D8C8FF"), scorch = H("2A2440"), arcs = true },
        new Elem { id = "Kinetic", usedBy = "Needle (PF_Tower_Gatling_1x1) crit / generic physical big hit", flash = H("FFFFFF"), core = H("FFD860"), smoke = H("B8A898"), debris = H("8A7A70"), scorch = H("3A3030"), dust = true },
    };

    static void Explosion(Elem e, float s = 1)
    {
        var root = Root("FX_Explosion_" + e.id, 4.2f);
        var add = M(true, Dot, .7f); var addFlash = M(true, Flash); var addRing = M(true, Ring, .5f, .18f);
        var f = PS(root, "Flash", addFlash, .14f, 2.6f * s, 0, e.flash, 1); Size(f, 1, .5f); Fade(f, e.flash, e.core, .3f);
        var fb = PS(root, "Fireball", add, R(.25f, .45f), R(.5f * s, 1.0f * s), R(1.5f, 3.2f), e.core, e.arcs ? 6 : 12, radius: .25f * s);
        Size(fb, 1, .2f); Fade(fb, e.flash, e.smoke, .25f); Drag(fb, 3);
        var ring = PS(root, "Shockwave", addRing, .38f, 3.8f * s, 0, A(e.core, .9f), 1); Flat(ring, .08f); SizeOut(ring, .08f, 1); Fade(ring, e.flash, e.core, .1f);
        var deb = PS(root, "Debris", M(false, Dot, .08f), R(.6f, 1.0f), R(.07f * s, .16f * s), R(3.5f, 7f), e.debris, e.shards ? 18 : 14, radius: .2f);
        Up(deb, ParticleSystemShapeType.Cone, 40); Gravity(deb, 1.8f); Spin(deb);
        if (e.shards) { var r = deb.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = M(true, Streak); Stretch(deb, .05f, 2f); }
        var sp = PS(root, "Sparks", M(true, Streak), R(.18f, .4f), R(.05f, .09f), R(6f, 11f), e.flash, e.arcs ? 26 : 16, radius: .15f);
        Stretch(sp, .07f, 1.3f); Fade(sp, e.flash, e.core, .4f); Gravity(sp, .6f);
        var sm = PS(root, "Smoke", M(false, Smoke, .15f), R(1.0f, 1.7f), R(.7f * s, 1.25f * s), R(.4f, 1.1f), A(e.smoke, .9f), e.arcs ? 4 : 8, radius: .35f * s);
        Size(sm, .5f, 1.5f); Fade(sm, Color.Lerp(e.smoke, Color.white, .25f), e.smoke, .35f); Gravity(sm, -.12f); Drag(sm, 1.5f); Spin(sm);
        var sc = PS(root, "Scorch", M(false, Scorch), 4f, 2.4f * s, 0, A(e.scorch, .8f), 1); Flat(sc, .03f); Fade(sc, e.scorch, e.scorch, .7f); Spin(sc);
        if (e.embers)
        {
            var em = PS(root, "Embers", add, R(.9f, 1.6f), R(.05f, .1f), R(.5f, 2f), H("FFB040"), 22, radius: .5f);
            Gravity(em, -.25f); var n = em.noise; n.enabled = true; n.strength = .8f; n.frequency = .9f; Fade(em, H("FFE08A"), H("FF3A10"), .6f);
        }
        if (e.arcs)
        {
            var ring2 = PS(root, "ArcRing", addRing, .22f, 2.4f * s, 0, e.flash, 1); Flat(ring2, .5f); SizeOut(ring2, .2f, 1);
            ring2.transform.localRotation = Quaternion.Euler(20, 0, 30);
            var zap = PS(root, "Zaps", M(true, Streak), R(.06f, .12f), R(.08f, .14f), R(14f, 20f), H("E8DEFF"), 10, radius: .2f);
            Stretch(zap, .05f, 3f); var b = zap.emission; b.SetBursts(new[] { new PSS.Burst(0, 6), new PSS.Burst(.08f, 6), new PSS.Burst(.16f, 4) });
        }
        if (e.dust)
        {
            var d = PS(root, "DustRing", M(false, Smoke, .2f), R(.6f, 1f), R(.4f, .7f), R(3f, 4.5f), A(H("C8B8A8"), .8f), 18, radius: .3f);
            var sh = d.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.rotation = new Vector3(-90, 0, 0); sh.radiusThickness = 0;
            Drag(d, 5); Size(d, .6f, 1.4f); Spin(d);
        }
        Save(root, e.usedBy);
    }

    // ---------- muzzle flashes (local +Z = barrel forward)
    static void Muzzle(string tower, string usedBy, Color flash, Color core, float s, bool smoke = true)
    {
        var root = Root("FX_Muzzle_" + tower, 1.2f);
        var f = PS(root, "Flash", M(true, Flash), .07f, .9f * s, 0, flash, 1); Size(f, 1, .6f);
        var c = PS(root, "Cone", M(true, Streak), R(.06f, .1f), R(.18f * s, .28f * s), R(5f, 9f), core, 6, radius: .02f);
        var sh = c.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 14; sh.radius = .02f; Stretch(c, .04f, 2.2f); Fade(c, flash, core, .3f);
        var sp = PS(root, "Sparks", M(true, Streak), R(.12f, .25f), .04f, R(3f, 6f), flash, 8, radius: .02f);
        var sh2 = sp.shape; sh2.shapeType = ParticleSystemShapeType.Cone; sh2.angle = 35; Stretch(sp, .06f, 1); Gravity(sp, .5f);
        if (smoke)
        {
            var sm = PS(root, "Smoke", M(false, Smoke, .2f), R(.4f, .7f), R(.25f * s, .4f * s), R(.6f, 1.4f), A(H("D8CCD8"), .7f), 4, radius: .05f);
            var sh3 = sm.shape; sh3.shapeType = ParticleSystemShapeType.Cone; sh3.angle = 20; Size(sm, .5f, 1.6f); Drag(sm, 4); Gravity(sm, -.1f); Spin(sm);
        }
        Save(root, usedBy);
    }

    static void TeslaMuzzle()
    {
        var root = Root("FX_Muzzle_Tesla", 1f);
        var f = PS(root, "Flash", M(true, Flash), .1f, 1.1f, 0, H("E8DEFF"), 1);
        var ring = PS(root, "Ring", M(true, Ring, .5f, .2f), .2f, 1.4f, 0, H("9A7BFF"), 1); Flat(ring); SizeOut(ring, .1f, 1);
        var z = PS(root, "Zaps", M(true, Streak), R(.05f, .1f), R(.06f, .1f), R(8f, 14f), H("C9B8FF"), 12, radius: .2f); Stretch(z, .05f, 2.5f);
        Save(root, "Pulse (PF_Tower_Tesla_1x1) discharge at coil top");
    }

    static void FlamerStream()
    {
        var root = new GameObject("FX_Muzzle_Flamer");
        var fl = PS(root, "Flame", M(true, Dot, .8f), R(.3f, .5f), R(.2f, .35f), R(5f, 7f), H("FFE08A"), 0, rate: 70, loop: true, dur: 1);
        var sh = fl.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12; sh.radius = .05f;
        Size(fl, .6f, 3f); Fade(fl, H("FFF0B0"), H("FF3A10"), .5f); Drag(fl, 2); Gravity(fl, -.3f); Spin(fl);
        var sm = PS(root, "Smoke", M(false, Smoke, .2f), R(.5f, .8f), R(.4f, .6f), R(3f, 4.5f), A(H("3A2A30"), .5f), 0, rate: 18, loop: true);
        var sh2 = sm.shape; sh2.shapeType = ParticleSystemShapeType.Cone; sh2.angle = 16; Size(sm, .5f, 2.4f); Drag(sm, 2); Gravity(sm, -.3f);
        var em = PS(root, "Embers", M(true, Streak), R(.3f, .6f), .04f, R(5f, 8f), H("FFB040"), 0, rate: 25, loop: true); Stretch(em, .05f, 1);
        var sh3 = em.shape; sh3.shapeType = ParticleSystemShapeType.Cone; sh3.angle = 18;
        Save(root, "Flamer (spare PF_Tower_Flamer_1x2) looping stream: Play() while firing, Stop() when idle (not self-destroying)");
    }

    // ---------- projectiles (visual only; gameplay moves the transform along +Z)
    static GameObject TrailObj(GameObject parent, Color c, float width, float time)
    {
        var go = new GameObject("Trail"); go.transform.SetParent(parent.transform, false);
        var t = go.AddComponent<TrailRenderer>(); t.sharedMaterial = M(true, Trail); t.time = time; t.minVertexDistance = .05f;
        t.widthCurve = AnimationCurve.Linear(0, width, 1, 0); t.shadowCastingMode = ShadowCastingMode.Off; t.receiveShadows = false;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, .3f), new GradientColorKey(c, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        t.colorGradient = g; return go;
    }
    static void Glow(GameObject parent, Color c, float size)
    {
        var g = PS(parent, "Glow", M(true, Dot, .9f), .12f, size, 0, c, 0, rate: 30, loop: true);
        var m = g.main; m.simulationSpace = ParticleSystemSimulationSpace.Local; var s = g.shape; s.enabled = false;
    }
    static void Ball(GameObject parent, float size, Material mat)
    {
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); var mesh = tmp.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(tmp);
        var go = new GameObject("Ball"); go.transform.SetParent(parent.transform, false); go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
    }
    static void SmokeTrail(GameObject parent, Color c, float rate, float size)
    {
        var s = PS(parent, "SmokeTrail", M(false, Smoke, .2f), R(.4f, .7f), R(size * .7f, size), 0, A(c, .7f), 0, rate: rate, loop: true, radius: .03f);
        Size(s, .6f, 1.5f); Spin(s); var e = s.emission; e.rateOverDistance = rate * .3f; e.rateOverTime = 0;
    }

    static void Projectiles()
    {
        var toon = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/M_Tower_Cannon.mat");
        var b = new GameObject("FX_Proj_Bullet"); Glow(b, H("FFE6A0"), .22f); TrailObj(b, H("FFB040"), .07f, .09f);
        Save(b, "Needle (PF_Tower_Gatling_1x1) tracer round");
        var c = new GameObject("FX_Proj_Cannonball"); Ball(c, .24f, toon); SmokeTrail(c, H("C8BCC8"), 40, .22f); TrailObj(c, H("FF9A2E"), .1f, .06f);
        Save(c, "Cannon (spare PF_Tower_Cannon_1x1) ball; impact -> FX_Explosion_Kinetic or HE");
        var m = new GameObject("FX_Proj_MortarShell"); Ball(m, .34f, toon); SmokeTrail(m, H("8A7A88"), 50, .3f); TrailObj(m, H("FF7A2A"), .16f, .12f); Glow(m, H("FF9A2E"), .5f);
        Save(m, "Seismic (PF_Tower_Mortar_2x2) arcing shell; impact -> FX_Explosion_HE");
        var f = new GameObject("FX_Proj_FrostShard"); Glow(f, H("BFF0FF"), .35f); TrailObj(f, H("7FD8FF"), .12f, .18f);
        var sp = PS(f, "Sparkle", M(true, Flash), R(.2f, .35f), R(.08f, .14f), R(.1f, .4f), H("E8FFFF"), 0, rate: 30, loop: true); var e = sp.emission; e.rateOverDistance = 8;
        Save(f, "Chill (PF_Tower_Frost_1x1) frost bolt; impact -> FX_Hit_Ice");
        var fb = new GameObject("FX_Proj_Fireball"); Glow(fb, H("FFB040"), .45f); TrailObj(fb, H("FF4A1A"), .2f, .15f); SmokeTrail(fb, H("3A2A30"), 30, .25f);
        Save(fb, "Flamer / fire-element lob; impact -> FX_Explosion_Fire");
        var t = new GameObject("FX_Proj_Tesla_Arc");
        var lr = t.AddComponent<LineRenderer>(); lr.sharedMaterial = M(true, Trail); lr.widthMultiplier = .09f; lr.numCapVertices = 2;
        lr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(H("F4F0FF"), 0), new GradientColorKey(H("9A7BFF"), 1) }, alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) } };
        lr.shadowCastingMode = ShadowCastingMode.Off; t.AddComponent<TeslaArc>();
        Save(t, "Pulse (PF_Tower_Tesla_1x1) chain arc: Instantiate, TeslaArc.SetEndpoints(coilTop, enemy); auto-destroys after lifetime");
    }

    static void Hits()
    {
        void Hit(string id, Color a, Color b, string usedBy, int n = 10)
        {
            var root = Root("FX_Hit_" + id, 1f);
            var f = PS(root, "Flash", M(true, Flash), .08f, .7f, 0, a, 1);
            var sp = PS(root, "Sparks", M(true, Streak), R(.12f, .28f), R(.04f, .07f), R(3.5f, 7f), a, n, radius: .05f); Stretch(sp, .06f, 1.2f); Fade(sp, a, b, .3f); Gravity(sp, .8f);
            var ring = PS(root, "Ring", M(true, Ring, .5f, .25f), .16f, .8f, 0, b, 1); SizeOut(ring, .2f, 1);
            Save(root, usedBy);
        }
        Hit("Kinetic", H("FFFFFF"), H("FFD860"), "Needle / Cannon bullet hit on enemy");
        Hit("Fire", H("FFE08A"), H("FF4A1A"), "Flamer / fire damage tick");
        Hit("Ice", H("EFFFFF"), H("7FD8FF"), "Chill frost bolt hit (+ slow)");
        Hit("Lightning", H("F4F0FF"), H("9A7BFF"), "Pulse arc hit per chained enemy", 14);
        Hit("Core", H("CFF4FF"), H("4AA8FF"), "Enemy reaches PF_Prop_Core (core damage)", 20);
    }

    static void EnemyFx()
    {
        var d = Root("FX_Enemy_DeathPuff", 1.6f);
        var st = PS(d, "Star", M(true, Flash), .12f, 1.4f, 0, H("FFFFFF"), 1);
        var sm = PS(d, "Puff", M(false, Smoke, .15f), R(.5f, .8f), R(.45f, .7f), R(1.5f, 2.5f), A(H("F0E6F0"), .95f), 10, radius: .2f);
        Drag(sm, 6); Size(sm, .7f, 1.3f); Spin(sm); Fade(sm, Color.white, H("C8B8D0"), .3f);
        var r = PS(d, "Ring", M(true, Ring, .5f, .2f), .25f, 1.8f, 0, H("FFD0E0"), 1); Flat(r, .05f); SizeOut(r, .1f, 1);
        Save(d, "Any enemy death (with EnemyHitFeedback.PlayDeath dissolve)");
        var c = Root("FX_Enemy_CoinPop", 1.6f);
        var coins = PS(c, "Coins", M(false, Coin), 1.1f, R(.18f, .24f), R(3.2f, 4.2f), H("FFD23A"), 6, radius: .05f);
        Up(coins, ParticleSystemShapeType.Cone, 22); Gravity(coins, 1.6f); var rot = coins.rotationBySpeed; rot.enabled = false;
        var sOL = coins.sizeOverLifetime; sOL.enabled = true; sOL.separateAxes = true; sOL.x = new PSS.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.25f, .2f), new Keyframe(.5f, 1), new Keyframe(.75f, .2f), new Keyframe(1, 1))); sOL.y = new PSS.MinMaxCurve(1, AnimationCurve.Constant(0, 1, 1)); // fake coin spin
        Fade(coins, H("FFE070"), H("FFC020"), .8f);
        var gl = PS(c, "Glints", M(true, Flash), R(.2f, .4f), R(.12f, .2f), R(1f, 2f), H("FFF4B0"), 6, radius: .2f); Up(gl);
        Save(c, "Enemy death reward (gold); BALANCE reward pop");
        var s = Root("FX_Enemy_SplitBurst", 1.6f);
        var fl = PS(s, "Flash", M(true, Flash), .1f, 1.6f, 0, H("F4D8FF"), 1);
        var goo = PS(s, "Goo", M(false, Dot, .1f), R(.5f, .8f), R(.12f, .26f), R(2.5f, 5f), H("B04AE0"), 16, radius: .15f); Up(goo, ParticleSystemShapeType.Cone, 50); Gravity(goo, 1.4f);
        Fade(goo, H("D07AF0"), H("7A2AA0"), .7f);
        var ring = PS(s, "Ring", M(true, Ring, .5f, .2f), .3f, 2.2f, 0, H("C060FF"), 1); Flat(ring, .05f); SizeOut(ring, .1f, 1);
        var sm2 = PS(s, "Smoke", M(false, Smoke, .2f), R(.6f, .9f), R(.4f, .6f), R(1f, 2f), A(H("8A4AA8"), .8f), 6, radius: .2f); Drag(sm2, 4); Spin(sm2);
        Save(s, "Splitter death -> spawns children (PF_Enemy_Splitter)");
    }

    static TMP_FontAsset Font()
    {
        const string fp = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fp);
        if (f == null)
        {
            string pkg = Path.GetFullPath("Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage");
            Debug.Log("STYLIZED VFX importing TMP Essential Resources from " + pkg);
            AssetDatabase.ImportPackage(pkg, false); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fp);
        }
        return f;
    }

    static void DamageNumberPrefab()
    {
        Directory.CreateDirectory(ResDir);
        var font = Font(); if (font == null) throw new Exception("TMP font LiberationSans SDF missing");
        string mp = Dir + "Materials/M_FX_DamageNumber.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        var sh = Shader.Find("TextMeshPro/Distance Field Overlay") ?? Shader.Find("TextMeshPro/Distance Field");
        if (mat == null) { mat = new Material(font.material); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = sh; mat.SetTexture("_MainTex", font.material.GetTexture("_MainTex"));
        mat.EnableKeyword("OUTLINE_ON"); mat.SetFloat("_OutlineWidth", .28f); mat.SetColor("_OutlineColor", H("1E1A3A")); mat.SetFloat("_FaceDilate", .25f);
        mat.EnableKeyword("UNDERLAY_ON"); mat.SetColor("_UnderlayColor", A(H("1E1A3A"), .6f)); mat.SetFloat("_UnderlayOffsetY", -.6f); mat.SetFloat("_UnderlaySoftness", .2f);
        EditorUtility.SetDirty(mat);
        var go = new GameObject("PF_FX_DamageNumber");
        var t = go.AddComponent<TextMeshPro>(); t.font = font; t.fontSharedMaterial = mat; t.fontSize = 5; t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.text = "123"; t.rectTransform.sizeDelta = new Vector2(4, 1.5f);
        t.sortingOrder = 100;
        go.AddComponent<DamageNumber>();
        PrefabUtility.SaveAsPrefabAsset(go, ResDir + go.name + ".prefab"); UnityEngine.Object.DestroyImmediate(go);
        Built.Add(("PF_FX_DamageNumber", "DamageNumbers.Spawn (all damage events; crit = bigger gold '!')"));
    }

    [MenuItem("StoneSignal/Stylized art/Build VFX + showcase")]
    public static void BuildAll()
    {
        Built.Clear();
        Directory.CreateDirectory(Dir); Directory.CreateDirectory(MatDir); Directory.CreateDirectory(ResDir);
        foreach (var e in Elements) Explosion(e);
        Muzzle("Cannon", "Cannon (spare PF_Tower_Cannon_1x1) shot", H("FFF4C8"), H("FF9A2E"), 1.2f);
        Muzzle("Gatling", "Needle (PF_Tower_Gatling_1x1) each round (alternate barrels)", H("FFF4C8"), H("FFC040"), .7f, false);
        Muzzle("Frost", "Chill (PF_Tower_Frost_1x1) shot", H("EFFFFF"), H("7FD8FF"), .9f);
        Muzzle("Mortar", "Seismic (PF_Tower_Mortar_2x2) launch (orient +Z up the barrel)", H("FFF4C8"), H("FF7A2A"), 1.6f);
        TeslaMuzzle(); FlamerStream(); Projectiles(); Hits(); EnemyFx(); DamageNumberPrefab();
        AssetDatabase.SaveAssets();
        BuildShowcase();
    }

    // ---------- showcase scene + captures
    static GameObject Prefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
    static GameObject Put(string path, Vector3 p, float yaw = 0, Transform parent = null)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(path), parent); if (go == null) throw new Exception("Missing " + path);
        go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0)); return go;
    }
    static GameObject Fx(string name, Vector3 p, float t, Quaternion? rot = null, Transform parent = null)
    {
        var go = Put(Dir + name + ".prefab", p, 0, parent); if (rot.HasValue) go.transform.rotation = rot.Value;
        var ps = go.GetComponent<ParticleSystem>();
        foreach (var c in go.GetComponentsInChildren<ParticleSystem>()) { c.useAutoRandomSeed = false; c.randomSeed = 7; }
        if (ps) ps.Simulate(t, true, true); else foreach (Transform ch in go.transform) { var cp = ch.GetComponent<ParticleSystem>(); if (cp) cp.Simulate(t, true, true); }
        return go;
    }
    /// Mortar barrel tip: the *_Muzzle transform when present; otherwise top of head bounds aimed at a 60 deg lob.
    public static (Vector3 pos, Quaternion rot) MortarBarrelTip(Transform head, Vector3 dir)
    {
        // v6 hierarchy: use the authored *_Muzzle (+Z = fire direction, follows the 28 deg barrel). Bounds fallback for older prefabs.
        var mz = head.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith("_Muzzle")); if (mz) return (mz.position, mz.rotation);
        var rs = head.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return (head.position + Vector3.up, Quaternion.LookRotation(Vector3.up));
        var b = rs.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        var aim = (dir * Mathf.Cos(60 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(60 * Mathf.Deg2Rad)).normalized;
        return (new Vector3(b.center.x, b.max.y, b.center.z) + dir * b.extents.x * .35f, Quaternion.LookRotation(aim));
    }
    static Transform _sec;
    static TextMeshPro Label(string s, Vector3 p, Camera cam, float size = 2.2f, Color? c = null)
    {
        var go = new GameObject("Label " + s); var t = go.AddComponent<TextMeshPro>(); t.text = s; t.fontSize = size; t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false; t.color = c ?? Color.white; t.fontStyle = FontStyles.Bold;
        var dm = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Materials/M_FX_DamageNumber.mat"); if (dm) t.fontSharedMaterial = dm;
        go.transform.SetParent(_sec, false); go.transform.position = p; go.transform.rotation = cam.transform.rotation; t.ForceMeshUpdate(); return t;
    }
    static void Floor(Transform world, int w, int d)
    {
        for (int x = -w; x <= w; x++) for (int z = -d; z <= d; z++)
                Put(StylizedArtIntegration.PrefabDir + ((x + z) % 3 == 0 ? "PF_Env_Tile_Stone_B" : "PF_Env_Tile_Stone_A") + ".prefab", new Vector3(x, 0, z), 0, world);
    }

    // Read-only copy of the Game.unity Main Camera: orthographic size 9.2, rotation (0.369, 0.139, -0.056, 0.917).
    static readonly Quaternion GameCamRot = new Quaternion(0.3691325f, 0.13881999f, -0.05586581f, 0.91725093f);
    static void GameCam(Camera cam, Vector3 focus, float size = 9.2f)
    {
        cam.orthographic = true; cam.orthographicSize = size; cam.transform.rotation = GameCamRot;
        cam.transform.position = focus - cam.transform.forward * 40f; cam.nearClipPlane = .3f; cam.farClipPlane = 120;
    }

    static void BuildShowcase()
    {
        string pf = StylizedArtIntegration.PrefabDir;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional; light.color = H("FFE9CC"); light.intensity = 1.3f; light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50, -40, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = H("9FB8E0"); RenderSettings.ambientEquatorColor = H("8A7FA0"); RenderSettings.ambientGroundColor = H("4A3A55");
        var cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = H("03357A"); cam.fieldOfView = 30;
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>(); cam.gameObject.AddComponent<CameraShake>();
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane); UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.position = new Vector3(0, -.3f, 0); water.transform.localScale = Vector3.one * 10;
        water.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/M_Env_Water_Flat.mat");

        // Section 1: explosions (z = 0), two moments: early (flash/ring) and late (smoke/debris/scorch)
        var s1 = new GameObject("Showcase_Explosions").transform;
        _sec = s1; Floor(s1, 11, 4);
        for (int i = 0; i < Elements.Length; i++)
        {
            float x = -8 + i * 4;
            Fx("FX_Explosion_" + Elements[i].id, new Vector3(x, 0, 1.5f), .09f, null, s1);
            Fx("FX_Explosion_" + Elements[i].id, new Vector3(x, 0, -2f), .55f, null, s1);
        }
        cam.transform.position = new Vector3(0, 7.5f, -15.5f); cam.transform.LookAt(new Vector3(0, .7f, -.2f)); cam.fieldOfView = 34;
        for (int i = 0; i < Elements.Length; i++) Label(Elements[i].id, new Vector3(-8 + i * 4, 3.6f, 1.5f), cam, 3);
        Label("t=0.09s", new Vector3(-10.6f, 1.6f, 1.5f), cam, 2.2f, H("FFE08A")); Label("t=0.55s", new Vector3(-10.6f, 1.2f, -2f), cam, 2.2f, H("FFE08A"));
        Directory.CreateDirectory(PreviewDir);
        foreach (var old in Directory.GetFiles(PreviewDir, "vfx_*_v2.png")) File.Delete(old);
        StylizedArtIntegration.Capture(cam, PreviewDir + "vfx_explosions.png");
        GameCam(cam, new Vector3(0, .5f, -.2f)); foreach (var t in s1.GetComponentsInChildren<TextMeshPro>()) t.transform.rotation = cam.transform.rotation;
        StylizedArtIntegration.Capture(cam, PreviewDir + "vfx_explosions_v2.png"); cam.orthographic = false;
        s1.gameObject.SetActive(false);

        // Section 2: turrets firing (muzzle + projectile in flight + hit), enemies as targets
        var s2 = new GameObject("Showcase_Turrets").transform; s2.position = new Vector3(0, 0, 0);
        _sec = s2; Floor(s2, 10, 4);
        var towers = new[] { ("PF_Tower_Gatling_1x1", "FX_Muzzle_Gatling", "FX_Proj_Bullet", "FX_Hit_Kinetic", "Needle"),
                             ("PF_Tower_Cannon_1x1", "FX_Muzzle_Cannon", "FX_Proj_Cannonball", "FX_Explosion_Kinetic", "Cannon"),
                             ("PF_Tower_Frost_1x1", "FX_Muzzle_Frost", "FX_Proj_FrostShard", "FX_Hit_Ice", "Chill"),
                             ("PF_Tower_Tesla_1x1", "FX_Muzzle_Tesla", "FX_Proj_Tesla_Arc", "FX_Hit_Lightning", "Pulse"),
                             ("PF_Tower_Flamer_1x2", "FX_Muzzle_Flamer", "FX_Proj_Fireball", "FX_Hit_Fire", "Flamer"),
                             ("PF_Tower_Mortar_2x2", "FX_Muzzle_Mortar", "FX_Proj_MortarShell", "FX_Explosion_HE", "Seismic") };
        var enemies = new[] { "PF_Enemy_Drifter", "PF_Enemy_Skimmer", "PF_Enemy_Bulwark", "PF_Enemy_Shard", "PF_Enemy_Splitter", "PF_Enemy_Boss" };
        cam.transform.position = new Vector3(-5, 9, -16); cam.transform.LookAt(new Vector3(0, .8f, .5f)); cam.fieldOfView = 32;
        for (int i = 0; i < towers.Length; i++)
        {
            var (tp, mz, pj, hit, label) = towers[i];
            float x = -8.5f + i * 3.4f; var tPos = new Vector3(x, 0, -1.8f); var ePos = new Vector3(x + .6f, 0, 2.8f);
            Put(pf + "PF_Env_Rock_1x1.prefab", tPos + Vector3.down * .0f, 0, s2);
            var tw = Put(pf + tp + ".prefab", tPos + Vector3.up * .6f, 0, s2);
            var head = tw.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("_Head")) ?? tw.transform;
            var dir = (ePos - tPos); dir.y = 0; dir.Normalize(); head.rotation = Quaternion.LookRotation(dir);
            var en = Put(pf + enemies[i] + ".prefab", ePos, 180, s2);
            var muzzlePos = tPos + Vector3.up * 1.25f + dir * .6f;
            var muzzleRot = Quaternion.LookRotation(dir);
            if (tp.Contains("Mortar")) { var hb = MortarBarrelTip(head, dir); muzzlePos = hb.pos; muzzleRot = hb.rot; }
            Fx(mz, muzzlePos, tp.Contains("Flamer") ? .8f : .04f, muzzleRot, s2);
            var hitPos = ePos + Vector3.up * .6f;
            if (pj == "FX_Proj_Tesla_Arc")
            {
                var arc = Put(Dir + pj + ".prefab", muzzlePos, 0, s2); arc.GetComponent<TeslaArc>().SetEndpoints(tPos + Vector3.up * 1.5f, hitPos);
            }
            else
            {
                float k = .55f; Vector3 p = Vector3.Lerp(muzzlePos, hitPos, k) + (tp.Contains("Mortar") ? Vector3.up * 1.5f : Vector3.zero);
                var pr = Put(Dir + pj + ".prefab", p, 0, s2); pr.transform.rotation = Quaternion.LookRotation(hitPos - muzzlePos);
                foreach (var tr in pr.GetComponentsInChildren<TrailRenderer>())
                {
                    var pts = new List<Vector3>(); for (int j = 6; j >= 1; j--) pts.Add(Vector3.Lerp(muzzlePos, p, 1 - j * .12f) + (tp.Contains("Mortar") ? Vector3.up * 1.5f * (1 - j * .12f) : Vector3.zero));
                    tr.AddPositions(pts.ToArray());
                }
                foreach (var ps in pr.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(.4f, false, true);
            }
            Fx(hit, hitPos, hit.StartsWith("FX_Explosion") ? .12f : .05f, null, s2);
            var fb = en.GetComponent<EnemyHitFeedback>(); if (fb) fb.SetVisualState(.4f, 0);
            Label(label, new Vector3(x, 3.2f, -1.8f), cam, 2.4f);
        }
        StylizedArtIntegration.Capture(cam, PreviewDir + "vfx_turrets.png");
        s2.gameObject.SetActive(false);

        // Section 3: enemy feedback: idle / hit flash / dissolve 0.4 / dissolve 0.75 + death puff + coin pop / split burst
        var s3 = new GameObject("Showcase_EnemyFeedback").transform;
        _sec = s3; Floor(s3, 8, 3);
        cam.transform.position = new Vector3(1, 7f, -15.5f); cam.transform.LookAt(new Vector3(1, .8f, 0)); cam.fieldOfView = 34;
        string[] states = { "Idle", "Hit flash", "Dissolve 40%", "Dissolve 75%", "Coin pop", "Split burst" };
        for (int i = 0; i < states.Length; i++)
        {
            float x = -6.25f + i * 2.5f;
            var e = Put(pf + (i == 5 ? "PF_Enemy_Splitter" : "PF_Enemy_Drifter") + ".prefab", new Vector3(x, 0, 0), 200, s3);
            var fb = e.GetComponent<EnemyHitFeedback>();
            if (i == 1) { fb.SetVisualState(1, 0); e.transform.Find("Rig").localScale = new Vector3(1.13f, .8f, 1.13f); }
            if (i == 2) fb.SetVisualState(0, .4f);
            if (i == 3) { fb.SetVisualState(0, .75f); Fx("FX_Enemy_DeathPuff", new Vector3(x, .4f, 0), .12f, null, s3); }
            if (i == 4) { fb.SetVisualState(0, 1); Fx("FX_Enemy_DeathPuff", new Vector3(x, .4f, 0), .3f, null, s3); Fx("FX_Enemy_CoinPop", new Vector3(x, .5f, 0), .32f, null, s3); }
            if (i == 5) { e.SetActive(false); Fx("FX_Enemy_SplitBurst", new Vector3(x, .3f, 0), .14f, null, s3);
                Put(pf + "PF_Enemy_Drifter.prefab", new Vector3(x - .7f, 0, -.3f), 160, s3).transform.localScale = Vector3.one * .55f;
                Put(pf + "PF_Enemy_Drifter.prefab", new Vector3(x + .7f, 0, -.3f), 220, s3).transform.localScale = Vector3.one * .55f; }
            Label(states[i], new Vector3(x, 2.4f, 0), cam, 1.8f);
        }
        Put(pf + "PF_Prop_Core.prefab", new Vector3(8.6f, 0, .5f), 0, s3); Fx("FX_Hit_Core", new Vector3(8.6f, 1.1f, -.3f), .06f, null, s3); Label("Core hit", new Vector3(8.6f, 3f, .5f), cam, 1.8f);
        StylizedArtIntegration.Capture(cam, PreviewDir + "vfx_enemy_feedback.png");
        GameCam(cam, new Vector3(1, .5f, 0), 6.5f); foreach (var t in s3.GetComponentsInChildren<TextMeshPro>()) t.transform.rotation = cam.transform.rotation;
        StylizedArtIntegration.Capture(cam, PreviewDir + "vfx_enemy_feedback_v2.png"); cam.orthographic = false;
        s3.gameObject.SetActive(false);

        // Section 4: damage numbers over enemies (normal / elements / crit / stacked)
        var s4 = new GameObject("Showcase_DamageNumbers").transform;
        _sec = s4; Floor(s4, 6, 3);
        cam.transform.position = new Vector3(0, 6f, -11); cam.transform.LookAt(new Vector3(0, 1.3f, .8f)); cam.fieldOfView = 34;
        var dnp = Prefab(ResDir + "PF_FX_DamageNumber.prefab");
        void Num(string s, Vector3 p, Color c, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(dnp, s4); var t = go.GetComponent<TextMeshPro>();
            t.text = s; t.color = c; go.transform.position = p; go.transform.rotation = cam.transform.rotation; go.transform.localScale = Vector3.one * scale; t.ForceMeshUpdate();
        }
        var kinds = new[] { (DamageKind.Physical, "12", "Physical"), (DamageKind.Fire, "8", "Fire"), (DamageKind.Ice, "15", "Ice"), (DamageKind.Lightning, "23", "Lightning"), (DamageKind.Explosive, "41", "Explosive") };
        for (int i = 0; i < kinds.Length; i++)
        {
            float x = -5 + i * 2.5f; Put(pf + "PF_Enemy_Drifter.prefab", new Vector3(x, 0, 0), 200, s4);
            Num(kinds[i].Item2, new Vector3(x + .2f, 1.7f + (i % 2) * .25f, 0), DamageNumbers.ColorFor(kinds[i].Item1), 1);
            Label(kinds[i].Item3, new Vector3(x, .15f, -1.2f), cam, 1.4f, H("C8D8FF"));
        }
        Put(pf + "PF_Enemy_Bulwark.prefab", new Vector3(-1.5f, 0, 2.6f), 200, s4);
        Num("96!", new Vector3(-1.3f, 2.9f, 2.6f), DamageNumbers.CritColor, DamageNumbers.CritScale * 1.2f);
        Label("CRIT (x1.6, gold, '!')", new Vector3(-1.5f, 4.1f, 2.6f), cam, 1.4f, H("FFE08A"));
        Put(pf + "PF_Enemy_Skimmer.prefab", new Vector3(2.5f, 0, 2.6f), 200, s4);
        for (int j = 0; j < 3; j++) Num(new[] { "6", "12", "18" }[j], new Vector3(2.2f + j * .45f, 1.6f + j * .55f, 2.6f), A(Color.white, .35f + j * .3f), .7f + j * .2f);
        Label("stacking: 6 -> 12 -> 18 (same target, 0.35s)", new Vector3(2.8f, 3.6f, 2.6f), cam, 1.2f, H("C8D8FF"));
        StylizedArtIntegration.Capture(cam, PreviewDir + "vfx_numbers.png");
        s4.gameObject.SetActive(false);

        s1.gameObject.SetActive(true); // saved scene opens on the explosion lineup (play mode: particles re-trigger via playOnAwake)
        EditorSceneManager.SaveScene(scene, ShowcasePath);
        Debug.Log("STYLIZED VFX BUILT " + Built.Count + " prefabs");
    }

    [MenuItem("StoneSignal/Stylized art/Check VFX")]
    public static void Checks()
    {
        var errors = new List<string>();
        foreach (var s in new[] { "StoneSignal/FXAdditive", "StoneSignal/FXAlpha", "StoneSignal/ToonLitOutline", "StoneSignal/ToonLit" })
        { var sh = Shader.Find(s); if (sh == null || ShaderUtil.ShaderHasError(sh)) errors.Add("FX shader missing/error " + s); }
        var names = Built.Select(b => b.prefab).ToList();
        if (names.Count < 25) errors.Add("only " + names.Count + " VFX prefabs built");
        foreach (var n in names)
        {
            var p = Prefab((n == "PF_FX_DamageNumber" ? ResDir : Dir) + n + ".prefab");
            if (p == null) { errors.Add("missing VFX prefab " + n); continue; }
            if (p.GetComponentsInChildren<Collider>(true).Length > 0) errors.Add(n + " has colliders");
            foreach (var r in p.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.sharedMaterial == null) errors.Add(n + "/" + r.name + " has no material");
        }
        var dn = Prefab(ResDir + "PF_FX_DamageNumber.prefab");
        if (dn == null || dn.GetComponent<TextMeshPro>() == null || dn.GetComponent<TextMeshPro>().font == null || dn.GetComponent<DamageNumber>() == null) errors.Add("PF_FX_DamageNumber invalid");
        foreach (var f in new[] { "vfx_explosions.png", "vfx_turrets.png", "vfx_enemy_feedback.png", "vfx_numbers.png", "vfx_explosions_v2.png", "vfx_enemy_feedback_v2.png" })
            if (!File.Exists(PreviewDir + f)) errors.Add("missing capture " + f);
        if (errors.Count > 0) throw new Exception("STYLIZED VFX FAIL\n" + string.Join("\n", errors));
        Debug.Log("STYLIZED VFX PASS (" + names.Count + " prefabs)\n" + string.Join("\n", Built.Select(b => $"VFXMAP {b.prefab} | {b.usedBy}")));
    }
}
