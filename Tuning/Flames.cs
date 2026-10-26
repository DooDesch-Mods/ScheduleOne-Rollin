using System;
using System.Collections.Generic;
using Rollin.Net;

namespace Rollin.Tuning
{
    /// <summary>
    /// The nitro flame out of each tailpipe: a blue additive cone that fades to its tip, a white core and a blue light,
    /// flickering while the driver holds nitro. Every peer builds its own at the tailpipe outlets; the driver's nitro
    /// state reaches everyone through the host (<c>nitro vehicle 1|0</c>).
    /// </summary>
    internal static class Flames
    {
        internal const string FlameName = "rollin_flame";

        private const float Length = 0.32f, Radius = 0.03f;
        private const float CoreLength = 0.14f, CoreRadius = 0.016f;

        private static readonly HashSet<int> _on = new HashSet<int>();
        private static Mesh _cone;
        private static Material _outer, _core;
        private static bool _materialsTried;

        internal static void Reset() => _on.Clear();

        /// <summary>Everyone: the host says a chair's nitro went on or off.</summary>
        internal static void OnNitro(int vehicle, bool on)
        {
            bool changed = on ? _on.Add(vehicle) : _on.Remove(vehicle);
            if (changed) Core.Log.Msg("[Flames] net " + vehicle + " nitro flames " + (on ? "on" : "off") + ".");
        }

        internal static bool IsOn(int vehicle) => _on.Contains(vehicle);

        /// <summary>The flame at a tailpipe outlet, built once; null when no material could be made.</summary>
        internal static Transform At(LandVehicle lv, Transform outlet)
        {
            var have = outlet.Find(FlameName);
            if (have != null) return have;
            if (!Materials()) return null;

            var flame = new GameObject(FlameName).transform;
            flame.SetParent(outlet, false);
            // Out of the back of the chair, whatever way the outlet marker faces.
            flame.rotation = Quaternion.LookRotation(-lv.transform.forward, lv.transform.up);
            Cone(flame, "outer", _outer, Length, Radius);
            Cone(flame, "core", _core, CoreLength, CoreRadius);
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(flame, false);
            light.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            light.type = LightType.Point;
            light.color = new Color(0.35f, 0.55f, 1f);
            light.range = 2.5f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            flame.gameObject.SetActive(false);
            Core.Log.Msg("[Flames] chair net " + Ids.Of(lv) + ": nitro flame built at a tailpipe.");
            return flame;
        }

        /// <summary>Shows or hides a chair's flames and lets them flicker.</summary>
        internal static void Tick(List<Transform> flames, bool on)
        {
            foreach (var f in flames)
            {
                if (f == null) continue;
                if (f.gameObject.activeSelf != on) f.gameObject.SetActive(on);
                if (!on) continue;
                float len = UnityEngine.Random.Range(0.75f, 1.25f);
                float wide = UnityEngine.Random.Range(0.9f, 1.1f);
                f.localScale = new Vector3(wide, wide, len);
                var l = f.GetComponentInChildren<Light>();
                if (l != null) l.intensity = UnityEngine.Random.Range(2f, 4f);
            }
        }

        private static void Cone(Transform parent, string name, Material mat, float length, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(radius, radius, length);
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>A unit cone along +Z: radius 1 at z 0, the tip at z 1; white at the base, black (nothing, additive) at the tip.</summary>
        private static Mesh ConeMesh()
        {
            if (_cone != null) return _cone;
            const int n = 16;
            var v = new Vector3[n + 2];
            var c = new Color[n + 2];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                c[i] = Color.white;
            }
            v[n] = new Vector3(0f, 0f, 1f); c[n] = Color.black;
            v[n + 1] = Vector3.zero; c[n + 1] = Color.white;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                tris[i * 6 + 0] = i; tris[i * 6 + 1] = n; tris[i * 6 + 2] = j;
                tris[i * 6 + 3] = j; tris[i * 6 + 4] = n + 1; tris[i * 6 + 5] = i;
            }
            _cone = new Mesh { name = "rollin_flame_cone" };
            _cone.vertices = v;
            _cone.colors = c;
            _cone.triangles = tris;
            _cone.RecalculateBounds();
            _cone.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _cone;
        }

        /// <summary>
        /// Additive, unlit, both sides, no depth write. URP's particle shader takes the cone's vertex colours, so the flame
        /// fades out to its tip; URP/Unlit is the fallback and draws an even cone.
        /// </summary>
        private static bool Materials()
        {
            if (_outer != null && _core != null) return true;
            if (_materialsTried) return false;
            _materialsTried = true;
            string used = "Universal Render Pipeline/Particles/Unlit";
            var shader = Shader.Find(used);
            if (shader == null) { used = "Universal Render Pipeline/Unlit"; shader = Shader.Find(used); }
            if (shader == null) { Core.Log.Error("[Flames] no URP unlit shader found - nitro shows no flames."); return false; }
            _outer = Additive(shader, new Color(0.25f, 0.45f, 1.6f));
            _core = Additive(shader, new Color(1.1f, 1.2f, 1.6f));
            Core.Log.Msg("[Flames] flame materials use '" + used + "'.");
            return true;
        }

        private static Material Additive(Shader shader, Color colour)
        {
            var m = new Material(shader) { name = "rollin_flame" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f); // additive
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            m.SetColor("_BaseColor", colour);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return m;
        }
    }
}
