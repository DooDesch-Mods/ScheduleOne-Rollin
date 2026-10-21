using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rollin.Tuning
{
    /// <summary>
    /// The game's own meshes for the tuning parts: a car wheel, a tailpipe, the police lightbar, the NPC umbrella, a
    /// garden gnome and other props. Each source is found once (a vehicle prefab, an item's stored model, a Resources
    /// prefab or a loaded object by name) and copied as plain mesh renderers with the game's own materials, so a part
    /// carries no network, physics or item behaviour. A source that cannot be found is logged and builds nothing.
    /// </summary>
    internal static class GameMeshes
    {
        internal const string RimSport = "rim_sport", RimMuscle = "rim_muscle", Tailpipe = "tailpipe", Lightbar = "lightbar",
            JerryCan = "jerrycan", Box = "box", Umbrella = "umbrella", Radio = "radio", Gnome = "gnome", Cone = "cone";

        internal static readonly string[] All = { RimSport, RimMuscle, Tailpipe, Lightbar, JerryCan, Box, Umbrella, Radio, Gnome, Cone };

        private static readonly Dictionary<string, Transform> _found = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private static readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Lower LODs, impostors and collider hulls sit beside the visible mesh; a copy keeps only LOD0.</summary>
        private static readonly Regex Skip = new Regex(@"LOD\s*_?[1-9]|Impostor|Convex|Collider", RegexOptions.IgnoreCase);

        /// <summary>The source object for <paramref name="key"/>, or null (logged once) when the game has none.</summary>
        internal static Transform Source(string key)
        {
            if (_found.TryGetValue(key, out var t) && t != null) return t;
            if (_missing.Contains(key)) return null;
            try { t = Find(key); }
            catch (Exception e) { Core.Log.Error("[GameMeshes] looking up '" + key + "' failed: " + e); t = null; }
            if (t == null)
            {
                _missing.Add(key);
                Core.Log.Error("[GameMeshes] no game mesh for '" + key + "' - the part shows nothing.");
                return null;
            }
            _found[key] = t;
            Core.Log.Msg("[GameMeshes] '" + key + "' from " + PathOf(t) + ".");
            return t;
        }

        /// <summary>Forgets every lookup; a new scene brings new objects.</summary>
        internal static void Reset()
        {
            _found.Clear();
            _missing.Clear();
        }

        private static Transform Find(string key)
        {
            switch (key)
            {
                case RimSport: return Vehicle("cheetah", "SportsCarWheel_FL/ModelContainer/Model/Sports car wheel/sports car wheel");
                case RimMuscle: return Vehicle("hounddog", "MuscleCarWheel_FR/ModelContainer/Model/Muscle Car Wheel/muscle car wheel");
                case Tailpipe: return Vehicle("bruiser", "SUV/suv/TailPipe");
                case Lightbar: return Vehicle("bruiser_police", "SUV/Lightbar/lightbar");
                case JerryCan: return Item("gasoline", "jerry can");
                case Box: return Named("smallbox", null, "Box");
                case Umbrella: return Resource("equippables/umbrella/Umbrella_TP", "Umbrella (Static)");
                case Radio: return Named("SM_Item_Radio_01", "SM_Item_Radio_01", null);
                case Gnome: return Named("gnome", null, "Beard");
                case Cone: return Named("trafficcone", null, null);
            }
            Core.Log.Error("[GameMeshes] unknown source '" + key + "'.");
            return null;
        }

        /// <summary>The police lightbar's on and off materials, from the <c>PoliceLight</c> beside the bar.</summary>
        internal static bool LightbarMaterials(out Material redOn, out Material redOff, out Material blueOn, out Material blueOff)
        {
            redOn = redOff = blueOn = blueOff = null;
            var bar = Source(Lightbar);
            var holder = bar != null ? bar.parent : null;
            var pl = holder != null ? holder.GetComponent<Il2CppScheduleOne.Lighting.PoliceLight>() : null;
            if (pl == null) { Core.Log.Warning("[GameMeshes] no PoliceLight beside the lightbar - the lenses keep their off colour."); return false; }
            redOn = pl.RedOnMat; redOff = pl.RedOffMat; blueOn = pl.BlueOnMat; blueOff = pl.BlueOffMat;
            bool ok = redOn != null && blueOn != null;
            if (!ok) Core.Log.Warning("[GameMeshes] the PoliceLight has no on materials - the lenses keep their off colour.");
            return ok;
        }

        // ---- lookups ---------------------------------------------------------------------------------------------------

        private static Transform Vehicle(string code, string path)
        {
            var vm = NetworkSingleton<VehicleManager>.Instance;
            if (vm == null) { Core.Log.Warning("[GameMeshes] no VehicleManager yet for '" + code + "'."); return null; }
            var lv = vm.GetVehiclePrefab(code);
            if (lv == null) { Core.Log.Warning("[GameMeshes] the game has no vehicle '" + code + "'."); return null; }
            var t = lv.transform.Find(path);
            if (t != null) return t;
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            t = Descendant(lv.transform, leaf);
            Core.Log.Warning("[GameMeshes] '" + code + "' has no '" + path + "'" + (t != null ? ", took " + PathOf(t) + " instead." : "."));
            return t;
        }

        private static Transform Item(string id, string child)
        {
            var def = Il2CppScheduleOne.Registry.GetItem(id);
            if (def == null) { Core.Log.Warning("[GameMeshes] the game has no item '" + id + "'."); return null; }
            var storable = def.TryCast<Il2CppScheduleOne.ItemFramework.StorableItemDefinition>();
            var stored = storable != null ? storable.StoredItem : null;
            if (stored == null) { Core.Log.Warning("[GameMeshes] item '" + id + "' has no stored model."); return null; }
            var t = Descendant(stored.transform, child);
            if (t == null) Core.Log.Warning("[GameMeshes] item '" + id + "' has no '" + child + "' under " + stored.name + ".");
            return t;
        }

        private static Transform Resource(string path, string child)
        {
            var go = Resources.Load<GameObject>(path);
            if (go == null) { Core.Log.Warning("[GameMeshes] no Resources prefab '" + path + "'."); return null; }
            var t = Descendant(go.transform, child);
            if (t == null) Core.Log.Warning("[GameMeshes] '" + path + "' has no '" + child + "'.");
            return t;
        }

        /// <summary>
        /// A loaded object (scene or asset, active or not) named <paramref name="name"/> whose meshes are its own, not a
        /// static batch: <paramref name="mesh"/> names its own mesh, <paramref name="child"/> a child it must have.
        /// </summary>
        private static Transform Named(string name, string mesh, string child)
        {
            int seen = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || t.name != name) continue;
                seen++;
                if (child != null && t.Find(child) == null) continue;
                if (mesh != null)
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != mesh) continue;
                }
                if (Batched(t)) continue;
                return t;
            }
            Core.Log.Warning("[GameMeshes] " + seen + " object(s) named '" + name + "', none with " + (mesh ?? child) + " and its own mesh.");
            return null;
        }

        private static bool Batched(Transform t)
        {
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) return true;
            return false;
        }

        private static Transform Descendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        // ---- copies ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// A copy of <paramref name="key"/>'s visible meshes under <paramref name="parent"/>, turned and scaled as the
        /// source lies in the world, at the parent's origin. Null when the game has no such mesh.
        /// </summary>
        internal static Transform Copy(string key, Transform parent, Material fallback = null)
        {
            var src = Source(key);
            if (src == null) return null;
            var copy = CopyNode(src, parent, fallback);
            // The source's lie in the world (a prefab's root sits at the origin unturned): a gnome placed on a rock group
            // or an umbrella under a hand bone gets its upright pose from its parents, not from itself.
            copy.localPosition = Vector3.zero;
            copy.localRotation = src.rotation;
            copy.localScale = src.lossyScale;
            return copy;
        }

        private static Transform CopyNode(Transform src, Transform parent, Material fallback)
        {
            var go = new GameObject(src.name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = src.localPosition;
            t.localRotation = src.localRotation;
            t.localScale = src.localScale;
            var mf = src.GetComponent<MeshFilter>();
            var mr = src.GetComponent<MeshRenderer>();
            if (mf != null && mf.sharedMesh != null && mr != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                var mats = mr.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null) { any = true; continue; }
                    mats[i] = fallback;
                }
                if (!any && fallback == null) Core.Log.Warning("[GameMeshes] " + src.name + " has no material and no fallback.");
                r.sharedMaterials = mats;
            }
            for (int i = 0; i < src.childCount; i++)
            {
                var c = src.GetChild(i);
                if (Skip.IsMatch(c.name)) continue;
                if (c.GetComponent<Light>() != null && c.GetComponent<MeshFilter>() == null && c.childCount == 0) continue;
                CopyNode(c, t, fallback);
            }
            return t;
        }

        /// <summary>The copy's mesh bounds in <paramref name="space"/>, from the eight corners of every mesh.</summary>
        internal static Bounds Measure(Transform space, Transform root)
        {
            var box = new Bounds();
            bool first = true;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (first) { box = new Bounds(p, Vector3.zero); first = false; }
                    else box.Encapsulate(p);
                }
            }
            return box;
        }

        /// <summary>One line per source for the console: found or not, where from, how big.</summary>
        internal static string Describe(string key)
        {
            var src = Source(key);
            if (src == null) return key + ": missing";
            var tmp = new GameObject("rollin_probe");
            try
            {
                var copy = Copy(key, tmp.transform);
                var b = Measure(tmp.transform, copy);
                int n = copy.GetComponentsInChildren<MeshRenderer>(true).Length;
                var shaders = new HashSet<string>();
                foreach (var r in copy.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials) shaders.Add(m == null ? "null" : m.name + " (" + (m.shader != null ? m.shader.name : "no shader") + ")");
                return key + ": " + PathOf(src) + ", " + n + " renderer(s), size " + b.size.ToString("F2") + ", " + string.Join(", ", shaders);
            }
            finally { UnityEngine.Object.Destroy(tmp); }
        }
    }
}
