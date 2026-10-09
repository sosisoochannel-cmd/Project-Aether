using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Aether.Data.Levels
{
    /// <summary>
    /// Reads the textual level format into <see cref="LevelData"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The format is line-oriented and deliberately strict: <b>a malformed line is an error, never a
    /// warning</b>. Silently skipping a line is how a piece of a level disappears without anyone
    /// noticing, and a level that is subtly missing its exit is far more expensive to find than one
    /// that refuses to load. Comments are allowed between sections but not inside <c>[tiles]</c> or
    /// <c>[legend]</c>, where '#' is a legal tile character.
    /// </para>
    /// <para>
    /// The same rules exist in <c>tools/verify/levelcheck.py</c>, which is what the static gate runs.
    /// Two readers of one format is a cost paid on purpose: the gate has to be able to read the files
    /// without Unity, and a second implementation that must agree is how the format itself gets
    /// tested. They are kept in step by <c>LevelParserTests</c>, which asserts the exact same
    /// strings are accepted and rejected here as the Python tool accepts and rejects.
    /// </para>
    /// </remarks>
    public static class LevelParser
    {
        /// <summary>Format version this parser understands.</summary>
        public const int SupportedVersion = 1;

        private const string HeaderToken = "aether-level";

        private static readonly Dictionary<string, LevelEntityKind> EntityTokens =
            new Dictionary<string, LevelEntityKind>(StringComparer.Ordinal)
            {
                { "player_start", LevelEntityKind.PlayerStart },
                { "checkpoint", LevelEntityKind.Checkpoint },
                { "enemy", LevelEntityKind.Enemy },
                { "discovery", LevelEntityKind.Discovery },
                { "story", LevelEntityKind.StoryMarker },
                { "exit", LevelEntityKind.Exit },
                { "anchor", LevelEntityKind.Anchor },
            };

        private static readonly HashSet<string> EntityAttributeTokens =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "x", "y", "type", "kind", "flag", "note", "patrol", "respawn",
            };

        private static readonly HashSet<string> MetaAttributeTokens =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "display_name", "region", "tile_size",
            };

        /// <summary>Parses level text. Throws <see cref="LevelParseException"/> on the first problem.</summary>
        public static LevelData Parse(string text, string sourceName)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new LevelParseException(sourceName, 1, "level text is empty");
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            var legend = new Dictionary<char, LevelTileKind>();
            var rows = new List<List<LevelTileKind>>();
            var entities = new List<LevelEntity>();
            var entityIds = new HashSet<string>(StringComparer.Ordinal);
            var entityLineNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
            var connections = new List<TraversalConnection>();

            string section = null;
            bool sawHeader = false;
            bool sawEntities = false;

            for (int index = 0; index < lines.Length; index++)
            {
                int lineNumber = index + 1;
                string line = lines[index];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2);
                    if (section == "entities") sawEntities = true;
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    if (section == "tiles")
                    {
                        int next = index + 1;
                        while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
                        bool sectionFollows = next < lines.Length
                            && lines[next].TrimStart().StartsWith("[", StringComparison.Ordinal)
                            && lines[next].TrimEnd().EndsWith("]", StringComparison.Ordinal);
                        if (next < lines.Length && !sectionFollows)
                        {
                            throw new LevelParseException(sourceName, lineNumber,
                                "tile row is empty; use tile characters for empty cells");
                        }
                    }
                    continue;
                }

                if (section == "tiles")
                {
                    rows.Add(ParseRow(sourceName, lineNumber, line, legend));
                    continue;
                }

                if (trimmed[0] == '#' && section != "legend")
                {
                    continue;
                }

                if (!sawHeader)
                {
                    string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || parts[0] != HeaderToken)
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"expected '{HeaderToken} <version>', got '{trimmed}'");
                    }
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"level version is not a number: '{parts[1]}'");
                    }
                    if (version != SupportedVersion)
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"level format version {version} is not supported");
                    }
                    sawHeader = true;
                    continue;
                }

                if (section == "legend")
                {
                    // The key is a single character and '=' itself is a legal key, so this cannot be
                    // split on the first '=' the way [meta] is.
                    char key = trimmed[0];
                    string rest = trimmed.Substring(1).TrimStart();
                    if (rest.Length == 0 || rest[0] != '=')
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"expected '<char> = <Kind>', got '{trimmed}'");
                    }
                    string kindName = rest.Substring(1).Trim();
                    if (!Enum.TryParse(kindName, out LevelTileKind kind) || !Enum.IsDefined(typeof(LevelTileKind), kind))
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"unknown tile kind '{kindName}'");
                    }
                    if (legend.ContainsKey(key))
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"legend symbol '{key}' is declared more than once");
                    }
                    legend[key] = kind;
                    continue;
                }

                if (section == "meta")
                {
                    int split = trimmed.IndexOf('=');
                    if (split < 0)
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"expected 'key = value', got '{trimmed}'");
                    }
                    string metaKey = trimmed.Substring(0, split).Trim();
                    string metaValue = trimmed.Substring(split + 1).Trim();
                    if (!MetaAttributeTokens.Contains(metaKey))
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"unknown metadata key '{metaKey}'");
                    }
                    if (meta.ContainsKey(metaKey))
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"metadata key '{metaKey}' is declared more than once");
                    }
                    meta[metaKey] = metaValue;
                    continue;
                }

                if (section == "entities")
                {
                    LevelEntity entity = ParseEntity(sourceName, lineNumber, trimmed);
                    if (!entityIds.Add(entity.Id))
                    {
                        throw new LevelParseException(sourceName, lineNumber,
                            $"duplicate entity id '{entity.Id}'");
                    }
                    entities.Add(entity);
                    entityLineNumbers.Add(entity.Id, lineNumber);
                    continue;
                }

                if (section == "validation")
                {
                    connections.Add(ParseConnection(sourceName, lineNumber, trimmed));
                    continue;
                }

                throw new LevelParseException(sourceName, lineNumber,
                    $"line is not inside a known section: '{trimmed}'");
            }

            if (!sawHeader) throw new LevelParseException(sourceName, 1, "missing 'aether-level 1' header");
            if (rows.Count == 0) throw new LevelParseException(sourceName, 1, "the [tiles] section is empty");
            if (!sawEntities) throw new LevelParseException(sourceName, 1, "missing the [entities] section");
            if (!meta.TryGetValue("id", out string id) || string.IsNullOrEmpty(id))
            {
                throw new LevelParseException(sourceName, 1, "the [meta] section must declare an id");
            }
            if (legend.Count == 0) throw new LevelParseException(sourceName, 1, "the [legend] section is empty");

            int width = rows[0].Count;
            for (int i = 0; i < entities.Count; i++)
            {
                LevelEntity entity = entities[i];
                int entityLine = entityLineNumbers[entity.Id];
                if (entity.Position.x < 0 || entity.Position.x >= width
                    || entity.Position.y < 0 || entity.Position.y >= rows.Count)
                {
                    throw new LevelParseException(sourceName, entityLine,
                        $"entity '{entity.Id}' is outside the map");
                }

                if (entity.Kind == LevelEntityKind.Checkpoint
                    && (entity.RespawnPosition.x < 0 || entity.RespawnPosition.x >= width
                        || entity.RespawnPosition.y < 0 || entity.RespawnPosition.y >= rows.Count))
                {
                    throw new LevelParseException(sourceName, entityLine,
                        $"checkpoint '{entity.Id}' respawn is outside the map");
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Count != width)
                {
                    throw new LevelParseException(sourceName, 1,
                        $"tile row {i} has {rows[i].Count} columns, expected {width}");
                }
            }

            var tiles = new LevelTileKind[width * rows.Count];
            for (int row = 0; row < rows.Count; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    tiles[(row * width) + col] = rows[row][col];
                }
            }

            float tileSize = 1f;
            if (meta.TryGetValue("tile_size", out string tileSizeText))
            {
                if (!float.TryParse(tileSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out tileSize)
                    || tileSize <= 0f)
                {
                    throw new LevelParseException(sourceName, 1, $"tile_size '{tileSizeText}' is not a positive number");
                }
            }

            meta.TryGetValue("display_name", out string displayName);
            meta.TryGetValue("region", out string region);

            return new LevelData(
                id,
                string.IsNullOrEmpty(displayName) ? id : displayName,
                string.IsNullOrEmpty(region) ? "unknown" : region,
                tileSize,
                width,
                rows.Count,
                tiles,
                entities,
                connections);
        }

        private static List<LevelTileKind> ParseRow(string source, int line, string text, Dictionary<char, LevelTileKind> legend)
        {
            string clean = text.TrimEnd('\r');
            var row = new List<LevelTileKind>(clean.Length);
            for (int i = 0; i < clean.Length; i++)
            {
                char c = clean[i];
                if (!legend.TryGetValue(c, out LevelTileKind kind))
                {
                    throw new LevelParseException(source, line, $"character '{c}' is not in the [legend]");
                }
                row.Add(kind);
            }
            return row;
        }

        private static LevelEntity ParseEntity(string source, int line, string text)
        {
            int split = text.IndexOf('=');
            if (split < 0) throw new LevelParseException(source, line, $"entity line has no '=': '{text}'");

            string token = text.Substring(0, split).Trim();
            if (!EntityTokens.TryGetValue(token, out LevelEntityKind kind))
            {
                throw new LevelParseException(source, line, $"unknown entity kind '{token}'");
            }

            string[] pieces = text.Substring(split + 1).Split(',');
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < pieces.Length; i++)
            {
                string piece = pieces[i].Trim();
                if (piece.Length == 0) throw new LevelParseException(source, line, $"empty attribute in '{text}'");

                int eq = piece.IndexOf('=');
                if (eq < 0) throw new LevelParseException(source, line, $"attribute '{piece}' is not 'key=value'");

                string key = piece.Substring(0, eq).Trim();
                string value = piece.Substring(eq + 1).Trim();
                if (attributes.ContainsKey(key))
                {
                    throw new LevelParseException(source, line, $"attribute '{key}' given twice");
                }
                attributes[key] = value;
            }

            foreach (string attribute in attributes.Keys)
            {
                if (!EntityAttributeTokens.Contains(attribute))
                {
                    throw new LevelParseException(source, line,
                        $"{token} has unknown attribute '{attribute}'");
                }

                bool allowedForKind = attribute == "id" || attribute == "x"
                    || attribute == "y" || attribute == "note";
                if (kind == LevelEntityKind.Checkpoint && attribute == "respawn") allowedForKind = true;
                if (kind == LevelEntityKind.Enemy
                    && (attribute == "type" || attribute == "patrol")) allowedForKind = true;
                if (kind == LevelEntityKind.Discovery
                    && (attribute == "kind" || attribute == "flag")) allowedForKind = true;
                if (kind == LevelEntityKind.StoryMarker && attribute == "kind") allowedForKind = true;

                if (!allowedForKind)
                {
                    throw new LevelParseException(source, line,
                        $"{token} does not support attribute '{attribute}'");
                }
            }

            if (!attributes.TryGetValue("id", out string id) || string.IsNullOrEmpty(id))
            {
                throw new LevelParseException(source, line, $"{token} has no id");
            }

            int col = RequireInt(source, line, attributes, "x", id);
            int row = RequireInt(source, line, attributes, "y", id);
            var entity = new LevelEntity(kind, id, new Vector2Int(col, row));

            attributes.TryGetValue("type", out string typeId);
            attributes.TryGetValue("kind", out string markerKind);
            attributes.TryGetValue("flag", out string flag);
            attributes.TryGetValue("note", out string note);
            entity.TypeId = typeId;
            entity.MarkerKind = markerKind;
            entity.Flag = flag;
            entity.Note = note;

            if (kind == LevelEntityKind.Enemy && string.IsNullOrEmpty(typeId))
            {
                throw new LevelParseException(source, line, $"enemy '{id}' has no type");
            }
            if (kind == LevelEntityKind.Discovery && string.IsNullOrEmpty(flag))
            {
                throw new LevelParseException(source, line, $"discovery '{id}' has no flag");
            }
            if (kind == LevelEntityKind.Checkpoint && !attributes.ContainsKey("respawn"))
            {
                throw new LevelParseException(source, line, $"checkpoint '{id}' has no respawn");
            }

            if (attributes.TryGetValue("patrol", out string patrolText))
            {
                if (!int.TryParse(patrolText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int patrol) || patrol < 0)
                {
                    throw new LevelParseException(source, line, $"enemy '{id}' patrol '{patrolText}' must be a non-negative integer");
                }
                entity.PatrolTiles = patrol;
            }

            entity.RespawnPosition = entity.Position;
            if (attributes.TryGetValue("respawn", out string respawnText))
            {
                // 'x:y', not 'x,y': a comma already separates attributes.
                string[] parts = respawnText.Split(':');
                if (parts.Length != 2)
                {
                    throw new LevelParseException(source, line, $"checkpoint '{id}' respawn '{respawnText}' is not 'x:y'");
                }
                if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rx)
                    || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ry))
                {
                    throw new LevelParseException(source, line, $"checkpoint '{id}' respawn '{respawnText}' is not numeric");
                }
                entity.RespawnPosition = new Vector2Int(rx, ry);
            }

            return entity;
        }

        private static int RequireInt(string source, int line, Dictionary<string, string> attributes, string key, string id)
        {
            if (!attributes.TryGetValue(key, out string text)
                || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new LevelParseException(source, line, $"entity '{id}' has no valid '{key}'");
            }
            return value;
        }

        private static TraversalConnection ParseConnection(string source, int line, string text)
        {
            int split = text.IndexOf('=');
            if (split < 0) throw new LevelParseException(source, line, $"validation entry has no '=': '{text}'");

            string token = text.Substring(0, split).Trim();
            if (token != "reach")
            {
                throw new LevelParseException(source, line, $"unknown validation entry '{token}'");
            }

            string[] pieces = text.Substring(split + 1).Split(',');
            string link = pieces[0].Trim();
            int arrow = link.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) throw new LevelParseException(source, line, $"reach entry needs 'from -> to', got '{link}'");

            string from = link.Substring(0, arrow).Trim();
            string to = link.Substring(arrow + 2).Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                throw new LevelParseException(source, line, $"reach entry has an empty endpoint: '{link}'");
            }

            bool mustWalk = false;
            bool oneWay = false;
            for (int i = 1; i < pieces.Length; i++)
            {
                string flag = pieces[i].Trim();
                if (flag.Length == 0) continue;
                if (flag == "must=walk") mustWalk = true;
                else if (flag == "oneway") oneWay = true;
                else throw new LevelParseException(source, line, $"unknown reach flag '{flag}'");
            }

            return new TraversalConnection(from, to, mustWalk, oneWay, null);
        }
    }
}
