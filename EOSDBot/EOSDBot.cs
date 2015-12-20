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

        const int BULLET_HEIGHT = 2;
        const int DODGE_RISK = 10;

        private readonly IntPtr PTR_DEATHS = new IntPtr(0x0069BCC0);
        private readonly IntPtr PTR_X_POS = new IntPtr(0x006CAA68);
        private readonly IntPtr PTR_Y_POS = new IntPtr(0x006CAA6C);

        private Bitmap screenData;
        private static IntPtr hWnd;
        private static IntPtr hndl;
        private float px, py;

        private Point first, second;
        private Rectangle screenRegion;

        private int[] _columnCount;
        private int _xGoal;
        private string dir = "";

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
            g.DrawString(dir, font, Brushes.Black, 10, 25);
            g.FillEllipse(Brushes.LawnGreen, px - 2, py - 2, 4, 4);
            g.DrawLine(Pens.Red, _xGoal, 0, _xGoal, this.Height);
            g.DrawLine(Pens.Blue, 0, py, this.Width, py);
            if (_columnCount != null)
            {
                for (int i = 0; i < screenData.Width; i++)
                {
                    if (_columnCount[i] > BULLET_HEIGHT)
                        g.DrawLine(Pens.Blue, i, 0, i, this.Height);
                }
            }
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
                    Thread.Sleep(3);
                }

            }).Start();

            const double MOVE_EPSILON = 1;
            const int FOCUS_DISTANCE = 2;

            // Motion control
            new Thread(() =>
            {
                while (true)
                {
                    if (_columnCount != null)
                    {
                        DInput.SendKey(0x2C, DInput.KEYEVENTF_SCANCODE);
                        dir = (px - _xGoal) + "";
                        //if (Math.Abs(px - _xGoal) < FOCUS_DISTANCE)
                        //    DInput.SendKey(0x2A, DInput.KEYEVENTF_SCANCODE);
                        //else
                        //    DInput.SendKey(0x2A, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
                        while (px - _xGoal < -MOVE_EPSILON)
                        {
                            DInput.SendKey(0x4D, DInput.KEYEVENTF_SCANCODE);
                            Thread.Sleep(1);
                            UpdatePosition();
                        }
                        DInput.SendKey(0x4D, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
                        
                        while (px - _xGoal > MOVE_EPSILON)
                        {
                            DInput.SendKey(0x4B, DInput.KEYEVENTF_SCANCODE);
                            Thread.Sleep(1);
                            UpdatePosition();
                        }
                        DInput.SendKey(0x4B, DInput.KEYEVENTF_KEYUP | DInput.KEYEVENTF_SCANCODE);
                    }
                    Thread.Sleep(3);
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

        public void UpdateGoal()
        {
            _xGoal = int.MaxValue;
            bool clear = true;
            for (int i = 0; i < screenData.Width; i++)
            {
                if (_columnCount[i] > BULLET_HEIGHT)
                {
                    clear = false;
                    break;
                }
            }
            if (clear)
            {
                _xGoal = screenData.Width / 2;
            }
            else
            {
                int pcx = (int)px;
                for (int x = 0; x < screenData.Width; x++)
                {
                    bool ok = true;
                    for (int dx = -DODGE_RISK; dx < DODGE_RISK; dx++)
                    {
                        if (x + dx > 0 && x + dx < screenData.Width)
                        {
                            if (_columnCount[x + dx] > BULLET_HEIGHT)
                            {
                                ok = false;
                                break;
                            }
                        }
                        else
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok && Math.Abs(pcx - x) < Math.Abs(pcx - _xGoal))
                        _xGoal = x;
                }
                if (_xGoal == int.MaxValue)
                    _xGoal = pcx;
            }
        }

        const int FORWARD_PEEK = 40;
        const int BACKWARD_PEEK = 15;
        public unsafe void CalculateColumns()
        {
            BitmapData regionData = screenData.LockBits(new Rectangle(0, 0, screenData.Width, screenData.Height), ImageLockMode.ReadWrite, screenData.PixelFormat);
            if (_columnCount == null)
                _columnCount = new int[screenData.Width];
            for (int x = 0; x < screenData.Width; x++)
            {
                _columnCount[x] = 0;
                for (int y = Math.Max(0, (int)py - FORWARD_PEEK); y < Math.Min(screenData.Height, (int)py + BACKWARD_PEEK); y++)
                {
                    byte* data = (byte*)(regionData.Scan0 + y * regionData.Stride + x * 4);
                    if (data[0] == 0)
                    {
                        _columnCount[x]++;
                        if (_columnCount[x] > BULLET_HEIGHT)
                            break;
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
                    if (GetBrightness(r, g, b) > 253)
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
            return (r + r + r + b + g + g + g + g) >> 3;
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
