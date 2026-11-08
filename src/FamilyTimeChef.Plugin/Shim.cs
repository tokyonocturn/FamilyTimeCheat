using System;
using System.Collections.Generic;
using FamilyTimeChef.Plugin;
using UnityEngine;
using UnityEngine.Rendering;

namespace FamilyTimeChef.InGame
{
    /// <summary>
    /// Entry point invoked by the native agent through mono_runtime_invoke. By the time
    /// this runs the assembly is inside the game's live scripting domain, so mscorlib,
    /// UnityEngine and Assembly-CSharp are all valid.
    ///
    /// The agent's thread is NOT Unity's main thread: touching Unity from it (even
    /// Application.dataPath) terminates the process. So nothing game-related happens
    /// here. Only a frame hook is installed; all game work is deferred to the first real
    /// frame.
    ///
    /// This Unity 6 CoreModule no longer has RenderPipelineManager.beginFrameRendering
    /// (removed in favour of the per-context and per-camera callbacks), so the URP-safe
    /// hooks are beginCameraRendering / beginContextRendering. Each hook lives in its own
    /// method so a missing member only disables that one hook.
    /// </summary>
    public static class Shim
    {
        private static ChefPlugin _host;
        private static readonly List<string> Attached = new List<string>();

        private static long _frameCount;
        private static int _pumping;
        private static bool _started;

        public static void Entry()
        {
            try
            {
                if (_host != null) { Log("Entry called again; ignored."); return; }

                _host = new ChefPlugin();
                AttachFramePump();

                Log("Shim.Entry complete; hooks=[" + string.Join(",", Attached.ToArray()) +
                    "]. Game work is deferred to the first main-thread frame.");
            }
            catch (Exception ex)
            {
                Log("Entry failed: " + ex);
            }
        }

        private static void AttachFramePump()
        {
            TryHookBeginCamera();
            TryHookBeginContext();
            TryHookCameraPreRender();
            TryHookCameraPreCull();
        }

        // --- isolated hook installers -------------------------------------------------

        private static void TryHookBeginCamera()
        {
            try
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                Attached.Add("beginCameraRendering");
            }
            catch (Exception ex) { Log("beginCameraRendering failed: " + ex.GetType().Name + " " + ex.Message); }
        }

        private static void TryHookBeginContext()
        {
            try
            {
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                Attached.Add("beginContextRendering");
            }
            catch (Exception ex) { Log("beginContextRendering failed: " + ex.GetType().Name + " " + ex.Message); }
        }

        private static void TryHookCameraPreRender()
        {
            try
            {
                Camera.onPreRender += OnCameraPreRender;
                Attached.Add("Camera.onPreRender");
            }
            catch (Exception ex) { Log("Camera.onPreRender failed: " + ex.GetType().Name + " " + ex.Message); }
        }

        private static void TryHookCameraPreCull()
        {
            try
            {
                Camera.onPreCull += OnCameraPreCull;
                Attached.Add("Camera.onPreCull");
            }
            catch (Exception ex) { Log("Camera.onPreCull failed: " + ex.GetType().Name + " " + ex.Message); }
        }

        // --- main-thread callbacks ----------------------------------------------------

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) { Pump(); }
        private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) { Pump(); }
        private static void OnCameraPreRender(Camera cam) { Pump(); }
        private static void OnCameraPreCull(Camera cam) { Pump(); }

        /// <summary>Runs on Unity's main thread, once per rendered frame.</summary>
        private static void Pump()
        {
            if (System.Threading.Interlocked.Exchange(ref _pumping, 1) == 1) return;
            try
            {
                _frameCount++;

                if (!_started)
                {
                    _started = true;
                    try
                    {
                        _host.Start();
                        Log("Started on the main thread at frame " + _frameCount + ".");
                    }
                    catch (Exception ex)
                    {
                        Log("Start failed: " + ex);
                    }
                }

                if (_host != null) _host.Tick();
            }
            catch (Exception ex)
            {
                Log("Pump error: " + ex.Message);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _pumping, 0);
            }
        }

        public static void Log(string message)
        {
            try
            {
                Io.Append(System.IO.Path.Combine(Settings.Dir, "shim.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + "\r\n");
            }
            catch { }
            try { if (_host != null) _host.Log(message); } catch { }
        }
    }
}
