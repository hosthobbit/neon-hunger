using System.Collections.Generic;
using NeonHunger.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonHunger
{
    /// <summary>Shared helpers for the procedurally built actors.</summary>
    public static class ActorKit
    {
        public static Renderer Part(Transform parent, string name, MeshKit kit, bool shadows = false)
        {
            var (mesh, mats) = kit.Build(name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return mr;
        }

        public static Quaternion Facing(Dir d) =>
            Quaternion.Euler(0f, d == Dir.Right ? 90f : d == Dir.Left ? -90f : d == Dir.Down ? 180f : 0f, 0f);
    }

    /// <summary>
    /// The player: a small glowing energy creature with big eyes and a chomping mouth. It lights the
    /// corridors around it, stretches when it dashes, and turns violet and spiky in a power surge.
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        Transform body, eyes, mouth, ring, shield;
        Material bodyMat;
        Light glow;
        TrailRenderer trail;
        float chomp, blinkAt, dying, appear = 1f, squash;
        Dir lastDir;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseId = Shader.PropertyToID("_BaseColor");
        public static readonly Color Warm = new Color(1f, 0.66f, 0.25f), Surge = new Color(0.75f, 0.45f, 1f);

        public void Build(Art art, int quality)
        {
            bodyMat = new Material(art.PlayerBody);
            var root = new GameObject("Body").transform;
            root.SetParent(transform, false);
            body = root;
            var k = new MeshKit();
            k.Sphere(bodyMat, new Vector3(0, 0.36f, 0), new Vector3(0.34f, 0.31f, 0.34f), 20, 12);
            root.localScale = Vector3.one;
            ActorKit.Part(root, "Glow Body", k, quality >= 2);

            eyes = new GameObject("Eyes").transform;
            eyes.SetParent(root, false);
            var e = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
            {
                e.Sphere(art.PlayerEye, new Vector3(s * 0.13f, 0.46f, 0.25f), new Vector3(0.1f, 0.12f, 0.08f), 12, 8);
                e.Sphere(art.Pupil, new Vector3(s * 0.12f, 0.46f, 0.32f), new Vector3(0.05f, 0.065f, 0.03f), 10, 6);
            }
            ActorKit.Part(eyes, "Eye Mesh", e);

            mouth = new GameObject("Mouth").transform;
            mouth.SetParent(root, false);
            mouth.localPosition = new Vector3(0, 0.27f, 0.27f);
            var m = new MeshKit();
            m.Sphere(art.Mouth, Vector3.zero, new Vector3(0.16f, 0.07f, 0.06f), 12, 6);
            ActorKit.Part(mouth, "Mouth Mesh", m);

            ring = new GameObject("Surge Ring").transform;
            ring.SetParent(root, false);
            ring.localPosition = new Vector3(0, 0.36f, 0);
            var rk = new MeshKit();
            rk.Torus(art.Power, Vector3.zero, Vector3.up, 0.5f, 0.03f, 40, 6);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                rk.Cylinder(art.Power, dir * 0.5f, dir * 0.66f, 0.04f, 6, true, 0.005f);
            }
            ActorKit.Part(ring, "Ring Mesh", rk);
            ring.gameObject.SetActive(false);

            // shield pickup: two crossed hoops spinning round the creature
            var shieldMat = new Material(art.Power);
            var sc = PickupView.Colour(PowerUp.Shield);
            shieldMat.SetColor(BaseId, sc); shieldMat.SetColor(EmissionId, sc * 1.8f);
            shield = new GameObject("Shield").transform;
            shield.SetParent(transform, false);
            shield.localPosition = new Vector3(0, 0.38f, 0);
            var sk = new MeshKit();
            sk.Torus(shieldMat, Vector3.zero, new Vector3(0.5f, 1f, 0f).normalized, 0.56f, 0.022f, 40, 6);
            sk.Torus(shieldMat, Vector3.zero, new Vector3(-0.5f, 1f, 0f).normalized, 0.56f, 0.022f, 40, 6);
            ActorKit.Part(shield, "Shield Mesh", sk);
            shield.gameObject.SetActive(false);

            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0, 1.6f, 0);
            glow.type = LightType.Point; glow.range = 5.4f; glow.intensity = 2.2f; glow.color = Warm;
            glow.shadows = LightShadows.None;

            trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = art.Trail;
            trail.time = 0.25f; trail.widthMultiplier = 0.45f; trail.minVertexDistance = 0.05f;
            trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            trail.colorGradient = Grad(Warm);
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            blinkAt = Time.time + 2f;
        }

        static Gradient Grad(Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0, 1) });
            return g;
        }

        public void Ate() => chomp = 1f;
        public void Die() => dying = 0.0001f;
        public void Respawn() { dying = 0f; appear = 0f; trail.Clear(); }

        public void Sync(GameSim sim, float dt)
        {
            var mv = sim.Player;
            transform.position = Art.World(mv.X, mv.Y);
            if (mv.Facing != lastDir) { squash = 0.25f; lastDir = mv.Facing; }
            body.localRotation = Quaternion.Slerp(body.localRotation, ActorKit.Facing(mv.Facing), 1f - Mathf.Exp(-dt * 18f));

            bool surge = sim.SurgeActive;
            bool ending = sim.SurgeEnding && !Settings.ReducedFlashing;
            var c = surge ? (ending && Mathf.Repeat(Time.time * 6f, 1f) > 0.5f ? Warm : Surge) : Warm;
            bodyMat.SetColor(BaseId, c);
            bodyMat.SetColor(EmissionId, c * (surge ? 1.2f : 0.8f));
            glow.color = c;
            glow.intensity = Mathf.MoveTowards(glow.intensity, surge ? 3f : 2.2f, dt * 4f);
            glow.range = Mathf.MoveTowards(glow.range, surge ? 7f : 5.4f, dt * 4f);
            ring.gameObject.SetActive(surge);
            if (surge) ring.localRotation = Quaternion.Euler(0, Time.time * 240f, 0);

            bool fast = sim.SpeedLeft > 0f;
            trail.emitting = sim.Dashing || fast;
            trail.colorGradient = Grad(fast && !sim.Dashing ? PickupView.Colour(PowerUp.Speed) : c);
            shield.gameObject.SetActive(sim.Shielded && dying <= 0f);
            if (sim.Shielded) shield.localRotation = Quaternion.Euler(0, Time.time * 160f, 0);

            // chomping, blinking, squash and stretch
            chomp = Mathf.MoveTowards(chomp, 0f, dt * 6f);
            bool moving = mv.Dir != Dir.None;
            float open = moving ? 0.6f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * 14f)) : 0.3f;
            mouth.localScale = new Vector3(1f, Mathf.Max(open, chomp * 2f), 1f);
            float blink = blinkAt - Time.time < 0.12f ? 0.15f : 1f;
            if (Time.time > blinkAt) blinkAt = Time.time + Random.Range(2f, 5f);
            eyes.localScale = new Vector3(1f, blink, 1f);
            squash = Mathf.MoveTowards(squash, 0f, dt * 2f);
            float stretch = sim.Dashing ? 1.35f : 1f;
            float bob = moving ? Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.04f : Mathf.Sin(Time.time * 3f) * 0.02f;
            appear = Mathf.MoveTowards(appear, 1f, dt * 2.5f);
            float s = appear;
            if (dying > 0f)
            {
                dying += dt;
                s = Mathf.Max(0f, 1f - dying / 1.2f);
                body.localRotation *= Quaternion.Euler(0, dt * 900f * dying, 0);
                glow.intensity = 6f * s;
            }
            s *= 1.25f;
            body.localScale = new Vector3((1f + squash) * s / Mathf.Sqrt(stretch), (1f - squash) * s, stretch * s);
            body.localPosition = new Vector3(0, bob, 0);
        }
    }

    /// <summary>
    /// A hunter. Each has its own silhouette and voice: the Stalker is a tall crimson spike, the
    /// Ambusher a low violet crawler, the Warden a squat amber sentinel with a lantern ring, and the
    /// Listener a thin teal stalk with dish ears. They turn blue and cower in a power surge and
    /// become a pair of fleeing eyes when caught.
    /// </summary>
    public class HunterView : MonoBehaviour
    {
        public HunterKind Kind;
        readonly List<Renderer> bodyParts = new List<Renderer>();
        Renderer eyesR;
        Transform rig, spinner, ears;
        Material bodyMat, frightMat, eyeMat;
        Light glow;
        AudioSource hum;
        float bobPhase;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseId = Shader.PropertyToID("_BaseColor");

        public void Build(HunterKind kind, Art art, int quality)
        {
            Kind = kind;
            bobPhase = (int)kind * 1.7f;
            bodyMat = new Material(art.HunterBody(kind));
            eyeMat = new Material(art.HunterEye);
            frightMat = new Material(art.Frightened);
            rig = new GameObject("Rig").transform;
            rig.SetParent(transform, false);
            var b = new MeshKit();
            var e = new MeshKit();
            var accent = new MeshKit();
            switch (kind)
            {
                case HunterKind.Stalker:
                    b.Cylinder(bodyMat, new Vector3(0, 0.02f, 0), new Vector3(0, 0.95f, 0), 0.33f, 16, true, 0.06f);
                    b.Sphere(bodyMat, new Vector3(0, 0.88f, 0.04f), new Vector3(0.19f, 0.17f, 0.19f), 14, 8);
                    foreach (float s in new[] { -1f, 1f })
                        b.Cylinder(bodyMat, new Vector3(s * 0.2f, 0.62f, 0), new Vector3(s * 0.46f, 0.98f, -0.08f), 0.06f, 6, true, 0.005f);
                    e.Box(eyeMat, new Vector3(0, 0.9f, 0.21f), new Vector3(0.22f, 0.035f, 0.02f));
                    // a glowing seam down its front
                    e.BoxRotated(eyeMat, new Vector3(0, 0.45f, 0.2f), new Vector3(0.025f, 0.7f, 0.02f), Quaternion.Euler(-17f, 0, 0));
                    break;
                case HunterKind.Ambusher:
                    b.Sphere(bodyMat, new Vector3(0, 0.3f, 0), new Vector3(0.44f, 0.2f, 0.36f), 16, 8);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        b.Cylinder(bodyMat, new Vector3(s * 0.3f, 0.3f, 0.1f), new Vector3(s * 0.5f, 0.02f, 0.22f), 0.035f, 6);
                        b.Cylinder(bodyMat, new Vector3(s * 0.3f, 0.3f, -0.12f), new Vector3(s * 0.5f, 0.02f, -0.26f), 0.035f, 6);
                        b.BoxRotated(bodyMat, new Vector3(s * 0.26f, 0.3f, 0.42f), new Vector3(0.1f, 0.08f, 0.26f), Quaternion.Euler(0, -s * 25f, 0));
                    }
                    for (int i = -1; i <= 1; i++) e.Sphere(eyeMat, new Vector3(i * 0.11f, 0.38f, 0.33f), new Vector3(0.05f, 0.05f, 0.04f), 8, 6);
                    foreach (float s in new[] { -1f, 1f }) e.Sphere(eyeMat, new Vector3(s * 0.32f, 0.3f, 0.55f), new Vector3(0.04f, 0.04f, 0.04f), 6, 4);   // claw tips
                    break;
                case HunterKind.Warden:
                    b.Box(bodyMat, new Vector3(0, 0.36f, 0), new Vector3(0.62f, 0.58f, 0.5f), 1f);
                    b.Sphere(bodyMat, new Vector3(0, 0.66f, 0), new Vector3(0.28f, 0.16f, 0.24f), 12, 6);
                    accent.Torus(art.NeonAmber, Vector3.zero, Vector3.up, 0.44f, 0.035f, 32, 6);
                    for (int i = 0; i < 4; i++) { float a = i * Mathf.PI / 2f; accent.Box(art.NeonAmber, new Vector3(Mathf.Cos(a) * 0.44f, 0, Mathf.Sin(a) * 0.44f), new Vector3(0.09f, 0.09f, 0.09f)); }
                    e.Sphere(eyeMat, new Vector3(0, 0.42f, 0.26f), new Vector3(0.12f, 0.12f, 0.05f), 12, 8);
                    break;
                default:
                    b.Cylinder(bodyMat, new Vector3(0, 0.02f, 0), new Vector3(0, 0.12f, 0), 0.24f, 12, true, 0.18f);
                    b.Cylinder(bodyMat, new Vector3(0, 0.1f, 0), new Vector3(0, 0.8f, 0), 0.07f, 8);
                    b.Sphere(bodyMat, new Vector3(0, 0.84f, 0), new Vector3(0.13f, 0.13f, 0.13f), 12, 8);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        accent.Torus(bodyMat, new Vector3(s * 0.24f, 0, 0), Vector3.right, 0.17f, 0.03f, 20, 6);
                        accent.Cylinder(bodyMat, new Vector3(s * 0.22f, 0, 0), new Vector3(s * 0.26f, 0, 0), 0.17f, 16);
                    }
                    e.Sphere(eyeMat, new Vector3(0, 0.86f, 0.1f), new Vector3(0.06f, 0.06f, 0.05f), 8, 6);
                    foreach (float s in new[] { -1f, 1f }) e.Torus(eyeMat, new Vector3(s * 0.27f, 0.84f, 0), Vector3.right, 0.13f, 0.012f, 20, 4);   // glowing ear rims
                    break;
            }
            bodyParts.Add(ActorKit.Part(rig, "Body", b, quality >= 2));
            eyesR = ActorKit.Part(rig, "Eyes", e);
            if (!accent.Empty)
            {
                var t = new GameObject(kind == HunterKind.Warden ? "Lantern" : "Ears").transform;
                t.SetParent(rig, false);
                t.localPosition = kind == HunterKind.Warden ? new Vector3(0, 0.5f, 0) : new Vector3(0, 0.84f, 0);
                bodyParts.Add(ActorKit.Part(t, "Accent", accent));
                if (kind == HunterKind.Warden) spinner = t; else ears = t;
            }
            if (quality >= 1)
            {
                glow = new GameObject("Glow").AddComponent<Light>();
                glow.transform.SetParent(transform, false);
                glow.transform.localPosition = new Vector3(0, 0.08f, 0);   // a pool of colour on the floor; the body stays a silhouette
                glow.type = LightType.Point; glow.range = quality >= 2 ? 3f : 2.2f; glow.intensity = 1.4f; glow.color = Art.HunterColour(kind);
                glow.shadows = LightShadows.None;
            }
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = Sfx.Instance ? Sfx.Instance.Hum(kind) : null;
            hum.loop = true; hum.spatialBlend = 1f; hum.rolloffMode = AudioRolloffMode.Linear; hum.minDistance = 1.5f; hum.maxDistance = 13f;
            hum.dopplerLevel = 0f; hum.volume = 0f;
            if (hum.clip) hum.Play();
        }

        public void Sync(GameSim sim, int index, float dt)
        {
            var h = sim.Hunters[index];
            var mv = h.Mover;
            float t = Time.time + bobPhase;
            transform.position = Art.World(mv.X, mv.Y);
            if (mv.Dir != Dir.None || h.Mode == HunterMode.Penned)
                rig.localRotation = Quaternion.Slerp(rig.localRotation, ActorKit.Facing(mv.Facing), 1f - Mathf.Exp(-dt * 10f));

            bool eyesOnly = h.Mode == HunterMode.Returning;
            bool frozen = sim.IsFrozen(h);
            bool fright = h.Mode == HunterMode.Fleeing && !frozen;
            bool flicker = fright && sim.SurgeEnding && !Settings.ReducedFlashing && Mathf.Repeat(Time.time * 5f, 1f) > 0.5f;
            foreach (var r in bodyParts)
            {
                r.enabled = !eyesOnly;
                if (!r.enabled) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == bodyMat || mats[i] == frightMat) mats[i] = fright && !flicker ? frightMat : bodyMat;
                r.sharedMaterials = mats;
            }
            var col = Art.HunterColour(Kind);
            if (frozen) col = Color.Lerp(col, PickupView.Colour(PowerUp.Freeze), 0.75f);
            bodyMat.SetColor(EmissionId, col * (h.Mode == HunterMode.Hunting ? (h.Investigating ? 0.7f : 0.32f) : 0.15f));
            eyeMat.SetColor(BaseId, (fright ? new Color(0.85f, 0.9f, 1f) : Color.Lerp(col, Color.white, 0.35f)) * (h.Investigating ? 3.2f : 2.4f));
            if (glow)
            {
                glow.enabled = !eyesOnly;
                glow.color = fright ? new Color(0.3f, 0.45f, 1f) : col;
                glow.intensity = frozen ? 0.9f : h.Mode == HunterMode.Hunting ? 1.6f : 0.8f;
            }

            // body language: hover, cower when frightened, lean in when hunting
            float bob = frozen ? 0f : Mathf.Sin(t * (fright ? 9f : 3f)) * (fright ? 0.05f : 0.04f);
            rig.localPosition = new Vector3(0, (eyesOnly ? 0.25f : 0f) + bob, 0);
            float lean = h.Mode == HunterMode.Hunting && mv.Dir != Dir.None ? 8f : fright ? -12f : 0f;
            rig.localRotation = Quaternion.Euler(lean, rig.localRotation.eulerAngles.y, 0);
            rig.localScale = (fright ? new Vector3(1.05f, 0.85f, 1.05f) : Vector3.one) * 1.3f;
            if (spinner && !frozen) spinner.localRotation = Quaternion.Euler(0, Time.time * (h.Mode == HunterMode.Hunting ? 120f : 40f), 0);
            if (ears) ears.localRotation = Quaternion.Euler(h.Investigating ? Mathf.Sin(Time.time * 10f) * 25f : 0f, h.Investigating ? Mathf.Sin(Time.time * 3f) * 40f : 0f, 0);

            // voice: louder while hunting (and pitched up when investigating a noise)
            if (hum)
            {
                float target = frozen ? 0.05f : h.Mode == HunterMode.Hunting ? 0.55f : h.Mode == HunterMode.Fleeing ? 0.3f : h.Mode == HunterMode.Returning ? 0f : 0.15f;
                hum.volume = Mathf.MoveTowards(hum.volume, target * (Sfx.Instance ? Sfx.Instance.SoundVolume : 1f), dt * 2f);
                hum.pitch = fright ? 0.7f : h.Investigating ? 1.25f : 1f;
            }
        }
    }
}
