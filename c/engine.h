// Moteur de ray tracing pur en C (sans dépendance d'affichage).
// Partagé par le benchmark (raytracer.c) et le viewer raylib (viewer.c).
// Fidèle au C# / JS : mêmes scène, algo (réflexions, ombres, spéculaire), float.
#ifndef RT_ENGINE_H
#define RT_ENGINE_H

#include <math.h>
#include <stddef.h>

// ---------- Vecteurs ----------
typedef struct { float x, y, z; } Vec;
static inline Vec v(float x, float y, float z) { Vec r = {x, y, z}; return r; }
static inline Vec vadd(Vec a, Vec b) { return v(a.x+b.x, a.y+b.y, a.z+b.z); }
static inline Vec vsub(Vec a, Vec b) { return v(a.x-b.x, a.y-b.y, a.z-b.z); }
static inline Vec vscale(float s, Vec a) { return v(s*a.x, s*a.y, s*a.z); }
static inline float vdot(Vec a, Vec b) { return a.x*b.x + a.y*b.y + a.z*b.z; }
static inline Vec vcross(Vec a, Vec b) { return v(a.y*b.z - a.z*b.y, a.z*b.x - a.x*b.z, a.x*b.y - a.y*b.x); }
static inline float vlen(Vec a) { return sqrtf(vdot(a, a)); }
static inline Vec vnorm(Vec a) { float l = vlen(a); return v(a.x/l, a.y/l, a.z/l); }

// ---------- Couleurs ----------
typedef struct { float r, g, b; } Col;
static inline Col mkcol(float r, float g, float b) { Col x = {r, g, b}; return x; }
static inline Col cscale(float n, Col a) { return mkcol(n*a.r, n*a.g, n*a.b); }
static inline Col cmul(Col a, Col b) { return mkcol(a.r*b.r, a.g*b.g, a.b*b.b); }
static inline Col cadd(Col a, Col b) { return mkcol(a.r+b.r, a.g+b.g, a.b+b.b); }
static const Col BACKGROUND = {0, 0, 0};
static inline float legal(float d) { return d > 1 ? 1 : (d < 0 ? 0 : d); }

// ---------- Surfaces ----------
enum { SURF_CHECKER, SURF_SHINY };
static Col surf_diffuse(int s, Vec p) {
    if (s == SURF_CHECKER)
        return ((int)(floorf(p.z) + floorf(p.x)) % 2 != 0) ? mkcol(1,1,1) : mkcol(0,0,0);
    return mkcol(1, 1, 1); // shiny
}
static Col surf_specular(int s, Vec p) {
    (void)p;
    return s == SURF_CHECKER ? mkcol(1,1,1) : mkcol(.5f,.5f,.5f);
}
static float surf_reflect(int s, Vec p) {
    if (s == SURF_CHECKER)
        return ((int)(floorf(p.z) + floorf(p.x)) % 2 != 0) ? .1f : .7f;
    return .6f; // shiny
}
static float surf_roughness(int s) { return s == SURF_CHECKER ? 150.f : 50.f; }

// ---------- Objets ----------
enum { OBJ_SPHERE, OBJ_PLANE };
typedef struct {
    int kind, surf;
    Vec center;  float radius;   // sphère
    Vec normal;  float offset;   // plan
} Obj;

typedef struct { Vec start, dir; } RtRay;   // "Rt" : évite le conflit avec raylib::Ray

static float intersect(const Obj *o, RtRay ray) {
    if (o->kind == OBJ_SPHERE) {
        Vec eo = vsub(o->center, ray.start);
        float vv = vdot(eo, ray.dir);
        if (vv < 0) return 0;
        float disc = o->radius*o->radius - (vdot(eo, eo) - vv*vv);
        if (disc < 0) return 0;
        return vv - sqrtf(disc);
    } else {
        float denom = vdot(o->normal, ray.dir);
        if (denom > 0) return 0;
        return (vdot(o->normal, ray.start) + o->offset) / (-denom);
    }
}
static Vec normalAt(const Obj *o, Vec pos) {
    return o->kind == OBJ_SPHERE ? vnorm(vsub(pos, o->center)) : o->normal;
}

// ---------- Lumières & scène ----------
typedef struct { Vec pos; Col color; } Light;
static const Light LIGHTS[4] = {
    { {-2,   2.5f, 0},    {.49f, .07f, .07f} },
    { { 1.5f,2.5f, 1.5f}, {.07f, .07f, .49f} },
    { { 1.5f,2.5f,-1.5f}, {.07f, .49f, .071f} },
    { { 0,   3.5f, 0},    {.21f, .21f, .35f} },
};
#define NLIGHTS 4

typedef struct { Obj things[3]; Vec campos, forward, up, right; } Scene;

