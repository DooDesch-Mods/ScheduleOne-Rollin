#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using HarmonyLib;
using Rollin.Chair;
using Rollin.Net;
using Rollin.Tuning;

namespace Rollin.Debugging
{
    /// <summary>
    /// Debug console commands under <c>rollin &lt;subcommand&gt;</c>: sit, push, spawn and tune the wheelchair. Console,
    /// not hotkeys, so every check can be scripted through the bridge. Driving, the orbit camera, first person and
    /// leaving the seat stay with Chassis (<c>chassis drive</c>, <c>chassis cam</c>, <c>chassis view</c>,
    /// <c>chassis exit</c>), since they work for every vehicle.
    ///
    /// Both Console.SubmitCommand overloads are patched (string + List&lt;string&gt;): depending on the caller either
    /// one fires, and dispatch dedupes per frame and signature so a command never runs twice.
    /// </summary>
    internal static class DebugConsolePatch
    {
        internal const string Word = "rollin";
        internal const string Usage = "rollin help, rollin sit, rollin push, rollin tune max, rollin menu";

        private static readonly List<string> _pending = new List<string>();

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(GameStringList args)
        {
            if (args == null || args.Count == 0) return false;
            string[] parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0) return false;
            if (!string.Equals(parts[0], Word, StringComparison.OrdinalIgnoreCase)) return false;

            string sig = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;
            _lastFrame = frame; _lastSig = sig;

