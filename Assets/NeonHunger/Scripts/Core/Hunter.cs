using System;
using System.Collections.Generic;

namespace NeonHunger.Core
{
    public enum HunterKind { Stalker, Ambusher, Warden, Listener }
    public enum HunterMode { Penned, Leaving, Hunting, Fleeing, Returning, Recovering }

    public sealed class Hunter
    {
        public readonly HunterKind Kind;
        public readonly Mover Mover = new Mover();
        public HunterMode Mode = HunterMode.Penned;
        public float ModeTime;
        public float ReleaseAt;
        public Tile PenSpot;
        public Tile Target;
        public bool EatenThisSurge;
        // Listener / noise
        public Tile Noise;
        public float NoiseLeft;
        public bool Investigating => NoiseLeft > 0f;
        // Warden / Listener wandering
        public float PatrolLeft;
        public Tile Wander;
        public float WanderLeft;

        public Hunter(HunterKind kind) { Kind = kind; }

        public bool Edible => Mode == HunterMode.Fleeing;
        public bool OnPenSide(Maze m) { var g = m.GroundAt(Mover.Tile); return g == Ground.Pen || g == Ground.Gate; }

        public void SetMode(HunterMode m) { Mode = m; ModeTime = 0f; }
    }

    /// <summary>
    /// Hunter decision-making, separate from movement and rendering. Each kind has its own idea of
    /// where to go; all of them then path there through the current maze (breadth-first distances
    /// over tiles they're allowed to use), so they always respect walls, sealed doors and moved walls.
    /// </summary>
    public static class HunterBrain
    {
        /// <summary>Path distance within which the Warden gives chase and the Listener hears footsteps.</summary>
        public const int WardenChase = 6, ListenerHears = 4;

        /// <summary>Stalker: straight for the player.</summary>
        public static Tile StalkerTarget(Tile player) => player;

        /// <summary>Ambusher: the next junction ahead of the player (up to 6 tiles), to cut them off.</summary>
        public static Tile AmbusherTarget(Maze m, Tile player, Dir facing)
        {
            if (facing == Dir.None) return player;
            var t = player;
            for (int i = 0; i < 6; i++)
            {
                var n = t.Step(facing);
                if (!m.PlayerCanEnter(n)) break;
                t = n;
                if (m.IsJunction(t)) return t;
            }
            return t;
        }

        /// <summary>
        /// Warden: guards whatever valuable spot (power core, gem, risk door, exit) the player is
        /// closest to, and only gives chase when the player comes within a short path distance.
        /// </summary>
        public static Tile WardenTarget(Maze m, Hunter w, Tile player, int[] fromPlayer, int pathToPlayer)
        {
            if (pathToPlayer >= 0 && pathToPlayer <= WardenChase) return player;
            var guards = GuardPoints(m);
            if (guards.Count == 0) return player;
            guards.Sort((a, b) => Rank(m, fromPlayer, a).CompareTo(Rank(m, fromPlayer, b)));
            // patrol: after reaching the nearest guard point, spend a while on the second one
            if (w.PatrolLeft > 0f && guards.Count > 1) return guards[1];
            return guards[0];
        }

        static int Rank(Maze m, int[] field, Tile t) { int d = m.Dist(field, t); return d < 0 ? int.MaxValue : d; }

        public static List<Tile> GuardPoints(Maze m)
        {
            var list = new List<Tile>();
            for (int r = 0; r < m.Height; r++)
                for (int c = 0; c < m.Width; c++)
                {
                    var t = new Tile(c, r);
                    var it = m.ItemAt(t);
                    if (it == Item.Power || it == Item.Gem) list.Add(NearestHunterTile(m, t));
                }
            foreach (var room in m.RiskRooms) if (!room.Sealed) list.Add(NearestHunterTile(m, room.Door));
            list.Add(NearestHunterTile(m, m.Exit));
            return list;
        }

        /// <summary>The closest tile to `t` that a hunter can stand on (doors and the exit are off limits).</summary>
        public static Tile NearestHunterTile(Maze m, Tile t)
        {
            if (m.HunterCanEnter(t, false)) return t;
            foreach (var d in Dirs.All) { var n = t.Step(d); if (m.HunterCanEnter(n, false)) return n; }
            foreach (var d in Dirs.All) foreach (var e in Dirs.All) { var n = t.Step(d).Step(e); if (m.HunterCanEnter(n, false)) return n; }
            return t;
        }

        /// <summary>Listener: goes to the last noise it heard; otherwise follows footsteps nearby, else wanders.</summary>
        public static Tile ListenerTarget(Hunter l, Tile player, int pathToPlayer)
        {
            if (l.Investigating) return l.Noise;
            if (pathToPlayer >= 0 && pathToPlayer <= ListenerHears) return player;
            return l.Wander;
        }

        /// <summary>
        /// Pick a direction at a tile centre. Hunters don't turn back unless it's the only way (or their
        /// mode just changed). Ties are broken in a fixed order so behaviour is repeatable.
        /// `flee` picks the way that gets furthest from the target instead.
        /// </summary>
        public static Dir Choose(Maze m, Tile at, Dir current, int[] field, Func<Tile, bool> canEnter, bool flee = false, Random rng = null)
        {
            Dir best = Dir.None;
            int bestScore = flee ? int.MinValue : int.MaxValue;
            int options = 0;
            foreach (var d in Dirs.All)
            {
                if (current != Dir.None && d == current.Opposite()) continue;
                var n = at.Step(d);
                if (!canEnter(n)) continue;
                options++;
                int dist = m.Dist(field, n);
                if (dist < 0) dist = flee ? -1 : 10000;
                int score = flee ? dist * 4 + (rng != null ? rng.Next(3) : 0) : dist;
                if (flee ? score > bestScore : score < bestScore) { bestScore = score; best = d; }
            }
            if (options == 0)
            {
                var back = current.Opposite();
                if (back != Dir.None && canEnter(at.Step(back))) return back;
                foreach (var d in Dirs.All) if (canEnter(at.Step(d))) return d;
                return Dir.None;
            }
            return best;
        }
    }
}
