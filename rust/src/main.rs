// Benchmark headless du ray tracer Rust (moteur dans lib.rs).
//
// Build : cargo build --release   (dans rust/)
// Run   : ./target/release/rtbench                      (tous les threads)
//         RT_THREADS=1 taskset -c 4 ./target/release/rtbench   (mono-thread)
use std::time::Instant;
use rtbench::{camera_create, make_scene, render, v};

fn bench_one(n: usize, nthreads: usize) {
    let (w, h) = (n, n);
    let mut buf = vec![0u8; w * h * 4];
    let (forward, up, right) = camera_create(v(3., 2., 4.), v(0., 0.5, 0.));

    const P: f32 = 1.1; const BH: f32 = 1.3; const REST_Y: f32 = 0.5;
    let scene_at = |frame: i32| {
        let t = (frame as f32 * 0.016) % P;
        let hh = 4.0 * BH * t * (P - t) / (P * P);
        make_scene(v(3., 2., 4.), forward, up, right, v(-1.0, REST_Y + hh, 1.5))
    };

    let (warmup, measure) = (40, 80);
    for i in 0..warmup { render(&scene_at(i), &mut buf, w, h, nthreads); }
    let t0 = Instant::now();
    for i in 0..measure { render(&scene_at(i), &mut buf, w, h, nthreads); }
    let ms = t0.elapsed().as_secs_f64() * 1000.0 / measure as f64;
    println!("  {}x{}  {:7.2} ms/frame   {:6.1} fps", n, n, ms, 1000.0 / ms);
}

fn main() {
    let nthreads = std::env::var("RT_THREADS").ok()
        .and_then(|s| s.parse::<usize>().ok())
        .unwrap_or_else(|| std::thread::available_parallelism().map(|n| n.get()).unwrap_or(1));
    println!("Threads = {}", nthreads);
    bench_one(300, nthreads);
    bench_one(600, nthreads);
}
