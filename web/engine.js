"use strict";
// Moteur de ray tracing pur (aucune dépendance DOM) — partagé par la page et les workers.
// Identique au portage mono-thread (web/index.html) pour que la comparaison reste juste.

// ---------- Vecteurs ----------
const V = (x, y, z) => ({ x, y, z });
const add  = (a, b) => V(a.x + b.x, a.y + b.y, a.z + b.z);
const sub  = (a, b) => V(a.x - b.x, a.y - b.y, a.z - b.z);
const scale= (s, a) => V(s * a.x, s * a.y, s * a.z);
const dot  = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
const cross= (a, b) => V(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
const len  = (a) => Math.sqrt(dot(a, a));
const norm = (a) => { const l = len(a); return V(a.x / l, a.y / l, a.z / l); };

// ---------- Couleurs ----------
const C = (r, g, b) => ({ r, g, b });
const cTimesS = (n, v) => C(n * v.r, n * v.g, n * v.b);
const cTimes  = (a, b) => C(a.r * b.r, a.g * b.g, a.b * b.b);
const cPlus   = (a, b) => C(a.r + b.r, a.g + b.g, a.b + b.b);
const BACKGROUND = C(0, 0, 0);
const legal = (d) => d > 1 ? 1 : d < 0 ? 0 : d;

// ---------- Surfaces ----------
const checkerWhite = C(1, 1, 1), checkerBlack = C(0, 0, 0), whiteSpec = C(1, 1, 1);
const CheckerBoard = {
  diffuse: (p) => ((Math.floor(p.z) + Math.floor(p.x)) % 2 !== 0) ? checkerWhite : checkerBlack,
  specular: (_) => whiteSpec,
  reflect: (p) => ((Math.floor(p.z) + Math.floor(p.x)) % 2 !== 0) ? 0.1 : 0.7,
  roughness: 150,
};
const shinyDiff = C(1, 1, 1), shinySpec = C(0.5, 0.5, 0.5);
const Shiny = {
  diffuse: (_) => shinyDiff,
  specular: (_) => shinySpec,
  reflect: (_) => 0.6,
  roughness: 50,
};

// ---------- Objets de scène ----------
function Sphere(center, radius, surface) { return { kind: "sphere", center, radius, surface }; }
function Plane(normal, offset, surface)  { return { kind: "plane", normal, offset, surface }; }

function intersect(obj, ray) {
  if (obj.kind === "sphere") {
    const eo = sub(obj.center, ray.start);
    const v = dot(eo, ray.dir);
    if (v < 0) return 0;
    const disc = obj.radius * obj.radius - (dot(eo, eo) - v * v);
    if (disc < 0) return 0;
    return v - Math.sqrt(disc);
  } else {
    const denom = dot(obj.normal, ray.dir);
    if (denom > 0) return 0;
    return (dot(obj.normal, ray.start) + obj.offset) / (-denom);
  }
}
function normalAt(obj, pos) {
  return obj.kind === "sphere" ? norm(sub(pos, obj.center)) : obj.normal;
}

// ---------- Lumières & scène ----------
const Lights = [
  { pos: V(-2,   2.5, 0),    color: C(0.49, 0.07, 0.07) },
  { pos: V( 1.5, 2.5, 1.5),  color: C(0.07, 0.07, 0.49) },
  { pos: V( 1.5, 2.5, -1.5), color: C(0.07, 0.49, 0.071) },
  { pos: V( 0,   3.5, 0),    color: C(0.21, 0.21, 0.35) },
];
const BouncingBallRadius = 0.5;
const GroundPlane = Plane(V(0, 1, 0), 0, CheckerBoard);
const BigSphere = Sphere(V(0, 1, 0), 1, Shiny);

function createScene(camera, ballCenter) {
  return {
    things: [GroundPlane, BigSphere, Sphere(ballCenter, BouncingBallRadius, Shiny)],
    lights: Lights,
    camera,
  };
}

function cameraCreate(pos, lookAt) {
  const forward = norm(sub(lookAt, pos));
  const down = V(0, -1, 0);
  const right = scale(1.5, norm(cross(forward, down)));
  const up = scale(1.5, norm(cross(forward, right)));
  return { pos, forward, up, right };
}

// ---------- Ray tracing ----------
const MaxDepth = 5;
const ShadowEpsilon = 1e-4;
const GREY = C(0.5, 0.5, 0.5);

function traceRay(ray, scene, depth) {
  let nearest = null, nearestDist = 0;
  for (const thing of scene.things) {
    const d = intersect(thing, ray);
    if (d !== 0 && (nearest === null || d < nearestDist)) { nearest = thing; nearestDist = d; }
  }
  if (nearest === null) return BACKGROUND;

  const d = ray.dir;
  const pos = add(scale(nearestDist, d), ray.start);
  const normal = normalAt(nearest, pos);
  const reflectDir = sub(d, scale(2 * dot(normal, d), normal));

  const shadowOrigin = add(pos, scale(ShadowEpsilon, normal));
  let naturalColor = BACKGROUND;
  const surf = nearest.surface;
  for (const light of scene.lights) {
    const ldis = sub(light.pos, pos);
    const livec = norm(ldis);
    const testRay = { start: shadowOrigin, dir: livec };

    let neatIsect = 0;
    for (const thing of scene.things) {
      const inter = intersect(thing, testRay);
      if (inter !== 0 && (neatIsect === 0 || inter < neatIsect)) neatIsect = inter;
    }
    const isInShadow = !((neatIsect > len(ldis)) || (neatIsect === 0));
    if (isInShadow) continue;

    const illum = dot(livec, normal);
    const lcolor = illum > 0 ? cTimesS(illum, light.color) : BACKGROUND;
    const specular = dot(livec, norm(reflectDir));
    const scolor = specular > 0 ? cTimesS(Math.pow(specular, surf.roughness), light.color) : BACKGROUND;
    naturalColor = cPlus(naturalColor,
      cPlus(cTimes(surf.diffuse(pos), lcolor), cTimes(surf.specular(pos), scolor)));
  }

  const reflectPos = add(pos, scale(0.001, reflectDir));
  const reflectColor = depth >= MaxDepth
    ? GREY
    : cTimesS(surf.reflect(reflectPos), traceRay({ start: reflectPos, dir: reflectDir }, scene, depth + 1));

  return cPlus(naturalColor, reflectColor);
}

// Rend les lignes [y0, y1) de la scène dans le framebuffer RGBA `pixels`.
function renderBand(scene, pixels, W, H, y0, y1) {
  const scaleF = 2 * H;
  const cam = scene.camera;
  for (let y = y0; y < y1; y++) {
    const recenterY = -(y - H / 2) / scaleF;
    let i = y * W * 4;
    for (let x = 0; x < W; x++) {
      const recenterX = (x - W / 2) / scaleF;
      const point = norm(add(cam.forward, add(scale(recenterX, cam.right), scale(recenterY, cam.up))));
      const col = traceRay({ start: cam.pos, dir: point }, scene, 0);
      pixels[i++] = legal(col.r) * 255;
      pixels[i++] = legal(col.g) * 255;
      pixels[i++] = legal(col.b) * 255;
      pixels[i++] = 255;
    }
  }
}

// Disponible aussi dans un worker (importScripts) : pas d'export ES, tout est en global.
if (typeof self !== "undefined") { self.RT = { createScene, cameraCreate, renderBand, V }; }
