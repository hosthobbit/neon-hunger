using System;
using System.Collections.Generic;
using System.Linq;
using NeonHunger.Core;
using NUnit.Framework;

namespace NeonHunger.Tests
{
    public class MazeTests
    {
        [Test] public void EveryLevelParsesWithOneStartExitGateAndPen()
        {
            foreach (var def in LevelDef.All)
            {
                var m = Maze.Parse(def.Map);
                Assert.Greater(m.NodesTotal, 100, def.Name);
                Assert.AreEqual(Ground.Exit, m.GroundAt(m.Exit), def.Name);
                Assert.AreEqual(Ground.Gate, m.GroundAt(m.Gate), def.Name);
                Assert.GreaterOrEqual(m.PenTiles.Count, 4, def.Name);
                Assert.IsFalse(m.ExitOpen, "the exit starts closed");
            }
        }

        /// <summary>The key safety property: in every possible living-wall configuration, every node and the exit can be reached.</summary>
        [Test] public void EverythingRequiredIsReachableInEveryWallConfiguration()
        {
            foreach (var def in LevelDef.All)
            {
                var m = Maze.Parse(def.Map);
                int n = m.Groups.Count;
                for (int mask = 0; mask < (1 << n); mask++)
                {
                    for (int g = 0; g < n; g++) m.Groups[g].Toggled = (mask & (1 << g)) != 0;
                    m.ExitOpen = true;
                    var reach = m.Distances(m.Start, m.PlayerCanEnter);
                    foreach (var t in m.RequiredTiles()) Assert.GreaterOrEqual(m.Dist(reach, t), 0, $"{def.Name} mask {mask}: {t} unreachable");
                    foreach (var room in m.RiskRooms) Assert.GreaterOrEqual(m.Dist(reach, room.Door), 0, $"{def.Name}: risk door unreachable");
                    var hunters = m.Distances(m.Gate, t => m.HunterCanEnter(t, true));
                    Assert.GreaterOrEqual(m.Dist(hunters, m.Start), 0, $"{def.Name} mask {mask}: pen cut off");
                }
            }
        }

        [Test] public void RiskRoomsAreSealedOffExceptForTheirDoorAndHoldNoRequiredNodes()
        {
            foreach (var def in LevelDef.All)
            {
                var m = Maze.Parse(def.Map);
                Assert.GreaterOrEqual(m.RiskRooms.Count, 1, def.Name);
                foreach (var room in m.RiskRooms)
                {
                    Assert.Greater(room.Cells.Count, 0);
                    foreach (var c in room.Cells)
                    {
                        Assert.AreNotEqual(Item.Node, m.ItemAt(c));
                        foreach (var d in Dirs.All)
                        {
                            var nb = c.Step(d);
                            Assert.IsTrue(room.Cells.Contains(nb) || nb == room.Door || m.GroundAt(nb) == Ground.Wall, $"{def.Name}: {c} leaks to {nb}");
                        }
                    }
                }
            }
        }

        static readonly string[] Small =
        {
            "#######",
            "#..A..#",
            "#.###.#",
            "#S.E..#",
            "###-###",
            "##HHH##",
            "#######",
        };

        [Test] public void PlayerAndHunterAccessRules()
        {
            var m = Maze.Parse(Small);
            Assert.IsFalse(m.PlayerCanEnter(m.Gate));
            Assert.IsFalse(m.PlayerCanEnter(m.PenTiles[0]));
            Assert.IsTrue(m.HunterCanEnter(m.Gate, true));
            Assert.IsFalse(m.HunterCanEnter(m.Gate, false));
            Assert.IsFalse(m.PlayerCanEnter(m.Exit), "closed exit is solid");
            Assert.IsFalse(m.HunterCanEnter(m.Exit, true), "hunters never use the exit");
        }

        [Test] public void CollectingTheLastNodeOpensTheExit()
        {
            var m = Maze.Parse(Small);
            foreach (var t in m.RequiredTiles().Where(t => m.ItemAt(t) == Item.Node).ToList()) m.Take(t);
            Assert.AreEqual(0, m.NodesLeft);
            Assert.IsTrue(m.ExitOpen);
            Assert.IsTrue(m.PlayerCanEnter(m.Exit));
        }

