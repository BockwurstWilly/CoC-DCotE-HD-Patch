using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

public static class Live
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }

    static Live() { SetProcessDPIAware(); }

    public static bool Focus(IntPtr h)
    {
        // pressing Alt lifts the foreground lock for this process
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        ShowWindow(h, 9);
        SetForegroundWindow(h);
        Thread.Sleep(200);
        return GetForegroundWindow() == h;
    }

    public static IntPtr Foreground() { return GetForegroundWindow(); }

    public static void Shot(string path, int w, int h)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        using (var bmp = new Bitmap(sw, sh))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(0, 0, 0, 0, new Size(sw, sh));
            using (var small = new Bitmap(bmp, w, h)) small.Save(path, ImageFormat.Png);
        }
    }

    static void Send(INPUT i) { SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT))); }

    /// <summary>Relative mouse motion in raw counts, split into steps (one per ~frame).</summary>
    public static void Move(int dx, int dy, int steps, int delayMs)
    {
        int sx = 0, sy = 0;
        for (int s = 1; s <= steps; s++)
        {
            int tx = dx * s / steps, ty = dy * s / steps;
            var i = new INPUT { type = 0 };
            i.u.mi.dx = tx - sx; i.u.mi.dy = ty - sy; i.u.mi.flags = 0x0001;
            sx = tx; sy = ty;
            Send(i);
            Thread.Sleep(delayMs);
        }
    }

    public static void Click()
    {
        var d = new INPUT { type = 0 }; d.u.mi.flags = 0x0002; Send(d);
        Thread.Sleep(60);
        var u = new INPUT { type = 0 }; u.u.mi.flags = 0x0004; Send(u);
    }

    public static void Key(ushort vk, int holdMs)
    {
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        var d = new INPUT { type = 1 }; d.u.ki.scan = scan; d.u.ki.flags = 0x0008; Send(d);
        Thread.Sleep(holdMs);
        var u = new INPUT { type = 1 }; u.u.ki.scan = scan; u.u.ki.flags = 0x0008 | 0x0002; Send(u);
    }

    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
}
