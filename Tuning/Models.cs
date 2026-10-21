using System;
using System.Collections.Generic;
using Chassis.Api;
using Rollin.Chair;

namespace Rollin.Tuning
{
    /// <summary>
    /// The visual tuning parts. Every part builds its pieces in the chair's own space (+Z forward, ground at 0, the
    /// seat at 0.5 m, the push handles at 0.95 m behind the backrest), so its <see cref="PartFit"/> stays at the origin.
    /// Moving pieces (rims, hat, rocket flame, lightbar) carry names <see cref="Effects"/> looks for.
    /// </summary>
    internal static class Models
    {
        // Names Effects drives.
        internal const string SpinLeft = "rollin_spin_L", SpinRight = "rollin_spin_R";
        internal const string Hat = "rollin_hat";
        // Not "rollin_flame": Glb.Spawn names a model "rollin_<model>", and the flame decal model is "flame".
        internal const string Flame = "rollin_fx_flame";
        internal const string Thruster = "rollin_thruster";
        internal const string Backfire = "rollin_backfire";
        internal const string BlinkRed = "rollin_blink_red", BlinkBlue = "rollin_blink_blue";
        internal const string Pulse = "rollin_pulse";

        /// <summary>The stock chair wheels a set of rims replaces.</summary>
        private static readonly string[] StockRims = { "Wheel", "Wheel (1)" };

        internal enum Anchor { Bottom, Centre, Back }

