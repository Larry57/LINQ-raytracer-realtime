using System;
using Raylib_cs;
using Vector = System.Numerics.Vector3;
using RlColor = Raylib_cs.Color;

namespace RayTracer
{
    // Cross-platform host for the real-time ray tracer (Linux / Windows / macOS).
    // Replaces the original Windows Forms viewer with Raylib: the framebuffer is
    // uploaded to a GPU texture each frame, and the mouse drives an orbit camera.
    //
    //   left drag  : orbit      right drag : pan      wheel : zoom
    static class Program
    {
        const int InitialWidth = 600;
        const int InitialHeight = 600;
        const float OrbitSensitivity = 0.008f;
        const float PanSensitivity = 0.0015f;
        const float MinPitch = -1.4f;
        const float MaxPitch = 1.4f;
        const float MinRadius = 1.5f;
        const float MaxRadius = 40f;

        // Bouncing ball: parabola h(t) = 4 H t (P - t) / P^2 over period P, peak height H.
        const float BallRestY = RayTracer.BouncingBallRadius;   // bottom of ball sits on the y=0 plane
        const float BallX = -1f;
        const float BallZ = 1.5f;
        const float BounceHeight = 1.3f;
        const float BouncePeriod = 1.1f;

        static Vector orbitTarget = new Vector(0, 0.5f, 0);
        static float orbitYaw, orbitPitch, orbitRadius;

        static void Main()
        {
            var initialPos = new Vector(3, 2, 4);
            var initialOffset = initialPos - orbitTarget;
            orbitRadius = initialOffset.Length();
            orbitYaw = MathF.Atan2(initialOffset.X, initialOffset.Z);
            orbitPitch = MathF.Asin(initialOffset.Y / orbitRadius);

            Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
            Raylib.InitWindow(InitialWidth, InitialHeight,
                "Ray Tracer — left drag: orbit, right drag: pan, wheel: zoom");
            Raylib.SetWindowMinSize(120, 120);

            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            var (texture, buffer) = CreateTarget(w, h);
            UpdateTitle(w, h);

            while (!Raylib.WindowShouldClose())
            {
                if (Raylib.IsWindowResized())
                {
                    Raylib.UnloadTexture(texture);
                    w = Raylib.GetScreenWidth();
                    h = Raylib.GetScreenHeight();
                    (texture, buffer) = CreateTarget(w, h);
                    UpdateTitle(w, h);
                }

                HandleInput();

                var center = GetBouncingBallCenter();
                var scene = RayTracer.CreateScene(GetOrbitCamera(), center);

                var rt = new RayTracer(w, h, (x, y, color) => buffer[y * w + x] = color);
                rt.Render(scene);
                Raylib.UpdateTexture(texture, buffer);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(RlColor.Black);
                Raylib.DrawTexture(texture, 0, 0, RlColor.White);
                Raylib.DrawFPS(10, 10);
                Raylib.EndDrawing();
            }

            Raylib.UnloadTexture(texture);
            Raylib.CloseWindow();
        }

        static void UpdateTitle(int w, int h) =>
            Raylib.SetWindowTitle($"Ray Tracer C# — {w}×{h} — clic g: orbite, clic d: pan, molette: zoom");

        static (Texture2D, int[]) CreateTarget(int w, int h)
        {
            var image = Raylib.GenImageColor(w, h, RlColor.Black);
            var texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            return (texture, new int[w * h]);
        }

        static void HandleInput()
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                var delta = Raylib.GetMouseDelta();
                orbitYaw -= delta.X * OrbitSensitivity;
                orbitPitch += delta.Y * OrbitSensitivity;
                orbitPitch = Math.Clamp(orbitPitch, MinPitch, MaxPitch);
            }
            else if (Raylib.IsMouseButtonDown(MouseButton.Right))
            {
                // Pan: translate orbitTarget in the camera plane. Camera.Right and .Up
                // are scaled by 1.5 in Camera.Create, so divide back to unit vectors.
                var delta = Raylib.GetMouseDelta();
                var cam = GetOrbitCamera();
                var camRight = cam.Right / 1.5f;
                var camUp = cam.Up / 1.5f;
                var step = orbitRadius * PanSensitivity;
                orbitTarget -= delta.X * step * camRight + delta.Y * step * camUp;
            }

            var wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
            {
                orbitRadius *= wheel > 0 ? 0.9f : 1.1f;
                orbitRadius = Math.Clamp(orbitRadius, MinRadius, MaxRadius);
            }
        }

        static Camera GetOrbitCamera()
        {
            var cp = MathF.Cos(orbitPitch);
            var pos = orbitTarget + new Vector(
                orbitRadius * cp * MathF.Sin(orbitYaw),
                orbitRadius * MathF.Sin(orbitPitch),
                orbitRadius * cp * MathF.Cos(orbitYaw));
            return Camera.Create(pos, orbitTarget);
        }

        static Vector GetBouncingBallCenter()
        {
            var t = (float)(Raylib.GetTime() % BouncePeriod);
            var hgt = 4f * BounceHeight * t * (BouncePeriod - t) / (BouncePeriod * BouncePeriod);
            return new Vector(BallX, BallRestY + hgt, BallZ);
        }
    }
}
