using System;
using System.Collections.Generic;
using Chassis.Api;
using Rollin.Chair;

namespace Rollin.Tuning
{
    /// <summary>
    /// The visual tuning parts, built from the game's own meshes (<see cref="GameMeshes"/>) and placed in the chair's own
    /// space (+Z forward, ground at 0, the seat at 0.5 m, the push handles at (±0.2, 0.95, -0.45)), so each
    /// <see cref="PartFit"/> stays at the origin. Every part has a mount zone that keeps the rider, the pusher and the
    /// frame free; <c>rollin tune overlap</c> measures it. Moving pieces carry names <see cref="Effects"/> looks for.
    /// </summary>
    internal static class Models
    {
        // Names Effects drives.
        internal const string SpinLeft = "rollin_spin_L", SpinRight = "rollin_spin_R";
        internal const string Hat = "rollin_hat";
        internal const string Flame = "rollin_fx_flame";
        internal const string Thruster = "rollin_thruster";
        internal const string Backfire = "rollin_backfire";
        internal const string BlinkRed = "rollin_blink_red", BlinkBlue = "rollin_blink_blue";
        internal const string BlinkOn = "rollin_on";
        internal const string Pulse = "rollin_pulse";

        /// <summary>The stock chair wheels a set of rims replaces.</summary>
        private static readonly string[] StockRims = { "Wheel", "Wheel (1)" };

        internal enum Anchor { Bottom, Top, Centre, Back, Front, BottomFront }

        // Colours from the game's flat palette (the swatch grid of its prop atlas).
        private static readonly Color DarkSlate = Hex(0x2c3e4f);
        private static readonly Color Steel = Hex(0x95a5a6);
        private static readonly Color PipeMetal = new Color(0.32f, 0.33f, 0.34f);

        /// <summary>Where the pole for the lightbar and the umbrella stands: on top of the backrest's top bar.</summary>
        private static readonly Vector3 PoleFoot = new Vector3(0f, 0.95f, -0.4f);

        /// <summary>The underside of the seat sling, where cargo hangs.</summary>
        private const float SeatUnderside = 0.43f;

        /// <summary>The backrest's two upright tubes (x), the back of its sling at seat height (z), and how far it leans back.</summary>
        private const float BackPostX = 0.2f, BackZ = -0.33f, BackTilt = 12f;

        /// <summary>The tailpipes: side offset, height, front end, and the cross pipe that joins them in front of that.</summary>
        private const float PipeX = 0.15f, PipeY = 0.17f, PipeFront = -0.23f, PipeJoin = -0.17f, PipeBore = 0.04f;

        /// <summary>The lightbar's place on the pole, above the rider's head (1.42 m).</summary>
        private const float LightbarTop = 1.46f;

        /// <summary>The umbrella's shaft starts above the lightbar, so both fit on one pole.</summary>
        private const float UmbrellaFoot = 1.54f;

