# 🏁 La Grande Course — un ray tracer, six incarnations

> *Comment une innocente question — « ça marcherait sous Linux ? » — a fini en
> tournoi de performance entre C#, JavaScript, C, Rust et un GPU, un soir de fête.* 🍾

Au départ : un petit ray tracer temps réel en **C# / Windows Forms** (un sol en
damier, une grosse sphère brillante, une petite qui rebondit, caméra orbitale).
À l'arrivée : le **même rendu, le même algorithme**, porté dans tous les sens
pour répondre à une seule question — **qu'est-ce qui fait *vraiment* la
performance ?**

Spoiler : **ce n'est presque jamais le langage.** C'est la plomberie (allocations,
indirections, ordonnancement) et le matériel.

---

## 🏆 Le podium (600×600, Intel Core Ultra 9 185H, 22 threads)

| Rang | Implémentation | mono-thread | multi-thread (22) |
|:----:|----------------|------------:|------------------:|
| 🥇 | **Rust** (raylib FFI) | **28,1 fps** | **148 fps** |
| 🥈 | **C** (gcc + OpenMP) | 22,1 fps | 124 fps |
| 🥉 | **C#** (.NET 10, alloc-free) | 20,4 fps | 103 fps |
| 4 | **JavaScript** (V8 + Web Workers) | ~8,5 fps | 38 fps |
| — | **GPU** (shader GLSL, hôte C *ou* C#) | — | **~4500 fps** 🔥 |

Le GPU est **hors-concours** : ~37× le meilleur CPU. Normal — le ray tracing,
c'est *fait* pour lui (un thread par pixel, 360 000 pixels indépendants).

Le shader GLSL est **le même quel que soit l'hôte** : l'hôte ne fait que pousser
les uniforms et dessiner un rectangle plein écran (travail négligeable). On a donc
deux hôtes interchangeables — un en **C** (`c/gpu.c`) et un en **C#**
(`gpu-cs/`) — qui chargent à l'identique `c/shader.fs` et tournent au même framerate.

---

## 📖 Les leçons de la soirée

1. **Les allocations tuent.** Le C# de départ allouait un objet `Ray` + un `ISect`
   *par rayon* (et il y en a des millions). Les passer en `struct` (pile, zéro GC) :
   **+65 % de FPS**, sans toucher à l'algo.
2. **À thread égal, C# ≈ JavaScript ≈ ~90 % du C.** V8 et .NET sont redoutables.
   Le managé n'est *pas* le boulet qu'on imagine.
3. **`AggressiveInlining` et la suppression du délégué `setPixel` : effet nul.**
   Le JIT inline déjà, et le goulot multi-thread, c'est la bande passante mémoire,
   pas l'appel par pixel. (On a désigné le mauvais coupable, et la mesure l'a dit.)
4. **L'ordonnancement compte autant que le langage.** Le premier Rust découpait
   l'image en bandes contiguës → déséquilibre de charge (le ciel est gratuit, le
   sol coûte cher) → scaling minable (×2,8). Passage à un ordonnancement
   **dynamique** (comme `OpenMP schedule(dynamic,8)`) → ×5,3, et Rust repasse devant.
5. **Pourquoi Rust > C ici ?** Même `f32`, même `-march=native`. La différence :
   le backend **LLVM** vectorise mieux que GCC sur ce code, et surtout l'**aliasing** —
   le `&mut` de Rust garantit au compilateur qu'aucun autre pointeur ne touche la
   mémoire (`noalias`), ce que le C ne peut pas promettre. Plus de libertés
   d'optimisation → meilleur code.
6. **Le bon outil écrase tout.** Toutes les versions CPU plafonnent à ~100-150 fps.
   Le GPU fait 4500. Le débit massif > la latence de quelques gros cœurs, quand le
   problème est parallèle.

> **TL;DR :** GPU ≫≫ Rust > C > C# > JS.
> Course CPU à code écrit pareil : le natif mène, le managé suit à ~10-20 %,
> et **Rust coiffe tout le monde** grâce à LLVM + l'aliasing.

---

## ▶️ Lancer chaque version

| Version | Dossier | Commande | Fenêtre |
|---------|---------|----------|---------|
| C# | racine | `dotnet run -c Release` | ✅ Raylib-cs |
| JS (mono-thread) | `web/` | ouvrir `web/index.html` | ✅ `<canvas>` |
| JS (multi-thread) | `web/` | `python3 web/serve.py` → http://localhost:8000/index-mt.html | ✅ `<canvas>` |
| C | `c/` | `gcc -O2 -march=native -fopenmp c/viewer.c -o c/viewer $(pkg-config --cflags --libs raylib) -lm` puis `./c/viewer` | ✅ raylib |
| Rust | `rust/` | `cargo build --release` puis `./rust/target/release/viewer` | ✅ raylib (FFI) |
| GPU (C) | `c/` | `gcc -O2 c/gpu.c -o c/gpu $(pkg-config --cflags --libs raylib) -lm` puis `./c/gpu` | ✅🔥 shader |
| GPU (C#) | `gpu-cs/` | `dotnet run --project gpu-cs/RayTracerGpu.csproj -c Release` | ✅🔥 shader |

Et les **benchmarks headless** (mêmes chiffres que le podium) :

```sh
# C    : ./c/raytracer          (OMP_NUM_THREADS=1 pour le mono)
# Rust : ./rust/target/release/rtbench   (RT_THREADS=1 pour le mono)
```

Prérequis : .NET 10 SDK · gcc + OpenMP · `raylib-devel` (5.5) · rust/cargo · python3.

Contrôles communs : **clic gauche** orbiter · **clic droit** pan · **molette** zoom.

---

## 🙏 Crédits

- Code d'origine : [Luke Hoban — LINQ-raytracer](https://github.com/lukehoban/LINQ-raytracer)
- Métamorphose temps réel, portages multi-langages et soirée benchmark :
  griffonnés avec [Claude Code](https://claude.com/claude-code) 🦀😎

*« C'est presque de la triche. » — l'humain, en voyant le GPU à 4500 fps*