        internal static IEnumerable<TuningPart> Parts()
        {
            // ---- spoiler -------------------------------------------------------------------------------------------
            yield return Part("rollin_spoiler_gt", "spoiler", "GT Wing", 2500, root =>
                Piece(root, "spoiler_gt", new Vector3(0f, 1.02f, -0.5f), Vector3.zero, Width("spoiler_gt", 0.75f), Anchor.Bottom, PaintSecond),
                new TuningStats { Grip = 1.1f });
            yield return Part("rollin_spoiler_drag", "spoiler", "Drag Wing", 4200, root =>
                Piece(root, "spoiler_drag", new Vector3(0f, 1.05f, -0.52f), Vector3.zero, Width("spoiler_drag", 0.85f), Anchor.Bottom, PaintSecond),
                new TuningStats { Grip = 1.2f, Steer = 0.9f });

            // ---- booster -------------------------------------------------------------------------------------------
            yield return Part("rollin_rocket", "booster", "Rocket Booster", 15000, root =>
                Rocket(root, new Vector3(0f, 0.55f, -0.33f), 0.62f),
                new TuningStats { Torque = 1.8f, Gearing = 1.8f, EnginePitch = 1.2f });
            yield return Part("rollin_rocket_twin", "booster", "Twin Boosters", 30000, root =>
            {
                Rocket(root, new Vector3(0.5f, 0.6f, 0.2f), 0.55f);
                Rocket(root, new Vector3(-0.5f, 0.6f, 0.2f), 0.55f);
            }, new TuningStats { Torque = 2.6f, Gearing = 2.6f, EnginePitch = 1.35f });

            // ---- exhaust -------------------------------------------------------------------------------------------
            yield return Part("rollin_exhaust_twin", "exhaust", "Twin Tailpipes", 900, root =>
            {
                foreach (float x in new[] { 0.14f, -0.14f })
                {
                    float len = 0.3f;
                    Piece(root, "tailpipe", new Vector3(x, 0.22f, -0.38f - len * 0.5f), Vector3.zero, Uniform(len / Size("tailpipe").z), Anchor.Centre);
                    Marker(root, Backfire, new Vector3(x, 0.22f, -0.4f - len));
                }
            }, new TuningStats { EnginePitch = 0.85f });
            yield return Part("rollin_exhaust_stacks", "exhaust", "Truck Stacks", 1800, root =>
            {
                foreach (float x in new[] { 0.27f, -0.27f })
                {
                    var s = Size("pipe");
                    float len = 0.9f, thick = 0.07f;
                    // The pipe runs along Z; tipped up it becomes a smoke stack.
                    Piece(root, "pipe", new Vector3(x, 0.35f, -0.42f), new Vector3(-90f, 0f, 0f),
                        new Vector3(thick / s.x, thick / s.y, len / s.z), Anchor.Back);
                    Marker(root, Backfire, new Vector3(x, 0.35f + len + 0.02f, -0.42f));
                }
            }, new TuningStats { EnginePitch = 0.7f, Mass = 1.1f });

            // ---- rims ----------------------------------------------------------------------------------------------
            yield return Rims("rollin_rims_racing", "Racing Rims", 1200, "wheel_racing");
            yield return Rims("rollin_rims_truck", "Monster Truck Tyres", 1600, "wheel_truck");
            yield return Rims("rollin_rims_dark", "Murdered-Out Rims", 1400, "wheel_dark");

            // ---- hat -----------------------------------------------------------------------------------------------
            yield return Part("rollin_hat_crown", "hat", "Royal Crown", 50000, root => HatPiece(root, "crown", 0.22f, 0f), null);
            yield return Part("rollin_hat_fedora", "hat", "M'lady Fedora", 420, root => HatPiece(root, "fedora", 0.34f, -0.04f), null);
            yield return Part("rollin_hat_cone", "hat", "Traffic Cone", 5, root => HatPiece(root, "cone", 0.24f, 0.09f, height: true), null);

            // ---- mascot --------------------------------------------------------------------------------------------
            yield return Part("rollin_duck", "mascot", "Rubber Duck", 3, root =>
                Piece(root, "duck", new Vector3(0.26f, 0.72f, 0.12f), new Vector3(0f, 180f, 0f), Width("duck", 0.12f), Anchor.Bottom), null);
            yield return Part("rollin_duck_mega", "mascot", "Mega Duck", 999, root =>
                Piece(root, "duck", new Vector3(0f, 1.0f, -0.4f), new Vector3(0f, 180f, 0f), Width("duck", 0.42f), Anchor.Bottom),
                new TuningStats { Mass = 1.15f });

            // ---- roof ----------------------------------------------------------------------------------------------
            yield return Part("rollin_umbrella", "roof", "Emergency Umbrella", 60, root =>
                Piece(root, "umbrella", new Vector3(0.24f, 0.62f, -0.42f), new Vector3(0f, 0f, 8f), Height("umbrella", 0.9f), Anchor.Bottom),
                new TuningStats { Mass = 0.9f });
            yield return Part("rollin_dish", "roof", "Satellite Dish", 3500, root =>
                Piece(root, "dish", new Vector3(0f, 1.0f, -0.46f), new Vector3(0f, 180f, 0f), Width("dish", 0.45f), Anchor.Bottom), null);
            yield return Part("rollin_flag", "roof", "Checkered Flag", 250, root =>
                Piece(root, "flag", new Vector3(-0.26f, 0.45f, -0.44f), Vector3.zero, Height("flag", 1.35f), Anchor.Bottom), null);

            // ---- lights --------------------------------------------------------------------------------------------
            yield return Part("rollin_lightbar", "lights", "Police Lightbar", 8000, Lightbar, null);

            // ---- audio ---------------------------------------------------------------------------------------------
            yield return Part("rollin_subwoofer", "audio", "Subwoofer", 1100, root =>
                Pulsing(Piece(root, "speaker", new Vector3(0f, 0.12f, 0.44f), new Vector3(0f, 0f, 0f), Height("speaker", 0.3f), Anchor.Bottom)),
                new TuningStats { Mass = 1.1f });
            yield return Part("rollin_boombox", "audio", "Boombox", 700, root =>
                Pulsing(Piece(root, "radio", new Vector3(0f, 0.56f, 0.2f), new Vector3(0f, 180f, 0f), Width("radio", 0.3f), Anchor.Bottom)), null);

            // ---- cargo ---------------------------------------------------------------------------------------------
            yield return Part("rollin_basket", "cargo", "Shopping Basket", 30, root =>
                Piece(root, "basket", new Vector3(0f, 0.1f, -0.05f), Vector3.zero, Width("basket", 0.34f), Anchor.Bottom), null);
            yield return Part("rollin_soda", "cargo", "Cup Holder Big Gulp", 4, root =>
                Piece(root, "soda", new Vector3(-0.27f, 0.72f, 0.14f), Vector3.zero, Height("soda", 0.18f), Anchor.Bottom), null);

            // ---- decal ---------------------------------------------------------------------------------------------
            yield return Part("rollin_flames", "decal", "Flame Job", 650, root =>
                DoubleSided(Piece(root, "flame", new Vector3(0f, 0.55f, -0.36f), new Vector3(0f, 180f, 0f), Height("flame", 0.42f), Anchor.Bottom), FlameOrange), null);
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

        private static TuningPart Rims(string id, string label, int price, string model)
        {
            // Not Chassis' own "wheels" slot: that one copies the rim onto the car's stock rim renderers, which the chair
            // lacks. Rollin's rims replace the two big wheel meshes and turn with their pivots (Effects).
            var part = Part(id, "rims", label, price, root =>
            {
                var s = Size(model);
                float scale = 0.7f / Mathf.Max(s.y, s.z);
                var stretch = new Vector3(0.11f / s.x, scale, scale);
                Spin(root, SpinRight, model, WheelchairType.BigWheel, Vector3.zero, stretch);
                Spin(root, SpinLeft, model, new Vector3(-WheelchairType.BigWheel.x, WheelchairType.BigWheel.y, WheelchairType.BigWheel.z), new Vector3(0f, 180f, 0f), stretch);
            }, model == "wheel_truck" ? new TuningStats { RideHeight = 0.03f, Grip = 1.15f } : null);
            part.Fits[WheelchairType.Code].Hide.AddRange(StockRims);
            return part;
        }

        /// <summary>One rim: a holder Effects moves onto the wheel pivot, with the model turned inside it.</summary>
        private static void Spin(Transform root, string name, string model, Vector3 at, Vector3 euler, Vector3 scale)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            holder.localPosition = at;
            Piece(holder, model, Vector3.zero, euler, scale, Anchor.Centre);
        }