        internal static IEnumerable<TuningPart> Parts()
        {
            // ---- rims: car wheels from the game, cut to the chair's wheel size -------------------------------------
            yield return Rims("rollin_rims_sport", "Sports Rims", 1200, GameMeshes.RimSport, null);
            yield return Rims("rollin_rims_muscle", "Muscle Rims", 1600, GameMeshes.RimMuscle, new TuningStats { Grip = 1.1f });

            // ---- exhaust: two Bruiser tailpipes out the back, joined by a cross pipe that rises to the seat -----------
            yield return Part("rollin_exhaust_twin", "exhaust", "Twin Tailpipes", 900, TwinExhaust, new TuningStats { EnginePitch = 0.85f });

            // ---- cargo: a moving box from the game's storage, flush under the seat --------------------------------
            yield return Part("rollin_box", "cargo", "Moving Box", 30, root =>
                Game(root, GameMeshes.Box, new Vector3(0f, SeatUnderside, 0.02f), new Vector3(0f, 12f, 0f), 0.26f, Axis.Longest, Anchor.Top),
                new TuningStats { Mass = 1.05f });

            // ---- booster: a jerry can strapped to the back of the backrest, flame out of the bottom -----------------
            yield return Part("rollin_jerrycan", "booster", "Jerry Can Rocket", 15000, JerryCanRocket,
                new TuningStats { Torque = 1.8f, Gearing = 1.8f, EnginePitch = 1.2f });

            // ---- spoiler: a small wing between the push handles -------------------------------------------------------
            yield return Part("rollin_wing", "spoiler", "Handle Wing", 2500, Wing, new TuningStats { Grip = 1.1f });

            // ---- lights: the police lightbar, shrunk onto a pole behind the head -------------------------------------
            yield return Part("rollin_lightbar", "lights", "Police Lightbar", 8000, Lightbar, null);

            // ---- roof: the NPC umbrella on the pole, over the rider's head -------------------------------------------
            yield return Part("rollin_umbrella", "roof", "Sun Umbrella", 60, root =>
            {
                // The pole runs up into the canopy, through the umbrella's own short shaft.
                Pole(root, UmbrellaFoot + 0.22f);
                Game(root, GameMeshes.Umbrella, new Vector3(PoleFoot.x, UmbrellaFoot, PoleFoot.z), Vector3.zero, 0.6f, Axis.Widest, Anchor.Bottom);
            }, new TuningStats { Mass = 0.9f });

            // ---- audio: the game's radio lengthwise on the left armrest, speaker facing out -------------------------
            yield return Part("rollin_radio", "audio", "Armrest Radio", 700, root =>
                Pulsing(Game(root, GameMeshes.Radio, new Vector3(-0.29f, 0.72f, 0.15f), new Vector3(0f, -90f, 0f), 0.18f, Axis.Longest, Anchor.Bottom, ownAxes: true)),
                null);

            // ---- mascot: the garden gnome on the seat between the rider's thighs, looking ahead ----------------------
            yield return Part("rollin_gnome", "mascot", "Garden Gnome", 3, root =>
                Game(root, GameMeshes.Gnome, new Vector3(0f, 0.52f, 0.16f), Vector3.zero, 0.24f, Axis.Height, Anchor.Bottom, upBy: "Hat", faceBy: "Beard"), null);

            // ---- hat: the gnome's hat on the rider's head ------------------------------------------------------------
            yield return Part("rollin_hat_gnome", "hat", "Gnome Hat", 420, root => HatPiece(root, GameMeshes.Gnome, "Hat", 0.2f, Axis.Widest, 0.02f), null);
        }

        // ---- part builders -----------------------------------------------------------------------------------------

        private static TuningPart Part(string id, string slot, string label, int price, Action<Transform> build, TuningStats stats)
        {
            var part = new TuningPart { Id = id, Slot = slot, Label = label, Price = price, Stats = stats };
            part.Build = lv =>
            {
                var root = new GameObject("rollin_part_" + id);
                build(root.transform);
                return root;
            };
            part.Fits[WheelchairType.Code] = new PartFit();
            return part;
        }

        private static TuningPart Rims(string id, string label, int price, string source, TuningStats stats)
        {
            // Not Chassis' own "wheels" slot: that one copies the rim onto the car's stock rim renderers, which the chair
            // lacks. Rollin's rims replace the two big wheel meshes and turn with their pivots (Effects).
            var part = Part(id, "rims", label, price, root =>
            {
                Rim(root, SpinRight, source, WheelchairType.BigWheel, 0f);
                Rim(root, SpinLeft, source, new Vector3(-WheelchairType.BigWheel.x, WheelchairType.BigWheel.y, WheelchairType.BigWheel.z), 180f);
            }, stats);
            part.Fits[WheelchairType.Code].Hide.AddRange(StockRims);
            return part;
        }

        /// <summary>
        /// One rim: a holder Effects moves onto the wheel pivot, with the car wheel turned so its axle runs along X and
        /// squeezed to the chair's wheel (0.66 m across, 6 cm wide).
        /// </summary>
        private static void Rim(Transform root, string name, string source, Vector3 at, float yaw)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            holder.localPosition = at;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var scaler = new GameObject("fit").transform;
            scaler.SetParent(holder, false);
            var copy = GameMeshes.Copy(source, scaler);
            if (copy == null) return;
            var b = GameMeshes.Measure(scaler, copy);
            // The axle is the wheel's thinnest side.
            var s = b.size;
            if (s.y < s.x && s.y < s.z) copy.localRotation = Quaternion.Euler(0f, 0f, 90f) * copy.localRotation;
            else if (s.z < s.x && s.z < s.y) copy.localRotation = Quaternion.Euler(0f, 90f, 0f) * copy.localRotation;
            b = GameMeshes.Measure(scaler, copy);
            float dia = 2f * WheelchairType.BigRadius;
            scaler.localScale = new Vector3(0.06f / Mathf.Max(0.001f, b.size.x), dia / Mathf.Max(0.001f, b.size.y), dia / Mathf.Max(0.001f, b.size.z));
            copy.localPosition -= b.center;
        }

