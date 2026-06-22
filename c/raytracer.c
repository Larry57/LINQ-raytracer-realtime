// Benchmark headless du ray tracer C (moteur dans engine.h).
//
// Build : gcc -O2 -march=native -fopenmp raytracer.c -o raytracer -lm
// Run   : ./raytracer                      (tous les cœurs)
//         OMP_NUM_THREADS=1 ./raytracer     (mono-thread)
#include <stdio.h>
#include <stdlib.h>
#include <omp.h>
#include "engine.h"

static double now_ms(void) { return omp_get_wtime() * 1000.0; }

static void bench_one(int n) {
    int W = n, H = n;
    unsigned char *px = malloc((size_t)W * H * 4);
    Vec fwd, up, right;
    camera_create(v(3, 2, 4), v(0, 0.5f, 0), &fwd, &up, &right);
    Vec campos = v(3, 2, 4);

    const float P = 1.1f, BH = 1.3f, restY = 0.5f;
    const int warmup = 40, measure = 80;
    for (int i = 0; i < warmup; i++) {
        float t = fmodf(i * 0.016f, P);
        float h = 4 * BH * t * (P - t) / (P * P);
        Scene s = make_scene(campos, fwd, up, right, v(-1, restY + h, 1.5f));
        render(&s, px, W, H);
    }
    double t0 = now_ms();
    for (int i = 0; i < measure; i++) {
        float t = fmodf(i * 0.016f, P);
        float h = 4 * BH * t * (P - t) / (P * P);
        Scene s = make_scene(campos, fwd, up, right, v(-1, restY + h, 1.5f));
        render(&s, px, W, H);
    }
    double ms = (now_ms() - t0) / measure;
    printf("  %dx%d  %7.2f ms/frame   %6.1f fps\n", n, n, ms, 1000.0 / ms);
    free(px);
}

int main(void) {
    printf("Threads OpenMP = %d\n", omp_get_max_threads());
    bench_one(300);
    bench_one(600);
    return 0;
}
