using System;
using System.Collections.Generic;
using NeonHunger.Core;
using UnityEngine;

namespace NeonHunger
{
    /// <summary>
    /// The game flow: title (with the machine playing itself behind the menu), level intro, play,
    /// pause, level clear, game over and victory. Owns the single authoritative GameSim, feeds it
    /// input, and turns its events into visuals, sound, hints and HUD updates.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum Phase { Title, Intro, Playing, LevelClear, GameOver, Victory }

        [Header("Links")]
        public Art Art;
        public Camera Cam;
        public CameraRig Rig;
        public Atmosphere Atmos;
        public UIManager Ui;

        public static GameManager Instance { get; private set; }
        public Phase Current { get; private set; } = Phase.Title;
        public bool Paused { get; private set; }
        public GameSim Sim { get; private set; }
        public RunScore Run { get; private set; }
        public int LevelIndex { get; private set; }
        public GameInput Input { get; private set; }
        public float PhaseTime { get; private set; }
        public HunterView[] HunterViews => hunters;
        public PlayerView PlayerViewRef => player;
        public bool NewBest { get; private set; }

        /// <summary>QA: a bot plays instead of the keyboard.</summary>
        public static bool BotPlays;
        public static bool IgnoreFocusLoss;
        public AutoPilot Bot = new AutoPilot(7) { TakeRisks = true };
        public event Action<SimEvent> OnSimEvent;

        MazeView mazeView;
        PlayerView player;
        HunterView[] hunters;
        PickupView pickup;
        ParticleSystem burst;
        readonly HashSet<string> hints = new HashSet<string>();
        int attractLevel;
        public const float IntroHold = 0.9f, ClearTime = 4f;

        void Awake()
        {
            Instance = this;
            Input = new GameInput();
            if (!Art || !Cam || !Rig || !Atmos || !Ui) Debug.LogError("NH_SCENE_ERROR GameManager is missing a reference");
            ApplySettings();
            burst = MakeBurst();
        }

        void Start()
        {
            Ui.Build(this);
            GoToTitle();
        }

        public void ApplySettings()
        {
            QualitySettings.SetQualityLevel(Mathf.Clamp(Settings.Quality, 0, QualitySettings.names.Length - 1), true);
            Application.targetFrameRate = 60;
            if (Sfx.Instance) { Sfx.Instance.MusicVolume = Settings.Music; Sfx.Instance.SoundVolume = Settings.Sound; Sfx.Instance.Muted = Settings.Muted; }
        }

        // ================================================================== flow

        public void GoToTitle()
        {
            SetPaused(false);
            Current = Phase.Title;
            PhaseTime = 0f;
            Run = new RunScore { Lives = 99 };
            LoadLevel(attractLevel % LevelDef.All.Length);
            Ui.ShowTitle();
        }

        public void NewRun()
        {
            SetPaused(false);
            Run = new RunScore();
            hints.Clear();
            NewBest = false;
            Input.ClearSwipe();
            StartLevel(0);
        }

        void StartLevel(int index)
        {
            LevelIndex = index;
            LoadLevel(index);
            Current = Phase.Intro;
            PhaseTime = 0f;
            Ui.ShowGame();
            Ui.LevelIntro(Sim.Def);
            Sfx.Play(Sfx.Kind.Ready, 0.8f);
            if (index == 0) Hint("goal", Ui.TouchMode ? "Swipe to steer. Eat every energy node, then reach the EXIT." : "WASD / arrows to steer. Eat every energy node, then reach the EXIT.", 6f);
        }

        void LoadLevel(int index)
        {
            Sim = new GameSim(LevelDef.All[index], Run, Environment.TickCount);
            if (mazeView) Destroy(mazeView.gameObject);
            mazeView = new GameObject("Maze").AddComponent<MazeView>();
            mazeView.Build(Sim.Maze, Art, Settings.Quality);
            if (player) Destroy(player.gameObject);
            player = new GameObject("Player").AddComponent<PlayerView>();
            player.Build(Art, Settings.Quality);
            if (hunters != null) foreach (var h in hunters) if (h) Destroy(h.gameObject);
            hunters = new HunterView[Sim.Hunters.Length];
            for (int i = 0; i < hunters.Length; i++)
            {
                hunters[i] = new GameObject(Sim.Hunters[i].Kind.ToString()).AddComponent<HunterView>();
                hunters[i].Build(Sim.Hunters[i].Kind, Art, Settings.Quality);
            }
            if (pickup) Destroy(pickup.gameObject);
            pickup = new GameObject("Pickup").AddComponent<PickupView>();
            pickup.Build(Art);
            var m = Sim.Maze;
            Rig.MinXZ = new Vector2(3f, -(m.Height - 1) + 1.5f);
            Rig.MaxXZ = new Vector2(m.Width - 4f, -2.5f);
            Rig.Target = Art.World(Sim.Player.X, Sim.Player.Y);
            Rig.Snap();
            Bot = new AutoPilot(index + 7) { TakeRisks = true };
        }

