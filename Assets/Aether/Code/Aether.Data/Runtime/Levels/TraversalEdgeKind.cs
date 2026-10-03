
namespace Aether.Data.Levels
{
    /// <summary>How the player gets from one standable tile to another.</summary>
    public enum TraversalEdgeKind
    {
        /// <summary>Ordinary running along level ground, or a one-tile step down. No jump input.</summary>
        Walk = 0,

        /// <summary>Walking off an edge and falling further than a step.</summary>
        Drop = 1,

        /// <summary>A jump.</summary>
        Leap = 2,
    }
}
