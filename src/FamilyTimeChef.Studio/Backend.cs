using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FamilyTimeChef.Studio
{
    internal sealed class GroupRow
    {
        public int Template;
        public string Name;
        public int Count;
        public int Hungry;
    }

    /// <summary>
    /// Talks to the in-game shim through %LOCALAPPDATA%\FamilyTimeChef.
    ///
    /// A socket was the original design, but the game's managed System.dll has no
    /// System.Net.Sockets (TcpListener throws TypeLoadException inside the live domain),
    /// so both sides share plain files instead. The channel is local-only by
    /// construction: it is a directory in the current user's profile.
    /// </summary>
    internal sealed class Backend : IDisposable
    {
        public event Action<Dictionary<string, string>> StatusReceived;
        public event Action<string> LogReceived;
        public event Action<List<GroupRow>> GroupsReceived;
        public event Action<bool, string> ConnectionChanged;

        private Thread _thread;
        private volatile bool _running;
        private int _nextId;
        private long _respOffset;
        private long _evtOffset;
        private string _dir;
        private string _lastStatus;
        private string _lastGroups;

        public bool Connected { get; private set; }
        public int Port { get; private set; }
        public int Episode { get; private set; }

        public static string BaseDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FamilyTimeChef");

        public static string SessionPath => Path.Combine(BaseDir, "hello.txt");

        public static bool TryReadSession(out int port, out string token, out string error)
        {
            port = 0;
            token = string.Empty;
            error = null;
            try
            {
                if (!File.Exists(SessionPath))
                {
                    error = "No live session. Start Family Time, then inject the agent.";
                    return false;
                }
                foreach (var raw in File.ReadAllLines(SessionPath))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq) == "pid")
                        int.TryParse(line.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool Connect(out string error)
        {
            error = null;
            Disconnect();

            if (!File.Exists(SessionPath))
            {
                error = "No live session. Start the game and inject the agent first.";
                return false;
            }

            try
            {
                _dir = BaseDir;
                _respOffset = Length("resp.txt");
                _evtOffset = Length("events.log");
                _lastStatus = null;

                int pid;
                string token;
                if (TryReadSession(out pid, out token, out error)) Port = pid;

                _running = true;
                Connected = true;
                _thread = new Thread(PollLoop) { IsBackground = true, Name = "Chef.Studio.Poll" };
                _thread.Start();

                ConnectionChanged?.Invoke(true, "Connected to Family Time (pid " + Port + ")");
                ReadStatus();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Disconnect();
                return false;
            }
        }

        private long Length(string name)
        {
            try
            {
                var fi = new FileInfo(Path.Combine(_dir, name));
                return fi.Exists ? fi.Length : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Read a file the in-game shim may be replacing at the same moment.
        ///
        /// File.ReadAllText uses FileShare.Read, which denies the writer: that is what made
        /// the game's snapshot rewrite fail while this GUI polled, freezing the pump. Opening
        /// explicitly with ReadWrite|Delete access lets the shim swap the file atomically
        /// under us, and a failed open is retried instead of reported as "no data".
        /// </summary>
        private string ReadTextShared(string name)
        {
            var path = Path.Combine(_dir, name);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                   FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                catch { Thread.Sleep(5); }
            }
            return null;
        }

        public void Disconnect()
        {
            _running = false;
            Connected = false;
            _thread = null;
        }

        public void Send(string verb, params string[] kv)
        {
            if (!Connected) return;
            try
            {
                int id = Interlocked.Increment(ref _nextId);
                var sb = new StringBuilder();
                sb.Append("CMD|").Append(id).Append('|').Append(verb.ToUpperInvariant());
                for (int i = 0; i + 1 < kv.Length; i += 2)
                {
                    if (kv[i + 1] == null) continue;
                    sb.Append('|').Append(kv[i]).Append('=').Append(kv[i + 1].Replace('|', ' '));
                }
                sb.Append('\n');

                // The in-game reader opens cmd.queue briefly on every frame; a single
                // sharing violation used to discard the command silently, which is why a
                // landed click could produce no trace at all. Retry, and report a real
                // failure instead of losing it.
                var path = Path.Combine(_dir, "cmd.queue");
                var line = sb.ToString();
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try { File.AppendAllText(path, line); return; }
                    catch { Thread.Sleep(5); }
                }
                LogReceived?.Invoke("Error: could not queue " + verb + " (channel busy)");
            }
            catch { }
        }

        private void PollLoop()
        {
            while (_running)
            {
                try
                {
                    ReadStatus();
                    ReadResponses();
                    ReadEvents();
                    ReadGroups();
                }
                catch { }
                Thread.Sleep(200);
            }
        }

        private void ReadStatus()
        {
            try
            {
                var text = ReadTextShared("status.txt");
                if (text == null || text.Length == 0 || text == _lastStatus) return;
                _lastStatus = text;

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    fields[line.Substring(0, eq)] = line.Substring(eq + 1);
                }

                int ep;
                if (fields.TryGetValue("episode", out var eps) &&
                    int.TryParse(eps, NumberStyles.Integer, CultureInfo.InvariantCulture, out ep))
                    Episode = ep;

                StatusReceived?.Invoke(fields);
            }
            catch { }
        }

        private void ReadResponses()
        {
            try
            {
                var path = Path.Combine(_dir, "resp.txt");
                if (!File.Exists(path)) return;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length <= _respOffset) return;
                    fs.Seek(_respOffset, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - _respOffset];
                    int read = fs.Read(buf, 0, buf.Length);
                    _respOffset += read;

                    var text = Encoding.UTF8.GetString(buf, 0, read);
                    foreach (var raw in text.Split('\n'))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0) continue;
                        HandleResponse(line);
                    }
                }
            }
            catch { }
        }

        private void HandleResponse(string line)
        {
            var parts = line.Split('|');
            if (parts.Length < 3) return;

            var verb = parts[2];
            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 3; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                f[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }

            string st, msg;
            f.TryGetValue("st", out st);
            f.TryGetValue("msg", out msg);

            if (st == "ERR") LogReceived?.Invoke("Error: " + msg);
            else if (st == "OK" && !string.IsNullOrEmpty(msg) && msg != "status") LogReceived?.Invoke(msg);
        }

        private void ReadEvents()
        {
            try
            {
                var path = Path.Combine(_dir, "events.log");
                if (!File.Exists(path)) return;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length <= _evtOffset) return;
                    fs.Seek(_evtOffset, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - _evtOffset];
                    int read = fs.Read(buf, 0, buf.Length);
                    _evtOffset += read;

                    var text = Encoding.UTF8.GetString(buf, 0, read);
                    foreach (var raw in text.Split('\n'))
                    {
                        var line = raw.Trim();
                        if (line.Length > 0) LogReceived?.Invoke(line);
                    }
                }
            }
            catch { }
        }

        private void ReadGroups()
        {
            try
            {
                var text = ReadTextShared("groups.txt");
                if (text == null || text == _lastGroups) return;
                _lastGroups = text;

                var rows = new List<GroupRow>();
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    int tpl, count, hungry;
                    int.TryParse(p[0], out tpl);
                    int.TryParse(p[2], out count);
                    int.TryParse(p[3], out hungry);
                    rows.Add(new GroupRow { Template = tpl, Name = p[1], Count = count, Hungry = hungry });
                }
                GroupsReceived?.Invoke(rows);
            }
            catch { }
        }

        public void Dispose() => Disconnect();
    }
}
