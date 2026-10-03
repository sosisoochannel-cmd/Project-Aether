using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aether.Data.Levels
{
    /// <summary>
    /// A parsed level: the grid, the things placed in it, and the traversal claims made about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <b>source of truth</b> for a region. The runtime builds colliders and visuals from
    /// it, the Editor bake tool turns it into Tilemaps, and the traversal solver proves it is
    /// playable. A scene is never edited by hand: the next bake would overwrite it.
    /// </para>
    /// <para>
    /// Coordinates. Tiles are indexed <c>(col, row)</c> with <b>row 0 at the top</b>, matching the
    /// textual format. Tiles are <see cref="TileSize"/> world units. An entity standing on tile
    /// <c>(col, row)</c> has its feet on the top edge of that tile, which is what
    /// <see cref="TileBottomCenter"/> returns. Conversion lives here and nowhere else, so the
    /// vertical flip cannot be re-derived incorrectly somewhere else.
    /// </para>
    /// </remarks>
    public sealed class LevelData
    {
        private readonly LevelTileKind[] _tiles;
        private readonly List<LevelEntity> _entities;
        private readonly List<TraversalConnection> _connections;

        public LevelData(string id, string displayName, string region, float tileSize,
                         int width, int height, LevelTileKind[] tiles,
                         List<LevelEntity> entities, List<TraversalConnection> connections)
        {
            Id = id;
            DisplayName = displayName;
            Region = region;
            TileSize = tileSize <= 0f ? 1f : tileSize;
            Width = width;
            Height = height;
            _tiles = tiles;
            _entities = entities;
            _connections = connections;
        }

        /// <summary>Stable level id, for example <c>region1.greenway</c>.</summary>
        public string Id { get; }

        /// <summary>Name shown to players.</summary>
        public string DisplayName { get; }

        /// <summary>Region this level belongs to.</summary>
        public string Region { get; }

        /// <summary>Edge length of one tile, in world units.</summary>
        public float TileSize { get; }

        /// <summary>Grid width in tiles.</summary>
        public int Width { get; }

        /// <summary>Grid height in tiles.</summary>
        public int Height { get; }

        /// <summary>Everything placed in the level, in file order.</summary>
        public IReadOnlyList<LevelEntity> Entities => _entities;

        /// <summary>Traversal links the level claims the player can make.</summary>
        public IReadOnlyList<TraversalConnection> Connections => _connections;

        /// <summary>Total world size of the level.</summary>
        public Vector2 WorldSize => new Vector2(Width * TileSize, Height * TileSize);

        /// <summary>
        /// World rectangle covering the whole grid, used for camera bounds and for deciding when a
        /// falling body has left the level.
        /// </summary>
        public Rect WorldBounds => new Rect(0f, 0f, Width * TileSize, Height * TileSize);

        /// <summary>Tile kind at a cell. Outside the grid is empty: the level simply ends.</summary>
        public LevelTileKind KindAt(int col, int row)
        {
            if (col < 0 || col >= Width || row < 0 || row >= Height) return LevelTileKind.Empty;
            return _tiles[(row * Width) + col];
        }

        /// <summary>True when a cell stops movement.</summary>
        public bool IsSolidAt(int col, int row) => LevelTileTraits.IsSolid(KindAt(col, row));

        /// <summary>True when a body can stand on top of a cell.</summary>
        public bool IsStandableAt(int col, int row) => LevelTileTraits.SupportsStanding(KindAt(col, row));

        /// <summary>
        /// World position of the bottom centre of a tile: where a character's feet rest when it
        /// stands on <c>(col, row)</c>.
        /// </summary>
        public Vector2 TileBottomCenter(int col, int row)
        {
            return new Vector2((col + 0.5f) * TileSize, (Height - row) * TileSize);
        }

        /// <summary>World position of the centre of a tile.</summary>
        public Vector2 TileCenter(int col, int row)
        {
            return new Vector2((col + 0.5f) * TileSize, (Height - row - 0.5f) * TileSize);
        }

        /// <summary>Grid cell containing a world position. Rows increase downwards.</summary>
        public Vector2Int CellAt(Vector2 world)
        {
            int col = Mathf.FloorToInt(world.x / TileSize);
            int row = Mathf.FloorToInt((Height * TileSize) - world.y);
            return new Vector2Int(col, row);
        }

        /// <summary>The entity with this id, or null.</summary>
        public LevelEntity FindEntity(string id)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Id == id) return _entities[i];
            }
            return null;
        }

        /// <summary>All entities of one kind. Allocates, so call it during setup, not per frame.</summary>
        public List<LevelEntity> EntitiesOfKind(LevelEntityKind kind)
        {
            var found = new List<LevelEntity>();
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Kind == kind) found.Add(_entities[i]);
            }
            return found;
        }

        /// <summary>The single player start, or null when the level is invalid.</summary>
        public LevelEntity PlayerStart
        {
            get
            {
                for (int i = 0; i < _entities.Count; i++)
                {
                    if (_entities[i].Kind == LevelEntityKind.PlayerStart) return _entities[i];
                }
                return null;
            }
        }

        /// <summary>The single exit, or null when the level is invalid.</summary>
        public LevelEntity Exit
        {
            get
            {
                for (int i = 0; i < _entities.Count; i++)
                {
                    if (_entities[i].Kind == LevelEntityKind.Exit) return _entities[i];
                }
                return null;
            }
        }

        /// <summary>
        /// Greedy merge of same-kind solid cells into maximal rectangles.
        /// </summary>
        /// <remarks>
        /// The runtime uses this to build one collider and one renderer per rectangle instead of one
        /// per tile: The Greenway's roughly two thousand solid cells become a few dozen objects. It
        /// is deliberately a simple row-wise greedy, not a minimal-cover solver, because the result
        /// only has to be small and stable — and being stable matters, since the bake produces a
        /// Tilemap from the same grid and the two must never disagree about where ground is.
        /// </remarks>
        public List<LevelRect> BuildSolidRects()
        {
            return BuildRects(LevelTileTraits.IsSolid);
        }

        /// <summary>Greedy merge of every cell of one exact kind, for drawing non-solid layers.</summary>
        public List<LevelRect> BuildRects(LevelTileKind onlyKind)
        {
            return BuildRects(kind => kind == onlyKind);
        }

        /// <summary>Greedy merge of every cell the predicate accepts.</summary>
        public List<LevelRect> BuildRects(Func<LevelTileKind, bool> include)
        {
            var rects = new List<LevelRect>();
            var used = new bool[Width * Height];
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (used[(row * Width) + col]) continue;
                    LevelTileKind kind = KindAt(col, row);
                    if (!include(kind)) continue;

                    int width = 1;
                    while (col + width < Width
                           && !used[(row * Width) + col + width]
                           && KindAt(col + width, row) == kind)
                    {
                        width++;
                    }

                    int height = 1;
                    bool canGrow = true;
                    while (canGrow && row + height < Height)
                    {
                        for (int c = col; c < col + width; c++)
                        {
                            if (used[((row + height) * Width) + c] || KindAt(c, row + height) != kind)
                            {
                                canGrow = false;
                                break;
                            }
                        }
                        if (canGrow) height++;
                    }

                    for (int r = row; r < row + height; r++)
                    {
                        for (int c = col; c < col + width; c++) used[(r * Width) + c] = true;
                    }

                    rects.Add(new LevelRect(col, row, width, height, kind));
                }
            }
            return rects;
        }
    }

    /// <summary>A run of same-kind solid tiles, in tile coordinates.</summary>
    public readonly struct LevelRect
    {
        public LevelRect(int col, int row, int width, int height, LevelTileKind kind)
        {
            Col = col;
            Row = row;
            Width = width;
            Height = height;
            Kind = kind;
        }

        public int Col { get; }

        public int Row { get; }

        public int Width { get; }

        public int Height { get; }

        public LevelTileKind Kind { get; }

        /// <summary>Centre of the rectangle in world space, assuming it is drawn centred on its own pivot.</summary>
        public Vector2 WorldCenter(float tileSize, int levelHeight)
        {
            float x = (Col + (Width * 0.5f)) * tileSize;
            float y = (levelHeight - Row - (Height * 0.5f)) * tileSize;
            return new Vector2(x, y);
        }

        /// <summary>Size of the rectangle in world units.</summary>
        public Vector2 WorldSize(float tileSize) => new Vector2(Width * tileSize, Height * tileSize);
    }

    /// <summary>Raised when level text cannot be understood. Carries the line number for the message.</summary>
    public sealed class LevelParseException : Exception
    {
        public LevelParseException(string source, int line, string message)
            : base($"{source}:{line}: {message}")
        {
            Source = source;
            Line = line;
        }

        public new string Source { get; }

        public int Line { get; }
    }
}
