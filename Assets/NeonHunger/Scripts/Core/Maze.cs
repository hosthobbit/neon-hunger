using System;
using System.Collections.Generic;

namespace NeonHunger.Core
{
    public enum Dir : byte { None, Up, Down, Left, Right }

    public static class Dirs
    {
        public static readonly Dir[] All = { Dir.Up, Dir.Left, Dir.Down, Dir.Right };
        public static int DC(this Dir d) => d == Dir.Left ? -1 : d == Dir.Right ? 1 : 0;
        /// <summary>Row step: rows count downwards, as in the map text.</summary>
        public static int DR(this Dir d) => d == Dir.Up ? -1 : d == Dir.Down ? 1 : 0;
        public static Dir Opposite(this Dir d) => d == Dir.Up ? Dir.Down : d == Dir.Down ? Dir.Up : d == Dir.Left ? Dir.Right : d == Dir.Right ? Dir.Left : Dir.None;
        public static bool Horizontal(this Dir d) => d == Dir.Left || d == Dir.Right;
    }

    public readonly struct Tile : IEquatable<Tile>
    {
        public readonly int C, R;
        public Tile(int c, int r) { C = c; R = r; }
        public Tile Step(Dir d) => new Tile(C + d.DC(), R + d.DR());
        public int Manhattan(Tile o) => Math.Abs(C - o.C) + Math.Abs(R - o.R);
        public bool Equals(Tile o) => C == o.C && R == o.R;
        public override bool Equals(object obj) => obj is Tile t && Equals(t);
        public override int GetHashCode() => C * 397 + R;
        public static bool operator ==(Tile a, Tile b) => a.Equals(b);
        public static bool operator !=(Tile a, Tile b) => !a.Equals(b);
        public override string ToString() => $"({C},{R})";
    }

    public enum Ground : byte { Wall, Floor, Pen, Gate, RiskFloor, RiskDoor, Exit }
    public enum Item : byte { None, Node, Power, Gem, RiskGem }

    /// <summary>A set of cells that swap between wall and floor together (a "living" wall).</summary>
    public sealed class ShiftGroup
    {
        public char Id;
        /// <summary>Cells that start as walls (open when toggled) and cells that start open (close when toggled).</summary>
        public readonly List<Tile> StartWalls = new List<Tile>(), StartOpen = new List<Tile>();
        public bool Toggled;
        public IEnumerable<Tile> Cells { get { foreach (var t in StartWalls) yield return t; foreach (var t in StartOpen) yield return t; } }
        public bool IsWall(Tile t) => StartWalls.Contains(t) ? !Toggled : StartOpen.Contains(t) && Toggled;
    }

    /// <summary>An optional bonus chamber behind one door that seals on a timer.</summary>
    public sealed class RiskRoom
    {
        public Tile Door;
        public readonly List<Tile> Cells = new List<Tile>();
        public bool Entered, Sealed;
        public float TimeLeft;
    }

    /// <summary>
    /// The maze: ground types, collectibles, the living walls and risk rooms. Connectivity questions
    /// (who can walk where, what's reachable) all go through here, so the player, the hunters and the
    /// safety checks always agree on the same, current layout.
    /// </summary>
    public sealed class Maze
    {
        public readonly int Width, Height;
        readonly Ground[] ground;
        readonly Item[] items;
        readonly sbyte[] groupOf;   // index into Groups, -1 if none
        public readonly List<ShiftGroup> Groups = new List<ShiftGroup>();
        public readonly List<RiskRoom> RiskRooms = new List<RiskRoom>();
        public readonly List<Tile> PenTiles = new List<Tile>();
        public Tile Start, Gate, Exit, PenCentre;
        public bool ExitOpen;
        /// <summary>Changes whenever walls move or a door seals (hunters re-plan).</summary>
        public int Version { get; private set; }
        public int NodesLeft { get; private set; }
        public int NodesTotal { get; private set; }
        public readonly Dictionary<Item, int> ItemTotals = new Dictionary<Item, int>();

        Maze(int w, int h)
        {
            Width = w; Height = h;
            ground = new Ground[w * h];
            items = new Item[w * h];
            groupOf = new sbyte[w * h];
            for (int i = 0; i < groupOf.Length; i++) groupOf[i] = -1;
        }