        [Test] public void LivingWallRefusesWhenOccupiedOrWhenItWouldCutSomethingOff()
        {
            var map = new[]
            {
                "#########",
                "#S.a....#",
                "#######.#",
                "#.....B.#",
                "##E##-###",
                "####HH###",
                "#########",
            };
            var m = Maze.Parse(map);
            var a = m.Groups.First(g => g.Id == 'A');
            var b = m.Groups.First(g => g.Id == 'B');
            var aCell = a.Cells.First();
            Assert.IsFalse(m.CanToggle(a, m.Start, new[] { aCell }, out string why), "occupied cell");
            StringAssert.Contains("actor", why);
            // closing 'a' would cut the start off from every node beyond it
            Assert.IsFalse(m.CanToggle(a, m.Start, new Tile[0], out why));
            StringAssert.Contains("cut off", why);
            int v = m.Version;
            Assert.IsTrue(m.CanToggle(b, m.Start, new Tile[0], out why), why);
            m.Toggle(b);
            Assert.AreEqual(v + 1, m.Version, "hunters re-plan after a change");
            Assert.IsTrue(m.PlayerCanEnter(b.StartWalls[0]));
        }
    }

    public class MoverTests
    {
        static readonly string[] Cross =
        {
            "#######",
            "###.###",
            "#.....#",
            "###.###",
            "#######",
        };

        static Func<Tile, bool> Open(Maze m) => m.PlayerCanEnter;

        [Test] public void MovesAtConstantSpeedAndStopsExactlyAtAWall()
        {
            var m = Maze.Parse(new[] { "#####", "#S..#", "#####" });
            var mv = new Mover();
            mv.Place(m.Start);
            mv.Want(Dir.Right, 0f);
            float moved = mv.Step(0.5f, 2f, Open(m), 0f);
            Assert.AreEqual(1f, moved, 1e-4);
            Assert.AreEqual(2f, mv.X, 1e-4);
            mv.Step(5f, 2f, Open(m), 0.5f);
            Assert.AreEqual(3f, mv.X, 1e-4, "stops at the last open tile centre");
            Assert.AreEqual(Dir.None, mv.Dir);
        }

        Maze CrossMaze()
        {
            var rows = new List<string>(Cross) { "###-###", "##HHH##", "###E###" };
            rows[2] = "#S....#";
            return Maze.Parse(rows.ToArray());
        }

        [Test] public void TurnPressedJustBeforeAJunctionIsTaken()
        {
            var m = CrossMaze();   // start at (1,2), junction at (3,2) with a branch up at (3,1)
            var mv = new Mover();
            mv.Place(m.Start);
            mv.Want(Dir.Right, 0f);
            float t = 0f;
            for (int i = 0; i < 15; i++) { mv.Step(0.1f, 1.5f, Open(m), t); t += 0.1f; }   // x = 3.25? stop before
            mv.Place(m.Start); mv.Want(Dir.Right, 0f); t = 0f;
            for (int i = 0; i < 12; i++) { mv.Step(0.1f, 1.5f, Open(m), t); t += 0.1f; }  // x = 2.8, 0.2 tiles before the junction
            Assert.AreEqual(2.8f, mv.X, 1e-3);
            mv.Want(Dir.Up, t);                                            // pressed early
            for (int i = 0; i < 4; i++) { mv.Step(0.05f, 1.5f, Open(m), t); t += 0.05f; }
            Assert.AreEqual(Dir.Up, mv.Dir, "buffered turn taken at the junction");
            Assert.AreEqual(3f, mv.X, 1e-4, "turned exactly on the centre-line");
            Assert.Less(mv.Y, 2f);
        }

        [Test] public void AStaleBufferedTurnIsForgotten()
        {
            var m = CrossMaze();
            var mv = new Mover();
            mv.Place(m.Start);
            mv.Want(Dir.Right, 0f);
            mv.Step(0.1f, 1.5f, Open(m), 0f);
            mv.Want(Dir.Up, 0.1f);                           // far too early: 1.7 tiles before the junction
            float t = 0.1f;
            for (int i = 0; i < 20; i++) { t += 0.1f; mv.Step(0.1f, 1.5f, Open(m), t); }
            Assert.AreEqual(2f, mv.Y, 1e-4, "kept going straight");
        }

        [Test] public void ReversingIsInstantMidTile()
        {
            var m = CrossMaze();
            var mv = new Mover();
            mv.Place(m.Start);
            mv.Want(Dir.Right, 0f);
            mv.Step(0.3f, 1f, Open(m), 0f);
            Assert.AreEqual(1.3f, mv.X, 1e-4);
            mv.Want(Dir.Left, 0.3f);
            mv.Step(0.1f, 1f, Open(m), 0.3f);
            Assert.AreEqual(Dir.Left, mv.Dir);
            Assert.AreEqual(1.2f, mv.X, 1e-4);
        }

