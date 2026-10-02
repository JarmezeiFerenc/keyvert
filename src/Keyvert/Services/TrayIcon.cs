using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Keyvert.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _onIcon;
    private readonly Icon _offIcon;
    private readonly ToolStripMenuItem _emulationItem;
    private readonly ToolStripMenuItem _profilesItem;
    private readonly Func<IReadOnlyList<string>> _listProfiles;
    private string? _currentProfile;

    public TrayIcon(Func<IReadOnlyList<string>> listProfiles)
    {
        _listProfiles = listProfiles;
        _onIcon = LoadIcon("tray_on.ico");
        _offIcon = LoadIcon("tray_off.ico");

        var openItem = new ToolStripMenuItem("Open Keyvert", null, (_, _) => OpenRequested?.Invoke());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        _emulationItem = new ToolStripMenuItem("Emulation", null, (_, _) => ToggleRequested?.Invoke());
        _profilesItem = new ToolStripMenuItem("Profile");
        _profilesItem.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });

        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_emulationItem);
        menu.Items.Add(_profilesItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        menu.Opening += (_, _) => RebuildProfileMenu();

        _notifyIcon = new NotifyIcon { ContextMenuStrip = menu, Icon = _offIcon, Visible = true };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke();
        };
    }

    public event Action? OpenRequested;
    public event Action? ToggleRequested;
    public event Action<string>? ProfileSelected;
    public event Action? ExitRequested;

    public void Update(bool emulationOn, string? profile, string toggleKey)
    {
        _currentProfile = profile;
        _notifyIcon.Icon = emulationOn ? _onIcon : _offIcon;
        _emulationItem.Checked = emulationOn;
        _emulationItem.Text = $"Emulation ({toggleKey})";

        string text = $"Keyvert: {(emulationOn ? "on" : "off")}";
        if (profile is not null)
            text += $"\n{profile}";
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void ShowBalloon(string title, string message) =>
        _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.None);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _onIcon.Dispose();
        _offIcon.Dispose();
    }

    private void RebuildProfileMenu()
    {
        _profilesItem.DropDownItems.Clear();
        foreach (string name in _listProfiles())
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => ProfileSelected?.Invoke(name))
            {
                Checked = string.Equals(name, _currentProfile, StringComparison.OrdinalIgnoreCase),
            };
            _profilesItem.DropDownItems.Add(item);
        }
    }

    private static Icon LoadIcon(string fileName)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{fileName}"))
            ?? throw new InvalidOperationException($"Missing resource {fileName}.");
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