        public void Restart()
        {
            if (Current == Phase.Title) return;
            NewRun();
        }

        public void SetPaused(bool on)
        {
            if (on && Current != Phase.Playing && Current != Phase.Intro) return;
            Paused = on;
            Time.timeScale = on ? 0f : 1f;
            Ui.ShowPause(on);
        }

        void OnApplicationFocus(bool focus) { if (!focus && !IgnoreFocusLoss && (Current == Phase.Playing || Current == Phase.Intro)) SetPaused(true); }
        void OnApplicationPause(bool p) { if (p && !IgnoreFocusLoss && (Current == Phase.Playing || Current == Phase.Intro)) SetPaused(true); }

        // ================================================================== per frame

        void Update()
        {
            if (Input.Pause.WasPressedThisFrame() && !Ui.ModalOpen)
            {
                if (Paused) SetPaused(false);
                else if (Current == Phase.Playing || Current == Phase.Intro) SetPaused(true);
            }
            if (Input.Restart.WasPressedThisFrame() && Current != Phase.Title && !Ui.TypingName) Restart();
            if (Paused || Sim == null) { Ui.UpdateHud(this); return; }

            float dt = Time.deltaTime;
            PhaseTime += dt;
            bool bot = BotPlays || Current == Phase.Title;
            var input = bot ? Bot.Think(Sim) : Input.Read();
            if (Current == Phase.LevelClear || Current == Phase.GameOver || Current == Phase.Victory) input = default;

            // the level banner shows for a moment before the countdown starts (turns pressed now are remembered)
            if (Current == Phase.Intro && PhaseTime < IntroHold) { if (input.Want != Dir.None) Sim.Player.Want(input.Want, Sim.Now + IntroHold); }
            else Sim.Tick(dt, input);
            foreach (var e in Sim.Events) Handle(e);
            Sim.Events.Clear();

            switch (Current)
            {
                case Phase.Title:
                    if (Sim.State == SimState.Cleared || Sim.State == SimState.GameOver || PhaseTime > 90f)
                    {
                        attractLevel++;
                        PhaseTime = 0f;
                        Run = new RunScore { Lives = 99 };
                        LoadLevel(attractLevel % LevelDef.All.Length);
                    }
                    break;
                case Phase.Intro:
                    if (Sim.State == SimState.Playing) { Current = Phase.Playing; PhaseTime = 0f; }
                    break;
                case Phase.Playing:
                    if (Sim.State == SimState.Cleared) { Current = Phase.LevelClear; PhaseTime = 0f; Ui.ShowLevelClear(this); SaveBest(); }
                    else if (Sim.State == SimState.GameOver) { Current = Phase.GameOver; PhaseTime = 0f; Sfx.Play(Sfx.Kind.GameOver); SaveBest(); Ui.ShowRunEnd(this, false); }
                    LevelHints();
                    break;
                case Phase.LevelClear:
                    if (PhaseTime >= ClearTime || (PhaseTime > 1.2f && Ui.ContinuePressed()))
                    {
                        if (LevelIndex + 1 < LevelDef.All.Length) StartLevel(LevelIndex + 1);
                        else { Current = Phase.Victory; PhaseTime = 0f; Sfx.Play(Sfx.Kind.Victory); SaveBest(); Ui.ShowRunEnd(this, true); }
                    }
                    break;
            }

            SyncViews(dt);
            Ui.UpdateHud(this);
        }

