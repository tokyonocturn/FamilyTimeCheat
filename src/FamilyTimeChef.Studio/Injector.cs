using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FamilyTimeChef.Studio
{
    internal enum InjectOutcome
    {
        Injected,
        AlreadyActive,
        NoGame,
        NoPayload,
        Failed
    }

    internal sealed class InjectResult
    {
        public InjectOutcome Outcome;
        public string Message;
        public int Pid;
    }

    /// <summary>
    /// Finds a running Family Time process and loads the in-game shim into it.
    ///
    /// This only automates the step the user used to run by hand:
    ///   ftinject.exe &lt;pid&gt; ftagent.dll
    /// The injection primitive is unchanged (LoadLibrary on a remote thread); this
    /// class just locates the process and the payload, and reports what happened.
    /// </summary>
    internal static class Injector
    {
        private static readonly object Gate = new object();
        private static DateTime _lastAttemptUtc = DateTime.MinValue;
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Where ftinject.exe and ftagent.dll live: next to the Studio executable
        /// (portable extraction folder) or in the installed channel directory.
        /// </summary>
        public static string PayloadDirectory()
        {
            var beside = AppContext.BaseDirectory;
            if (HasPayload(beside)) return beside;
            if (HasPayload(Backend.BaseDir)) return Backend.BaseDir;
            return null;
        }

        private static bool HasPayload(string dir)
        {
            try
            {
                return !string.IsNullOrEmpty(dir)
                    && File.Exists(Path.Combine(dir, "ftinject.exe"))
                    && File.Exists(Path.Combine(dir, "ftagent.dll"));
            }
            catch { return false; }
        }

        /// <summary>First Family Time process, preferring one that has a window.</summary>
        public static int FindGameProcess()
        {
            Process[] all;
            try { all = Process.GetProcessesByName("FamilyTime"); }
            catch { return 0; }

            int fallback = 0;
            try
            {
                foreach (var p in all)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        if (fallback == 0) fallback = p.Id;
                        if (p.MainWindowHandle != IntPtr.Zero) return p.Id;
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in all) { try { p.Dispose(); } catch { } }
            }
            return fallback;
        }

        /// <summary>
        /// True when status.txt already reports a bound shim for this process.
        /// status.txt is written by the shim, so this reflects the game, not our wishes.
        /// </summary>
        private static bool AlreadyBound(int pid)
        {
            try
            {
                var path = Path.Combine(Backend.BaseDir, "status.txt");
                if (!File.Exists(path)) return false;

                int statusPid = 0;
                string bound = null;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq);
                    var value = line.Substring(eq + 1);
                    if (key == "pid") int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out statusPid);
                    else if (key == "bound") bound = value;
                }
                return bound == "1" && (statusPid == 0 || statusPid == pid);
            }
            catch { return false; }
        }

        public static Task<InjectResult> InjectAsync()
        {
            return Task.Run(() => Inject());
        }

        public static InjectResult Inject()
        {
            lock (Gate)
            {
                var elapsed = DateTime.UtcNow - _lastAttemptUtc;
                if (elapsed < Cooldown)
                    return new InjectResult { Outcome = InjectOutcome.Failed, Message = "Injection already attempted a moment ago; waiting." };
                _lastAttemptUtc = DateTime.UtcNow;
            }

            int pid = FindGameProcess();
            if (pid == 0)
            {
                return new InjectResult
                {
                    Outcome = InjectOutcome.NoGame,
                    Message = "Family Time is not running. Start the game, then press Connect."
                };
            }

            if (AlreadyBound(pid))
            {
                return new InjectResult
                {
                    Outcome = InjectOutcome.AlreadyActive,
                    Pid = pid,
                    Message = "Already attached to Family Time (pid " + pid + ")."
                };
            }

            var dir = PayloadDirectory();
            if (dir == null)
            {
                return new InjectResult
                {
                    Outcome = InjectOutcome.NoPayload,
                    Pid = pid,
                    Message = "Could not find ftinject.exe and ftagent.dll next to the Studio executable or in %LOCALAPPDATA%\\FamilyTimeChef."
                };
            }

            var exe = Path.Combine(dir, "ftinject.exe");
            var dll = Path.Combine(dir, "ftagent.dll");

            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = "\"" + pid + "\" \"" + dll + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = dir
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                        return new InjectResult { Outcome = InjectOutcome.Failed, Pid = pid, Message = "Could not start the injector." };

                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(20000))
                    {
                        try { proc.Kill(); } catch { }
                        return new InjectResult { Outcome = InjectOutcome.Failed, Pid = pid, Message = "Injector timed out after 20 seconds." };
                    }

                    var detail = (stdout + " " + stderr).Trim().Replace(Environment.NewLine, " ");
                    if (proc.ExitCode == 0)
                    {
                        return new InjectResult
                        {
                            Outcome = InjectOutcome.Injected,
                            Pid = pid,
                            Message = "Attached to Family Time (pid " + pid + "). " + detail
                        };
                    }

                    return new InjectResult
                    {
                        Outcome = InjectOutcome.Failed,
                        Pid = pid,
                        Message = "Injector failed (exit " + proc.ExitCode + "): " + detail
                    };
                }
            }
            catch (Exception ex)
            {
                return new InjectResult
                {
                    Outcome = InjectOutcome.Failed,
                    Pid = pid,
                    Message = "Injection error: " + ex.Message
                };
            }
        }
    }
}