        [Test] public void RandomInputNeverLeavesTheCorridorsOrEntersAWall()
        {
            var m = Maze.Parse(LevelDef.All[1].Map);
            var mv = new Mover();
            mv.Place(m.Start);
            var rng = new Random(7);
            float t = 0f;
            for (int i = 0; i < 20000; i++)
            {
                if (rng.Next(6) == 0) mv.Want(Dirs.All[rng.Next(4)], t);
                float dt = (float)rng.NextDouble() * 0.06f;
                t += dt;
                mv.Step(dt, 5.5f, m.PlayerCanEnter, t);
                Assert.IsTrue(m.PlayerCanEnter(mv.Tile), $"inside a wall at {mv.X},{mv.Y}");
                Assert.IsTrue(Math.Abs(mv.X - Math.Round(mv.X)) < 1e-3 || Math.Abs(mv.Y - Math.Round(mv.Y)) < 1e-3, "off the centre-lines");
            }
        }
    }

    public class HunterTests
    {
        static GameSim Sim(int level = 0, int seed = 1) => new GameSim(LevelDef.All[level], new RunScore(), seed);

        static void Run(GameSim s, float seconds, PlayerInput input = default)
        {
            for (float t = 0; t < seconds; t += 1f / 60f) { s.Tick(1f / 60f, input); s.Events.Clear(); }
        }

        [Test] public void HuntersLeaveThePenInOrder()
        {
            var s = Sim();
            Run(s, SimTime(s, 3f));
            Assert.AreNotEqual(HunterMode.Penned, s.Hunters[0].Mode, "Stalker released first");
            Assert.AreEqual(HunterMode.Penned, s.Hunters[3].Mode, "Listener still waiting");
        }

        static float SimTime(GameSim s, float playSeconds) => GameSim.ReadyTime + playSeconds;

        [Test] public void StalkerTargetsThePlayer()
        {
            var s = Sim();
            Assert.AreEqual(s.Player.Tile, HunterBrain.StalkerTarget(s.Player.Tile));
            s.Hunters[0].SetMode(HunterMode.Hunting);
            Assert.AreEqual(s.Player.Tile, s.TargetFor(0));
        }

        [Test] public void AmbusherTargetsTheNextJunctionAhead()
        {
            var m = Maze.Parse(LevelDef.All[0].Map);
            // on row 4 (an open corridor) heading right from (1,4): the first junction is (1,4)->(4,4)? walk and check
            var target = HunterBrain.AmbusherTarget(m, new Tile(1, 4), Dir.Right);
            Assert.IsTrue(m.IsJunction(target), $"{target} should be a junction");
            Assert.AreEqual(4, target.R);
            Assert.Greater(target.C, 1);
            Assert.LessOrEqual(target.C, 7);
            Assert.AreEqual(new Tile(1, 4), HunterBrain.AmbusherTarget(m, new Tile(1, 4), Dir.None));
        }

        [Test] public void WardenGuardsValuablesUntilThePlayerComesClose()
        {
            var s = Sim();
            var m = s.Maze;
            var w = s.Hunters[2];
            var fromPlayer = m.Distances(s.Player.Tile, t => m.HunterCanEnter(t, false) || m.PlayerCanEnter(t));
            var far = HunterBrain.WardenTarget(m, w, s.Player.Tile, fromPlayer, 20);
            Assert.Contains(far, HunterBrain.GuardPoints(m), "guards a valuable spot when the player is far");
            Assert.AreEqual(s.Player.Tile, HunterBrain.WardenTarget(m, w, s.Player.Tile, fromPlayer, 4), "chases when close");
        }

        [Test] public void ListenerInvestigatesADash()
        {
            var s = Sim();
            var l = s.Hunters[3];
            l.SetMode(HunterMode.Hunting);
            l.Mover.Place(new Tile(1, 19));
            s.Events.Clear();
            s.MakeNoise(new Tile(19, 1));
            Assert.IsTrue(l.Investigating);
            Assert.AreEqual(new Tile(19, 1), s.TargetFor(3));
            Assert.IsTrue(s.Events.Any(e => e.Kind == SimEventKind.HunterHeard && e.Hunter == 3));
        }

        [Test] public void ChooseFollowsTheShortestPathAndDoesNotTurnBack()
        {
            var m = Maze.Parse(LevelDef.All[0].Map);
            var field = m.Distances(new Tile(19, 4), t => m.HunterCanEnter(t, false));
            Assert.AreEqual(Dir.Right, HunterBrain.Choose(m, new Tile(10, 4), Dir.Right, field, t => m.HunterCanEnter(t, false)));
            // heading left, it may not reverse even though right is shorter
            Assert.AreNotEqual(Dir.Right, HunterBrain.Choose(m, new Tile(10, 4), Dir.Left, field, t => m.HunterCanEnter(t, false)));
        }

