// Viewer interactif du ray tracer en C pur, fenêtre via raylib.
// Équivalent visuel du host C# (Program.cs) : orbite / pan / zoom + sphère qui rebondit.
//
//   clic gauche : orbiter   clic droit : pan   molette : zoom
//
// Build : gcc -O2 -march=native -fopenmp viewer.c -o viewer $(pkg-config --cflags --libs raylib) -lm
// Run   : ./viewer
#include <math.h>
#include <stdio.h>
#include "raylib.h"
#include "engine.h"

#define INIT_W 600
#define INIT_H 600

// Constantes caméra (identiques au C# / JS)
static const float OrbitSensitivity = 0.008f, PanSensitivity = 0.0015f;
static const float MinPitch = -1.4f, MaxPitch = 1.4f, MinRadius = 1.5f, MaxRadius = 40.f;

static Vec orbitTarget;
static float orbitYaw, orbitPitch, orbitRadius;

static void get_orbit_camera(Vec *campos, Vec *fwd, Vec *up, Vec *right) {
    float cp = cosf(orbitPitch);
    Vec pos = vadd(orbitTarget, v(
        orbitRadius * cp * sinf(orbitYaw),
        orbitRadius * sinf(orbitPitch),
        orbitRadius * cp * cosf(orbitYaw)));
    *campos = pos;
    camera_create(pos, orbitTarget, fwd, up, right);
}

// Rebond : h(t) = 4 H t (P - t) / P^2
static Vec bouncing_ball(void) {
    const float P = 1.1f, BH = 1.3f, restY = 0.5f, bx = -1, bz = 1.5f;
    float t = fmodf((float)GetTime(), P);
    float h = 4 * BH * t * (P - t) / (P * P);
    return v(bx, restY + h, bz);
}

static float clampf(float x, float lo, float hi) { return x < lo ? lo : (x > hi ? hi : x); }

static void update_title(int w, int h) {
    char t[96];
    snprintf(t, sizeof t, "Ray Tracer C — %d×%d — clic g: orbite, clic d: pan, molette: zoom", w, h);
    SetWindowTitle(t);
}

int main(void) {
    // Caméra initiale (pos (3,2,4) regardant (0,0.5,0))
    orbitTarget = v(0, 0.5f, 0);
    Vec off = vsub(v(3, 2, 4), orbitTarget);
    orbitRadius = vlen(off);
    orbitYaw = atan2f(off.x, off.z);
    orbitPitch = asinf(off.y / orbitRadius);

    SetConfigFlags(FLAG_WINDOW_RESIZABLE);
    InitWindow(INIT_W, INIT_H, "Ray Tracer C — clic g: orbite, clic d: pan, molette: zoom");
    SetWindowMinSize(120, 120);

    int w = GetScreenWidth(), h = GetScreenHeight();
    Image img = GenImageColor(w, h, BLACK);
    Texture2D tex = LoadTextureFromImage(img);
    UnloadImage(img);
    unsigned char *pixels = (unsigned char *)MemAlloc(w * h * 4);
    update_title(w, h);

    while (!WindowShouldClose()) {
        if (IsWindowResized()) {
            UnloadTexture(tex);
            MemFree(pixels);
            w = GetScreenWidth(); h = GetScreenHeight();
            img = GenImageColor(w, h, BLACK);
            tex = LoadTextureFromImage(img);
            UnloadImage(img);
            pixels = (unsigned char *)MemAlloc(w * h * 4);
            update_title(w, h);
        }

        // --- Entrées souris ---
        if (IsMouseButtonDown(MOUSE_BUTTON_LEFT)) {
            Vector2 d = GetMouseDelta();
            orbitYaw -= d.x * OrbitSensitivity;
            orbitPitch = clampf(orbitPitch + d.y * OrbitSensitivity, MinPitch, MaxPitch);
        } else if (IsMouseButtonDown(MOUSE_BUTTON_RIGHT)) {
            Vector2 d = GetMouseDelta();
            Vec cp, fwd, up, right;
            get_orbit_camera(&cp, &fwd, &up, &right);
            float step = orbitRadius * PanSensitivity;
            orbitTarget = vsub(orbitTarget,
                vadd(vscale(d.x * step, vscale(1/1.5f, right)),
                     vscale(d.y * step, vscale(1/1.5f, up))));
        }
        float wheel = GetMouseWheelMove();
        if (wheel != 0)
            orbitRadius = clampf(orbitRadius * (wheel > 0 ? 0.9f : 1.1f), MinRadius, MaxRadius);

        // --- Rendu de la scène ---
        Vec campos, fwd, up, right;
        get_orbit_camera(&campos, &fwd, &up, &right);
        Scene scene = make_scene(campos, fwd, up, right, bouncing_ball());
        render(&scene, pixels, w, h);
        UpdateTexture(tex, pixels);

        BeginDrawing();
        ClearBackground(BLACK);
        DrawTexture(tex, 0, 0, WHITE);
        DrawFPS(10, 10);
        EndDrawing();
    }

    MemFree(pixels);
    UnloadTexture(tex);
    CloseWindow();
    return 0;
}
