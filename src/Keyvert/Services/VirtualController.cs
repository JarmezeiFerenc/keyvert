using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Keyvert.Core;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Keyvert.Services;

/// <summary>
/// A virtual Xbox 360 controller on the ViGEmBus driver. Reports are sent from a background
/// thread so the keyboard hook callback never waits on the driver.
/// </summary>
public sealed class VirtualController : IDisposable
{
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _pad;
    private readonly Thread _worker;
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _lock = new();
    private ControllerState _pending;
    private bool _stopping;

    /// <summary>Raised on the background thread when the driver rejects a report.</summary>
    public event Action<Exception>? SubmitFailed;

    /// <exception cref="Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException">ViGEmBus is not installed.</exception>
    public VirtualController()
    {
        _client = new ViGEmClient();
        try
        {
            _pad = _client.CreateXbox360Controller();
            _pad.AutoSubmitReport = false;
            _pad.Connect();
        }
        catch
        {
            _client.Dispose();
            throw;
        }

        _worker = new Thread(SubmitLoop) { IsBackground = true, Name = "Virtual controller" };
        _worker.Start();
    }

    /// <summary>The 1-based player slot Windows assigned, once the driver reports it.</summary>
    public int? PlayerNumber
    {
        get
        {
            try
            {
                return _pad.UserIndex + 1;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public void Update(ControllerState state)
    {
        lock (_lock)
        {
            if (_stopping)
                return;
            _pending = state;
        }
        _signal.Set();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_stopping)
                return;
            _pending = ControllerState.Neutral;
            _stopping = true;
        }
        _signal.Set();
        _worker.Join(TimeSpan.FromSeconds(2));

        try
        {
            _pad.Disconnect();
        }
        catch
        {
        }
        _client.Dispose();
        _signal.Dispose();
    }

    private void SubmitLoop()
    {
        ControllerState? submitted = null;
        while (true)
        {
            _signal.WaitOne();

            ControllerState state;
            bool stopping;
            lock (_lock)
            {
                state = _pending;
                stopping = _stopping;
            }

            if (state != submitted)
            {
                try
                {
                    Submit(state);
                    submitted = state;
                }
                catch (Exception ex)
                {
                    SubmitFailed?.Invoke(ex);
                }
            }

            if (stopping)
                return;
        }
    }

    private void Submit(ControllerState state)
    {
        _pad.SetButtonsFull((ushort)state.Buttons);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, state.RightX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, state.RightY);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
        _pad.SubmitReport();
    }
}
