using System;
using System.Collections.Generic;
using Chassis.Api;
using Rollin.Chair;
using Rollin.Net;

namespace Rollin.Tuning
{
    /// <summary>
    /// Moves the parts that are more than a model, on every peer for its own copy of each chair: rims turn with the
    /// wheel pivots, the hat sits on the rider's head, the tailpipes smoke while someone sits in the chair, the
    /// lightbar flashes and the speakers bounce. Runs late in the frame, after the wheels moved.
    /// </summary>
    internal static class Effects
    {
        private sealed class State
        {
            internal LandVehicle Lv;
            internal float NextScan;
            internal Transform PivotL, PivotR;
            internal Transform SpinL, SpinR, Hat;
            /// <summary>The part the hat belongs to; it goes back there when the rider gets up.</summary>
            internal Transform HatHome;
            internal readonly List<Renderer> HatRenderers = new List<Renderer>();
            internal readonly List<ParticleSystem> Smokes = new List<ParticleSystem>();
            internal readonly List<Wheel> Tyres = new List<Wheel>();
            internal bool Squealing;
            internal readonly List<Transform> Pulses = new List<Transform>();
            internal Transform Red, Blue;
            internal Vector3 LastPos;
            internal bool HasPos;
            internal float Speed, Accel;
            internal Player HeadOf;
            internal Transform Head;
        }

        private static readonly Dictionary<IntPtr, State> _chairs = new Dictionary<IntPtr, State>();
        private static readonly List<IntPtr> _gone = new List<IntPtr>();

        internal static void Init()
        {
            WheelchairSpawn.Started += lv => { if (lv != null) _chairs[lv.Pointer] = new State { Lv = lv }; };
            // A new neon would sit at the Chassis spot until the next scan; the change itself starts one.
            ChassisApi.TuningChanged += (lv, key) =>
            {
                if (lv != null && _chairs.TryGetValue(lv.Pointer, out var s)) s.NextScan = 0f;
            };
        }

        internal static void Reset() => _chairs.Clear();

        internal static int Count => _chairs.Count;

        internal static void LateTick()
        {
            if (_chairs.Count == 0) return;
            _gone.Clear();
            foreach (var kv in _chairs)
            {
                var s = kv.Value;
                if (s.Lv == null) { _gone.Add(kv.Key); continue; }
                try { Tick(s); }
                catch (Exception e)
                {
                    // One broken chair must not stop the others; it is dropped and logged.
                    Core.Log.Error("[Effects] chair net " + Ids.Of(s.Lv) + " failed and stops moving its parts: " + e);
                    _gone.Add(kv.Key);
                }
            }
            foreach (var k in _gone) _chairs.Remove(k);
        }

        private static void Tick(State s)
        {
            var lv = s.Lv;
            // Chassis rebuilds the part models whenever the tuning changes; a rescan twice a second finds the new ones.
            if (Time.time >= s.NextScan) Scan(s);

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 pos = lv.transform.position;
            if (s.HasPos)
            {
                float now = Vector3.Distance(pos, s.LastPos) / dt * 3.6f;
                if (now > 300f) now = s.Speed; // a teleport, not a speed
                float smooth = Mathf.Lerp(s.Speed, now, 1f - Mathf.Exp(-dt * 8f));
                s.Accel = Mathf.Lerp(s.Accel, (smooth - s.Speed) / dt, 1f - Mathf.Exp(-dt * 6f));
                s.Speed = smooth;
            }
            s.LastPos = pos;
            s.HasPos = true;

            Rims(s);
            Hat(s);
            Smoke(s);
            Squeal(s);
            Lightbar(s);
            Pulse(s);
        }

