using System;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace RayTracerGpu
{
    // Viewer GPU en C# : le ray tracer tourne entièrement dans un fragment shader
    // (c/shader.fs), un thread GPU par pixel. L'hôte ne fait que pousser les uniforms
    // et dessiner un rectangle plein écran. Pendant CES mêmes contrôles que la version
    // CPU (Program.cs racine) et que le viewer C (c/gpu.c) dont ce fichier est le portage.
    //
    //   clic gauche : orbiter      clic droit : pan      molette : zoom
    //
    // Build : dotnet build gpu-cs/RayTracerGpu.csproj
    // Run   : dotnet run --project gpu-cs/RayTracerGpu.csproj
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

        // Balle qui rebondit : parabole h(t) = 4 H t (P - t) / P^2 sur la période P.
        const float BallRestY = 0.5f;   // = rayon de la balle (bas posé sur le plan y=0)
        const float BallX = -1f;
        const float BallZ = 1.5f;
        const float BounceHeight = 1.3f;
        const float BouncePeriod = 1.1f;

        static Vector3 orbitTarget = new Vector3(0, 0.5f, 0);
        static float orbitYaw, orbitPitch, orbitRadius;

        static void Main()
        {
            var initialPos = new Vector3(3, 2, 4);
            var initialOffset = initialPos - orbitTarget;
            orbitRadius = initialOffset.Length();
            orbitYaw = MathF.Atan2(initialOffset.X, initialOffset.Z);
            orbitPitch = MathF.Asin(initialOffset.Y / orbitRadius);

            Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
            Raylib.InitWindow(InitialWidth, InitialHeight, "Ray Tracer GPU (GLSL) — C#");
            Raylib.SetWindowMinSize(120, 120);

            // Charge le shader à côté de l'exécutable, quel que soit le cwd.
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "shader.fs");
            var shader = Raylib.LoadShader(null, shaderPath);
            int locRes   = Raylib.GetShaderLocation(shader, "resolution");
            int locPos   = Raylib.GetShaderLocation(shader, "camPos");
            int locFwd   = Raylib.GetShaderLocation(shader, "camFwd");
            int locUp    = Raylib.GetShaderLocation(shader, "camUp");
            int locRight = Raylib.GetShaderLocation(shader, "camRight");
            int locBall  = Raylib.GetShaderLocation(shader, "ballCenter");

            while (!Raylib.WindowShouldClose())
            {
                int w = Raylib.GetScreenWidth();
                int h = Raylib.GetScreenHeight();

                HandleInput();

                GetCamera(out var pos, out var fwd, out var up, out var right);
                var ball = GetBouncingBallCenter();

                Raylib.SetShaderValue(shader, locRes,   new Vector2(w, h), ShaderUniformDataType.Vec2);
                Raylib.SetShaderValue(shader, locPos,   pos,   ShaderUniformDataType.Vec3);
                Raylib.SetShaderValue(shader, locFwd,   fwd,   ShaderUniformDataType.Vec3);
                Raylib.SetShaderValue(shader, locUp,    up,    ShaderUniformDataType.Vec3);
                Raylib.SetShaderValue(shader, locRight, right, ShaderUniformDataType.Vec3);
                Raylib.SetShaderValue(shader, locBall,  ball,  ShaderUniformDataType.Vec3);

                Raylib.SetWindowTitle($"Ray Tracer GPU (GLSL) — C# — {w}×{h} — {Raylib.GetFPS()} fps");

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                Raylib.BeginShaderMode(shader);
                Raylib.DrawRectangle(0, 0, w, h, Color.White);
                Raylib.EndShaderMode();
                Raylib.DrawFPS(10, 10);
                Raylib.EndDrawing();
            }

            Raylib.UnloadShader(shader);
            Raylib.CloseWindow();
        }

        static void HandleInput()
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                var delta = Raylib.GetMouseDelta();
                orbitYaw -= delta.X * OrbitSensitivity;
                orbitPitch = Math.Clamp(orbitPitch + delta.Y * OrbitSensitivity, MinPitch, MaxPitch);
            }
            else if (Raylib.IsMouseButtonDown(MouseButton.Right))
            {
                // Pan : translate orbitTarget dans le plan caméra. Right/Up sont mis à
                // l'échelle ×1.5 (cf. GetCamera), on revient donc à des vecteurs unitaires.
                var delta = Raylib.GetMouseDelta();
                GetCamera(out _, out _, out var up, out var right);
                var step = orbitRadius * PanSensitivity;
                orbitTarget -= delta.X * step * (right / 1.5f) + delta.Y * step * (up / 1.5f);
            }

            var wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
                orbitRadius = Math.Clamp(orbitRadius * (wheel > 0 ? 0.9f : 1.1f), MinRadius, MaxRadius);
        }

        // Reproduit Camera.Create du moteur CPU : right/up mis à l'échelle ×1.5.
        static void GetCamera(out Vector3 pos, out Vector3 fwd, out Vector3 up, out Vector3 right)
        {
            var cp = MathF.Cos(orbitPitch);
            pos = orbitTarget + new Vector3(
                orbitRadius * cp * MathF.Sin(orbitYaw),
                orbitRadius * MathF.Sin(orbitPitch),
                orbitRadius * cp * MathF.Cos(orbitYaw));

            fwd = Vector3.Normalize(orbitTarget - pos);
            var down = new Vector3(0, -1, 0);
            right = 1.5f * Vector3.Normalize(Vector3.Cross(fwd, down));
            up = 1.5f * Vector3.Normalize(Vector3.Cross(fwd, right));
        }

        static Vector3 GetBouncingBallCenter()
        {
            var t = (float)(Raylib.GetTime() % BouncePeriod);
            var hgt = 4f * BounceHeight * t * (BouncePeriod - t) / (BouncePeriod * BouncePeriod);
            return new Vector3(BallX, BallRestY + hgt, BallZ);
        }
    }
}