        [Test] public void HuntersNeverEnterWallsRiskRoomsOrTheExitDuringLongGames()
        {
            for (int level = 0; level < 3; level++)
            {
                var s = Sim(level, 11 + level);
                var bot = new AutoPilot(5) { TakeRisks = true };
                for (int f = 0; f < 60 * 120 && s.State != SimState.GameOver && s.State != SimState.Cleared; f++)
                {
                    s.Tick(1f / 60f, bot.Think(s));
                    s.Events.Clear();
                    foreach (var h in s.Hunters)
                    {
                        var g = s.Maze.GroundAt(h.Mover.Tile);
                        Assert.IsFalse(s.Maze.IsSolid(h.Mover.Tile), $"L{level + 1} {h.Kind} in a wall at {h.Mover.Tile}");
                        Assert.IsTrue(g != Ground.RiskFloor && g != Ground.RiskDoor && g != Ground.Exit, $"{h.Kind} on {g}");
                    }
                    Assert.IsTrue(s.Maze.PlayerCanEnter(s.Player.Tile), "player in a wall");
                }
            }
        }
    }

    public class SimTests
    {
        static void Tick(GameSim s, float seconds, PlayerInput input, List<SimEvent> log = null)
        {
            for (float t = 0; t < seconds; t += 1f / 60f)
            {
                s.Tick(1f / 60f, input);
                log?.AddRange(s.Events);
                s.Events.Clear();
            }
        }

        /// <summary>Keep the hunters in the pen so a test can focus on one rule.</summary>
        static void Calm(GameSim s) { foreach (var h in s.Hunters) h.ReleaseAt = 9999f; }

        static GameSim Playing(int level = 0)
        {
            var s = new GameSim(LevelDef.All[level], new RunScore(), 2);
            Tick(s, GameSim.ReadyTime + 0.05f, default);
            Assert.AreEqual(SimState.Playing, s.State);
            return s;
        }

