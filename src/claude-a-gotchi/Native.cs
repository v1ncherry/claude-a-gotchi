using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeAGotchi;

static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

    // For the window with real transparency (UpdateLayeredWindow): picture, size and position change in one step.
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UPDATELAYEREDWINDOWINFO
    {
        public int cbSize;
        public IntPtr hdcDst;
        public POINT* pptDst;
        public SIZE* psize;
        public IntPtr hdcSrc;
        public POINT* pptSrc;
        public uint crKey;
        public BLENDFUNCTION* pblend;
        public uint dwFlags;
        public RECT* prcDirty;
    }
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindowIndirect(IntPtr h, ref UPDATELAYEREDWINDOWINFO info);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    public const int WS_EX_LAYERED = 0x80000;
    public const uint ULW_ALPHA = 2;

    public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, 256);
        return sb.ToString();
    }

    public static uint ScreenDpi(Screen sc)
    {
        try
        {
            var pt = new POINT { X = sc.Bounds.Left + sc.Bounds.Width / 2, Y = sc.Bounds.Top + sc.Bounds.Height / 2 };
            var mon = MonitorFromPoint(pt, 2);
            if (GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 && dx > 0) return dx;
        }
        catch { }
        return 96;
    }
}

// Top edges of windows near Clawd that he can jump onto.
sealed record Edge(IntPtr Hwnd, int Top, int A, int B, bool Work, int WinLeft);

static class WindowScanner
{
    static readonly Dictionary<uint, string> ProcNames = new();
    static readonly HashSet<string> WorkProcs = new(StringComparer.OrdinalIgnoreCase)
    { "WindowsTerminal", "OpenConsole", "conhost", "Code", "Claude", "powershell", "pwsh", "cmd", "Cursor", "wezterm-gui", "alacritty" };
    static readonly HashSet<string> SkipClasses = new()
    { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow" };

    public static List<Edge> Scan(IntPtr self, Rectangle fb, int minClear, int minAbove, int rightLimit, int minWidth)
    {
        var wins = new List<(IntPtr h, Rectangle r)>();
        Native.EnumWindows((h, _) =>
        {
            try
            {
                if (h == self || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                if (SkipClasses.Contains(Native.ClassOf(h))) return true;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0)
                    Native.GetWindowRect(h, out r);
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width < 100 || rect.Height < 60) return true;
                wins.Add((h, rect));
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        var list = new List<Edge>();
        for (int i = 0; i < wins.Count; i++)
        {
            var (h, r) = wins[i];
            int top = r.Top;
            if (top < fb.Top + minClear || top > fb.Bottom - minAbove) continue;
            int a = Math.Max(r.Left, fb.Left), b = Math.Min(r.Right, rightLimit);
            if (b - a < minWidth) continue;
            var segs = new List<(int a, int b)> { (a, b) };
            // Parts covered by windows in front do not count.
            for (int j = 0; j < i; j++)
            {
                var o = wins[j].r;
                if (o.Top < top + 3 && o.Bottom > top - 3) segs = Subtract(segs, o.Left, o.Right);
            }
            var best = segs.OrderByDescending(x => x.b - x.a).FirstOrDefault();
            if (best.b - best.a < minWidth) continue;
            list.Add(new Edge(h, top, best.a, best.b, IsWork(h), r.Left));
        }
        return list;
    }

    static List<(int a, int b)> Subtract(List<(int a, int b)> segs, int l, int r)
    {
        var res = new List<(int a, int b)>();
        foreach (var (a, b) in segs)
        {
            if (r <= a || l >= b) { res.Add((a, b)); continue; }
            if (l > a) res.Add((a, l));
            if (r < b) res.Add((r, b));
        }
        return res;
    }

    static bool IsWork(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (!ProcNames.TryGetValue(pid, out var name))
        {
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { name = ""; }
            ProcNames[pid] = name;
        }
        return WorkProcs.Contains(name);
    }
}

// Drawing surface in memory that is handed to Windows as the window content (32 bit with alpha).
// Pixels with alpha 0 are see-through, and mouse clicks pass through them.
sealed class LayeredSurface : IDisposable
{
    public readonly int W, H;
    public readonly Bitmap Bitmap;
    readonly IntPtr dib, dc, oldObj;
    bool loggedError;

    public LayeredSurface(int w, int h)
    {
        W = w; H = h;
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h,          // negative: rows run top to bottom
            biPlanes = 1, biBitCount = 32,
        };
        dib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
        dc = Native.CreateCompatibleDC(IntPtr.Zero);
        oldObj = Native.SelectObject(dc, dib);
        Bitmap = new Bitmap(w, h, w * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
    }

    // Shows the part src (surface coordinates) at the screen point dst. dirty: only this part changed (window coordinates), null = all.
    public unsafe void Present(IntPtr hwnd, Point dst, Rectangle src, Rectangle? dirty)
    {
        Native.GdiFlush();
        var pDst = new Native.POINT { X = dst.X, Y = dst.Y };
        var size = new Native.SIZE { cx = src.Width, cy = src.Height };
        var pSrc = new Native.POINT { X = src.X, Y = src.Y };
        var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        var rc = new Native.RECT();
        if (dirty is Rectangle d) rc = new Native.RECT { Left = d.Left, Top = d.Top, Right = d.Right, Bottom = d.Bottom };
        var info = new Native.UPDATELAYEREDWINDOWINFO
        {
            cbSize = sizeof(Native.UPDATELAYEREDWINDOWINFO),
            pptDst = &pDst, psize = &size, hdcSrc = dc, pptSrc = &pSrc,
            pblend = &blend, dwFlags = Native.ULW_ALPHA,
            prcDirty = dirty.HasValue ? &rc : null,
        };
        if (Native.UpdateLayeredWindowIndirect(hwnd, ref info)) return;
        int err = Marshal.GetLastWin32Error();
        if (!loggedError)
        {
            loggedError = true;
            AppPaths.LogLine("UpdateLayeredWindow error " + err + " (dirty " + (dirty?.ToString() ?? "all") + ")");
        }
        if (dirty.HasValue) Present(hwnd, dst, src, null);   // partial update refused: send everything
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        Native.SelectObject(dc, oldObj);
        Native.DeleteDC(dc);
        Native.DeleteObject(dib);
    }
}
