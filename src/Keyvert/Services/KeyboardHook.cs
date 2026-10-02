using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Keyvert.Services;

/// <summary>
/// System-wide low-level keyboard hook running on its own thread and message loop, so a busy
/// UI thread can never delay keyboard input. The handler is called on that thread and must
/// return quickly, or Windows silently removes the hook.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    /// <summary>Returns true to swallow the key event.</summary>
    public delegate bool KeyHandler(int virtualKey, bool isDown);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint PM_NOREMOVE = 0;

#if DEBUG
    // Lets automated UI checks drive the hook with SendInput. Release builds never accept injected keys.
    private static readonly bool AcceptInjected = Environment.GetEnvironmentVariable("KTC_DEBUG_ACCEPT_INJECTED") == "1";
#else
    private const bool AcceptInjected = false;
#endif

    private readonly KeyHandler _handler;
    private readonly Action<Exception> _onError;
    private readonly HookProc _proc;
    private readonly Thread _thread;
    private IntPtr _hook;
    private uint _threadId;
    private bool _disposed;

    public KeyboardHook(KeyHandler handler, Action<Exception> onError)
    {
        _handler = handler;
        _onError = onError;
        _proc = HookCallback;

        using var ready = new ManualResetEventSlim();
        int installError = 0;
        _thread = new Thread(() => Run(ready, ref installError))
        {
            IsBackground = true,
            Name = "Keyboard hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait();

        if (installError != 0)
            throw new Win32Exception(installError, "Could not install the keyboard hook.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run(ManualResetEventSlim ready, ref int installError)
    {
        _threadId = GetCurrentThreadId();

        // Creates this thread's message queue, so Dispose's WM_QUIT can't be posted before it exists.
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            installError = Marshal.GetLastWin32Error();
            ready.Set();
            return;
        }
        ready.Set();

        // Low-level hook callbacks are delivered while this thread waits in GetMessage.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int message = (int)wParam;
            bool isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = message is WM_KEYUP or WM_SYSKEYUP;

            // Injected events come from other software (or ourselves); remapping them could loop.
            if ((isDown || isUp) && (AcceptInjected || (info.flags & LLKHF_INJECTED) == 0))
            {
                try
                {
                    if (_handler((int)info.vkCode, isDown))
                        return 1;
                }
                catch (Exception ex)
                {
                    _onError(ex);
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
