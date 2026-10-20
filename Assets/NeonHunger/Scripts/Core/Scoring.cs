using System;
using System.Collections.Generic;
using System.Text;

namespace NeonHunger.Core
{
    /// <summary>
    /// Points, in one place. The public scoreboard (server/neon-scores.php) recomputes a run's score
    /// from the per-level counts below with exactly these numbers, so keep the two in step.
    /// </summary>
    public static class Points
    {
        public const int Node = 10, Power = 50, Gem = 300, RiskGem = 750;
        /// <summary>Hunters caught during one power surge: 200, 400, 800, 1600.</summary>
        public static int Hunter(int comboIndex) => 200 << Math.Min(3, Math.Max(0, comboIndex));
        public static int LevelClear(int level) => 1000 * level;
        public static int TimeBonus(float seconds, float par) => Math.Max(0, (int)Math.Floor(par - seconds)) * 10;
        public const int ExtraLifeAt = 10000;
    }

    /// <summary>What happened on one level (sent to the scoreboard).</summary>
    public sealed class LevelStats
    {
        public int Level, Nodes, Powers, Gems, RiskGems, Deaths;
        /// <summary>Hunters caught by combo position: [0] = first in a surge, [3] = fourth.</summary>
        public readonly int[] Combo = new int[4];
        public bool Cleared;
        public float Seconds;

        public int Score(float par)
        {
            int s = Nodes * Points.Node + Powers * Points.Power + Gems * Points.Gem + RiskGems * Points.RiskGem;
            for (int i = 0; i < 4; i++) s += Combo[i] * Points.Hunter(i);
            if (Cleared) s += Points.LevelClear(Level) + Points.TimeBonus(Seconds, par);
            return s;
        }

        /// <summary>Compact form for the scoreboard: level:nodes,powers,gems,risk,c1,c2,c3,c4,cleared,seconds</summary>
        public string Encode() =>
            $"{Level}:{Nodes},{Powers},{Gems},{RiskGems},{Combo[0]},{Combo[1]},{Combo[2]},{Combo[3]},{(Cleared ? 1 : 0)},{Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>A whole run across levels.</summary>
    public sealed class RunScore
    {
        public readonly List<LevelStats> Levels = new List<LevelStats>();
        public int Lives = 3;
        public bool ExtraLifeGiven;

        public int Total
        {
            get
            {
                int s = 0;
                foreach (var l in Levels) s += l.Score(LevelDef.All[l.Level - 1].ParSeconds);
                return s;
            }
        }

        public string Encode()
        {
            var sb = new StringBuilder();
            foreach (var l in Levels) { if (sb.Length > 0) sb.Append(';'); sb.Append(l.Encode()); }
            return sb.ToString();
        }
    }
}
