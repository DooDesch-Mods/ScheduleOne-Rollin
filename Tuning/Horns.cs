using System;
using System.Collections.Generic;
using Chassis.Api;

namespace Rollin.Tuning
{
    /// <summary>
    /// The horns, synthesised at load time so the mod carries no sound files: a clown bulb horn, a rubber duck, a train
    /// horn chord, the stadium "Charge!" fanfare and the opening of La Cucaracha (traditional, public domain). Each one
    /// is registered with Chassis and set through the "horn" tuning key.
    /// </summary>
    internal static class Horns
    {
        private const int Rate = 44100;

        internal sealed class Horn
        {
            internal string Name;
            internal string Label;
            internal Func<float[]> Make;
        }

        internal static readonly List<Horn> All = new List<Horn>
        {
            new Horn { Name = "rollin_clown", Label = "Clown Horn", Make = Clown },
            new Horn { Name = "rollin_duck", Label = "Rubber Duck", Make = Duck },
            new Horn { Name = "rollin_train", Label = "Freight Train", Make = Train },
            new Horn { Name = "rollin_charge", Label = "Charge!", Make = Charge },
            new Horn { Name = "rollin_cucaracha", Label = "La Cucaracha", Make = Cucaracha },
        };

        internal static void RegisterAll()
        {
            foreach (var h in All)
            {
                try
                {
                    float[] data = h.Make();
                    var clip = AudioClip.Create(h.Name, data.Length, 1, Rate, false);
                    clip.SetData(data, 0);
                    // Clips outlive scenes, like the chairs that play them.
                    clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    ChassisApi.RegisterSound(h.Name, clip);
                    Core.Log.Msg("[Horns] '" + h.Name + "' made, " + ((float)data.Length / Rate).ToString("F2") + " s.");
                }
                catch (Exception e) { Core.Log.Error("[Horns] making '" + h.Name + "' failed - that horn is missing: " + e); }
            }
        }

        // ---- synthesis ---------------------------------------------------------------------------------------------

        private static float[] Buffer(float seconds) => new float[(int)(seconds * Rate)];

        /// <summary>Adds one tone: a waveform from 0..1 phase, a pitch from start to end, an attack/release envelope.</summary>
        private static void Tone(float[] buf, float at, float length, float fromHz, float toHz, Func<float, float> wave, float gain)
        {
            int start = (int)(at * Rate), n = (int)(length * Rate);
            double phase = 0;
            for (int i = 0; i < n && start + i < buf.Length; i++)
            {
                float t = (float)i / n;
                float hz = Mathf.Lerp(fromHz, toHz, t);
                phase += hz / Rate;
                float env = Mathf.Min(1f, i / (0.01f * Rate)) * Mathf.Min(1f, (n - i) / (0.03f * Rate));
                buf[start + i] += wave((float)(phase - Math.Floor(phase))) * env * gain;
            }
        }

        private static float Saw(float p) => 2f * p - 1f;
        private static float Square(float p) => p < 0.5f ? 0.8f : -0.8f;
        private static float Sine(float p) => Mathf.Sin(p * 2f * Mathf.PI);

        private static float[] Clip(float[] buf)
        {
            for (int i = 0; i < buf.Length; i++) buf[i] = Mathf.Clamp(buf[i], -1f, 1f);
            return buf;
        }

        private static float[] Clown()
        {
            var b = Buffer(0.75f);
            Tone(b, 0f, 0.28f, 420f, 360f, Saw, 0.45f);
            Tone(b, 0f, 0.28f, 840f, 720f, Square, 0.12f);
            Tone(b, 0.38f, 0.32f, 400f, 330f, Saw, 0.45f);
            Tone(b, 0.38f, 0.32f, 800f, 660f, Square, 0.12f);
            return Clip(b);
        }

        private static float[] Duck()
        {
            var b = Buffer(0.5f);
            Tone(b, 0f, 0.18f, 900f, 1500f, Square, 0.35f);
            Tone(b, 0.22f, 0.22f, 1400f, 800f, Square, 0.35f);
            return Clip(b);
        }

        private static float[] Train()
        {
            var b = Buffer(1.4f);
            foreach (float hz in new[] { 277f, 349f, 415f, 523f })
            {
                Tone(b, 0f, 1.4f, hz, hz * 0.995f, Saw, 0.16f);
                Tone(b, 0f, 1.4f, hz * 1.004f, hz, Saw, 0.12f);
            }
            return Clip(b);
        }

        private static float[] Melody(float beat, (float hz, float beats)[] notes, Func<float, float> wave, float gain)
        {
            float total = 0f;
            foreach (var n in notes) total += n.beats * beat;
            var b = Buffer(total + 0.05f);
            float at = 0f;
            foreach (var n in notes)
            {
                if (n.hz > 0f) Tone(b, at, n.beats * beat * 0.9f, n.hz, n.hz, wave, gain);
                at += n.beats * beat;
            }
            return Clip(b);
        }

        // Equal temperament, A4 = 440 Hz.
        private const float G4 = 392.00f, C5 = 523.25f, D5 = 587.33f, E5 = 659.25f, F5 = 698.46f, G5 = 783.99f, A5 = 880.00f;

        private static float[] Charge() => Melody(0.13f, new[]
        {
            (G4, 1f), (C5, 1f), (E5, 1f), (G5, 2f), (E5, 1f), (G5, 4f),
        }, Square, 0.3f);

        private static float[] Cucaracha() => Melody(0.11f, new[]
        {
            (C5, 1f), (C5, 1f), (C5, 1f), (F5, 3f), (A5, 2f), (0f, 1f),
            (C5, 1f), (C5, 1f), (C5, 1f), (F5, 3f), (A5, 2f), (0f, 1f),
            (F5, 2f), (F5, 1f), (E5, 1f), (E5, 1f), (D5, 1f), (D5, 1f), (C5, 4f),
        }, Square, 0.28f);
    }
}
