using System;
using System.Collections.Generic;
using Chassis.Api;
using Rollin.Chair;

namespace Rollin.Tuning
{
    /// <summary>
    /// The trim paint on the chair itself: the game's wheelchair mesh keeps its push handle grips, armrest pads and
    /// footrests in submeshes of their own, each with its own material slot. Those slots take the trim paint while
    /// the chair has one and their own material otherwise. The frame (submesh 0) stays with the body paint.
    /// </summary>
    internal static class StockTrim
    {
        /// <summary>The slot's own material per renderer and submesh, from before the first trim paint.</summary>
        private static readonly Dictionary<KeyValuePair<IntPtr, int>, Material> _own = new Dictionary<KeyValuePair<IntPtr, int>, Material>();

        private static bool _warnedSlots;

        internal static void Init() => ChassisApi.TuningChanged += OnTuningChanged;

        internal static void Reset() { _own.Clear(); _warnedSlots = false; }

        private static void OnTuningChanged(LandVehicle lv, string key)
        {
            if (!WheelchairType.Is(lv) || (key != "paint2" && key != "*")) return;
            try { Apply(lv); }
            catch (Exception e) { Core.Log.Error("[StockTrim] painting the stock trim of net " + Net.Ids.Of(lv) + " failed: " + e); }
        }

        private enum Piece { None, Grip, Armrest, Footrest }

        /// <summary>Which trim piece a submesh is, from its bounds in the chair's space (ground 0, +Z forward).</summary>
        private static Piece Classify(Vector3 c, Vector3 size)
        {
            if (c.y > 1.0f && c.z < -0.25f) return Piece.Grip;
            if (c.y > 0.7f && c.y < 0.85f && size.y < 0.12f) return Piece.Armrest;
            if (c.y < 0.3f) return Piece.Footrest;
            return Piece.None;
        }

        internal static void Apply(LandVehicle lv)
        {
            var frame = lv != null ? lv.transform.Find(WheelchairType.FrameName) : null;
            if (frame == null) { Core.Log.Msg("[StockTrim] net " + Net.Ids.Of(lv) + " has no chair model yet - the trim follows when it is built."); return; }
            string trim = ChassisApi.GetTuning(lv, "paint2");
            var paint = Models.TrimPaint(trim);
            int grips = 0, arms = 0, feet = 0;
            foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (WheelchairModel.IsWheelMesh(r.name) || IsPart(r.transform, frame)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || mesh.subMeshCount < 2) continue;
                var mats = r.sharedMaterials;
                if (mats.Length < mesh.subMeshCount)
                {
                    if (!_warnedSlots) Core.Log.Warning("[StockTrim] " + r.name + " has " + mats.Length + " material(s) for " + mesh.subMeshCount + " submeshes - its trim stays stock.");
                    _warnedSlots = true;
                    continue;
                }
                bool changed = false;
                for (int i = 1; i < mesh.subMeshCount; i++)
                {
                    Bounds b = ToFrame(mesh.GetSubMesh(i).bounds, r.transform, frame);
                    var piece = Classify(b.center, b.size);
                    if (piece == Piece.None) continue;
                    var slot = new KeyValuePair<IntPtr, int>(r.Pointer, i);
                    if (!_own.ContainsKey(slot)) _own[slot] = mats[i];
                    var want = paint ?? _own[slot];
                    if (!Compat.Interop.Same(mats[i], want)) { mats[i] = want; changed = true; }
                    if (piece == Piece.Grip) grips++; else if (piece == Piece.Armrest) arms++; else feet++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            Core.Log.Msg("[StockTrim] net " + Net.Ids.Of(lv) + ": " + (paint != null ? "trim '" + trim + "'" : "own look") + " on " + grips + " grip, "
                + arms + " armrest and " + feet + " footrest slot(s).");
        }

        private static bool IsPart(Transform t, Transform frame)
        {
            for (; t != null && t != frame; t = t.parent)
                if (t.name.StartsWith("rollin_part_", StringComparison.Ordinal) || t.name.StartsWith("Chassis_Tune_", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Mesh-space bounds to the chair's space, through all eight corners.</summary>
        private static Bounds ToFrame(Bounds b, Transform mesh, Transform frame)
        {
            var result = new Bounds(frame.InverseTransformPoint(mesh.TransformPoint(b.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(frame.InverseTransformPoint(mesh.TransformPoint(corner)));
            }
            return result;
        }
    }
}
