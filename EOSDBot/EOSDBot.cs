using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EOSDBot
{
    public partial class EOSDBot : Form
    {
        public EOSDBot()
        {
            InitializeComponent();
        }

        private static readonly Size SUPPOSED_GAME_SIZE = new Size(640, 480);
        private static readonly Rectangle SUPPOSED_BOX = new Rectangle(33, 17, 384, 448);

        private readonly IntPtr PTR_DEATHS = new IntPtr(0x0069BCC0);
        private readonly IntPtr PTR_X_POS = new IntPtr(0x006CAA68);
        private readonly IntPtr PTR_Y_POS = new IntPtr(0x006CAA6C);
        private readonly IntPtr PTR_BOSS_X_POS = new IntPtr(0x004B8928);
        //private readonly IntPtr PTR_DEATHS = new IntPtr(0x0164CFA4);
        //private readonly IntPtr PTR_X_POS = new IntPtr(0x017D6110);
        //private readonly IntPtr PTR_Y_POS = new IntPtr(0x017D6114);

        private Bitmap screenData;
        private bool[] fieldData;
        private static IntPtr hWnd;
        private static IntPtr hndl;
        private float px, py;

        private Rectangle screenRegion;

        private Point _goalPoint = Point.Empty;

        private static readonly ushort DODGE_BLOCK_WIDTH = (ushort)(SUPPOSED_BOX.Width / HITBOX_RADIUS);
        private static readonly ushort DODGE_BLOCK_HEIGHT = (ushort)(SUPPOSED_BOX.Height / HITBOX_RADIUS);
        private readonly byte[] _safetyBlocks = new byte[DODGE_BLOCK_HEIGHT * DODGE_BLOCK_WIDTH];

        private void EOSDBot_Load(object sender, EventArgs e)
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.DoubleBuffer, true);

            Process[] processes = Process.GetProcessesByName("東方紅魔郷");
            if (processes.Length < 1)
            {
                Console.WriteLine("Process '東方紅魔郷.exe' is not running!");
                Environment.Exit(0);
            }
            Process process = processes[0];
            hWnd = process.MainWindowHandle;
            hndl = Memory.GetProcessHandle(process);
            Console.WriteLine(hWnd);

            Memory.SetForegroundWindow(hWnd);
            UpdateScreenPosition();
            screenData = new Bitmap(SUPPOSED_BOX.Width, SUPPOSED_BOX.Height, PixelFormat.Format32bppPArgb);
            fieldData = new bool[SUPPOSED_BOX.Width * SUPPOSED_BOX.Height];
            StartBot();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (screenData != null)
            {
                g.DrawImageUnscaled(screenData, 0, 0);
            }
            var font = new Font(FontFamily.GenericMonospace, 10);
            g.DrawString(px + ", " + py, font, Brushes.Black, 10, 10);
            g.DrawString(_goalPoint.ToString(), font, Brushes.Black, 10, 30);
            g.FillEllipse(Brushes.LawnGreen, px - 2, py - 2, 4, 4);

            for (int y = 0; y < DODGE_BLOCK_HEIGHT; y++)
            {
                for (int x = 0; x < DODGE_BLOCK_WIDTH; x++)
                {
                    if (_safetyBlocks[y * DODGE_BLOCK_WIDTH + x] > BULLET_IGNORE)
                        g.FillRectangle(Brushes.Blue, x * HITBOX_RADIUS, y * HITBOX_RADIUS, HITBOX_RADIUS, HITBOX_RADIUS);
                }
            }
            foreach (Circle c in currentFrameObjects)
            {
                g.FillEllipse(Brushes.Red, (float)(c.Center.x - c.Radius), (float)(c.Center.y - c.Radius), c.Radius * 2, c.Radius * 2);
                g.DrawLine(new Pen(Brushes.Green, c.Radius * 2), (float)c.Center.x, (float)c.Center.y, (float)(c.Center.x + c.Velocity.x * PREDICTION_RANGE), (float)(c.Center.y + c.Velocity.y * PREDICTION_RANGE));
            }
            g.FillRectangle(Brushes.Crimson, _goalPoint.X, _goalPoint.Y, HITBOX_RADIUS, HITBOX_RADIUS);
            g.DrawLine(Pens.Blue, 0, py, this.Width, py);
        }

        const int PREDICTION_RANGE = 2;
        public void StartBot()
        {
            // Screen capturing
            new Thread(() =>
            {
                while (!this.IsDisposed)
                {
                    Bitmap scrn;
                    using (Bitmap tmpImage = Screenfetch.TakeScreenshot(screenRegion))
                        scrn = Screenfetch.RescaleBitmap(tmpImage, SUPPOSED_BOX.Size);
                    RasterizeRegions(scrn);
                    scrn.Dispose();

                    UpdatePosition();
                    CountIntersections();
                    UpdateGoal();

                    //CreateObjects();

                    Invalidate();
                    Thread.Sleep(10);
                }

            }, int.MaxValue / 8).Start(); // Make the stack just a bit bigger

            const double MOVE_EPSILON = 1;

            // Motion control
            new Thread(() =>
            {
                while (!IsDisposed)
                {
                    DInput.SendKey(0x2C, DInput.KEYEVENTF_SCANCODE);
                    while (px - _goalPoint.X < -MOVE_EPSILON)
                    {
                        DInput.SendKey(0x4D, DInput.KEYEVENTF_SCANCODE);
                        Thread.Sleep(1);
                        UpdatePosition();
                    }
                    DInput.SendKey(0x4D, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);

                    while (px - _goalPoint.X > MOVE_EPSILON)
                    {
                        DInput.SendKey(0x4B, DInput.KEYEVENTF_SCANCODE);
                        Thread.Sleep(1);
                        UpdatePosition();
                    }
                    DInput.SendKey(0x4B, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);

                    while (py - _goalPoint.Y > MOVE_EPSILON)
                    {
                        DInput.SendKey(0x48, DInput.KEYEVENTF_SCANCODE);
                        Thread.Sleep(1);
                        UpdatePosition();
                    }
                    DInput.SendKey(0x48, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);

                    while (py - _goalPoint.Y < -MOVE_EPSILON)
                    {
                        DInput.SendKey(0x50, DInput.KEYEVENTF_SCANCODE);
                        Thread.Sleep(1);
                        UpdatePosition();
                    }
                    DInput.SendKey(0x50, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
                    Thread.Sleep(5);
                }
            }).Start();

            // Deathbombing
            new Thread(() =>
            {
                byte lastDeaths;
                Memory.ReadMemoryByte(hndl, PTR_DEATHS, out lastDeaths);
                while (!IsDisposed)
                {
                    byte deaths;
                    Memory.ReadMemoryByte(hndl, PTR_DEATHS, out deaths);
                    if (deaths > lastDeaths)
                    {
                        Console.WriteLine("DEATHBOMB");
                        Bomb();
                    }
                    lastDeaths = deaths;
                    Thread.Sleep(10);
                }
            }).Start();
        }

        public void UpdateScreenPosition()
        {
            Memory.RECT rect;
            Memory.GetWindowRect(hWnd, out rect);

            var topLeft = new Memory.POINT(0, 0);
            Memory.ClientToScreen(hWnd, ref topLeft);

            int width = rect.Width - 2 * (topLeft.X - rect.Left);
            int height = rect.Height - (topLeft.Y - rect.Top);

            double ratX = 1.0 * width / SUPPOSED_GAME_SIZE.Width;
            double ratY = 1.0 * height / SUPPOSED_GAME_SIZE.Height;

            var actualScreen = new Rectangle(
                new Point((int)(topLeft.X + SUPPOSED_BOX.X * ratX), (int)(topLeft.Y + SUPPOSED_BOX.Y * ratY)),
                new Size((int)(SUPPOSED_BOX.Width * ratX), (int)(SUPPOSED_BOX.Height * ratY)));
            this.screenRegion = actualScreen;
        }

        public void Bomb()
        {
            DInput.SendKey(0x4B, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
            DInput.SendKey(0x4D, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
            DInput.SendKey(0x2C, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
            Thread.Sleep(20);
            DInput.SendKey(0x2D, DInput.KEYEVENTF_SCANCODE);
            Thread.Sleep(20);
            DInput.SendKey(0x2D, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
        }

        private class Circle
        {
            public Vec2 Center { get; private set; }
            public int Radius { get; set; }
            public Vec2 Velocity { get; set; }

            public Circle(Vec2 Center, int Radius)
            {
                this.Center = Center;
                this.Radius = Radius;
            }
        }

        private List<Circle> lastFrameObjects = new List<Circle>();
        private List<Circle> currentFrameObjects = new List<Circle>();

        private static readonly short[,] AdjacentDirections = { { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 } };
        const double MINIMUM_DENSITY = 0.3;
        const int MINIMUM_RADIUS = 2;
        const float MAX_DISTANCE = 10;
        const float FIX_THRESHOLD = 1f;

        public void CreateObjects()
        {
            bool[] fieldDataCopy = fieldData.Clone() as bool[];
            lastFrameObjects = currentFrameObjects;
            currentFrameObjects = new List<Circle>();
            short height = (short)screenData.Height;
            short width = (short)screenData.Width;
            for (short y = 0; y < height; y++)
            {
                for (short x = 0; x < width; x++)
                {
                    if (fieldDataCopy[y * width + x])
                    {
                        short minX = width;
                        short maxX = 0;
                        short minY = height;
                        short maxY = 0;
                        short calculatedArea = 0;
                        FloodFill(fieldDataCopy, x, y, ref width, ref height, ref calculatedArea, ref minX, ref maxX, ref minY, ref maxY);
                        int radius = Math.Max(maxX - minX, maxY - minY) / 2;
                        Vec2 center = new Vec2((minX + maxX) / 2.0, (minY + maxY) / 2.0);
                        Circle circle = new Circle(center, radius);
                        double filledArea = Math.PI * radius * radius;
                        if (calculatedArea / filledArea > MINIMUM_DENSITY && radius > MINIMUM_RADIUS)
                            currentFrameObjects.Add(circle);
                    }
                }
            }
            foreach (Circle c in currentFrameObjects)
            {
                float minDist = float.MaxValue;
                Circle minCircle = null;
                foreach (Circle p in lastFrameObjects)
                {
                    float dist =
                        (float)Math.Sqrt((c.Center.x - p.Center.x) * (c.Center.x - p.Center.x) +
                                          (c.Center.y - p.Center.y) * (c.Center.y - p.Center.y));
                    if (dist < minDist)
                    {
                        minCircle = p;
                        minDist = dist;
                    }
                }
                if (minCircle != null && minDist < MAX_DISTANCE)
                {
                    Vec2 newVel = c.Center - minCircle.Center;
                    Vec2 diff = newVel - minCircle.Velocity;
                    if (diff.Length() > FIX_THRESHOLD)
                        c.Velocity = newVel;
                    else
                        c.Velocity = minCircle.Velocity;
                    
                }
            }
        }

        public static void FloodFill(bool[] arr, short x, short y, ref short width, ref short height, ref short density,
            ref short minX, ref short maxX, ref short minY, ref short maxY)
        {
            arr[x + y * width] = false;
            density++;
            for (int d = 0; d < 4; d++)
            {
                short adjacentX = (short)(x + AdjacentDirections[d, 0]);
                short adjacentY = (short)(y + AdjacentDirections[d, 1]);
                if (adjacentX < 0 || adjacentX >= width || adjacentY < 0 || adjacentY >= height) continue;
                if (!arr[adjacentX + adjacentY * width]) continue;
                if (adjacentX < minX)
                    minX = adjacentX;
                if (adjacentX > maxX)
                    maxX = adjacentX;
                if (adjacentY < minY)
                    minY = adjacentY;
                if (adjacentY > maxY)
                    maxY = adjacentY;
                arr[adjacentX + adjacentY * width] = false;
                FloodFill(arr, adjacentX, adjacentY, ref width, ref height, ref density, ref minX, ref maxX, ref minY, ref maxY);
            }
        }
        private const int HITBOX_RADIUS = 2;
        private const int BULLET_IGNORE = 1;
        private const int LOOK_UP = 7;
        private const int LOOK_BACK = 4;
        private const int LOOK_SIDES = 4;
        private const int CHECK_RADIUS = 2;

        public void UpdateGoal()
        {
            ushort cpx = (ushort)(px / HITBOX_RADIUS);
            ushort cpy = (ushort)(py / HITBOX_RADIUS);
            double minD = double.MaxValue;
            int cax = 0;
            int cay = 0;
            bool empty = true;


            ushort cx = (ushort)Math.Max(0, cpx - CHECK_RADIUS);
            ushort cy = (ushort)Math.Max(0, cpy - CHECK_RADIUS);
            ushort dcx = (ushort)Math.Min(cpx + CHECK_RADIUS, DODGE_BLOCK_WIDTH);
            ushort dcy = (ushort)Math.Min(cpy + CHECK_RADIUS, DODGE_BLOCK_HEIGHT);

            for (ushort x = cx; x < dcx; x++)
            {
                for (ushort y = cy; y < dcy; y++)
                {
                    int vl = _safetyBlocks[y * DODGE_BLOCK_WIDTH + x];
                    if (vl > BULLET_IGNORE) continue;
                    bool ok = true;
                    for (short dy = -LOOK_UP; dy < LOOK_BACK; dy++)
                    {
                        for (short dx = -LOOK_SIDES; dx < LOOK_SIDES; dx++)
                        {
                            if (y + dy < 0 || y + dy >= DODGE_BLOCK_HEIGHT || x + dx < 0 || x + dx >= DODGE_BLOCK_WIDTH) continue;
                            byte dvl = _safetyBlocks[(y + dy) * DODGE_BLOCK_WIDTH + x + dx];
                            if (dvl <= BULLET_IGNORE) continue;
                            ok = false;
                            goto end;
                        }
                    }
                    end:
                    if (ok)
                    {
                        short d = (short)Isqrt((x - cpx) * (x - cpx) + (y - cpy) * (y - cpy));
                        if (!(d < minD)) continue;
                        minD = d;
                        cax = x;
                        cay = y;
                    }
                    else
                    {
                        empty = false;
                    }
                }
            }

            _goalPoint.X = cax * HITBOX_RADIUS;
            _goalPoint.Y = cay * HITBOX_RADIUS;
            if (!empty) return;
            float bossX;
            Memory.ReadMemoryFloat(hndl, PTR_BOSS_X_POS, out bossX);
            if (bossX != 32)
                _goalPoint.X = (int)bossX;
            else
                _goalPoint.X = screenData.Width / 2;
            _goalPoint.Y = screenData.Height * 3 / 4;
        }

        public static int Isqrt(int num)
        {
            if (0 == num) { return 0; }
            int n = (num / 2) + 1;
            int n1 = (n + (num / n)) / 2;
            while (n1 < n)
            {
                n = n1;
                n1 = (n + (num / n)) / 2;
            }
            return n;
        }

        public void CountIntersections()
        {

            for (ushort y = 0; y < DODGE_BLOCK_HEIGHT; y++)
                for (ushort x = 0; x < DODGE_BLOCK_WIDTH; x++)
                    _safetyBlocks[y * DODGE_BLOCK_WIDTH + x] = 0;

            ushort cx = (ushort)Math.Max(0, px / HITBOX_RADIUS - CHECK_RADIUS);
            ushort cy = (ushort)Math.Max(0, py / HITBOX_RADIUS - CHECK_RADIUS);
            ushort dcx = (ushort)Math.Min(px / HITBOX_RADIUS + CHECK_RADIUS, DODGE_BLOCK_WIDTH);
            ushort dcy = (ushort)Math.Min(py / HITBOX_RADIUS + CHECK_RADIUS, DODGE_BLOCK_HEIGHT);

            int width = screenData.Width;
            for (ushort y = cy; y < dcy; y++)
            {
                for (ushort x = cx; x < dcx; x++)
                {
                    for (byte i = 0; i < HITBOX_RADIUS; i++)
                    {
                        for (byte j = 0; j < HITBOX_RADIUS; j++)
                        {
                            if (fieldData[x * HITBOX_RADIUS + j + (y * HITBOX_RADIUS + i) * width])
                                _safetyBlocks[y * DODGE_BLOCK_WIDTH + x]++;
                        }
                    }
                }
            }
        }

        public void UpdatePosition()
        {
            Memory.ReadMemoryFloat(hndl, PTR_X_POS, out px);
            Memory.ReadMemoryFloat(hndl, PTR_Y_POS, out py);
        }

        public unsafe void RasterizeRegions(Bitmap scrn)
        {
            var lockBits = scrn.LockBits(new Rectangle(0, 0, scrn.Width, scrn.Height), ImageLockMode.ReadOnly, scrn.PixelFormat);
            var regionData = screenData.LockBits(new Rectangle(0, 0, screenData.Width, screenData.Height), ImageLockMode.ReadWrite, screenData.PixelFormat);
            int height = screenData.Height;
            int width = screenData.Width;
            for (ushort y = 0; y < height; y++)
            {
                byte* row = (byte*)lockBits.Scan0 + (y * lockBits.Stride);
                for (ushort x = 0; x < width; x++)
                {
                    int b = row[x * 4];
                    int g = row[x * 4 + 1];
                    int r = row[x * 4 + 2];

                    byte* pix = (byte*)(regionData.Scan0 + y * regionData.Stride + x * 4);
                    if (GetBrightness(r, g, b) > 254)
                    {
                        pix[0] = 200;
                        pix[1] = 200;
                        pix[2] = 200;
                        pix[3] = 255;
                        fieldData[x + y * width] = true;
                    }
                    else
                    {
                        pix[0] = 255;
                        pix[1] = 255;
                        pix[2] = 255;
                        pix[3] = 255;
                        fieldData[x + y * width] = false;
                    }
                }
            }
            scrn.UnlockBits(lockBits);
            screenData.UnlockBits(regionData);
        }

        public static double GetBrightness(int r, int g, int b)
        {
            return (r + g + b) / 3.0;
        }
    }
}