        /// <summary>A hat that Effects puts on the rider's head; with nobody seated it waits on the cushion.</summary>
        private static void HatPiece(Transform root, string source, string child, float size, Axis axis, float sink)
        {
            var holder = new GameObject(Hat).transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(0f, 0.5f, -0.08f);
            var piece = Game(holder, source, new Vector3(0f, -sink, 0f), Vector3.zero, size, axis, Anchor.Bottom, child: child);
            if (piece == null) Core.Log.Warning("[Models] hat from '" + source + "' did not build.");
        }

        private static void TwinExhaust(Transform root)
        {
            var metal = Flat(PipeMetal, 0.55f, 0.5f);
            int pipes = 0;
            foreach (float x in new[] { PipeX, -PipeX })
            {
                var pipe = Game(root, GameMeshes.Tailpipe, new Vector3(x, PipeY, PipeFront), Vector3.zero, 0.26f, Axis.Longest, Anchor.Front,
                    metal, lengthAlongZ: true);
                if (pipe == null) continue;
                pipes++;
                Marker(root, Backfire, new Vector3(x, PipeY, -0.5f));
                // Reaches a little into the tailpipe so no seam shows where the game mesh starts.
                Tube(root, "elbow", new Vector3(x, PipeY, PipeFront - 0.02f), new Vector3(x, PipeY, PipeJoin - PipeBore * 0.5f), PipeBore, metal);
            }
            if (pipes == 0) { Core.Log.Warning("[Models] no tailpipe built - the exhaust stays without its cross pipe."); return; }
            float half = PipeX + PipeBore * 0.5f;
            Tube(root, "manifold", new Vector3(-half, PipeY, PipeJoin), new Vector3(half, PipeY, PipeJoin), PipeBore, metal);
            // Up to the seat's underside, bolted on with a flange inside the neon frame.
            Tube(root, "riser", new Vector3(0f, PipeY - PipeBore * 0.5f, PipeJoin), new Vector3(0f, SeatUnderside - 0.012f, PipeJoin), PipeBore, metal);
            Box(root, "flange", new Vector3(0f, SeatUnderside - 0.006f, PipeJoin), new Vector3(0.07f, 0.012f, 0.07f), Flat(Steel, 0.4f, 0.6f));
        }

