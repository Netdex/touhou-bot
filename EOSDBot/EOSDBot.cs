using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace EOSDBot
{
    public partial class EOSDBot : Form
    {
        public EOSDBot()
        {
            InitializeComponent();
            this.Subscribe();
        }

        private static readonly Size SUPPOSED_SIZE = new Size(384, 448);

        private readonly IntPtr PTR_DEATHS = new IntPtr(0x0069BCC0);
        private readonly IntPtr PTR_X_POS = new IntPtr(0x006CAA68);
        private readonly IntPtr PTR_Y_POS = new IntPtr(0x006CAA6C);

        private Bitmap screenData;
        private static IntPtr hWnd;
        private static IntPtr hndl;
        private float px, py;

        private Point first, second;
        private Rectangle screenRegion;

        private Point _goalPoint = Point.Empty;
        private const int DODGE_REGION = 4;
        private static readonly int DODGE_BLOCK_WIDTH = SUPPOSED_SIZE.Width / DODGE_REGION;
        private static readonly int DODGE_BLOCK_HEIGHT = SUPPOSED_SIZE.Height / DODGE_REGION;
        private readonly int[,] _safetyBlocks = new int[DODGE_BLOCK_HEIGHT, DODGE_BLOCK_WIDTH];

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
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            if (screenData != null)
            {
                g.DrawImageUnscaled(screenData, 0, 0);
            }
            Font font = new Font(FontFamily.GenericMonospace, 10);
            g.DrawString(px + ", " + py, font, Brushes.Black, 10, 10);
            g.DrawString(_goalPoint.ToString(), font, Brushes.Black, 10, 30);
            g.FillEllipse(Brushes.LawnGreen, px - 2, py - 2, 4, 4);

            for (int y = 0; y < DODGE_BLOCK_HEIGHT; y++)
            {
                for (int x = 0; x < DODGE_BLOCK_WIDTH; x++)
                {
                    if (_safetyBlocks[y, x] > BULLET_IGNORE)
                        g.FillRectangle(Brushes.Blue, x * DODGE_REGION, y * DODGE_REGION, DODGE_REGION, DODGE_REGION);
                }
            }
            g.FillRectangle(Brushes.Crimson, _goalPoint.X, _goalPoint.Y, DODGE_REGION, DODGE_REGION);
            g.DrawLine(Pens.Blue, 0, py, this.Width, py);
        }

        public void StartBot()
        {
            // Screen capturing
            new Thread(() =>
            {
                while (true)
                {
                    Bitmap scrn;
                    using (Bitmap tmpImage = Screenfetch.TakeScreenshot(screenRegion))
                        scrn = Screenfetch.RescaleBitmap(tmpImage, SUPPOSED_SIZE);
                    if (screenData == null)
                        screenData = new Bitmap(scrn.Width, scrn.Height, PixelFormat.Format32bppPArgb);
                    RasterizeRegions(scrn, screenData);
                    scrn.Dispose();

                    UpdatePosition();
                    CalculateColumns();
                    UpdateGoal();

                    Invalidate();
                    Thread.Sleep(2);
                }

            }).Start();

            const double MOVE_EPSILON = 1;

            // Motion control
            new Thread(() =>
            {
                while (true)
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

                    Thread.Sleep(2);
                }
            }).Start();

            // Deathbombing
            new Thread(() =>
            {
                byte lastDeaths;
                Memory.ReadMemoryByte(hndl, PTR_DEATHS, out lastDeaths);
                while (true)
                {
                    byte deaths;
                    Memory.ReadMemoryByte(hndl, PTR_DEATHS, out deaths);
                    if (deaths > lastDeaths)
                    {
                        Console.WriteLine("DEATHBOMB");
                        Bomb();
                    }
                    lastDeaths = deaths;
                    Thread.Sleep(2);
                }
            }).Start();
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

        const int BULLET_IGNORE = 5;
        const int LOOK_UP = 7;
        const int LOOK_BACK = 3;
        const int LOOK_SIDES = 2;

        public void UpdateGoal()
        {
            int cpx = (int)(px / DODGE_REGION);
            int cpy = (int)(py / DODGE_REGION);
            double minD = double.MaxValue;
            int cax = 0;
            int cay = 0;
            bool empty = true;
            for (int y = Math.Max(0, cpy - 20); y < Math.Min(cpy + 20, DODGE_BLOCK_HEIGHT); y++)
            {
                for (int x = Math.Max(0, cpx - 20); x < Math.Min(cpx + 20, DODGE_BLOCK_WIDTH); x++)
                {
                    int vl = _safetyBlocks[y, x];
                    if (vl <= BULLET_IGNORE)
                    {
                        bool ok = true;
                        for (int dy = -LOOK_UP; dy < LOOK_BACK; dy++)
                        {
                            for (int dx = -LOOK_SIDES; dx < LOOK_SIDES; dx++)
                            {
                                if (y + dy >= 0 && y + dy < DODGE_BLOCK_HEIGHT && x + dx >= 0 &&
                                    x + dx < DODGE_BLOCK_WIDTH)
                                {
                                    int dvl = _safetyBlocks[y + dy, x + dx];
                                    if (dvl > BULLET_IGNORE)
                                    {
                                        ok = false;
                                        goto end;
                                    }
                                }
                            }
                        }
                        end:
                        if (ok)
                        {
                            double d = Math.Sqrt((x - cpx)*(x - cpx) + (y - cpy)*(y - cpy));
                            if (d < minD)
                            {
                                minD = d;
                                cax = x;
                                cay = y;
                            }
                        }
                        else
                        {
                            empty = false;
                        }
                    }
                }
            }
            
            _goalPoint.X = cax * DODGE_REGION ;
            _goalPoint.Y = cay * DODGE_REGION ;
            if (empty)
            {
                _goalPoint.X = screenData.Width/2;
                _goalPoint.Y = screenData.Height*4/5;
            }
        }

        public unsafe void CalculateColumns()
        {
            BitmapData regionData = screenData.LockBits(new Rectangle(0, 0, screenData.Width, screenData.Height), ImageLockMode.ReadWrite, screenData.PixelFormat);
            for (int y = 0; y < DODGE_BLOCK_HEIGHT; y++)
            {
                for (int x = 0; x < DODGE_BLOCK_WIDTH; x++)
                {
                    _safetyBlocks[y, x] = 0;

                    for (int i = 0; i < DODGE_REGION; i++)
                    {
                        for (int j = 0; j < DODGE_REGION; j++)
                        {
                            byte* pix = (byte*)(regionData.Scan0 + (y * DODGE_REGION + i) * regionData.Stride + (x * DODGE_REGION + j) * 4);
                            if (pix[0] == 0)
                            {
                                _safetyBlocks[y, x]++;
                            }
                        }
                    }
                }
            }
            screenData.UnlockBits(regionData);
        }

        public void UpdatePosition()
        {
            Memory.ReadMemoryFloat(hndl, PTR_X_POS, out px);
            Memory.ReadMemoryFloat(hndl, PTR_Y_POS, out py);
        }

        public static unsafe void RasterizeRegions(Bitmap scrn, Bitmap visualData)
        {
            BitmapData screenData = scrn.LockBits(new Rectangle(0, 0, scrn.Width, scrn.Height), ImageLockMode.ReadOnly,
                scrn.PixelFormat);
            BitmapData regionData = visualData.LockBits(new Rectangle(0, 0, visualData.Width, visualData.Height),
                ImageLockMode.ReadWrite, visualData.PixelFormat);
            for (int y = 0; y < screenData.Height; y++)
            {
                byte* row = (byte*)screenData.Scan0 + (y * screenData.Stride);
                for (int x = 0; x < screenData.Width; x++)
                {
                    int b = row[x * 4];
                    int g = row[x * 4 + 1];
                    int r = row[x * 4 + 2];

                    byte* pix = (byte*)(regionData.Scan0 + y * regionData.Stride + x * 4);
                    if (GetBrightness(r, g, b) > 254)
                    {
                        pix[0] = 0;
                        pix[1] = 0;
                        pix[2] = 0;
                        pix[3] = 255;
                    }
                    else
                    {
                        pix[0] = 255;
                        pix[1] = 255;
                        pix[2] = 255;
                        pix[3] = 255;
                    }
                }
            }
            scrn.UnlockBits(screenData);
            visualData.UnlockBits(regionData);
        }

        public static double GetBrightness(int r, int g, int b)
        {
            return (r + g + b) / 3.0;
        }
        private IKeyboardMouseEvents m_GlobalHook;

        public void Subscribe()
        {
            m_GlobalHook = Hook.GlobalEvents();
            m_GlobalHook.MouseDownExt += GlobalHookMouseDownExt;
        }

        private void GlobalHookMouseDownExt(object sender, MouseEventExtArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (first == Point.Empty)
                first = new Point(e.X, e.Y);
            else if (second == Point.Empty)
            {
                second = new Point(e.X, e.Y);
                screenRegion = new Rectangle(first, new Size(second.X - first.X, second.Y - first.Y));
                StartBot();
                Unsubscribe();
            }
        }

        public void Unsubscribe()
        {
            m_GlobalHook.MouseDownExt -= GlobalHookMouseDownExt;
            m_GlobalHook.Dispose();
        }



    }
}
