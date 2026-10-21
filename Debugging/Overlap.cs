#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Rollin.Chair;
using Rollin.Tuning;

namespace Rollin.Debugging
{
    /// <summary>
    /// <c>rollin tune overlap</c>: measures every tuning part in chair space (+Z forward, ground 0) against the places it
    /// must leave free, and logs each hit. Two kinds of obstacles:
    /// fixed keep-out boxes for the rider, the pusher and the frame, so the check works on an empty chair too; and the
    /// bones of every player within reach, sampled along each limb, so a part that clips a real arm or head shows up.
    /// A part is measured by its mesh bounds carried into chair space, which stays tight on a turned part.
    /// </summary>
    internal static class Overlap
    {
        /// <summary>Overlaps thinner than this (metres) are a part resting on the frame, not clipping.</summary>
        private const float Touch = 0.012f;

        private struct Zone
        {
            internal string Name;
            internal Bounds Box;
            internal Zone(string name, Vector3 min, Vector3 max)
            {
                Name = name;
                Box = new Bounds((min + max) * 0.5f, max - min);
            }
        }

        // Rider seated at (0, 0.5, -0.12) with the hands on the push rims and the feet on the footrests (WheelchairProfile);
        // pusher standing at z -0.95 with the hands on the handles at (±0.2, 0.95, -0.45).
        private static readonly Zone[] Zones =
        {
            new Zone("rider head", new Vector3(-0.15f, 1.05f, -0.22f), new Vector3(0.15f, 1.42f, 0.2f)),
            new Zone("rider torso", new Vector3(-0.2f, 0.5f, -0.214f), new Vector3(0.2f, 1.08f, 0.12f)),
            new Zone("rider thighs", new Vector3(-0.22f, 0.5f, -0.1f), new Vector3(0.22f, 0.68f, 0.4f)),
            new Zone("rider shins", new Vector3(-0.24f, 0.08f, 0.3f), new Vector3(0.24f, 0.56f, 0.6f)),
            // The arms run from the shoulders out and back to the elbows, then down past the frame tubes (x 0.26) to the
            // hands on the push rims.
            new Zone("rider arm R", new Vector3(0.27f, 0.45f, -0.45f), new Vector3(0.6f, 1.05f, 0.05f)),
            new Zone("rider arm L", new Vector3(-0.6f, 0.45f, -0.45f), new Vector3(-0.27f, 1.05f, 0.05f)),
            new Zone("pusher body", new Vector3(-0.3f, 0f, -1.18f), new Vector3(0.3f, 1.85f, -0.72f)),
            // Each arm reaches from the shoulder to its handle; the space between the arms stays free.
            new Zone("pusher arm R", new Vector3(0.12f, 0.88f, -0.85f), new Vector3(0.36f, 1.45f, -0.43f)),
            new Zone("pusher arm L", new Vector3(-0.36f, 0.88f, -0.85f), new Vector3(-0.12f, 1.45f, -0.43f)),
            // The sling's measured faces in the game's wheelchair mesh.
            new Zone("frame backrest", new Vector3(-0.25f, 0.5f, -0.285f), new Vector3(0.25f, 1.013f, -0.214f)),
            new Zone("frame seat", new Vector3(-0.25f, 0.479f, -0.234f), new Vector3(0.25f, 0.499f, 0.266f)),
            new Zone("wheel R", new Vector3(0.26f, 0f, -0.56f), new Vector3(0.34f, 0.66f, 0.1f)),
            new Zone("wheel L", new Vector3(-0.34f, 0f, -0.56f), new Vector3(-0.26f, 0.66f, 0.1f)),
            new Zone("caster R", new Vector3(0.24f, 0f, 0.2f), new Vector3(0.32f, 0.2f, 0.41f)),
            new Zone("caster L", new Vector3(-0.32f, 0f, 0.2f), new Vector3(-0.24f, 0.2f, 0.41f)),
        };

