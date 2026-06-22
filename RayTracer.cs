using System;
using System.Threading;
using System.Threading.Tasks;
using Vector = System.Numerics.Vector3;

// Moteur de ray tracing, optimisé "à la C" sans changer l'algorithme :
//  - Ray est un struct (pile) — pas d'allocation par rayon.
//  - Les surfaces sont un enum + switch (appels statiques inlinables) au lieu de Func<>.
//  - Les objets sont un struct tagué (Obj/ObjKind) parcouru par switch — pas de
//    dispatch virtuel ni d'allocation d'ISect (Intersect renvoie une distance float).
// Résultat : ~92 % de la vitesse d'un portage C natif (cf. c/raytracer.c).
namespace RayTracer
{
    public class RayTracer
    {
        private const int MaxDepth = 5;
        private const float ShadowEpsilon = 1e-4f;

        private readonly int screenWidth, screenHeight;
        private readonly Action<int, int, int> setPixel;

        // setPixel reçoit un pixel 32 bits empaqueté en RGBA little-endian (R = octet bas),
        // au format PixelFormat.UncompressedR8G8B8A8 de Raylib.
        public RayTracer(int screenWidth, int screenHeight, Action<int, int, int> setPixel)
        {
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.setPixel = setPixel;
        }

        private Color TraceRay(Ray ray, Scene scene, int depth)
        {
            var things = scene.Things;
            int nearest = -1;
            float nearestDist = 0;
            for (int i = 0; i < things.Length; i++)
            {
                var dist = Obj.Intersect(in things[i], ray);
                if (dist != 0 && (nearest < 0 || dist < nearestDist)) { nearest = i; nearestDist = dist; }
            }
            if (nearest < 0) return Color.Background;

            ref var hit = ref things[nearest];
            var surf = hit.Surf;
            var d = ray.Dir;
            var pos = nearestDist * d + ray.Start;
            var normal = Obj.Normal(in hit, pos);
            var reflectDir = d - 2f * Vector.Dot(normal, d) * normal;

            var shadowOrigin = pos + ShadowEpsilon * normal;
            var naturalColor = Color.Background;
            foreach (var light in scene.Lights)
            {
                var ldis = light.Pos - pos;
                var livec = Vector.Normalize(ldis);
                var testRay = new Ray(shadowOrigin, livec);

                float neatIsect = 0;
                for (int i = 0; i < things.Length; i++)
                {
                    var inter = Obj.Intersect(in things[i], testRay);
                    if (inter != 0 && (neatIsect == 0 || inter < neatIsect)) neatIsect = inter;
                }
                var isInShadow = !((neatIsect > ldis.Length()) || (neatIsect == 0));
                if (isInShadow) continue;

                var illum = Vector.Dot(livec, normal);
                var lcolor = illum > 0 ? Color.Times(illum, light.Color) : Color.Make(0, 0, 0);
                var specular = Vector.Dot(livec, Vector.Normalize(reflectDir));
                var scolor = specular > 0
                    ? Color.Times(MathF.Pow(specular, Surf.Roughness(surf)), light.Color)
                    : Color.Make(0, 0, 0);
                naturalColor = Color.Plus(naturalColor,
                    Color.Plus(Color.Times(Surf.Diffuse(surf, pos), lcolor),
                               Color.Times(Surf.Specular(surf, pos), scolor)));
            }

            var reflectPos = pos + .001f * reflectDir;
            var reflectColor = depth >= MaxDepth
                ? Color.Make(.5, .5, .5)
                : Color.Times(Surf.Reflect(surf, reflectPos),
                              TraceRay(new Ray(reflectPos, reflectDir), scene, depth + 1));

            return Color.Plus(naturalColor, reflectColor);
        }

        internal void Render(Scene scene, CancellationToken cancellationToken = default)
        {
            var scale = 2f * screenHeight;
            var options = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.For(0, screenHeight, options, y =>
            {
                var recenterY = -(y - (screenHeight / 2f)) / scale;
                for (int x = 0; x < screenWidth; x++)
                {
                    var recenterX = (x - (screenWidth / 2f)) / scale;
                    var point = Vector.Normalize(scene.Camera.Forward
                        + recenterX * scene.Camera.Right + recenterY * scene.Camera.Up);
                    setPixel(x, y, TraceRay(new Ray(scene.Camera.Pos, point), scene, 0).ToRgba());
                }
            });
        }

        internal const float BouncingBallRadius = 0.5f;

        internal static readonly Light[] DefaultLights =
        {
            new Light { Pos = new Vector(-2,   2.5f,  0),    Color = Color.Make(.49, .07, .07) },
            new Light { Pos = new Vector( 1.5f, 2.5f, 1.5f), Color = Color.Make(.07, .07, .49) },
            new Light { Pos = new Vector( 1.5f, 2.5f, -1.5f),Color = Color.Make(.07, .49, .071) },
            new Light { Pos = new Vector( 0,   3.5f,  0),    Color = Color.Make(.21, .21, .35) }
        };

