using UnityEngine;
using UnityEngine.Rendering;

namespace NeonHunger
{
    /// <summary>
    /// Mood lighting: a dim, cold base; a violet wash with stronger bloom during a power surge; a
    /// faint crimson pulse while walls are about to move or a risk door is closing. Reduced
    /// flashing keeps these changes slow and steady.
    /// </summary>
    public class Atmosphere : MonoBehaviour
    {
        public Volume SurgeVolume, DangerVolume;
        public Light Moon;
        public Color AmbientCalm = new Color(0.1f, 0.11f, 0.17f), AmbientSurge = new Color(0.2f, 0.1f, 0.3f);
        public bool Surge, Danger;
        float surge, danger;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool calm = Settings.ReducedFlashing;
            surge = Mathf.MoveTowards(surge, Surge ? 1f : 0f, dt * (calm ? 1f : 3f));
            float pulse = Danger ? (calm ? 0.5f : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f)) : 0f;
            danger = Mathf.MoveTowards(danger, pulse, dt * (calm ? 1f : 6f));
            if (SurgeVolume) SurgeVolume.weight = surge * (calm ? 0.6f : 1f);
            if (DangerVolume) DangerVolume.weight = danger * 0.8f;
            RenderSettings.ambientLight = Color.Lerp(AmbientCalm, AmbientSurge, surge);
            if (Moon) Moon.intensity = Mathf.Lerp(0.22f, 0.12f, surge);
        }
    }
}