        /// <summary>
        /// Map legend: # wall, . node, (space) floor, o power, * gem, S start, H pen, - pen gate, E exit,
        /// R risk floor, r risk gem, D risk door, A-C living wall (starts solid), a-c living wall (starts open).
        /// </summary>
        public static Maze Parse(string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            var m = new Maze(w, h);
            var groups = new Dictionary<char, ShiftGroup>();
            var riskCells = new List<Tile>();
            var doors = new List<Tile>();
            for (int r = 0; r < h; r++)
            {
                if (rows[r].Length != w) throw new FormatException($"Row {r} is {rows[r].Length} wide, expected {w}");
                for (int c = 0; c < w; c++)
                {
                    char ch = rows[r][c];
                    var t = new Tile(c, r);
                    int i = r * w + c;
                    m.ground[i] = Ground.Floor;
                    switch (ch)
                    {
                        case '#': m.ground[i] = Ground.Wall; break;
                        case '.': m.items[i] = Item.Node; break;
                        case 'o': m.items[i] = Item.Power; break;
                        case '*': m.items[i] = Item.Gem; break;
                        case ' ': break;
                        case 'S': m.Start = t; break;
                        case 'H': m.ground[i] = Ground.Pen; m.PenTiles.Add(t); break;
                        case '-': m.ground[i] = Ground.Gate; m.Gate = t; break;
                        case 'E': m.ground[i] = Ground.Exit; m.Exit = t; break;
                        case 'R': m.ground[i] = Ground.RiskFloor; riskCells.Add(t); break;
                        case 'r': m.ground[i] = Ground.RiskFloor; m.items[i] = Item.RiskGem; riskCells.Add(t); break;
                        case 'D': m.ground[i] = Ground.RiskDoor; doors.Add(t); break;
                        default:
                            if (char.IsLetter(ch))
                            {
                                char id = char.ToUpperInvariant(ch);
                                if (!groups.TryGetValue(id, out var g)) { g = new ShiftGroup { Id = id }; groups[id] = g; m.Groups.Add(g); }
                                (char.IsUpper(ch) ? g.StartWalls : g.StartOpen).Add(t);
                                m.groupOf[i] = (sbyte)m.Groups.IndexOf(g);
                                break;
                            }
                            throw new FormatException($"Unknown map character '{ch}' at {t}");
                    }
                    if (m.items[i] != Item.None) m.ItemTotals[m.items[i]] = m.ItemTotals.TryGetValue(m.items[i], out int n) ? n + 1 : 1;
                }
            }
            // each door owns the risk cells connected to it
            foreach (var d in doors)
            {
                var room = new RiskRoom { Door = d };
                var stack = new Stack<Tile>();
                foreach (var dir in Dirs.All) { var n = d.Step(dir); if (riskCells.Contains(n)) stack.Push(n); }
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    if (room.Cells.Contains(t)) continue;
                    room.Cells.Add(t);
                    foreach (var dir in Dirs.All) { var n = t.Step(dir); if (riskCells.Contains(n) && !room.Cells.Contains(n)) stack.Push(n); }
                }
                m.RiskRooms.Add(room);
            }
            int sc = 0, sr = 0;
            foreach (var p in m.PenTiles) { sc += p.C; sr += p.R; }
            if (m.PenTiles.Count > 0) m.PenCentre = new Tile((int)Math.Round((double)sc / m.PenTiles.Count), (int)Math.Round((double)sr / m.PenTiles.Count));
            m.NodesTotal = m.NodesLeft = m.ItemTotals.TryGetValue(Item.Node, out int nodes) ? nodes : 0;
            return m;
        }

        public bool InBounds(Tile t) => t.C >= 0 && t.C < Width && t.R >= 0 && t.R < Height;
        int Index(Tile t) => t.R * Width + t.C;
        public Ground GroundAt(Tile t) => InBounds(t) ? ground[Index(t)] : Ground.Wall;
        public Item ItemAt(Tile t) => InBounds(t) ? items[Index(t)] : Item.None;
        public ShiftGroup GroupAt(Tile t) => InBounds(t) && groupOf[Index(t)] >= 0 ? Groups[groupOf[Index(t)]] : null;

        public RiskRoom RoomOf(Tile t)
        {
            foreach (var room in RiskRooms) if (room.Cells.Contains(t)) return room;
            return null;
        }

