namespace Aether.Data.Levels
{
    /// <summary>
    /// A traversal link the level claims exists, together with the author's reason for believing
    /// it. The solver verifies the claim; nothing else trusts it.
    /// </summary>
    public sealed class TraversalConnection
    {
        public TraversalConnection(string fromId, string toId, bool mustWalk, bool oneWay, string note)
        {
            FromId = fromId;
            ToId = toId;
            MustWalk = mustWalk;
            OneWay = oneWay;
            Note = note;
        }

        /// <summary>Id of the entity the player travels from.</summary>
        public string FromId { get; }

        /// <summary>Id of the entity the player must be able to reach.</summary>
        public string ToId { get; }

        /// <summary>
        /// When true the link must be traversable with no jump input at all. This is how a level
        /// claims "the player is taught to walk here before being asked to jump".
        /// </summary>
        public bool MustWalk { get; }

        /// <summary>
        /// When true the author has declared that the player is not expected to come back. Any
        /// link that turns out to be one-way without this flag is reported as ambiguous.
        /// </summary>
        public bool OneWay { get; }

        /// <summary>Free-form author note explaining why this link exists. Never gameplay-relevant.</summary>
        public string Note { get; }
    }

    /// <summary>Result state of a traversal check.</summary>
    public enum TraversalReachability
    {
        /// <summary>The solver simulated the player and arrived. Safe to build on.</summary>
        Reachable = 0,

        /// <summary>A route exists geometrically, but the player cannot make it with the current tuning.</summary>
        Unreachable = 1,

        /// <summary>Geometry is missing or wrong: the destination is not standable, the start is invalid, or the route is sealed.</summary>
        Blocked = 2,

        /// <summary>
        /// Traversable only once the player gains an ability that does not exist yet. Reachability
        /// gating is expressed in data; this state exists so a future ability is reported honestly
        /// rather than being silently accepted or silently dropped.
        /// </summary>
        RequiresAbility = 3,

        /// <summary>
        /// More than one outcome applies, or the author's declared kind disagrees with what the
        /// solver measured. The level must be re-authored: a connection that is ambiguous cannot
        /// be tested, so it cannot be trusted.
        /// </summary>
        Ambiguous = 4,
    }

    /// <summary>One connection's verdict, with the numbers behind it.</summary>
    public sealed class TraversalReport
    {
        public TraversalReport(string fromId, string toId, string kind, TraversalReachability state,
                               string detail, float horizontalDistance, float rise)
        {
            FromId = fromId;
            ToId = toId;
            Kind = kind;
            State = state;
            Detail = detail;
            HorizontalDistance = horizontalDistance;
            Rise = rise;
        }

        public string FromId { get; }

        public string ToId { get; }

        /// <summary>The kind the author declared in the level data.</summary>
        public string Kind { get; }

        public TraversalReachability State { get; }

        /// <summary>Human-readable evidence. Always populated, including for successes.</summary>
        public string Detail { get; }

        /// <summary>
        /// Signed horizontal travel the player must cover, measured between standable support
        /// surfaces rather than between entity origins.
        /// </summary>
        public float HorizontalDistance { get; }

        /// <summary>Vertical travel, positive when climbing.</summary>
        public float Rise { get; }

        /// <summary>True when the level may rely on this link.</summary>
        public bool IsUsable => State == TraversalReachability.Reachable;

        /// <summary>Single-line form used by the verifier and by the bake tool's summary.</summary>
        public string Describe()
        {
            return $"{FromId} -> {ToId} [{Kind}] {State} " +
                   $"(dx {HorizontalDistance:0.##}, dy {Rise:0.##}): {Detail}";
        }
    }
}
