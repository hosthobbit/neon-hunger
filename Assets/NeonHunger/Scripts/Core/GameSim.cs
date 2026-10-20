using System;
using System.Collections.Generic;

namespace NeonHunger.Core
{
    public enum SimState { Ready, Playing, Dying, Cleared, GameOver }

    public enum SimEventKind
    {
        Node, Power, Gem, RiskGem, ExitOpen, LevelCleared,
        SurgeStart, SurgeWarning, SurgeEnd, HunterCaught, HunterReleased, HunterHome, HunterHeard,
        PlayerCaught, Respawn, GameOver, ExtraLife, Dash,
        ShiftWarning, Shift, ShiftCancelled,
        RiskEntered, RiskWarning, RiskSealed,
    }

    public struct SimEvent
    {
        public SimEventKind Kind;
        public Tile Tile;
        public int Value;      // points, combo index, group index…
        public int Hunter;     // hunter index, -1 if none
        public bool Flag;      // RiskSealed: player trapped
        public override string ToString() => $"{Kind} {Tile} v={Value} h={Hunter}{(Flag ? " !" : "")}";
    }

    public struct PlayerInput
    {
        public Dir Want;
        public bool Dash;
    }

    /// <summary>
    /// One level of NEON HUNGER, simulated without any engine code: the player, four hunters, the
    /// living walls, risk rooms, the power surge, scoring and lives. The Unity side feeds it input and
    /// draws what it reports, so every rule can be tested (and whole games played) headlessly.
    /// </summary>
    public sealed class GameSim
    {
        public readonly LevelDef Def;
        public readonly Maze Maze;
        public readonly Mover Player = new Mover();
        public readonly Hunter[] Hunters;
        public readonly RunScore Run;
        public readonly LevelStats Stats;
        public readonly List<SimEvent> Events = new List<SimEvent>();

        public SimState State { get; private set; } = SimState.Ready;
        public float StateTime { get; private set; }
        /// <summary>Seconds of play on this level (excludes ready/dying pauses).</summary>
        public float Clock { get; private set; }
        public float Now { get; private set; }

        // power surge
        public float SurgeLeft { get; private set; }
        public bool SurgeActive => SurgeLeft > 0f;
        public bool SurgeEnding => SurgeLeft > 0f && SurgeLeft <= WarnSeconds;
        public const float WarnSeconds = 2.2f;
        int combo;
        bool warned;

        // dash
        public const float DashTime = 0.28f, DashCooldown = 2.6f, DashBoost = 1.9f;
        public float DashLeft { get; private set; }
        public float DashCooldownLeft { get; private set; }
        public bool Dashing => DashLeft > 0f;

        // living walls
        public const float ShiftTelegraph = 3f;
        public ShiftGroup PendingShift { get; private set; }
        public float ShiftTelegraphLeft { get; private set; }
        public float ShiftTimer { get; private set; }
        int shiftCursor, shiftRetries;

        public const float ReadyTime = 1.6f, DyingTime = 1.7f, CatchRadius = 0.62f;
        float playSince;   // Clock value when the current life started (for hunter release)
        readonly Random rng;
        readonly int[] scratchA, scratchB;

        public GameSim(LevelDef def, RunScore run, int seed = 1)
        {
            Def = def;
            Run = run ?? new RunScore();
            Maze = Maze.Parse(def.Map);
            Stats = new LevelStats { Level = def.Number };
            Run.Levels.Add(Stats);
            rng = new Random(seed);
            scratchA = new int[Maze.Width * Maze.Height];
            scratchB = new int[Maze.Width * Maze.Height];
            Hunters = new[] { new Hunter(HunterKind.Stalker), new Hunter(HunterKind.Ambusher), new Hunter(HunterKind.Warden), new Hunter(HunterKind.Listener) };
            // pen spots: the pen tiles nearest its centre
            var spots = new List<Tile>(Maze.PenTiles);
            spots.Sort((a, b) => a.Manhattan(Maze.PenCentre).CompareTo(b.Manhattan(Maze.PenCentre)));
            for (int i = 0; i < Hunters.Length; i++) Hunters[i].PenSpot = spots[Math.Min(i, spots.Count - 1)];
            ShiftTimer = def.FirstShift;
            ResetActors();
        }