        private static void HatPiece(Transform root, string model, float size, float sink, bool height = false)
        {
            // Effects puts the holder on the rider's head; with nobody seated the hat waits on the cushion.
            var holder = new GameObject(Hat).transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(0f, 0.5f, -0.08f);
            var s = Size(model);
            Piece(holder, model, new Vector3(0f, sink, 0f), Vector3.zero, Uniform(size / (height ? s.y : Mathf.Max(s.x, s.z))), Anchor.Bottom);
        }

        /// <summary>A Kenney space-kit rocket: base, fuel tank and nose stacked, laid flat with the nose forward.</summary>
        private static void Rocket(Transform root, Vector3 nose, float length)
        {
            string[] stack = { "rocket_base", "rocket_fuel", "rocket_top" };
            float total = 0f;
            foreach (var m in stack) total += Size(m).y;
            if (total <= 0f) return;
            float scale = length / total;

            // The holder sits at the tail; its +Y is the rocket's axis and points forward once tipped by 90 degrees.
            var holder = new GameObject("rollin_rocket").transform;
            holder.SetParent(root, false);
            holder.localPosition = nose - new Vector3(0f, 0f, length);
            holder.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float y = 0f;
            foreach (var m in stack)
            {
                Piece(holder, m, new Vector3(0f, y, 0f), Vector3.zero, Uniform(scale), Anchor.Bottom);
                y += Size(m).y * scale;
            }

            // A flame cone out of the tail (holder -Y), a light and the thruster loop, all driven by Effects.
            var flame = new GameObject(Flame).transform;
            flame.SetParent(holder, false);
            flame.localPosition = Vector3.zero;
            flame.localRotation = Quaternion.Euler(180f, 0f, 0f);
            float r = Size("rocket_base").x * scale * 0.32f;
            AddCone(flame, "core", r * 0.55f, length * 0.55f, new Color(1f, 0.95f, 0.6f));
            AddCone(flame, "outer", r, length * 0.9f, new Color(1f, 0.42f, 0.05f));
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(flame, false);
            light.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 2f;
            light.intensity = 0f;
            var thruster = new GameObject(Thruster);
            thruster.transform.SetParent(holder, false);
            Sounds.AddLoop(thruster, "thruster");
        }