        private static void JerryCanRocket(Transform root)
        {
            // A rack that leans with the backrest: the can stands on a steel shelf flat against the back of the sling, two
            // straps hold it to the backrest tubes and a brace under each side carries the shelf from the tube. The flame
            // leaves the bottom through the shelf, tipped backwards so it stays short of the pusher's feet.
            const float floor = 0.5f, height = 0.28f;
            var rack = new GameObject("rollin_rack").transform;
            rack.SetParent(root, false);
            rack.localPosition = new Vector3(0f, floor, BackZ);
            rack.localRotation = Quaternion.Euler(-BackTilt, 0f, 0f);
            var can = Game(rack, GameMeshes.JerryCan, Vector3.zero, Vector3.zero, height, Axis.Height, Anchor.BottomFront, flatSideBack: true);
            if (can == null) return;
            var b = GameMeshes.Measure(rack, can);
            float back = b.min.z - 0.006f, mid = back * 0.5f, depth = -back;
            var steel = Flat(Steel, 0.4f, 0.6f);
            var strap = Flat(DarkSlate, 0.1f, 0f);
            float span = 2f * BackPostX + 0.02f;
            Box(rack, "shelf", new Vector3(0f, -0.008f, mid), new Vector3(span, 0.016f, depth + 0.02f), steel);
            foreach (float x in new[] { -1f, 1f })
            {
                // From the shelf's back corner down and forward to the tube.
                var brace = Strut(rack, "brace", new Vector3(x * BackPostX, -0.016f, back + 0.012f), new Vector3(x * BackPostX, -0.12f, 0f), 0.012f, steel);
                if (brace == null) Core.Log.Warning("[Models] the jerry can rack has no brace on side " + x + ".");
            }
            foreach (float y in new[] { height * 0.3f, height * 0.75f })
            {
                Box(rack, "strap", new Vector3(0f, y, back - 0.004f), new Vector3(b.size.x + 0.01f, 0.03f, 0.008f), strap);
                foreach (float x in new[] { -1f, 1f })
                {
                    // From the can's edge round to the tube.
                    float edge = x * (b.size.x * 0.5f + 0.005f), tube = x * BackPostX;
                    Box(rack, "strap", new Vector3((edge + tube) * 0.5f, y, back - 0.004f), new Vector3(Mathf.Abs(tube - edge) + 0.01f, 0.03f, 0.008f), strap);
                    Box(rack, "strap", new Vector3(tube, y, mid - 0.002f), new Vector3(0.008f, 0.03f, depth + 0.012f), strap);
                }
            }

            var nozzle = new GameObject("rollin_nozzle").transform;
            nozzle.SetParent(rack, false);
            nozzle.localPosition = new Vector3(0f, -0.016f, b.center.z);
            // 20 degrees back from straight down in chair space; the rack already leans BackTilt of it.
            nozzle.localRotation = Quaternion.Euler(-(20f - BackTilt), 0f, 0f);
            Box(nozzle, "ring", new Vector3(0f, -0.012f, 0f), new Vector3(0.06f, 0.024f, 0.06f), DarkSlate, false);

            var flame = new GameObject(Flame).transform;
            flame.SetParent(nozzle, false);
            flame.localPosition = new Vector3(0f, -0.024f, 0f);
            flame.localRotation = Quaternion.Euler(180f, 0f, 0f);
            AddCone(flame, "core", 0.022f, 0.16f, new Color(1f, 0.95f, 0.6f));
            AddCone(flame, "outer", 0.04f, 0.28f, new Color(1f, 0.42f, 0.05f));
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(flame, false);
            light.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 2f;
            light.intensity = 0f;
            var thruster = new GameObject(Thruster);
            thruster.transform.SetParent(nozzle, false);
            Sounds.AddLoop(thruster, "thruster");
        }

        private static void Wing(Transform root)
        {
            // Between the handles (±0.2 m), narrower than the pusher's grip, on two struts from the backrest's top bar.
            var mat = Flat(DarkSlate, 0.15f, 0.1f);
            var wing = new GameObject("rollin_wing").transform;
            wing.SetParent(root, false);
            wing.localPosition = new Vector3(0f, 1.02f, -0.53f);
            wing.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            Box(wing, "blade", Vector3.zero, new Vector3(0.26f, 0.018f, 0.11f), mat);
            Box(wing, "plate_L", new Vector3(-0.135f, 0.015f, 0f), new Vector3(0.01f, 0.06f, 0.12f), mat);
            Box(wing, "plate_R", new Vector3(0.135f, 0.015f, 0f), new Vector3(0.01f, 0.06f, 0.12f), mat);
            var strut = Flat(Steel, 0.4f, 0.6f);
            Box(root, "strut_L", new Vector3(-0.07f, 0.975f, -0.47f), new Vector3(0.015f, 0.09f, 0.015f), strut);
            Box(root, "strut_R", new Vector3(0.07f, 0.975f, -0.47f), new Vector3(0.015f, 0.09f, 0.015f), strut);
        }

        private static void Lightbar(Transform root)
        {
            Pole(root, LightbarTop);
            var bar = Game(root, GameMeshes.Lightbar, new Vector3(PoleFoot.x, LightbarTop, PoleFoot.z), Vector3.zero, 0.38f, Axis.Widest, Anchor.Bottom);
            if (bar == null) return;
            GameMeshes.LightbarMaterials(out var redOn, out _, out var blueOn, out _);
            Lens(bar, "Red", BlinkRed, redOn, new Color(1f, 0.13f, 0f));
            Lens(bar, "Blue", BlinkBlue, blueOn, new Color(0f, 0.56f, 1f));
        }

        /// <summary>A steel pole from <see cref="PoleFoot"/> up to <paramref name="top"/>; the overlap check treats it as a mount.</summary>
        private static void Pole(Transform root, float top) =>
            Box(root, "pole", new Vector3(PoleFoot.x, (PoleFoot.y + top) * 0.5f, PoleFoot.z), new Vector3(0.022f, top - PoleFoot.y, 0.022f), Flat(Steel, 0.4f, 0.6f));

