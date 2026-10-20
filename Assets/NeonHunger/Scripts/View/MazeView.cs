using System.Collections.Generic;
using NeonHunger.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonHunger
{
    /// <summary>
    /// Builds and animates one level's geometry from the logical maze: worn metal floor plates, dark
    /// walls with restrained neon trims, the hunters' pen, risk chambers, living walls (which sink
    /// into the floor or rise out of it), the exit, the energy, and a little atmosphere: sparks,
    /// drifting mist and a few flickering lamps. Purely visual: the maze model decides everything.
    /// </summary>
    public class MazeView : MonoBehaviour
    {
        public const float WallHeight = 1.1f;
        Maze maze;
        Art art;
        readonly Dictionary<Tile, Transform> items = new Dictionary<Tile, Transform>();
        readonly List<(Transform t, float phase, Item kind)> spinning = new List<(Transform, float, Item)>();
        readonly Dictionary<Tile, Transform> shiftBlocks = new Dictionary<Tile, Transform>();
        readonly Dictionary<Tile, Renderer> warnPlates = new Dictionary<Tile, Renderer>();
        readonly List<(RiskRoom room, Transform door, TextMesh label, Light light)> rooms = new List<(RiskRoom, Transform, TextMesh, Light)>();
        readonly List<(Light light, float baseIntensity, float seed)> lamps = new List<(Light, float, float)>();
        readonly List<ParticleSystem> sparks = new List<ParticleSystem>();
        Transform exitBars, exitBeam, gate;
        Light exitLight;
        ShiftGroup telegraphing;
        float telegraphStart, nextSpark;
        MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");

        public void Build(Maze m, Art a, int quality)
        {
            maze = m; art = a;
            block = new MaterialPropertyBlock();
            foreach (Transform c in transform) Destroy(c.gameObject);
            items.Clear(); spinning.Clear(); shiftBlocks.Clear(); warnPlates.Clear(); rooms.Clear(); lamps.Clear(); sparks.Clear();

            var floor = new MeshKit();
            var walls = new MeshKit();
            // a dark base under everything, so the maze sits in a void
            floor.Plane(art.Base, Art.World((m.Width - 1) * 0.5f, (m.Height - 1) * 0.5f, -0.02f), new Vector2(m.Width + 30, m.Height + 30), Vector3.up, Vector3.forward, false);
            for (int r = 0; r < m.Height; r++)
                for (int c = 0; c < m.Width; c++)
                {
                    var t = new Tile(c, r);
                    var g = m.GroundAt(t);
                    var p = Art.World(c, r);
                    if (g == Ground.Wall) { Wall(walls, t, p); continue; }
                    var mat = g == Ground.Pen || g == Ground.Gate ? art.FloorPen : g == Ground.RiskFloor || g == Ground.RiskDoor ? art.FloorRisk : art.Floor;
                    floor.Plane(mat, p, new Vector2(1f, 1f), Vector3.up, Vector3.forward, false);
                    if (m.GroupAt(t) != null) ShiftBlock(t, p);
                }
            Mesh("Floor", floor, false);
            Mesh("Walls", walls, quality >= 2);

            BuildGate();
            BuildExit();
            foreach (var room in m.RiskRooms) BuildRiskDoor(room);
            BuildItems();
            BuildAtmosphere(quality);
        }

        GameObject Mesh(string name, MeshKit kit, bool shadows)
        {
            var (mesh, mats) = kit.Build(name);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        bool Open(Tile t) => maze.InBounds(t) && maze.GroundAt(t) != Ground.Wall;

        /// <summary>A wall cell: a dark block, a slightly lighter cap, and a thin neon line along each edge that faces a corridor.</summary>
        void Wall(MeshKit k, Tile t, Vector3 p)
        {
            k.Box(art.Wall, p + Vector3.up * (WallHeight * 0.5f), new Vector3(1f, WallHeight, 1f), 0.5f);
            k.Box(art.WallTop, p + Vector3.up * (WallHeight + 0.02f), new Vector3(0.84f, 0.04f, 0.84f), 0.5f);
            foreach (var d in Dirs.All)
            {
                var n = t.Step(d);
                if (!Open(n)) continue;
                var g = maze.GroundAt(n);
                var neon = g == Ground.Pen || g == Ground.Gate ? art.NeonViolet : g == Ground.RiskFloor || g == Ground.RiskDoor ? art.NeonCrimson : g == Ground.Exit ? art.NeonCrimson : art.NeonCyan;
                var off = new Vector3(d.DC() * 0.505f, 0f, -d.DR() * 0.505f);
                var size = d.Horizontal() ? new Vector3(0.02f, 0.045f, 1f) : new Vector3(1f, 0.045f, 0.02f);
                k.Box(neon, p + off + Vector3.up * (WallHeight - 0.06f), size);
            }
        }

        void ShiftBlock(Tile t, Vector3 p)
        {
            var k = new MeshKit();
            k.Box(art.ShiftWall, Vector3.up * (WallHeight * 0.5f), new Vector3(0.96f, WallHeight, 0.96f), 1f);
            // thin amber rails along the top edges: "this one moves"
            foreach (var s in new[] { -1f, 1f })
            {
                k.Box(art.NeonAmber, new Vector3(s * 0.47f, WallHeight + 0.01f, 0), new Vector3(0.03f, 0.03f, 0.96f));
                k.Box(art.NeonAmber, new Vector3(0, WallHeight + 0.01f, s * 0.47f), new Vector3(0.96f, 0.03f, 0.03f));
            }
            var go = Mesh("Living Wall " + t, k, false);
            go.transform.position = p + Vector3.up * (maze.IsSolid(t) ? 0f : -WallHeight - 0.05f);
            shiftBlocks[t] = go.transform;
            // a warning plate on the floor, lit while the change is being telegraphed
            var plate = new MeshKit();
            plate.Plane(art.Warning, Vector3.up * 0.015f, new Vector2(0.98f, 0.98f), Vector3.up, Vector3.forward);
            var pg = Mesh("Warning " + t, plate, false);
            pg.transform.position = p;
            var r = pg.GetComponent<Renderer>();
            r.enabled = false;
            warnPlates[t] = r;
        }

        void BuildGate()
        {
            var k = new MeshKit();
            for (int i = -2; i <= 2; i++) k.Box(art.Gate, new Vector3(i * 0.2f, 0.45f, 0f), new Vector3(0.05f, 0.9f, 0.05f));
            k.Box(art.NeonViolet, new Vector3(0, 0.92f, 0), new Vector3(1f, 0.05f, 0.1f));
            var go = Mesh("Pen Gate", k, false);
            go.transform.position = Art.World(maze.Gate.C, maze.Gate.R);
            bool horizontal = maze.GroundAt(maze.Gate.Step(Dir.Left)) == Ground.Wall || maze.GroundAt(maze.Gate.Step(Dir.Right)) == Ground.Wall;
            go.transform.rotation = Quaternion.Euler(0, horizontal ? 0 : 90, 0);
            gate = go.transform;
        }

        void BuildExit()
        {
            var p = Art.World(maze.Exit.C, maze.Exit.R);
            var k = new MeshKit();
            // an arch in the outer wall
            k.Box(art.Wall, new Vector3(-0.46f, WallHeight * 0.6f, 0), new Vector3(0.12f, WallHeight * 1.2f, 1f));
            k.Box(art.Wall, new Vector3(0.46f, WallHeight * 0.6f, 0), new Vector3(0.12f, WallHeight * 1.2f, 1f));
            k.Box(art.Wall, new Vector3(0, WallHeight * 1.2f, 0), new Vector3(1.04f, 0.14f, 1f));
            k.Box(art.NeonCrimson, new Vector3(0, WallHeight * 1.12f, -0.51f), new Vector3(0.8f, 0.04f, 0.02f));
            Mesh("Exit Arch", k, false).transform.position = p;
            var bars = new MeshKit();
            for (int i = -2; i <= 2; i++) bars.Box(art.NeonCrimson, new Vector3(i * 0.16f, WallHeight * 0.55f, 0), new Vector3(0.05f, WallHeight * 1.1f, 0.05f));
            exitBars = Mesh("Exit Bars", bars, false).transform;
            exitBars.position = p;
            var beam = new MeshKit();
            beam.FrontQuad(art.Beam, new Vector3(0, 2.5f, 0), new Vector2(0.9f, 5f));
            exitBeam = Mesh("Exit Beam", beam, false).transform;
            exitBeam.position = p;
            exitBeam.gameObject.SetActive(false);
            exitLight = new GameObject("Exit Light").AddComponent<Light>();
            exitLight.transform.SetParent(transform, false);
            exitLight.transform.position = p + new Vector3(0, 1.2f, 0.6f);
            exitLight.type = LightType.Point; exitLight.range = 6f; exitLight.intensity = 0f; exitLight.color = new Color(0.3f, 0.95f, 1f);
            exitLight.shadows = LightShadows.None;
        }

        void BuildRiskDoor(RiskRoom room)
        {
            var p = Art.World(room.Door.C, room.Door.R);
            var k = new MeshKit();
            k.Box(art.Door, Vector3.up * (WallHeight * 0.5f), new Vector3(0.92f, WallHeight, 0.92f), 1f);
            k.Box(art.NeonCrimson, Vector3.up * (WallHeight + 0.02f), new Vector3(0.6f, 0.04f, 0.6f));
            var door = Mesh("Risk Door", k, false).transform;
            door.position = p + Vector3.down * (WallHeight + 0.05f);   // open: sunk into the floor
            var labelGo = new GameObject("Risk Label");
            labelGo.transform.SetParent(transform, false);
            // the label sits over the room itself
            var centre = Vector3.zero;
            foreach (var c in room.Cells) centre += Art.World(c.C, c.R);
            centre /= room.Cells.Count;
            labelGo.transform.position = centre + Vector3.up * 1.4f;
            labelGo.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.font = art.WorldFont;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = art.WorldFont.material;
            tm.text = "RISK";
            tm.fontSize = 64; tm.characterSize = 0.07f; tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(1f, 0.25f, 0.35f);
            var light = new GameObject("Risk Light").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.position = centre + Vector3.up * 1f;
            light.type = LightType.Point; light.range = 3.5f; light.intensity = 0.8f; light.color = new Color(1f, 0.15f, 0.25f);
            light.shadows = LightShadows.None;
            rooms.Add((room, door, tm, light));
        }

        void BuildItems()
        {
            Mesh node = Sphere(0.09f, 8), power = Sphere(0.24f, 12), gem = Octa(0.24f), riskGem = Octa(0.32f);  // shapes only; materials set per item
            for (int r = 0; r < maze.Height; r++)
                for (int c = 0; c < maze.Width; c++)
                {
                    var t = new Tile(c, r);
                    var it = maze.ItemAt(t);
                    if (it == Item.None) continue;
                    var go = new GameObject(it.ToString());
                    go.transform.SetParent(transform, false);
                    go.transform.position = Art.World(c, r, it == Item.Node ? 0.35f : 0.45f);
                    go.AddComponent<MeshFilter>().sharedMesh = it == Item.Node ? node : it == Item.Power ? power : it == Item.Gem ? gem : riskGem;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = it == Item.Node ? art.Node : it == Item.Power ? art.Power : it == Item.Gem ? art.Gem : art.RiskGem;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    items[t] = go.transform;
                    if (it != Item.Node) spinning.Add((go.transform, (c * 7 + r * 3) * 0.37f, it));
                    if (it == Item.Power)
                    {
                        var ring = new MeshKit();
                        ring.Torus(art.Power, Vector3.zero, Vector3.up, 0.36f, 0.025f, 32, 6);
                        var rg = Mesh("Power Ring", ring, false);
                        rg.transform.SetParent(go.transform, false);
                        rg.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                    }
                }
        }

        Mesh Sphere(float r, int seg)
        {
            var k = new MeshKit();
            k.Sphere(art.Node, Vector3.zero, Vector3.one * r, seg, seg / 2 + 1);
            return k.Build("Sphere").mesh;
        }

        Mesh Octa(float r)
        {
            var k = new MeshKit();
            k.Sphere(art.Gem, Vector3.zero, new Vector3(r * 0.7f, r, r * 0.7f), 4, 2);
            var m = k.Build("Gem").mesh;
            m.RecalculateNormals();
            return m;
        }

        void BuildAtmosphere(int quality)
        {
            var rng = new System.Random(maze.Width * 31 + maze.Height);
            // flickering wall lamps on corridor-facing walls (few: real-time lights are expensive in browsers)
            int lampCount = quality == 0 ? 0 : quality == 1 ? 3 : 5;
            for (int i = 0, tries = 0; i < lampCount && tries < 400; tries++)
            {
                var t = new Tile(rng.Next(maze.Width), rng.Next(maze.Height));
                if (maze.GroundAt(t) != Ground.Floor || maze.GroupAt(t) != null) continue;
                bool far = true;
                foreach (var l in lamps) if (Vector3.Distance(l.light.transform.position, Art.World(t.C, t.R)) < 6f) far = false;
                if (!far) continue;
                var light = new GameObject("Lamp").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.transform.position = Art.World(t.C, t.R, 1.6f);
                light.type = LightType.Point; light.range = 4.5f; light.color = i % 2 == 0 ? new Color(0.35f, 0.85f, 1f) : new Color(0.6f, 0.35f, 1f);
                light.intensity = 1.1f; light.shadows = LightShadows.None;
                lamps.Add((light, light.intensity, (float)rng.NextDouble() * 10f));
                i++;
            }
            // sparks spitting from wall tops now and then
            int sparkCount = quality == 0 ? 2 : 5;
            for (int i = 0, tries = 0; i < sparkCount && tries < 400; tries++)
            {
                var t = new Tile(rng.Next(maze.Width), rng.Next(maze.Height));
                if (maze.GroundAt(t) != Ground.Wall) continue;
                bool nearCorridor = false;
                foreach (var d in Dirs.All) if (Open(t.Step(d))) nearCorridor = true;
                if (!nearCorridor) continue;
                sparks.Add(MakeSparks(Art.World(t.C, t.R, WallHeight + 0.05f)));
                i++;
            }
            if (quality > 0)
            {
                // slow low mist drifting through the corridors
                var go = new GameObject("Mist");
                go.transform.SetParent(transform, false);
                go.transform.position = Art.World((maze.Width - 1) * 0.5f, (maze.Height - 1) * 0.5f, 0.25f);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.duration = 10f; main.startLifetime = 14f; main.startSpeed = 0.15f;
                main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startColor = new Color(0.4f, 0.5f, 0.8f, 0.05f);
                main.maxParticles = quality == 2 ? 60 : 30;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = quality == 2 ? 4f : 2f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(maze.Width, 0.2f, maze.Height);
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.4f), new GradientAlphaKey(0, 1) });
                col.color = g;
                var pr = go.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = art.Mist;
                pr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                ps.Simulate(10f);
                ps.Play();
            }
        }

        ParticleSystem MakeSparks(Vector3 at)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(0.6f, 0.9f, 1f));
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 50f; sh.radius = 0.05f;
            go.transform.rotation = Quaternion.Euler(-90f, 0, 0);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = art.Spark;
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.velocityScale = 0.05f; pr.lengthScale = 1.2f;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // ------------------------------------------------------------------ live updates

        public void Collect(Tile t)
        {
            if (items.TryGetValue(t, out var tr) && tr) { Destroy(tr.gameObject); }
            items.Remove(t);
        }

        public void Telegraph(ShiftGroup g)
        {
            telegraphing = g;
            telegraphStart = Time.time;
            if (g == null) foreach (var p in warnPlates.Values) p.enabled = false;
        }

        public void ApplyShift(ShiftGroup g)
        {
            telegraphing = null;
            foreach (var p in warnPlates.Values) p.enabled = false;
            foreach (var t in g.Cells) if (shiftBlocks.TryGetValue(t, out var b)) StartCoroutine(Slide(b, maze.IsSolid(t) ? 0f : -WallHeight - 0.05f, 0.7f));
        }

        public void SealDoor(RiskRoom room)
        {
            foreach (var r in rooms) if (r.room == room) { StartCoroutine(Slide(r.door, 0f, 0.25f)); r.label.text = "SEALED"; r.light.intensity = 0.2f; }
        }

        public void OpenExit()
        {
            StartCoroutine(Slide(exitBars, -WallHeight * 1.2f, 1.2f));
            exitBeam.gameObject.SetActive(true);
        }

        System.Collections.IEnumerator Slide(Transform t, float y, float time)
        {
            float from = t.position.y;
            for (float k = 0; k < 1f; k += Time.deltaTime / time)
            {
                float e = k * k * (3 - 2 * k);
                t.position = new Vector3(t.position.x, Mathf.Lerp(from, y, e), t.position.z);
                yield return null;
            }
            t.position = new Vector3(t.position.x, y, t.position.z);
        }

        void Update()
        {
            if (maze == null) return;
            float time = Time.time;
            bool calm = Settings.ReducedFlashing;
            // energy items bob and spin
            foreach (var (t, phase, kind) in spinning)
            {
                if (!t) continue;
                t.localRotation = Quaternion.Euler(0f, time * (kind == Item.Power ? 60f : 90f) + phase * 50f, 0f);
                var p = t.position; p.y = 0.45f + Mathf.Sin(time * 2.2f + phase) * 0.06f; t.position = p;
            }
            // telegraphed living walls: plates pulse, blocks shudder
            if (telegraphing != null)
            {
                float k = time - telegraphStart;
                float pulse = calm ? 0.6f : 0.5f + 0.5f * Mathf.Sin(k * Mathf.Lerp(6f, 18f, Mathf.Clamp01(k / GameSim.ShiftTelegraph)));
                foreach (var t in telegraphing.Cells)
                {
                    if (warnPlates.TryGetValue(t, out var plate))
                    {
                        plate.enabled = true;
                        plate.GetPropertyBlock(block);
                        bool closing = !maze.IsSolid(t);
                        var c = closing ? new Color(1f, 0.2f, 0.25f) : new Color(1f, 0.7f, 0.2f);
                        block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, 0.25f + 0.55f * pulse));
                        plate.SetPropertyBlock(block);
                    }
                    if (!calm && shiftBlocks.TryGetValue(t, out var b) && maze.IsSolid(t))
                        b.localPosition = new Vector3(b.localPosition.x, Mathf.Sin(k * 50f) * 0.015f, b.localPosition.z);
                }
            }
            // risk room countdowns
            foreach (var (room, door, label, light) in rooms)
            {
                if (room.Sealed || !room.Entered) continue;
                label.text = Mathf.CeilToInt(room.TimeLeft).ToString();
                float urgency = room.TimeLeft <= 3f ? (calm ? 1f : 0.5f + 0.5f * Mathf.Sin(time * 14f)) : 0.6f;
                light.intensity = 0.6f + 1.6f * urgency;
            }
            // lamps flicker
            foreach (var (light, baseI, seed) in lamps)
            {
                if (!light) continue;
                float n = Mathf.PerlinNoise(seed, time * 3f);
                bool cut = !calm && Mathf.PerlinNoise(seed * 2f, time * 0.7f) > 0.78f && Mathf.PerlinNoise(seed, time * 25f) > 0.5f;
                light.intensity = cut ? 0.1f : baseI * (0.8f + 0.3f * n);
            }
            if (exitBeam && exitBeam.gameObject.activeSelf)
            {
                exitLight.intensity = Mathf.MoveTowards(exitLight.intensity, 3f + (calm ? 0f : Mathf.Sin(time * 3f) * 0.6f), Time.deltaTime * 3f);
                if (Camera.main) exitBeam.rotation = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f);
            }
            // pen gate shimmer
            if (gate) gate.localScale = new Vector3(1f, 1f + (calm ? 0f : Mathf.Sin(time * 8f) * 0.03f), 1f);
            if (time > nextSpark && sparks.Count > 0)
            {
                nextSpark = time + Random.Range(1.2f, 3.5f);
                var ps = sparks[Random.Range(0, sparks.Count)];
                if (ps) ps.Emit(Random.Range(8, 20));
            }
        }
    }
}