        void SyncViews(float dt)
        {
            player.Sync(Sim, dt);
            for (int i = 0; i < hunters.Length; i++) hunters[i].Sync(Sim, i, dt);
            pickup.Sync(Sim);
            Rig.Target = Art.World(Sim.Player.X, Sim.Player.Y);
            var d = Sim.Player.Dir;
            Rig.LookDir = Vector3.Lerp(Rig.LookDir, new Vector3(d.DC(), 0, -d.DR()), 1f - Mathf.Exp(-dt * 3f));
            Atmos.Surge = Sim.SurgeActive;
            bool risk = false;
            foreach (var room in Sim.Maze.RiskRooms) if (room.Entered && !room.Sealed && room.TimeLeft <= 3f) risk = true;
            Atmos.Danger = Current != Phase.Title && (Sim.PendingShift != null || risk);
            if (Sfx.Instance) Sfx.Instance.SurgeMusic = Sim.SurgeActive && Current != Phase.Title;
        }

        void SaveBest()
        {
            int total = Run.Total;
            if (total > Settings.BestScore) { Settings.BestScore = total; NewBest = true; }
            Settings.BestLevel = Mathf.Max(Settings.BestLevel, LevelIndex + (Sim.State == SimState.Cleared ? 1 : 0));
            Settings.Save();
        }

        // ================================================================== events

        void Handle(SimEvent e)
        {
            OnSimEvent?.Invoke(e);
            bool live = Current != Phase.Title;
            var at = Art.World(e.Tile.C, e.Tile.R, 0.5f);
            switch (e.Kind)
            {
                case SimEventKind.Node: mazeView.Collect(e.Tile); player.Ate(); if (live) Sfx.Play(Sfx.Kind.Node, 0.45f); break;
                case SimEventKind.Power:
                    mazeView.Collect(e.Tile); player.Ate();
                    Burst(at, new Color(0.7f, 0.4f, 1f), 40);
                    if (live) { Sfx.Play(Sfx.Kind.Power); Rig.Shake(0.35f); Hint("surge", "POWER SURGE! They're afraid of you now. Catch them for combo points.", 4f); }
                    break;
                case SimEventKind.Gem: mazeView.Collect(e.Tile); Burst(at, new Color(1f, 0.2f, 0.3f), 20); if (live) { Sfx.Play(Sfx.Kind.Gem); Ui.Popup(at, "+" + e.Value, new Color(1f, 0.35f, 0.45f)); } break;
                case SimEventKind.RiskGem: mazeView.Collect(e.Tile); Burst(at, new Color(1f, 0.85f, 0.9f), 30); if (live) { Sfx.Play(Sfx.Kind.RiskGem); Ui.Popup(at, "+" + e.Value, Color.white); } break;
                case SimEventKind.ExitOpen:
                    mazeView.OpenExit();
                    if (live) { Sfx.Play(Sfx.Kind.ExitOpen); Hint("exit", "The EXIT is open. Get out!", 4f, true); }
                    break;
                case SimEventKind.LevelCleared: if (live) { Sfx.Play(Sfx.Kind.LevelClear); Ui.Popup(at, "+" + e.Value, UiKit.Cyan); } break;
                case SimEventKind.SurgeWarning: if (live) Sfx.Play(Sfx.Kind.SurgeEnding, 0.8f); break;
                case SimEventKind.HunterCaught:
                {
                    var h = Sim.Hunters[e.Hunter];
                    var pos = Art.World(h.Mover.X, h.Mover.Y, 0.6f);
                    Burst(pos, Art.HunterColour(h.Kind), 45);
                    if (live) { Sfx.Play(Sfx.Kind.Caught, 1f, 1f + 0.12f * Mathf.Log(e.Value / 200f, 2f)); Ui.Popup(pos, "+" + e.Value, Art.HunterColour(h.Kind)); Rig.Shake(0.25f); }
                    break;
                }
                case SimEventKind.HunterHeard: if (live) { Sfx.Play(Sfx.Kind.Heard, 0.6f); Hint("heard", "Your dash was loud. The Listener is coming to look.", 3.5f); } break;
                case SimEventKind.PlayerCaught:
                    player.Die();
                    Burst(Art.World(Sim.Player.X, Sim.Player.Y, 0.4f), PlayerView.Warm, 60);
                    if (live) { Sfx.Play(Sfx.Kind.Death); Rig.Shake(0.9f); }
                    break;
                case SimEventKind.Respawn: player.Respawn(); if (live) Sfx.Play(Sfx.Kind.Respawn, 0.7f); break;
                case SimEventKind.ExtraLife: if (live) { Sfx.Play(Sfx.Kind.RiskGem); Ui.Popup(Art.World(Sim.Player.X, Sim.Player.Y, 0.8f), "EXTRA LIFE", UiKit.Cyan); } break;
                case SimEventKind.Dash: if (live) Sfx.Play(Sfx.Kind.Dash, 0.7f); break;
                case SimEventKind.ShiftWarning:
                    mazeView.Telegraph(Sim.PendingShift);
                    if (live) { Sfx.Play(Sfx.Kind.ShiftAlarm, 0.8f); Hint("shift", "Warning plates! Those walls are about to move.", 4f); }
                    break;
                case SimEventKind.Shift: mazeView.ApplyShift(Sim.Maze.Groups[e.Value]); if (live) { Sfx.Play(Sfx.Kind.ShiftMove); Rig.Shake(0.2f); } break;
                case SimEventKind.ShiftCancelled: mazeView.Telegraph(null); break;
                case SimEventKind.PickupSpawned:
                    if (live) { Sfx.Play(Sfx.Kind.Ready, 0.5f, 1.4f); Hint("pickup", "A bonus pill has appeared! Grab it before it fades.", 4f); }
                    break;
                case SimEventKind.PickupTaken:
                {
                    var kind = (PowerUp)e.Value;
                    Burst(at, PickupView.Colour(kind), 45);
                    if (live)
                    {
                        Sfx.Play(Sfx.Kind.Power, 1f, kind == PowerUp.Speed ? 1.35f : kind == PowerUp.Freeze ? 0.8f : 1.15f);
                        Ui.Popup(at, PickupView.Name(kind), PickupView.Colour(kind));
                        Ui.Hint(PickupView.Blurb(kind), 3.5f);
                        Rig.Shake(0.2f);
                    }
                    break;
                }
                case SimEventKind.PowerUpEnding: if (live) Sfx.Play(Sfx.Kind.SurgeEnding, 0.6f, 1.2f); break;
                case SimEventKind.ShieldBlock:
                {
                    var h = Sim.Hunters[e.Hunter];
                    var pos = Art.World(h.Mover.X, h.Mover.Y, 0.6f);
                    Burst(pos, PickupView.Colour(PowerUp.Shield), 60);
                    if (live) { Sfx.Play(Sfx.Kind.DoorSlam); Ui.Popup(pos, "BLOCKED!", PickupView.Colour(PowerUp.Shield)); Rig.Shake(0.5f); }
                    break;
                }
                case SimEventKind.RiskEntered: if (live) { Sfx.Play(Sfx.Kind.RiskAlarm); Hint("riskIn", $"The door seals in {e.Value} seconds. Grab the bonus and get out!", 3.5f, true); } break;
                case SimEventKind.RiskWarning: if (live) Sfx.Play(Sfx.Kind.RiskAlarm, 1f, 1.3f); break;
                case SimEventKind.RiskSealed:
                    mazeView.SealDoor(Sim.Maze.RiskRooms[e.Value]);
                    if (live) { Sfx.Play(Sfx.Kind.DoorSlam); Rig.Shake(0.3f); if (e.Flag) Hint("trapped", "Trapped inside! The chamber purges.", 3f, true); }
                    break;
            }
        }

