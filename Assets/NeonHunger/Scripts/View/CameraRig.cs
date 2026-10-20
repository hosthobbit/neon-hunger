using UnityEngine;

namespace NeonHunger
{
    /// <summary>
    /// Angled overhead camera that follows the player smoothly with a little look-ahead, stays
    /// inside the maze, and pulls back on narrow (portrait) screens. Shake respects the
    /// reduced-shake setting.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public float Pitch = 66f;
        [Tooltip("Tiles of maze visible across a 16:9 screen.")] public float TilesAcross = 15f;
        public float LookAhead = 1.4f;
        public float Smooth = 5f;
        public Vector3 Target;
        public Vector3 LookDir;
        public Vector2 MinXZ, MaxXZ;
        Vector3 focus;
        float shake;
        Camera cam;
        bool snapped;

        void Awake() { cam = GetComponent<Camera>(); }

        public void Shake(float amount) => shake = Mathf.Max(shake, Settings.ReducedShake ? amount * 0.15f : amount);
        public void Snap() => snapped = false;

        void LateUpdate()
        {
            var want = Target + LookDir * LookAhead;
            want.x = Mathf.Clamp(want.x, MinXZ.x, MaxXZ.x);
            want.z = Mathf.Clamp(want.z, MinXZ.y, MaxXZ.y);
            float dt = Time.unscaledDeltaTime;
            focus = snapped ? Vector3.Lerp(focus, want, 1f - Mathf.Exp(-dt * Smooth)) : want;
            snapped = true;
            // distance so that roughly TilesAcross tiles fit the width (more on narrow screens)
            float aspect = Mathf.Max(0.4f, cam.aspect);
            float halfW = TilesAcross * 0.5f * (aspect < 1f ? 1.25f : 1f);
            float tanH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
            float dist = halfW / tanH;
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
            float t = Time.unscaledTime;
            var jitter = new Vector3(Mathf.PerlinNoise(t * 30f, 1f) - 0.5f, 0f, Mathf.PerlinNoise(2f, t * 30f) - 0.5f) * shake * 0.8f;
            transform.SetPositionAndRotation(focus + jitter - rot * Vector3.forward * dist, rot);
        }
    }
}