static Scene make_scene(Vec campos, Vec forward, Vec up, Vec right, Vec ball) {
    Scene s;
    s.things[0] = (Obj){ .kind=OBJ_PLANE,  .surf=SURF_CHECKER, .normal=v(0,1,0), .offset=0 };
    s.things[1] = (Obj){ .kind=OBJ_SPHERE, .surf=SURF_SHINY,   .center=v(0,1,0), .radius=1 };
    s.things[2] = (Obj){ .kind=OBJ_SPHERE, .surf=SURF_SHINY,   .center=ball,     .radius=.5f };
    s.campos = campos; s.forward = forward; s.up = up; s.right = right;
    return s;
}

static void camera_create(Vec pos, Vec lookAt, Vec *forward, Vec *up, Vec *right) {
    Vec f = vnorm(vsub(lookAt, pos));
    Vec down = v(0, -1, 0);
    Vec r = vscale(1.5f, vnorm(vcross(f, down)));
    Vec u = vscale(1.5f, vnorm(vcross(f, r)));
    *forward = f; *up = u; *right = r;
}

// ---------- Ray tracing ----------
#define MAXDEPTH 5
static const float ShadowEpsilon = 1e-4f;

static Col traceRay(RtRay ray, const Scene *sc, int depth) {
    const Obj *nearest = NULL; float nearestDist = 0;
    for (int i = 0; i < 3; i++) {
        float d = intersect(&sc->things[i], ray);
        if (d != 0 && (nearest == NULL || d < nearestDist)) { nearest = &sc->things[i]; nearestDist = d; }
    }
    if (nearest == NULL) return BACKGROUND;

    Vec d = ray.dir;
    Vec pos = vadd(vscale(nearestDist, d), ray.start);
    Vec normal = normalAt(nearest, pos);
    Vec reflectDir = vsub(d, vscale(2 * vdot(normal, d), normal));

    Vec shadowOrigin = vadd(pos, vscale(ShadowEpsilon, normal));
    Col naturalColor = BACKGROUND;
    int surf = nearest->surf;
    for (int li = 0; li < NLIGHTS; li++) {
        Vec ldis = vsub(LIGHTS[li].pos, pos);
        Vec livec = vnorm(ldis);
        RtRay testRay = { shadowOrigin, livec };

        float neatIsect = 0;
        for (int i = 0; i < 3; i++) {
            float inter = intersect(&sc->things[i], testRay);
            if (inter != 0 && (neatIsect == 0 || inter < neatIsect)) neatIsect = inter;
        }
        int isInShadow = !((neatIsect > vlen(ldis)) || (neatIsect == 0));
        if (isInShadow) continue;

        float illum = vdot(livec, normal);
        Col lcolor = illum > 0 ? cscale(illum, LIGHTS[li].color) : BACKGROUND;
        float spec = vdot(livec, vnorm(reflectDir));
        Col scolor = spec > 0 ? cscale(powf(spec, surf_roughness(surf)), LIGHTS[li].color) : BACKGROUND;
        naturalColor = cadd(naturalColor,
            cadd(cmul(surf_diffuse(surf, pos), lcolor), cmul(surf_specular(surf, pos), scolor)));
    }

    Vec reflectPos = vadd(pos, vscale(0.001f, reflectDir));
    Col reflectColor;
    if (depth >= MAXDEPTH) reflectColor = mkcol(.5f, .5f, .5f);
    else {
        RtRay rr = { reflectPos, reflectDir };
        reflectColor = cscale(surf_reflect(surf, reflectPos), traceRay(rr, sc, depth + 1));
    }
    return cadd(naturalColor, reflectColor);
}

// Rend toute l'image dans le framebuffer RGBA `pixels` (OpenMP sur les lignes).
static void render(const Scene *sc, unsigned char *pixels, int W, int H) {
    float scaleF = 2.f * H;
    #pragma omp parallel for schedule(dynamic, 8)
    for (int y = 0; y < H; y++) {
        float recenterY = -(y - H / 2.f) / scaleF;
        int idx = y * W * 4;
        for (int x = 0; x < W; x++) {
            float recenterX = (x - W / 2.f) / scaleF;
            Vec point = vnorm(vadd(sc->forward, vadd(vscale(recenterX, sc->right), vscale(recenterY, sc->up))));
            RtRay ray = { sc->campos, point };
            Col col = traceRay(ray, sc, 0);
            pixels[idx++] = (unsigned char)(legal(col.r) * 255);
            pixels[idx++] = (unsigned char)(legal(col.g) * 255);
            pixels[idx++] = (unsigned char)(legal(col.b) * 255);
            pixels[idx++] = 255;
        }
    }
}

#endif // RT_ENGINE_H
