namespace NeonHunger.Core
{
    /// <summary>One level: its map and how hard it pushes.</summary>
    public sealed class LevelDef
    {
        public int Number;
        public string Name, Subtitle;
        public string[] Map;
        public float PlayerSpeed = 5.4f;
        /// <summary>Hunting speeds for Stalker, Ambusher, Warden, Listener (tiles/second).</summary>
        public float[] HunterSpeed = { 4.6f, 4.5f, 4.2f, 4.3f };
        /// <summary>Seconds after the start (or a respawn) before each hunter leaves the pen.</summary>
        public float[] Release = { 1.5f, 5f, 9f, 13f };
        public float SurgeSeconds = 8f;
        public float FirstShift = 25f, ShiftEvery = 30f;
        public float RiskSeconds = 10f;
        public float ParSeconds = 150f;

        public static readonly LevelDef[] All =
        {
            new LevelDef
            {
                Number = 1, Name = "INTAKE", Subtitle = "The machine is hungry too.",
                PlayerSpeed = 5.4f, HunterSpeed = new[] { 4.4f, 4.3f, 4.0f, 4.1f }, Release = new[] { 2f, 7f, 12f, 17f },
                SurgeSeconds = 9f, FirstShift = 35f, ShiftEvery = 35f, RiskSeconds = 10f, ParSeconds = 140f,
                Map = new[]
                {
                    "##########E##########",
                    "#........#.#........#",
                    "#.##.###.#.#.###.##.#",
                    "#o##.###.....###.##o#",
                    "#...................#",
                    "#.##.#.#######.#.##.#",
                    "#....#....#....#....#",
                    "####.####.#.####.####",
                    "####.#    -    #.####",
                    "#RD..# ##HHH## #.*..#",
                    "#rR#.# #HHHHH# #.##.#",
                    "####.# ####### #....#",
                    "####.#    S    #.####",
                    "#........#.#........#",
                    "#.##.###.#.#.###.##.#",
                    "#o.#.....A.A.....#.o#",
                    "##.#.#.#######.#.#.##",
                    "#....#....#....#....#",
                    "#.#######.#.#######.#",
                    "#...................#",
                    "#####################",
                },
            },
            new LevelDef
            {
                Number = 2, Name = "FURNACE", Subtitle = "The walls have started to move.",
                PlayerSpeed = 5.5f, HunterSpeed = new[] { 4.7f, 4.6f, 4.3f, 4.4f }, Release = new[] { 1.5f, 5f, 9f, 13f },
                SurgeSeconds = 7.5f, FirstShift = 18f, ShiftEvery = 22f, RiskSeconds = 8f, ParSeconds = 170f,
                Map = new[]
                {
                    "###########E###########",
                    "#*........#.#........*#",
                    "#.###.###.#.#.###.###.#",
                    "#o###.###.....###.###o#",
                    "#.....#....A....#.....#",
                    "#.###.#.#######.#.###.#",
                    "#...#.#....#....#.#...#",
                    "###.#.####.#.####.#.###",
                    "#RD.#.#    -    #.#.DR#",
                    "#rR#..# ##HHH## #..#Rr#",
                    "###..#. #HHHHH# .#..###",
                    "#....#. ####### .#....#",
                    "#.##.B.    S    .C.##.#",
                    "#.##.#.#########.#.##.#",
                    "#.......b.....c.......#",
                    "#.###.###.###.###.###.#",
                    "#o..#.....#.#.....#..o#",
                    "###.#.###.#.#.###.#.###",
                    "#.....#...a.a...#.....#",
                    "#.#####.###.###.#####.#",
                    "#.....................#",
                    "#######################",
                },
            },
            new LevelDef
            {
                Number = 3, Name = "CORE", Subtitle = "It knows you're here.",
                PlayerSpeed = 5.6f, HunterSpeed = new[] { 5.0f, 4.9f, 4.6f, 4.7f }, Release = new[] { 1f, 3.5f, 6.5f, 9.5f },
                SurgeSeconds = 6f, FirstShift = 14f, ShiftEvery = 16f, RiskSeconds = 7f, ParSeconds = 200f,
                Map = new[]
                {
                    "############E############",
                    "#*.......#.....#.......*#",
                    "#.##.###.#.###.#.###.##.#",
                    "#o##.#...C.....c...#.##o#",
                    "#....#.###.#.#.###.#....#",
                    "####.#.#...#.#...#.#.####",
                    "#RD..#.#.#######.#.#..DR#",
                    "#rR#...A....#....a...#Rr#",
                    "####.###.#..-..#.###.####",
                    "#....#...#HHHHH#...#....#",
                    "#.##.#.###HHHHH###.#.##.#",
                    "#.##.#...#######...#.##.#",
                    "#....B......S......b....#",
                    "###.#.#####.#.#####.#.###",
                    "#.....#.....#.....#.....#",
                    "#.###.#.###.#.###.#.###.#",
                    "#o..#...#.......#...#..o#",
                    "###.#.#.#.#.#.#.#.#.#.###",
                    "#.....#...#...#...#.....#",
                    "#.#####.#####.#####.###.#",
                    "#.......*.........*.....#",
                    "#########################",
                },
            },
        };
    }
}
