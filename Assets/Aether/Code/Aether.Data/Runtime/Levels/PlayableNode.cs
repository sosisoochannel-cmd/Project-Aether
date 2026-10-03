
namespace Aether.Data.Levels
{
    /// <summary>One standable tile in the level grid.</summary>
    public readonly struct PlayableNode
    {
        public PlayableNode(int col, int row)
        {
            Col = col;
            Row = row;
        }

        public int Col { get; }

        public int Row { get; }

        public bool Equals(PlayableNode other)
        {
            return Col == other.Col && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayableNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = (Col * 397) ^ Row;
            return hash;
        }

        public override string ToString()
        {
            return $"({Col},{Row})";
        }
    }
}
