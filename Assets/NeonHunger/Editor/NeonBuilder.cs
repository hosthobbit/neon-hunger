using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonHunger.EditorTools
{
    /// <summary>
    /// Builds the project from code: settings, quality presets, post-processing, textures,
    /// materials and the scene, plus desktop and browser players. Levels themselves are built at
    /// runtime from the map data.
    ///   Menu:   NEON HUNGER > Build Everything
    ///   Batch:  Unity -batchmode -nographics -projectPath . -executeMethod NeonHunger.EditorTools.NeonBuilder.GenerateAndBuildWebGL -quit
    /// </summary>
    public static class NeonBuilder
    {
        public const string Root = "Assets/NeonHunger/";
        public const string Gen = Root + "Generated/";
        public const string ScenePath = Root + "Scenes/NeonHunger.unity";
        const string LinuxPath = "Build/Linux/NeonHunger.x86_64";
        public const string WebGLPath = "Build/WebGL";

        [MenuItem("NEON HUNGER/Build Everything (assets + scene)")]
        public static void GenerateAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ConfigureProject();
            foreach (var d in new[] { Gen, Gen + "Materials", Root + "Scenes", Root + "Settings" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            Mats.Make();
            MakePipelines();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"NH_GENERATE_OK in {sw.Elapsed.TotalSeconds:F1}s");
        }

        [MenuItem("NEON HUNGER/Build Linux Player")]
        public static void BuildLinux()
        {
            ConfigureProject();
            Directory.CreateDirectory("Build/Linux");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = LinuxPath, target = BuildTarget.StandaloneLinux64 });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
            Debug.Log($"NH_BUILD_OK size={report.summary.totalSize / (1024 * 1024)}MB");
        }

        public static void GenerateAndBuildLinux() { GenerateAll(); BuildLinux(); }

        [MenuItem("NEON HUNGER/Build Web (WebGL) Player")]
        public static void BuildWebGL()
        {
            ConfigureProject();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            // GitHub Pages can't send Content-Encoding headers for .unityweb files, so ship gzip with
            // the built-in JavaScript decompression fallback (works on any static host)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.template = "PROJECT:NeonHunger";
            PlayerSettings.runInBackground = false;
            if (Directory.Exists(WebGLPath)) Directory.Delete(WebGLPath, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = WebGLPath, target = BuildTarget.WebGL });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("WebGL build failed: " + report.summary.result);
            Debug.Log("NH_WEBGL_OK");
        }

        public static void GenerateAndBuildWebGL() { GenerateAll(); BuildWebGL(); }

        static void ConfigureProject()
        {
            PlayerSettings.companyName = "Host Hobbit";
            PlayerSettings.productName = "NEON HUNGER";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
            try { PlayerSettings.SplashScreen.show = false; } catch { }
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input = so.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ------------------------------------------------------------------ pipelines

        static void MakePipelines()
        {
            var renderer = Recreate(Root + "Settings/Renderer.asset", () => ScriptableObject.CreateInstance<UniversalRendererData>());
            renderer.renderingMode = RenderingMode.Forward;
            EditorUtility.SetDirty(renderer);
            UniversalRenderPipelineAsset Pipe(string name, int msaa, int shadowRes, float scale, int lights)
            {
                string path = Root + $"Settings/URP_{name}.asset";
                if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
                var urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, path);
                urp.renderScale = scale;
                urp.msaaSampleCount = msaa;
                urp.supportsHDR = true;
                urp.shadowDistance = 25f;
                urp.shadowCascadeCount = 1;
                urp.mainLightShadowmapResolution = Mathf.Max(256, shadowRes);
                urp.maxAdditionalLightsCount = lights;
                var so = new SerializedObject(urp);
                SetBool(so, "m_MainLightShadowsSupported", shadowRes > 0);
                SetBool(so, "m_AdditionalLightShadowsSupported", false);
                SetBool(so, "m_SoftShadowsSupported", shadowRes > 0);
                SetBool(so, "m_SupportsTerrainHoles", false);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
                return urp;
            }
            var low = Pipe("Low", 1, 0, 0.8f, 4);
            var med = Pipe("Medium", 2, 0, 1f, 6);
            var high = Pipe("High", 4, 2048, 1f, 8);
            GraphicsSettings.defaultRenderPipeline = high;
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var list = qs.FindProperty("m_QualitySettings");
            while (list.arraySize < 3) list.InsertArrayElementAtIndex(list.arraySize);
            while (list.arraySize > 3) list.DeleteArrayElementAtIndex(list.arraySize - 1);
            var pipes = new[] { low, med, high };
            string[] names = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = names[i];
                e.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipes[i];
                var v = e.FindPropertyRelative("vSyncCount"); if (v != null) v.intValue = 1;
                var soft = e.FindPropertyRelative("softParticles"); if (soft != null) soft.boolValue = false;
                var tex = e.FindPropertyRelative("globalTextureMipmapLimit"); if (tex != null) tex.intValue = i == 0 ? 1 : 0;
            }
            qs.FindProperty("m_CurrentQuality").intValue = 2;
            var per = qs.FindProperty("m_PerPlatformDefaultQuality");
            if (per != null)
                for (int i = 0; i < per.arraySize; i++)
                {
                    var e = per.GetArrayElementAtIndex(i);
                    string plat = e.FindPropertyRelative("first").stringValue;
                    e.FindPropertyRelative("second").intValue = plat == "WebGL" ? 1 : plat == "Android" || plat == "iPhone" ? 0 : 2;
                }
            qs.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        static T Recreate<T>(string path, Func<T> make) where T : UnityEngine.Object
        {
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var a = make();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void SetBool(SerializedObject so, string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }

        static VolumeProfile Profile(string name, Action<VolumeProfile> fill)
        {
            string path = Root + $"Settings/{name}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            fill(profile);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.11f, 0.17f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.fogColor = new Color(0.01f, 0.012f, 0.03f);
            RenderSettings.skybox = null;

            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.65f, 1f);
            moon.intensity = 0.22f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.6f;
            moon.transform.rotation = Quaternion.Euler(62f, 25f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.005f, 0.006f, 0.015f);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CameraRig>();
            camGo.transform.position = new Vector3(10, 14, -18);
            camGo.transform.rotation = Quaternion.Euler(58, 0, 0);

            var baseProfile = Profile("PostBase", p =>
            {
                p.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
                var b = p.Add<Bloom>(); b.threshold.Override(1.0f); b.intensity.Override(0.75f); b.scatter.Override(0.68f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.38f); v.smoothness.Override(0.45f);
                var ca = p.Add<ColorAdjustments>(); ca.postExposure.Override(0.05f); ca.contrast.Override(20f); ca.saturation.Override(10f);
                var g = p.Add<FilmGrain>(); g.type.Override(FilmGrainLookup.Thin1); g.intensity.Override(0.18f);
            });
            var surgeProfile = Profile("PostSurge", p =>
            {
                var b = p.Add<Bloom>(); b.intensity.Override(1.5f); b.threshold.Override(0.8f);
                var ca = p.Add<ColorAdjustments>(); ca.colorFilter.Override(new Color(0.92f, 0.78f, 1.15f)); ca.saturation.Override(20f);
                var ab = p.Add<ChromaticAberration>(); ab.intensity.Override(0.18f);
            });
            var dangerProfile = Profile("PostDanger", p =>
            {
                var v = p.Add<Vignette>(); v.color.Override(new Color(0.6f, 0f, 0.08f)); v.intensity.Override(0.48f);
                var ca = p.Add<ColorAdjustments>(); ca.colorFilter.Override(new Color(1.12f, 0.88f, 0.9f));
            });
            Volume Vol(string name, VolumeProfile prof, float weight, int priority)
            {
                var go = new GameObject(name);
                var vol = go.AddComponent<Volume>();
                vol.isGlobal = true; vol.sharedProfile = prof; vol.weight = weight; vol.priority = priority;
                return vol;
            }
            Vol("Post Base", baseProfile, 1f, 0);
            var surge = Vol("Post Surge", surgeProfile, 0f, 1);
            var danger = Vol("Post Danger", dangerProfile, 0f, 2);
            var atmos = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmos.SurgeVolume = surge; atmos.DangerVolume = danger; atmos.Moon = moon;

            var game = new GameObject("Game");
            var art = game.AddComponent<Art>();
            Mats.Assign(art);
            game.AddComponent<Sfx>();
            var ui = game.AddComponent<UIManager>();
            var gm = game.AddComponent<GameManager>();
            gm.Art = art; gm.Cam = cam; gm.Rig = rig; gm.Atmos = atmos; gm.Ui = ui;
            new GameObject("QA").AddComponent<QaDriver>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("NH_SCENE ok");
        }
    }

    /// <summary>Every material: URP Lit for solid metal, Unlit (HDR) for neon so it glows under bloom.</summary>
    public static class Mats
    {
        static Shader lit, unlit, particles;
        static Material Floor, FloorPen, FloorRisk, Base, Wall, WallTop, NeonCyan, NeonViolet, NeonCrimson, NeonAmber, ShiftWall, Warning, Gate, Door, Beam, Lamp,
            Node, Power, Gem, RiskGem, PlayerBody, PlayerEye, Pupil, Mouth, HunterEye, Stalker, Ambusher, Warden, Listener, Frightened, Trail, Spark, Mist;

        static Material Lit(string name, Color c, Texture2D tex = null, float smooth = 0.5f, float metal = 0f, Color? emission = null, Texture2D emissionMap = null, float tiling = 1f)
        {
            var m = new Material(lit) { name = name };
            m.SetColor("_BaseColor", c);
            if (tex) { m.SetTexture("_BaseMap", tex); m.mainTextureScale = Vector2.one * tiling; }
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                if (emissionMap) { m.SetTexture("_EmissionMap", emissionMap); m.SetTextureScale("_EmissionMap", Vector2.one * tiling); }
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            return Save(m);
        }

        static Material Unlit(string name, Color c, Texture2D tex = null, bool transparent = false, bool additive = false)
        {
            var m = new Material(unlit) { name = name };
            m.SetColor("_BaseColor", c);
            if (tex) m.SetTexture("_BaseMap", tex);
            if (transparent)
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", additive ? 2 : 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            m.enableInstancing = true;
            return Save(m);
        }

        static Material Particle(string name, Texture2D tex, bool additive)
        {
            var m = new Material(particles) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return Save(m);
        }

        static Material Save(Material m)
        {
            string path = NeonBuilder.Gen + "Materials/" + m.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) { existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); existing.shaderKeywords = m.shaderKeywords; existing.renderQueue = m.renderQueue; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static void Make()
        {
            lit = Shader.Find("Universal Render Pipeline/Lit");
            unlit = Shader.Find("Universal Render Pipeline/Unlit");
            particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!lit || !unlit || !particles) throw new Exception("URP shaders not found");

            var plate = TextureKit.FloorPlate("FloorPlate");
            var seams = TextureKit.FloorSeams("FloorSeams");
            var panel = TextureKit.WallPanels("WallPanels");
            var hazard = TextureKit.Hazard("Hazard");
            var stripes = TextureKit.Stripes("Stripes");
            var soft = TextureKit.Soft("Soft");
            var beam = TextureKit.Beam("Beam");

            Floor = Lit("Floor", new Color(0.42f, 0.45f, 0.53f), plate, 0.55f, 0.6f, new Color(0.1f, 0.5f, 0.7f) * 0.35f, seams);
            FloorPen = Lit("FloorPen", new Color(0.3f, 0.2f, 0.45f), plate, 0.5f, 0.6f, new Color(0.5f, 0.2f, 1f) * 0.6f, seams);
            FloorRisk = Lit("FloorRisk", new Color(0.45f, 0.18f, 0.22f), plate, 0.5f, 0.6f, new Color(1f, 0.1f, 0.25f) * 0.6f, seams);
            Base = Unlit("Void", new Color(0.004f, 0.005f, 0.012f));
            Wall = Lit("Wall", new Color(0.3f, 0.31f, 0.37f), panel, 0.45f, 0.7f);
            WallTop = Lit("WallTop", new Color(0.2f, 0.21f, 0.26f), panel, 0.35f, 0.6f);
            NeonCyan = Unlit("NeonCyan", new Color(0.15f, 0.8f, 1f) * 1.25f);
            NeonViolet = Unlit("NeonViolet", new Color(0.6f, 0.3f, 1f) * 1.5f);
            NeonCrimson = Unlit("NeonCrimson", new Color(1f, 0.08f, 0.22f) * 1.6f);
            NeonAmber = Unlit("NeonAmber", new Color(1f, 0.55f, 0.12f) * 1.5f);
            ShiftWall = Lit("ShiftWall", new Color(0.55f, 0.5f, 0.45f), hazard, 0.4f, 0.6f, new Color(0.4f, 0.22f, 0.02f), hazard);
            Warning = Unlit("WarningPlate", new Color(1f, 0.7f, 0.2f, 0.6f), stripes, true, true);
            Gate = Unlit("PenGate", new Color(0.65f, 0.35f, 1f) * 2.4f);
            Door = Lit("RiskDoor", new Color(0.3f, 0.08f, 0.1f), hazard, 0.5f, 0.6f, new Color(0.5f, 0.02f, 0.06f), hazard);
            Beam = Unlit("ExitBeam", new Color(0.3f, 0.95f, 1f, 0.55f), beam, true, true);
            Lamp = Unlit("LampStrip", new Color(0.8f, 0.95f, 1f) * 3f);
            Node = Unlit("EnergyNode", new Color(0.3f, 0.9f, 1f) * 1.5f);
            Power = Unlit("PowerCore", new Color(0.7f, 0.35f, 1f) * 1.9f);
            Gem = Lit("Gem", new Color(1f, 0.15f, 0.3f), null, 0.9f, 0.2f, new Color(1f, 0.08f, 0.22f) * 2.2f);
            RiskGem = Lit("RiskGem", new Color(1f, 0.85f, 0.9f), null, 0.95f, 0.2f, new Color(1f, 0.6f, 0.7f) * 2.6f);
            PlayerBody = Lit("PlayerBody", new Color(1f, 0.62f, 0.2f), null, 0.6f, 0f, new Color(1f, 0.6f, 0.2f));
            PlayerEye = Unlit("PlayerEye", new Color(0.97f, 0.97f, 1f));
            Pupil = Unlit("Pupil", new Color(0.02f, 0.02f, 0.04f));
            Mouth = Unlit("Mouth", new Color(0.08f, 0.0f, 0.04f));
            HunterEye = Unlit("HunterEye", new Color(1f, 0.98f, 0.95f) * 2f);
            Stalker = Lit("Stalker", new Color(0.2f, 0.025f, 0.045f), null, 0.35f, 0.2f, new Color(1f, 0.16f, 0.26f) * 0.3f);
            Ambusher = Lit("Ambusher", new Color(0.12f, 0.045f, 0.2f), null, 0.35f, 0.2f, new Color(0.72f, 0.3f, 1f) * 0.3f);
            Warden = Lit("Warden", new Color(0.17f, 0.1f, 0.035f), null, 0.35f, 0.3f, new Color(1f, 0.66f, 0.2f) * 0.3f);
            Listener = Lit("Listener", new Color(0.035f, 0.15f, 0.11f), null, 0.35f, 0.2f, new Color(0.2f, 1f, 0.7f) * 0.3f);
            Frightened = Lit("Frightened", new Color(0.05f, 0.08f, 0.3f), null, 0.6f, 0.2f, new Color(0.2f, 0.35f, 1f) * 0.7f);
            Trail = Particle("Trail", soft, true);
            Spark = Particle("Spark", soft, true);
            Mist = Particle("Mist", soft, false);
            AssetDatabase.SaveAssets();
        }

        public static void Assign(Art a)
        {
            a.Floor = Floor; a.FloorPen = FloorPen; a.FloorRisk = FloorRisk; a.Base = Base; a.Wall = Wall; a.WallTop = WallTop;
            a.NeonCyan = NeonCyan; a.NeonViolet = NeonViolet; a.NeonCrimson = NeonCrimson; a.NeonAmber = NeonAmber; a.ShiftWall = ShiftWall;
            a.Warning = Warning; a.Gate = Gate; a.Door = Door; a.Beam = Beam; a.Lamp = Lamp;
            a.Node = Node; a.Power = Power; a.Gem = Gem; a.RiskGem = RiskGem;
            a.PlayerBody = PlayerBody; a.PlayerEye = PlayerEye; a.Pupil = Pupil; a.Mouth = Mouth; a.HunterEye = HunterEye;
            a.Stalker = Stalker; a.Ambusher = Ambusher; a.Warden = Warden; a.Listener = Listener; a.Frightened = Frightened; a.Trail = Trail;
            a.Spark = Spark; a.Mist = Mist;
            a.WorldFont = Resources.Load<Font>("Fonts/Audiowide");
        }
    }
}
