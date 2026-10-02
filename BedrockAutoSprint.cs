using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class SprintState
{
    internal bool Enabled = true;
    internal bool WDown;
    internal bool IDown;
    private readonly Func<bool, bool> send;
    internal SprintState(Func<bool, bool> sender) { send = sender; }
    internal void Key(bool down, bool game)
    {
        bool fresh = down && !WDown;
        WDown = down;
        if (!down) Release();
        else if (fresh && Enabled && game && !IDown) { IDown = send(true); }
    }
    internal void Release() { if (IDown && send(false)) IDown = false; }
    internal void Toggle() { Enabled = !Enabled; Release(); }
}

internal sealed class SprintApp : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private readonly HookProc callback;
    private readonly SprintState state;
    private readonly ToolStripMenuItem toggle;
    private readonly Icon appIcon;
    private IntPtr hook;
    private bool f8Down;
    private bool failed;

    internal SprintApp()
    {
        state = new SprintState(SendI);
        toggle = new ToolStripMenuItem("Enabled (F8)", null, delegate { Toggle(); });
        toggle.Checked = true;
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(toggle);
        menu.Items.Add("Exit", null, delegate { ExitThread(); });
        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        tray = new NotifyIcon { Icon = appIcon, Text = "Bedrock Auto Sprint: ON (F8)", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += delegate { Toggle(); };
        callback = KeyboardHook;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) { tray.Dispose(); throw new System.ComponentModel.Win32Exception(); }
        timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += delegate {
            if (failed) { ExitThread(); return; }
            if (!IsMinecraft()) state.Release();
            if ((GetAsyncKeyState(0x57) & 0x8000) == 0) state.Key(false, false);
        };
        timer.Start();
        tray.ShowBalloonTip(4000, "Bedrock Auto Sprint", "Set Minecraft Sprint to I. W also holds I while Minecraft is active. F8 pauses; right-click this icon to exit.", ToolTipIcon.Info);
    }

    private void Toggle()
    {
        state.Toggle();
        toggle.Checked = state.Enabled;
        tray.Text = "Bedrock Auto Sprint: " + (state.Enabled ? "ON" : "OFF") + " (F8)";
    }

    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            KeyboardData k = (KeyboardData)Marshal.PtrToStructure(data, typeof(KeyboardData));
            if ((k.flags & 0x10) == 0)
            {
                int m = message.ToInt32();
                bool down = m == 0x100 || m == 0x104;
                bool up = m == 0x101 || m == 0x105;
                if (down || up)
                {
                    if (k.vkCode == 0x77)
                    {
                        if (down && !f8Down) Toggle();
                        f8Down = down;
                        return new IntPtr(1);
                    }
                    if (k.vkCode == 0x57) state.Key(down, IsMinecraft());
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private static bool IsMinecraft()
    {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        try {
            using (Process p = Process.GetProcessById((int)pid)) {
                return IsMinecraftName(p.ProcessName);
            }
        } catch { return false; }
    }

    internal static bool IsMinecraftName(string name)
    {
        return string.Equals(name, "Minecraft.Windows", StringComparison.OrdinalIgnoreCase);
    }

    private bool SendI(bool down)
    {
        if (down && !IsMinecraft()) return false;
        INPUT input = new INPUT();
        input.type = 1;
        input.data.keyboard.wScan = 0x17; // Physical I scan code.
        input.data.keyboard.flags = 0x0008u | (down ? 0u : 0x0002u);
        bool success = SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
        if (!success && !failed)
        {
            failed = true;
            tray.ShowBalloonTip(5000, "Input could not be sent", "Run Minecraft and this helper at the same permission level, then restart the helper.", ToolTipIcon.Error);
        }
        return success;
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        state.Release();
        timer.Dispose();
        tray.Visible = false;
        tray.Dispose();
        appIcon.Dispose();
        base.ExitThreadCore();
    }

    internal static void SelfTest()
    {
        int balance = 0, sends = 0;
        SprintState s = new SprintState(delegate(bool down) { balance += down ? 1 : -1; sends++; return true; });
        s.Key(true, false); Check(balance == 0, "Outside game");
        s.Key(false, false);
        s.Key(true, true); s.Key(true, true); Check(balance == 1 && sends == 1, "Press and repeat");
        s.Key(false, true); Check(balance == 0, "Release W");
        s.Key(true, true); s.Toggle(); Check(balance == 0 && !s.Enabled, "Disable releases I");
        s.Key(false, true); s.Key(true, true); Check(balance == 0, "Disabled press");
        s.Toggle(); s.Key(true, true); Check(balance == 0, "Enable needs fresh press");
        s.Key(false, true); s.Key(true, true); s.Release(); Check(balance == 0, "Focus loss or exit releases I");
        Check(IsMinecraftName("Minecraft.Windows") && !IsMinecraftName("Minecraft") && !IsMinecraftName("notepad") && !IsMinecraftName("javaw"), "Scope");
        Check(Marshal.SizeOf(typeof(INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "INPUT ABI");
        Check(Marshal.OffsetOf(typeof(INPUT), "data").ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "INPUT alignment");
        bool accept = false;
        SprintState failure = new SprintState(delegate(bool down) { return accept; });
        failure.Key(true, true); Check(!failure.IDown, "Failed press is not held");
        failure.Key(false, true); accept = true; failure.Key(true, true);
        accept = false; failure.Release(); Check(failure.IDown, "Failed release retains state");
        accept = true; failure.Release(); Check(!failure.IDown, "Release retry");
    }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint vkCode, scanCode, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion {
        [FieldOffset(0)] public KeyboardInput keyboard;
        [FieldOffset(0)] public MouseInput mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort wVk, wScan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int dx, dy; public uint mouseData, flags, time; public UIntPtr extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test") {
            try { SprintApp.SelfTest(); return 0; } catch { return 1; }
        }
        bool created;
        using (Mutex mutex = new Mutex(true, "Local\\BedrockAutoSprintHelper", out created)) {
            if (args.Length == 1 && args[0] == "--instance-check") return created ? 0 : 2;
            if (!created) { MessageBox.Show("Bedrock Auto Sprint is already running. Look for its tray icon."); return 0; }
            Application.EnableVisualStyles();
            try { Application.Run(new SprintApp()); return 0; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Bedrock Auto Sprint"); return 1; }
        }
    }
}
