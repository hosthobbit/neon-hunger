using UnityEngine;

namespace NeonHunger
{
    /// <summary>Materials made by the Editor builder (so their shaders ship with the build).</summary>
    public class Art : MonoBehaviour
    {
        [Header("Maze")]
        public Material Floor;
        public Material FloorPen, FloorRisk, Base, Wall, WallTop, NeonCyan, NeonViolet, NeonCrimson, NeonAmber, ShiftWall, Warning, Gate, Door, Beam, Lamp;
        [Header("Items")]
        public Material Node;
        public Material Power, Gem, RiskGem;
        [Header("Actors")]
        public Material PlayerBody;
        public Material PlayerEye, Pupil, Mouth, HunterEye, Stalker, Ambusher, Warden, Listener, Frightened, Trail;
        [Header("Particles")]
        public Material Spark;
        public Material Mist;
        public Font WorldFont;

        public static Art Instance { get; private set; }
        void Awake()
        {
            Instance = this;
            if (!Floor || !Wall || !Node || !PlayerBody || !Stalker || !Spark) Debug.LogError("NH_SCENE_ERROR Art is missing materials");
        }

        public Material HunterBody(Core.HunterKind k) =>
            k == Core.HunterKind.Stalker ? Stalker : k == Core.HunterKind.Ambusher ? Ambusher : k == Core.HunterKind.Warden ? Warden : Listener;

        public static Color HunterColour(Core.HunterKind k) =>
            k == Core.HunterKind.Stalker ? new Color(1f, 0.16f, 0.26f) : k == Core.HunterKind.Ambusher ? new Color(0.72f, 0.3f, 1f)
            : k == Core.HunterKind.Warden ? new Color(1f, 0.66f, 0.2f) : new Color(0.2f, 1f, 0.7f);

        /// <summary>Tile (column, row) to world position: rows run towards -Z.</summary>
        public static Vector3 World(float c, float r, float y = 0f) => new Vector3(c, y, -r);
    }
}
