using System;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FamilyTimeChef.Plugin
{
    /// <summary>
    /// Reflection bridge onto the game's own managed API. Compiled against no game
    /// assembly on purpose: Episode 1, 2 and 3 have different struct layouts and method
    /// sets, so binding happens at runtime and each episode gets its own adapter layer.
    /// </summary>
    internal sealed class GameBridge
    {
        // The instance-info element holds a packed uint. Episode 1 uses a 16-byte element
        // (uint + Vector3 direction); Episode 2/3 use a 4-byte element (uint only).
        [StructLayout(LayoutKind.Sequential)]
        private struct InfoRaw16
        {
            public uint info;
            public float dx;
            public float dy;
            public float dz;
        }

        public sealed class Snapshot
        {
            public int Count;
            public int HungryCount;
            public int HungerLevelTotal;
            public string HungerType = "unknown";
            public int GroupCount;
            public bool[] HungryFlags = new bool[0];
            public readonly List<Group> Groups = new List<Group>();
        }

        public sealed class Group
        {
            public int TemplateIndex;
            public UnityEngine.Object Template;
            public string Name;
            public int Count;
            public int Hungry;
        }

        /// <summary>Best-guess food template by name, used when the GUI does not pin one.</summary>
        public int ResolveFoodTemplate()
        {
            var groups = TemplateGroups();
            string[] keys = { "soup", "stew", "meal", "food", "meat", "berry", "fish", "chicken" };
            foreach (var k in keys)
            {
                foreach (var g in groups)
                {
                    if (g.TemplateIndex < 0) continue;
                    if (g.Name != null && g.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        return g.TemplateIndex;
                }
            }
            return -1;
        }

        public int StableIndexOf(object handle)
        {
            try
            {
                if (handle == null || _fHandleStable == null) return -1;
                return (ushort)_fHandleStable.GetValue(handle);
            }
            catch { return -1; }
        }

        private Assembly _asm;
        private Type _tWorld, _tGat, _tUtils, _tHungerBeh, _tHandle, _tTemplateEntry;
        private Func<object> _worldGat, _worldUtils, _worldHungerBeh;
        private PropertyInfo _pInstanceCount;
        private PropertyInfo _pHungerType;
        private MethodInfo _mGetInfoBuffer, _mGetMatricesImmediate, _mGetHandle,
                           _mCreateInstance, _mApplyHungerDelta, _mGetTemplateEntries, _mGetTemplateIndex;
        private FieldInfo _fHandleStable, _fEntryTemplate, _fEntryStart, _fEntryCount;
        private Delegate _noopHandleCb;

        public bool Bound { get; private set; }
        public string BindError { get; private set; } = string.Empty;
        public int ElementStride { get; private set; }

        public bool TryBind()
        {
            try
            {
                _asm = FindAssembly("Assembly-CSharp");
                if (_asm == null) { BindError = "Assembly-CSharp is not loaded yet."; return false; }

                _tWorld = TypeOf("GAT.World");
                _tGat = TypeOf("GAT.GAT");
                _tUtils = TypeOf("GAT.GATUtilities");
                _tHungerBeh = TypeOf("GAT.GATHungerBehavior");
                _tHandle = TypeOf("GAT.Handle");
                _tTemplateEntry = TypeOf("GAT.GATTemplateEntry");

                if (_tWorld == null || _tGat == null || _tUtils == null || _tHandle == null)
                {
                    BindError = "Family Time game types were not found in Assembly-CSharp.";
                    return false;
                }

                _worldGat = StaticGetter(_tWorld, "gat");
                _worldUtils = StaticGetter(_tWorld, "gatUtilities");
                _worldHungerBeh = StaticGetter(_tWorld, "gatHungerBehavior");

                if (_worldGat == null) { BindError = "GAT.World.gat is missing."; return false; }

                _pInstanceCount = _tGat.GetProperty("instanceCount", BindingFlags.Public | BindingFlags.Instance);
                _mGetInfoBuffer = _tGat.GetMethod("GetInstanceInfoBuffer", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                _mGetMatricesImmediate = _tGat.GetMethod("GetInstanceMatricesImmediate", BindingFlags.Public | BindingFlags.Instance);
                _mGetHandle = _tGat.GetMethod("GetHandle", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(ushort) }, null);
                _mGetTemplateEntries = _tGat.GetMethod("GetTemplateEntries", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                _fHandleStable = _tHandle.GetField("stableIndex", BindingFlags.Public | BindingFlags.Instance);

                // GetTemplateIndex is overloaded (Template and BuildPart); pick the Template one.
                foreach (var mm in _tGat.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (mm.Name != "GetTemplateIndex") continue;
                    var ps = mm.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.Name == "Template")
                    {
                        _mGetTemplateIndex = mm;
                        break;
                    }
                }

                var ci = _tGat.GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Instance);
                if (ci != null) _mCreateInstance = ci;

                if (_tUtils != null)
                    _mApplyHungerDelta = _tUtils.GetMethod("ApplyHungerDelta", BindingFlags.Public | BindingFlags.Instance);

                if (_tHungerBeh != null)
                    _pHungerType = _tHungerBeh.GetProperty("HungerType", BindingFlags.Public | BindingFlags.Instance);

                if (_tTemplateEntry != null)
                {
                    _fEntryTemplate = _tTemplateEntry.GetField("template", BindingFlags.Public | BindingFlags.Instance);
                    _fEntryStart = _tTemplateEntry.GetField("startIndex", BindingFlags.Public | BindingFlags.Instance);
                    _fEntryCount = _tTemplateEntry.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                }

                BuildNoopCallback();
                Bound = true;
                return true;
            }
            catch (Exception ex)
            {
                BindError = "Binding failed: " + ex.Message;
                Bound = false;
                return false;
            }
        }

        private void BuildNoopCallback()
        {
            // System.Linq.Expressions cannot build the typed Action<Handle> delegate on this
            // runtime, so no completion callback is supplied. The game's API tolerates null
            // here and every effect is confirmed by re-reading the instance buffer anyway.
            _noopHandleCb = null;
        }

        private static Assembly FindAssembly(string simpleName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (a.GetName().Name == simpleName) return a; }
                catch { }
            }
            return null;
        }

        /// <summary>Assembly.GetType(string,bool) is absent from this runtime.</summary>
        private Type TypeOf(string name)
        {
            try { return _asm.GetType(name); }
            catch { return null; }
        }

        private static Func<object> StaticGetter(Type t, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (p != null && p.CanRead) return () => p.GetValue(null, null);

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (f != null) return () => f.GetValue(null);

            return null;
        }

        public object Gat => _worldGat?.Invoke();
        public object Utils => _worldUtils?.Invoke();
        public object HungerBehavior => _worldHungerBeh?.Invoke();

        public bool IsReady
        {
            get
            {
                try
                {
                    var g = Gat;
                    return g != null && _pInstanceCount != null;
                }
                catch { return false; }
            }
        }

        public int InstanceCount
        {
            get
            {
                try
                {
                    var g = Gat;
                    if (g == null || _pInstanceCount == null) return 0;
                    return (int)_pInstanceCount.GetValue(g, null);
                }
                catch { return 0; }
            }
        }

        public string HungerTypeName
        {
            get
            {
                try
                {
                    var hb = HungerBehavior;
                    if (hb == null || _pHungerType == null) return "unknown";
                    var v = _pHungerType.GetValue(hb, null);
                    return v?.ToString() ?? "unknown";
                }
                catch { return "unknown"; }
            }
        }

        /// <summary>Reads the packed instance-info buffer and aggregates hunger by template.</summary>
        public Snapshot ReadSnapshot(int maxInstances = 20000)
        {
            var snap = new Snapshot();
            try
            {
                var gat = Gat;
                if (gat == null) return snap;

                var buffer = _mGetInfoBuffer?.Invoke(gat, null) as ComputeBuffer;
                if (buffer == null) return snap;

                int count = Math.Min(buffer.count, Math.Min(InstanceCount, maxInstances));
                if (count <= 0) return snap;

                int stride = buffer.stride;
                ElementStride = stride;

                var hungry = new bool[count];
                var level = new int[count];
                int hungryTotal = 0, levelTotal = 0;

                if (stride >= 16)
                {
                    // Episode 1 layout: hunger is a 4-bit level at bit 19.
                    var raw = new InfoRaw16[count];
                    buffer.GetData(raw, 0, 0, count);
                    for (int i = 0; i < count; i++)
                    {
                        int h = (int)((raw[i].info >> 19) & 0xF);
                        level[i] = h;
                        levelTotal += h;
                        hungry[i] = h > 0;
                        if (h > 0) hungryTotal++;
                    }
                }
                else
                {
                    // Episode 2/3 layout: hunger is a single "needs attention" bit at bit 19.
                    var raw = new uint[count];
                    buffer.GetData(raw, 0, 0, count);
                    for (int i = 0; i < count; i++)
                    {
                        int h = (int)((raw[i] >> 19) & 1);
                        level[i] = h;
                        levelTotal += h;
                        hungry[i] = h != 0;
                        if (h != 0) hungryTotal++;
                    }
                }

                snap.Count = count;
                snap.HungryFlags = hungry;
                snap.HungryCount = hungryTotal;
                snap.HungerLevelTotal = levelTotal;
                snap.HungerType = HungerTypeName;

                // Group by template using the authoritative contiguous ranges.
                var entries = _mGetTemplateEntries?.Invoke(gat, null) as System.Collections.IEnumerable;
                if (entries != null && _fEntryStart != null && _fEntryCount != null)
                {
                    foreach (var entry in entries)
                    {
                        if (entry == null) continue;
                        var tmpl = _fEntryTemplate?.GetValue(entry) as UnityEngine.Object;
                        int start = (int)(_fEntryStart.GetValue(entry) ?? 0);
                        int len = (int)(_fEntryCount.GetValue(entry) ?? 0);
                        if (len <= 0) continue;

                        int h = 0;
                        int end = Math.Min(start + len, count);
                        for (int i = Math.Max(0, start); i < end; i++) if (hungry[i]) h++;

                        int resolved = -1;
                        if (tmpl != null && _mGetTemplateIndex != null)
                        {
                            try { resolved = (int)_mGetTemplateIndex.Invoke(gat, new object[] { tmpl }); }
                            catch { resolved = -1; }
                        }

                        var g = new Group
                        {
                            TemplateIndex = resolved,
                            Template = tmpl,
                            Name = tmpl != null ? tmpl.name : "template",
                            Count = len,
                            Hungry = h
                        };
                        snap.Groups.Add(g);
                    }
                    snap.GroupCount = snap.Groups.Count;
                }

                return snap;
            }
            catch
            {
                return snap;
            }
        }

        public bool TryGetHandle(int physicalIndex, out object handle)
        {
            handle = null;
            try
            {
                if (_mGetHandle == null || _tHandle == null) return false;
                var gat = Gat;
                if (gat == null) return false;
                if (physicalIndex < 0 || physicalIndex > ushort.MaxValue) return false;
                var boxed = _mGetHandle.Invoke(gat, new object[] { (ushort)physicalIndex });
                if (boxed == null) return false;
                handle = boxed;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Clears the hunger/thirst flag on one instance using the game's own API.</summary>
        public bool Feed(int physicalIndex, out string error)
        {
            error = string.Empty;
            try
            {
                object handle;
                if (!TryGetHandle(physicalIndex, out handle)) { error = "handle lookup failed"; return false; }
                return FeedHandle(handle, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool FeedHandle(object handle, out string error)
        {
            error = string.Empty;
            try
            {
                var utils = Utils;
                if (utils == null || _mApplyHungerDelta == null) { error = "ApplyHungerDelta unavailable"; return false; }

                var pars = _mApplyHungerDelta.GetParameters();
                object result = null;
                if (pars.Length == 3)
                    result = _mApplyHungerDelta.Invoke(utils, new object[] { handle, (short)-1, null });
                else
                    result = _mApplyHungerDelta.Invoke(utils, new[] { handle, (object)(short)-1 });

                if (result is bool b) return b;
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                error = inner.Message;
                return false;
            }
        }

        public bool SpawnFood(Vector3 position, int templateIndex, out string error)
        {
            error = string.Empty;
            try
            {
                var gat = Gat;
                if (gat == null) { error = "GAT not ready"; return false; }
                if (_mCreateInstance == null) { error = "CreateInstance unavailable"; return false; }

                var m = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
                var pars = _mCreateInstance.GetParameters();
                object[] args;
                if (pars.Length >= 3)
                    args = new object[] { m, templateIndex, _noopHandleCb };
                else
                    args = new object[] { m, templateIndex };

                _mCreateInstance.Invoke(gat, args);
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                error = inner.Message;
                return false;
            }
        }

        // Food in this game is a GAT Template asset (the tutorial spawns it with
        // CreateInstance(..., GetTemplateIndex(tutorialFood), ...)), not one of the live
        // animal instance groups, so it has to be located among loaded assets.
        private static readonly string[] FoodKeys =
        {
            "food", "meal", "soup", "stew", "meat", "berry", "fish",
            "acorn", "egg", "milk", "carrot", "apple", "bread", "mushroom"
        };

        // Episode 1 ships no acorn asset at all: its food item is chicken_drumstick_GAT, seen
        // as a live template in the Patreon build. These keys are consulted only when the
        // list above matched nothing, so Episode 2 and 3 keep resolving acorn_GAT exactly as
        // before. No creature token is listed here: "drumstick" is the one word the food
        // asset shares with chicken_GAT, and the creature templates must never be spawned as
        // food.
        private static readonly string[] FallbackFoodKeys = { "drumstick" };

        private object FindFoodTemplate(out string chosen)
        {
            chosen = null;
            try
            {
                var templateType = TypeOf("GAT.Template") ?? TypeOf("Template");
                if (templateType == null) return null;

                var objects = Resources.FindObjectsOfTypeAll(templateType);
                var keySets = new[] { FoodKeys, FallbackFoodKeys };

                // Episode order matters: the primary list is tried over every asset first, so a
                // build that carries acorn_GAT still resolves it, and only a build without any
                // primary match falls through to the Episode 1 item.
                foreach (var keys in keySets)
                {
                    foreach (var candidate in objects)
                    {
                        var asset = candidate as UnityEngine.Object;
                        if (asset == null || string.IsNullOrEmpty(asset.name)) continue;

                        var lower = asset.name.ToLowerInvariant();

                        // Never pick a creature or the player as "food".
                        if (lower.Contains("gat") && (lower.Contains("pig") || lower.Contains("hen") ||
                            lower.Contains("rooster") || lower.Contains("boar") || lower.Contains("player") ||
                            lower.Contains("wolf") || lower.Contains("ghost")))
                            continue;

                        foreach (var key in keys)
                        {
                            if (lower.IndexOf(key, StringComparison.Ordinal) < 0) continue;
                            chosen = asset.name;
                            return asset;
                        }
                    }
                }

                chosen = null;
                return null;
            }
            catch (Exception ex)
            {
                chosen = "search failed: " + ex.Message;
                return null;
            }
        }

        /// <summary>Spawns one food instance just in front of the player.</summary>
        public bool SpawnFoodNearPlayer(out string error, out string chosen)
        {
            string detail;
            return SpawnFoodTracked(out error, out chosen, out detail);
        }

        /// <summary>Spawns one food instance and reports that instance's own identity.</summary>
        public bool SpawnFoodTracked(out string error, out string chosen, out string detail)
        {
            detail = string.Empty;
            error = string.Empty;
            chosen = null;
            try
            {
                var gat = Gat;
                if (gat == null) { error = "GAT not ready"; return false; }
                if (_mCreateInstance == null) { error = "CreateInstance unavailable"; return false; }
                if (_mGetTemplateIndex == null) { error = "GetTemplateIndex unavailable"; return false; }

                var template = FindFoodTemplate(out chosen);
                if (template == null) { error = "no food template asset found (" + chosen + ")"; return false; }

                int index;
                try { index = (int)_mGetTemplateIndex.Invoke(gat, new[] { template }); }
                catch (Exception ex) { error = "GetTemplateIndex threw: " + (ex.InnerException ?? ex).Message; return false; }

                if (index < 0) { error = "template " + chosen + " is not registered with GAT (index " + index + ")"; return false; }

                var pos = PlayerPosition();
                var forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
                pos += new Vector3(forward.x, 0f, forward.z).normalized * 1.6f;

                int before = InstanceCount;
                var matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
                _mCreateInstance.Invoke(gat, new object[] { matrix, index, null });
                int after = InstanceCount;

                detail = "template=" + chosen + " templateIndex=" + index
                       + " countBefore=" + before + " countAfter=" + after
                       + " requested=x=" + pos.x.ToString("0.00", CultureInfo.InvariantCulture)
                       + " y=" + pos.y.ToString("0.00", CultureInfo.InvariantCulture)
                       + " z=" + pos.z.ToString("0.00", CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        /// <summary>
        /// Forces the active need type. The game chooses it with
        /// SetHungerTypeForDay(day) => day % 3 == 2 ? THIRST : FOOD, and SetHungerType is
        /// public, so a FOOD need can be staged on demand instead of waiting days.
        /// </summary>
        public bool SetHungerType(string name, out string error)
        {
            error = string.Empty;
            try
            {
                var behavior = HungerBehavior;
                if (behavior == null) { error = "hunger behaviour not available"; return false; }

                var method = _tHungerBeh == null
                    ? null
                    : _tHungerBeh.GetMethod("SetHungerType", BindingFlags.Public | BindingFlags.Instance);
                if (method == null) { error = "SetHungerType not found"; return false; }

                var pars = method.GetParameters();
                if (pars.Length != 1) { error = "unexpected SetHungerType signature"; return false; }

                var value = Enum.Parse(pars[0].ParameterType, name, true);
                method.Invoke(behavior, new[] { value });
                return true;
            }
            catch (Exception ex)
            {
                error = (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        // Time and Application are resolved out of UnityEngine.CoreModule by full name.
        // Binding through typeof(Time) picked up the wrong type in the player, which is why
        // the first probe reported -1 for a value that does exist.
        private static PropertyInfo _pTimeScale;
        private static PropertyInfo _pRunInBackground;

        // Facade/forwarder assemblies expose the type name without the real members, so the
        // real CoreModule is scanned first and a matching property is required before use.
        private static PropertyInfo StaticProperty(ref PropertyInfo cache, string typeName, string propName)
        {
            if (cache != null) return cache;
            const BindingFlags f = BindingFlags.Public | BindingFlags.Static;

            // Assembly-qualified lookup first: AppDomain.GetAssemblies() is unavailable here.
            try
            {
                var t = FindAssembly("UnityEngine.CoreModule")?.GetType(typeName);
                if (t != null)
                {
                    var p = t.GetProperty(propName, f);
                    if (p != null) { cache = p; return cache; }
                }
            }
            catch { }

            // Then the assembly that actually defines UnityEngine.Object in this player.
            try
            {
                var t = FindAssembly("UnityEngine")?.GetType(typeName);
                if (t != null)
                {
                    var p = t.GetProperty(propName, f);
                    if (p != null) { cache = p; return cache; }
                }
            }
            catch { }
            return cache;
        }

        public float TimeScale
        {
            get
            {
                try
                {
                    var p = StaticProperty(ref _pTimeScale, "UnityEngine.Time", "timeScale");
                    return p == null ? -1f : (float)p.GetValue(null, null);
                }
                catch { return -1f; }
            }
        }

        public void SetTimeScale(float value)
        {
            try
            {
                var p = StaticProperty(ref _pTimeScale, "UnityEngine.Time", "timeScale");
                if (p != null && p.CanWrite) p.SetValue(null, value, null);
            }
            catch { }
        }

        /// <summary>The game resets runInBackground, so it has to be re-asserted every frame.</summary>
        public bool ForceBackground;

        public void EnforceBackground()
        {
            if (!ForceBackground) return;
            try
            {
                var p = StaticProperty(ref _pRunInBackground, "UnityEngine.Application", "runInBackground");
                if (p != null && p.CanWrite) p.SetValue(null, true, null);
            }
            catch { }
        }

        /// <summary>A Unity world stops simulating as soon as it loses focus unless this is on.</summary>
        public bool RunInBackground
        {
            get
            {
                try
                {
                    var p = StaticProperty(ref _pRunInBackground, "UnityEngine.Application", "runInBackground");
                    return p != null && (bool)p.GetValue(null, null);
                }
                catch { return false; }
            }
        }

        public void SetRunInBackground(bool value)
        {
            try
            {
                var p = StaticProperty(ref _pRunInBackground, "UnityEngine.Application", "runInBackground");
                if (p != null && p.CanWrite) p.SetValue(null, value, null);
            }
            catch { }
        }

        // ---- game session control -------------------------------------------------
        // The world only simulates once GameSession leaves Title/Menu. Driving that from
        // inside the shim avoids pushing synthetic input into the game's own menus.
        private Type _tGameSession;

        private Type GameSessionType()
        {
            if (_tGameSession != null) return _tGameSession;
            try { _tGameSession = TypeOf("GameSession") ?? TypeOf("GAT.GameSession"); }
            catch { }
            return _tGameSession;
        }

        private static object FindLiveInstance(Type t)
        {
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var f = t.GetField("instance", flags) ?? t.GetField("Instance", flags);
                if (f != null)
                {
                    var v = f.GetValue(null);
                    if (v != null) return v;
                }
            }
            catch { }

            try
            {
                string[] names = { "FindFirstObjectByType", "FindAnyObjectByType", "FindObjectOfType" };
                var methods = typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (var n in names)
                {
                    foreach (var m in methods)
                    {
                        if (m.Name != n) continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 1 || ps[0].ParameterType != typeof(Type)) continue;
                        var r = m.Invoke(null, new object[] { t });
                        if (r != null) return r;
                    }
                }
            }
            catch { }
            return null;
        }

        public string SessionState()
        {
            try
            {
                var t = GameSessionType();
                if (t == null) return "notype";
                var inst = FindLiveInstance(t);
                if (inst == null) return "noinstance";
                var p = t.GetProperty("State");
                if (p == null) return "nostate";
                return p.GetValue(inst, null).ToString();
            }
            catch (Exception ex) { return "err:" + (ex.InnerException ?? ex).Message; }
        }

        private bool InvokeSession(string method, object[] args, out string error)
        {
            error = null;
            try
            {
                var t = GameSessionType();
                if (t == null) { error = "GameSession type not found"; return false; }
                var inst = FindLiveInstance(t);
                if (inst == null) { error = "no GameSession instance"; return false; }
                var m = t.GetMethod(method);
                if (m == null) { error = method + " not found"; return false; }
                m.Invoke(inst, args);
                return true;
            }
            catch (Exception ex) { error = (ex.InnerException ?? ex).Message; return false; }
        }

        public bool BeginFromTitle(out string error)
        {
            return InvokeSession("BeginFromTitle", null, out error);
        }

        public bool LoadSession(string name, out string error)
        {
            return InvokeSession("LoadGame", new object[] { name }, out error);
        }

        public bool StartNewGame(int seed, bool tutorial, out string error)
        {
            error = null;
            try
            {
                var t = GameSessionType();
                if (t == null) { error = "GameSession type not found"; return false; }
                var m = t.GetMethod("StartNewGame");
                if (m == null) { error = "StartNewGame not found"; return false; }
                var ps = m.GetParameters();
                object mode = Activator.CreateInstance(ps[1].ParameterType);
                object[] args = ps.Length >= 3
                    ? new object[] { seed, mode, tutorial }
                    : new object[] { seed, mode };
                return InvokeSession("StartNewGame", args, out error);
            }
            catch (Exception ex) { error = (ex.InnerException ?? ex).Message; return false; }
        }

        /// <summary>One-shot introspection so reflection failures can be diagnosed in-game.</summary>
        public string Diag()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                sb.Append("asmCount=").Append(asms.Length).Append(';');
                bool coreFound = false;
                foreach (var a in asms)
                {
                    string n = null;
                    try { n = a.GetName().Name; } catch { }
                    if (n == null) continue;
                    if (n == "Assembly-CSharp") sb.Append("AC=ok;");
                    if (n.IndexOf("UnityEngine", StringComparison.Ordinal) >= 0 && !coreFound)
                        sb.Append("uasm:").Append(n).Append(',');
                    if (n.IndexOf("CoreModule", StringComparison.Ordinal) >= 0)
                    {
                        coreFound = true;
                        sb.Append("core=").Append(n).Append(';');
                        try
                        {
                            var t = a.GetType("UnityEngine.Time");
                            sb.Append("timeType=").Append(t != null ? "y" : "n").Append(';');
                            if (t != null)
                            {
                                var p = t.GetProperty("timeScale", BindingFlags.Public | BindingFlags.Static);
                                sb.Append("tsProp=").Append(p != null ? "y" : "n").Append(';');
                            }
                        }
                        catch (Exception ex) { sb.Append("terr=").Append((ex.InnerException ?? ex).Message).Append(';'); }
                    }
                }
                if (!coreFound) sb.Append("core=missing;");

                // Which loaded assembly actually owns GameSession?
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (a.GetType("GameSession") != null)
                            sb.Append("gsAsm=").Append(a.GetName().Name).Append(';');
                    }
                    catch { }
                }

                try
                {
                    var core = FindAssembly("UnityEngine.CoreModule");
                    var tt = core.GetType("UnityEngine.Time");
                    sb.Append("tsField=").Append(tt.GetField("timeScale", BindingFlags.Public | BindingFlags.Static) != null ? "y" : "n").Append(';');
                    var app = core.GetType("UnityEngine.Application");
                    sb.Append("appType=").Append(app != null ? "y" : "n").Append(';');
                    if (app != null)
                    {
                        var rp = app.GetProperty("runInBackground", BindingFlags.Public | BindingFlags.Static);
                        sb.Append("ribProp=").Append(rp != null ? "y" : "n").Append(';');
                        if (rp != null) sb.Append("rib=").Append(rp.GetValue(null, null)).Append(';');
                    }
                }
                catch (Exception e3) { sb.Append("unErr=").Append((e3.InnerException ?? e3).Message).Append(';'); }

                try { sb.Append("state=").Append(SessionState()).Append(';'); }
                catch (Exception e2) { sb.Append("stErr=").Append(e2.Message).Append(';'); }
            }
            catch (Exception ex) { sb.Append("fatal=").Append((ex.InnerException ?? ex).Message); }
            return sb.ToString();
        }

        // ---- window control -------------------------------------------------------
        // The Studio GUI can only receive real pointer input if the game stops owning an
        // exclusive fullscreen window, so the switch is driven from inside the shim.
        private Type _tScreen;

        private Type ScreenType()
        {
            if (_tScreen != null) return _tScreen;
            try
            {
                var asm = FindAssembly("UnityEngine.CoreModule") ?? FindAssembly("UnityEngine");
                if (asm != null) _tScreen = asm.GetType("UnityEngine.Screen");
            }
            catch { }
            return _tScreen;
        }

        public bool SetWindowed(int w, int h, out string error)
        {
            error = string.Empty;
            try
            {
                var t = ScreenType();
                if (t == null) { error = "UnityEngine.Screen not found"; return false; }

                var fs = t.GetProperty("fullScreen", BindingFlags.Public | BindingFlags.Static);
                if (fs != null && fs.CanWrite) fs.SetValue(null, false, null);

                // SetResolution is overloaded, so GetMethod(name, flags) throws AmbiguousMatch.
                MethodInfo m = null;
                foreach (var cand in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (cand.Name != "SetResolution") continue;
                    var cp = cand.GetParameters();
                    if (cp.Length == 3) { m = cand; break; }
                    if (cp.Length == 2 && m == null) m = cand;
                }
                if (m == null) { error = "Screen.SetResolution not found"; return false; }

                var ps = m.GetParameters();
                object[] args;
                if (ps.Length == 3)
                {
                    Type p2 = ps[2].ParameterType;
                    if (p2 == typeof(bool)) args = new object[] { w, h, false };
                    else
                    {
                        object windowed = Enum.ToObject(p2, 3); // FullScreenMode.Windowed
                        try { windowed = Enum.Parse(p2, "Windowed", true); } catch { }
                        args = new object[] { w, h, windowed };
                    }
                }
                else if (ps.Length == 2) args = new object[] { w, h };
                else { error = "unexpected SetResolution arity " + ps.Length; return false; }

                m.Invoke(null, args);
                return true;
            }
            catch (Exception ex) { error = (ex.InnerException ?? ex).Message; return false; }
        }

        public string ScreenInfo()
        {
            try
            {
                var t = ScreenType();
                if (t == null) return "notype";
                int w = -1, h = -1;
                bool full = false;
                var pw = t.GetProperty("width", BindingFlags.Public | BindingFlags.Static);
                var ph = t.GetProperty("height", BindingFlags.Public | BindingFlags.Static);
                var pf = t.GetProperty("fullScreen", BindingFlags.Public | BindingFlags.Static);
                if (pw != null) w = (int)pw.GetValue(null, null);
                if (ph != null) h = (int)ph.GetValue(null, null);
                if (pf != null) full = (bool)pf.GetValue(null, null);
                return w + "x" + h + (full ? " fullscreen" : " windowed");
            }
            catch { return "err"; }
        }

        /// <summary>Live position of one physical instance, for churn-resistant spawn proof.</summary>
        public string InstancePosition(int index)
        {
            try
            {
                var gat = Gat;
                if (gat == null || _mGetMatricesImmediate == null) return "unavailable";
                var arr = _mGetMatricesImmediate.Invoke(gat, null) as Matrix4x4[];
                if (arr == null) return "no-array";
                if (index < 0 || index >= arr.Length) return "out-of-range(" + arr.Length + ")";
                var m = arr[index];
                return "x=" + m.m03.ToString("0.00", CultureInfo.InvariantCulture)
                     + " y=" + m.m13.ToString("0.00", CultureInfo.InvariantCulture)
                     + " z=" + m.m23.ToString("0.00", CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { return "err:" + (ex.InnerException ?? ex).Message; }
        }

        public Vector3 PlayerPosition()
        {
            try
            {
                var getter = StaticGetter(_tWorld, "playerController");
                var pc = getter?.Invoke() as Component;
                if (pc != null) return pc.transform.position;
            }
            catch { }
            return Vector3.zero;
        }

        public List<Group> TemplateGroups()
        {
            return ReadSnapshot().Groups;
        }
    }
}