            string sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "help";
            try
            {
                switch (sub)
                {
                    case "help":
                        Say("status                        - swapped props, pushers, tuning store and moving parts");
                        Say("props                         - every wheelchair prop with its index and position");
                        Say("sit [tp <i>]                  - sit on the nearest wheelchair prop (as the Sit prompt); tp: go to prop i first");
                        Say("push [off|fake]               - push the nearest occupied wheelchair, or let go; fake: push an empty one");
                        Say("pushin <fwd> <steer> [s]      - pusher input for s seconds (default 3)");
                        Say("spawn | despawn               - wheelchair vehicle 2 m ahead (host) | remove all");
                        Say("tune <slot> <part id|label|n> - put a part on your chair (or the nearest); 'none' takes it off");
                        Say("tune set <key> <value>        - any tuning key (paint, paint2, neon, horn, part.<slot>)");
                        Say("tune max | clear | show       - the full silly build | stock | what the chair wears");
                        Say("parts [slot]                  - every Rollin part with its id");
                        Say("menu [open|close|up|down|left|right] - the T menu without keys");
                        Say("(drive, camera, first person and exit: chassis drive | chassis cam | chassis view | chassis exit)");
                        break;

                    case "status":
                        Say(PropSwap.Describe());
                        Say(Push.Describe());
                        Say(Effects.Describe());
                        Say("menu " + (Menu.IsOpen ? "open" : "closed"));
                        foreach (var lv in Chairs())
                        {
                            string prop = PropSwap.KeyOfVehicle(Ids.Of(lv));
                            Say("net " + Ids.Of(lv) + " at " + lv.transform.position.ToString("F1") + (lv.IsOccupied ? " occupied" : " empty") + ", prop " + (prop ?? "none")
                                + ", tuning " + Describe(ChassisApi.Tuning(lv)) + ", kept " + Garage.Describe(prop));
                        }
                        break;

                    case "props":
                    {
                        var props = PropSwap.FindProps();
                        var lp = Player.Local;
                        for (int i = 0; i < props.Count; i++)
                            Say(i + ": " + props[i].name + " at " + props[i].position.ToString("F1") + (lp != null ? ", " + Vector3.Distance(lp.transform.position, props[i].position).ToString("F0") + " m" : ""));
                        Say("props: " + props.Count);
                        break;
                    }

                    case "sit":
                    {
                        var lp = Player.Local;
                        if (lp == null) { Say("sit: no local player"); break; }
                        var props = PropSwap.FindProps();
                        Transform best = null; float bd = float.MaxValue;
                        if (parts.Length > 3 && parts[2] == "tp" && int.TryParse(parts[3], out int ti) && ti >= 0 && ti < props.Count)
                        {
                            // Stand 1.6 m in front of prop ti first; the Sit then comes from there.
                            best = props[ti];
                            Vector3 fwd = Vector3.ProjectOnPlane(best.forward, Vector3.up).normalized;
                            PlayerSingleton<PlayerMovement>.Instance.Teleport(best.position + fwd * 1.6f + Vector3.up * 0.1f, true);
                            bd = 1.6f;
                        }
                        else foreach (var c in props)
                        {
                            float d = Vector3.Distance(c.position, lp.transform.position);
                            if (d < bd) { bd = d; best = c; }
                        }
                        if (best == null) { Say("sit: no wheelchair prop"); break; }
                        RollinNet.ToHost("req.sit", PropSwap.KeyOf(best), Ids.Of(lp).ToString(CultureInfo.InvariantCulture));
                        Say("sit: asked for " + PropSwap.KeyOf(best) + " at " + bd.ToString("F1") + " m");
                        break;
                    }

                    case "push":
                    {
                        var lp = Player.Local;
                        if (lp == null) { Say("push: no local player"); break; }
                        var lv = Nearest(lp.transform.position, false);
                        if (parts.Length > 2 && parts[2] == "off")
                        {
                            if (lv == null) { Say("push off: no wheelchair"); break; }
                            Push.Request(Ids.Of(lv), false);
                            Say("push: release asked for net " + Ids.Of(lv));
                            break;
                        }
                        if (lv == null) { Say("push: no wheelchair vehicle"); break; }
                        // fake: an empty chair counts as occupied, so one player can test pushing alone.
                        if (parts.Length > 2 && parts[2] == "fake") { lv.IsOccupied = true; Say("push: net " + Ids.Of(lv) + " marked occupied"); }
                        Push.Request(Ids.Of(lv), true);
                        Say("push: asked for net " + Ids.Of(lv) + " at " + Vector3.Distance(lv.transform.position, lp.transform.position).ToString("F1") + " m");
                        break;
                    }

                    case "pushin":
                    {
                        float f = F(parts, 2, 1f), st = F(parts, 3, 0f), sec = F(parts, 4, 3f);
                        Push.DebugPush = new Vector2(f, st);
                        Push.DebugPushUntil = Time.time + sec;
                        Say("pushin: forward " + f + " steer " + st + " for " + sec + " s");
                        break;
                    }

                    case "spawn":
                    {
                        var local = Player.Local;
                        if (local == null) { Say("spawn: no local player"); break; }
                        Vector3 fwd = Vector3.ProjectOnPlane(local.transform.forward, Vector3.up).normalized;
                        var lv = WheelchairSpawn.Spawn(local.transform.position + fwd * 2f + Vector3.up * 0.3f, Quaternion.LookRotation(fwd, Vector3.up), "console");
                        Say(lv != null ? "spawned wheelchair net " + Ids.Of(lv) : "spawn failed, see the log");
                        break;
                    }

                    case "despawn":
                    {
                        int n = 0;
                        foreach (var lv in Chairs()) { WheelchairSpawn.Despawn(lv, "console"); n++; }
                        Say("despawned " + n);
                        break;
                    }

                    case "tune":
                        Tune(parts);
                        break;

                    case "parts":
                    {
                        string slot = parts.Length > 2 ? parts[2] : null;
                        int n = 0;
                        foreach (var p in Parts.All)
                        {
                            if (slot != null && !string.Equals(p.Slot, slot, StringComparison.OrdinalIgnoreCase)) continue;
                            Say(p.Slot + ": " + p.Id + " - " + p.Label + (p.Stats != null ? " (" + p.Stats + ")" : ""));
                            n++;
                        }
                        foreach (var h in Horns.All) if (slot == null || slot == "horn") Say("horn: " + h.Name + " - " + h.Label);
                        if (slot == null || slot == "horn") Say("horn: " + Sounds.BicycleHorn + " - Bicycle Horn");
                        Say("parts: " + n);
                        break;
                    }

                    case "menu":
                    {
                        string op = parts.Length > 2 ? parts[2].ToLowerInvariant() : "open";
                        switch (op)
                        {
                            case "open":
                                var lv = Menu.LocalChair();
                                if (lv == null) { Say("menu: drive a wheelchair first"); break; }
                                Menu.Open(lv, "console");
                                break;
                            case "close": Menu.Close("console"); break;
                            case "up": Menu.Move(-1); break;
                            case "down": Menu.Move(1); break;
                            case "left": Menu.Cycle(-1); break;
                            case "right": Menu.Cycle(1); break;
                            default: Say("menu: open|close|up|down|left|right"); break;
                        }
                        Say("menu " + (Menu.IsOpen ? "open" : "closed"));
                        break;
                    }

                    default:
                        Say("unknown '" + sub + "', try 'rollin help'");
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log.Error("[Console] '" + sig + "' failed: " + e);
                Say("failed: " + e.Message);
            }
            return true;
        }