        public Tile GateOut
        {
            get
            {
                foreach (var d in Dirs.All) { var n = Maze.Gate.Step(d); if (Maze.HunterCanEnter(n, false)) return n; }
                return Maze.Gate;
            }
        }

        void ResetActors()
        {
            Player.Place(Maze.Start, Dir.Left);
            for (int i = 0; i < Hunters.Length; i++)
            {
                var h = Hunters[i];
                h.Mover.Place(h.PenSpot, Dir.Up);
                h.SetMode(HunterMode.Penned);
                h.ReleaseAt = Def.Release[i];
                h.NoiseLeft = 0f; h.PatrolLeft = 0f; h.WanderLeft = 0f;
                h.EatenThisSurge = false;
            }
            SurgeLeft = 0f; combo = 0;
            DashLeft = 0f; DashCooldownLeft = 0f;
            playSince = Clock;
            State = SimState.Ready; StateTime = 0f;
        }

        void Emit(SimEventKind k, Tile t = default, int value = 0, int hunter = -1, bool flag = false)
            => Events.Add(new SimEvent { Kind = k, Tile = t, Value = value, Hunter = hunter, Flag = flag });

        // ================================================================== tick

        public void Tick(float dt, PlayerInput input)
        {
            if (dt <= 0f) return;
            dt = Math.Min(dt, 0.1f);  // never skip through walls on a long frame
            Now += dt;
            StateTime += dt;
            switch (State)
            {
                case SimState.Ready:
                    if (input.Want != Dir.None) Player.Want(input.Want, Now);
                    if (StateTime >= ReadyTime) { State = SimState.Playing; StateTime = 0f; }
                    return;
                case SimState.Dying:
                    if (StateTime >= DyingTime)
                    {
                        if (Run.Lives <= 0) { State = SimState.GameOver; StateTime = 0f; Emit(SimEventKind.GameOver); }
                        else { ResetActors(); Emit(SimEventKind.Respawn, Maze.Start); }
                    }
                    return;
                case SimState.Cleared:
                case SimState.GameOver:
                    return;
            }

            Clock += dt;
            UpdatePlayer(dt, input);
            if (State != SimState.Playing) return;
            UpdateSurge(dt);
            for (int i = 0; i < Hunters.Length; i++) UpdateHunter(i, dt);
            CheckContacts();
            if (State != SimState.Playing) return;
            UpdateRiskRooms(dt);
            if (State != SimState.Playing) return;
            UpdateShifts(dt);
            CheckExtraLife();
        }

        // ================================================================== player

        void UpdatePlayer(float dt, PlayerInput input)
        {
            if (input.Want != Dir.None) Player.Want(input.Want, Now);
            DashCooldownLeft = Math.Max(0f, DashCooldownLeft - dt);
            if (input.Dash && DashCooldownLeft <= 0f && !Dashing)
            {
                DashLeft = DashTime;
                DashCooldownLeft = DashCooldown;
                Emit(SimEventKind.Dash, Player.Tile);
                MakeNoise(Player.Tile);
            }
            float speed = Def.PlayerSpeed * (Dashing ? DashBoost : 1f) * (SurgeActive ? 1.05f : 1f);
            DashLeft = Math.Max(0f, DashLeft - dt);
            Player.Step(dt, speed, Maze.PlayerCanEnter, Now);

            var tile = Player.Tile;
            var item = Maze.Take(tile);
            switch (item)
            {
                case Item.Node:
                    Stats.Nodes++; Emit(SimEventKind.Node, tile, Points.Node);
                    if (Maze.NodesLeft == 0) Emit(SimEventKind.ExitOpen, Maze.Exit);
                    break;
                case Item.Power: Stats.Powers++; Emit(SimEventKind.Power, tile, Points.Power); StartSurge(); break;
                case Item.Gem: Stats.Gems++; Emit(SimEventKind.Gem, tile, Points.Gem); break;
                case Item.RiskGem: Stats.RiskGems++; Emit(SimEventKind.RiskGem, tile, Points.RiskGem); break;
            }
            if (Maze.ExitOpen && tile == Maze.Exit)
            {
                Stats.Cleared = true;
                Stats.Seconds = Clock;
                State = SimState.Cleared; StateTime = 0f;
                Emit(SimEventKind.LevelCleared, tile, Points.LevelClear(Def.Number) + Points.TimeBonus(Clock, Def.ParSeconds));
            }
        }