        internal static void Run(LandVehicle lv)
        {
            var root = lv.transform;
            var parts = Measure(root);
            if (parts.Count == 0) { DebugConsolePatch.Say("tune overlap: no tuning parts on net " + Net.Ids.Of(lv)); return; }

            var limbs = Limbs(root);
            int hits = 0;
            foreach (var p in parts)
            {
                foreach (var z in Zones)
                {
                    if (Allowed(p.Path, z.Name)) continue;
                    float depth = Depth(p.Box, z.Box);
                    if (depth <= Touch) continue;
                    Core.Log.Warning("[overlap] " + p.Path + " enters " + z.Name + " by " + Cm(depth) + " (part " + Box(p.Box) + ")");
                    hits++;
                }
                foreach (var q in parts)
                {
                    // Each pair once, and only across slots: one part's own pieces may touch.
                    if (string.CompareOrdinal(p.Path, q.Path) >= 0 || Slot(p.Path) == Slot(q.Path)) continue;
                    // A pole is a mount: the lightbar and the umbrella share it on purpose.
                    if (p.Path.EndsWith("/pole", StringComparison.Ordinal) || q.Path.EndsWith("/pole", StringComparison.Ordinal)) continue;
                    float depth = Depth(p.Box, q.Box);
                    if (depth <= Touch) continue;
                    Core.Log.Warning("[overlap] " + p.Path + " enters " + q.Path + " by " + Cm(depth));
                    hits++;
                }
                foreach (var l in limbs)
                {
                    if (p.Path.Contains(Models.Hat)) break;
                    // The rider's hands grip the rims.
                    if ((p.Path.Contains(Models.SpinLeft) || p.Path.Contains(Models.SpinRight)) && l.Name.Contains("Hand")) continue;
                    var inner = p.Box;
                    inner.Expand(-2f * Touch);
                    if (!inner.Contains(l.Point)) continue;
                    Core.Log.Warning("[overlap] " + p.Path + " holds " + l.Name + " at " + V(l.Point) + " (part " + Box(p.Box) + ")");
                    hits++;
                    break;
                }
            }
            Core.Log.Msg("[overlap] " + parts.Count + " renderer(s) checked against " + Zones.Length + " zone(s) and " + limbs.Count + " limb point(s).");
            DebugConsolePatch.Say("tune overlap: " + hits + " hit(s) in " + parts.Count + " renderer(s), " + limbs.Count + " limb point(s)" + (hits > 0 ? " - see the log" : ""));
        }

        private static string Slot(string path)
        {
            int slash = path.IndexOf('/');
            return slash < 0 ? path : path.Substring(0, slash);
        }

        /// <summary>A hat sits on the head and rims are the wheels: those two meet their zone on purpose.</summary>
        private static bool Allowed(string path, string zone)
        {
            if (path.Contains(Models.Hat)) return zone == "rider head";
            // The gnome sits between the thighs on purpose; the leg bones still catch it if it touches a leg.
            if (path.Contains("rollin_gnome")) return zone == "rider thighs";
            // The jerry can rack leans on the backrest, and the exhaust riser is bolted to the seat's underside.
            if (path.Contains("rollin_rack")) return zone == "frame backrest";
            if (path.Contains("Chassis_Tune_exhaust/riser") || path.Contains("Chassis_Tune_exhaust/flange")) return zone == "frame seat";
            // The rider's hands grip the rims, so a rim may meet the arm zone as well.
            if (path.Contains(Models.SpinLeft)) return zone == "wheel L" || zone == "rider arm L";
            if (path.Contains(Models.SpinRight)) return zone == "wheel R" || zone == "rider arm R";
            return false;
        }

        private struct Part { internal string Path; internal Bounds Box; }
        private struct Limb { internal string Name; internal Vector3 Point; }

        private static List<Part> Measure(Transform root)
        {
            var list = new List<Part>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                string path = r.name;
                bool tuned = false;
                for (var t = r.transform; t != null && t != root; t = t.parent)
                {
                    if (t != r.transform) path = t.name + "/" + path;
                    if (t.name.StartsWith("Chassis_Tune_", StringComparison.Ordinal)) tuned = true;
                }
                if (!tuned) continue;
                if (!LocalBounds(r, out var local)) { Core.Log.Warning("[overlap] " + path + " has no mesh bounds, skipped."); continue; }
                list.Add(new Part { Path = path, Box = Spinner(r.transform, out var spin) ? Wheel(root, spin, r.transform, local) : ToChair(root, r.transform, local) });
            }
            return list;
        }

