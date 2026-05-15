using System.Drawing;
using System.Drawing.Imaging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RayTracer
{
    public class RayTracer
    {
        private const int MaxDepth = 5;
        private const double ShadowEpsilon = 1e-4;

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
            var reflectDir = d - (2 * Vector.Dot(normal, d)) * normal;

            var shadowOrigin = pos + ShadowEpsilon * normal;
            var naturalColor = Color.Background;
            foreach (var light in scene.Lights)
            {
                var ldis = light.Pos - pos;
                var livec = Vector.Norm(ldis);
                var testRay = new Ray { Start = shadowOrigin, Dir = livec };

                double neatIsect = 0;
                foreach (var thing in scene.Things)
                {
                    var inter = thing.Intersect(testRay);
                    if (inter != null && (neatIsect == 0 || inter.Dist < neatIsect))
                        neatIsect = inter.Dist;
                }
                var isInShadow = !((neatIsect > Vector.Mag(ldis)) || (neatIsect == 0));
                if (isInShadow) continue;

                var illum = Vector.Dot(livec, normal);
                var lcolor = illum > 0 ? Color.Times(illum, light.Color) : Color.Make(0, 0, 0);
                var specular = Vector.Dot(livec, Vector.Norm(reflectDir));
                var scolor = specular > 0
                    ? Color.Times(Math.Pow(specular, nearest.Thing.Surface.Roughness), light.Color)
                    : Color.Make(0, 0, 0);
                naturalColor = Color.Plus(naturalColor,
                    Color.Plus(Color.Times(nearest.Thing.Surface.Diffuse(pos), lcolor),
                               Color.Times(nearest.Thing.Surface.Specular(pos), scolor)));
            }

            var reflectPos = pos + .001 * reflectDir;
            var reflectColor = depth >= MaxDepth
                ? Color.Make(.5, .5, .5)
                : Color.Times(nearest.Thing.Surface.Reflect(reflectPos),
                              TraceRay(new Ray { Start = reflectPos, Dir = reflectDir }, scene, depth + 1));

            return Color.Plus(naturalColor, reflectColor);
        }

        internal void Render(Scene scene, CancellationToken cancellationToken = default)
        {
            var scale = 2.0 * screenHeight;
            var options = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.For(0, screenHeight, options, y =>
            {
                var recenterY = -(y - (screenHeight / 2.0)) / scale;
                for (int x = 0; x < screenWidth; x++)
                {
                    var recenterX = (x - (screenWidth / 2.0)) / scale;
                    var point = Vector.Norm(scene.Camera.Forward
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
                                    Norm = Vector.Make(0,1,0),
                                    Offset = 0,
                                    Surface = Surfaces.CheckerBoard
                                },
                                new Sphere() {
                                    Center = Vector.Make(0,1,0),
                                    Radius = 1,
                                    Surface = Surfaces.Shiny
                                },
                                new Sphere() {
                                    Center = Vector.Make(-1,.5,1.5),
                                    Radius = .5,
                                    Surface = Surfaces.Shiny
                                }},
                Lights = new Light[] { 
                                new Light() {
                                    Pos = Vector.Make(-2,2.5,0),
                                    Color = Color.Make(.49,.07,.07)
                                },
                                new Light() {
                                    Pos = Vector.Make(1.5,2.5,1.5),
                                    Color = Color.Make(.07,.07,.49)
                                },
                                new Light() {
                                    Pos = Vector.Make(1.5,2.5,-1.5),
                                    Color = Color.Make(.07,.49,.071)
                                },
                                new Light() {
                                    Pos = Vector.Make(0,3.5,0),
                                    Color = Color.Make(.21,.21,.35)
                                }},
                Camera = Camera.Create(Vector.Make(3, 2, 4), Vector.Make(-1, .5, 0))
            };
    }

    static class Surfaces
    {
        // Only works with X-Z plane.
        public static readonly Surface CheckerBoard =
            new Surface()
            {
                Diffuse = pos => ((Math.Floor(pos.Z) + Math.Floor(pos.X)) % 2 != 0)
                                    ? Color.Make(1, 1, 1)
                                    : Color.Make(0, 0, 0),
                Specular = pos => Color.Make(1, 1, 1),
                Reflect = pos => ((Math.Floor(pos.Z) + Math.Floor(pos.X)) % 2 != 0)
                                    ? .1
                                    : .7,
                Roughness = 150
            };


        public static readonly Surface Shiny =
            new Surface()
            {
                Diffuse = pos => Color.Make(1, 1, 1),
                Specular = pos => Color.Make(.5, .5, .5),
                Reflect = pos => .6,
                Roughness = 50
            };
    }

    internal readonly record struct Vector(double X, double Y, double Z)
    {
        public static Vector Make(double x, double y, double z) => new Vector(x, y, z);

        public static Vector operator +(Vector a, Vector b) => new Vector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector operator -(Vector a, Vector b) => new Vector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector operator *(double n, Vector v) => new Vector(v.X * n, v.Y * n, v.Z * n);

        public static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static double Mag(Vector v) => Math.Sqrt(Dot(v, v));

        public static Vector Norm(Vector v)
        {
            var m = Mag(v);
            return m == 0 ? new Vector(0, 0, 0) : (1.0 / m) * v;
        }

        public static Vector Cross(Vector a, Vector b) => new Vector(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
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
        public double Dist;
    }

    class Surface
    {
        public Func<Vector, Color> Diffuse;
        public Func<Vector, Color> Specular;
        public Func<Vector, double> Reflect;
        public double Roughness;
    }

    class Camera
    {
        public Vector Pos;
        public Vector Forward;
        public Vector Up;
        public Vector Right;

        public static Camera Create(Vector pos, Vector lookAt)
        {
            var forward = Vector.Norm(lookAt - pos);
            var down = new Vector(0, -1, 0);
            var right = 1.5 * Vector.Norm(Vector.Cross(forward, down));
            var up = 1.5 * Vector.Norm(Vector.Cross(forward, right));

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
        public double Radius;

        public override ISect Intersect(Ray ray)
        {
            var eo = Center - ray.Start;
            double v = Vector.Dot(eo, ray.Dir);
            double dist;
            if (v < 0)
            {
                dist = 0;
            }
            else
            {
                double disc = Math.Pow(Radius, 2) - (Vector.Dot(eo, eo) - Math.Pow(v, 2));
                dist = disc < 0 ? 0 : v - Math.Sqrt(disc);
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
            return Vector.Norm(pos - Center);
        }
    }

    class Plane : SceneObject
    {
        public Vector Norm;
        public double Offset;

        public override ISect Intersect(Ray ray)
        {
            double denom = Vector.Dot(Norm, ray.Dir);
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
