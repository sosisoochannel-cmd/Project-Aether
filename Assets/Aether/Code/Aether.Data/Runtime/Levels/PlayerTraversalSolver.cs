using System;
using System.Collections.Generic;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Data.Levels
{
    /// <summary>
    /// Proves that a level can actually be traversed with the player the game ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the pipeline's half of the traversal model; <c>tools/verify/levelcheck.py</c> is the
    /// gate's half, and the two are meant to agree. Both read their numbers from
    /// <see cref="PlayerTuningData"/>, so neither can drift from the game's real jump arc.
    /// </para>
    /// <para>
    /// The model is a fixed set of simulated control sequences rather than a search over continuous
    /// input: approach speed (full or half), jump strength (full or cut short), and horizontal input
    /// (hold, brake to a stop, or start from standing). That set spans what the player can actually
    /// do and keeps the solver fast and deterministic. It is conservative in the direction that
    /// matters: anything it cannot prove is reported as unreachable rather than assumed possible.
    /// </para>
    /// <para>
    /// Numbers here are single precision while the gate's are double, so a boundary case could in
    /// principle be classified differently by the two. That is why every link in the level keeps a
    /// wide margin rather than sitting on a threshold, and why the gate is treated as the authority.
    /// </para>
    /// </remarks>
    public sealed class PlayerTraversalSolver
    {
        private const float Dt = 1f / 120f;
        private const int MaxSteps = 600;
        private const float Epsilon = 1e-4f;
        private const float BrakeAt = 0.15f;
        private const float CutAt = 0.10f;
        private const float RampAt = 0.15f;

        /// <summary>A hop that leaves less slack than this is treated as unfair.</summary>
        public const float MarginFloor = 0.15f;

        /// <summary>How heavily route search penalises a hop that leaves little slack.</summary>
        private const float FragilityWeight = 4f;

        private readonly LevelData _level;
        private readonly PlayerTuningData _tuning;
        private List<PlayableNode> _nodes;
        private HashSet<PlayableNode> _nodeSet;
        private Dictionary<PlayableNode, List<TraversalEdge>> _edges;
        private readonly Dictionary<long, float> _reachCache = new Dictionary<long, float>();

        public PlayerTraversalSolver(LevelData level, PlayerTuningData tuning)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        }

        // -- geometry ------------------------------------------------------------------------

        /// <summary>World y of the surface a body stands on when it occupies tile (col, row).</summary>
        public float FeetY(int row) => (_level.Height - row) * _level.TileSize;

        /// <summary>True when the body box at this position touches a solid tile or leaves the map.</summary>
        public bool BodyBlocked(float cx, float feet)
        {
            float half = _tuning.BodyWidth * 0.5f;
            float tile = _level.TileSize;
            int c0 = Mathf.FloorToInt((cx - half + Epsilon) / tile);
            int c1 = Mathf.FloorToInt((cx + half - Epsilon) / tile);
            int rBottom = _level.Height - 1 - Mathf.FloorToInt((feet + Epsilon) / tile);
            int rTop = _level.Height - 1 - Mathf.FloorToInt((feet + _tuning.BodyHeight - Epsilon) / tile);
            for (int col = c0; col <= c1; col++)
            {
                if (col < 0 || col >= _level.Width) return true;   // the map edge is a wall
                for (int row = rTop; row <= rBottom; row++)
                {
                    if (_level.IsSolidAt(col, row)) return true;
                }
            }
            return false;
        }

        /// <summary>True when a solid tile sits directly under the body's feet.</summary>
        public bool HasSupport(float cx, float feet)
        {
            float tile = _level.TileSize;
            float half = _tuning.BodyWidth * 0.5f;
            int c0 = Mathf.FloorToInt((cx - half + Epsilon) / tile);
            int c1 = Mathf.FloorToInt((cx + half - Epsilon) / tile);
            int row = _level.Height - 1 - Mathf.FloorToInt((feet - 0.02f) / tile);
            for (int col = c0; col <= c1; col++)
            {
                if (_level.IsSolidAt(col, row)) return true;
            }
            return false;
        }

        /// <summary>Top surface of the highest solid tile the body has just penetrated.</summary>
        public float SnapSurface(float cx, float feet)
        {
            float tile = _level.TileSize;
            float half = _tuning.BodyWidth * 0.5f;
            int c0 = Mathf.FloorToInt((cx - half + Epsilon) / tile);
            int c1 = Mathf.FloorToInt((cx + half - Epsilon) / tile);
            int rBottom = _level.Height - 1 - Mathf.FloorToInt((feet + Epsilon) / tile);
            int rTop = _level.Height - 1 - Mathf.FloorToInt((feet + _tuning.BodyHeight - Epsilon) / tile);
            int bestRow = int.MaxValue;
            for (int col = c0; col <= c1; col++)
            {
                for (int row = rTop; row <= rBottom; row++)
                {
                    if (_level.IsSolidAt(col, row) && row < bestRow) bestRow = row;
                }
            }
            return bestRow == int.MaxValue ? feet : (_level.Height - bestRow) * tile;
        }

        /// <summary>Every tile the player can come to rest on.</summary>
        public List<PlayableNode> StandNodes()
        {
            if (_nodes != null) return _nodes;

            var nodes = new List<PlayableNode>();
            float tile = _level.TileSize;
            for (int row = 1; row < _level.Height; row++)
            {
                for (int col = 0; col < _level.Width; col++)
                {
                    if (!_level.IsSolidAt(col, row) || _level.IsSolidAt(col, row - 1)) continue;
                    if (!BodyBlocked((col + 0.5f) * tile, FeetY(row))) nodes.Add(new PlayableNode(col, row));
                }
            }
            _nodes = nodes;
            return nodes;
        }

        private HashSet<PlayableNode> NodeSet()
        {
            if (_nodeSet == null) _nodeSet = new HashSet<PlayableNode>(StandNodes());
            return _nodeSet;
        }

        /// <summary>
        /// True when the player can run between two tiles without leaving the ground: continuous
        /// floor, clear headroom, and a body that fits the whole way.
        /// </summary>
        public bool CanWalk(PlayableNode a, PlayableNode b, int limit = 4)
        {
            if (a.Row != b.Row) return false;
            if (Mathf.Abs(b.Col - a.Col) > limit) return false;

            int step = b.Col > a.Col ? 1 : -1;
            float tile = _level.TileSize;
            float feet = FeetY(a.Row);

            for (int col = a.Col; col != b.Col + step; col += step)
            {
                if (!_level.IsSolidAt(col, a.Row)) return false;
                if (_level.IsSolidAt(col, a.Row - 1)) return false;
            }
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                float cx = ((a.Col + 0.5f) + (t * (b.Col - a.Col))) * tile;
                if (BodyBlocked(cx, feet)) return false;
            }
            return true;
        }

        // -- simulation ----------------------------------------------------------------------

        /// <summary>One simulated control sequence, returning where the player came to rest.</summary>
        public bool TryRunSequence(PlayableNode start, int direction, float speedScale, bool cutJump,
                                   string horizontal, bool jumped, out PlayableNode landing,
                                   out float span, out float rise)
        {
            float tile = _level.TileSize;
            float cx = (start.Col + 0.5f) * tile;
            float feet = FeetY(start.Row);
            float startCx = cx;
            float startFeet = feet;
            bool grounded = !jumped;

            float vy = jumped ? _tuning.JumpVelocity : 0f;
            float vx;
            if (jumped)
            {
                vx = horizontal == "ramp" ? 0f : _tuning.RunSpeed * speedScale * direction;
            }
            else
            {
                vx = _tuning.RunSpeed * direction;
            }

            bool cutUsed = false;
            float time = 0f;
            bool finished = false;

            for (int step = 0; step < MaxSteps; step++)
            {
                time += Dt;

                if (jumped)
                {
                    if (horizontal == "ramp" && time < RampAt)
                    {
                        vx = Approach(vx, 0f, _tuning.AirAcceleration * Dt);
                    }
                    else if (horizontal == "ramp")
                    {
                        vx = Approach(vx, _tuning.RunSpeed * speedScale * direction, _tuning.AirAcceleration * Dt);
                    }
                    else if (horizontal == "brake" && time >= BrakeAt)
                    {
                        vx = Approach(vx, 0f, _tuning.AirDeceleration * Dt);
                    }

                    if (cutJump && !cutUsed && time >= CutAt && vy > 0f)
                    {
                        vy *= _tuning.JumpCutMultiplier;
                        cutUsed = true;
                    }
                }

                float moved = cx + (vx * Dt);
                if (BodyBlocked(moved, feet))
                {
                    vx = 0f;                       // stopped by a wall; keep the height we have
                }
                else
                {
                    cx = moved;
                }

                if (grounded)
                {
                    grounded = HasSupport(cx, feet);
                    if (grounded)
                    {
                        continue;                  // still running along the ground
                    }
                    vy = 0f;                       // just ran off an edge
                }

                float gravity = vy > 0f ? _tuning.RiseGravity : _tuning.FallGravity;
                vy = Mathf.Max(vy - (gravity * Dt), -_tuning.MaxFallSpeed);
                feet += vy * Dt;

                if (BodyBlocked(cx, feet))
                {
                    if (vy > 0f)
                    {
                        feet -= vy * Dt;           // head bump
                        vy = 0f;
                    }
                    else
                    {
                        feet = SnapSurface(cx, feet);
                        vy = 0f;
                        grounded = true;
                        finished = true;
                        break;
                    }
                }

                if (feet < -_level.Height)
                {
                    landing = default;
                    span = Mathf.Abs(cx - startCx);
                    rise = 0f;
                    return false;                  // left the level through the floor
                }
            }

            span = Mathf.Abs(cx - startCx);
            rise = feet - startFeet;

            if (!finished && !grounded)
            {
                landing = default;
                return false;
            }

            int col = Mathf.FloorToInt(cx / tile);
            int row = _level.Height - Mathf.RoundToInt(feet / tile);
            var candidate = new PlayableNode(col, row);
            if (!NodeSet().Contains(candidate))
            {
                landing = default;
                return false;
            }

            landing = candidate;
            return true;
        }

        private static float Approach(float current, float target, float rate)
        {
            return current < target ? Mathf.Min(current + rate, target) : Mathf.Max(current - rate, target);
        }

        /// <summary>Farthest distance the player can come to rest in this direction from this tile.</summary>
        public float MaxReach(PlayableNode node, int direction)
        {
            long key = ((long)node.Col << 32) ^ (uint)node.Row ^ (direction > 0 ? 1L << 48 : 0L);
            if (_reachCache.TryGetValue(key, out float cached)) return cached;

            float best = 0f;
            foreach (ControlProfile profile in ControlProfile.All)
            {
                if (!TryRunSequence(node, direction, profile.SpeedScale, profile.CutJump, profile.Horizontal,
                                    true, out PlayableNode landing, out float span, out _))
                {
                    continue;
                }
                if ((landing.Col - node.Col) * direction > 0 && span > best) best = span;
            }

            _reachCache[key] = best;
            return best;
        }

        /// <summary>All ways the player can leave one tile.</summary>
        public List<TraversalEdge> EdgesFrom(PlayableNode node, Dictionary<PlayableNode, List<TraversalEdge>> graph)
        {
            var result = new List<TraversalEdge>();
            float tile = _level.TileSize;

            for (int direction = -1; direction <= 1; direction += 2)
            {
                float reach = MaxReach(node, direction);

                for (int step = 1; step <= 4; step++)
                {
                    var target = new PlayableNode(node.Col + (direction * step), node.Row);
                    if (NodeSet().Contains(target) && CanWalk(node, target))
                    {
                        result.Add(new TraversalEdge(target, TraversalEdgeKind.Walk, "walk", step * tile, 0f, reach));
                    }
                }

                if (TryRunSequence(node, direction, 1f, false, "hold", false,
                                   out PlayableNode landing, out float span, out float rise)
                    && !landing.Equals(node)
                    && (landing.Col - node.Col) * direction > 0
                    && rise < -Epsilon)
                {
                    TraversalEdgeKind kind = -rise <= tile + Epsilon ? TraversalEdgeKind.Walk : TraversalEdgeKind.Drop;
                    result.Add(new TraversalEdge(landing, kind, "walk-off", span, rise, reach));
                }

                foreach (ControlProfile profile in ControlProfile.All)
                {
                    if (!TryRunSequence(node, direction, profile.SpeedScale, profile.CutJump,
                                        profile.Horizontal, true, out landing, out _, out rise))
                    {
                        continue;
                    }
                    if (landing.Equals(node) || (landing.Col - node.Col) * direction <= 0) continue;

                    // What the level demands is reaching the destination tile, so the demanded
                    // travel is the distance to its near edge.
                    float nearEdge = direction > 0 ? landing.Col : landing.Col + 1;
                    float needed = Mathf.Abs(nearEdge - ((node.Col + 0.5f) * tile));
                    result.Add(new TraversalEdge(landing, TraversalEdgeKind.Leap, profile.Name, needed, rise, reach));
                }
            }

            graph[node] = result;
            return result;
        }

        private Dictionary<PlayableNode, List<TraversalEdge>> BuildGraph()
        {
            if (_edges != null) return _edges;
            var graph = new Dictionary<PlayableNode, List<TraversalEdge>>();
            foreach (PlayableNode node in StandNodes()) EdgesFrom(node, graph);
            _edges = graph;
            return graph;
        }

        // -- search --------------------------------------------------------------------------

        /// <summary>
        /// Cheapest route between two tiles, or null.
        /// </summary>
        /// <remarks>
        /// Cost is lexicographic: unfair hops first, then jump count, then distance weighted by how
        /// little slack the hop leaves. Putting unfair hops first is what makes the report honest —
        /// a route of two comfortable jumps is the one a player takes, and reporting a single
        /// pixel-perfect jump instead would hide the very thing this solver exists to find.
        /// </remarks>
        public List<TraversalEdge> FindRoute(PlayableNode start, PlayableNode goal, bool walkOnly)
        {
            Dictionary<PlayableNode, List<TraversalEdge>> graph = BuildGraph();
            if (!graph.ContainsKey(start) || !graph.ContainsKey(goal)) return null;

            var best = new Dictionary<PlayableNode, RouteCost> { [start] = new RouteCost(0, 0, 0f) };
            var previous = new Dictionary<PlayableNode, KeyValuePair<PlayableNode, TraversalEdge>>();
            var visited = new HashSet<PlayableNode>();

            while (true)
            {
                PlayableNode current = default;
                bool found = false;
                RouteCost bestCost = default;
                foreach (KeyValuePair<PlayableNode, RouteCost> candidate in best)
                {
                    if (visited.Contains(candidate.Key)) continue;
                    if (!found || candidate.Value.CompareTo(bestCost) < 0)
                    {
                        current = candidate.Key;
                        bestCost = candidate.Value;
                        found = true;
                    }
                }
                if (!found) return null;
                if (current.Equals(goal))
                {
                    var route = new List<TraversalEdge>();
                    PlayableNode cursor = goal;
                    while (previous.TryGetValue(cursor, out KeyValuePair<PlayableNode, TraversalEdge> step))
                    {
                        route.Add(step.Value);
                        cursor = step.Key;
                    }
                    route.Reverse();
                    return route;
                }

                visited.Add(current);
                List<TraversalEdge> outgoing = graph[current];
                for (int i = 0; i < outgoing.Count; i++)
                {
                    TraversalEdge edge = outgoing[i];
                    if (walkOnly && edge.Kind != TraversalEdgeKind.Walk) continue;

                    int unfair = edge.Kind == TraversalEdgeKind.Leap && edge.Margin < MarginFloor ? 1 : 0;
                    float weight = 1f + (FragilityWeight * (1f - edge.Margin));
                    var next = new RouteCost(bestCost.Unfair + unfair,
                                             bestCost.Jumps + edge.Jumps,
                                             bestCost.Distance + (edge.Span * weight));
                    if (!best.TryGetValue(edge.Target, out RouteCost existing) || next.CompareTo(existing) < 0)
                    {
                        best[edge.Target] = next;
                        previous[edge.Target] = new KeyValuePair<PlayableNode, TraversalEdge>(current, edge);
                    }
                }
            }
        }

        /// <summary>Nearest standable tile to a cell, which is what the report talks about.</summary>
        public bool TryFindNearestNode(int col, int row, out PlayableNode node)
        {
            var wanted = new PlayableNode(col, row);
            if (NodeSet().Contains(wanted))
            {
                node = wanted;
                return true;
            }

            int bestDistance = int.MaxValue;
            bool found = false;
            node = default;
            foreach (PlayableNode candidate in StandNodes())
            {
                int distance = Mathf.Abs(candidate.Col - col) + Mathf.Abs(candidate.Row - row);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    node = candidate;
                    found = true;
                }
            }
            return found;
        }

        // -- reports -------------------------------------------------------------------------

        /// <summary>Checks every traversal claim in the level, in file order.</summary>
        public List<TraversalReport> CheckConnections()
        {
            var reports = new List<TraversalReport>();
            for (int i = 0; i < _level.Connections.Count; i++)
            {
                reports.Add(CheckConnection(_level.Connections[i]));
            }
            return reports;
        }

        private TraversalReport CheckConnection(TraversalConnection connection)
        {
            LevelEntity from = _level.FindEntity(connection.FromId);
            LevelEntity to = _level.FindEntity(connection.ToId);
            if (from == null || to == null)
            {
                return new TraversalReport(connection.FromId, connection.ToId, "unknown",
                    TraversalReachability.Blocked, "endpoint is not in the level", 0f, 0f);
            }

            if (!TryFindNearestNode(from.Position.x, from.Position.y, out PlayableNode start)
                || start.Col != from.Position.x || start.Row != from.Position.y)
            {
                return new TraversalReport(connection.FromId, connection.ToId, "unknown",
                    TraversalReachability.Blocked, "the starting tile is not somewhere the player can stand", 0f, 0f);
            }
            if (!TryFindNearestNode(to.Position.x, to.Position.y, out PlayableNode goal)
                || goal.Col != to.Position.x || goal.Row != to.Position.y)
            {
                return new TraversalReport(connection.FromId, connection.ToId, "unknown",
                    TraversalReachability.Blocked, "the destination tile is not somewhere the player can stand", 0f, 0f);
            }

            List<TraversalEdge> route = FindRoute(start, goal, connection.MustWalk);
            if (route == null)
            {
                List<TraversalEdge> anyRoute = connection.MustWalk ? FindRoute(start, goal, false) : null;
                if (anyRoute != null)
                {
                    int needed = 0;
                    for (int i = 0; i < anyRoute.Count; i++) needed += anyRoute[i].Jumps;
                    return new TraversalReport(connection.FromId, connection.ToId, "mixed",
                        TraversalReachability.Blocked,
                        $"a route exists but needs {needed} jump(s) and this link is declared must=walk",
                        0f, 0f);
                }
                return new TraversalReport(connection.FromId, connection.ToId, "unknown",
                    TraversalReachability.Unreachable,
                    "no sequence of the player's jumps and walks connects these two tiles", 0f, 0f);
            }

            int jumps = 0;
            int leaps = 0;
            int drops = 0;
            float tightest = 1f;
            float travel = 0f;
            float climb = 0f;
            string tightestProfile = "-";
            for (int i = 0; i < route.Count; i++)
            {
                TraversalEdge edge = route[i];
                jumps += edge.Jumps;
                travel += edge.Span;
                if (edge.Rise > climb) climb = edge.Rise;
                if (edge.Kind == TraversalEdgeKind.Drop) drops++;
                if (edge.Kind != TraversalEdgeKind.Leap) continue;
                leaps++;
                if (edge.Margin < tightest)
                {
                    tightest = edge.Margin;
                    tightestProfile = edge.Profile;
                }
            }

            List<TraversalEdge> back = FindRoute(goal, start, false);
            bool oneWay = back == null;
            if (oneWay && !connection.OneWay)
            {
                return new TraversalReport(connection.FromId, connection.ToId, "mixed",
                    TraversalReachability.Ambiguous,
                    "traversable, but there is no way back and the data does not declare oneway",
                    travel, climb);
            }

            if (leaps > 0 && tightest < MarginFloor)
            {
                return new TraversalReport(connection.FromId, connection.ToId, "leap",
                    TraversalReachability.Ambiguous,
                    $"the best route's tightest jump leaves only {tightest:P0} of its reach "
                    + $"({tightestProfile}); the floor is {MarginFloor:P0}",
                    travel, climb);
            }

            string kind = leaps > 0 ? "leap" : drops > 0 ? "drop" : "walk";
            string detail = leaps == 0
                ? $"walk only: {route.Count} step(s) of ground, no jump input needed"
                : $"{route.Count} hop(s), {jumps} jump(s), tightest jump leaves {tightest:P0} of its reach";
            if (oneWay) detail += "; declared one-way";

            return new TraversalReport(connection.FromId, connection.ToId, kind,
                TraversalReachability.Reachable, detail, travel, climb);
        }

        /// <summary>
        /// Problems with the level data that are not about the player's movement: missing or buried
        /// landmarks, patrols that walk off a ledge or into a wall, discoveries that would not be
        /// remembered.
        /// </summary>
        public List<string> StructuralProblems()
        {
            var problems = new List<string>();

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int playerStarts = 0;
            int exits = 0;

            for (int i = 0; i < _level.Entities.Count; i++)
            {
                LevelEntity entity = _level.Entities[i];
                if (!seenIds.Add(entity.Id)) problems.Add($"duplicate entity id '{entity.Id}'");

                if (entity.Position.x < 0 || entity.Position.x >= _level.Width
                    || entity.Position.y < 0 || entity.Position.y >= _level.Height)
                {
                    problems.Add($"entity '{entity.Id}' is outside the map");
                    continue;
                }

                if (entity.Kind == LevelEntityKind.PlayerStart) playerStarts++;
                if (entity.Kind == LevelEntityKind.Exit) exits++;

                if (!_level.IsSolidAt(entity.Position.x, entity.Position.y))
                {
                    problems.Add($"entity '{entity.Id}' stands on empty tile ({entity.Position.x},{entity.Position.y})");
                }
                else if (_level.IsSolidAt(entity.Position.x, entity.Position.y - 1))
                {
                    problems.Add($"entity '{entity.Id}' is buried: the tile above it is solid");
                }

                if (entity.Kind == LevelEntityKind.Checkpoint)
                {
                    if (!_level.IsSolidAt(entity.RespawnPosition.x, entity.RespawnPosition.y))
                    {
                        problems.Add($"checkpoint '{entity.Id}' respawns onto an empty tile");
                    }
                    else if (_level.IsSolidAt(entity.RespawnPosition.x, entity.RespawnPosition.y - 1))
                    {
                        problems.Add($"checkpoint '{entity.Id}' respawns into a solid tile");
                    }
                }

                if (entity.Kind == LevelEntityKind.Enemy)
                {
                    if (string.IsNullOrEmpty(entity.TypeId)) problems.Add($"enemy '{entity.Id}' declares no type");

                    for (int dx = -entity.PatrolTiles; dx <= entity.PatrolTiles; dx++)
                    {
                        int col = entity.Position.x + dx;
                        if (!_level.IsSolidAt(col, entity.Position.y))
                        {
                            problems.Add($"enemy '{entity.Id}' would patrol off the ground at column {col}");
                            break;
                        }
                        if (_level.IsSolidAt(col, entity.Position.y - 1))
                        {
                            problems.Add($"enemy '{entity.Id}' would walk into a wall at column {col}");
                            break;
                        }
                    }
                }

                if (entity.Kind == LevelEntityKind.Discovery && string.IsNullOrEmpty(entity.Flag))
                {
                    problems.Add($"discovery '{entity.Id}' declares no flag, so finding it would not persist");
                }
            }

            if (playerStarts != 1) problems.Add($"level must declare exactly one player start, found {playerStarts}");
            if (exits != 1) problems.Add($"level must declare exactly one exit, found {exits}");

            for (int i = 0; i < _level.Connections.Count; i++)
            {
                TraversalConnection connection = _level.Connections[i];
                if (_level.FindEntity(connection.FromId) == null)
                {
                    problems.Add($"traversal link references unknown id '{connection.FromId}'");
                }
                if (_level.FindEntity(connection.ToId) == null)
                {
                    problems.Add($"traversal link references unknown id '{connection.ToId}'");
                }
            }

            return problems;
        }

        /// <summary>
        /// Every standable tile the player can actually get to from the level's start.
        /// </summary>
        public HashSet<PlayableNode> ReachableTiles()
        {
            var visited = new HashSet<PlayableNode>();
            LevelEntity startEntity = _level.PlayerStart;
            if (startEntity == null) return visited;
            if (!TryFindNearestNode(startEntity.Position.x, startEntity.Position.y, out PlayableNode start))
            {
                return visited;
            }

            Dictionary<PlayableNode, List<TraversalEdge>> graph = BuildGraph();
            visited.Add(start);
            var stack = new Stack<PlayableNode>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                PlayableNode node = stack.Pop();
                if (!graph.TryGetValue(node, out List<TraversalEdge> outgoing)) continue;
                for (int i = 0; i < outgoing.Count; i++)
                {
                    if (visited.Add(outgoing[i].Target)) stack.Push(outgoing[i].Target);
                }
            }
            return visited;
        }

        /// <summary>
        /// Standable tiles the player can never reach. Wall tops are expected here; a tile inside
        /// the play space is a piece of level that has been sealed off by accident.
        /// </summary>
        public List<PlayableNode> UnreachableTiles()
        {
            HashSet<PlayableNode> reachable = ReachableTiles();
            var unreachable = new List<PlayableNode>();
            List<PlayableNode> nodes = StandNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!reachable.Contains(nodes[i])) unreachable.Add(nodes[i]);
            }
            return unreachable;
        }

        /// <summary>
        /// Unreachable tiles that look reachable, and so are level bugs rather than scenery.
        /// </summary>
        /// <remarks>
        /// A wall crown eight tiles above the only floor the player can stand on is architecture;
        /// a ledge one tile past a gap is a place the player will absolutely try to reach. The
        /// test is the tuning's own jump height: anything within one jump of reachable ground in
        /// any direction is somewhere the player will expect to get to.
        /// </remarks>
        public List<string> UnreachableProblems()
        {
            var problems = new List<string>();
            HashSet<PlayableNode> reachable = ReachableTiles();
            if (reachable.Count == 0) return problems;

            List<PlayableNode> nodes = StandNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                PlayableNode node = nodes[i];
                if (reachable.Contains(node)) continue;

                int gap = NearestReachableGap(node, reachable, out int col, out int row);
                if (gap > _tuning.JumpHeight + Epsilon) continue;

                problems.Add(
                    $"({node.Col},{node.Row}) is unreachable but looks reachable: the player can " +
                    $"stand on ({col},{row}) just {gap} tiles away, inside the " +
                    $"{_tuning.JumpHeight:0.##}-tile jump envelope, and still cannot get there");
            }
            return problems;
        }

        /// <summary>Unreachable tiles that are explained by being out of reach by design.</summary>
        public List<string> UnreachableNotes()
        {
            var notes = new List<string>();
            HashSet<PlayableNode> reachable = ReachableTiles();
            if (reachable.Count == 0) return notes;

            List<PlayableNode> nodes = StandNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                PlayableNode node = nodes[i];
                if (reachable.Contains(node)) continue;

                int gap = NearestReachableGap(node, reachable, out int col, out int row);
                if (gap <= _tuning.JumpHeight + Epsilon) continue;

                notes.Add($"({node.Col},{node.Row}): {gap} tiles from the nearest reachable " +
                          $"surface ({col},{row}) — further than the {_tuning.JumpHeight:0.##}-tile " +
                          "jump envelope, so it is out of reach by design rather than sealed off");
            }
            return notes;
        }

        /// <summary>
        /// Chebyshev distance to the nearest reachable tile: how far the player has to travel,
        /// which is the question they ask when they look at a ledge.
        /// </summary>
        private static int NearestReachableGap(PlayableNode node, HashSet<PlayableNode> reachable,
                                               out int col, out int row)
        {
            col = 0;
            row = 0;
            int best = int.MaxValue;
            foreach (PlayableNode other in reachable)
            {
                int dx = Math.Abs(other.Col - node.Col);
                int dy = Math.Abs(other.Row - node.Row);
                int gap = dx > dy ? dx : dy;
                if (gap >= best) continue;
                best = gap;
                col = other.Col;
                row = other.Row;
            }
            return best;
        }

        private readonly struct RouteCost
        {
            public RouteCost(int unfair, int jumps, float distance)
            {
                Unfair = unfair;
                Jumps = jumps;
                Distance = distance;
            }

            public int Unfair { get; }

            public int Jumps { get; }

            public float Distance { get; }

            public int CompareTo(RouteCost other)
            {
                int first = Unfair.CompareTo(other.Unfair);
                if (first != 0) return first;
                int second = Jumps.CompareTo(other.Jumps);
                if (second != 0) return second;
                return Distance.CompareTo(other.Distance);
            }
        }

        /// <summary>
        /// One simulated control sequence. The set of these is the model: it spans what the player
        /// can actually do, and it is small enough to be exhaustive and deterministic.
        /// </summary>
        private readonly struct ControlProfile
        {
            public ControlProfile(string name, float speedScale, bool cutJump, string horizontal)
            {
                Name = name;
                SpeedScale = speedScale;
                CutJump = cutJump;
                Horizontal = horizontal;
            }

            public string Name { get; }

            public float SpeedScale { get; }

            public bool CutJump { get; }

            public string Horizontal { get; }

            public static readonly ControlProfile[] All = Build();

            private static ControlProfile[] Build()
            {
                var profiles = new List<ControlProfile>(12);
                float[] speeds = { 1f, 0.5f };
                bool[] cuts = { false, true };
                string[] horizontals = { "hold", "brake", "ramp" };
                for (int s = 0; s < speeds.Length; s++)
                {
                    for (int c = 0; c < cuts.Length; c++)
                    {
                        for (int h = 0; h < horizontals.Length; h++)
                        {
                            string speedName = speeds[s] == 1f ? "full" : "half";
                            string jumpName = cuts[c] ? "cut" : "full";
                            string name = $"{speedName}-speed/{jumpName}-jump/{horizontals[h]}";
                            profiles.Add(new ControlProfile(name, speeds[s], cuts[c], horizontals[h]));
                        }
                    }
                }
                return profiles.ToArray();
            }
        }
    }
}
