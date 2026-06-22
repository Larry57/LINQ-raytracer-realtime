// Viewer GPU : le ray tracer tourne entièrement dans un fragment shader (shader.fs),
// un thread GPU par pixel. L'hôte ne fait que pousser les uniforms et dessiner un
// rectangle plein écran. Mêmes contrôles que les viewers CPU.
//
//   clic gauche : orbiter   clic droit : pan   molette : zoom
//
// Build : gcc -O2 c/gpu.c -o c/gpu $(pkg-config --cflags --libs raylib) -lm
// Run   : ./c/gpu
#include <math.h>
#include <stdio.h>
#include "raylib.h"
#include "raymath.h"

#define INIT_W 600
#define INIT_H 600

static const float OrbitSensitivity = 0.008f, PanSensitivity = 0.0015f;
static const float MinPitch = -1.4f, MaxPitch = 1.4f, MinRadius = 1.5f, MaxRadius = 40.f;

static Vector3 orbitTarget;
static float orbitYaw, orbitPitch, orbitRadius;

// Caméra : reproduit camera_create du CPU (right/up mis à l'échelle ×1.5).
static void get_camera(Vector3 *pos, Vector3 *fwd, Vector3 *up, Vector3 *right) {
    float cp = cosf(orbitPitch);
    Vector3 p = {
        orbitTarget.x + orbitRadius * cp * sinf(orbitYaw),
        orbitTarget.y + orbitRadius * sinf(orbitPitch),
        orbitTarget.z + orbitRadius * cp * cosf(orbitYaw)
    };
    Vector3 f = Vector3Normalize(Vector3Subtract(orbitTarget, p));
    Vector3 down = { 0, -1, 0 };
    Vector3 r = Vector3Scale(Vector3Normalize(Vector3CrossProduct(f, down)), 1.5f);
    Vector3 u = Vector3Scale(Vector3Normalize(Vector3CrossProduct(f, r)), 1.5f);
    *pos = p; *fwd = f; *right = r; *up = u;
}

static Vector3 bouncing_ball(void) {
    const float P = 1.1f, BH = 1.3f, restY = 0.5f;
    float t = fmodf((float)GetTime(), P);
    float h = 4 * BH * t * (P - t) / (P * P);
    Vector3 b = { -1.0f, restY + h, 1.5f };
    return b;
}

static float clampf(float x, float lo, float hi) { return x < lo ? lo : (x > hi ? hi : x); }

int main(void) {
    orbitTarget = (Vector3){ 0, 0.5f, 0 };
    Vector3 off = { 3 - orbitTarget.x, 2 - orbitTarget.y, 4 - orbitTarget.z };
    orbitRadius = sqrtf(off.x*off.x + off.y*off.y + off.z*off.z);
    orbitYaw = atan2f(off.x, off.z);
    orbitPitch = asinf(off.y / orbitRadius);

    SetConfigFlags(FLAG_WINDOW_RESIZABLE);
    InitWindow(INIT_W, INIT_H, "Ray Tracer GPU (GLSL)");
    SetWindowMinSize(120, 120);

    // Charge le shader à côté de l'exécutable, quel que soit le cwd.
    Shader shader = LoadShader(0, TextFormat("%sshader.fs", GetApplicationDirectory()));
    int locRes   = GetShaderLocation(shader, "resolution");
    int locPos   = GetShaderLocation(shader, "camPos");
    int locFwd   = GetShaderLocation(shader, "camFwd");
    int locUp    = GetShaderLocation(shader, "camUp");
    int locRight = GetShaderLocation(shader, "camRight");
    int locBall  = GetShaderLocation(shader, "ballCenter");

    int w = GetScreenWidth(), h = GetScreenHeight();

    while (!WindowShouldClose()) {
        w = GetScreenWidth(); h = GetScreenHeight();

        if (IsMouseButtonDown(MOUSE_BUTTON_LEFT)) {
            Vector2 d = GetMouseDelta();
            orbitYaw -= d.x * OrbitSensitivity;
            orbitPitch = clampf(orbitPitch + d.y * OrbitSensitivity, MinPitch, MaxPitch);
        } else if (IsMouseButtonDown(MOUSE_BUTTON_RIGHT)) {
            Vector2 d = GetMouseDelta();
            Vector3 pos, fwd, up, right;
            get_camera(&pos, &fwd, &up, &right);
            float step = orbitRadius * PanSensitivity;
            Vector3 ur = Vector3Scale(right, 1.0f / 1.5f);
            Vector3 uu = Vector3Scale(up,    1.0f / 1.5f);
            orbitTarget = Vector3Subtract(orbitTarget,
                Vector3Add(Vector3Scale(ur, d.x * step), Vector3Scale(uu, d.y * step)));
        }
        float wheel = GetMouseWheelMove();
        if (wheel != 0)
            orbitRadius = clampf(orbitRadius * (wheel > 0 ? 0.9f : 1.1f), MinRadius, MaxRadius);

        Vector3 pos, fwd, up, right;
        get_camera(&pos, &fwd, &up, &right);
        Vector3 ball = bouncing_ball();
        float res[2] = { (float)w, (float)h };
        SetShaderValue(shader, locRes,   res,    SHADER_UNIFORM_VEC2);
        SetShaderValue(shader, locPos,   &pos,   SHADER_UNIFORM_VEC3);
        SetShaderValue(shader, locFwd,   &fwd,   SHADER_UNIFORM_VEC3);
        SetShaderValue(shader, locUp,    &up,    SHADER_UNIFORM_VEC3);
        SetShaderValue(shader, locRight, &right, SHADER_UNIFORM_VEC3);
        SetShaderValue(shader, locBall,  &ball,  SHADER_UNIFORM_VEC3);

        SetWindowTitle(TextFormat("Ray Tracer GPU (GLSL) — %d×%d — %d fps", w, h, GetFPS()));
        { static double last = 0; if (GetTime() - last > 1.0) { last = GetTime();
            fprintf(stderr, "GPU: %d fps  @ %dx%d\n", GetFPS(), w, h); } }

        BeginDrawing();
            ClearBackground(BLACK);
            BeginShaderMode(shader);
                DrawRectangle(0, 0, w, h, WHITE);
            EndShaderMode();
            DrawFPS(10, 10);
        EndDrawing();
    }

    UnloadShader(shader);
    CloseWindow();
    return 0;
}
