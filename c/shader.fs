#version 330
// Ray tracer complet exécuté sur le GPU, un thread par pixel.
// Même scène / algo que le moteur CPU (engine.h), mais la récursion des
// réflexions (profondeur 5) est déroulée en boucle : GLSL n'a pas de récursion.
out vec4 finalColor;

uniform vec2 resolution;
uniform vec3 camPos;
uniform vec3 camFwd;
uniform vec3 camUp;     // déjà × 1.5 (cf. camera_create)
uniform vec3 camRight;  // déjà × 1.5
uniform vec3 ballCenter;

const int   MAXDEPTH = 5;
const float EPS = 1e-4;

// kind: 0 = sphère, 1 = plan   |   surf: 0 = damier, 1 = brillant
struct Obj { int kind; int surf; vec3 center; float radius; vec3 norm; float offset; };
Obj objs[3];

const int NL = 4;
const vec3 lightPos[NL] = vec3[](
    vec3(-2.0, 2.5, 0.0), vec3(1.5, 2.5, 1.5), vec3(1.5, 2.5, -1.5), vec3(0.0, 3.5, 0.0));
const vec3 lightCol[NL] = vec3[](
    vec3(0.49,0.07,0.07), vec3(0.07,0.07,0.49), vec3(0.07,0.49,0.071), vec3(0.21,0.21,0.35));

float intersectObj(Obj o, vec3 ro, vec3 rd) {
    if (o.kind == 0) {
        vec3 eo = o.center - ro;
        float v = dot(eo, rd);
        if (v < 0.0) return 0.0;
        float disc = o.radius*o.radius - (dot(eo,eo) - v*v);
        if (disc < 0.0) return 0.0;
        return v - sqrt(disc);
    } else {
        float denom = dot(o.norm, rd);
        if (denom > 0.0) return 0.0;
        return (dot(o.norm, ro) + o.offset) / (-denom);
    }
}
vec3 normalObj(Obj o, vec3 p) { return o.kind == 0 ? normalize(p - o.center) : o.norm; }

bool checker(vec3 p) { return mod(floor(p.z) + floor(p.x), 2.0) != 0.0; }
vec3  diffuse(int s, vec3 p)  { return s == 0 ? (checker(p) ? vec3(1.0) : vec3(0.0)) : vec3(1.0); }
vec3  specularCol(int s)      { return s == 0 ? vec3(1.0) : vec3(0.5); }
float reflectivity(int s, vec3 p) { return s == 0 ? (checker(p) ? 0.1 : 0.7) : 0.6; }
float roughness(int s)        { return s == 0 ? 150.0 : 50.0; }

void trace(vec3 ro, vec3 rd, out int idx, out float dist) {
    idx = -1; dist = 0.0;
    for (int i = 0; i < 3; i++) {
        float d = intersectObj(objs[i], ro, rd);
        if (d != 0.0 && (idx < 0 || d < dist)) { idx = i; dist = d; }
    }
}

vec3 rayTrace(vec3 ro, vec3 rd) {
    vec3 color = vec3(0.0);
    vec3 atten = vec3(1.0);
    for (int bounce = 0; bounce <= MAXDEPTH; bounce++) {
        int idx; float dist;
        trace(ro, rd, idx, dist);
        if (idx < 0) break;                 // fond noir
        Obj o = objs[idx];
        int surf = o.surf;
        vec3 pos = ro + dist * rd;
        vec3 nrm = normalObj(o, pos);
        vec3 reflectDir = rd - 2.0 * dot(nrm, rd) * nrm;

        vec3 natural = vec3(0.0);
        vec3 shadowOrigin = pos + EPS * nrm;
        for (int li = 0; li < NL; li++) {
            vec3 ldis = lightPos[li] - pos;
            vec3 livec = normalize(ldis);
            int sidx; float sdist;
            trace(shadowOrigin, livec, sidx, sdist);
            bool inShadow = !((sdist > length(ldis)) || (sdist == 0.0));
            if (inShadow) continue;
            float illum = dot(livec, nrm);
            vec3 lcolor = illum > 0.0 ? illum * lightCol[li] : vec3(0.0);
            float spec = dot(livec, normalize(reflectDir));
            vec3 scolor = spec > 0.0 ? pow(spec, roughness(surf)) * lightCol[li] : vec3(0.0);
            natural += diffuse(surf, pos) * lcolor + specularCol(surf) * scolor;
        }
        color += atten * natural;

        if (bounce == MAXDEPTH) { color += atten * vec3(0.5); break; }
        vec3 reflectPos = pos + 0.001 * reflectDir;
        atten *= reflectivity(surf, reflectPos);
        ro = reflectPos;
        rd = reflectDir;
    }
    return color;
}

void main() {
    // Coords pixel en repère haut-gauche (comme le CPU) ; gl_FragCoord est bas-gauche.
    float px = gl_FragCoord.x;
    float py = resolution.y - gl_FragCoord.y;
    float scaleF = 2.0 * resolution.y;
    float rcx =  (px - resolution.x * 0.5) / scaleF;
    float rcy = -(py - resolution.y * 0.5) / scaleF;
    vec3 dir = normalize(camFwd + rcx * camRight + rcy * camUp);

    objs[0] = Obj(1, 0, vec3(0.0),        0.0, vec3(0.0, 1.0, 0.0), 0.0);
    objs[1] = Obj(0, 1, vec3(0.0,1.0,0.0),1.0, vec3(0.0),           0.0);
    objs[2] = Obj(0, 1, ballCenter,       0.5, vec3(0.0),           0.0);

    finalColor = vec4(clamp(rayTrace(camPos, dir), 0.0, 1.0), 1.0);
}
