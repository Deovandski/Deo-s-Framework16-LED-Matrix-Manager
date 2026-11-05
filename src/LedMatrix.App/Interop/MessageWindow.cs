using System.Windows.Interop;

namespace LedMatrix.App.Interop;

/// <summary>
/// An invisible top-level window that receives the system broadcasts we care about
/// (display changes, device arrival/removal, power and session events, tray icon callbacks).
/// It must be top-level, not message-only, because broadcasts skip message-only windows.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("DeoLedMatrixManager.MessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0, // WS_OVERLAPPED, not WS_VISIBLE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>Raised for every message. Handlers return true to mark it handled.</summary>
    public event Func<int, IntPtr, IntPtr, bool>? Message;

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var subscribers = Message;
        if (subscribers is null)
        {
            return IntPtr.Zero;
        }

        foreach (var d in subscribers.GetInvocationList())
        {
            try
            {
                if (((Func<int, IntPtr, IntPtr, bool>)d)(msg, wParam, lParam))
                {
                    handled = true;
                }
            }
            catch (Exception ex)
            {
                Core.Log.Error($"Message handler failed for 0x{msg:X4}", ex);
            }
        }

        // WM_POWERBROADCAST expects TRUE.
        return msg == NativeMethods.WM_POWERBROADCAST ? new IntPtr(1) : IntPtr.Zero;
    }
}