        /// <summary>A dash is loud: the Listener hears it anywhere; other hunters nearby glance over.</summary>
        public void MakeNoise(Tile at)
        {
            var field = Maze.Distances(at, t => Maze.HunterCanEnter(t, false) || Maze.PlayerCanEnter(t), scratchB);
            for (int i = 0; i < Hunters.Length; i++)
            {
                var h = Hunters[i];
                if (h.Mode != HunterMode.Hunting) continue;
                int d = Maze.Dist(field, h.Mover.Tile);
                if (h.Kind == HunterKind.Listener) { h.Noise = HunterBrain.NearestHunterTile(Maze, at); h.NoiseLeft = 7f; Emit(SimEventKind.HunterHeard, at, 0, i); }
                else if (d >= 0 && d <= 7) { h.Noise = HunterBrain.NearestHunterTile(Maze, at); h.NoiseLeft = 1.6f; }
            }
        }

        // ================================================================== surge

        void StartSurge()
        {
            SurgeLeft = Def.SurgeSeconds;
            combo = 0;
            warned = false;
            Emit(SimEventKind.SurgeStart, Player.Tile, (int)Def.SurgeSeconds);
            foreach (var h in Hunters)
            {
                h.EatenThisSurge = false;
                if (h.Mode == HunterMode.Hunting)
                {
                    h.SetMode(HunterMode.Fleeing);
                    if (h.Mover.Dir != Dir.None) h.Mover.Dir = h.Mover.Dir.Opposite();  // a visible flinch
                }
            }
        }

        void UpdateSurge(float dt)
        {
            if (!SurgeActive) return;
            SurgeLeft -= dt;
            if (!warned && SurgeLeft <= WarnSeconds) { warned = true; Emit(SimEventKind.SurgeWarning); }
            if (SurgeLeft <= 0f)
            {
                SurgeLeft = 0f;
                foreach (var h in Hunters) if (h.Mode == HunterMode.Fleeing) h.SetMode(HunterMode.Hunting);
                Emit(SimEventKind.SurgeEnd);
            }
        }

        // ================================================================== hunters

        public float HunterSpeed(int i)
        {
            var h = Hunters[i];
            switch (h.Mode)
            {
                case HunterMode.Penned: case HunterMode.Recovering: return 0f;
                case HunterMode.Leaving: return 3.2f;
                case HunterMode.Returning: return 11f;
                case HunterMode.Fleeing: return Def.HunterSpeed[i] * 0.6f;
            }
            float s = Def.HunterSpeed[i];
            if (h.Kind == HunterKind.Listener && h.Investigating) s *= 1.2f;
            // the machine gets impatient when the maze is nearly clear
            if (h.Kind == HunterKind.Stalker && Maze.NodesLeft < Maze.NodesTotal / 5) s *= 1.06f;
            return s;
        }

