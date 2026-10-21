using System;
using System.Collections.Generic;
using Rollin.Chair;
using Rollin.Net;

namespace Rollin.Tuning
{
    /// <summary>
    /// Moves the parts that are more than a model, on every peer for its own copy of each chair: rims turn with the
    /// wheel pivots, the hat sits on the rider's head, rocket flames and the thruster follow the speed, exhausts pop on
    /// hard braking, the lightbar flashes and the speakers bounce. Runs late in the frame, after the wheels moved.
    /// </summary>
    internal static class Effects
    {
        private sealed class State
        {
            internal LandVehicle Lv;
            internal float NextScan;
            internal Transform PivotL, PivotR;
            internal Transform SpinL, SpinR, Hat;
            internal readonly List<Renderer> HatRenderers = new List<Renderer>();
            internal readonly List<Transform> Flames = new List<Transform>();
            internal readonly List<Light> FlameLights = new List<Light>();
            internal readonly List<AudioSource> Thrusters = new List<AudioSource>();
            internal readonly List<Transform> Backfires = new List<Transform>();
            internal readonly List<Transform> Pulses = new List<Transform>();
            internal Transform Red, Blue;
            internal Vector3 LastPos;
            internal bool HasPos;
            internal float Speed, Accel;
            internal float NextPop;
            internal Player HeadOf;
            internal Transform Head;
            internal Vector3 HeadLocal;
            internal bool HasHeadLocal;
        }

        private static readonly Dictionary<IntPtr, State> _chairs = new Dictionary<IntPtr, State>();
        private static readonly List<IntPtr> _gone = new List<IntPtr>();

        internal static void Init()
        {
            WheelchairSpawn.Started += lv => { if (lv != null) _chairs[lv.Pointer] = new State { Lv = lv }; };
            // The seated pose (BipedIK) bends the body after OnLateUpdate. At the end of the frame the head has its final
            // pose; its spot in chair space is kept and the hat goes there in the next LateTick.
            MelonLoader.MelonCoroutines.Start(EndOfFrame());
        }

