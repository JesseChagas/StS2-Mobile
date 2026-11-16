using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public partial class CodeSection : VBoxContainer
{
    public event Action<string> CodeSubmitted;
    private readonly StyledLabel _codeLabel;
    private readonly LineEdit _codeField;
    private readonly StyledButton _submit;

    public CodeSection(float scale)
    {
        AddThemeConstantOverride("separation", (int)(10 * scale));
        Visible = false;
        _codeLabel = new StyledLabel("Enter Steam Guard code", scale, 14)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(_codeLabel);
        _codeField = new StyledLineEdit("Steam Guard code", scale) { MaxLength = 10 };
        _codeField.TextSubmitted += _ => OnSubmit();
        AddChild(_codeField);
        _submit = new StyledButton("Verify code", scale);
        _submit.SetPrimary(true);
        _submit.Pressed += OnSubmit;
        AddChild(_submit);
    }

    public void Show(bool wasIncorrect)
    {
        Visible = true;
        _submit.Disabled = false;
        _codeField.Text = "";
        _codeLabel.SetCopy("Steam Guard code");
        _codeLabel.SetError(wasIncorrect);
        _codeField.GrabFocus();
    }

    private void OnSubmit()
    {
        var code = _codeField.Text.Trim();
        if (_submit.Disabled || code.Length == 0)
            return;
        _submit.Disabled = true;
        Visible = false;
        CodeSubmitted?.Invoke(code);
    }
}
