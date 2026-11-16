using System;
using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher.Sections;

public partial class BranchSection : VBoxContainer
{
    public event Action<string> BranchChanged;
    private readonly StyledButton _button;
    private string _branch;

    public BranchSection(float scale)
    {
        Visible = false;
        _button = new StyledButton("", scale, 18, 48);
        AddChild(_button);
        _button.Pressed += () =>
        {
            _branch = BranchSettings.Toggle(_branch);
            UpdateText();
            BranchChanged?.Invoke(_branch);
        };
        _branch = BranchSettings.Load();
        UpdateText();
    }

    public string Branch => _branch;

    public void SetDisabled(bool disabled) => _button.Disabled = disabled;

    private void UpdateText() =>
        _button.SetCopy(_branch == BranchSettings.PublicBeta ? "Version: beta" : "Version: stable");
}