        [Test] public void PowerSurgeMakesHuntersFleeWarnsAndEnds()
        {
            var s = Playing();
            Tick(s, 2f, default);   // let the Stalker out
            var power = new Tile(1, 3);
            s.Player.Place(new Tile(1, 4));
            var log = new List<SimEvent>();
            Tick(s, 0.4f, new PlayerInput { Want = Dir.Up }, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.SurgeStart));
            Assert.IsTrue(s.SurgeActive);
            Assert.IsTrue(s.Hunters.Where(h => h.Mode != HunterMode.Penned && h.Mode != HunterMode.Leaving).All(h => h.Mode == HunterMode.Fleeing));
            log.Clear();
            Tick(s, s.Def.SurgeSeconds - GameSim.WarnSeconds, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.SurgeWarning), "warning before it ends");
            Assert.IsTrue(s.SurgeEnding);
            Tick(s, GameSim.WarnSeconds + 0.1f, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.SurgeEnd));
            Assert.IsFalse(s.Hunters.Any(h => h.Mode == HunterMode.Fleeing));
        }

        [Test] public void CatchingHuntersScoresIncreasingCombosAndTheyRecoverInThePen()
        {
            var s = Playing();
            Calm(s);
            // let everyone out into the maze, far from the player
            var spots = new[] { new Tile(1, 1), new Tile(19, 1), new Tile(1, 19), new Tile(19, 19) };
            for (int i = 0; i < 4; i++) { s.Hunters[i].SetMode(HunterMode.Hunting); s.Hunters[i].Mover.Place(spots[i]); }
            var log = new List<SimEvent>();
            // force a surge and put each fleeing hunter on the player in turn
            typeof(GameSim).GetMethod("StartSurge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(s, null);
            int caught = 0;
            for (int i = 0; i < 4; i++)
            {
                var h = s.Hunters[i];
                if (h.Mode != HunterMode.Fleeing) continue;
                h.Mover.Place(s.Player.Tile);
                Tick(s, 1f / 60f, default, log);
                caught++;
            }
            var points = log.Where(e => e.Kind == SimEventKind.HunterCaught).Select(e => e.Value).ToList();
            Assert.AreEqual(caught, points.Count);
            for (int k = 0; k < points.Count; k++) Assert.AreEqual(200 << k, points[k]);
            var eaten = s.Hunters.First(h => h.Mode == HunterMode.Returning);
            Tick(s, 8f, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.HunterHome), "eaten hunters go home");
            Assert.AreNotEqual(HunterMode.Returning, eaten.Mode);
            Assert.AreNotEqual(HunterMode.Fleeing, eaten.Mode, "a recovered hunter isn't edible again in the same surge");
        }

        [Test] public void BeingCaughtCostsALifeRespawnsAndEndsTheGameAtZero()
        {
            var s = Playing();
            s.Run.Lives = 2;
            var log = new List<SimEvent>();
            var h = s.Hunters[0];
            h.SetMode(HunterMode.Hunting);
            h.Mover.Place(s.Player.Tile);
            Tick(s, 0.05f, default, log);
            Assert.AreEqual(SimState.Dying, s.State);
            Assert.AreEqual(1, s.Run.Lives);
            Tick(s, GameSim.DyingTime + 0.1f, default, log);
            Assert.AreEqual(SimState.Ready, s.State);
            Assert.AreEqual(s.Maze.Start, s.Player.Tile);
            Assert.IsTrue(s.Hunters.All(x => x.Mode == HunterMode.Penned), "hunters back in the pen: no unfair spawn");
            Tick(s, GameSim.ReadyTime + 0.05f, default, log);
            h.SetMode(HunterMode.Hunting);
            h.Mover.Place(s.Player.Tile);
            Tick(s, GameSim.DyingTime + 0.2f, default, log);
            Assert.AreEqual(SimState.GameOver, s.State);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.GameOver));
        }

        [Test] public void ExtraLifeAtTenThousand()
        {
            var s = Playing();
            s.Stats.RiskGems = 14;   // 10,500 points
            Tick(s, 0.05f, default);
            Assert.IsTrue(s.Run.ExtraLifeGiven);
            Assert.AreEqual(4, s.Run.Lives);
        }

        [Test] public void RiskRoomTrapsYouIfYouStayAndLetsYouGoIfYouLeave()
        {
            // stay inside: trapped
            var s = Playing();
            Calm(s);
            var room = s.Maze.RiskRooms[0];
            var inside = room.Cells[0];
            s.Player.Place(inside);
            var log = new List<SimEvent>();
            Tick(s, 0.1f, default, log);
            Assert.IsTrue(room.Entered);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.RiskEntered));
            Tick(s, s.Def.RiskSeconds, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.RiskWarning), "telegraphed");
            Assert.IsTrue(room.Sealed);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.RiskSealed && e.Flag), "trapped");
            Assert.AreEqual(SimState.Dying, s.State);

            // step out in time: safe, door seals behind you
            s = Playing();
            Calm(s);
            room = s.Maze.RiskRooms[0];
            s.Player.Place(room.Cells[0]);
            Tick(s, 0.1f, default);
            var outside = Dirs.All.Select(d => room.Door.Step(d)).First(t => s.Maze.RoomOf(t) == null && s.Maze.PlayerCanEnter(t));
            s.Player.Place(outside);
            log.Clear();
            Tick(s, s.Def.RiskSeconds, default, log);
            Assert.IsTrue(room.Sealed);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.RiskSealed && !e.Flag));
            Assert.AreEqual(SimState.Playing, s.State);
            Assert.IsFalse(s.Maze.PlayerCanEnter(room.Door), "the door is now solid");
        }

        [Test] public void RiskDoorNeverClosesOnThePlayer()
        {
            var s = Playing();
            var room = s.Maze.RiskRooms[0];
            s.Player.Place(room.Cells[0]);
            Tick(s, 0.1f, default);
            s.Player.Place(room.Door);
            Tick(s, s.Def.RiskSeconds + 2f, default);
            Assert.IsFalse(room.Sealed, "waits while someone is in the doorway");
            Assert.AreEqual(SimState.Playing, s.State);
        }

        [Test] public void LivingWallsAreTelegraphedAndNeverMoveOntoAnyone()
        {
            var s = Playing(1);
            var log = new List<SimEvent>();
            s.Run.Lives = 99;
            var bot = new AutoPilot(9);
            float warnedAt = -1, shiftedAt = -1;
            float t = 0;
            for (int f = 0; f < 60 * 90; f++)
            {
                var occupiedBefore = s.Occupied().ToList();
                s.Tick(1f / 60f, bot.Think(s));
                t += 1f / 60f;
                foreach (var e in s.Events)
                {
                    if (e.Kind == SimEventKind.ShiftWarning && warnedAt < 0) warnedAt = t;
                    if (e.Kind == SimEventKind.Shift && shiftedAt < 0) shiftedAt = t;
                    if (e.Kind == SimEventKind.Shift)
                    {
                        // nobody stands in a cell that just became solid
                        foreach (var o in s.Occupied()) Assert.IsFalse(s.Maze.IsSolid(o) && s.Maze.GroupAt(o) != null, $"wall moved onto {o}");
                        // and everything required is still reachable
                        var reach = s.Maze.Distances(s.Player.Tile, x => s.Maze.PlayerCanEnter(x) || x == s.Maze.Exit);
                        foreach (var r in s.Maze.RequiredTiles()) Assert.GreaterOrEqual(s.Maze.Dist(reach, r), 0, $"{r} cut off");
                    }
                }
                log.AddRange(s.Events);
                s.Events.Clear();
            }
            Assert.Greater(warnedAt, 0, "a shift was announced");
            Assert.Greater(shiftedAt, 0, "a shift happened");
            Assert.GreaterOrEqual(shiftedAt - warnedAt, GameSim.ShiftTelegraph - 0.05f, "with at least the telegraph time to react");
        }

        [Test] public void ClearingTheMazeOpensTheExitAndTheExitEndsTheLevel()
        {
            var s = Playing();
            var log = new List<SimEvent>();
            foreach (var t in s.Maze.RequiredTiles().Where(t => s.Maze.ItemAt(t) == Item.Node).ToList()) { s.Maze.Take(t); s.Stats.Nodes++; }
            Assert.IsTrue(s.Maze.ExitOpen);
            s.Player.Place(s.Maze.Exit.Step(Dir.Down));
            Tick(s, 0.5f, new PlayerInput { Want = Dir.Up }, log);
            Assert.AreEqual(SimState.Cleared, s.State);
            var clear = log.First(e => e.Kind == SimEventKind.LevelCleared);
            Assert.AreEqual(Points.LevelClear(1) + Points.TimeBonus(s.Stats.Seconds, s.Def.ParSeconds), clear.Value);
            Assert.IsTrue(s.Stats.Cleared);
        }

        [Test] public void DashIsLimitedByItsCooldownAndMakesNoise()
        {
            var s = Playing();
            var log = new List<SimEvent>();
            Tick(s, 1f / 60f, new PlayerInput { Want = Dir.Left, Dash = true }, log);
            Assert.AreEqual(1, log.Count(e => e.Kind == SimEventKind.Dash));
            Assert.IsTrue(s.Dashing);
            Tick(s, 1f, new PlayerInput { Want = Dir.Left, Dash = true }, log);
            Assert.AreEqual(1, log.Count(e => e.Kind == SimEventKind.Dash), "still cooling down");
            Tick(s, GameSim.DashCooldown, new PlayerInput { Want = Dir.Left, Dash = true }, log);
            Assert.AreEqual(2, log.Count(e => e.Kind == SimEventKind.Dash));
        }

        [Test] public void ScoreEqualsTheSumOfEventPointsAndEncodesForTheScoreboard()
        {
            var run = new RunScore();
            var s = new GameSim(LevelDef.All[0], run, 4);
            var bot = new AutoPilot(4) { TakeRisks = true };
            int points = 0;
            for (int f = 0; f < 60 * 200 && s.State != SimState.GameOver && s.State != SimState.Cleared; f++)
            {
                s.Tick(1f / 60f, bot.Think(s));
                foreach (var e in s.Events)
                    if (e.Kind == SimEventKind.Node || e.Kind == SimEventKind.Power || e.Kind == SimEventKind.Gem || e.Kind == SimEventKind.RiskGem
                        || e.Kind == SimEventKind.HunterCaught || e.Kind == SimEventKind.LevelCleared) points += e.Value;
                s.Events.Clear();
            }
            Assert.AreEqual(points, run.Total);
            StringAssert.StartsWith("1:", run.Encode());
            Assert.AreEqual(10, run.Encode().Split(':')[1].Split(',').Length);
        }

        [Test] public void TheBotCanClearEveryLevel()
        {
            foreach (var def in LevelDef.All)
            {
                bool cleared = false;
                for (int seed = 1; seed <= 4 && !cleared; seed++)
                {
                    var s = new GameSim(def, new RunScore { Lives = 9 }, seed);
                    var bot = new AutoPilot(seed);
                    for (int f = 0; f < 60 * 400 && s.State != SimState.GameOver && s.State != SimState.Cleared; f++) { s.Tick(1f / 60f, bot.Think(s)); s.Events.Clear(); }
                    cleared = s.State == SimState.Cleared;
                    TestContext.Progress.WriteLine($"{def.Name} seed {seed}: {s.State} t={s.Clock:0}s deaths={s.Stats.Deaths} nodes={s.Maze.NodesLeft} left score={s.Run.Total}");
                }
                Assert.IsTrue(cleared, $"{def.Name} is completable");
            }
        }
    }
    public class PowerUpTests
    {
        static readonly string[] Cross = { "#######", "###.###", "#S....#", "###.###", "###-###", "##HHH##", "###E###" };

        static void Tick(GameSim s, float seconds, PlayerInput input = default, List<SimEvent> log = null)
        {
            for (float t = 0; t < seconds; t += 1f / 60f)
            {
                s.Tick(1f / 60f, input);
                log?.AddRange(s.Events);
                s.Events.Clear();
            }
        }

        static GameSim Playing(int level = 0)
        {
            var s = new GameSim(LevelDef.All[level], new RunScore { Lives = 9 }, 3);
            Tick(s, GameSim.ReadyTime + 0.05f);
            Assert.AreEqual(SimState.Playing, s.State);
            return s;
        }

        /// <summary>Send every hunter out into the maze, a few tiles from the player.</summary>
        static void AllOut(GameSim s)
        {
            foreach (var h in s.Hunters) { h.Mover.Place(s.GateOut, Dir.Left); h.SetMode(HunterMode.Hunting); }
        }

        static void Take(GameSim s, PowerUp kind, List<SimEvent> log = null)
        {
            Assert.IsTrue(s.SpawnPickup(kind, s.Player.Tile));
            Tick(s, 1f / 60f, default, log);
            Assert.AreEqual(PowerUp.None, s.Pickup, "picked up");
        }

        [Test] public void ATurnPressedJustPastAJunctionStillTurns()
        {
            var m = Maze.Parse(Cross);
            foreach (var corner in new[] { 0f, GameSim.PlayerCorner })
            {
                var mv = new Mover { Corner = corner };
                mv.Place(m.Start);
                mv.Want(Dir.Right, 0f);
                float t = 0f;
                for (int i = 0; i < 14; i++) { mv.Step(0.1f, 1.5f, m.PlayerCanEnter, t); t += 0.1f; }   // x = 3.1: just past the junction at 3
                Assert.AreEqual(3.1f, mv.X, 1e-3);
                mv.Want(Dir.Up, t);
                mv.Step(0.05f, 1.5f, m.PlayerCanEnter, t);
                if (corner > 0f) { Assert.AreEqual(Dir.Up, mv.Dir, "late turn forgiven"); Assert.AreEqual(3f, mv.X, 1e-4, "on the centre-line"); }
                else Assert.AreEqual(Dir.Right, mv.Dir, "no slack: the junction was missed");
            }
        }

        [Test] public void ThePlayerRemembersATurnForMostOfASecond()
        {
            var s = Playing();
            Assert.AreEqual(GameSim.PlayerBufferWindow, s.Player.BufferWindow);
            Assert.GreaterOrEqual(s.Player.BufferWindow * s.Def.PlayerSpeed, 4f, "about four tiles of warning");
        }

        [Test] public void TheHomePillSendsEveryoneHomeForTenSeconds()
        {
            var s = Playing();
            AllOut(s);
            Tick(s, 0.5f);
            var log = new List<SimEvent>();
            Take(s, PowerUp.Home, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.PickupTaken && e.Value == (int)PowerUp.Home));
            Assert.AreEqual(GameSim.HomeSeconds, s.HomeLeft, 0.05f);
            foreach (var h in s.Hunters) Assert.AreEqual(HunterMode.Returning, h.Mode, h.Kind.ToString());
            // they reach the pen and stay there; nobody can catch the player
            Tick(s, GameSim.HomeSeconds - 0.3f, default, log);
            foreach (var h in s.Hunters)
            {
                Assert.AreEqual(HunterMode.Recovering, h.Mode, h.Kind + " waits at home");
                Assert.IsTrue(h.OnPenSide(s.Maze), h.Kind + " is in the pen");
            }
            Assert.IsFalse(log.Any(e => e.Kind == SimEventKind.PlayerCaught));
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.PowerUpEnding && e.Value == (int)PowerUp.Home), "warned before it ends");
            Tick(s, 0.6f, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.PowerUpEnd && e.Value == (int)PowerUp.Home));
            Assert.IsTrue(s.Hunters.All(h => h.Mode == HunterMode.Leaving), "back out when it wears off");
        }

        [Test] public void TheHomePillHoldsHuntersThatHaventLeftYet()
        {
            var s = Playing();
            Take(s, PowerUp.Home);
            Tick(s, GameSim.HomeSeconds - 0.5f);
            Assert.IsTrue(s.Hunters.All(h => h.Mode == HunterMode.Penned), "release waits");
            Tick(s, 1f);
            Assert.IsTrue(s.Hunters.Any(h => h.Mode != HunterMode.Penned), "release resumes");
        }

        [Test] public void FreezeStopsHuntersAndMakesThemHarmless()
        {
            var s = Playing();
            AllOut(s);
            Tick(s, 0.5f);
            Take(s, PowerUp.Freeze);
            var where = s.Hunters.Select(h => (h.Mover.X, h.Mover.Y)).ToArray();
            var log = new List<SimEvent>();
            // stand a hunter right on the player: frozen, it does nothing
            s.Hunters[0].Mover.Place(s.Player.Tile);
            where[0] = (s.Hunters[0].Mover.X, s.Hunters[0].Mover.Y);
            Tick(s, GameSim.FreezeSeconds - 0.5f, default, log);
            for (int i = 0; i < 4; i++) Assert.AreEqual(where[i], (s.Hunters[i].Mover.X, s.Hunters[i].Mover.Y), "frozen in place");
            Assert.IsTrue(s.IsFrozen(s.Hunters[1]));
            Assert.IsFalse(log.Any(e => e.Kind == SimEventKind.PlayerCaught), "touching a frozen hunter is safe");
            Tick(s, 1f, default, log);
            Assert.IsFalse(s.IsFrozen(s.Hunters[1]));
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.PlayerCaught), "thawed, it catches the player");
        }

        [Test] public void SpeedMakesThePlayerFasterForAWhile()
        {
            var s = Playing();
            foreach (var h in s.Hunters) h.ReleaseAt = 9999f;
            var input = new PlayerInput { Want = Dir.Left };
            float x0 = s.Player.X; Tick(s, 0.3f, input); float normal = Math.Abs(s.Player.X - x0);
            s.Player.Place(s.Maze.Start);
            Take(s, PowerUp.Speed);
            x0 = s.Player.X; Tick(s, 0.3f, input); float fast = Math.Abs(s.Player.X - x0);
            Assert.AreEqual(GameSim.SpeedBoost, fast / normal, 0.08f);
            Tick(s, GameSim.SpeedSeconds);
            Assert.AreEqual(0f, s.SpeedLeft);
        }

        [Test] public void TheShieldTakesOneHitAndSendsTheHunterHome()
        {
            var s = Playing();
            AllOut(s);
            foreach (var h in s.Hunters.Skip(1)) { h.SetMode(HunterMode.Penned); h.ReleaseAt = 9999f; h.Mover.Place(h.PenSpot); }
            Take(s, PowerUp.Shield);
            Assert.IsTrue(s.Shielded);
            var log = new List<SimEvent>();
            s.Hunters[0].Mover.Place(s.Player.Tile);
            Tick(s, 1f / 60f, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.ShieldBlock && e.Hunter == 0));
            Assert.IsFalse(log.Any(e => e.Kind == SimEventKind.PlayerCaught));
            Assert.IsFalse(s.Shielded, "used up");
            Assert.AreEqual(HunterMode.Returning, s.Hunters[0].Mode);
            s.Hunters[0].SetMode(HunterMode.Hunting);
            s.Hunters[0].Mover.Place(s.Player.Tile);
            Tick(s, 1f / 60f, default, log);
            Assert.IsTrue(log.Any(e => e.Kind == SimEventKind.PlayerCaught), "the second hit counts");
        }

        [Test] public void PickupsAppearOnlyInSafeReachableSpotsAndExpire()
        {
            foreach (var def in LevelDef.All)
            {
                var s = new GameSim(def, new RunScore { Lives = 99 }, 5);
                var bot = new AutoPilot(5);
                int spawned = 0, expiredOrTaken = 0;
                for (int f = 0; f < 60 * 240 && s.State != SimState.GameOver && s.State != SimState.Cleared; f++)
                {
                    s.Tick(1f / 60f, bot.Think(s));
                    foreach (var e in s.Events)
                    {
                        if (e.Kind == SimEventKind.PickupSpawned)
                        {
                            spawned++;
                            var t = e.Tile;
                            Assert.IsTrue(s.Maze.PlayerCanEnter(t) && s.Maze.HunterCanEnter(t, false), $"{def.Name} {t} is open floor");
                            Assert.IsNull(s.Maze.RoomOf(t), $"{def.Name} {t} not in a risk room");
                            Assert.IsNull(s.Maze.GroupAt(t), $"{def.Name} {t} not on a living wall");
                            Assert.GreaterOrEqual(s.Maze.Dist(s.Maze.Distances(s.Player.Tile, s.Maze.PlayerCanEnter), t), 6, "reachable, not on top of the player");
                        }
                        if (e.Kind == SimEventKind.PickupExpired || e.Kind == SimEventKind.PickupTaken) expiredOrTaken++;
                    }
                    s.Events.Clear();
                }
                TestContext.Progress.WriteLine($"{def.Name}: {spawned} pickups, {expiredOrTaken} taken or expired");
                Assert.GreaterOrEqual(spawned, 2, def.Name);
                Assert.GreaterOrEqual(expiredOrTaken, spawned - 1, "each pickup is taken or expires");
            }
        }
    }
}
