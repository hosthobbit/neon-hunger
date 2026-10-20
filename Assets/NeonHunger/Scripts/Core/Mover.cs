using System;

namespace NeonHunger.Core
{
    /// <summary>
    /// Grid movement shared by the player and the hunters. Actors travel along tile centre-lines at a
    /// constant speed, so collisions are exact and predictable. A turn pressed shortly before a
    /// junction is buffered and taken the moment the actor reaches the junction's centre; reversing
    /// is always instant.
    /// </summary>
    public sealed class Mover
    {
        public float X, Y;                 // tile coordinates (column, row); centres are whole numbers
        public Dir Dir;                    // current motion (None = standing still)
        public Dir Facing = Dir.Left;
        public Dir Buffered;
        public float BufferedAt = -99f;
        /// <summary>How long (seconds) a pressed direction is remembered while waiting for an opening.</summary>
        public float BufferWindow = 0.32f;

        public Tile Tile => new Tile((int)Math.Round(X), (int)Math.Round(Y));
        public bool AtCentre => Math.Abs(X - Math.Round(X)) < 1e-4 && Math.Abs(Y - Math.Round(Y)) < 1e-4;

        /// <summary>The tile being moved into (or the current tile when standing still).</summary>
        public Tile NextTile
        {
            get
            {
                if (Dir == Dir.None || AtCentre) return Dir == Dir.None ? Tile : Tile.Step(Dir);
                // between centres: the tile ahead along the motion
                float fx = X + Dir.DC() * 0.5f, fy = Y + Dir.DR() * 0.5f;
                return new Tile((int)Math.Round(fx), (int)Math.Round(fy));
            }
        }

        public void Place(Tile t, Dir facing = Dir.Left)
        {
            X = t.C; Y = t.R; Dir = Dir.None; Facing = facing; Buffered = Dir.None; BufferedAt = -99f;
        }

        /// <summary>Remember a wanted direction (call every frame while a key is held).</summary>
        public void Want(Dir d, float now)
        {
            if (d == Dir.None) return;
            Buffered = d;
            BufferedAt = now;
        }

        /// <summary>Advance by speed × dt tiles. Returns the distance actually moved.</summary>
        /// <param name="decide">Optional: called at every tile centre reached (hunters choose their way here).</param>
        public float Step(float dt, float speed, Func<Tile, bool> canEnter, float now, Func<Mover, Dir> decide = null)
        {
            float remaining = Math.Max(0f, speed * dt), moved = 0f;
            Tile lastDecided = new Tile(int.MinValue, 0);
            for (int guard = 0; guard < 16 && remaining > 1e-6f; guard++)
            {
                if (decide != null && AtCentre && Tile != lastDecided)
                {
                    lastDecided = Tile;
                    var d = decide(this);
                    if (d != Dir.None && canEnter(Tile.Step(d))) { Dir = d; Facing = d; }
                    else if (d == Dir.None) Dir = Dir.None;
                }
                bool buffered = Buffered != Dir.None && now - BufferedAt <= BufferWindow;
                if (buffered && Buffered == Dir.Opposite() && Dir != Dir.None)
                {
                    Dir = Buffered; Facing = Dir; Buffered = Dir.None;
                }
                else if (buffered && AtCentre && Buffered != Dir && canEnter(Tile.Step(Buffered)))
                {
                    Snap();
                    Dir = Buffered; Facing = Dir; Buffered = Dir.None;
                }
                if (Dir == Dir.None) break;
                if (AtCentre && !canEnter(Tile.Step(Dir))) { Snap(); Dir = Dir.None; break; }

                // distance to the next tile centre along the motion
                float coord = Dir.Horizontal() ? X : Y;
                int sign = Dir.Horizontal() ? Dir.DC() : Dir.DR();
                float along = coord * sign;
                float next = (float)Math.Floor(along + 1e-4) + 1f;
                float toCentre = next - along;
                float step = Math.Min(remaining, toCentre);
                if (Dir.Horizontal()) X += step * sign; else Y += step * sign;
                remaining -= step;
                moved += step;
                if (step >= toCentre - 1e-5f) Snap();
                Facing = Dir;
            }
            return moved;
        }

        void Snap() { X = (float)Math.Round(X); Y = (float)Math.Round(Y); }

        public float DistanceTo(Mover o) => (float)Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
    }
}