        private static void Lightbar(Transform root)
        {
            var bar = Box(root, "bar", new Vector3(0f, 1.06f, -0.44f), new Vector3(0.56f, 0.05f, 0.09f), new Color(0.08f, 0.08f, 0.09f), false);
            Box(root, "post_L", new Vector3(0.2f, 1.0f, -0.44f), new Vector3(0.025f, 0.08f, 0.025f), new Color(0.15f, 0.15f, 0.15f), false);
            Box(root, "post_R", new Vector3(-0.2f, 1.0f, -0.44f), new Vector3(0.025f, 0.08f, 0.025f), new Color(0.15f, 0.15f, 0.15f), false);
            Blinker(root, BlinkRed, new Vector3(0.15f, 1.1f, -0.44f), new Color(1f, 0.05f, 0.05f));
            Blinker(root, BlinkBlue, new Vector3(-0.15f, 1.1f, -0.44f), new Color(0.1f, 0.25f, 1f));
            if (bar == null) Core.Log.Warning("[Models] the lightbar body did not build.");
        }

        private static void Blinker(Transform root, string name, Vector3 at, Color c)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            holder.localPosition = at;
            Box(holder, "lens", Vector3.zero, new Vector3(0.2f, 0.05f, 0.08f), c, true);
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(holder, false);
            light.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            light.type = LightType.Point;
            light.color = c;
            light.range = 4f;
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

        /// <summary>The flame decal's atlas colour is pink; the Flame Job is painted orange instead.</summary>
        private static readonly Color FlameOrange = new Color(1f, 0.36f, 0.04f);

        /// <summary>Copies the piece's materials with back faces drawn, optionally with the texture replaced by a colour.</summary>
        private static Transform DoubleSided(Transform piece, Color? tint = null)
        {
            if (piece == null) return null;
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                var m = new Material(r.sharedMaterial) { name = r.sharedMaterial.name };
                m.SetFloat("_Cull", 0f);
                if (tint.HasValue)
                {
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint.Value);
                    m.color = tint.Value;
                }
                r.sharedMaterial = m;
            }
            return piece;
        }

        private static string PaintSecond(string material) => "paint2";

        // ---- placement ---------------------------------------------------------------------------------------------

        internal static Vector3 Size(string model)
        {
            var m = Glb.Get(model);
            return m != null ? m.Bounds.size : Vector3.one;
        }

        private static Vector3 Uniform(float s) => new Vector3(s, s, s);
        private static Vector3 Width(string model, float width) => Uniform(width / Mathf.Max(0.0001f, Size(model).x));
        private static Vector3 Height(string model, float height) => Uniform(height / Mathf.Max(0.0001f, Size(model).y));

        /// <summary>
        /// One model under <paramref name="parent"/>: a holder at <paramref name="at"/> turned by <paramref name="euler"/>,
        /// with the model scaled inside so the anchor point of its bounds sits on the holder.
        /// </summary>
        internal static Transform Piece(Transform parent, string model, Vector3 at, Vector3 euler, Vector3 scale, Anchor anchor, Func<string, string> rename = null)
        {
            var holder = new GameObject(model).transform;
            holder.SetParent(parent, false);
            holder.localPosition = at;
            holder.localRotation = Quaternion.Euler(euler);
            var inner = Glb.Spawn(model, rename).transform;
            inner.SetParent(holder, false);
            var m = Glb.Get(model);
            if (m == null) return holder;
            var b = m.Bounds;
            Vector3 point = anchor == Anchor.Bottom ? new Vector3(b.center.x, b.min.y, b.center.z)
                : anchor == Anchor.Back ? new Vector3(b.center.x, b.center.y, b.min.z)
                : b.center;
            inner.localScale = scale;
            inner.localPosition = -Vector3.Scale(point, scale);
            return holder;
        }

        // ---- simple shapes -----------------------------------------------------------------------------------------

        private static Transform Box(Transform parent, string name, Vector3 at, Vector3 size, Color c, bool glow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // A collider under the vehicle would join its rigidbody; the parts are looks only.
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = glow ? Glow(c) : Flat(c);
            return go.transform;
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

        private static Material Flat(Color c)
        {
            string key = "flat" + ColorUtility.ToHtmlStringRGB(c);
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Glb.LitShader()) { name = "rollin_" + key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.4f);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _mats[key] = m;
        }

        /// <summary>Unlit and double sided: flames and lenses read as light, not as lit plastic.</summary>
        internal static Material Glow(Color c)
        {
            string key = "glow" + ColorUtility.ToHtmlStringRGB(c);
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { Core.Log.Warning("[Models] URP/Unlit shader not found - glowing parts use the lit shader."); shader = Glb.LitShader(); }
            m = new Material(shader) { name = "rollin_" + key };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _mats[key] = m;
        }
    }
}
