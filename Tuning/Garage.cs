using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chassis.Api;
using HarmonyLib;
using Rollin.Chair;
using Rollin.Net;

namespace Rollin.Tuning
{
    /// <summary>
    /// Keeps each wheelchair's tuning while it is a prop. A chair vehicle lives only while someone sits in it, and
    /// Chassis drops a car's tuning with the car, so the host stores the tuning per prop key and gives it back to the
    /// next vehicle of that prop. The host writes the store next to the save (<c>Rollin.tuning</c>).
    ///
    /// A tuned chair stays a vehicle when its rider gets out. The store keeps where it was parked, and after loading the
    /// host puts it back there as a vehicle.
    ///
    /// Clients ask the host through Rollin's channel (<c>req.tune</c>); the host applies it with the Chassis tuning
    /// API, which accepts everything a mod does on the host.
    /// </summary>
    internal static class Garage
    {
        internal const string FileName = "Rollin.tuning";

        private static readonly Dictionary<string, Dictionary<string, string>> _byProp = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Vector4> _parked = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _cargo = new Dictionary<string, string>(StringComparer.Ordinal);
        private static int _applying = -1;
        private static string _loadedFrom;
        private static string _restoredFor;
        private static float _restoreAt = -1f;

        /// <summary>Seconds after the world is there before the parked chairs come back, so the props are found.</summary>
        private const float RestoreDelay = 3f;

        internal static void Init()
        {
            ChassisApi.TuningChanged += OnTuningChanged;
            PropSwap.Swapped += OnSwapped;
            PropSwap.KeepsVehicle = KeepsVehicle;
            Parts.RegisterAll();
        }

        internal static void Reset()
        {
            _byProp.Clear();
            _parked.Clear();
            _cargo.Clear();
            _applying = -1;
            _loadedFrom = null;
            _restoredFor = null;
            _restoreAt = -1f;
        }

        internal static void Tick()
        {
            if (!RollinNet.IsHost) return;
            EnsureLoaded();
            RestoreParked();
        }

        /// <summary>Whether a prop carries any tuning; such a chair stays a vehicle when it is empty.</summary>
        internal static bool IsTuned(string prop) => prop != null && _byProp.TryGetValue(prop, out var s) && s.Count > 0;

        /// <summary>An empty chair stays a vehicle while it is tuned or carries items in its box.</summary>
        private static bool KeepsVehicle(string prop) => IsTuned(prop) || Cargo.HasItems(PropSwap.VehicleOf(prop));

        /// <summary>Host, once per loaded save: every tuned chair becomes a vehicle again where it was parked.</summary>
        private static void RestoreParked()
        {
            if (_loadedFrom == null || _restoredFor == _loadedFrom || Player.Local == null) return;
            if (_restoreAt < 0f) { _restoreAt = Time.time + RestoreDelay; return; }
            if (Time.time < _restoreAt) return;
            _restoredFor = _loadedFrom;
            _restoreAt = -1f;
            int ok = 0, total = 0;
            var props = new HashSet<string>(_byProp.Keys, StringComparer.Ordinal);
            props.UnionWith(_cargo.Keys);
            foreach (var prop in props)
            {
                total++;
                bool has = _parked.TryGetValue(prop, out var at);
                if (PropSwap.HostPark(prop, has ? new Vector3(at.x, at.y, at.z) : (Vector3?)null, has ? at.w : 0f)) ok++;
            }
            Core.Log.Msg("[Garage] " + ok + "/" + total + " tuned wheelchair(s) parked again as vehicles.");
        }

        // ---- requests ----------------------------------------------------------------------------------------------

        /// <summary>Sets one tuning key on a chair: at once on the host, through the host on a client.</summary>
        internal static bool Request(LandVehicle lv, string key, string value, string why)
        {
            if (lv == null || !WheelchairType.Is(lv)) { Core.Log.Warning("[Garage] " + why + ": no wheelchair given - " + key + " ignored."); return false; }
            value = value ?? string.Empty;
            if (RollinNet.IsHost) return ChassisApi.SetTuning(lv, key, value);
            RollinNet.ToHost("req.tune", Ids.Of(lv).ToString(CultureInfo.InvariantCulture), key, value);
            Core.Log.Msg("[Garage] asked the host to set " + key + " = '" + value + "' on net " + Ids.Of(lv) + " (" + why + ").");
            return true;
        }

