using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class AuditTests
{
    [StructLayout(LayoutKind.Sequential)] private struct KeyData { public uint key, scan, flags, time; public UIntPtr extra; }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    [STAThread] private static int Main(string[] args)
    {
        try {
            SprintApp.SelfTest(); Console.WriteLine("PASS: state transitions, input failures, release retry, scope, native layout");
            using (Mutex owner = new Mutex(true, "Local\\BedrockAutoSprintHelper")) {
                ProcessStartInfo info = new ProcessStartInfo(args[0], "--instance-check");
                info.UseShellExecute = false; info.CreateNoWindow = true;
                using (Process first = Process.Start(info))
                using (Process second = Process.Start(info)) {
                    first.WaitForExit(); second.WaitForExit();
                    Require(first.ExitCode == 2 && second.ExitCode == 2, "both duplicate launches rejected");
                }
            }
            using (Process alone = Process.Start(new ProcessStartInfo(args[0], "--instance-check") { UseShellExecute = false, CreateNoWindow = true })) {
                alone.WaitForExit(); Require(alone.ExitCode == 0, "restart allowed after mutex closes");
            }
            Application.EnableVisualStyles();
            SprintApp app = new SprintApp();
            Type type = typeof(SprintApp);
            SprintState state = (SprintState)type.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
            MethodInfo hook = type.GetMethod("KeyboardHook", BindingFlags.Instance | BindingFlags.NonPublic);
            IntPtr data = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(KeyData)));
            try {
                Marshal.StructureToPtr(new KeyData { key = 0x77 }, data, false);
                hook.Invoke(app, new object[] { 0, new IntPtr(0x100), data });
                Require(!state.Enabled, "F8 disables");
                hook.Invoke(app, new object[] { 0, new IntPtr(0x100), data });
                Require(!state.Enabled, "held F8 does not repeat-toggle");
                hook.Invoke(app, new object[] { 0, new IntPtr(0x101), data });
                hook.Invoke(app, new object[] { 0, new IntPtr(0x100), data });
                Require(state.Enabled, "next F8 press enables");
                Marshal.StructureToPtr(new KeyData { key = 0x77, flags = 0x10 }, data, false);
                hook.Invoke(app, new object[] { 0, new IntPtr(0x101), data });
                Require(state.Enabled, "injected input ignored");
                NotifyIcon tray = (NotifyIcon)type.GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                Require(tray.Icon != null && tray.Visible, "embedded icon and tray startup");
            } finally { Marshal.FreeHGlobal(data); }
            System.Windows.Forms.Timer stop = new System.Windows.Forms.Timer { Interval = 300 };
            stop.Tick += delegate { stop.Stop(); app.ExitThread(); };
            stop.Start(); Application.Run(app); stop.Dispose();
            Require((IntPtr)type.GetField("hook", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app) == IntPtr.Zero, "normal exit removes keyboard hook");
            Require(!state.IDown, "normal exit leaves no tracked I press");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
