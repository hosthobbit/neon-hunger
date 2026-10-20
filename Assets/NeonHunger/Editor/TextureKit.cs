using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeonHunger.EditorTools
{
    /// <summary>Procedural textures (brick, slate, tarmac, paving, grass, newsprint…), saved as PNG assets.</summary>
    public static class TextureKit
    {
        public const string Dir = "Assets/NeonHunger/Generated/Textures/";

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        /// <summary>Smooth value noise, tileable with period `period`.</summary>
        public static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float u, float v, int baseFreq, int seed, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int f = baseFreq;
            for (int i = 0; i < octaves; i++) { sum += Noise(u * f, v * f, f, seed + i) * amp; norm += amp; amp *= 0.5f; f *= 2; }
            return sum / norm;
        }

        public static Texture2D Save(string name, int size, Func<float, float, Color> f, bool alpha = false, int height = 0)
        {
            Directory.CreateDirectory(Dir);
            int h = height > 0 ? height : size;
            var t = new Texture2D(size, h, TextureFormat.RGBA32, false);
            var px = new Color[size * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / h);
            t.SetPixels(px);
            t.Apply();
            string path = Dir + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.alphaIsTransparency = alpha;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A soft round blob (particles, glows).</summary>
        public static Texture2D Soft(string name) => Save(name, 64, (u, v) =>
        {
            float d = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3 - 2 * a);
            return new Color(1, 1, 1, a);
        }, true);

        /// <summary>Vertical beam: bright core, fading up and at the sides.</summary>
        public static Texture2D Beam(string name) => Save(name, 64, (u, v) =>
        {
            float side = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f);
            side = side * side * (3 - 2 * side);
            return new Color(1, 1, 1, side * Mathf.Lerp(1f, 0f, v));
        }, true, 256);

        /// <summary>One worn metal floor plate per tile: bevelled seams, rivets, scuffs and grime.</summary>
        public static Texture2D FloorPlate(string name) => Save(name, 256, (u, v) =>
        {
            float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
            float seam = Mathf.Clamp01(edge / 0.03f);
            float bevel = Mathf.Clamp01((edge - 0.03f) / 0.04f);
            float n = Fbm(u, v, 4, 61) * 0.5f + Fbm(u, v, 16, 62) * 0.25f;
            float scratches = Mathf.Pow(Mathf.Abs(Mathf.Sin((u * 0.8f + v * 0.25f) * 60f + Fbm(u, v, 8, 63) * 9f)), 40f) * 0.25f;
            float b = 0.35f + n * 0.4f + scratches;
            // rivets in the corners
            foreach (var c in new[] { new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f), new Vector2(0.9f, 0.9f) })
            {
                float d = (new Vector2(u, v) - c).magnitude;
                if (d < 0.025f) b = 0.7f - d * 8f;
            }
            b *= Mathf.Lerp(0.25f, 1f, seam) * Mathf.Lerp(0.8f, 1f, bevel);
            float grime = Mathf.Clamp01(Fbm(u, v, 3, 64) * 1.6f - 0.5f);
            b *= 1f - grime * 0.45f;
            return new Color(b, b * 1.01f, b * 1.06f);
        });

        /// <summary>Emission mask for the floor: only the seams glow, faintly.</summary>
        public static Texture2D FloorSeams(string name) => Save(name, 128, (u, v) =>
        {
            float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
            float s = Mathf.Clamp01(1f - edge / 0.018f);
            return new Color(s, s, s);
        });

        /// <summary>Scratched, riveted dark wall panels.</summary>
        public static Texture2D WallPanels(string name) => Save(name, 256, (u, v) =>
        {
            float gu = Mathf.Abs(u * 2f - Mathf.Round(u * 2f)), gv = Mathf.Abs(v * 2f - Mathf.Round(v * 2f));
            float seam = Mathf.Clamp01(Mathf.Min(gu, gv) / 0.025f);
            float n = Fbm(u, v, 6, 71) * 0.5f + Fbm(u, v, 24, 72) * 0.2f;
            float streak = Mathf.Clamp01(Fbm(u * 0.3f, v * 3f, 8, 73) * 1.4f - 0.55f);
            float b = (0.4f + n * 0.5f) * Mathf.Lerp(0.3f, 1f, seam) * (1f - streak * 0.5f);
            return new Color(b, b, b * 1.05f);
        });

        /// <summary>Hazard stripes for the living walls and risk doors.</summary>
        public static Texture2D Hazard(string name) => Save(name, 128, (u, v) =>
        {
            float stripe = Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? 1f : 0f;
            float n = 0.75f + Fbm(u, v, 8, 81) * 0.25f;
            var a = new Color(1f, 0.62f, 0.12f) * n; var b = new Color(0.08f, 0.07f, 0.06f);
            return Color.Lerp(b, a, stripe * Mathf.Clamp01(Fbm(u, v, 6, 82) * 1.5f));
        });

        /// <summary>Warning plate: diagonal stripes with a frame (tinted per use).</summary>
        public static Texture2D Stripes(string name) => Save(name, 128, (u, v) =>
        {
            float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
            float frame = edge < 0.06f ? 1f : 0f;
            float stripe = Mathf.Repeat((u + v) * 3f, 1f) < 0.45f ? 0.75f : 0.1f;
            return new Color(1, 1, 1, Mathf.Max(frame, stripe));
        }, true);
    }
}
