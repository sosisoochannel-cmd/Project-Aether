
namespace Aether.Data.Levels
{
    /// <summary>One route step, with the numbers that justify it.</summary>
    public readonly struct TraversalEdge
    {
        public TraversalEdge(PlayableNode target, TraversalEdgeKind kind, string profile,
                             float span, float rise, float reach)
        {
            Target = target;
            Kind = kind;
            Profile = profile;
            Span = span;
            Rise = rise;
            Reach = reach;
        }

        public PlayableNode Target { get; }

        public TraversalEdgeKind Kind { get; }

        /// <summary>Name of the simulated control sequence. Diagnostic only.</summary>
        public string Profile { get; }

        /// <summary>Horizontal travel this move demands, in world units.</summary>
        public float Span { get; }

        /// <summary>Vertical travel, positive when climbing.</summary>
        public float Rise { get; }

        /// <summary>Farthest the player can come to rest in this direction from the same tile.</summary>
        public float Reach { get; }

        /// <summary>Jumps this move costs. Only a jump leaves the ground deliberately.</summary>
        public int Jumps => Kind == TraversalEdgeKind.Walk ? 0 : 1;

        /// <summary>Fraction of the player's reach left unused. Only jumps spend it.</summary>
        public float Margin
        {
            get
            {
                if (Kind != TraversalEdgeKind.Leap || Reach <= 0f) return 1f;
                return (Reach - Span) / Reach;
            }
        }
    }
}
