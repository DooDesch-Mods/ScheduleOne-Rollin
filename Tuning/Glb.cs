using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Rollin.Tuning
{
    /// <summary>
    /// Reads the embedded part models (<c>Assets/parts/*.glb</c>) into Unity meshes and materials, once per model. Based
    /// on Yesla's GlbModel: float positions, normals and the first UV set, PBR base colour, metallic and roughness, and
    /// base colour textures stored as PNG inside the file. Unlike Yesla's single model, the CC0 kits put their meshes
    /// under nodes with their own translation, rotation and scale, so every node's transform is baked into its vertices.
    ///
    /// glTF is right-handed and Unity left-handed. Negating X keeps the model facing +Z, the vehicle's forward.
    /// </summary>
    internal static class Glb
    {
        internal sealed class Piece
        {
            internal Mesh Mesh;
            internal Material Material;
            internal string MaterialName;
        }

        internal sealed class Model
        {
            internal string Name;
            internal readonly List<Piece> Pieces = new List<Piece>();
            /// <summary>Bounds of every vertex, in Unity model space.</summary>
            internal Bounds Bounds;
        }

        private static readonly Dictionary<string, Model> _models = new Dictionary<string, Model>(StringComparer.Ordinal);
        private static readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material> _renamed = new Dictionary<string, Material>(StringComparer.Ordinal);

        /// <summary>The model read from <c>Rollin.Assets.parts.&lt;name&gt;.glb</c>, or null (logged once).</summary>
        internal static Model Get(string name)
        {
            if (_models.TryGetValue(name, out var m)) return m;
            if (_failed.Contains(name)) return null;
            string resource = "Rollin.Assets.parts." + name + ".glb";
            try
            {
                byte[] bytes;
                using (Stream s = typeof(Glb).Assembly.GetManifestResourceStream(resource))
                {
                    if (s == null) { Core.Log.Error("[Glb] embedded model '" + resource + "' is missing - parts using it stay invisible."); _failed.Add(name); return null; }
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
                }
                m = Parse(name, bytes);
            }
            catch (Exception e) { Core.Log.Error("[Glb] reading '" + name + "' failed - parts using it stay invisible: " + e); m = null; }
            if (m == null) { _failed.Add(name); return null; }
            _models[name] = m;
            return m;
        }

        /// <summary>
        /// A new object with one renderer per piece of the model, at the model's own origin. <paramref name="rename"/>
        /// gives a material a new name ("paint", "paint2"), which tells Chassis to colour it with the chair's paint.
        /// </summary>
        internal static GameObject Spawn(string name, Func<string, string> rename = null)
        {
            var model = Get(name);
            var go = new GameObject("rollin_" + name);
            if (model == null) return go;
            foreach (var p in model.Pieces)
            {
                var child = new GameObject(p.Mesh.name);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                var r = child.AddComponent<MeshRenderer>();
                string to = rename?.Invoke(p.MaterialName);
                r.sharedMaterial = string.IsNullOrEmpty(to) ? p.Material : Renamed(name, p, to);
            }
            return go;
        }

        private static Material Renamed(string model, Piece p, string to)
        {
            string key = model + "|" + p.MaterialName + "|" + to;
            if (_renamed.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(p.Material) { name = to + "_" + model + "_" + p.MaterialName };
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _renamed[key] = m;
            return m;
        }

        // ---- parsing -----------------------------------------------------------------------------------------------

        private static Model Parse(string name, byte[] glb)
        {
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) { Core.Log.Error("[Glb] '" + name + "' is not a GLB file."); return null; }
            JObject json = null;
            byte[] bin = null;
            int offset = 12;
            while (offset + 8 <= glb.Length)
            {
                int length = (int)BitConverter.ToUInt32(glb, offset);
                uint type = BitConverter.ToUInt32(glb, offset + 4);
                int start = offset + 8;
                if (start + length > glb.Length) break;
                if (type == 0x4E4F534A) json = JObject.Parse(Encoding.UTF8.GetString(glb, start, length));
                else if (type == 0x004E4942) { bin = new byte[length]; Buffer.BlockCopy(glb, start, bin, 0, length); }
                offset = start + length;
                if ((offset & 3) != 0) offset += 4 - (offset & 3);
            }
            if (json == null || bin == null) { Core.Log.Error("[Glb] '" + name + "' has no JSON or BIN chunk."); return null; }

            var model = new Model { Name = name };
            var accessors = (JArray)json["accessors"];
            var views = (JArray)json["bufferViews"];
            var meshes = (JArray)json["meshes"];
            var nodes = (JArray)json["nodes"];
            var materialDefs = json["materials"] as JArray ?? new JArray();
            List<Texture2D> textures = ReadTextures(name, json, views, bin);

            var materials = new List<Material>();
            var names = new List<string>();
            for (int i = 0; i < materialDefs.Count; i++)
            {
                var md = (JObject)materialDefs[i];
                string mn = (string)md["name"] ?? ("material" + i);
                materials.Add(BuildMaterial(name, md, mn, textures));
                names.Add(mn);
            }

            bool any = false;
            var bounds = new Bounds();
            int triangles = 0;

            void Walk(int index, Matrix4x4 parent)
            {
                var node = (JObject)nodes[index];
                Matrix4x4 world = parent * Local(node);
                if (node["mesh"] != null)
                {
                    var prims = (JArray)meshes[(int)node["mesh"]]["primitives"];
                    for (int p = 0; p < prims.Count; p++)
                    {
                        var prim = (JObject)prims[p];
                        var attr = (JObject)prim["attributes"];
                        Vector3[] pos = ReadVec3(accessors, views, bin, (int)attr["POSITION"]);
                        Vector3[] nrm = attr["NORMAL"] != null ? ReadVec3(accessors, views, bin, (int)attr["NORMAL"]) : null;
                        Vector2[] uv = attr["TEXCOORD_0"] != null ? ReadVec2(accessors, views, bin, (int)attr["TEXCOORD_0"]) : null;
                        int[] idx = prim["indices"] != null ? ReadIndices(accessors, views, bin, (int)prim["indices"]) : Sequence(pos.Length);
                        // The node transform in glTF space, then the X mirror into Unity space.
                        Matrix4x4 normalM = world.inverse.transpose;
                        for (int i = 0; i < pos.Length; i++)
                        {
                            Vector3 w = world.MultiplyPoint3x4(pos[i]);
                            pos[i] = new Vector3(-w.x, w.y, w.z);
                            if (!any) { bounds = new Bounds(pos[i], Vector3.zero); any = true; } else bounds.Encapsulate(pos[i]);
                        }
                        if (nrm != null)
                            for (int i = 0; i < nrm.Length; i++)
                            {
                                Vector3 n = normalM.MultiplyVector(nrm[i]).normalized;
                                nrm[i] = new Vector3(-n.x, n.y, n.z);
                            }
                        // A mirror (the X flip, or a negative node scale) reverses the winding; swap two corners back.
                        bool flip = world.determinant > 0f;
                        if (flip) for (int i = 0; i + 2 < idx.Length; i += 3) { int t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }

                        int mi = prim["material"] != null ? (int)prim["material"] : -1;
                        var mesh = new Mesh { name = name + "_" + ((string)node["name"] ?? index.ToString()) + "_" + p };
                        if (pos.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.SetVertices(ToList(pos));
                        if (nrm != null && nrm.Length == pos.Length) mesh.SetNormals(ToList(nrm)); else mesh.RecalculateNormals();
                        if (uv != null && uv.Length == pos.Length) mesh.SetUVs(0, ToList(uv));
                        mesh.SetTriangles(ToList(idx), 0);
                        mesh.RecalculateBounds();
                        mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        triangles += idx.Length / 3;
                        model.Pieces.Add(new Piece
                        {
                            Mesh = mesh,
                            Material = mi >= 0 ? materials[mi] : Fallback(),
                            MaterialName = mi >= 0 ? names[mi] : "none",
                        });
                    }
                }
                if (node["children"] is JArray children)
                    foreach (var c in children) Walk((int)c, world);
            }

            var scenes = json["scenes"] as JArray;
            int sceneIndex = json["scene"] != null ? (int)json["scene"] : 0;
            if (scenes != null && scenes.Count > sceneIndex)
                foreach (var root in (JArray)scenes[sceneIndex]["nodes"]) Walk((int)root, Matrix4x4.identity);
            else
                for (int i = 0; i < nodes.Count; i++) Walk(i, Matrix4x4.identity);

            model.Bounds = bounds;
            Core.Log.Msg("[Glb] '" + name + "' read: " + model.Pieces.Count + " piece(s), " + materials.Count + " material(s), " + triangles + " triangles, size " + bounds.size.ToString("F3") + ".");
            return model;
        }

        private static Matrix4x4 Local(JObject node)
        {
            if (node["matrix"] is JArray m && m.Count == 16)
            {
                // glTF stores the matrix column by column.
                Vector4 C(int c) => new Vector4((float)m[c * 4], (float)m[c * 4 + 1], (float)m[c * 4 + 2], (float)m[c * 4 + 3]);
                return new Matrix4x4(C(0), C(1), C(2), C(3));
            }
            var t = node["translation"] as JArray;
            var q = node["rotation"] as JArray;
            var s = node["scale"] as JArray;
            Vector3 tv = t != null ? new Vector3((float)t[0], (float)t[1], (float)t[2]) : Vector3.zero;
            Quaternion qv = q != null ? new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]) : Quaternion.identity;
            Vector3 sv = s != null ? new Vector3((float)s[0], (float)s[1], (float)s[2]) : Vector3.one;
            return Matrix4x4.TRS(tv, qv, sv);
        }

        private static Material _fallback;

        private static Material Fallback()
        {
            if (_fallback != null) return _fallback;
            _fallback = new Material(LitShader()) { name = "rollin_default" };
            _fallback.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return _fallback;
        }

        internal static Shader LitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Core.Log.Warning("[Glb] URP/Lit shader not found - the parts use the default shader."); shader = Shader.Find("Standard"); }
            return shader;
        }

        private static Material BuildMaterial(string model, JObject m, string name, List<Texture2D> textures)
        {
            var mat = new Material(LitShader()) { name = name };
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            var pbr = m["pbrMetallicRoughness"] as JObject;
            var f = pbr?["baseColorFactor"] as JArray;
            Color c = f != null && f.Count >= 3 ? new Color((float)f[0], (float)f[1], (float)f[2], f.Count > 3 ? (float)f[3] : 1f) : Color.white;
            // The kits mark flat plastic as fully metallic; at metallic 1 URP draws it nearly black without reflections.
            float metallic = pbr?["metallicFactor"] != null ? Mathf.Min(0.3f, (float)pbr["metallicFactor"]) : 0f;
            float roughness = pbr?["roughnessFactor"] != null ? (float)pbr["roughnessFactor"] : 1f;
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness) * 0.6f);

            if (pbr?["baseColorTexture"] is JObject bt)
            {
                int ti = bt["index"] != null ? (int)bt["index"] : -1;
                Texture2D tex = ti >= 0 && ti < textures.Count ? textures[ti] : null;
                if (tex == null) Core.Log.Error("[Glb] '" + model + "' material '" + name + "' points at texture " + ti + ", which did not load - it stays untextured.");
                else
                {
                    mat.SetTexture("_BaseMap", tex);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                }
            }
            if (m["doubleSided"] != null && (bool)m["doubleSided"]) mat.SetFloat("_Cull", 0f);
            return mat;
        }

        private static List<Texture2D> ReadTextures(string model, JObject json, JArray views, byte[] bin)
        {
            var result = new List<Texture2D>();
            var texDefs = json["textures"] as JArray;
            if (texDefs == null) return result;
            var images = json["images"] as JArray;
            var samplers = json["samplers"] as JArray;
            for (int i = 0; i < texDefs.Count; i++)
            {
                try
                {
                    var def = (JObject)texDefs[i];
                    int source = def["source"] != null ? (int)def["source"] : -1;
                    var image = images != null && source >= 0 && source < images.Count ? (JObject)images[source] : null;
                    if (image == null || image["bufferView"] == null) { Core.Log.Error("[Glb] '" + model + "' texture " + i + " has no embedded image."); result.Add(null); continue; }
                    var view = (JObject)views[(int)image["bufferView"]];
                    int offset = view["byteOffset"] != null ? (int)view["byteOffset"] : 0;
                    var bytes = new byte[(int)view["byteLength"]];
                    Buffer.BlockCopy(bin, offset, bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (!ImageConversion.LoadImage(tex, bytes, false)) { Core.Log.Error("[Glb] '" + model + "' texture " + i + " is not a PNG Unity can decode."); result.Add(null); continue; }
                    tex.name = "rollin_" + model + "_" + ((string)image["name"] ?? ("texture" + i));
                    tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    var sampler = samplers != null && def["sampler"] != null ? (JObject)samplers[(int)def["sampler"]] : null;
                    int mag = sampler?["magFilter"] != null ? (int)sampler["magFilter"] : 9729;
                    // The Kenney palettes are a grid of flat swatches; point filtering keeps the swatch edges clean.
                    tex.filterMode = mag == 9728 || ((string)image["name"]) == "colormap" ? FilterMode.Point : FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Repeat;
                    result.Add(tex);
                }
                catch (Exception e) { Core.Log.Error("[Glb] '" + model + "' texture " + i + " failed: " + e.Message); result.Add(null); }
            }
            return result;
        }

        private static int[] Sequence(int n)
        {
            var r = new int[n];
            for (int i = 0; i < n; i++) r[i] = i;
            return r;
        }

        private static Vector2[] ReadVec2(JArray accessors, JArray views, byte[] bin, int index)
        {
            var a = (JObject)accessors[index];
            if ((int)a["componentType"] != 5126 || (string)a["type"] != "VEC2") return null;
            int count = (int)a["count"];
            Resolve(a, views, out int start, out int stride, 8);
            var r = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int o = start + i * stride;
                r[i] = new Vector2(BitConverter.ToSingle(bin, o), 1f - BitConverter.ToSingle(bin, o + 4));
            }
            return r;
        }

        private static Vector3[] ReadVec3(JArray accessors, JArray views, byte[] bin, int index)
        {
            var a = (JObject)accessors[index];
            int count = (int)a["count"];
            Resolve(a, views, out int start, out int stride, 12);
            var r = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int o = start + i * stride;
                r[i] = new Vector3(BitConverter.ToSingle(bin, o), BitConverter.ToSingle(bin, o + 4), BitConverter.ToSingle(bin, o + 8));
            }
            return r;
        }

        private static int[] ReadIndices(JArray accessors, JArray views, byte[] bin, int index)
        {
            var a = (JObject)accessors[index];
            int type = (int)a["componentType"];
            int size = type == 5125 ? 4 : type == 5123 ? 2 : type == 5121 ? 1 : 0;
            if (size == 0) throw new InvalidDataException("index component type " + type);
            int count = (int)a["count"];
            Resolve(a, views, out int start, out int stride, size);
            var r = new int[count];
            for (int i = 0; i < count; i++)
            {
                int o = start + i * stride;
                r[i] = size == 4 ? (int)BitConverter.ToUInt32(bin, o) : size == 2 ? BitConverter.ToUInt16(bin, o) : bin[o];
            }
            return r;
        }

        private static void Resolve(JObject accessor, JArray views, out int start, out int stride, int elementSize)
        {
            var view = (JObject)views[(int)accessor["bufferView"]];
            start = (view["byteOffset"] != null ? (int)view["byteOffset"] : 0) + (accessor["byteOffset"] != null ? (int)accessor["byteOffset"] : 0);
            stride = view["byteStride"] != null ? (int)view["byteStride"] : elementSize;
        }

        private static Il2CppSystem.Collections.Generic.List<Vector3> ToList(Vector3[] v)
        {
            var l = new Il2CppSystem.Collections.Generic.List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++) l.Add(v[i]);
            return l;
        }

        private static Il2CppSystem.Collections.Generic.List<Vector2> ToList(Vector2[] v)
        {
            var l = new Il2CppSystem.Collections.Generic.List<Vector2>(v.Length);
            for (int i = 0; i < v.Length; i++) l.Add(v[i]);
            return l;
        }

        private static Il2CppSystem.Collections.Generic.List<int> ToList(int[] v)
        {
            var l = new Il2CppSystem.Collections.Generic.List<int>(v.Length);
            for (int i = 0; i < v.Length; i++) l.Add(v[i]);
            return l;
        }
    }
}
