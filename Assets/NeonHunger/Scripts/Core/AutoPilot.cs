using System;
using System.Collections.Generic;

namespace NeonHunger.Core
{
    /// <summary>
    /// A simple bot for automated testing: heads for the nearest energy along a path that avoids
    /// tiles near dangerous hunters, chases hunters during a surge, takes the exit when it opens,
    /// dashes away when cornered, and (optionally) raids risk rooms while watching the door timer.
    /// </summary>
    public sealed class AutoPilot
    {
        public bool TakeRisks;
        public Tile Goal { get; private set; }
        readonly Random rng;
        int[] dist;
        Tile[] prev;

        public AutoPilot(int seed = 3) { rng = new Random(seed); }

        public PlayerInput Think(GameSim sim)
        {
            var m = sim.Maze;
            var me = sim.Player.Tile;
            int n = m.Width * m.Height;
            if (dist == null || dist.Length != n) { dist = new int[n]; prev = new Tile[n]; }

            // tiles to avoid: around hunters that can hurt us (not when they're fleeing)
            var danger = new HashSet<Tile>();
            int closest = int.MaxValue;
            foreach (var h in sim.Hunters)
            {
                bool hurts = h.Mode == HunterMode.Hunting || (h.Mode == HunterMode.Leaving && !h.OnPenSide(m));
                if (!hurts) continue;
                var ht = h.Mover.Tile;
                closest = Math.Min(closest, ht.Manhattan(me));
                danger.Add(ht); danger.Add(h.Mover.NextTile);
                foreach (var d in Dirs.All) { danger.Add(ht.Step(d)); danger.Add(ht.Step(d).Step(d)); }
            }
            danger.Remove(me);

            RiskRoom openRoom = null;
            foreach (var room in m.RiskRooms) if (room.Entered && !room.Sealed && room.Cells.Contains(me)) openRoom = room;

            Func<Tile, bool> ok = t => m.PlayerCanEnter(t) && !danger.Contains(t) && (TakeRisks || m.RoomOf(t) == null || m.RoomOf(me) != null);
            Search(m, me, ok);

            Tile? goal = null;
            if (openRoom != null && openRoom.TimeLeft < 3.5f) goal = Outside(m, openRoom);
            if (goal == null && sim.SurgeActive && sim.SurgeLeft > 1.5f)
                goal = Nearest(m, t => { foreach (var h in sim.Hunters) if (h.Mode == HunterMode.Fleeing && h.Mover.Tile == t) return true; return false; }, 10);
            if (goal == null && m.ExitOpen) goal = Reach(m, m.Exit) ? m.Exit : (Tile?)null;
            if (goal == null)
                goal = Nearest(m, t =>
                {
                    var it = m.ItemAt(t);
                    if (it == Item.None) return false;
                    if (it == Item.RiskGem) { var room = m.RoomOf(t); return TakeRisks && room != null && (!room.Entered || room.TimeLeft > 5f); }
                    return true;
                }, int.MaxValue);
            if (goal == null)
            {
                // boxed in by danger: go wherever is furthest from the nearest hunter
                Search(m, me, m.PlayerCanEnter);
                goal = Nearest(m, t => !danger.Contains(t) && t.Manhattan(me) > 3, int.MaxValue);
            }

            var input = new PlayerInput();
            if (goal.HasValue)
            {
                Goal = goal.Value;
                input.Want = FirstStep(m, me, goal.Value);
            }
            if (input.Want == Dir.None)
            {
                // nowhere safe to go: keep moving away from trouble
                foreach (var d in Dirs.All) if (m.PlayerCanEnter(me.Step(d)) && !danger.Contains(me.Step(d))) { input.Want = d; break; }
            }
            input.Dash = closest <= 2 && !sim.SurgeActive && sim.DashCooldownLeft <= 0f;
            return input;
        }

        bool Reach(Maze m, Tile t) => m.Dist(dist, t) >= 0;

        Tile? Outside(Maze m, RiskRoom room)
        {
            foreach (var d in Dirs.All) { var n = room.Door.Step(d); if (m.RoomOf(n) == null && m.PlayerCanEnter(n)) return n; }
            return null;
        }

        void Search(Maze m, Tile from, Func<Tile, bool> ok)
        {
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var q = new Queue<Tile>();
            dist[from.R * m.Width + from.C] = 0;
            q.Enqueue(from);
            // shuffle neighbour order a little so the bot doesn't always sweep the same way
            var order = (Dir[])Dirs.All.Clone();
            for (int i = order.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }
            while (q.Count > 0)
            {
                var t = q.Dequeue();
                foreach (var d in order)
                {
                    var nb = t.Step(d);
                    if (!m.InBounds(nb) || dist[nb.R * m.Width + nb.C] >= 0 || !ok(nb)) continue;
                    dist[nb.R * m.Width + nb.C] = dist[t.R * m.Width + t.C] + 1;
                    prev[nb.R * m.Width + nb.C] = t;
                    q.Enqueue(nb);
                }
            }
        }

        Tile? Nearest(Maze m, Func<Tile, bool> want, int maxDist)
        {
            Tile? best = null; int bd = int.MaxValue;
            for (int r = 0; r < m.Height; r++)
                for (int c = 0; c < m.Width; c++)
                {
                    int d = dist[r * m.Width + c];
                    if (d <= 0 || d >= bd || d > maxDist) continue;
                    var t = new Tile(c, r);
                    if (want(t)) { bd = d; best = t; }
                }
            return best;
        }

        Dir FirstStep(Maze m, Tile from, Tile to)
        {
            if (m.Dist(dist, to) < 0) return Dir.None;
            var t = to;
            for (int guard = 0; guard < 2000 && t != from; guard++)
            {
                var p = prev[t.R * m.Width + t.C];
                if (p == from)
                {
                    foreach (var d in Dirs.All) if (from.Step(d) == t) return d;
                }
                t = p;
            }
            return Dir.None;
        }
    }
}