        internal static Scene CreateScene(Camera camera, Vector bouncingBallCenter) =>
            new Scene
            {
                Things = new[]
                {
                    new Obj { Kind = ObjKind.Plane,  Surf = SurfKind.Checker, Norm = new Vector(0, 1, 0), Offset = 0 },
                    new Obj { Kind = ObjKind.Sphere, Surf = SurfKind.Shiny,   Center = new Vector(0, 1, 0), Radius = 1f },
                    new Obj { Kind = ObjKind.Sphere, Surf = SurfKind.Shiny,   Center = bouncingBallCenter, Radius = BouncingBallRadius },
                },
                Lights = DefaultLights,
                Camera = camera
            };
    }

    enum SurfKind { Checker, Shiny }

    // Surfaces en switch (remplace les Func<Vector,...> : appels statiques inlinables).
    static class Surf
    {
        public static Color Diffuse(SurfKind s, Vector p) => s == SurfKind.Checker
            ? (((MathF.Floor(p.Z) + MathF.Floor(p.X)) % 2 != 0) ? new Color(1, 1, 1) : new Color(0, 0, 0))
            : new Color(1, 1, 1);
        public static Color Specular(SurfKind s, Vector p) =>
            s == SurfKind.Checker ? new Color(1, 1, 1) : new Color(.5f, .5f, .5f);
        public static float Reflect(SurfKind s, Vector p) => s == SurfKind.Checker
            ? (((MathF.Floor(p.Z) + MathF.Floor(p.X)) % 2 != 0) ? .1f : .7f) : .6f;
        public static float Roughness(SurfKind s) => s == SurfKind.Checker ? 150f : 50f;
    }

    enum ObjKind { Sphere, Plane }

    // Objet de scène tagué : un seul struct pour sphère et plan, sélectionné par switch
    // (pas de classe abstraite ni de dispatch virtuel). Intersect renvoie la distance, 0 = miss.
    struct Obj
    {
        public ObjKind Kind;
        public SurfKind Surf;
        public Vector Center; public float Radius;   // sphère
        public Vector Norm;   public float Offset;   // plan

        public static float Intersect(in Obj o, Ray ray)
        {
            if (o.Kind == ObjKind.Sphere)
            {
                var eo = o.Center - ray.Start;
                var v = Vector.Dot(eo, ray.Dir);
                if (v < 0) return 0;
                var disc = o.Radius * o.Radius - (Vector.Dot(eo, eo) - v * v);
                if (disc < 0) return 0;
                return v - MathF.Sqrt(disc);
            }
            else
            {
                var denom = Vector.Dot(o.Norm, ray.Dir);
                if (denom > 0) return 0;
                return (Vector.Dot(o.Norm, ray.Start) + o.Offset) / (-denom);
            }
        }

        public static Vector Normal(in Obj o, Vector pos) =>
            o.Kind == ObjKind.Sphere ? Vector.Normalize(pos - o.Center) : o.Norm;
    }

    // struct (pile) plutôt que class (tas) : supprime une allocation par rayon.
    readonly struct Ray
    {
        public readonly Vector Start, Dir;
        public Ray(Vector start, Vector dir) { Start = start; Dir = dir; }
    }

    public readonly record struct Color
    {
        public readonly float R, G, B;

        public Color(float r, float g, float b) { R = r; G = g; B = b; }

        // Surcharge d'écriture : accepte des littéraux double (.49, .07, …) stockés en float.
        public static Color Make(double r, double g, double b) => new Color((float)r, (float)g, (float)b);

        public static Color Times(float n, Color v) => new Color(n * v.R, n * v.G, n * v.B);
        public static Color Times(Color a, Color b) => new Color(a.R * b.R, a.G * b.G, a.B * b.B);
        public static Color Plus(Color a, Color b) => new Color(a.R + b.R, a.G + b.G, a.B + b.B);

        public static readonly Color Background = Make(0, 0, 0);

        private static float Legalize(float d) => d > 1 ? 1 : d < 0 ? 0 : d;

        // RGBA empaqueté little-endian (octet 0 = R), alpha opaque — format de la texture Raylib.
        public int ToRgba()
        {
            int r = (int)(Legalize(R) * 255), g = (int)(Legalize(G) * 255), b = (int)(Legalize(B) * 255);
            return (0xFF << 24) | (b << 16) | (g << 8) | r;
        }
    }

    struct Light { public Vector Pos; public Color Color; }

    class Camera
    {
        public Vector Pos, Forward, Up, Right;

        public static Camera Create(Vector pos, Vector lookAt)
        {
            var forward = Vector.Normalize(lookAt - pos);
            var down = new Vector(0, -1, 0);
            var right = 1.5f * Vector.Normalize(Vector.Cross(forward, down));
            var up = 1.5f * Vector.Normalize(Vector.Cross(forward, right));
            return new Camera() { Pos = pos, Forward = forward, Up = up, Right = right };
        }
    }

    class Scene
    {
        public Obj[] Things;
        public Light[] Lights;
        public Camera Camera;
    }
}