        private static void Scan(State s)
        {
            s.NextScan = Time.time + 0.5f;
            var root = s.Lv.transform;
            // A hat on the rider's head is not under the chair, so the scan below does not see it. It stays on as long as
            // its part is still fitted; a part that was replaced or removed takes it back.
            Transform worn = null, wornHome = null;
            if (s.Hat != null && s.Head != null && s.Hat.parent == s.Head) { worn = s.Hat; wornHome = s.HatHome; }
            s.PivotL = s.PivotL != null ? s.PivotL : WheelchairType.Find(root, WheelchairType.RimLeft);
            s.PivotR = s.PivotR != null ? s.PivotR : WheelchairType.Find(root, WheelchairType.RimRight);
            s.SpinL = s.SpinR = s.Hat = s.HatHome = s.Red = s.Blue = null;
            s.HatRenderers.Clear(); s.Smokes.Clear(); s.Pulses.Clear();
            if (s.Tyres.Count == 0) foreach (var w in root.GetComponentsInChildren<Wheel>(true)) if (w != null && w.DriftAudioEnabled) s.Tyres.Add(w);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name;
                if (n == Models.ChassisNeon && t.parent == root && t.Find(Models.SeatNeon) == null)
                {
                    Models.FitNeon(s.Lv, t);
                    continue;
                }
                if (!n.StartsWith("rollin_", StringComparison.Ordinal)) continue;
                switch (n)
                {
                    case Models.SpinLeft: s.SpinL = t; break;
                    case Models.SpinRight: s.SpinR = t; break;
                    case Models.Hat:
                        // The seated avatar hangs under the chair, so the hat on its head shows up here too; it is
                        // handled after the loop.
                        if (worn != null && Compat.Interop.Same(t, worn)) break;
                        s.Hat = t;
                        s.HatHome = t.parent;
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true)) s.HatRenderers.Add(r);
                        break;
                    case Models.SmokePoint:
                        var fx = SmokeAt(s.Lv, t);
                        if (fx != null) s.Smokes.Add(fx);
                        break;
                    case Models.BlinkRed: s.Red = t; break;
                    case Models.BlinkBlue: s.Blue = t; break;
                    case Models.Pulse: s.Pulses.Add(t); break;
                }
            }
            if (worn == null) return;
            if (s.Hat == null && wornHome != null && wornHome.IsChildOf(root))
            {
                s.Hat = worn;
                s.HatHome = wornHome;
                foreach (var r in worn.GetComponentsInChildren<Renderer>(true)) s.HatRenderers.Add(r);
                return;
            }
            if (wornHome != null)
            {
                // Chassis destroys the old part at the end of the frame; the hat goes with it.
                worn.SetParent(wornHome, true);
                Core.Log.Msg("[Effects] chair net " + Ids.Of(s.Lv) + ": hat part replaced or removed, the old hat is taken off.");
            }
            else
            {
                UnityEngine.Object.Destroy(worn.gameObject);
                Core.Log.Msg("[Effects] chair net " + Ids.Of(s.Lv) + ": hat part gone, the hat is taken off the rider's head.");
            }
        }

        private static void Rims(State s)
        {
            if (s.SpinL != null && s.PivotL != null) { s.SpinL.position = s.PivotL.position; s.SpinL.rotation = s.PivotL.rotation; }
            if (s.SpinR != null && s.PivotR != null) { s.SpinR.position = s.PivotR.position; s.SpinR.rotation = s.PivotR.rotation; }
        }

        private static void Hat(State s)
        {
            if (s.Hat == null) return;
            var lv = s.Lv;
            Player rider = null;
            var seats = lv.Seats;
            if (seats != null && seats.Length > 0 && seats[0] != null) rider = seats[0].Occupant;

            if (rider == null)
            {
                // Nobody seated: the hat waits on the cushion.
                if (s.Head != null && s.Hat.parent == s.Head) Core.Log.Msg("[Effects] chair net " + Ids.Of(lv) + ": the rider got up, the hat is back on the seat.");
                HatHome(s);
                ShowHat(s, true);
                s.HeadOf = null; s.Head = null;
                return;
            }
            if (s.HeadOf == null || !Compat.Interop.Same(s.HeadOf, rider))
            {
                HatHome(s);
                s.HeadOf = rider;
                s.Head = HeadOf(rider);
                if (s.Head == null) Core.Log.Warning("[Effects] chair net " + Ids.Of(lv) + ": " + rider.name + " has no head bone - the hat stays on the seat.");
            }
            if (s.Head == null) return;
            if (s.Hat.parent != s.Head) PutOn(s, rider);
            // The rider's own first-person camera sits inside the head; the hat would fill the view.
            var cam = Camera.main;
            ShowHat(s, cam == null || Vector3.Distance(cam.transform.position, s.Head.position) > 0.45f);
        }

        /// <summary>
        /// Puts the hat on the rider's head bone, upright over the skull. From then on the bone carries it: every turn,
        /// nod and lean of the seated pose moves it along.
        /// </summary>
        private static void PutOn(State s, Player rider)
        {
            var lv = s.Lv;
            float lift = LiftOver(rider, s.Head, out string by);
            var up = lv.transform.up;
            s.Hat.SetParent(s.Head, true); // keeps the size: the head bone is scaled
            s.Hat.position = s.Head.position + up * lift;
            s.Hat.rotation = Quaternion.LookRotation(lv.transform.forward, up);
            Core.Log.Msg("[Effects] chair net " + Ids.Of(lv) + ": hat on " + rider.name + "'s head, "
                + lift.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " m above the head bone (" + by + ").");
        }

        /// <summary>Puts the hat back on its part, on the cushion.</summary>
        private static void HatHome(State s)
        {
            if (s.Hat == null || s.HatHome == null || s.Hat.parent == s.HatHome) return;
            s.Hat.SetParent(s.HatHome, true);
            s.Hat.localPosition = Models.HatSeat;
            s.Hat.localRotation = Quaternion.identity;
        }

        /// <summary>From the head bone (the base of the skull) up to where a hat sits, without hair.</summary>
        private const float HatLift = 0.14f;

        /// <summary>The most a hairstyle or cap lifts the hat.</summary>
        private const float MaxLift = 0.24f;

        private static void ShowHat(State s, bool on)
        {
            foreach (var r in s.HatRenderers) if (r != null && r.enabled != on) r.enabled = on;
        }

        /// <summary>
        /// How far above the head bone the top of the head is: the skull at least, more under a big hairstyle or a cap.
        /// Taken from the small renderers right at the head (hair, cap); the body and the clothes are larger and left out.
        /// </summary>
        private static float LiftOver(Player p, Transform head, out string by)
        {
            float top = head.position.y + HatLift;
            by = "skull";
            foreach (var r in p.Avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (b.size.y > 0.45f || Vector3.Distance(b.center, head.position) > 0.25f) continue;
                if (b.max.y <= top) continue;
                top = b.max.y;
                by = r.name;
            }
            float lift = top - head.position.y;
            if (lift > MaxLift) { by += ", capped from " + lift.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); lift = MaxLift; }
            return lift;
        }

        private static Transform HeadOf(Player p)
        {
            var avatar = p != null ? p.Avatar : null;
            if (avatar == null) return null;
            Animator anim = avatar.Animation != null ? avatar.Animation._animator : null;
            if (anim == null) anim = avatar.GetComponentInChildren<Animator>(true);
            if (anim == null || !anim.isHuman) return null;
            return anim.GetBoneTransform(HumanBodyBones.Head);
        }

        /// <summary>
        /// The exhaust smoke of the car the chair is built from (its <see cref="VehicleFX"/>), copied onto a tailpipe's
        /// outlet once. The car's own emitters stay where the hidden car body would have its exhaust.
        /// </summary>
        private static ParticleSystem SmokeAt(LandVehicle lv, Transform point)
        {
            var have = point.Find(Models.SmokeFx);
            if (have != null) return have.GetComponent<ParticleSystem>();
            var fx = lv.GetComponentInChildren<VehicleFX>(true);
            var source = fx != null && fx.exhaustFX != null && fx.exhaustFX.Length > 0 ? fx.exhaustFX[0] : null;
            if (source == null)
            {
                Core.Log.Warning("[Effects] chair net " + Ids.Of(lv) + " has no exhaust smoke to copy - the tailpipes stay clean.");
                point.gameObject.name = Models.SmokePoint + "_none";
                return null;
            }
            var copy = UnityEngine.Object.Instantiate(source.gameObject, point, false);
            copy.name = Models.SmokeFx;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            // The chair hides every renderer of the car it is built from, the smoke's among them.
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            var ps = copy.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Core.Log.Msg("[Effects] chair net " + Ids.Of(lv) + ": exhaust smoke '" + source.name + "' on a tailpipe.");
            return ps;
        }

        private static void Smoke(State s)
        {
            if (s.Smokes.Count == 0) return;
            bool on = s.Lv.IsOccupied;
            foreach (var ps in s.Smokes)
            {
                if (ps == null) continue;
                if (on && !ps.isPlaying) ps.Play(true);
                else if (!on && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>The game plays the tyre squeal itself (Wheel); this only logs when it starts and stops.</summary>
        private static void Squeal(State s)
        {
            bool now = false;
            foreach (var w in s.Tyres) if (w != null && w.DriftAudioSource != null && w.DriftAudioSource.IsPlaying) { now = true; break; }
            if (now == s.Squealing) return;
            s.Squealing = now;
            Core.Log.Msg("[Effects] chair net " + Ids.Of(s.Lv) + ": tyres " + (now ? "squeal" : "quiet") + " at " + s.Speed.ToString("F1") + " km/h.");
        }

        private static void Lightbar(State s)
        {
            if (s.Red == null && s.Blue == null) return;
            bool on = s.Lv.IsOccupied;
            float t = Time.time * 3.2f;
            bool redPhase = (t - Mathf.Floor(t)) < 0.5f;
            bool strobe = (Time.time * 14f) % 1f < 0.6f;
            Blink(s.Red, on && redPhase && strobe);
            Blink(s.Blue, on && !redPhase && strobe);
        }

        private static void Blink(Transform t, bool on)
        {
            if (t == null) return;
            var l = t.GetComponentInChildren<Light>(true);
            if (l != null) l.intensity = on ? 1.2f : 0f;
            // The lens keeps its off material; its lit twin lies on top and is what blinks.
            var twin = t.Find(Models.BlinkOn);
            var r = twin != null ? twin.GetComponent<Renderer>() : null;
            if (r != null && r.enabled != on) r.enabled = on;
        }

        private static void Pulse(State s)
        {
            if (s.Pulses.Count == 0) return;
            float beat = s.Lv.IsOccupied ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * Mathf.PI * 2f * 2.1f)), 6f) : 0f;
            var scale = Vector3.one * (1f + 0.08f * beat);
            foreach (var p in s.Pulses) if (p != null) p.localScale = scale;
        }

        private static string SmokeInfo(ParticleSystem ps)
        {
            if (ps == null) return "(gone)";
            var r = ps.GetComponent<ParticleSystemRenderer>();
            return "(" + (ps.isPlaying ? "playing" : "stopped") + ", " + ps.particleCount + " particles, renderer "
                + (r == null ? "none" : (r.enabled ? "on" : "off") + ", bounds " + r.bounds.size.ToString("F2")) + ")";
        }

        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (var s in _chairs.Values)
            {
                if (s.Lv == null) continue;
                parts.Add("net " + Ids.Of(s.Lv) + " " + s.Speed.ToString("F1") + " km/h"
                    + (s.SpinL != null ? ", rims" : "") + (s.Hat != null ? ", hat" + (s.Head != null && s.Hat.parent == s.Head ? " on head" : " on seat") : "")
                    + (s.Smokes.Count > 0 ? ", " + s.Smokes.Count + " smoking pipe(s) " + SmokeInfo(s.Smokes[0]) : "")
                    + (s.Red != null ? ", lightbar" : "") + (s.Pulses.Count > 0 ? ", speaker" : ""));
            }
            return "effects: " + (parts.Count == 0 ? "no chairs" : string.Join("; ", parts));
        }
    }
}
