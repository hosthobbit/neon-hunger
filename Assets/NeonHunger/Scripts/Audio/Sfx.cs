using System;
using System.Collections.Generic;
using NeonHunger.Core;
using UnityEngine;

namespace NeonHunger
{
    /// <summary>
    /// Every sound, synthesised at start-up (original, no audio files): a tense ambient drone, a
    /// driving power-surge track, a distinct hum for each hunter (played in 3D so you can hear where
    /// they are), and the effects. Browsers start audio only after the first click or key press.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public enum Kind { Node, Power, Gem, RiskGem, Dash, Caught, Death, Respawn, ShiftAlarm, ShiftMove, RiskAlarm, DoorSlam, ExitOpen, LevelClear, GameOver, Victory, Click, SurgeEnding, Heard, Ready }

        public static Sfx Instance { get; private set; }
        const int Rate = 32000;

        [Range(0, 1)] public float SoundVolume = 0.85f;
        [Range(0, 1)] public float MusicVolume = 0.6f;
        public bool Muted;

        readonly Dictionary<Kind, AudioClip> clips = new Dictionary<Kind, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource ambient, surge;
        readonly AudioClip[] hums = new AudioClip[4];
        float surgeMix, nodePitch = 1f, lastNode;
        public bool SurgeMusic;
        readonly System.Random rng = new System.Random(5);

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < 12; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                pool.Add(s);
            }
            clips[Kind.Node] = Make(0.07f, d => Blip(d, 1320f, 0.22f, 55f));
            clips[Kind.Power] = Make(0.9f, PowerUp);
            clips[Kind.Gem] = Make(0.6f, d => Arp(d, new[] { 988f, 1318f, 1760f }, 0.06f, 0.18f, 6f));
            clips[Kind.RiskGem] = Make(0.9f, d => Arp(d, new[] { 784f, 1046f, 1318f, 2093f }, 0.07f, 0.2f, 4f));
            clips[Kind.Dash] = Make(0.35f, Whoosh);
            clips[Kind.Caught] = Make(0.45f, Gulp);
            clips[Kind.Death] = Make(1.5f, Death);
            clips[Kind.Respawn] = Make(0.6f, d => Sweep(d, 200f, 900f, 0.2f));
            clips[Kind.ShiftAlarm] = Make(1.0f, Alarm);
            clips[Kind.ShiftMove] = Make(1.1f, Rumble);
            clips[Kind.RiskAlarm] = Make(0.5f, d => { Blip(d, 880f, 0.18f, 10f); });
            clips[Kind.DoorSlam] = Make(0.9f, Slam);
            clips[Kind.ExitOpen] = Make(1.4f, d => Arp(d, new[] { 392f, 523f, 659f, 784f, 1046f }, 0.12f, 0.16f, 2.5f));
            clips[Kind.LevelClear] = Make(2.2f, d => Arp(d, new[] { 523f, 659f, 784f, 1046f, 784f, 1046f, 1318f }, 0.14f, 0.15f, 2f));
            clips[Kind.GameOver] = Make(2.4f, d => Arp(d, new[] { 392f, 330f, 262f, 196f }, 0.35f, 0.18f, 1.5f));
            clips[Kind.Victory] = Make(3.2f, d => Arp(d, new[] { 523f, 659f, 784f, 1046f, 1318f, 1568f, 2093f }, 0.16f, 0.14f, 1.6f));
            clips[Kind.Click] = Make(0.05f, d => Blip(d, 1600f, 0.15f, 90f));
            clips[Kind.SurgeEnding] = Make(0.25f, d => Blip(d, 660f, 0.2f, 18f));
            clips[Kind.Heard] = Make(0.6f, d => Sweep(d, 300f, 160f, 0.15f));
            clips[Kind.Ready] = Make(0.8f, d => Arp(d, new[] { 262f, 392f }, 0.2f, 0.16f, 3f));
            for (int k = 0; k < 4; k++) { int kk = k; hums[k] = Make(2f, d => Hum(d, kk)); }

            ambient = Loop(Make(16f, Ambient));
            surge = Loop(Make(4.8f, SurgeTrack));
            surge.volume = 0f;
        }

        AudioSource Loop(AudioClip c)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.playOnAwake = false;
            s.Play();
            return s;
        }

        void Update()
        {
            float master = Muted ? 0f : 1f;
            surgeMix = Mathf.MoveTowards(surgeMix, SurgeMusic ? 1f : 0f, Time.unscaledDeltaTime * 2.5f);
            if (ambient) ambient.volume = MusicVolume * master * (1f - surgeMix * 0.8f) * 0.9f;
            if (surge) surge.volume = MusicVolume * master * surgeMix * 0.75f;
            AudioListener.volume = Muted ? 0f : 1f;
            if (Time.unscaledTime - lastNode > 0.4f) nodePitch = 1f;
        }

        public AudioClip Hum(HunterKind k) => hums[(int)k];

        public static void Play(Kind k, float volume = 1f, float pitch = 1f)
        {
            var me = Instance;
            if (!me || me.Muted || !me.clips.TryGetValue(k, out var clip)) return;
            if (k == Kind.Node)
            {
                // consecutive nodes climb in pitch a little: satisfying runs
                pitch = me.nodePitch;
                me.nodePitch = Mathf.Min(1.5f, me.nodePitch + 0.02f);
                me.lastNode = Time.unscaledTime;
            }
            foreach (var s in me.pool)
            {
                if (s.isPlaying) continue;
                s.clip = clip;
                s.volume = volume * me.SoundVolume;
                s.pitch = pitch;
                s.Play();
                return;
            }
        }

        // ------------------------------------------------------------------ synthesis

        AudioClip Make(float seconds, Action<float[]> synth)
        {
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            synth(data);
            var c = AudioClip.Create("sfx", data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        float Noise() => (float)(rng.NextDouble() * 2 - 1);
        static float Env(float t, float a, float decay) => t < a ? t / a : Mathf.Exp(-(t - a) * decay);

        void Blip(float[] d, float f, float vol, float decay)
        {
            for (int i = 0; i < d.Length; i++) { float t = (float)i / Rate; d[i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * f * t)) * vol * Env(t, 0.002f, decay); }
        }

        void Sweep(float[] d, float f0, float f1, float vol)
        {
            float ph = 0, len = (float)d.Length / Rate;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate, k = t / len;
                ph += 2 * Mathf.PI * Mathf.Lerp(f0, f1, k) / Rate;
                d[i] += Mathf.Sin(ph) * vol * Mathf.Sin(k * Mathf.PI);
            }
        }

        void Arp(float[] d, float[] notes, float step, float vol, float decay)
        {
            for (int n = 0; n < notes.Length; n++)
            {
                int s0 = (int)(n * step * Rate);
                for (int i = s0; i < d.Length; i++)
                {
                    float t = (float)(i - s0) / Rate, f = notes[n];
                    float v = Mathf.Sin(2 * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2 * Mathf.PI * f * 2.003f * t) + 0.15f * Mathf.Sin(2 * Mathf.PI * f * 3f * t);
                    d[i] += v * vol * Env(t, 0.004f, decay);
                }
            }
        }

        void PowerUp(float[] d)
        {
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                ph += 2 * Mathf.PI * (110f + 900f * t * t) / Rate;
                d[i] = (Mathf.Sign(Mathf.Sin(ph)) * 0.12f + Mathf.Sin(ph * 2f) * 0.15f) * Env(t, 0.02f, 3f);
            }
        }

        void Whoosh(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate, k = t / 0.35f;
                lp += (Noise() - lp) * Mathf.Lerp(0.5f, 0.05f, k);
                d[i] = lp * Mathf.Sin(Mathf.Min(1f, k) * Mathf.PI) * 0.6f;
            }
        }

        void Gulp(float[] d)
        {
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                ph += 2 * Mathf.PI * (600f - 900f * t) / Rate;
                d[i] = (Mathf.Sin(ph) * 0.4f + Mathf.Sin(ph * 0.5f) * 0.2f) * Env(t, 0.005f, 7f);
            }
        }

        void Death(float[] d)
        {
            float ph = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                ph += 2 * Mathf.PI * Mathf.Max(40f, 520f * Mathf.Exp(-t * 2.2f)) / Rate;
                lp += (Noise() - lp) * 0.08f;
                d[i] = (Mathf.Sin(ph) * 0.35f + Mathf.Sign(Mathf.Sin(ph * 1.01f)) * 0.08f + lp * 0.4f * Env(t, 0.01f, 3f)) * Env(t, 0.01f, 1.6f);
            }
        }

        void Alarm(float[] d)
        {
            for (int b = 0; b < 3; b++)
            {
                int s0 = (int)(b * 0.3f * Rate);
                for (int i = s0; i < Mathf.Min(d.Length, s0 + (int)(0.2f * Rate)); i++)
                {
                    float t = (float)(i - s0) / Rate;
                    d[i] += (Mathf.Sin(2 * Mathf.PI * 520f * t) + 0.5f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 260f * t))) * 0.1f * Mathf.Min(1f, t * 200f) * Mathf.Min(1f, (0.2f - t) * 60f);
                }
            }
        }

        void Rumble(float[] d)
        {
            float lp = 0, ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                lp += (Noise() - lp) * 0.03f;
                ph += 2 * Mathf.PI * 48f / Rate;
                d[i] = (lp * 1.6f + Mathf.Sin(ph) * 0.3f) * Mathf.Sin(Mathf.Min(1f, t / 1.1f) * Mathf.PI) * 0.8f;
            }
        }

        void Slam(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                lp += (Noise() - lp) * 0.2f;
                d[i] = (lp * 0.8f + Mathf.Sin(2 * Mathf.PI * 70f * t) * 0.6f) * Env(t, 0.002f, 6f);
            }
        }

        /// <summary>Each hunter has its own voice: a throb, a chitter, a low siren, a breathy whine.</summary>
        void Hum(float[] d, int kind)
        {
            float ph = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float v;
                switch (kind)
                {
                    case 0: // Stalker: deep pulsing throb
                        ph += 2 * Mathf.PI * 62f / Rate;
                        v = (Mathf.Sin(ph) + 0.4f * Mathf.Sin(ph * 2.01f)) * (0.55f + 0.45f * Mathf.Sin(2 * Mathf.PI * 2f * t));
                        break;
                    case 1: // Ambusher: fast insect chitter
                        ph += 2 * Mathf.PI * 340f / Rate;
                        v = Mathf.Sign(Mathf.Sin(ph)) * 0.35f * Mathf.Max(0f, Mathf.Sin(2 * Mathf.PI * 9f * t));
                        break;
                    case 2: // Warden: slow two-tone siren
                        ph += 2 * Mathf.PI * (180f + 40f * Mathf.Sin(2 * Mathf.PI * 0.5f * t)) / Rate;
                        v = Mathf.Sin(ph) * 0.6f;
                        break;
                    default: // Listener: breathy whine
                        lp += (Noise() - lp) * 0.15f;
                        ph += 2 * Mathf.PI * 720f / Rate;
                        v = lp * 0.9f + Mathf.Sin(ph) * 0.12f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 1f * t));
                        break;
                }
                d[i] = v * 0.5f;
            }
        }

        void Ambient(float[] d)
        {
            // a dark drone: detuned low tones, a slow filtered noise wind, distant metallic knocks
            float lp = 0, p1 = 0, p2 = 0, p3 = 0;
            float len = (float)d.Length / Rate;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float swell = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * t / len * 2f);
                p1 += 2 * Mathf.PI * 55f / Rate; p2 += 2 * Mathf.PI * 55.4f / Rate; p3 += 2 * Mathf.PI * 82.4f / Rate;
                lp += (Noise() - lp) * (0.01f + 0.01f * Mathf.Sin(2 * Mathf.PI * t / len * 3f));
                d[i] = (Mathf.Sin(p1) + Mathf.Sin(p2)) * 0.18f * swell + Mathf.Sin(p3) * 0.07f * (1 - swell) + lp * 0.5f;
            }
            for (int k = 0; k < 9; k++)
            {
                int s0 = (int)((1f + k * 1.7f + (float)rng.NextDouble() * 0.6f) * Rate);
                float f = 180f + (float)rng.NextDouble() * 400f;
                for (int i = s0; i < Mathf.Min(d.Length, s0 + Rate / 2); i++)
                {
                    float t = (float)(i - s0) / Rate;
                    d[i] += (Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * 2.76f * t) * 0.3f) * 0.12f * Env(t, 0.002f, 9f);
                }
            }
            // a heartbeat every few seconds
            for (float b = 2f; b < len; b += 4f)
                foreach (float off in new[] { 0f, 0.28f })
                {
                    int s0 = (int)((b + off) * Rate);
                    for (int i = s0; i < Mathf.Min(d.Length, s0 + Rate / 4); i++)
                    {
                        float t = (float)(i - s0) / Rate;
                        d[i] += Mathf.Sin(2 * Mathf.PI * (60f - 30f * t) * t) * 0.35f * Env(t, 0.004f, 18f);
                    }
                }
            Normalise(d, 0.5f);
        }

        void SurgeTrack(float[] d)
        {
            // 150 bpm pounding loop: kick, pulsing saw bass, a rising arpeggio
            const float beat = 60f / 150f;
            int[] notes = { 0, 3, 7, 10, 12, 10, 7, 3 };
            float baseF = 110f;
            for (int b = 0; b < 12; b++)
            {
                float t0 = b * beat;
                Kick(d, t0, 0.5f);
                for (int s = 0; s < 2; s++)
                {
                    float ts = t0 + s * beat * 0.5f;
                    int n = notes[(b * 2 + s) % notes.Length];
                    Saw(d, ts, baseF / 2f, 0.16f, 0.18f);
                    Saw(d, ts, baseF * 2f * Mathf.Pow(2, n / 12f), 0.07f, 0.16f);
                }
            }
            Normalise(d, 0.6f);
        }

        void Kick(float[] d, float start, float vol)
        {
            int s0 = (int)(start * Rate); float ph = 0;
            for (int i = 0; i < (int)(0.25f * Rate) && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                ph += 2 * Mathf.PI * (45f + 120f * Mathf.Exp(-t * 35f)) / Rate;
                d[s0 + i] += Mathf.Sin(ph) * vol * Env(t, 0.002f, 12f);
            }
        }

        void Saw(float[] d, float start, float f, float vol, float len)
        {
            int s0 = (int)(start * Rate);
            for (int i = 0; i < (int)(len * Rate) && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float ph = (f * t) % 1f;
                d[s0 + i] += (ph * 2f - 1f) * vol * Env(t, 0.003f, 10f);
            }
        }

        static void Normalise(float[] d, float peakTo)
        {
            float peak = 0.001f;
            foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peakTo / peak;
        }
    }
}