        /// <summary>The full build for screenshots: one of everything, loud colours.</summary>
        private static readonly string[][] MaxBuild =
        {
            new[] { "paint", "FF3EA5,metallic" },
            new[] { "paint2", "FFC423,chrome" },
            new[] { "neon", "2EF2FF,fblr" },
            new[] { "horn", "rollin_cucaracha" },
            new[] { "part.spoiler", "rollin_spoiler_gt" },
            new[] { "part.booster", "rollin_rocket_twin" },
            new[] { "part.exhaust", "rollin_exhaust_twin" },
            new[] { "part.rims", "rollin_rims_racing" },
            new[] { "part.hat", "rollin_hat_crown" },
            new[] { "part.mascot", "rollin_duck" },
            new[] { "part.roof", "rollin_flag" },
            new[] { "part.lights", "rollin_lightbar" },
            new[] { "part.audio", "rollin_subwoofer" },
            new[] { "part.cargo", "rollin_soda" },
            new[] { "part.decal", "rollin_flames" },
            new[] { "part.engine", "rollin_engine_2" },
            new[] { "part.tyres", "rollin_tyres_grip" },
        };

        /// <summary>Logs every tuning renderer's world bounds in chair space (+Z forward), to check part placement.</summary>
        private static void Where(LandVehicle lv)
        {
            var root = lv.transform;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<UnityEngine.Renderer>(true))
            {
                string path = r.name;
                bool tuned = false;
                for (var t = r.transform; t != null && t != root; t = t.parent)
                {
                    if (t != r.transform) path = t.name + "/" + path;
                    if (t.name.StartsWith("Chassis_Tune_", StringComparison.Ordinal)) tuned = true;
                }
                if (!tuned) continue;
                var b = r.bounds;
                var c = root.InverseTransformPoint(b.center);
                Core.Log.Msg("[where] " + path + " centre " + V(c) + " size " + V(b.size) + (r.enabled && r.gameObject.activeInHierarchy ? "" : " (hidden)"));
                n++;
            }
            Say("tune where: " + n + " renderer(s) logged");
        }

        private static string V(UnityEngine.Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);

