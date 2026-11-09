using System;
using System.IO;
using UnityEngine;

namespace FamilyTimeChef.Plugin
{
    /// <summary>
    /// In-process companion for the external Studio GUI.
    ///
    /// The game keeps child hunger in GPU compute buffers and exposes feeding and food
    /// spawning only as managed calls, so an external process cannot do this on its own.
    /// This runs inside the game's live Mono scripting domain (injected from outside)
    /// and drives the game's own API through reflection, while Studio talks to it over a
    /// loopback-only socket.
    /// </summary>
    public sealed class ChefPlugin
    {
        public const string Guid = "com.familytimechef.plugin";
        public const string Version = "1.1.0";

        internal static ChefPlugin Instance;

        internal BuildDetector.Identity Build;
        internal GameBridge Bridge;
        internal Settings Cfg;
        internal Automation Auto;
        internal MainThreadDispatcher Main = new MainThreadDispatcher();

        private IpcServer _ipc;
        private float _bindRetryAt;
        private bool _ready;

        private static readonly object LogLock = new object();
        private static string _logPath;

        public void Start()
        {
            Instance = this;
            Log("Chef start");

            Build = BuildDetector.Detect();
            Cfg = Settings.Load();
            Bridge = new GameBridge();
            Auto = new Automation(this, Bridge, Cfg);

            Log($"Family Time Chef {Version} loaded.");
            Log($"Detected: {Build.EpisodeLabel} / {Build.BuildLabel} / {Build.Renderer} (supported={Build.Supported}).");
            if (!string.IsNullOrEmpty(Build.Note)) Log(Build.Note);

            _ipc = new IpcServer(this, Main);
            _ipc.Start();
            _ready = true;
        }

        /// <summary>Called once per frame from the render hook, on Unity's main thread.</summary>
        public void Tick()
        {
            if (!_ready) return;
            try
            {
                Main.Pump();
                Bridge.EnforceBackground();
                _ipc?.Poll();

                if (!Bridge.Bound && Time.realtimeSinceStartup >= _bindRetryAt)
                {
                    _bindRetryAt = Time.realtimeSinceStartup + 1f;
                    if (Bridge.TryBind())
                    {
                        Log("Bound to the live game API.");
                        if (!Build.Supported) Log("Writes stay disabled: unrecognised build.");
                    }
                }

                Auto.Tick();
            }
            catch (Exception ex)
            {
                Log("Tick error: " + ex.Message);
            }
        }

        public void Stop()
        {
            _ready = false;
            try { _ipc?.Stop(); } catch { }
        }

        /// <summary>Writes to the plugin log and forwards the line to connected clients.</summary>
        public void Log(string message)
        {
            WriteLog(message);
            try { _ipc?.Broadcast(message); } catch { }
        }

        private static void WriteLog(string message)
        {
            try
            {
                lock (LogLock)
                {
                    if (_logPath == null) _logPath = Path.Combine(Settings.Dir, "plugin.log");
                    Io.Append(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + "\r\n");
                }
            }
            catch { }
        }
    }
}
