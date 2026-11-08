using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FamilyTimeChef.Plugin
{
    /// <summary>Persisted user settings (automation switches and intervals).</summary>
    internal sealed class Settings
    {
        public bool AutoFeed;
        public bool AutoWater;
        public bool AutoSpawn;

        public int FeedIntervalMs = 2500;
        public int WaterIntervalMs = 2500;
        public int SpawnIntervalMs = 8000;

        public int MaxActionsPerTick = 8;
        public int MaxLiveFood = 40;
        public int MaxInstances = 8192;
        public float PerInstanceCooldownMs = 1500f;
        public int FoodTemplateIndex = -1;

        public static string Dir
        {
            get
            {
                var d = Root();
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        /// <summary>
        /// %LOCALAPPDATA%\FamilyTimeChef - the same location Studio reads session.txt from.
        /// </summary>
        private static string Root()
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local)) return Path.Combine(local, "FamilyTimeChef");
            }
            catch { }
            try
            {
                var dir = Path.GetDirectoryName(typeof(Settings).Assembly.Location);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch { }
            return ".";
        }

        public static string SessionPath => Path.Combine(Dir, "session.txt");
        private static string ConfigPath => Path.Combine(Dir, "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!Io.Exists(ConfigPath)) return s;

                foreach (var raw0 in Io.Read(ConfigPath).Split('\n'))
                {
                    var line = raw0.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = line.Substring(0, eq).Trim();
                    var v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "AutoFeed": s.AutoFeed = v == "1"; break;
                        case "AutoWater": s.AutoWater = v == "1"; break;
                        case "AutoSpawn": s.AutoSpawn = v == "1"; break;
                        case "FeedIntervalMs": s.FeedIntervalMs = Int(v, s.FeedIntervalMs); break;
                        case "WaterIntervalMs": s.WaterIntervalMs = Int(v, s.WaterIntervalMs); break;
                        case "SpawnIntervalMs": s.SpawnIntervalMs = Int(v, s.SpawnIntervalMs); break;
                        case "MaxActionsPerTick": s.MaxActionsPerTick = Int(v, s.MaxActionsPerTick); break;
                        case "MaxLiveFood": s.MaxLiveFood = Int(v, s.MaxLiveFood); break;
                        case "MaxInstances": s.MaxInstances = Int(v, s.MaxInstances); break;
                        case "FoodTemplateIndex": s.FoodTemplateIndex = Int(v, s.FoodTemplateIndex); break;
                        case "PerInstanceCooldownMs": s.PerInstanceCooldownMs = Flt(v, s.PerInstanceCooldownMs); break;
                    }
                }
            }
            catch { }
            return s;
        }

        private static int Int(string v, int fallback) =>
            int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;

        private static float Flt(string v, float fallback) =>
            float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : fallback;

        public void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Family Time Chef settings",
                    "AutoFeed=" + (AutoFeed ? "1" : "0"),
                    "AutoWater=" + (AutoWater ? "1" : "0"),
                    "AutoSpawn=" + (AutoSpawn ? "1" : "0"),
                    "FeedIntervalMs=" + FeedIntervalMs.ToString(CultureInfo.InvariantCulture),
                    "WaterIntervalMs=" + WaterIntervalMs.ToString(CultureInfo.InvariantCulture),
                    "SpawnIntervalMs=" + SpawnIntervalMs.ToString(CultureInfo.InvariantCulture),
                    "MaxActionsPerTick=" + MaxActionsPerTick.ToString(CultureInfo.InvariantCulture),
                    "MaxLiveFood=" + MaxLiveFood.ToString(CultureInfo.InvariantCulture),
                    "MaxInstances=" + MaxInstances.ToString(CultureInfo.InvariantCulture),
                    "FoodTemplateIndex=" + FoodTemplateIndex.ToString(CultureInfo.InvariantCulture),
                    "PerInstanceCooldownMs=" + PerInstanceCooldownMs.ToString(CultureInfo.InvariantCulture),
                };
                Io.Write(ConfigPath, string.Join("\n", lines.ToArray()));
            }
            catch { }
        }
    }
}
