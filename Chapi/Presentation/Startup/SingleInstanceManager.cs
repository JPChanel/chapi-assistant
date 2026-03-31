using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Chapi.Startup;

public sealed class SingleInstanceManager : IDisposable
{
    private const int WM_COPYDATA = 0x004A;

    private readonly string _mutexName;
    private readonly string _windowTitle;
    private readonly uint _restoreMessage;
    private Mutex? _mutex;
    private System.Windows.Interop.HwndSource? _windowSource;

    public SingleInstanceManager(string mutexName, string windowTitle, string restoreMessageName)
    {
        _mutexName = mutexName;
        _windowTitle = windowTitle;
        _restoreMessage = RegisterWindowMessage(restoreMessageName);
    }

    public bool TryRedirectToExistingInstance(string[] args)
    {
        _mutex = new Mutex(true, _mutexName, out bool isNewInstance);
        if (isNewInstance)
        {
            return false;
        }

        var hWnd = FindExistingWindow();
        if (hWnd != IntPtr.Zero)
        {
            SendArgumentsToExistingWindow(hWnd, args);
            PostMessage(hWnd, _restoreMessage, IntPtr.Zero, IntPtr.Zero);
        }

        return true;
    }

    public void AttachToWindow(Window window, Action<string> processArguments)
    {
        window.Loaded += (_, _) =>
        {
            if (_windowSource != null)
            {
                return;
            }

            _windowSource = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(window).Handle);

            _windowSource?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == _restoreMessage)
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    handled = true;
                }
                else if (msg == WM_COPYDATA)
                {
                    var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                    if (!string.IsNullOrWhiteSpace(cds.lpData))
                    {
                        processArguments(cds.lpData);
                    }

                    handled = true;
                }

                return IntPtr.Zero;
            });
        };
    }

    public void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
        }

        try
        {
            _mutex?.Dispose();
        }
        catch
        {
        }

        _mutex = null;
    }

    public void Dispose()
    {
        Release();
    }

    private IntPtr FindExistingWindow()
    {
        var currentProcess = Process.GetCurrentProcess();
        var otherProcess = Process.GetProcessesByName(currentProcess.ProcessName)
            .FirstOrDefault(p => p.Id != currentProcess.Id);

        if (otherProcess?.MainWindowHandle != IntPtr.Zero)
        {
            return otherProcess.MainWindowHandle;
        }

        return FindWindow(null, _windowTitle);
    }

    private static void SendArgumentsToExistingWindow(IntPtr hWnd, string[] args)
    {
        var joinedArgs = string.Join(" ", args);
        if (string.IsNullOrWhiteSpace(joinedArgs))
        {
            return;
        }

        var cds = new COPYDATASTRUCT
        {
            dwData = (IntPtr)100,
            cbData = System.Text.Encoding.Default.GetByteCount(joinedArgs) + 1,
            lpData = joinedArgs
        };

        SendMessage(hWnd, WM_COPYDATA, IntPtr.Zero, ref cds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;

        [MarshalAs(UnmanagedType.LPStr)]
        public string lpData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam);
}
