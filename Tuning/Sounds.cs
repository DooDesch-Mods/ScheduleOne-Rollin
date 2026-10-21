using System;
using System.Collections.Generic;
using System.IO;
using Chassis.Api;

namespace Rollin.Tuning
{
    /// <summary>
    /// The CC0 sounds Rollin carries (<c>Assets/sounds/*.pcm</c>, 16-bit mono at 22050 Hz, see CREDITS.md): the rocket's
    /// thruster loop, the exhaust backfire and the bicycle horn, which joins the synthesised horns in Chassis.
    /// </summary>
    internal static class Sounds
    {
        private const int Rate = 22050;
        internal const string BicycleHorn = "rollin_bicycle";

        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private static readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);

        internal static void RegisterHorns()
        {
            var clip = Get("bicycle");
            if (clip == null) return;
            ChassisApi.RegisterSound(BicycleHorn, clip);
            Core.Log.Msg("[Sounds] '" + BicycleHorn + "' registered with Chassis, " + clip.length.ToString("F2") + " s.");
        }

        /// <summary>The clip read from <c>Rollin.Assets.sounds.&lt;name&gt;.pcm</c>, or null (logged once).</summary>
        internal static AudioClip Get(string name)
        {
            if (_clips.TryGetValue(name, out var c) && c != null) return c;
            if (_failed.Contains(name)) return null;
            string resource = "Rollin.Assets.sounds." + name + ".pcm";
            try
            {
                byte[] bytes;
                using (Stream s = typeof(Sounds).Assembly.GetManifestResourceStream(resource))
                {
                    if (s == null) { Core.Log.Error("[Sounds] embedded sound '" + resource + "' is missing - that sound stays silent."); _failed.Add(name); return null; }
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
                }
                int n = bytes.Length / 2;
                var data = new float[n];
                for (int i = 0; i < n; i++) data[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                c = AudioClip.Create("rollin_" + name, n, 1, Rate, false);
                c.SetData(data, 0);
                c.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                _clips[name] = c;
                return c;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Sounds] reading '" + name + "' failed - that sound stays silent: " + e);
                _failed.Add(name);
                return null;
            }
        }

        /// <summary>A looping 3D source on <paramref name="go"/>, silent until Effects turns it up.</summary>
        internal static AudioSource AddLoop(GameObject go, string name)
        {
            var clip = Get(name);
            if (clip == null) return null;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.minDistance = 1.5f;
            src.maxDistance = 30f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = 0f;
            return src;
        }

        /// <summary>A one-shot at a world point (the backfire pop).</summary>
        internal static void PlayAt(string name, Vector3 at, float volume, float pitch)
        {
            var clip = Get(name);
            if (clip == null) return;
            var go = new GameObject("rollin_oneshot_" + name);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;
            src.minDistance = 2f;
            src.maxDistance = 40f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = volume;
            src.pitch = pitch;
            src.Play();
            UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }
    }
}