        void UpdateHunter(int i, float dt)
        {
            var h = Hunters[i];
            h.ModeTime += dt;
            h.NoiseLeft = Math.Max(0f, h.NoiseLeft - dt);
            h.PatrolLeft = Math.Max(0f, h.PatrolLeft - dt);
            h.WanderLeft = Math.Max(0f, h.WanderLeft - dt);

            switch (h.Mode)
            {
                case HunterMode.Penned:
                    if (Clock - playSince >= h.ReleaseAt) { h.SetMode(HunterMode.Leaving); Emit(SimEventKind.HunterReleased, h.Mover.Tile, 0, i); }
                    return;
                case HunterMode.Recovering:
                    if (h.ModeTime >= 2.5f) h.SetMode(HunterMode.Leaving);
                    return;
            }

            bool pen = h.Mode == HunterMode.Leaving || h.Mode == HunterMode.Returning;
            Func<Tile, bool> canEnter = t => Maze.HunterCanEnter(t, pen);
            h.Mover.Step(dt, HunterSpeed(i), canEnter, Now, m => Decide(i, m.Tile, canEnter));

            var tile = h.Mover.Tile;
            if (h.Mode == HunterMode.Leaving && tile == GateOut && h.Mover.AtCentre)
                h.SetMode(SurgeActive && !h.EatenThisSurge ? HunterMode.Fleeing : HunterMode.Hunting);
            else if (h.Mode == HunterMode.Returning && tile == h.PenSpot && h.Mover.AtCentre)
            {
                h.SetMode(HunterMode.Recovering);
                Emit(SimEventKind.HunterHome, tile, 0, i);
            }
            if (h.Kind == HunterKind.Listener && h.Investigating && tile == h.Noise) h.NoiseLeft = 0f;
            if (h.Kind == HunterKind.Warden && h.Mode == HunterMode.Hunting && h.PatrolLeft <= 0f && tile == h.Target) h.PatrolLeft = 5f;
            if (h.Kind == HunterKind.Listener && (h.WanderLeft <= 0f || tile == h.Wander)) PickWander(h);
        }

        void PickWander(Hunter h)
        {
            h.WanderLeft = 7f;
            for (int tries = 0; tries < 40; tries++)
            {
                var t = new Tile(rng.Next(Maze.Width), rng.Next(Maze.Height));
                if (Maze.HunterCanEnter(t, false) && t.Manhattan(h.Mover.Tile) > 6) { h.Wander = t; return; }
            }
            h.Wander = Player.Tile;
        }

        /// <summary>Where a hunter wants to go right now (also used by tests and the debug view).</summary>
        public Tile TargetFor(int i)
        {
            var h = Hunters[i];
            switch (h.Mode)
            {
                case HunterMode.Leaving: return GateOut;
                case HunterMode.Returning: return h.PenSpot;
                case HunterMode.Fleeing: return Player.Tile;  // used as the thing to run from
            }
            if (h.Kind != HunterKind.Listener && h.Investigating) return h.Noise;
            var player = Player.Tile;
            switch (h.Kind)
            {
                case HunterKind.Stalker: return HunterBrain.StalkerTarget(player);
                case HunterKind.Ambusher: return HunterBrain.AmbusherTarget(Maze, player, Player.Dir != Dir.None ? Player.Dir : Player.Facing);
                case HunterKind.Warden:
                {
                    var fromPlayer = Maze.Distances(player, t => Maze.HunterCanEnter(t, false) || Maze.PlayerCanEnter(t), scratchB);
                    return HunterBrain.WardenTarget(Maze, h, player, fromPlayer, Maze.Dist(fromPlayer, h.Mover.Tile));
                }
                default:
                {
                    if (h.Wander == default(Tile) && h.WanderLeft <= 0f) PickWander(h);
                    int d = Math.Abs(h.Mover.Tile.C - player.C) + Math.Abs(h.Mover.Tile.R - player.R) <= HunterBrain.ListenerHears * 2
                        ? Maze.Dist(Maze.Distances(player, Maze.PlayerCanEnter, scratchB), h.Mover.Tile) : -1;
                    return HunterBrain.ListenerTarget(h, player, d);
                }
            }
        }

        Dir Decide(int i, Tile at, Func<Tile, bool> canEnter)
        {
            var h = Hunters[i];
            h.Target = TargetFor(i);
            if ((h.Mode == HunterMode.Returning || h.Mode == HunterMode.Leaving) && at == h.Target) return Dir.None;  // arrived
            bool flee = h.Mode == HunterMode.Fleeing;
            var field = Maze.Distances(h.Target, canEnter, scratchA);
            var current = h.Mover.Dir != Dir.None ? h.Mover.Dir : Dir.None;
            // returning / leaving hunters may turn back freely; so may anyone who was standing still
            if (h.Mode == HunterMode.Returning || h.Mode == HunterMode.Leaving) current = Dir.None;
            return HunterBrain.Choose(Maze, at, current, field, canEnter, flee, rng);
        }

        // ================================================================== contacts

