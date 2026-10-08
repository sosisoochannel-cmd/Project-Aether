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
    /// Settings and saves share the same small storage primitive: a sibling temporary file is
    /// completed before it replaces the real document. Where an atomic replace is unavailable, the
    /// old file is moved to a recovery sibling before the new one is moved into place. A reader checks
    /// that recovery file if the primary file is missing or unreadable, so an interrupted fallback
    /// does not turn a saved run into an empty slot.
    /// </para>
    /// <para>
    /// Methods report failure instead of throwing. In particular, a failed replacement never deletes
    /// the only readable copy of the previous document.
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

        private string BackupPath => Path + ".bak";

        /// <summary>True when a primary document or a recoverable backup exists.</summary>
        public bool Exists
        {
            get
            {
                try
                {
                    return File.Exists(Path) || File.Exists(BackupPath);
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
            if (TryReadPath(Path, out value)) return true;
            if (TryReadPath(BackupPath, out value)) return true;

            value = null;
            return false;
        }

        private static bool TryReadPath<T>(string path, out T value) where T : class
        {
            value = null;

            try
            {
                if (!File.Exists(path)) return false;
                string text = File.ReadAllText(path);
                if (string.IsNullOrEmpty(text)) return false;

                value = JsonUtility.FromJson<T>(text);
                return value != null;
            }
            catch (Exception)
            {
                // A damaged primary is not silently treated as an empty slot: TryRead checks the
                // recovery sibling next, and callers can still report a truly unreadable document.
                value = null;
                return false;
            }
        }

        /// <summary>Writes the document. False when the write failed.</summary>
        public bool Write<T>(T value) where T : class
        {
            if (value == null) return false;

            string temporaryPath = Path + ".tmp";
            string backupPath = BackupPath;

            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(value, true));

                if (!File.Exists(Path))
                {
                    // A backup may be the only readable copy after an interrupted prior replace.
                    // Keep it until the new primary is in place.
                    File.Move(temporaryPath, Path);
                    TryDelete(backupPath);
                    return true;
                }

                if (TryReadPath<T>(Path, out _))
                {
                    // The primary is good, so an older recovery file can be discarded before the
                    // next atomic replace. If that deletion fails, the outer catch leaves the good
                    // primary untouched and reports failure.
                    if (File.Exists(backupPath)) File.Delete(backupPath);

                    try
                    {
                        File.Replace(temporaryPath, Path, backupPath);
                        TryDelete(backupPath);
                        return true;
                    }
                    catch (Exception)
                    {
                        // File.Replace is not supported by every Android filesystem. The move
                        // fallback below preserves the old primary until the new file is ready.
                        return MoveThroughBackup(temporaryPath, backupPath);
                    }
                }

                // The main document is damaged. Preserve a readable backup throughout the repair;
                // if moving the new file fails, TryRead can still recover from that backup.
                if (TryReadPath<T>(backupPath, out _))
                {
                    File.Delete(Path);
                    File.Move(temporaryPath, Path);
                    TryDelete(backupPath);
                    return true;
                }

                // Neither copy parses. Retain the damaged primary until the temporary is complete,
                // then use the same recoverable move protocol as for an unsupported atomic replace.
                if (File.Exists(backupPath)) File.Delete(backupPath);
                return MoveThroughBackup(temporaryPath, backupPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool MoveThroughBackup(string temporaryPath, string backupPath)
        {
            bool movedPrimary = false;

            try
            {
                if (File.Exists(Path))
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Move(Path, backupPath);
                    movedPrimary = true;
                }

                File.Move(temporaryPath, Path);
                TryDelete(backupPath);
                return true;
            }
            catch (Exception)
            {
                // Restore the previous document when possible. If the process is interrupted here,
                // the backup remains discoverable by TryRead instead of being thrown away.
                if (movedPrimary && !File.Exists(Path) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Move(backupPath, Path);
                    }
                    catch (Exception)
                    {
                        // Leave the recovery sibling in place for the next read.
                    }
                }

                return false;
            }
        }

        /// <summary>Deletes the document, recovery copy and any temporary left behind.</summary>
        public bool Delete()
        {
            bool succeeded = true;
            string[] paths = { Path, BackupPath, Path + ".tmp" };

            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    if (File.Exists(paths[i])) File.Delete(paths[i]);
                }
                catch (Exception)
                {
                    succeeded = false;
                }
            }

            return succeeded;
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
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