        private static System.Collections.IEnumerator EndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                foreach (var kv in _chairs)
                {
                    var s = kv.Value;
                    try
                    {
                        if (s.Lv == null || s.Head == null) { s.HasHeadLocal = false; continue; }
                        s.HeadLocal = s.Lv.transform.InverseTransformPoint(s.Head.position);
                        s.HasHeadLocal = true;
                    }
                    catch (Exception e)
                    {
                        Core.Log.Error("[Effects] reading the rider's head failed; the hat waits on the seat: " + e);
                        s.Head = null; s.HasHeadLocal = false;
                    }
                }
            }
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
            Rocket(s);
            Backfire(s);
            Lightbar(s);
            Pulse(s);
        }

        private static void Scan(State s)
        {
            s.NextScan = Time.time + 0.5f;
            var root = s.Lv.transform;
            s.PivotL = s.PivotL != null ? s.PivotL : WheelchairType.Find(root, WheelchairType.RimLeft);
            s.PivotR = s.PivotR != null ? s.PivotR : WheelchairType.Find(root, WheelchairType.RimRight);
            s.SpinL = s.SpinR = s.Hat = s.Red = s.Blue = null;
            s.HatRenderers.Clear(); s.Flames.Clear(); s.FlameLights.Clear(); s.Thrusters.Clear(); s.Backfires.Clear(); s.Pulses.Clear();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name;
                if (!n.StartsWith("rollin_", StringComparison.Ordinal)) continue;
                switch (n)
                {
                    case Models.SpinLeft: s.SpinL = t; break;
                    case Models.SpinRight: s.SpinR = t; break;
                    case Models.Hat:
                        s.Hat = t;
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true)) s.HatRenderers.Add(r);
                        break;
                    case Models.Flame:
                        s.Flames.Add(t);
                        var l = t.GetComponentInChildren<Light>(true);
                        if (l != null) s.FlameLights.Add(l);
                        break;
                    case Models.Thruster:
                        var a = t.GetComponent<AudioSource>();
                        if (a != null) s.Thrusters.Add(a);
                        break;
                    case Models.Backfire: s.Backfires.Add(t); break;
                    case Models.BlinkRed: s.Red = t; break;
                    case Models.BlinkBlue: s.Blue = t; break;
                    case Models.Pulse: s.Pulses.Add(t); break;
                }
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
                s.Hat.localPosition = new Vector3(0f, 0.5f, -0.08f);
                s.Hat.localRotation = Quaternion.identity;
                ShowHat(s, true);
                s.HeadOf = null; s.Head = null;
                return;
            }
            if (s.HeadOf == null || !Compat.Interop.Same(s.HeadOf, rider) || s.Head == null)
            {
                s.HeadOf = rider;
                s.Head = HeadOf(rider);
                Core.Log.Msg("[Effects] chair net " + Ids.Of(lv) + ": hat follows " + rider.name + (s.Head != null ? " (head bone found)." : " - no head bone, the hat stays on the seat."));
            }
            if (s.Head == null || !s.HasHeadLocal) return;
            var up = lv.transform.up;
            var head = lv.transform.TransformPoint(s.HeadLocal);
            s.Hat.position = head + up * HatLift;
            s.Hat.rotation = Quaternion.LookRotation(lv.transform.forward, up);
            // The rider's own first-person camera sits inside the head; the hat would fill the view.
            var cam = Camera.main;
            ShowHat(s, cam == null || Vector3.Distance(cam.transform.position, head) > 0.45f);
        }

        /// <summary>From the head bone (the base of the skull) up to where a hat sits.</summary>
        private const float HatLift = 0.14f;

        private static void ShowHat(State s, bool on)
        {
            foreach (var r in s.HatRenderers) if (r != null && r.enabled != on) r.enabled = on;
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

        private static float Throttle(State s)
        {
            var lv = s.Lv;
            if (lv.LocalPlayerIsDriver) return Mathf.Clamp01(Mathf.Abs(lv.currentThrottle));
            // Other peers do not know the driver's keys; speed and acceleration stand in for them.
            return Mathf.Clamp01(s.Speed / 20f + Mathf.Max(0f, s.Accel) / 15f);
        }

        private static void Rocket(State s)
        {
            if (s.Flames.Count == 0 && s.Thrusters.Count == 0) return;
            bool occupied = s.Lv.IsOccupied;
            float k = occupied ? Mathf.Clamp01(0.2f + 0.8f * Throttle(s)) : 0f;
            float flicker = 1f + 0.18f * Mathf.Sin(Time.time * 53f) * Mathf.Sin(Time.time * 31f);
            foreach (var f in s.Flames)
            {
                if (f == null) continue;
                bool show = k > 0.01f;
                if (f.gameObject.activeSelf != show) f.gameObject.SetActive(show);
                f.localScale = new Vector3(0.7f + 0.3f * k, (0.25f + 1.2f * k) * flicker, 0.7f + 0.3f * k);
            }
            foreach (var l in s.FlameLights) if (l != null) l.intensity = (0.4f + 1.6f * k) * flicker;
            foreach (var a in s.Thrusters)
            {
                if (a == null) continue;
                a.volume = occupied ? 0.12f + 0.55f * k : 0f;
                a.pitch = 0.75f + 0.6f * k;
                if (occupied && !a.isPlaying) a.Play();
                else if (!occupied && a.isPlaying) a.Stop();
            }
        }

        private static void Backfire(State s)
        {
            if (s.Backfires.Count == 0 || Time.time < s.NextPop) return;
            if (s.Speed < 5f || s.Accel > -4f) return;
            var at = s.Backfires[UnityEngine.Random.Range(0, s.Backfires.Count)];
            if (at == null) return;
            s.NextPop = Time.time + UnityEngine.Random.Range(0.18f, 0.6f);
            Sounds.PlayAt("backfire", at.position, 0.7f, UnityEngine.Random.Range(0.9f, 1.35f));
            var flash = new GameObject("rollin_backfire_flash");
            flash.transform.position = at.position;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.15f);
            light.range = 3f;
            light.intensity = 6f;
            UnityEngine.Object.Destroy(flash, 0.08f);
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
            if (l != null) l.intensity = on ? 2.5f : 0f;
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

        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (var s in _chairs.Values)
            {
                if (s.Lv == null) continue;
                parts.Add("net " + Ids.Of(s.Lv) + " " + s.Speed.ToString("F1") + " km/h"
                    + (s.SpinL != null ? ", rims" : "") + (s.Hat != null ? ", hat" + (s.Head != null ? " on head" : " on seat") : "")
                    + (s.Flames.Count > 0 ? ", " + s.Flames.Count + " flame(s)" : "") + (s.Backfires.Count > 0 ? ", " + s.Backfires.Count + " pipe(s)" : "")
                    + (s.Red != null ? ", lightbar" : "") + (s.Pulses.Count > 0 ? ", speaker" : ""));
            }
            return "effects: " + (parts.Count == 0 ? "no chairs" : string.Join("; ", parts));
        }
    }
}
