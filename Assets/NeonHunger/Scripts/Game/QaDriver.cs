using System;
using System.Collections;
using System.IO;
using NeonHunger.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using PlayerInput = NeonHunger.Core.PlayerInput;
using UnityEngine.InputSystem.LowLevel;

namespace NeonHunger
{
    /// <summary>
    /// Automated testing only. Enabled by command-line flags (desktop) or URL parameters (browser):
    ///   --autoplay / ?autoplay   the AutoPilot bot plays complete runs through the real game
    ///   --autoshot DIR           also saves screenshots of each screen and key moments
    ///   --shotsonly              with --autoshot: one short run for screenshots only
    ///   --inputtest              drives the game with virtual keyboard and gamepad devices
    ///   --quality N              force a graphics preset
    /// Results are logged as NH_RUN / NH_PERF / NH_INPUT / NH_QA_DONE lines.
    /// </summary>
    public class QaDriver : MonoBehaviour
    {
        bool autoplay, inputTest, shotsOnly;
        string shotDir;
        int passes, fails, frames, errors;
        float frameSum, worstFrame;
        GameManager G => GameManager.Instance;

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            string url = Application.absoluteURL ?? "";
            if (Array.IndexOf(args, "--autoplay") >= 0 || Array.IndexOf(args, "--autoshot") >= 0 || Array.IndexOf(args, "--inputtest") >= 0 || url.Contains("autoplay"))
            {
                Settings.Prefix = "nhqa.";
                Settings.ResetAll();
                Settings.Muted = true;    // automated runs stay silent
            }
        }

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--autoplay") autoplay = true;
                if (args[i] == "--autoshot" && i + 1 < args.Length) { autoplay = true; shotDir = args[i + 1]; }
                if (args[i] == "--shotsonly") shotsOnly = true;
                if (args[i] == "--inputtest") inputTest = true;
                if (args[i] == "--quality" && i + 1 < args.Length && int.TryParse(args[i + 1], out int q)) Settings.Quality = q;
            }
            string url = Application.absoluteURL ?? "";
            if (url.Contains("autoplay")) autoplay = true;
            if (!autoplay && !inputTest) { enabled = false; return; }
            GameManager.IgnoreFocusLoss = true;
            Application.logMessageReceived += (msg, stack, type) => { if (type == LogType.Exception || type == LogType.Error) errors++; };
            G.ApplySettings();
            StartCoroutine(inputTest ? InputTest() : Autoplay());
        }

        void Update()
        {
            if (Time.frameCount > 30) { frames++; frameSum += Time.unscaledDeltaTime; worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime); }
        }

        void LogPerf(string label)
        {
            float avg = frames > 0 ? frameSum / frames : 0f;
            Debug.Log($"NH_PERF {label} frames={frames} avg_ms={avg * 1000f:0.0} avg_fps={(avg > 0 ? 1f / avg : 0):0.0} worst_ms={worstFrame * 1000f:0.0} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} res={Screen.width}x{Screen.height} gpu={SystemInfo.graphicsDeviceName}");
            frames = 0; frameSum = 0; worstFrame = 0;
        }

        IEnumerator CloseUps()
        {
            var rig = G.Rig;
            float keep = rig.TilesAcross;
            G.SetPaused(true);
            G.Ui.ShowPause(false);
            rig.TilesAcross = 6.5f;
            var minKeep = rig.MinXZ; var maxKeep = rig.MaxXZ;
            rig.MinXZ = new Vector2(-50, -50); rig.MaxXZ = new Vector2(50, 50);
            var sim2 = G.Sim;
            rig.Target = Art.World(sim2.Player.X, sim2.Player.Y);
            rig.Snap();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("12_closeup_player");
            for (int k = 0; k < 4; k++)
            {
                var hk = sim2.Hunters[k];
                if (hk.Mode == HunterMode.Returning) continue;
                rig.Target = Art.World(hk.Mover.X, hk.Mover.Y);
                rig.Snap();
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("13_closeup_" + hk.Kind.ToString().ToLower());
            }
            rig.TilesAcross = keep;
            rig.MinXZ = minKeep; rig.MaxXZ = maxKeep;
            G.SetPaused(false);
        }

        /// <summary>The bonus pills: one lying in the maze, then each effect just after it's taken.</summary>
        IEnumerator PillShots()
        {
            var sim = G.Sim;
            var field = sim.Maze.Distances(sim.Player.Tile, sim.Maze.PlayerCanEnter);
            bool placed = false;
            for (int r = 0; r < sim.Maze.Height && !placed; r++)
                for (int c = 0; c < sim.Maze.Width && !placed; c++)
                {
                    var t = new Tile(c, r);
                    if (sim.Maze.Dist(field, t) == 3 && sim.Maze.HunterCanEnter(t, false)) placed = sim.SpawnPickup(PowerUp.Home, t);
                }
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("14_pill");
            foreach (var (kind, name, wait) in new[] { (PowerUp.Freeze, "15_freeze", 0.8f), (PowerUp.Shield, "16_shield", 0.5f), (PowerUp.Home, "17_go_home", 1.2f) })
            {
                if (G.Sim != sim || sim.State != SimState.Playing) yield break;
                sim.SpawnPickup(kind, sim.Player.Tile);
                yield return new WaitForSecondsRealtime(wait);
                yield return Shot(name);
            }
        }

        IEnumerator Shot(string name)
        {
            if (shotDir == null) yield break;
            Directory.CreateDirectory(shotDir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"));
            Debug.Log("NH_SHOT " + name);
            yield return null;
        }

        static IEnumerator WaitFor(Func<bool> cond, float timeout)
        {
            for (float t = 0; t < timeout && !cond(); t += Time.unscaledDeltaTime) yield return null;
        }

        // ================================================================== bot runs

        IEnumerator Autoplay()
        {
            Debug.Log("NH_QA autoplay" + (shotDir != null ? " autoshot " + shotDir : ""));
            yield return new WaitForSecondsRealtime(4f);
            yield return Shot("01_title");
            if (shotDir != null)
            {
                G.Ui.ShowControls(); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("02_controls"); G.Ui.CloseModal();
                G.Ui.ShowSettings(); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("03_settings"); G.Ui.CloseModal();
            }
            int runs = shotsOnly ? 1 : 3;
            for (int run = 0; run < runs; run++)
            {
                GameManager.BotPlays = true;
                int errs0 = errors;
                float t0 = Time.realtimeSinceStartup;
                int surges = 0, caught = 0, shifts = 0, risks = 0, deaths = 0, levelsCleared = 0;
                bool shotIntro = false, shotPlay = false, shotClose = false, shotSurge = false, shotShift = false, shotRisk = false, shotClear = false, shotLate = false, shotPills = false;
                Action<SimEvent> count = e =>
                {
                    if (e.Kind == SimEventKind.SurgeStart) surges++;
                    if (e.Kind == SimEventKind.HunterCaught) caught++;
                    if (e.Kind == SimEventKind.Shift) shifts++;
                    if (e.Kind == SimEventKind.RiskEntered) risks++;
                    if (e.Kind == SimEventKind.PlayerCaught) deaths++;
                    if (e.Kind == SimEventKind.LevelCleared) levelsCleared++;
                };
                G.OnSimEvent += count;
                G.NewRun();
                if (run == 2) G.Run.Lives = 20;   // the third run has spare lives so it reaches level 3 and the victory screen
                while (G.Current != GameManager.Phase.GameOver && G.Current != GameManager.Phase.Victory && Time.realtimeSinceStartup - t0 < 1500f)
                {
                    var sim = G.Sim;
                    if (shotDir != null && run == 0)
                    {
                        if (!shotIntro && G.Current == GameManager.Phase.Intro && G.PhaseTime > 0.5f) { shotIntro = true; yield return Shot("04_level_intro"); }
                        if (!shotPlay && G.Current == GameManager.Phase.Playing && G.PhaseTime > 12f) { shotPlay = true; yield return Shot("05_gameplay"); }
                        if (!shotClose && G.Current == GameManager.Phase.Playing && G.PhaseTime > 18f && Array.TrueForAll(sim.Hunters, h => h.Mode == HunterMode.Hunting)) { shotClose = true; yield return CloseUps(); }
                        if (!shotPills && shotClose && G.LevelIndex == 0 && G.Current == GameManager.Phase.Playing && sim.State == SimState.Playing && G.PhaseTime > 24f) { shotPills = true; yield return PillShots(); }
                        if (!shotSurge && sim.SurgeActive && sim.SurgeLeft < sim.Def.SurgeSeconds - 1f && Array.Exists(sim.Hunters, h => h.Mode == HunterMode.Fleeing)) { shotSurge = true; yield return Shot("06_power_surge"); }
                        if (!shotShift && sim.PendingShift != null && sim.ShiftTelegraphLeft < 1.8f) { shotShift = true; yield return Shot("07_walls_warning"); }
                        if (!shotRisk && Array.Exists(sim.Maze.RiskRooms.ToArray(), r => r.Entered && !r.Sealed && r.TimeLeft < sim.Def.RiskSeconds - 1f)) { shotRisk = true; yield return Shot("08_risk_room"); }
                        if (!shotClear && G.Current == GameManager.Phase.LevelClear && G.PhaseTime > 1f) { shotClear = true; yield return Shot("09_level_clear"); }
                        if (!shotLate && G.LevelIndex == 2 && G.Current == GameManager.Phase.Playing && G.PhaseTime > 20f) { shotLate = true; yield return Shot("10_level3"); }
                        if (shotsOnly && shotClear && shotSurge && shotShift && G.LevelIndex >= 1 && G.PhaseTime > 5f && (shotRisk || G.LevelIndex >= 2)) break;
                    }
                    yield return null;
                }
                G.OnSimEvent -= count;
                bool finished = G.Current == GameManager.Phase.GameOver || G.Current == GameManager.Phase.Victory;
                if (finished) { yield return new WaitForSecondsRealtime(1.5f); if (run == 0) yield return Shot(G.Current == GameManager.Phase.Victory ? "11_victory" : "11_game_over"); }
                bool ok = (finished || shotsOnly) && errors == errs0;
                Debug.Log($"NH_RUN {run + 1} {(ok ? "PASS" : "FAIL")} result={G.Current} reached_level={G.LevelIndex + 1} cleared={levelsCleared} score={G.Run.Total} encoded={G.Run.Encode()} " +
                          $"deaths={deaths} surges={surges} hunters_caught={caught} wall_shifts={shifts} risk_rooms={risks} errors={errors - errs0} real={Time.realtimeSinceStartup - t0:0}s");
                if (ok) passes++; else fails++;
            }
            GameManager.BotPlays = false;
            LogPerf("autoplay");
            Debug.Log($"NH_QA_DONE pass={passes} fail={fails} errors={errors}");
            if (!Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer) Application.Quit();
        }

        // ================================================================== input test

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) passes++; else fails++;
            Debug.Log($"NH_INPUT {name} {(ok ? "PASS" : "FAIL")} {detail}");
        }

        static void Keys(Keyboard kb, params Key[] keys) => InputSystem.QueueStateEvent(kb, new KeyboardState(keys));

        IEnumerator Tap(Keyboard kb, Key key)
        {
            Keys(kb, key);
            yield return null; yield return null;
            Keys(kb);
            yield return null; yield return null;
        }

        IEnumerator InputTest()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var kb = InputSystem.AddDevice<Keyboard>("QA Keyboard");
            var pad = InputSystem.AddDevice<Gamepad>("QA Gamepad");
            yield return new WaitForSecondsRealtime(3f);

            var sel = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            Check("title_focus_play", sel && sel.name.StartsWith("PLAY"), sel ? sel.name : "nothing");
            yield return Tap(kb, Key.Enter);
            yield return WaitFor(() => G.Current == GameManager.Phase.Intro || G.Current == GameManager.Phase.Playing, 5f);
            Check("enter_starts_run", G.Current == GameManager.Phase.Intro || G.Current == GameManager.Phase.Playing, G.Current.ToString());
            // keep the hunters home so the movement checks are about movement
            foreach (var h in G.Sim.Hunters) h.ReleaseAt = 9999f;
            yield return WaitFor(() => G.Current == GameManager.Phase.Playing, 10f);

            // the level 1 start is on an open corridor: hold Left and the player moves left
            float x0 = G.Sim.Player.X;
            Keys(kb, Key.A);
            yield return new WaitForSecondsRealtime(0.5f);
            Keys(kb);
            Check("hold_A_moves_left", G.Sim.Player.X < x0 - 0.5f, $"x {x0:0.00} -> {G.Sim.Player.X:0.00}");

            // buffered turn: press Down shortly before reaching the junction below the start corridor
            var sim = G.Sim;
            sim.Player.Place(new Tile(6, 12));                 // corridor row 12, heading right towards column 10
            Keys(kb, Key.RightArrow);
            yield return null; yield return null;
            Keys(kb);
            yield return WaitFor(() => sim.Player.X > 9.1f, 5f);
            Keys(kb, Key.DownArrow);                          // early: the player is still before column 10
            yield return null; yield return null;
            Keys(kb);
            yield return WaitFor(() => sim.Player.Y > 12.4f || sim.Player.X > 10.6f, 3f);
            Check("buffered_turn_at_junction", sim.Player.Y > 12.4f && Mathf.Abs(sim.Player.X - 10f) < 0.01f, $"pos {sim.Player.X:0.00},{sim.Player.Y:0.00}");

            // walls stop the player exactly on the centre-line
            sim.Player.Place(new Tile(1, 1));
            Keys(kb, Key.UpArrow);
            yield return new WaitForSecondsRealtime(0.4f);
            Keys(kb);
            Check("wall_stops_player", Mathf.Abs(sim.Player.Y - 1f) < 0.001f && sim.Player.Dir == Dir.None, $"y={sim.Player.Y:0.000}");

            // dash with space: cooldown starts
            sim.Player.Place(new Tile(1, 4));
            Keys(kb, Key.D);
            yield return Tap(kb, Key.Space);
            Keys(kb);
            Check("space_dashes", sim.DashCooldownLeft > 0f, $"cooldown={sim.DashCooldownLeft:0.00}");

            // gamepad steering
            sim.Player.Place(new Tile(1, 4));
            yield return null;
            x0 = sim.Player.X;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(1f, 0f) });
            yield return new WaitForSecondsRealtime(0.4f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            Check("gamepad_stick_moves", sim.Player.X > x0 + 0.5f, $"x {x0:0.00} -> {sim.Player.X:0.00}");

            // pause freezes everything; Enter on Resume resumes
            yield return Tap(kb, Key.Escape);
            float clock = sim.Clock;
            yield return new WaitForSecondsRealtime(0.5f);
            Check("escape_pauses", G.Paused && Time.timeScale == 0f && Mathf.Approximately(clock, sim.Clock));
            yield return Tap(kb, Key.Enter);
            yield return new WaitForSecondsRealtime(0.3f);
            Check("enter_resumes", !G.Paused && sim.Clock > clock);

            // collecting nodes scores
            int score0 = G.Run.Total;
            sim.Player.Place(new Tile(1, 13));
            Keys(kb, Key.RightArrow);
            yield return new WaitForSecondsRealtime(0.6f);
            Keys(kb);
            Check("nodes_score", G.Run.Total > score0, $"{score0} -> {G.Run.Total}");

            // R restarts the run instantly
            G.Run.Lives = 1;
            yield return Tap(kb, Key.R);
            yield return null;
            Check("r_restarts", G.Run.Lives == 3 && G.LevelIndex == 0 && G.Run.Total == 0 && (G.Current == GameManager.Phase.Intro));

            // settings persist
            Settings.ReducedFlashing = true; Settings.Save();
            Check("settings_persist", PlayerPrefs.GetInt(Settings.Prefix + "lessFlash", 0) == 1);
            Settings.ReducedFlashing = false; Settings.Save();

            // best score persists after a run ends
            G.Sim.Run.Lives = 0;
            foreach (var h in G.Sim.Hunters) { h.SetMode(HunterMode.Hunting); }
            yield return WaitFor(() => G.Current == GameManager.Phase.Playing, 10f);
            G.Sim.Hunters[0].SetMode(HunterMode.Hunting);
            G.Sim.Hunters[0].Mover.Place(G.Sim.Player.Tile);
            yield return WaitFor(() => G.Current == GameManager.Phase.GameOver, 8f);
            Check("game_over_screen", G.Current == GameManager.Phase.GameOver && G.Ui.PanelOpen, G.Current.ToString());

            Check("no_console_errors", errors == 0, $"errors={errors}");
            LogPerf("inputtest");
            Debug.Log($"NH_INPUT_DONE pass={passes} fail={fails}");
            if (!Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer) Application.Quit();
        }

        // ================================================================== browser hooks

        /// <summary>From the page: unityInstance.SendMessage("QA", "WebCommand", "play" | "state" | "swipe:x,y,dx,dy").</summary>
        public void WebCommand(string cmd)
        {
            if (cmd == "play") { G.NewRun(); return; }
            if (cmd == "state")
            {
                var s = G.Sim;
                Debug.Log($"NH_STATE phase={G.Current} level={G.LevelIndex + 1} sim={s.State} pos={s.Player.X:0.00},{s.Player.Y:0.00} dir={s.Player.Dir} nodes={s.Maze.NodesLeft} score={G.Run.Total} input={G.Input.Last} touch=[{G.Input.TouchDebug}] screen={Screen.width}x{Screen.height} dpi={Screen.dpi}");
            }
        }
    }
}
