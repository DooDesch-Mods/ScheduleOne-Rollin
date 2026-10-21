using System;
using System.Collections.Generic;
using Rollin.Compat;

namespace Rollin.Chair
{
    /// <summary>
    /// The look of a spawned wheelchair: a copy of a placed wheelchair prop's model under the frame "RollinModel",
    /// built by every peer for its own copy of the vehicle when it starts. The big wheels and the casters move under
    /// the hull's wheel pivots, so they turn with the physics wheels and the hands can follow the rims. The frame meshes
    /// become the hull's paintable body, so Chassis paint and neon land on the chair instead of the hidden car.
    /// </summary>
    internal static class WheelchairModel
    {
        private static readonly string[] WheelMeshes = { "Wheel", "Wheel (1)", "SmallWheel", "SmallWheel (1)" };

        internal static void Build(LandVehicle lv)
        {
            var root = lv.transform;
            if (root.Find(WheelchairType.FrameName) != null) return;

            var template = FindTemplate();
            if (template == null)
            {
                Core.Log.Error("[Wheelchair] no placed wheelchair prop to copy the look from - this wheelchair stays invisible.");
                return;
            }

            var frame = new GameObject(WheelchairType.FrameName).transform;
            frame.SetParent(root, false);
            var model = UnityEngine.Object.Instantiate(template.gameObject, frame, false);
            model.name = "wheelchair";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            foreach (var b in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(b);
            // The template can be the prop the swap just hid, whose renderers are off; the copy is the visible look.
            int shown = 0;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) if (!r.enabled) { r.enabled = true; shown++; }
            if (shown > 0) Core.Log.Msg("[Wheelchair] template " + template.parent.name + " was hidden, " + shown + " renderer(s) switched on in the copy.");

            int moved = 0;
            moved += Hang(lv, frame, model.transform, "Wheel", WheelchairType.RimRight);
            moved += Hang(lv, frame, model.transform, "Wheel (1)", WheelchairType.RimLeft);
            moved += Hang(lv, frame, model.transform, "SmallWheel", WheelchairType.CasterRight);
            moved += Hang(lv, frame, model.transform, "SmallWheel (1)", WheelchairType.CasterLeft);
            int body = PaintTargets(lv, model.transform);
            Core.Log.Msg("[Wheelchair] look built from " + template.name + " for " + lv.name + " (net " + Net.Ids.Of(lv) + "), " + moved + "/4 wheels on pivots, " + body + " paintable mesh(es).");
        }

        /// <summary>The model child ("wheelchair") of any placed prop; they are the same mesh.</summary>
        internal static Transform FindTemplate()
        {
            foreach (var rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb == null || rb.name.IndexOf("wheelchair", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (rb.GetComponent<LandVehicle>() != null) continue;
                var model = rb.transform.Find("wheelchair");
                if (model != null) return model;
            }
            return null;
        }

        private static int Hang(LandVehicle lv, Transform frame, Transform model, string meshName, string pivotName)
        {
            var mesh = model.Find(meshName);
            var pivot = WheelchairType.Find(lv.transform, pivotName);
            if (mesh == null || pivot == null)
            {
                Core.Log.Warning("[Wheelchair] '" + meshName + "' or pivot '" + pivotName + "' missing; that wheel stays fixed to the frame.");
                return 0;
            }
            // Put the pivot on the wheel's centre at rest, aligned with the frame, then hang the mesh under it with its
            // own import rotation. Wheel.FixedUpdateWheel moves the pivot from then on.
            pivot.position = mesh.position;
            pivot.rotation = frame.rotation;
            mesh.SetParent(pivot, true);
            return 1;
        }

        /// <summary>
        /// The hull's VehicleColor points at the hidden car body. The frame meshes take its place: vanilla's own colour
        /// (white on a spawned hull) tints the chair texture the way it was, and Chassis paints and measures them.
        /// </summary>
        private static int PaintTargets(LandVehicle lv, Transform model)
        {
            var vc = lv.GetComponent<VehicleColor>() ?? lv.GetComponentInChildren<VehicleColor>(true);
            if (vc == null) { Core.Log.Warning("[Wheelchair] the hull has no VehicleColor - paint and neon have no body on this chair."); return 0; }
            var list = new List<VehicleColor.BodyMesh>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (Array.IndexOf(WheelMeshes, r.name) >= 0) continue;
                list.Add(new VehicleColor.BodyMesh { Renderer = r, MaterialIndex = 0 });
            }
            vc.BodyMeshes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleColor.BodyMesh>(list.ToArray());
            return list.Count;
        }

        /// <summary>Whether a transform belongs to the chair's own look (not a tuning part).</summary>
        internal static bool IsWheelMesh(string name) => Array.IndexOf(WheelMeshes, name) >= 0;
    }
}
