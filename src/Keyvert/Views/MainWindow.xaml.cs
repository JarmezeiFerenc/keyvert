using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Keyvert.Core;
using Keyvert.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Keyvert.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
#if DEBUG
            // Lets automated UI checks render the light theme without touching the Windows setting.
            if (Environment.GetEnvironmentVariable("KTC_DEBUG_THEME") == "Light")
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica);
                return;
            }
#endif
            SystemThemeWatcher.Watch(this);
        };
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Raised when the user closes the window while "keep running when closed" is off.</summary>
    public event Action? ExitRequested;

    /// <summary>Raised when closing the window only hid it to the tray.</summary>
    public event Action? HiddenToTray;

    public bool AllowClose { get; set; }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            if (_viewModel.CloseToTray)
            {
                Hide();
                HiddenToTray?.Invoke();
            }
            else
            {
                ExitRequested?.Invoke();
            }
        }

        base.OnClosing(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPromptOpen) && _viewModel.IsPromptOpen && _viewModel.PromptHasInput)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                PromptTextBox.Focus();
                PromptTextBox.SelectAll();
            });
        }
    }

    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BindingRowViewModel row })
            _viewModel.HighlightedAction = row.Action;
    }

    private void Row_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BindingRowViewModel row } && _viewModel.HighlightedAction == row.Action)
            _viewModel.HighlightedAction = null;
    }

    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = ProfileMenuButton.ContextMenu;
        menu.PlacementTarget = ProfileMenuButton;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
    }

    private void PresetMenu_Click(object sender, RoutedEventArgs e)
    {
        // Clicks on the preset items bubble up here; the parent item itself only opens the submenu.
        if (e.OriginalSource is FrameworkElement { DataContext: ProfilePreset preset })
            _viewModel.AddPresetCommand.Execute(preset);
    }

    private void CaptureOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.CancelCaptureCommand.Execute(null);
        e.Handled = true;
    }
}
