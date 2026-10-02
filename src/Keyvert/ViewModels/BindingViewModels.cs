using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keyvert.Core;

namespace Keyvert.ViewModels;

public sealed record KeyChip(int VirtualKey, string Label);

public sealed class BindingGroupViewModel(string title, IReadOnlyList<BindingRowViewModel> rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<BindingRowViewModel> Rows { get; } = rows;
}

public sealed partial class BindingRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public BindingRowViewModel(ActionInfo info, MainViewModel owner)
    {
        Action = info.Action;
        Name = info.Name;
        FullName = info.FullName;
        Hint = info.Hint;
        _owner = owner;
        Keys.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasKeys));
    }

    public ControllerAction Action { get; }
    public string Name { get; }
    public string FullName { get; }
    public string? Hint { get; }
    public ObservableCollection<KeyChip> Keys { get; } = [];
    public bool HasKeys => Keys.Count > 0;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    [RelayCommand]
    private void AddKey() => _owner.StartCapture(this);

    [RelayCommand]
    private void RemoveKey(KeyChip chip) => _owner.RemoveKey(chip.VirtualKey);
}

public sealed record ActionInfo(ControllerAction Action, string Name, string FullName, string? Hint = null);

public static class ActionCatalog
{
    public static readonly IReadOnlyList<(string Group, ActionInfo[] Actions)> Groups =
    [
        ("Left stick",
        [
            new(ControllerAction.LeftStickUp, "Up", "Left stick up"),
            new(ControllerAction.LeftStickDown, "Down", "Left stick down"),
            new(ControllerAction.LeftStickLeft, "Left", "Left stick left"),
            new(ControllerAction.LeftStickRight, "Right", "Left stick right"),
            new(ControllerAction.LS, "Click", "Left stick click (L3)", "L3"),
            new(ControllerAction.Walk, "Walk", "Walk", "Hold for a partial tilt"),
        ]),
        ("Right stick",
        [
            new(ControllerAction.RightStickUp, "Up", "Right stick up"),
            new(ControllerAction.RightStickDown, "Down", "Right stick down"),
            new(ControllerAction.RightStickLeft, "Left", "Right stick left"),
            new(ControllerAction.RightStickRight, "Right", "Right stick right"),
            new(ControllerAction.RS, "Click", "Right stick click (R3)", "R3"),
        ]),
        ("Face buttons",
        [
            new(ControllerAction.A, "A", "A"),
            new(ControllerAction.B, "B", "B"),
            new(ControllerAction.X, "X", "X"),
            new(ControllerAction.Y, "Y", "Y"),
        ]),
        ("Bumpers and triggers",
        [
            new(ControllerAction.LB, "Left bumper", "Left bumper (LB)", "LB"),
            new(ControllerAction.RB, "Right bumper", "Right bumper (RB)", "RB"),
            new(ControllerAction.LT, "Left trigger", "Left trigger (LT)", "LT"),
            new(ControllerAction.RT, "Right trigger", "Right trigger (RT)", "RT"),
        ]),
        ("D-pad",
        [
            new(ControllerAction.DpadUp, "Up", "D-pad up"),
            new(ControllerAction.DpadDown, "Down", "D-pad down"),
            new(ControllerAction.DpadLeft, "Left", "D-pad left"),
            new(ControllerAction.DpadRight, "Right", "D-pad right"),
        ]),
        ("Menu buttons",
        [
            new(ControllerAction.Start, "Start", "Start (Menu)", "Menu"),
            new(ControllerAction.Back, "Back", "Back (View)", "View"),
            new(ControllerAction.Guide, "Guide", "Guide (Xbox button)", "Xbox button"),
        ]),
    ];

    public static string FullName(ControllerAction action) =>
        Groups.SelectMany(g => g.Actions).First(a => a.Action == action).FullName;
}