        /// <summary>
        /// One lens of the copied lightbar becomes a blinker: the lens stays in its off material, an "on" twin in the lit
        /// material lies on top and Effects switches the twin and a light.
        /// </summary>
        private static void Lens(Transform bar, string lens, string name, Material on, Color lightColour)
        {
            Transform t = null;
            foreach (var x in bar.GetComponentsInChildren<Transform>(true)) if (x.name == lens) { t = x; break; }
            if (t == null) { Core.Log.Warning("[Models] the lightbar copy has no '" + lens + "' lens - it does not blink."); return; }
            t.name = name;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && on != null)
            {
                var twin = new GameObject(BlinkOn);
                twin.transform.SetParent(t, false);
                twin.transform.localScale = Vector3.one * 1.01f;
                twin.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                twin.AddComponent<MeshRenderer>().sharedMaterial = on;
                twin.GetComponent<MeshRenderer>().enabled = false;
            }
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(t, false);
            light.transform.localPosition = Vector3.zero;
            light.type = LightType.Point;
            light.color = lightColour;
            light.range = 2.5f;
            light.intensity = 0f;
        }

        private static void Marker(Transform root, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.localPosition = at;
        }

        private static Transform Pulsing(Transform piece)
        {
            if (piece != null) piece.name = Pulse;
            return piece;
        }

        // ---- placement ---------------------------------------------------------------------------------------------

        internal enum Axis { Longest, Widest, Height }

        /// <summary>
        /// A game mesh under <paramref name="parent"/>: the copy turned by <paramref name="euler"/>, scaled uniformly so its
        /// <paramref name="axis"/> measures <paramref name="size"/> metres, and moved so the anchor point of its bounds
        /// (in chair axes, after the turn) sits on a holder at <paramref name="at"/>. <paramref name="lengthAlongZ"/> first
        /// turns the copy so its longest side runs along Z; <paramref name="upBy"/> first turns it so that child is on top and
        /// <paramref name="faceBy"/> so that child faces forward; <paramref name="flatSideBack"/> turns its broad side to face
        /// backwards. <paramref name="ownAxes"/> drops the lie the source has in its scene and starts from the mesh's own axes.
        /// </summary>
        private static Transform Game(Transform parent, string source, Vector3 at, Vector3 euler, float size, Axis axis, Anchor anchor,
            Material fallback = null, bool lengthAlongZ = false, string child = null, string upBy = null, bool flatSideBack = false,
            string faceBy = null, bool ownAxes = false)
        {
            var holder = new GameObject("rollin_" + source).transform;
            holder.SetParent(parent, false);
            holder.localPosition = at;
            var scaler = new GameObject("fit").transform;
            scaler.SetParent(holder, false);
            var copy = GameMeshes.Copy(source, scaler, fallback);
            if (copy == null) return null;
            if (child != null)
            {
                Transform keep = null;
                foreach (var t in copy.GetComponentsInChildren<Transform>(true)) if (t.name == child) { keep = t; break; }
                if (keep == null) { Core.Log.Warning("[Models] '" + source + "' has no '" + child + "'."); UnityEngine.Object.Destroy(copy.gameObject); return null; }
                keep.SetParent(scaler, true);
                UnityEngine.Object.DestroyImmediate(copy.gameObject);
                copy = keep;
            }
            if (ownAxes) copy.localRotation = Quaternion.identity;
            if (upBy != null) Upright(scaler, copy, upBy);
            if (faceBy != null) Facing(scaler, copy, faceBy);
            if (flatSideBack)
            {
                var s1 = GameMeshes.Measure(scaler, copy).size;
                if (s1.z > s1.x) copy.localRotation = Quaternion.Euler(0f, 90f, 0f) * copy.localRotation;
            }
            if (lengthAlongZ)
            {
                var s0 = GameMeshes.Measure(scaler, copy).size;
                if (s0.x >= s0.y && s0.x >= s0.z) copy.localRotation = Quaternion.Euler(0f, 90f, 0f) * copy.localRotation;
                else if (s0.y >= s0.x && s0.y >= s0.z) copy.localRotation = Quaternion.Euler(90f, 0f, 0f) * copy.localRotation;
            }
            copy.localRotation = Quaternion.Euler(euler) * copy.localRotation;
            var b = GameMeshes.Measure(scaler, copy);
            var s = b.size;
            float measured = axis == Axis.Height ? s.y : axis == Axis.Widest ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            float k = size / Mathf.Max(0.0001f, measured);
            scaler.localScale = new Vector3(k, k, k);
            Vector3 point = anchor == Anchor.Bottom ? new Vector3(b.center.x, b.min.y, b.center.z)
                : anchor == Anchor.Top ? new Vector3(b.center.x, b.max.y, b.center.z)
                : anchor == Anchor.Back ? new Vector3(b.center.x, b.center.y, b.min.z)
                : anchor == Anchor.Front ? new Vector3(b.center.x, b.center.y, b.max.z)
                : anchor == Anchor.BottomFront ? new Vector3(b.center.x, b.min.y, b.max.z)
                : b.center;
            copy.localPosition -= point;
            return holder;
        }

        /// <summary>Turns <paramref name="copy"/> so the line from its middle to its child <paramref name="top"/> points up.</summary>
        private static void Upright(Transform space, Transform copy, string top)
        {
            Transform t = null;
            foreach (var x in copy.GetComponentsInChildren<Transform>(true)) if (x.name == top) { t = x; break; }
            if (t == null) { Core.Log.Warning("[Models] " + copy.name + " has no '" + top + "' to stand it up by - it keeps the source's lie."); return; }
            var up = GameMeshes.Measure(space, t).center - GameMeshes.Measure(space, copy).center;
            if (up.sqrMagnitude < 1e-8f) { Core.Log.Warning("[Models] " + copy.name + "'s '" + top + "' sits in its middle - it keeps the source's lie."); return; }
            copy.localRotation = Quaternion.FromToRotation(up, Vector3.up) * copy.localRotation;
        }

        /// <summary>Turns <paramref name="copy"/> about the vertical so its child <paramref name="front"/> faces forward (+Z).</summary>
        private static void Facing(Transform space, Transform copy, string front)
        {
            Transform t = null;
            foreach (var x in copy.GetComponentsInChildren<Transform>(true)) if (x.name == front) { t = x; break; }
            if (t == null) { Core.Log.Warning("[Models] " + copy.name + " has no '" + front + "' to face it forward by - it keeps its heading."); return; }
            var ahead = GameMeshes.Measure(space, t).center - GameMeshes.Measure(space, copy).center;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-8f) { Core.Log.Warning("[Models] " + copy.name + "'s '" + front + "' sits in its middle - it keeps its heading."); return; }
            copy.localRotation = Quaternion.FromToRotation(ahead, Vector3.forward) * copy.localRotation;
        }

        // ---- neon ----------------------------------------------------------------------------------------------------

        /// <summary>The underglow Chassis fits on every car; on a chair it measures the frame and lands near the ground.</summary>
        internal const string ChassisNeon = "Chassis_Tune_neon", ChassisNeonLight = "Chassis_Tune_neon_light_";

        /// <summary>The strips Rollin puts under the seat in place of the Chassis ones.</summary>
        internal const string SeatNeon = "rollin_neon";

        /// <summary>
        /// Moves the Chassis underglow to the underside of the seat: its strips are hidden and the same material runs as a
        /// frame along the seat's edges on the chosen sides (the cargo box sits inside it), the two lights hang just below.
        /// Chassis rebuilds the neon on every change, so a new one is fitted again on the next scan.
        /// </summary>
        internal static void FitNeon(LandVehicle lv, Transform neon)
        {
            var r = neon.GetComponent<MeshRenderer>();
            if (r == null) { Core.Log.Warning("[Models] the Chassis neon on net " + Net.Ids.Of(lv) + " has no renderer - it stays where Chassis put it."); return; }
            string value = ChassisApi.GetTuning(lv, "neon") ?? "";
            int comma = value.IndexOf(',');
            string sides = comma >= 0 ? value.Substring(comma + 1).ToLowerInvariant() : "fblr";
            r.enabled = false;

            var frame = new GameObject(SeatNeon).transform;
            frame.SetParent(neon, false);
            const float thick = 0.022f, x = 0.2f, back = -0.28f, front = 0.18f;
            float y = SeatUnderside - thick * 0.5f;
            var mat = r.sharedMaterial;
            if (sides.IndexOf('l') >= 0) Strip(frame, new Vector3(-x, y, (back + front) * 0.5f), new Vector3(thick, thick, front - back), mat);
            if (sides.IndexOf('r') >= 0) Strip(frame, new Vector3(x, y, (back + front) * 0.5f), new Vector3(thick, thick, front - back), mat);
            if (sides.IndexOf('f') >= 0) Strip(frame, new Vector3(0f, y, front), new Vector3(2f * x + thick, thick, thick), mat);
            if (sides.IndexOf('b') >= 0) Strip(frame, new Vector3(0f, y, back), new Vector3(2f * x + thick, thick, thick), mat);

            int lights = 0;
            foreach (var t in neon.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(ChassisNeonLight, StringComparison.Ordinal)) continue;
                t.localPosition = new Vector3(0f, SeatUnderside - 0.06f, lights == 0 ? 0.1f : -0.2f);
                lights++;
            }
            Core.Log.Msg("[Models] neon on net " + Net.Ids.Of(lv) + " moved under the seat (" + sides + "), " + lights + " light(s).");
        }

        private static void Strip(Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var t = Box(parent, "strip", at, size, mat);
            t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- simple shapes -----------------------------------------------------------------------------------------

        private static Transform Box(Transform parent, string name, Vector3 at, Vector3 size, Color c, bool glow) =>
            Box(parent, name, at, size, glow ? Glow(c) : Flat(c, 0.15f, 0f));

        private static Transform Box(Transform parent, string name, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // A collider under the vehicle would join its rigidbody; the parts are looks only.
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>A round pipe of diameter <paramref name="bore"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static Transform Tube(Transform parent, string name, Vector3 from, Vector3 to, float bore, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            // The primitive is 2 long along Y with diameter 1.
            t.localPosition = (from + to) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            t.localScale = new Vector3(bore, (to - from).magnitude * 0.5f, bore);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return t;
        }

        /// <summary>A square bar of width <paramref name="width"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static Transform Strut(Transform parent, string name, Vector3 from, Vector3 to, float width, Material mat)
        {
            var d = to - from;
            if (d.sqrMagnitude < 1e-8f) return null;
            var t = Box(parent, name, (from + to) * 0.5f, new Vector3(width, width, d.magnitude), mat);
            t.localRotation = Quaternion.LookRotation(d, Vector3.up);
            return t;
        }

        private static void AddCone(Transform parent, string name, float radius, float length, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Cone();
            go.AddComponent<MeshRenderer>().sharedMaterial = Glow(c);
            go.transform.localScale = new Vector3(radius, length, radius);
        }

        private static Mesh _cone;

        /// <summary>A unit cone: base radius 1 at y 0, tip at y 1, open at the base.</summary>
        private static Mesh Cone()
        {
            if (_cone != null) return _cone;
            const int n = 16;
            var v = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            v.Add(new Vector3(0f, 1f, 0f));
            for (int i = 0; i < n; i++) { tris.Add(i); tris.Add(n); tris.Add((i + 1) % n); }
            _cone = new Mesh { name = "rollin_cone" };
            var vl = new Il2CppSystem.Collections.Generic.List<Vector3>();
            foreach (var p in v) vl.Add(p);
            var tl = new Il2CppSystem.Collections.Generic.List<int>();
            foreach (var t in tris) tl.Add(t);
            _cone.SetVertices(vl);
            _cone.SetTriangles(tl, 0);
            _cone.RecalculateNormals();
            _cone.RecalculateBounds();
            _cone.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _cone;
        }

        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>(StringComparer.Ordinal);

        /// <summary>The game's prop look: URP Lit, a flat palette colour, little shine.</summary>
        private static Material Flat(Color c, float smoothness, float metallic)
        {
            string key = "flat" + ColorUtility.ToHtmlStringRGB(c) + smoothness.ToString("F2") + metallic.ToString("F2");
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader()) { name = "rollin_" + key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _mats[key] = m;
        }

        /// <summary>Unlit and double sided: flames read as light, not as lit plastic.</summary>
        internal static Material Glow(Color c)
        {
            string key = "glow" + ColorUtility.ToHtmlStringRGB(c);
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { Core.Log.Warning("[Models] URP/Unlit shader not found - glowing parts use the lit shader."); shader = LitShader(); }
            m = new Material(shader) { name = "rollin_" + key };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _mats[key] = m;
        }

        private static Shader LitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Core.Log.Warning("[Models] URP/Lit shader not found - the parts use the default shader."); shader = Shader.Find("Standard"); }
            return shader;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);
    }
}
