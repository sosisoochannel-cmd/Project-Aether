using System;
using System.IO;
using UnityEngine;

namespace Aether.Gameplay.Storage
{
    /// <summary>
    /// One JSON document on disk, written so that a crash cannot leave it half-written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settings and the save file are different things with the same problem — a small object that
    /// has to survive a phone killing the app mid-write — so they share this and nothing else. The
    /// write goes to a sibling temporary file, which is flushed and then moved over the real one:
    /// on every platform this project targets, a rename within a directory either happened or did
    /// not, so the reader never sees a truncated document.
    /// </para>
    /// <para>
    /// Every method returns a bool instead of throwing. Losing a settings write is an inconvenience;
    /// a settings write that takes the game down on launch is a bug the player cannot work around.
    /// </para>
    /// </remarks>
    public sealed class JsonFileStore
    {
        private readonly string _folder;
        private readonly string _fileName;

        /// <param name="folder">Folder under the platform's persistent data path.</param>
        /// <param name="fileName">File name, including extension.</param>
        public JsonFileStore(string folder, string fileName)
        {
            _folder = folder ?? string.Empty;
            _fileName = fileName ?? "data.json";
        }

        /// <summary>Absolute path of the document.</summary>
        public string Path => System.IO.Path.Combine(Root, _fileName);

        /// <summary>Absolute path of the folder the document lives in.</summary>
        public string Root => System.IO.Path.Combine(Application.persistentDataPath, _folder);

        /// <summary>True when a document exists right now.</summary>
        public bool Exists
        {
            get
            {
                try
                {
                    return File.Exists(Path);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Reads and parses the document. False when there is none, or it is unusable.</summary>
        public bool TryRead<T>(out T value) where T : class
        {
            value = null;

            try
            {
                if (!File.Exists(Path)) return false;
                string text = File.ReadAllText(Path);
                if (string.IsNullOrEmpty(text)) return false;

                value = JsonUtility.FromJson<T>(text);
                return value != null;
            }
            catch (Exception)
            {
                // A corrupt or unreadable file is indistinguishable, from here, from no file at all:
                // both mean "start fresh". Nothing is deleted, so a support conversation can still
                // look at what the player had.
                return false;
            }
        }

        /// <summary>Writes the document. False when the write failed.</summary>
        public bool Write<T>(T value) where T : class
        {
            if (value == null) return false;

            string temporary = _fileName + ".tmp";
            string temporaryPath = System.IO.Path.Combine(Root, temporary);

            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(value, true));

                if (File.Exists(Path))
                {
                    // Replace is atomic on the platforms that have it and unavailable on some
                    // Android filesystems; the fallback is a delete and a move, which is a much
                    // smaller window than writing the real file in place.
                    try
                    {
                        File.Replace(temporaryPath, Path, null);
                    }
                    catch (Exception)
                    {
                        File.Delete(Path);
                        File.Move(temporaryPath, Path);
                    }
                }
                else
                {
                    File.Move(temporaryPath, Path);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Deletes the document, and any temporary left behind. False when that failed.</summary>
        public bool Delete()
        {
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
                string leftover = System.IO.Path.Combine(Root, _fileName + ".tmp");
                if (File.Exists(leftover)) File.Delete(leftover);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>How this store reads in a diagnostics line.</summary>
        public override string ToString()
        {
            return _fileName + " in " + Root;
        }
    }
}
