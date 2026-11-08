using System;
using System.Collections.Generic;
using UnityEngine;

namespace FamilyTimeChef.Plugin
{
    /// <summary>
    /// Schedules the automatic actions. Every write goes through the same guards:
    /// supported build only, the live hunger bit must still be set, a per-target
    /// cooldown, and a hard action budget per tick. That prevents duplicate feeds and
    /// stops the tool from hammering the GPU pipeline.
    /// </summary>
    internal sealed class Automation
    {
        private readonly ChefPlugin _host;
        private readonly GameBridge _bridge;
        private readonly Settings _cfg;

        private float _lastFeedAt = -999f;
        private float _lastWaterAt = -999f;
        private float _lastSpawnAt = -999f;
        private readonly Dictionary<int, float> _lastTarget = new Dictionary<int, float>();

        public int FeedsThisSession { get; private set; }
        public int WatersThisSession { get; private set; }
        public int SpawnsThisSession { get; private set; }
        public int BlockedActions { get; private set; }

        public Automation(ChefPlugin host, GameBridge bridge, Settings cfg)
        {
            _host = host;
            _bridge = bridge;
            _cfg = cfg;
        }

        private static float Now => Time.realtimeSinceStartup;

        private bool Cooling(int physical)
        {
            if (_lastTarget.TryGetValue(physical, out var t))
                return (Now - t) * 1000f < _cfg.PerInstanceCooldownMs;
            return false;
        }

        private void MarkTarget(int physical) => _lastTarget[physical] = Now;

        /// <summary>Called from the plugin's Update; never blocks.</summary>
        public void Tick()
        {
            if (!_bridge.IsReady) return;
            if (!_host.Build.Supported) return;

            if (_cfg.AutoFeed && (Now - _lastFeedAt) * 1000f >= _cfg.FeedIntervalMs)
            {
                _lastFeedAt = Now;
                var r = FeedHungry(false);
                if (r.Actions > 0) _host.Log($"Auto-feed: satisfied {r.Actions} child need(s).");
            }

            if (_cfg.AutoWater && (Now - _lastWaterAt) * 1000f >= _cfg.WaterIntervalMs)
            {
                _lastWaterAt = Now;
                var r = WaterThirsty(false);
                if (r.Actions > 0) _host.Log($"Auto-water: delivered water to {r.Actions} child(ren).");
                else if (r.Message != null) _host.Log("Auto-water: " + r.Message);
            }

            if (_cfg.AutoSpawn && (Now - _lastSpawnAt) * 1000f >= _cfg.SpawnIntervalMs)
            {
                _lastSpawnAt = Now;
                var r = SpawnFood(_cfg.FoodTemplateIndex);
                if (r.Ok) _host.Log("Auto-spawn: placed a food item near the player.");
                else if (r.Message != null) _host.Log("Auto-spawn skipped: " + r.Message);
            }
        }

        public struct Result
        {
            public bool Ok;
            public int Actions;
            public string Message;
        }

        /// <summary>Clears the food-hunger flag for hungry instances.</summary>
        public Result FeedHungry(bool manual)
        {
            var res = new Result();
            if (!_host.Build.Supported)
            {
                res.Message = "This build is not supported; writes are disabled.";
                return res;
            }

            var snap = _bridge.ReadSnapshot(_cfg.MaxInstances);
            if (snap.Count == 0) { res.Message = "No live instances yet (still loading?)."; return res; }

            var type = snap.HungerType;
            if (manual && type != "unknown" && type != "FOOD")
            {
                res.Message = $"Current need is {type}; use Give Water for this day.";
                return res;
            }

            int budget = Math.Max(1, _cfg.MaxActionsPerTick);
            int done = 0;
            for (int i = 0; i < snap.Count && done < budget; i++)
            {
                if (i >= snap.HungryFlags.Length) break;
                if (!snap.HungryFlags[i]) continue;
                if (Cooling(i)) { res.Message = res.Message ?? "Some targets were on cooldown."; continue; }

                string err;
                if (_bridge.Feed(i, out err))
                {
                    MarkTarget(i);
                    done++;
                    FeedsThisSession++;
                }
                else
                {
                    BlockedActions++;
                    res.Message = "Feed failed: " + err;
                }
            }

            res.Ok = done > 0;
            res.Actions = done;
            if (done == 0 && res.Message == null) res.Message = "No hungry children right now.";
            return res;
        }

        /// <summary>Delivers water on thirst days (Episode 2/3 expose a thirst need).</summary>
        public Result WaterThirsty(bool manual)
        {
            var res = new Result();
            if (!_host.Build.Supported)
            {
                res.Message = "This build is not supported; writes are disabled.";
                return res;
            }

            if (_host.Build.Episode < 2)
            {
                res.Message = "Episode 1 has no thirst mechanic, so water is not applicable.";
                return res;
            }

            var snap = _bridge.ReadSnapshot(_cfg.MaxInstances);
            if (snap.Count == 0) { res.Message = "No live instances yet (still loading?)."; return res; }

            if (snap.HungerType != "THIRST")
            {
                res.Message = "Today's need is FOOD; water applies on thirst days (every third day).";
                return res;
            }

            int budget = Math.Max(1, _cfg.MaxActionsPerTick);
            int done = 0;
            for (int i = 0; i < snap.Count && done < budget; i++)
            {
                if (i >= snap.HungryFlags.Length) break;
                if (!snap.HungryFlags[i]) continue;
                if (Cooling(i)) continue;

                string err;
                if (_bridge.Feed(i, out err))
                {
                    MarkTarget(i);
                    done++;
                    WatersThisSession++;
                }
                else
                {
                    BlockedActions++;
                    res.Message = "Water failed: " + err;
                }
            }

            res.Ok = done > 0;
            res.Actions = done;
            if (done == 0 && res.Message == null) res.Message = "No thirsty children right now.";
            return res;
        }

        public Result SpawnFood(int templateIndex)
        {
            var res = new Result();
            if (!_host.Build.Supported) { res.Message = "Unsupported build; writes disabled."; return res; }

            string err, chosen;
            if (!_bridge.SpawnFoodNearPlayer(out err, out chosen))
            {
                res.Message = "Spawn failed: " + err;
                return res;
            }

            SpawnsThisSession++;
            res.Ok = true;
            res.Actions = 1;
            res.Message = "spawned " + chosen + " in front of the player.";
            return res;
        }

    }
}
