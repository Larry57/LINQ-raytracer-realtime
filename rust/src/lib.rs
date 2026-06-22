// Moteur de ray tracing Rust, partagé par le benchmark (main.rs) et le viewer (bin/viewer.rs).
use std::ops::{Add, Sub, Mul};
use std::sync::atomic::{AtomicUsize, Ordering};

// ---------- Vecteurs ----------
#[derive(Clone, Copy)]
pub struct V { pub x: f32, pub y: f32, pub z: f32 }
#[inline(always)] pub const fn v(x: f32, y: f32, z: f32) -> V { V { x, y, z } }
impl Add for V { type Output = V; #[inline(always)] fn add(self, o: V) -> V { v(self.x+o.x, self.y+o.y, self.z+o.z) } }
impl Sub for V { type Output = V; #[inline(always)] fn sub(self, o: V) -> V { v(self.x-o.x, self.y-o.y, self.z-o.z) } }
impl Mul<f32> for V { type Output = V; #[inline(always)] fn mul(self, s: f32) -> V { v(self.x*s, self.y*s, self.z*s) } }
#[inline(always)] pub fn dot(a: V, b: V) -> f32 { a.x*b.x + a.y*b.y + a.z*b.z }
#[inline(always)] pub fn cross(a: V, b: V) -> V { v(a.y*b.z - a.z*b.y, a.z*b.x - a.x*b.z, a.x*b.y - a.y*b.x) }
#[inline(always)] pub fn vlen(a: V) -> f32 { dot(a, a).sqrt() }
#[inline(always)] pub fn norm(a: V) -> V { let l = vlen(a); v(a.x/l, a.y/l, a.z/l) }

// ---------- Couleurs ----------
#[derive(Clone, Copy)]
struct Col { r: f32, g: f32, b: f32 }
#[inline(always)] const fn c(r: f32, g: f32, b: f32) -> Col { Col { r, g, b } }
#[inline(always)] fn cscale(n: f32, a: Col) -> Col { c(n*a.r, n*a.g, n*a.b) }
#[inline(always)] fn cmul(a: Col, b: Col) -> Col { c(a.r*b.r, a.g*b.g, a.b*b.b) }
#[inline(always)] fn cadd(a: Col, b: Col) -> Col { c(a.r+b.r, a.g+b.g, a.b+b.b) }
const BG: Col = c(0.0, 0.0, 0.0);
#[inline(always)] fn legal(d: f32) -> f32 { if d > 1.0 { 1.0 } else if d < 0.0 { 0.0 } else { d } }

// ---------- Surfaces ----------
#[derive(Clone, Copy, PartialEq)]
enum Surf { Checker, Shiny }
#[inline(always)] fn checker(p: V) -> bool { ((p.z.floor() + p.x.floor()) as i32) % 2 != 0 }
#[inline(always)] fn surf_diffuse(s: Surf, p: V) -> Col {
    match s { Surf::Checker => if checker(p) { c(1.,1.,1.) } else { c(0.,0.,0.) }, Surf::Shiny => c(1.,1.,1.) }
}
#[inline(always)] fn surf_specular(s: Surf) -> Col { match s { Surf::Checker => c(1.,1.,1.), Surf::Shiny => c(0.5,0.5,0.5) } }
#[inline(always)] fn surf_reflect(s: Surf, p: V) -> f32 {
    match s { Surf::Checker => if checker(p) { 0.1 } else { 0.7 }, Surf::Shiny => 0.6 }
}
#[inline(always)] fn surf_roughness(s: Surf) -> f32 { match s { Surf::Checker => 150.0, Surf::Shiny => 50.0 } }

// ---------- Objets ----------
#[derive(Clone, Copy)]
enum Kind { Sphere, Plane }
#[derive(Clone, Copy)]
struct Obj { kind: Kind, surf: Surf, center: V, radius: f32, norm: V, offset: f32 }

#[inline(always)]
fn intersect(o: &Obj, ro: V, rd: V) -> f32 {
    match o.kind {
        Kind::Sphere => {
            let eo = o.center - ro;
            let vv = dot(eo, rd);
            if vv < 0.0 { return 0.0; }
            let disc = o.radius*o.radius - (dot(eo, eo) - vv*vv);
            if disc < 0.0 { return 0.0; }
            vv - disc.sqrt()
        }
        Kind::Plane => {
            let denom = dot(o.norm, rd);
            if denom > 0.0 { return 0.0; }
            (dot(o.norm, ro) + o.offset) / (-denom)
        }
    }
}
#[inline(always)]
fn normal_at(o: &Obj, pos: V) -> V {
    match o.kind { Kind::Sphere => norm(pos - o.center), Kind::Plane => o.norm }
}

// ---------- Lumières & scène ----------
const LIGHTS: [(V, Col); 4] = [
    (v(-2.0, 2.5, 0.0),  c(0.49, 0.07, 0.07)),
    (v(1.5, 2.5, 1.5),   c(0.07, 0.07, 0.49)),
    (v(1.5, 2.5, -1.5),  c(0.07, 0.49, 0.071)),
    (v(0.0, 3.5, 0.0),   c(0.21, 0.21, 0.35)),
];

pub struct Scene { things: [Obj; 3], pos: V, forward: V, up: V, right: V }

pub fn make_scene(pos: V, forward: V, up: V, right: V, ball: V) -> Scene {
    Scene {
        things: [
            Obj { kind: Kind::Plane,  surf: Surf::Checker, center: v(0.,0.,0.), radius: 0.0, norm: v(0.,1.,0.), offset: 0.0 },
            Obj { kind: Kind::Sphere, surf: Surf::Shiny,   center: v(0.,1.,0.), radius: 1.0, norm: v(0.,0.,0.), offset: 0.0 },
            Obj { kind: Kind::Sphere, surf: Surf::Shiny,   center: ball,        radius: 0.5, norm: v(0.,0.,0.), offset: 0.0 },
        ],
        pos, forward, up, right,
    }
}

pub fn camera_create(pos: V, look_at: V) -> (V, V, V) {
    let forward = norm(look_at - pos);
    let down = v(0., -1., 0.);
    let right = norm(cross(forward, down)) * 1.5;
    let up = norm(cross(forward, right)) * 1.5;
    (forward, up, right)
}

// ---------- Ray tracing ----------
const MAXDEPTH: i32 = 5;
const SHADOW_EPS: f32 = 1e-4;

fn trace_ray(start: V, dir: V, sc: &Scene, depth: i32) -> Col {
    let mut nearest: i32 = -1;
    let mut nearest_dist = 0.0f32;
    for i in 0..3 {
        let d = intersect(&sc.things[i], start, dir);
        if d != 0.0 && (nearest < 0 || d < nearest_dist) { nearest = i as i32; nearest_dist = d; }
    }
    if nearest < 0 { return BG; }
    let hit = &sc.things[nearest as usize];
    let surf = hit.surf;

    let pos = dir * nearest_dist + start;
    let normal = normal_at(hit, pos);
    let reflect_dir = dir - normal * (2.0 * dot(normal, dir));

    let shadow_origin = pos + normal * SHADOW_EPS;
    let mut natural = BG;
    for (lpos, lcol) in LIGHTS.iter() {
        let ldis = *lpos - pos;
        let livec = norm(ldis);
        let mut neat = 0.0f32;
        for i in 0..3 {
            let inter = intersect(&sc.things[i], shadow_origin, livec);
            if inter != 0.0 && (neat == 0.0 || inter < neat) { neat = inter; }
        }
        let in_shadow = !((neat > vlen(ldis)) || (neat == 0.0));
        if in_shadow { continue; }

        let illum = dot(livec, normal);
        let lcolor = if illum > 0.0 { cscale(illum, *lcol) } else { BG };
        let spec = dot(livec, norm(reflect_dir));
        let scolor = if spec > 0.0 { cscale(spec.powf(surf_roughness(surf)), *lcol) } else { BG };
        natural = cadd(natural, cadd(cmul(surf_diffuse(surf, pos), lcolor), cmul(surf_specular(surf), scolor)));
    }

    let reflect_pos = pos + reflect_dir * 0.001;
    let reflect_color = if depth >= MAXDEPTH {
        c(0.5, 0.5, 0.5)
    } else {
        cscale(surf_reflect(surf, reflect_pos), trace_ray(reflect_pos, reflect_dir, sc, depth + 1))
    };
    cadd(natural, reflect_color)
}

#[inline]
unsafe fn render_rows(sc: &Scene, ptr: *mut u8, w: usize, h: usize, y0: usize, y1: usize) {
    let scale = 2.0 * h as f32;
    for y in y0..y1 {
        let recenter_y = -((y as f32) - h as f32 / 2.0) / scale;
        for x in 0..w {
            let recenter_x = (x as f32 - w as f32 / 2.0) / scale;
            let dir = norm(sc.forward + sc.right * recenter_x + sc.up * recenter_y);
            let col = trace_ray(sc.pos, dir, sc, 0);
            let i = (y * w + x) * 4;
            *ptr.add(i)     = (legal(col.r) * 255.0) as u8;
            *ptr.add(i + 1) = (legal(col.g) * 255.0) as u8;
            *ptr.add(i + 2) = (legal(col.b) * 255.0) as u8;
            *ptr.add(i + 3) = 255;
        }
    }
}

struct SendPtr(*mut u8);
unsafe impl Send for SendPtr {}
unsafe impl Sync for SendPtr {}

pub fn render(sc: &Scene, pixels: &mut [u8], w: usize, h: usize, nthreads: usize) {
    let ptr = pixels.as_mut_ptr();
    if nthreads <= 1 {
        unsafe { render_rows(sc, ptr, w, h, 0, h); }
        return;
    }
    const CHUNK: usize = 8;   // ordonnancement dynamique (= OpenMP schedule(dynamic,8))
    let next = AtomicUsize::new(0);
    let sp = SendPtr(ptr);
    std::thread::scope(|s| {
        for _ in 0..nthreads {
            let next = &next;
            let sp = &sp;
            s.spawn(move || loop {
                let y0 = next.fetch_add(CHUNK, Ordering::Relaxed);
                if y0 >= h { break; }
                let y1 = (y0 + CHUNK).min(h);
                unsafe { render_rows(sc, sp.0, w, h, y0, y1); }
            });
        }
    });
}