        /// <summary>Host: a client's request. Any player may tune a wheelchair; they belong to nobody.</summary>
        internal static void HostRequest(int vehicle, string key, string value)
        {
            var lv = Ids.Find<LandVehicle>(vehicle);
            if (lv == null || !WheelchairType.Is(lv)) { Core.Log.Warning("[Garage] tuning request for net " + vehicle + ", which is no wheelchair here - dropped."); return; }
            if (!ChassisApi.SetTuning(lv, key, value)) Core.Log.Warning("[Garage] Chassis refused " + key + " = '" + value + "' on net " + vehicle + " (reason above).");
        }

        // ---- store -------------------------------------------------------------------------------------------------

        private static void OnTuningChanged(LandVehicle lv, string key)
        {
            if (lv == null || !WheelchairType.Is(lv) || !RollinNet.IsHost) return;
            int id = Ids.Of(lv);
            if (id == _applying) return;
            string prop = PropSwap.KeyOfVehicle(id);
            if (prop == null) { Core.Log.Msg("[Garage] net " + id + " is no swapped prop (console spawn) - its tuning is not kept."); return; }
            var now = ChassisApi.Tuning(lv);
            if (now.Count == 0) { _byProp.Remove(prop); _parked.Remove(prop); }
            else _byProp[prop] = now;
            Core.Log.Msg("[Garage] prop " + prop + " keeps " + now.Count + " tuning key(s).");
        }

        /// <summary>Host: a prop became a vehicle; it gets the prop's tuning back.</summary>
        private static void OnSwapped(string prop, LandVehicle lv)
        {
            EnsureLoaded();
            if (_cargo.TryGetValue(prop, out var contents) && !Cargo.HasItems(lv))
            {
                Cargo.LoadLater(lv, contents);
                Core.Log.Msg("[Garage] prop " + prop + ": its box contents go back into net " + Ids.Of(lv) + ".");
            }
            if (!_byProp.TryGetValue(prop, out var saved) || saved.Count == 0) return;
            int id = Ids.Of(lv);
            int ok = 0;
            _applying = id;
            try
            {
                foreach (var kv in new List<KeyValuePair<string, string>>(saved))
                    if (ChassisApi.SetTuning(lv, kv.Key, kv.Value)) ok++;
                    else Core.Log.Warning("[Garage] prop " + prop + ": " + kv.Key + " = '" + kv.Value + "' was refused (part no longer registered?).");
            }
            finally { _applying = -1; }
            Core.Log.Msg("[Garage] prop " + prop + " is net " + id + " again with " + ok + "/" + saved.Count + " tuning key(s).");
        }

        /// <summary>The tuning a prop keeps, for the console.</summary>
        internal static string Describe(string prop)
        {
            if (prop == null || !_byProp.TryGetValue(prop, out var s)) return "stock";
            return Encode(s);
        }

        // ---- save file ---------------------------------------------------------------------------------------------

        private static string SavePath()
        {
            if (!Singleton<GameLoadManager>.InstanceExists) return null;
            string folder = Singleton<GameLoadManager>.Instance.LoadedGameFolderPath;
            return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : Path.Combine(folder, FileName);
        }

