using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// Lightweight in-game crash/error recorder. It never uploads data; reports stay on this device.
    /// In development builds, press F8 on a keyboard to show or hide the diagnostic overlay.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class AetherDiagnostics : MonoBehaviour
    {
        private const int MaxEntries = 160;
        private const string ReportFileName = "aether-diagnostics.log";

        private static AetherDiagnostics _instance;
        private static readonly ConcurrentQueue<LogEntry> Incoming = new ConcurrentQueue<LogEntry>();
        private static int _errorCount;
        private static int _exceptionCount;
        private static int _warningCount;

        private readonly List<LogEntry> _entries = new List<LogEntry>(MaxEntries);
        private readonly object _entryLock = new object();
        private bool _showOverlay;
        private float _fps;
        private float _fpsElapsed;
        private int _fpsFrames;
        private string _lastScene = "Unknown";
        private string _reportPath;
        private GUIStyle _boxStyle;
        private GUIStyle _textStyle;

        private struct LogEntry
        {
            public readonly string Time;
            public readonly string Type;
            public readonly string Message;
            public readonly string Stack;

            public LogEntry(string type, string message, string stack)
            {
                Time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
                Type = type;
                Message = message ?? string.Empty;
                Stack = stack ?? string.Empty;
            }

            public override string ToString()
            {
                return "[" + Time + "] [" + Type + "] " + Message +
                       (string.IsNullOrEmpty(Stack) ? string.Empty : "\n" + Stack);
            }
        }

        public static int ErrorCount => Volatile.Read(ref _errorCount);
        public static int ExceptionCount => Volatile.Read(ref _exceptionCount);
        public static int WarningCount => Volatile.Read(ref _warningCount);
        public string ReportPath => _reportPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var host = new GameObject("AetherDiagnostics");
            DontDestroyOnLoad(host);
            host.AddComponent<AetherDiagnostics>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _reportPath = Path.Combine(Application.persistentDataPath, ReportFileName);
            _lastScene = SceneManager.GetActiveScene().name;
            Application.logMessageReceivedThreaded += OnLogMessage;
            SceneManager.sceneLoaded += OnSceneLoaded;

            Enqueue("INFO", "Diagnostics started. Unity " + Application.unityVersion +
                "; platform " + Application.platform + "; device " + SystemInfo.deviceModel +
                "; scene " + _lastScene, string.Empty);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            Application.logMessageReceivedThreaded -= OnLogMessage;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            Enqueue("INFO", "Application pause changed: " + paused, string.Empty);
        }

        private void OnApplicationFocus(bool focused)
        {
            Enqueue("INFO", "Application focus changed: " + focused, string.Empty);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _lastScene = scene.name;
            Enqueue("INFO", "Scene loaded: " + scene.name + " (" + mode + ")", string.Empty);
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                    Interlocked.Increment(ref _errorCount);
                    break;
                case LogType.Exception:
                    Interlocked.Increment(ref _errorCount);
                    Interlocked.Increment(ref _exceptionCount);
                    break;
                case LogType.Warning:
                    Interlocked.Increment(ref _warningCount);
                    break;
                default:
                    return;
            }

            Enqueue(type.ToString().ToUpperInvariant(), condition, stackTrace);
        }

        /// <summary>Lets gameplay systems record a recoverable invariant failure with context.</summary>
        public static void RecordFinding(string category, string message)
        {
            Enqueue("FINDING/" + (category ?? "GENERAL"), message, string.Empty);
        }

        private static void Enqueue(string type, string message, string stack)
        {
            Incoming.Enqueue(new LogEntry(type, message, stack));
        }

        private void Update()
        {
            LogEntry entry;
            while (Incoming.TryDequeue(out entry))
            {
                lock (_entryLock)
                {
                    _entries.Add(entry);
                    if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
                }

                if (entry.Type == "ERROR" || entry.Type == "ASSERT" || entry.Type == "EXCEPTION")
                    Persist(entry);
            }

            _fpsFrames++;
            _fpsElapsed += Time.unscaledDeltaTime;
            if (_fpsElapsed >= 0.5f)
            {
                _fps = _fpsElapsed > 0f ? _fpsFrames / _fpsElapsed : 0f;
                _fpsFrames = 0;
                _fpsElapsed = 0f;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                _showOverlay = !_showOverlay;
#endif
        }

        private void Persist(LogEntry entry)
        {
            try
            {
                File.AppendAllText(_reportPath, entry + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // Diagnostics must never create a second error loop if storage is unavailable.
            }
        }

        /// <summary>Returns a shareable text snapshot without transmitting it anywhere.</summary>
        public string ExportReport()
        {
            var builder = new StringBuilder(2048);
            builder.AppendLine("Varellon / Project Aether diagnostics");
            builder.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            builder.AppendLine("Unity: " + Application.unityVersion);
            builder.AppendLine("Platform: " + Application.platform);
            builder.AppendLine("Device: " + SystemInfo.deviceModel);
            builder.AppendLine("OS: " + SystemInfo.operatingSystem);
            builder.AppendLine("Scene: " + _lastScene);
            builder.AppendLine("Errors: " + ErrorCount + "; exceptions: " + ExceptionCount +
                "; warnings: " + WarningCount);
            builder.AppendLine("FPS sample: " + _fps.ToString("0.0"));
            builder.AppendLine();
            lock (_entryLock)
            {
                for (int i = 0; i < _entries.Count; i++)
                    builder.AppendLine(_entries[i].ToString()).AppendLine();
            }
            return builder.ToString();
        }

        private void OnGUI()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!_showOverlay) return;
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
                _textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true };
            }

            float width = Mathf.Min(Screen.width - 24f, 640f);
            float height = Mathf.Min(Screen.height - 24f, 300f);
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), _boxStyle);
            GUILayout.Label("AETHER DIAGNOSTICS  •  F8 to hide", _textStyle);
            GUILayout.Label("Scene: " + _lastScene + "   FPS: " + _fps.ToString("0.0"), _textStyle);
            GUILayout.Label("Errors: " + ErrorCount + "   Exceptions: " + ExceptionCount +
                "   Warnings: " + WarningCount, _textStyle);
            GUILayout.BeginVertical();
            lock (_entryLock)
            {
                int start = Mathf.Max(0, _entries.Count - 5);
                for (int i = start; i < _entries.Count; i++)
                    GUILayout.Label("[" + _entries[i].Type + "] " + _entries[i].Message, _textStyle);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
#endif
        }
    }
}
