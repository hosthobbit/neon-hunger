using UnityEngine;

namespace NeonHunger
{
    /// <summary>Preferences and personal bests, kept in PlayerPrefs (browser local storage on the web).</summary>
    public static class Settings
    {
        /// <summary>Automated test runs use their own prefix so they never touch a player's data.</summary>
        public static string Prefix = "nh.";

        public static float Music { get => PlayerPrefs.GetFloat(Prefix + "music", 0.6f); set => PlayerPrefs.SetFloat(Prefix + "music", value); }
        public static float Sound { get => PlayerPrefs.GetFloat(Prefix + "sound", 0.85f); set => PlayerPrefs.SetFloat(Prefix + "sound", value); }
        public static bool Muted { get => PlayerPrefs.GetInt(Prefix + "muted", 0) == 1; set => PlayerPrefs.SetInt(Prefix + "muted", value ? 1 : 0); }
        /// <summary>0 Low, 1 Medium, 2 High.</summary>
        public static int Quality { get => PlayerPrefs.GetInt(Prefix + "quality", DefaultQuality); set => PlayerPrefs.SetInt(Prefix + "quality", value); }
        public static bool ReducedShake { get => PlayerPrefs.GetInt(Prefix + "lessShake", 0) == 1; set => PlayerPrefs.SetInt(Prefix + "lessShake", value ? 1 : 0); }
        public static bool ReducedFlashing { get => PlayerPrefs.GetInt(Prefix + "lessFlash", 0) == 1; set => PlayerPrefs.SetInt(Prefix + "lessFlash", value ? 1 : 0); }
        public static bool HintsSeen { get => PlayerPrefs.GetInt(Prefix + "hints", 0) == 1; set => PlayerPrefs.SetInt(Prefix + "hints", value ? 1 : 0); }
        public static int BestScore { get => PlayerPrefs.GetInt(Prefix + "best", 0); set => PlayerPrefs.SetInt(Prefix + "best", value); }
        public static int BestLevel { get => PlayerPrefs.GetInt(Prefix + "bestLevel", 0); set => PlayerPrefs.SetInt(Prefix + "bestLevel", value); }
        public static string PlayerName { get => PlayerPrefs.GetString(Prefix + "name", ""); set => PlayerPrefs.SetString(Prefix + "name", value); }

        static int DefaultQuality => Application.isMobilePlatform ? 0 : Application.platform == RuntimePlatform.WebGLPlayer ? 1 : 2;
        public static readonly string[] QualityNames = { "Low", "Medium", "High" };

        public static void ResetAll()
        {
            foreach (var k in new[] { "music", "sound", "muted", "quality", "lessShake", "lessFlash", "hints", "best", "bestLevel", "name" }) PlayerPrefs.DeleteKey(Prefix + k);
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