        private static void EnsureLoaded()
        {
            string path = SavePath();
            if (path == null || path == _loadedFrom) return;
            _loadedFrom = path;
            _byProp.Clear();
            _parked.Clear();
            _cargo.Clear();
            if (!File.Exists(path)) { Core.Log.Msg("[Garage] no " + FileName + " in this save yet."); return; }
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    if (line[0] == '@')
                    {
                        var f = line.Substring(bar + 1).Split(';');
                        if (f.Length == 4 && TryF(f[0], out float x) && TryF(f[1], out float y) && TryF(f[2], out float z) && TryF(f[3], out float yaw))
                            _parked[line.Substring(1, bar - 1)] = new Vector4(x, y, z, yaw);
                        else Core.Log.Warning("[Garage] unreadable parking line '" + line + "' - that chair comes back at its prop.");
                        continue;
                    }
                    if (line[0] == '$')
                    {
                        try { _cargo[line.Substring(1, bar - 1)] = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(bar + 1))); }
                        catch (FormatException) { Core.Log.Error("[Garage] unreadable box line for " + line.Substring(1, bar - 1) + " - those items are lost."); }
                        continue;
                    }
                    var s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string pair in line.Substring(bar + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) { Core.Log.Warning("[Garage] unreadable entry '" + pair + "' skipped."); continue; }
                        s[pair.Substring(0, eq)] = pair.Substring(eq + 1);
                    }
                    if (s.Count > 0) _byProp[line.Substring(0, bar)] = s;
                }
                Core.Log.Msg("[Garage] loaded the tuning of " + _byProp.Count + " wheelchair(s), " + _parked.Count + " parking spot(s), " + _cargo.Count + " filled box(es), from " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Garage] could not read " + path + ", the wheelchairs stay stock: " + e.Message); }
        }

        private static void Save()
        {
            string path = SavePath();
            if (path == null) { Core.Log.Warning("[Garage] no save folder - the wheelchair tuning stays in memory."); return; }
            EnsureLoaded();
            var sb = new StringBuilder();
            foreach (var kv in _byProp) sb.Append(kv.Key).Append('|').Append(Encode(kv.Value)).Append('\n');
            foreach (var prop in PropSwap.VehicleKeys())
            {
                string contents = Cargo.Serialize(PropSwap.VehicleOf(prop));
                if (contents != null) _cargo[prop] = contents;
                else _cargo.Remove(prop);
            }
            foreach (var kv in _cargo)
                sb.Append('$').Append(kv.Key).Append('|').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value))).Append('\n');
            var keep = new HashSet<string>(_byProp.Keys, StringComparer.Ordinal);
            keep.UnionWith(_cargo.Keys);
            foreach (var prop in keep)
            {
                var lv = PropSwap.VehicleOf(prop);
                if (lv != null) { var t = lv.transform; _parked[prop] = new Vector4(t.position.x, t.position.y, t.position.z, t.eulerAngles.y); }
                if (_parked.TryGetValue(prop, out var at))
                    sb.Append('@').Append(prop).Append('|').Append(F(at.x)).Append(';').Append(F(at.y)).Append(';').Append(F(at.z)).Append(';').Append(F(at.w)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            Core.Log.Msg("[Garage] saved the tuning of " + _byProp.Count + " wheelchair(s), " + _parked.Count + " parking spot(s), " + _cargo.Count + " filled box(es), to " + path + ".");
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static bool TryF(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static string Encode(Dictionary<string, string> s)
        {
            var sb = new StringBuilder();
            foreach (var kv in s)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// At the end of a save vanilla deletes every file in the folder no saveable claimed; the prefix reads the store
        /// before that, the postfix claims the name and writes it again.
        /// </summary>
        [HarmonyPatch(typeof(GameSaveManager), nameof(GameSaveManager.Save), new Type[] { typeof(string) })]
        private static class SaveHook
        {
            private static void Prefix()
            {
                if (!RollinNet.IsHost) return;
                try { EnsureLoaded(); }
                catch (Exception e) { Core.Log.Error("[Garage] reading " + FileName + " before the save failed: " + e); }
            }

            private static void Postfix(GameSaveManager __instance)
            {
                if (!RollinNet.IsHost) return;
                try
                {
                    var approved = __instance.ApprovedBaseLevelPaths;
                    if (approved == null) Core.Log.Warning("[Garage] the game has no list of kept files - " + FileName + " may be deleted at the end of the save.");
                    else if (!approved.Contains(FileName)) approved.Add(FileName);
                }
                catch (Exception e) { Core.Log.Error("[Garage] could not mark " + FileName + " as kept: " + e); }
                try { Save(); }
                catch (Exception e) { Core.Log.Error("[Garage] saving the wheelchair tuning failed: " + e); }
            }
        }
    }
}