        private static void Tune(string[] parts)
        {
            var lp = Player.Local;
            var lv = Menu.LocalChair() ?? (lp != null ? Nearest(lp.transform.position, false) : null);
            if (lv == null) { Say("tune: no wheelchair"); return; }
            string op = parts.Length > 2 ? parts[2].ToLowerInvariant() : "show";
            switch (op)
            {
                case "show":
                    Say("tune: net " + Ids.Of(lv) + " " + Describe(ChassisApi.Tuning(lv)) + ", stats " + ChassisApi.TuningStatsOf(lv));
                    return;
                case "clear":
                    Say("tune clear: " + ChassisApi.ClearTuning(lv));
                    return;
                case "max":
                {
                    int ok = 0;
                    foreach (var kv in MaxBuild) if (Garage.Request(lv, kv[0], kv[1], "console max")) ok++;
                    Say("tune max: " + ok + "/" + MaxBuild.Length + " key(s) on net " + Ids.Of(lv));
                    return;
                }
                case "where":
                    Where(lv);
                    return;
                case "set":
                {
                    if (parts.Length < 4) { Say("tune set <key> <value>"); return; }
                    string value = parts.Length > 4 ? string.Join(" ", parts, 4, parts.Length - 4) : "";
                    if (value == "none") value = "";
                    Say("tune set " + parts[3] + " = '" + value + "': " + Garage.Request(lv, parts[3], value, "console"));
                    return;
                }
            }

            // tune <slot> <id|label|index|none>
            string slot = parts[2];
            if (parts.Length < 4) { Say("tune " + slot + " <part id|label|n|none>"); return; }
            string want = string.Join(" ", parts, 3, parts.Length - 3);
            var inSlot = Parts.All.FindAll(p => string.Equals(p.Slot, slot, StringComparison.OrdinalIgnoreCase));
            if (inSlot.Count == 0) { Say("tune: no Rollin parts in slot '" + slot + "' - see 'rollin parts'"); return; }
            string id = null;
            if (want == "none" || want == "stock") id = "";
            else if (int.TryParse(want, out int idx) && idx >= 1 && idx <= inSlot.Count) id = inSlot[idx - 1].Id;
            else foreach (var p in inSlot)
                    if (string.Equals(p.Id, want, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Label, want, StringComparison.OrdinalIgnoreCase)
                        || p.Id.EndsWith("_" + want, StringComparison.OrdinalIgnoreCase)) { id = p.Id; break; }
            if (id == null) { Say("tune: no part '" + want + "' in " + slot); return; }
            Say("tune " + slot + " = '" + id + "': " + Garage.Request(lv, "part." + slot.ToLowerInvariant(), id, "console"));
        }

        private static string Describe(Dictionary<string, string> s)
        {
            if (s == null || s.Count == 0) return "stock";
            var list = new List<string>();
            foreach (var kv in s) list.Add(kv.Key + "=" + kv.Value);
            return string.Join(";", list);
        }

        private static List<LandVehicle> Chairs()
        {
            var list = new List<LandVehicle>();
            foreach (var v in UnityEngine.Object.FindObjectsOfType<LandVehicle>()) if (WheelchairType.Is(v)) list.Add(v);
            return list;
        }

        private static LandVehicle Nearest(Vector3 at, bool occupiedOnly)
        {
            LandVehicle best = null; float bd = float.MaxValue;
            foreach (var v in Chairs())
            {
                if (occupiedOnly && !v.IsOccupied) continue;
                float d = Vector3.Distance(v.transform.position, at);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        private static float F(string[] p, int i, float fallback)
        {
            if (p.Length <= i) return fallback;
            if (float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            Say("not a number: '" + p[i] + "', kept " + fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        internal static void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            Core.Log.Msg(line);
            _pending.Add("[Rollin] " + line);
        }

        internal static void FlushToConsole()
        {
            if (_pending.Count == 0) return;
            for (int i = 0; i < _pending.Count; i++)
            {
                try { GameConsole.Log(_pending[i]); }
                catch (Exception e) { Core.Log.Warning("[Console] could not echo into the game console: " + e.Message); }
            }
            _pending.Clear();
        }
    }

    [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(string) })]
    internal static class Rollin_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !DebugConsolePatch.TryHandle(args); }
            catch (Exception e) { Core.Log.Error("[Console] dispatch failed: " + e); return true; }
        }
    }

    [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(GameStringList) })]
    internal static class Rollin_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(GameStringList args)
        {
            try { return !DebugConsolePatch.TryHandle(args); }
            catch (Exception e) { Core.Log.Error("[Console] dispatch failed: " + e); return true; }
        }
    }
}
#endif