        /// <summary>The rim holder above <paramref name="t"/>, which turns with the wheel.</summary>
        private static bool Spinner(Transform t, out Transform spin)
        {
            for (spin = t; spin != null; spin = spin.parent)
                if (spin.name == Models.SpinLeft || spin.name == Models.SpinRight) return true;
            return false;
        }

        /// <summary>
        /// A rim measured in its own turning holder and moved to the holder's spot: a box around a turned wheel would
        /// grow with the turn, the wheel itself does not.
        /// </summary>
        private static Bounds Wheel(Transform root, Transform spin, Transform t, Bounds local)
        {
            var box = ToChair(spin, t, local);
            box.center += root.InverseTransformPoint(spin.position);
            return box;
        }

        private static bool LocalBounds(Renderer r, out Bounds b)
        {
            b = default;
            var skinned = r.TryCast<SkinnedMeshRenderer>();
            if (skinned != null) { b = skinned.localBounds; return true; }
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            b = mf.sharedMesh.bounds;
            return true;
        }

        private static Bounds ToChair(Transform root, Transform t, Bounds local)
        {
            Vector3 min = local.min, max = local.max;
            var outBox = new Bounds();
            bool first = true;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                var c = root.InverseTransformPoint(t.TransformPoint(corner));
                if (first) { outBox = new Bounds(c, Vector3.zero); first = false; }
                else outBox.Encapsulate(c);
            }
            return outBox;
        }

        /// <summary>
        /// Bones of every player whose body is within 2.5 m of the chair, five points along each bone to its parent bone.
        /// The distance is taken from the bones: a seated player's root stays where they sat down.
        /// </summary>
        private static List<Limb> Limbs(Transform root)
        {
            var list = new List<Limb>();
            foreach (var p in Player.PlayerList)
            {
                if (p == null) continue;
                var seen = new HashSet<IntPtr>();
                var mine = new List<Limb>();
                float nearest = float.MaxValue;
                // The avatar is not under the player object in every state; take it from the player.
                var body = p.Avatar != null ? p.Avatar.transform : p.transform;
                var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in renderers)
                {
                    var bones = smr.bones;
                    if (bones == null || bones.Length == 0) { Core.Log.Msg("[overlap] " + p.PlayerName + "'s " + smr.name + " has no bones."); continue; }
                    var set = new HashSet<IntPtr>();
                    foreach (var b in bones) if (b != null) set.Add(b.Pointer);
                    foreach (var b in bones)
                    {
                        if (b == null || !seen.Add(b.Pointer)) continue;
                        nearest = Mathf.Min(nearest, Vector3.Distance(b.position, root.position));
                        var parent = b.parent;
                        bool limb = parent != null && set.Contains(parent.Pointer);
                        for (int i = 0; i < (limb ? 5 : 1); i++)
                        {
                            var w = limb ? Vector3.Lerp(b.position, parent.position, i / 5f) : b.position;
                            mine.Add(new Limb { Name = p.PlayerName + "'s " + b.name, Point = root.InverseTransformPoint(w) });
                        }
                    }
                }
                if (nearest > 2.5f)
                {
                    Core.Log.Msg("[overlap] " + p.PlayerName + " is " + (nearest == float.MaxValue ? "without bones" : nearest.ToString("F1", CultureInfo.InvariantCulture) + " m away") + ", not measured.");
                    continue;
                }
                list.AddRange(mine);
                Core.Log.Msg("[overlap] " + p.PlayerName + ": " + renderers.Length + " skinned renderer(s), " + mine.Count + " limb point(s).");
            }
            return list;
        }

        /// <summary>How deep two boxes overlap along their thinnest shared axis; 0 or less when they do not touch.</summary>
        private static float Depth(Bounds a, Bounds b)
        {
            float x = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float y = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            float z = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            return Mathf.Min(x, Mathf.Min(y, z));
        }

        private static string Cm(float m) => (m * 100f).ToString("0", CultureInfo.InvariantCulture) + " cm";

        private static string V(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);

        private static string Box(Bounds b) => V(b.min) + ".." + V(b.max);
    }
}
#endif