        /// <summary>Solid right now: walls, closed living walls, sealed risk doors, and the exit until it opens.</summary>
        public bool IsSolid(Tile t)
        {
            if (!InBounds(t)) return true;
            var g = ground[Index(t)];
            if (g == Ground.Wall) return true;
            var group = GroupAt(t);
            if (group != null && group.IsWall(t)) return true;
            if (g == Ground.RiskDoor) foreach (var room in RiskRooms) if (room.Door == t && room.Sealed) return true;
            if (g == Ground.Exit && !ExitOpen) return true;
            return false;
        }

        public bool PlayerCanEnter(Tile t)
        {
            if (IsSolid(t)) return false;
            var g = GroundAt(t);
            return g != Ground.Pen && g != Ground.Gate;
        }

        /// <summary>Hunters stay out of risk rooms and the exit; only hunters heading home (or leaving) use the pen gate.</summary>
        public bool HunterCanEnter(Tile t, bool penAccess)
        {
            if (IsSolid(t)) return false;
            var g = GroundAt(t);
            if (g == Ground.RiskFloor || g == Ground.RiskDoor || g == Ground.Exit) return false;
            if ((g == Ground.Pen || g == Ground.Gate) && !penAccess) return false;
            return true;
        }

        public Item Take(Tile t)
        {
            if (!InBounds(t)) return Item.None;
            int i = Index(t);
            var it = items[i];
            if (it == Item.None) return it;
            items[i] = Item.None;
            if (it == Item.Node) { NodesLeft--; if (NodesLeft == 0) ExitOpen = true; }
            return it;
        }

        public void SealRoom(RiskRoom room) { room.Sealed = true; Version++; }

        /// <summary>Breadth-first distances (in steps) from `from` over cells `canEnter` allows; -1 = unreachable.</summary>
        public int[] Distances(Tile from, Func<Tile, bool> canEnter, int[] into = null)
        {
            var dist = into ?? new int[Width * Height];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            if (!InBounds(from)) return dist;
            var q = new Queue<Tile>();
            dist[Index(from)] = 0;
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var t = q.Dequeue();
                int d = dist[Index(t)];
                foreach (var dir in Dirs.All)
                {
                    var n = t.Step(dir);
                    if (!InBounds(n) || dist[Index(n)] >= 0 || !canEnter(n)) continue;
                    dist[Index(n)] = d + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        public int Dist(int[] field, Tile t) => InBounds(t) ? field[Index(t)] : -1;

        /// <summary>Tiles that still have to be reached: every remaining node, and the exit.</summary>
        public List<Tile> RequiredTiles()
        {
            var list = new List<Tile>();
            for (int r = 0; r < Height; r++)
                for (int c = 0; c < Width; c++)
                    if (items[r * Width + c] == Item.Node) list.Add(new Tile(c, r));
            list.Add(Exit);
            return list;
        }

        /// <summary>
        /// Would toggling this living wall be safe? Refused if any actor is on (or entering) one of its
        /// cells, if any remaining node or the exit would become unreachable from the player, or if the
        /// hunters' pen gate would be cut off from the player.
        /// </summary>
        public bool CanToggle(ShiftGroup g, Tile player, IEnumerable<Tile> occupied, out string reason)
        {
            reason = null;
            foreach (var t in occupied)
                foreach (var c in g.Cells)
                    if (c == t) { reason = $"actor at {t}"; return false; }
            g.Toggled = !g.Toggled;
            try
            {
                bool wasOpen = ExitOpen;
                ExitOpen = true; // the exit counts as reachable ground for this check
                var reach = Distances(player, PlayerCanEnter);
                ExitOpen = wasOpen;
                foreach (var t in RequiredTiles())
                    if (Dist(reach, t) < 0) { reason = $"would cut off {t}"; return false; }
                var hunterReach = Distances(Gate, t => HunterCanEnter(t, true));
                if (Dist(hunterReach, player) < 0 && GroundAt(player) != Ground.RiskFloor && GroundAt(player) != Ground.RiskDoor) { reason = "would cut the pen off"; return false; }
                return true;
            }
            finally { g.Toggled = !g.Toggled; }
        }

        public void Toggle(ShiftGroup g) { g.Toggled = !g.Toggled; Version++; }

        /// <summary>A junction: three or more open ways for the player.</summary>
        public bool IsJunction(Tile t)
        {
            int n = 0;
            foreach (var d in Dirs.All) if (PlayerCanEnter(t.Step(d))) n++;
            return n >= 3;
        }
    }
}