        void LevelHints()
        {
            if (LevelIndex != 0) return;
            if (PhaseTime > 7f) Hint("dash", Ui.TouchMode ? "Tap DASH for a burst of speed. It's loud, and the Listener hears it." : "SPACE / SHIFT: dash. It's loud, and the Listener hears it.", 4.5f);
            foreach (var room in Sim.Maze.RiskRooms)
                if (!room.Entered && room.Door.Manhattan(Sim.Player.Tile) <= 3)
                    Hint("risk", "RISK chamber: bonus energy inside, but the door seals on a timer.", 4.5f);
        }

        /// <summary>Show a hint once per run (and the basics only once ever, unless replaying level 1).</summary>
        void Hint(string id, string text, float seconds, bool always = false)
        {
            if (Current == Phase.Title || hints.Contains(id)) return;
            if (!always && LevelIndex > 0 && Settings.HintsSeen) return;
            hints.Add(id);
            Ui.Hint(text, seconds);
            if (id == "risk") { Settings.HintsSeen = true; Settings.Save(); }
        }

        // ================================================================== effects

        ParticleSystem MakeBurst()
        {
            var go = new GameObject("Bursts");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Art.Spark;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f; r.lengthScale = 1.3f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        void Burst(Vector3 at, Color c, int n)
        {
            if (Settings.Quality == 0) n /= 2;
            var p = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true, startColor = c * 1.5f };
            burst.Emit(p, n);
        }
    }
}
