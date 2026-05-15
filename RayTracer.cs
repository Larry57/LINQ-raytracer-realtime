using System.Drawing;
using System.Drawing.Imaging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vector = System.Numerics.Vector3;

namespace RayTracer
{
    public class RayTracer
    {
        private const int MaxDepth = 5;
        private const float ShadowEpsilon = 1e-4f;

        private readonly int screenWidth;
        private readonly int screenHeight;
        private readonly Action<int, int, System.Drawing.Color> setPixel;

        public RayTracer(int screenWidth, int screenHeight, Action<int, int, System.Drawing.Color> setPixel)
        {
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.setPixel = setPixel;
        }

        private Color TraceRay(Ray ray, Scene scene, int depth)
        {
            ISect nearest = null;
            foreach (var thing in scene.Things)
            {
                var isect = thing.Intersect(ray);
                if (isect != null && (nearest == null || isect.Dist < nearest.Dist))
                    nearest = isect;
            }
            if (nearest == null) return Color.Background;

            var d = nearest.Ray.Dir;
            var pos = nearest.Dist * d + nearest.Ray.Start;
            var normal = nearest.Thing.Normal(pos);
            var reflectDir = d - 2f * Vector.Dot(normal, d) * normal;

            var shadowOrigin = pos + ShadowEpsilon * normal;
            var naturalColor = Color.Background;
            foreach (var light in scene.Lights)
            {
                var ldis = light.Pos - pos;
                var livec = Vector.Normalize(ldis);
                var testRay = new Ray { Start = shadowOrigin, Dir = livec };

                float neatIsect = 0;
                foreach (var thing in scene.Things)
                {
                    var inter = thing.Intersect(testRay);
                    if (inter != null && (neatIsect == 0 || inter.Dist < neatIsect))
                        neatIsect = inter.Dist;
                }
                var isInShadow = !((neatIsect > ldis.Length()) || (neatIsect == 0));
                if (isInShadow) continue;

                var illum = Vector.Dot(livec, normal);
                var lcolor = illum > 0 ? Color.Times(illum, light.Color) : Color.Make(0, 0, 0);
                var specular = Vector.Dot(livec, Vector.Normalize(reflectDir));
                var scolor = specular > 0
                    ? Color.Times(MathF.Pow(specular, nearest.Thing.Surface.Roughness), light.Color)
                    : Color.Make(0, 0, 0);
                naturalColor = Color.Plus(naturalColor,
                    Color.Plus(Color.Times(nearest.Thing.Surface.Diffuse(pos), lcolor),
                               Color.Times(nearest.Thing.Surface.Specular(pos), scolor)));
            }

            var reflectPos = pos + .001f * reflectDir;
            var reflectColor = depth >= MaxDepth
                ? Color.Make(.5, .5, .5)
                : Color.Times(nearest.Thing.Surface.Reflect(reflectPos),
                              TraceRay(new Ray { Start = reflectPos, Dir = reflectDir }, scene, depth + 1));

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
                        + recenterX * scene.Camera.Right
                        + recenterY * scene.Camera.Up);
                    var ray = new Ray { Start = scene.Camera.Pos, Dir = point };
                    setPixel(x, y, TraceRay(ray, scene, 0).ToDrawingColor());
                }
            });
        }

        internal readonly Scene DefaultScene =
            new Scene()
            {
                Things = new SceneObject[] {
                                new Plane() {
                                    Norm = new Vector(0, 1, 0),
                                    Offset = 0,
                                    Surface = Surfaces.CheckerBoard
                                },
                                new Sphere() {
                                    Center = new Vector(0, 1, 0),
                                    Radius = 1f,
                                    Surface = Surfaces.Shiny
                                },
                                new Sphere() {
                                    Center = new Vector(-1, .5f, 1.5f),
                                    Radius = .5f,
                                    Surface = Surfaces.Shiny
                                }},
                Lights = new Light[] {
                                new Light() {
                                    Pos = new Vector(-2, 2.5f, 0),
                                    Color = Color.Make(.49, .07, .07)
                                },
                                new Light() {
                                    Pos = new Vector(1.5f, 2.5f, 1.5f),
                                    Color = Color.Make(.07, .07, .49)
                                },
                                new Light() {
                                    Pos = new Vector(1.5f, 2.5f, -1.5f),
                                    Color = Color.Make(.07, .49, .071)
                                },
                                new Light() {
                                    Pos = new Vector(0, 3.5f, 0),
                                    Color = Color.Make(.21, .21, .35)
                                }},
                Camera = Camera.Create(new Vector(3, 2, 4), new Vector(-1, .5f, 0))
            };
    }

    static class Surfaces
    {
        // Only works with X-Z plane.
        public static readonly Surface CheckerBoard =
            new Surface()
            {
                Diffuse = pos => ((MathF.Floor(pos.Z) + MathF.Floor(pos.X)) % 2 != 0)
                                    ? Color.Make(1, 1, 1)
                                    : Color.Make(0, 0, 0),
                Specular = pos => Color.Make(1, 1, 1),
                Reflect = pos => ((MathF.Floor(pos.Z) + MathF.Floor(pos.X)) % 2 != 0)
                                    ? .1f
                                    : .7f,
                Roughness = 150f
            };


        public static readonly Surface Shiny =
            new Surface()
            {
                Diffuse = pos => Color.Make(1, 1, 1),
                Specular = pos => Color.Make(.5, .5, .5),
                Reflect = pos => .6f,
                Roughness = 50f
            };
    }

    public class Color
    {
        public readonly double R;
        public readonly double G;
        public readonly double B;

        public Color(double r, double g, double b) { R = r; G = g; B = b; }

        public static Color Make(double r, double g, double b) { return new Color(r, g, b); }

        public static Color Times(double n, Color v)
        {
            return new Color(n * v.R, n * v.G, n * v.B);
        }
        public static Color Times(Color v1, Color v2)
        {
            return new Color(v1.R * v2.R, v1.G * v2.G, v1.B * v2.B);
        }

        public static Color Plus(Color v1, Color v2)
        {
            return new Color(v1.R + v2.R, v1.G + v2.G, v1.B + v2.B);
        }
        public static Color Minus(Color v1, Color v2)
        {
            return new Color(v1.R - v2.R, v1.G - v2.G, v1.B - v2.B);
        }

        public static readonly Color Background = Make(0, 0, 0);
        public static readonly Color DefaultColor = Make(0, 0, 0);

        private static double Legalize(double d)
        {
            return d > 1 ? 1 : d < 0 ? 0 : d;
        }

        public System.Drawing.Color ToDrawingColor()
        {
            return System.Drawing.Color.FromArgb((int)(Legalize(R) * 255), (int)(Legalize(G) * 255), (int)(Legalize(B) * 255));
        }

    }

    class Ray
    {
        public Vector Start;
        public Vector Dir;
    }

    class ISect
    {
        public SceneObject Thing;
        public Ray Ray;
        public float Dist;
    }

    class Surface
    {
        public Func<Vector, Color> Diffuse;
        public Func<Vector, Color> Specular;
        public Func<Vector, float> Reflect;
        public float Roughness;
    }

    class Camera
    {
        public Vector Pos;
        public Vector Forward;
        public Vector Up;
        public Vector Right;

        public static Camera Create(Vector pos, Vector lookAt)
        {
            var forward = Vector.Normalize(lookAt - pos);
            var down = new Vector(0, -1, 0);
            var right = 1.5f * Vector.Normalize(Vector.Cross(forward, down));
            var up = 1.5f * Vector.Normalize(Vector.Cross(forward, right));

            return new Camera() { Pos = pos, Forward = forward, Up = up, Right = right };
        }
    }

    class Light
    {
        public Vector Pos;
        public Color Color;
    }

    abstract class SceneObject
    {
        public Surface Surface;
        public abstract ISect Intersect(Ray ray);
        public abstract Vector Normal(Vector pos);
    }

    class Sphere : SceneObject
    {
        public Vector Center;
        public float Radius;

        public override ISect Intersect(Ray ray)
        {
            var eo = Center - ray.Start;
            var v = Vector.Dot(eo, ray.Dir);
            float dist;
            if (v < 0)
            {
                dist = 0;
            }
            else
            {
                var disc = Radius * Radius - (Vector.Dot(eo, eo) - v * v);
                dist = disc < 0 ? 0 : v - MathF.Sqrt(disc);
            }
            if (dist == 0) return null;
            return new ISect()
                   {
                       Thing = this,
                       Ray = ray,
                       Dist = dist
                   };
        }

        public override Vector Normal(Vector pos)
        {
            return Vector.Normalize(pos - Center);
        }
    }

    class Plane : SceneObject
    {
        public Vector Norm;
        public float Offset;

        public override ISect Intersect(Ray ray)
        {
            var denom = Vector.Dot(Norm, ray.Dir);
            if (denom > 0) return null;
            return new ISect()
                   {
                       Thing = this,
                       Ray = ray,
                       Dist = (Vector.Dot(Norm, ray.Start) + Offset) / (-denom)
                   };
        }

        public override Vector Normal(Vector pos)
        {
            return Norm;
        }
    }

    class Scene
    {
        public SceneObject[] Things;
        public Light[] Lights;
        public Camera Camera;
    }

    public class RayTracerForm : Form
    {
        const int InitialWidth = 600;
        const int InitialHeight = 600;
        const int ResizeDebounceMs = 150;

        readonly PictureBox pictureBox;
        readonly System.Windows.Forms.Timer resizeDebounce;
        readonly System.Windows.Forms.Timer flushTimer;

        Bitmap bitmap;
        int[] pixelBuffer;
        int bufferWidth, bufferHeight;
        CancellationTokenSource renderCts;

        public RayTracerForm()
        {
            pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = System.Drawing.Color.Black
            };
            Controls.Add(pictureBox);

            ClientSize = new System.Drawing.Size(InitialWidth, InitialHeight);
            MinimumSize = new System.Drawing.Size(120, 120);
            Text = "Ray Tracer";
            DoubleBuffered = true;

            resizeDebounce = new System.Windows.Forms.Timer { Interval = ResizeDebounceMs };
            resizeDebounce.Tick += (_, __) => { resizeDebounce.Stop(); StartRender(); };

            flushTimer = new System.Windows.Forms.Timer { Interval = 100 };
            flushTimer.Tick += (_, __) => FlushBuffer();

            Load += (_, __) => StartRender();
            ClientSizeChanged += OnClientSizeChanged;
        }

        void OnClientSizeChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated) return;
            if (WindowState == FormWindowState.Minimized) return;
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            resizeDebounce.Stop();
            resizeDebounce.Start();
        }

        void StartRender()
        {
            renderCts?.Cancel();
            flushTimer.Stop();

            var w = ClientSize.Width;
            var h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            bufferWidth = w;
            bufferHeight = h;

            var oldBitmap = bitmap;
            bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            pixelBuffer = new int[w * h];
            pictureBox.Image = bitmap;
            oldBitmap?.Dispose();

            var cts = new CancellationTokenSource();
            renderCts = cts;
            var buf = pixelBuffer;
            var token = cts.Token;

            flushTimer.Start();
            var sw = Stopwatch.StartNew();

            Task.Run(() =>
            {
                var rt = new RayTracer(w, h, (x, y, color) =>
                {
                    if (token.IsCancellationRequested) return;
                    buf[y * w + x] = color.ToArgb();
                });
                rt.Render(rt.DefaultScene, token);
            }, token).ContinueWith(t =>
            {
                if (token.IsCancellationRequested || t.IsCanceled || t.IsFaulted) return;
                flushTimer.Stop();
                FlushBuffer();
                sw.Stop();
                Text = $"Ray Tracer — {w}×{h} — {sw.ElapsedMilliseconds} ms ({Environment.ProcessorCount} threads)";
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void FlushBuffer()
        {
            var bmp = bitmap;
            var buf = pixelBuffer;
            if (bmp == null || buf == null) return;
            var data = bmp.LockBits(
                new Rectangle(0, 0, bufferWidth, bufferHeight),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            bmp.UnlockBits(data);
            pictureBox.Invalidate();
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new RayTracerForm());
        }
    }
}
