using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public partial class DownloadSection : VBoxContainer
{
    public event Action DownloadRequested;
    public bool IsDownloading { get; private set; }
    private readonly StyledButton _downloadButton;
    private readonly VBoxContainer _progress;
    private readonly ProgressBar _progressBar;
    private readonly StyledLabel _percentage;
    private readonly StyledLabel _progressLabel;

    public DownloadSection(float scale)
    {
        AddThemeConstantOverride("separation", (int)(14 * scale));
        Visible = false;
        _downloadButton = new StyledButton("Download game", scale, 22, 56);
        _downloadButton.SetPrimary(true);
        _downloadButton.Pressed += () => DownloadRequested?.Invoke();
        AddChild(_downloadButton);
        _progress = new VBoxContainer { Visible = false };
        _progress.AddThemeConstantOverride("separation", (int)(14 * scale));
        AddChild(_progress);
        _progress.AddChild(new StyledLabel("Downloading your game", scale, 19));
        _percentage = new StyledLabel("0%", scale, 42);
        _progress.AddChild(_percentage);
        _progressBar = new StyledProgressBar(scale);
        _progress.AddChild(_progressBar);
        _progressLabel = new StyledLabel("Preparing download...", scale, 17)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _progressLabel.SetMuted();
        _progress.AddChild(_progressLabel);
    }

    public void SetProgress(double pct, string text)
    {
        IsDownloading = true;
        _progress.Visible = true;
        _downloadButton.Visible = false;
        _progressBar.Value = pct;
        _percentage.SetCopy($"{pct:F0}%");
        _progressLabel.SetCopy(text);
    }

    public void ShowProgress(string text)
    {
        SetProgress(0, text);
        _downloadButton.Disabled = true;
    }

    public void HideProgress()
    {
        IsDownloading = false;
        _progress.Visible = false;
        _downloadButton.Visible = true;
    }

    public void SetButtonDisabled(bool disabled) => _downloadButton.Disabled = disabled;

    public void SetButtonText(string text) => _downloadButton.SetCopy(text);

    public void Reset(string buttonText = "Download game")
    {
        IsDownloading = false;
        _downloadButton.Disabled = false;
        _downloadButton.Visible = true;
        _downloadButton.SetCopy(buttonText);
        _progress.Visible = false;
        _progressBar.Value = 0;
    }
}