        void CheckContacts()
        {
            for (int i = 0; i < Hunters.Length; i++)
            {
                var h = Hunters[i];
                if (Player.DistanceTo(h.Mover) > CatchRadius) continue;
                if (h.Mode == HunterMode.Fleeing)
                {
                    int pts = Points.Hunter(combo);
                    Stats.Combo[Math.Min(3, combo)]++;
                    Emit(SimEventKind.HunterCaught, h.Mover.Tile, pts, i);
                    combo++;
                    h.EatenThisSurge = true;
                    h.NoiseLeft = 0f;
                    h.SetMode(HunterMode.Returning);
                }
                else if (h.Mode == HunterMode.Hunting || (h.Mode == HunterMode.Leaving && !h.OnPenSide(Maze)))
                {
                    Die(i);
                    return;
                }
            }
        }

        void Die(int hunter)
        {
            Run.Lives--;
            Stats.Deaths++;
            State = SimState.Dying; StateTime = 0f;
            Emit(SimEventKind.PlayerCaught, Player.Tile, Run.Lives, hunter);
            PendingShift = null;
        }

        void CheckExtraLife()
        {
            if (!Run.ExtraLifeGiven && Run.Total >= Points.ExtraLifeAt)
            {
                Run.ExtraLifeGiven = true;
                Run.Lives++;
                Emit(SimEventKind.ExtraLife, Player.Tile, Run.Lives);
            }
        }

        // ================================================================== risk rooms

        void UpdateRiskRooms(float dt)
        {
            var tile = Player.Tile;
            for (int k = 0; k < Maze.RiskRooms.Count; k++)
            {
                var room = Maze.RiskRooms[k];
                if (room.Sealed) continue;
                bool inside = room.Cells.Contains(tile);
                if (!room.Entered)
                {
                    if (inside) { room.Entered = true; room.TimeLeft = Def.RiskSeconds; Emit(SimEventKind.RiskEntered, room.Door, (int)Def.RiskSeconds, k); }
                    continue;
                }
                float before = room.TimeLeft;
                room.TimeLeft = Math.Max(0f, room.TimeLeft - dt);
                if (before > 3f && room.TimeLeft <= 3f) Emit(SimEventKind.RiskWarning, room.Door, 3, k);
                if (room.TimeLeft > 0f) continue;
                // never close the door on top of anyone: wait until the doorway itself is clear
                bool onDoor = tile == room.Door || Player.NextTile == room.Door;
                if (onDoor) continue;
                Maze.SealRoom(room);
                Emit(SimEventKind.RiskSealed, room.Door, k, -1, inside);
                if (inside) { Die(-1); return; }
            }
        }

        // ================================================================== living walls

        void UpdateShifts(float dt)
        {
            if (Maze.Groups.Count == 0) return;
            if (PendingShift == null)
            {
                ShiftTimer -= dt;
                if (ShiftTimer > 0f) return;
                PendingShift = Maze.Groups[shiftCursor % Maze.Groups.Count];
                shiftCursor++;
                ShiftTelegraphLeft = ShiftTelegraph;
                shiftRetries = 0;
                Emit(SimEventKind.ShiftWarning, default, Maze.Groups.IndexOf(PendingShift));
                return;
            }
            ShiftTelegraphLeft -= dt;
            if (ShiftTelegraphLeft > 0f) return;
            int gi = Maze.Groups.IndexOf(PendingShift);
            if (Maze.CanToggle(PendingShift, Player.Tile, Occupied(), out _))
            {
                Maze.Toggle(PendingShift);
                Emit(SimEventKind.Shift, default, gi);
                PendingShift = null;
                ShiftTimer = Def.ShiftEvery;
            }
            else if (++shiftRetries > 6)
            {
                Emit(SimEventKind.ShiftCancelled, default, gi);
                PendingShift = null;
                ShiftTimer = Def.ShiftEvery * 0.5f;
            }
            else ShiftTelegraphLeft = 0.5f;   // someone is in the way: try again shortly
        }

        /// <summary>Every tile an actor stands on or is moving into.</summary>
        public IEnumerable<Tile> Occupied()
        {
            yield return Player.Tile;
            yield return Player.NextTile;
            foreach (var h in Hunters) { yield return h.Mover.Tile; yield return h.Mover.NextTile; }
        }
    }
}
