using NeonHunger.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonHunger
{
    /// <summary>
    /// The bonus pickup currently in the maze: a glowing two-tone pill with its name floating above,
    /// coloured by kind. It bobs and spins, and blinks for its last few seconds.
    /// </summary>
    public class PickupView : MonoBehaviour
    {
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseId = Shader.PropertyToID("_BaseColor");

        public static Color Colour(PowerUp k) =>
            k == PowerUp.Home ? new Color(0.25f, 1f, 0.45f) : k == PowerUp.Freeze ? new Color(0.55f, 0.88f, 1f)
            : k == PowerUp.Speed ? new Color(1f, 0.88f, 0.2f) : new Color(1f, 0.4f, 0.85f);

        public static string Name(PowerUp k) =>
            k == PowerUp.Home ? "GO HOME" : k == PowerUp.Freeze ? "FREEZE" : k == PowerUp.Speed ? "SPEED" : "SHIELD";

        public static string Blurb(PowerUp k) =>
            k == PowerUp.Home ? $"GO HOME! Every hunter is sent back to the pen for {GameSim.HomeSeconds:0} seconds."
            : k == PowerUp.Freeze ? $"FREEZE! The hunters are frozen solid for {GameSim.FreezeSeconds:0} seconds. Touching them is safe."
            : k == PowerUp.Speed ? $"SPEED! You're faster for {GameSim.SpeedSeconds:0} seconds."
            : "SHIELD! The next hunter to catch you is thrown back home instead.";

        Transform pill, label;
        Material colourMat, whiteMat;
        TextMesh text;
        Light glow;
        PowerUp shown;

        public void Build(Art art)
        {
            colourMat = new Material(art.Power);
            whiteMat = new Material(art.Power);
            whiteMat.SetColor(BaseId, Color.white);
            whiteMat.SetColor(EmissionId, Color.white * 0.9f);

            // a capsule lying on its side: one half coloured, one half white
            var kit = new MeshKit();
            const float r = 0.17f, half = 0.16f;
            kit.Cylinder(colourMat, new Vector3(-half, 0, 0), Vector3.zero, r, 16, false);
            kit.Sphere(colourMat, new Vector3(-half, 0, 0), Vector3.one * r, 16, 9);
            kit.Cylinder(whiteMat, Vector3.zero, new Vector3(half, 0, 0), r, 16, false);
            kit.Sphere(whiteMat, new Vector3(half, 0, 0), Vector3.one * r, 16, 9);
            var (mesh, mats) = kit.Build("Pill");
            pill = new GameObject("Pill").transform;
            pill.SetParent(transform, false);
            pill.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = pill.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var ring = new MeshKit();
            ring.Torus(colourMat, Vector3.zero, Vector3.up, 0.42f, 0.025f, 32, 6);
            var (rm, rmats) = ring.Build("Pickup Ring");
            var rg = new GameObject("Ring");
            rg.transform.SetParent(transform, false);
            rg.transform.localPosition = new Vector3(0, 0.08f, 0);
            rg.AddComponent<MeshFilter>().sharedMesh = rm;
            var rr = rg.AddComponent<MeshRenderer>();
            rr.sharedMaterials = rmats;
            rr.shadowCastingMode = ShadowCastingMode.Off;

            label = new GameObject("Label").transform;
            label.SetParent(transform, false);
            label.localPosition = new Vector3(0, 0.3f, -0.62f);   // just below the pill on screen
            label.rotation = Quaternion.Euler(66f, 0f, 0f);
            text = label.gameObject.AddComponent<TextMesh>();
            text.font = art.WorldFont;
            label.GetComponent<MeshRenderer>().sharedMaterial = art.WorldFont.material;
            text.fontSize = 64; text.characterSize = 0.045f; text.anchor = TextAnchor.MiddleCenter;

            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0, 0.8f, 0);
            glow.type = LightType.Point; glow.range = 3.2f; glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;
            gameObject.SetActive(false);
        }

        public void Sync(GameSim sim)
        {
            bool on = sim.Pickup != PowerUp.None;
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (!on) return;
            if (sim.Pickup != shown)
            {
                shown = sim.Pickup;
                var c = Colour(shown);
                colourMat.SetColor(BaseId, c);
                colourMat.SetColor(EmissionId, c * 1.6f);
                text.text = Name(shown);
                text.color = c;
                glow.color = c;
            }
            float t = Time.time;
            transform.position = Art.World(sim.PickupTile.C, sim.PickupTile.R, 0.42f + Mathf.Sin(t * 2.6f) * 0.07f);
            pill.localRotation = Quaternion.Euler(0f, t * 110f, 20f);
            // blink for the last few seconds so the player knows it's about to go
            bool blinkOff = sim.PickupLeft < 3.5f && !Settings.ReducedFlashing && Mathf.Repeat(t * 4f, 1f) > 0.6f;
            pill.gameObject.SetActive(!blinkOff);
            glow.intensity = sim.PickupLeft < 3.5f && Settings.ReducedFlashing ? 0.8f : 1.6f;
        }
    }
}
