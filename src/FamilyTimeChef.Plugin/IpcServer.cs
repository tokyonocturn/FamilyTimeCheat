using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FamilyTimeChef.Plugin
{
    /// <summary>Marshals work from other threads onto the Unity main thread.</summary>
    internal sealed class MainThreadDispatcher
    {
        private readonly List<Action> _queue = new List<Action>();

        public void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Add(action);
        }

        public void Pump()
        {
            Action[] work;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                work = _queue.ToArray();
                _queue.Clear();
            }

            foreach (var action in work)
            {
                try { action(); }
                catch (Exception ex) { ChefPlugin.Instance?.Log("IPC error: " + ex.Message); }
            }
        }
    }

    /// <summary>
    /// File-based control channel.
    ///
    /// The game's managed System.dll does not contain System.Net.Sockets (connecting
    /// TcpListener threw TypeLoadException inside the live domain), so the transport is
    /// plain files in %LOCALAPPDATA%\FamilyTimeChef:
    ///
    ///   hello.txt   identity of the live session
    ///   cmd.queue   append-only requests from Studio
    ///   resp.txt    append-only replies, each tagged with the verb
    ///   status.txt  live snapshot, rewritten on the main thread
    ///   groups.txt  per-template instance/hunger counts
    ///   events.log  human-readable operation log
    ///
    /// Only System.IO is used, so it works on this runtime regardless of what the
    /// networking stack looks like.
    /// </summary>
    internal sealed class IpcServer
    {
        private readonly ChefPlugin _host;
        private readonly MainThreadDispatcher _main;
        private volatile bool _running;
        private long _cmdOffset;
        private int _frames;

        public int Port { get; private set; }

        public IpcServer(ChefPlugin host, MainThreadDispatcher main)
        {
            _host = host;
            _main = main;
        }

        private static string Dir => Settings.Dir;
        private static string HelloPath => Path.Combine(Dir, "hello.txt");
        private static string CmdPath => Path.Combine(Dir, "cmd.queue");
        private static string RespPath => Path.Combine(Dir, "resp.txt");
        private static string StatusPath => Path.Combine(Dir, "status.txt");
        private static string GroupsPath => Path.Combine(Dir, "groups.txt");
        private static string EventsPath => Path.Combine(Dir, "events.log");

        public void Start()
        {
            try
            {
                _running = true;
                // System.Diagnostics.Process is stripped from this runtime, so the
                // session is identified by its files rather than a pid.
                Port = 0;

                Io.Write(CmdPath, string.Empty);
                Io.Write(RespPath, string.Empty);
                Io.Write(EventsPath, string.Empty);
                _cmdOffset = 0;

                var b = _host.Build;
                Io.Write(HelloPath, string.Join("\n", new[]
                {
                    "version=" + ChefPlugin.Version,
                    "pid=" + Port.ToString(CultureInfo.InvariantCulture),
                    "episode=" + b.Episode.ToString(CultureInfo.InvariantCulture),
                    "episodeLabel=" + b.EpisodeLabel,
                    "buildLabel=" + b.BuildLabel,
                    "renderer=" + b.Renderer,
                    "supported=" + (b.Supported ? "1" : "0"),
                    "buildGuid=" + b.BuildGuid,
                }) + "\n");

                WriteStatus();
                _host.Log("File channel ready in " + Dir);
            }
            catch (Exception ex)
            {
                _host.Log("Failed to start file channel: " + ex.Message);
            }
        }

        /// <summary>Called every frame from Unity's main thread.</summary>
        public void Poll()
        {
            if (!_running) return;
            try
            {
                ReadCommands();
                if (++_frames % 5 == 0) WriteStatus();
            }
            catch { }
        }

        public void Broadcast(string message)
        {
            try { Io.Append(EventsPath, DateTime.Now.ToString("HH:mm:ss") + "  " + Protocol.Clean(message) + "\n"); }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            Io.Delete(HelloPath);
            Io.Delete(StatusPath);
        }

        private void ReadCommands()
        {
            if (!Io.Exists(CmdPath)) return;

            // A concurrent writer makes the read fail outright; that must not be mistaken
            // for a truncated file, or the offset resets and commands are silently skipped.
            var all = Io.ReadShared(CmdPath);
            if (all == null) return;
            if (all.Length <= _cmdOffset)
            {
                if (all.Length < _cmdOffset) _cmdOffset = all.Length;
                return;
            }

            // Advance the offset one complete line at a time. The old code consumed the whole
            // chunk up front, so a single command that threw anywhere (including in the
            // response writer) silently discarded every command behind it, permanently.
            int baseOffset = (int)_cmdOffset;
            var chunk = all.Substring(baseOffset);
            int cursor = 0;

            while (true)
            {
                int nl = chunk.IndexOf('\n', cursor);
                if (nl < 0) break; // partial line still being written: pick it up next poll

                var line = chunk.Substring(cursor, nl - cursor).Trim();
                cursor = nl + 1;
                _cmdOffset = baseOffset + cursor; // consumed exactly this line

                if (line.Length == 0) continue;

                string kind, id, verb;
                Dictionary<string, string> f;
                if (!Protocol.TryParse(line, out kind, out id, out verb, out f)) continue;

                try { Execute(id, verb, f); }
                catch { } // never let one command stall the ones behind it
            }
        }

        private void Execute(string id, string verb, Dictionary<string, string> f)
        {
            string status = "OK";
            string msg = string.Empty;

            try
            {
                switch ((verb ?? string.Empty).ToUpperInvariant())
                {
                    case "PING":
                        msg = "pong";
                        break;

                    case "FEED":
                        {
                            var r = _host.Auto.FeedHungry(true);
                            status = r.Ok ? "OK" : "ERR";
                            msg = r.Ok ? $"Manual feed: {r.Actions} need(s) satisfied." : "Manual feed: " + r.Message;
                            _host.Log(msg);
                            break;
                        }

                    case "WATER":
                        {
                            var r = _host.Auto.WaterThirsty(true);
                            status = r.Ok ? "OK" : "ERR";
                            msg = r.Ok ? $"Manual water: {r.Actions} delivered." : "Manual water: " + r.Message;
                            _host.Log(msg);
                            break;
                        }

                    case "SPAWN":
                        {
                            string err, chosen, detail;
                            if (_host.Bridge.SpawnFoodTracked(out err, out chosen, out detail))
                                msg = "Manual spawn: " + detail;
                            else { status = "ERR"; msg = "Manual spawn failed: " + err; }
                            _host.Log(msg);
                            break;
                        }

                    case "WIN":
                        {
                            int w = 1100, h = 620;
                            string rawW, rawH;
                            if (f != null && f.TryGetValue("w", out rawW)) int.TryParse(rawW, out w);
                            if (f != null && f.TryGetValue("h", out rawH)) int.TryParse(rawH, out h);
                            string err;
                            if (_host.Bridge.SetWindowed(w, h, out err))
                                msg = "windowed " + w + "x" + h + " -> " + _host.Bridge.ScreenInfo();
                            else { status = "ERR"; msg = "WIN failed: " + err; }
                            _host.Log(msg);
                            break;
                        }

                    case "SCREEN":
                        msg = _host.Bridge.ScreenInfo();
                        _host.Log(msg);
                        break;

                    case "POS":
                        {
                            int idx = -1;
                            string rawIdx;
                            if (f != null && f.TryGetValue("index", out rawIdx)) int.TryParse(rawIdx, out idx);
                            msg = "instance " + idx + " -> " + _host.Bridge.InstancePosition(idx);
                            _host.Log(msg);
                            break;
                        }

                    case "CONFIG":
                        ApplyConfig(f);
                        msg = "Automation settings updated.";
                        break;

                    case "STATUS":
                        msg = "status";
                        break;

                    // Groups are published continuously in groups.txt, so the verb is
                    // acknowledged rather than executed.
                    case "GROUPS":
                        msg = "groups";
                        break;

                    case "SETNEED":
                        {
                            string want = "FOOD";
                            string rawType;
                            if (f != null && f.TryGetValue("type", out rawType) && !string.IsNullOrEmpty(rawType))
                                want = rawType.ToUpperInvariant();

                            string err;
                            if (_host.Bridge.SetHungerType(want, out err)) msg = "need type set to " + want;
                            else { status = "ERR"; msg = "SETNEED failed: " + err; }
                            _host.Log(msg);
                            break;
                        }

                    case "TICK":
                        {
                            float ts = 1f;
                            string rawTs;
                            if (f != null && f.TryGetValue("scale", out rawTs))
                                float.TryParse(rawTs, NumberStyles.Float, CultureInfo.InvariantCulture, out ts);
                            _host.Bridge.SetTimeScale(ts);
                            msg = "timeScale set to " + ts.ToString("0.###", CultureInfo.InvariantCulture);
                            _host.Log(msg);
                            break;
                        }

                    case "RELAX":
                        {
                            _host.Bridge.ForceBackground = true;
                            _host.Bridge.SetRunInBackground(true);
                            _host.Bridge.SetTimeScale(1f);
                            msg = "runInBackground=" + (_host.Bridge.RunInBackground ? "1" : "0")
                                + " timeScale=" + _host.Bridge.TimeScale.ToString("0.###", CultureInfo.InvariantCulture);
                            _host.Log(msg);
                            break;
                        }

                    case "SESS":
                        msg = "session=" + _host.Bridge.SessionState();
                        _host.Log(msg);
                        break;

                    case "START":
                        {
                            string err;
                            string before = _host.Bridge.SessionState();
                            if (_host.Bridge.BeginFromTitle(out err))
                                msg = "BeginFromTitle ok (" + before + " -> " + _host.Bridge.SessionState() + ")";
                            else { status = "ERR"; msg = "BeginFromTitle failed: " + err + " (state " + before + ")"; }
                            _host.Log(msg);
                            break;
                        }

                    case "NEWGAME":
                        {
                            int seed = 20260101;
                            string rawSeed;
                            if (f != null && f.TryGetValue("seed", out rawSeed)) int.TryParse(rawSeed, out seed);
                            string err;
                            if (_host.Bridge.StartNewGame(seed, false, out err))
                                msg = "StartNewGame requested, session=" + _host.Bridge.SessionState();
                            else { status = "ERR"; msg = "StartNewGame failed: " + err; }
                            _host.Log(msg);
                            break;
                        }

                    case "LOADGAME":
                        {
                            string name = "The-one";
                            string rawName;
                            if (f != null && f.TryGetValue("name", out rawName) && !string.IsNullOrEmpty(rawName)) name = rawName;
                            string err;
                            if (_host.Bridge.LoadSession(name, out err))
                                msg = "LoadGame(" + name + ") requested, session=" + _host.Bridge.SessionState();
                            else { status = "ERR"; msg = "LoadGame failed: " + err; }
                            _host.Log(msg);
                            break;
                        }

                    case "DIAG":
                        msg = _host.Bridge.Diag();
                        _host.Log(msg);
                        break;

                    default:
                        status = "ERR";
                        msg = "unknown verb " + verb;
                        break;
                }
            }
            catch (Exception ex)
            {
                status = "ERR";
                msg = ex.Message;
            }

            WriteStatus();
            Io.Append(RespPath, Protocol.Build("RES", id, verb, F("st", status, "msg", msg, "n", CountField())) + "\n");
        }

        private string CountField() =>
            _host.Auto.FeedsThisSession.ToString(CultureInfo.InvariantCulture);

        private void ApplyConfig(Dictionary<string, string> f)
        {
            if (f == null) return;
            var c = _host.Cfg;
            string v;
            if (f.TryGetValue("autoFeed", out v)) c.AutoFeed = v == "1";
            if (f.TryGetValue("autoWater", out v)) c.AutoWater = v == "1";
            if (f.TryGetValue("autoSpawn", out v)) c.AutoSpawn = v == "1";
            if (f.TryGetValue("feedMs", out v)) c.FeedIntervalMs = Clamp(ParseInt(v, c.FeedIntervalMs), 200, 600000);
            if (f.TryGetValue("waterMs", out v)) c.WaterIntervalMs = Clamp(ParseInt(v, c.WaterIntervalMs), 200, 600000);
            if (f.TryGetValue("spawnMs", out v)) c.SpawnIntervalMs = Clamp(ParseInt(v, c.SpawnIntervalMs), 500, 600000);
            if (f.TryGetValue("maxActions", out v)) c.MaxActionsPerTick = Clamp(ParseInt(v, c.MaxActionsPerTick), 1, 64);
            if (f.TryGetValue("foodTemplate", out v)) c.FoodTemplateIndex = ParseInt(v, c.FoodTemplateIndex);
            c.Save();
        }

        private void WriteStatus()
        {
            try
            {
                var b = _host.Build;
                var c = _host.Cfg;
                var a = _host.Auto;
                var snap = _host.Bridge.IsReady ? _host.Bridge.ReadSnapshot(c.MaxInstances) : null;

                var sb = new StringBuilder();
                sb.Append("episode=").Append(b.Episode).Append('\n');
                sb.Append("episodeLabel=").Append(b.EpisodeLabel).Append('\n');
                sb.Append("buildLabel=").Append(b.BuildLabel).Append('\n');
                sb.Append("renderer=").Append(b.Renderer).Append('\n');
                sb.Append("supported=").Append(b.Supported ? "1" : "0").Append('\n');
                sb.Append("note=").Append(Protocol.Clean(b.Note)).Append('\n');
                sb.Append("bound=").Append(_host.Bridge.Bound ? "1" : "0").Append('\n');
                sb.Append("bindError=").Append(Protocol.Clean(_host.Bridge.BindError)).Append('\n');
                sb.Append("instances=").Append(snap != null ? snap.Count : 0).Append('\n');
                sb.Append("hungry=").Append(snap != null ? snap.HungryCount : 0).Append('\n');
                sb.Append("hungerTotal=").Append(snap != null ? snap.HungerLevelTotal : 0).Append('\n');
                sb.Append("hungerType=").Append(snap != null ? snap.HungerType : "unknown").Append('\n');
                sb.Append("timeScale=").Append(_host.Bridge.TimeScale.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("runInBackground=").Append(_host.Bridge.RunInBackground ? "1" : "0").Append('\n');
                sb.Append("session=").Append(_host.Bridge.SessionState()).Append('\n');
                sb.Append("screen=").Append(_host.Bridge.ScreenInfo()).Append('\n');
                sb.Append("autoFeed=").Append(c.AutoFeed ? "1" : "0").Append('\n');
                sb.Append("autoWater=").Append(c.AutoWater ? "1" : "0").Append('\n');
                sb.Append("autoSpawn=").Append(c.AutoSpawn ? "1" : "0").Append('\n');
                sb.Append("feedMs=").Append(c.FeedIntervalMs).Append('\n');
                sb.Append("waterMs=").Append(c.WaterIntervalMs).Append('\n');
                sb.Append("spawnMs=").Append(c.SpawnIntervalMs).Append('\n');
                sb.Append("maxActions=").Append(c.MaxActionsPerTick).Append('\n');
                sb.Append("feeds=").Append(a.FeedsThisSession).Append('\n');
                sb.Append("waters=").Append(a.WatersThisSession).Append('\n');
                sb.Append("spawns=").Append(a.SpawnsThisSession).Append('\n');
                sb.Append("blocked=").Append(a.BlockedActions).Append('\n');
                sb.Append("t=").Append(EpochMs().ToString(CultureInfo.InvariantCulture)).Append('\n');

                Io.Write(StatusPath, sb.ToString());

                if (snap != null)
                {
                    var g = new StringBuilder();
                    foreach (var grp in snap.Groups)
                    {
                        g.Append(grp.TemplateIndex).Append('|')
                         .Append(Protocol.Clean(grp.Name)).Append('|')
                         .Append(grp.Count).Append('|')
                         .Append(grp.Hungry).Append('\n');
                    }
                    Io.Write(GroupsPath, g.ToString());
                }
            }
            catch { }
        }

        private static long EpochMs()
        {
            var span = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)span.TotalMilliseconds;
        }

        private static int ParseInt(string s, int fallback)
        {
            int r;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private static Dictionary<string, string> F(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1] ?? string.Empty;
            return d;
        }
    }
}
