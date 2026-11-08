using System;
using System.IO;
using System.Reflection;

namespace FamilyTimeChef.Boot
{
    /// <summary>
    /// Unity-free hand-off shim. BepInEx's patched PreloaderPreMain calls this, which
    /// loads the real plugin assembly and runs its bootstrap entry.
    ///
    /// Only primitives this runtime actually implements are used. Every risky call sits
    /// in its own method so a JIT-time MissingMethodException stays catchable at the
    /// call site instead of killing Run() wholesale.
    /// </summary>
    public static class Loader
    {
        public static void Run()
        {
            var dir = Dir();
            Log(dir, "Boot.Run start");

            var asm = LoadPlugin(dir);
            Log(dir, "assembly " + (asm == null ? "<null>" : "loaded"));
            if (asm == null) return;

            var type = FindType(asm, "FamilyTimeChef.Bootstrap.Entry");
            Log(dir, "type " + (type == null ? "<null>" : type.FullName));
            if (type == null) return;

            var start = FindMethod(type, "Start");
            Log(dir, "method " + (start == null ? "<null>" : start.Name));
            if (start == null) return;

            Log(dir, "invoke " + Invoke(start));
        }

        private static string Dir()
        {
            try
            {
                var d = Path.GetDirectoryName(typeof(Loader).Assembly.Location);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            return ".";
        }

        private static Assembly LoadPlugin(string dir)
        {
            try { return Assembly.LoadFrom(Path.Combine(dir, "FamilyTimeChef.Plugin.dll")); }
            catch (Exception ex) { Log(dir, "load failed: " + ex.Message); return null; }
        }

        private static Type FindType(Assembly asm, string name)
        {
            try { return asm.GetType(name); }
            catch (Exception ex) { return null; }
        }

        private static MethodInfo FindMethod(Type t, string name)
        {
            try { return t.GetMethod(name); }
            catch { return null; }
        }

        private static string Invoke(MethodInfo m)
        {
            try { m.Invoke(null, null); return "ok"; }
            catch (Exception ex) { return "failed: " + ex.Message; }
        }

        /// <summary>Overwrites each step, so the file always shows the furthest stage reached.</summary>
        private static void Log(string dir, string message)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(dir, "boot.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + "\r\n");
            }
            catch { }
        }
    }
}
