using System;
using System.IO;
using System.Text;

namespace FamilyTimeChef.Plugin
{
    /// <summary>
    /// File I/O for a runtime whose mscorlib is a reduced facade.
    ///
    /// Unity 6000.2's Mono is missing members that normal .NET code assumes exist
    /// (confirmed absent at runtime: AppDomain.BaseDirectory,
    /// Assembly.GetType(string,bool), File.AppendAllText). A missing member fails JIT
    /// for the entire method, so every primitive lives in its own small method here and
    /// is wrapped at the call site: a failure stays catchable instead of taking down the
    /// caller.
    ///
    /// File.WriteAllText is used as the one write primitive because it is proven to work
    /// on this runtime (BepInEx's own preloader log uses it).
    /// </summary>
    internal static class Io
    {
        public static void Append(string path, string text)
        {
            // Preferred: a real append, which never truncates and never rewrites history, so
            // a concurrent reader cannot lose or corrupt the log behind it.
            try { if (AppendDirect(path, text)) return; }
            catch { }

            // Fallback for a runtime without FileStream: rewrite atomically. Never fall back
            // to "" on a failed read here, or the whole history behind this append is lost.
            try
            {
                var existing = ReadShared(path);
                if (existing == null) existing = string.Empty;
                WriteAtomic(path, existing + text);
            }
            catch { }
        }

        /// <summary>
        /// Append with FileStream in its own method: a member missing from this runtime fails
        /// JIT for the whole method, and the caller catches that to fall back.
        /// </summary>
        private static bool AppendDirect(string path, string text)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var bytes = Encoding.UTF8.GetBytes(text);
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write,
                                           FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush();
            }
            return true;
        }

        public static string Read(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Read that tolerates a concurrent writer. Returns null when the file could not be
        /// read at all, which is different from "empty": the command reader must keep its
        /// offset on a failed read instead of treating it as a truncated file.
        /// </summary>
        public static string ReadShared(string path)
        {
            // Only primitives already proven on this runtime: File.ReadAllText with a few
            // quick retries. FileStream/StreamReader are not guaranteed to exist, and a
            // missing member fails JIT for the entire method, which would silently stop
            // the command reader from ever consuming the queue.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (!File.Exists(path)) return string.Empty;
                    return File.ReadAllText(path);
                }
                catch { }
                try { System.Threading.Thread.Sleep(5); } catch { }
            }
            return null;
        }

        /// <summary>
        /// Replace a file atomically: write a sibling temp file, then swap it in.
        ///
        /// The old truncate-and-rewrite is what froze the pump: while the external GUI was
        /// reading status.txt, the rewrite could not open the file, the exception was
        /// swallowed, and the snapshot silently stopped advancing (t=x -> t=x). With a
        /// temp file the destination is never truncated in place, so a concurrent reader
        /// can never block or observe a half-written snapshot.
        /// </summary>
        public static bool WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    File.WriteAllText(tmp, text);

                    // A reader holding the destination open blocks the swap; retry briefly
                    // rather than giving up on the frame.
                    if (File.Exists(path))
                    {
                        // Replace keeps the destination continuously present. Deleting
                        // first left a window in which a reader saw no file at all, which
                        // is what made one snapshot read come back empty.
                        try { File.Replace(tmp, path, null); return true; }
                        catch { }
                        try { File.Delete(path); }
                        catch { }
                    }
                    File.Move(tmp, path);
                    return true;
                }
                catch
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    try { System.Threading.Thread.Sleep(2); } catch { }
                }
            }
            return false;
        }

        public static void Write(string path, string text)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Startup truncation has no concurrent reader yet; use the direct write so
                // an existing file is emptied deterministically.
                if (text.Length == 0) { File.WriteAllText(path, text); return; }

                WriteAtomic(path, text);
            }
            catch { }
        }

        public static bool Exists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        public static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
