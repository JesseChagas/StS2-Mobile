using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public partial class LoginSection : VBoxContainer
{
    public event Action<string, string> LoginRequested;
    public LineEdit UsernameField { get; }
    private readonly LineEdit _passwordField;
    private readonly StyledButton _loginButton;
    private readonly StyledButton _showPassword;
    private readonly StyledLabel _validation;

    public LoginSection(float scale)
    {
        AddThemeConstantOverride("separation", (int)(8 * scale));
        Visible = false;
        AddChild(new StyledLabel("Steam username", scale, 16));
        UsernameField = new StyledLineEdit("Steam username", scale);
        AddChild(UsernameField);
        AddChild(new StyledLabel("Password", scale, 16));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", (int)(6 * scale));
        AddChild(row);
        _passwordField = new StyledLineEdit("Password", scale, true)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(_passwordField);
        _showPassword = new StyledButton("Show password", scale, 15, 52);
        _showPassword.SetQuiet();
        row.AddChild(_showPassword);
        _showPassword.Pressed += () =>
        {
            _passwordField.Secret = !_passwordField.Secret;
            _showPassword.SetCopy(_passwordField.Secret ? "Show password" : "Hide password");
        };
        UsernameField.TextSubmitted += _ => _passwordField.GrabFocus();
        _passwordField.TextSubmitted += _ => OnLoginPressed();
        _validation = new StyledLabel("Enter your username and password.", scale, 16)
        {
            Visible = false,
        };
        _validation.SetError(true);
        AddChild(_validation);
        _loginButton = new StyledButton("Sign in", scale, 22, 56);
        _loginButton.SetPrimary(true);
        _loginButton.Pressed += OnLoginPressed;
        AddChild(_loginButton);
        var note = new StyledLabel("Your password is not stored by this launcher.", scale, 14)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.SetMuted();
        AddChild(note);
    }

    public void SetDisabled(bool disabled)
    {
        _loginButton.Disabled = disabled;
        UsernameField.Editable = !disabled;
        _passwordField.Editable = !disabled;
        _showPassword.Disabled = disabled;
    }

    public void ClearPassword()
    {
        _passwordField.Text = "";
        _passwordField.Secret = true;
        _showPassword.SetCopy("Show password");
    }

    private void OnLoginPressed()
    {
        if (_loginButton.Disabled)
            return;
        var username = UsernameField.Text.Trim();
        var password = _passwordField.Text;
        _validation.Visible = string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password);
        if (!_validation.Visible)
            LoginRequested?.Invoke(username, password);
    }
}
