using Godot;
using System;
using System.Collections.Generic;

public partial class BattleHud : CanvasLayer
{
    public enum AllyTactic
    {
        Assault,
        Hold,
        Focus,
        Regroup,
    }

    public event Action<AllyTactic> TacticSelected;
    public event Action RestartRequested;

    private readonly Dictionary<AllyTactic, Button> _tacticButtons = [];
    private Label _statusLabel;
    private CenterContainer _resultOverlay;
    private Label _resultTitle;
    private Label _resultSummary;

    public CommandWheelControl CommandWheel { get; private set; }

    public override void _Ready()
    {
        BuildCommandWheel();
        BuildHudPanel();
        BuildResultPanel();
        SetTactic(AllyTactic.Assault);
    }

    public void SetStatusLines(IEnumerable<string> lines)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = string.Join("\n", lines);
        }
    }

    public void SetTactic(AllyTactic tactic)
    {
        foreach (var pair in _tacticButtons)
        {
            pair.Value.ButtonPressed = pair.Key == tactic;
        }
    }

    public void ShowResult(string title, string summary)
    {
        if (_resultOverlay == null)
        {
            return;
        }

        _resultOverlay.Visible = true;
        _resultTitle.Text = title;
        _resultSummary.Text = summary;
    }

    private void BuildCommandWheel()
    {
        CommandWheel = new CommandWheelControl
        {
            Name = "CommandWheel",
        };
        CommandWheel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(CommandWheel);
    }

    private void BuildHudPanel()
    {
        var panel = new PanelContainer
        {
            Name = "HudPanel",
            OffsetLeft = 24.0f,
            OffsetTop = 24.0f,
            OffsetRight = 640.0f,
            OffsetBottom = 500.0f,
        };
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        var title = new Label
        {
            Text = "Ally Tactics",
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(title);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);

        AddTacticButton(row, AllyTactic.Assault, "Assault");
        AddTacticButton(row, AllyTactic.Hold, "Hold");
        AddTacticButton(row, AllyTactic.Focus, "Focus");
        AddTacticButton(row, AllyTactic.Regroup, "Regroup");

        _statusLabel = new Label
        {
            Text = "Loading prototype...",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(_statusLabel);
    }

    private void AddTacticButton(HBoxContainer row, AllyTactic tactic, string text)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(118.0f, 44.0f),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += () => TacticSelected?.Invoke(tactic);
        row.AddChild(button);
        _tacticButtons[tactic] = button;
    }

    private void BuildResultPanel()
    {
        _resultOverlay = new CenterContainer
        {
            Name = "ResultOverlay",
            Visible = false,
        };
        _resultOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_resultOverlay);

        var panel = new PanelContainer
        {
            Name = "ResultPanel",
            CustomMinimumSize = new Vector2(520.0f, 260.0f),
        };
        _resultOverlay.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        margin.AddChild(box);

        _resultTitle = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "Battle Complete",
        };
        _resultTitle.AddThemeFontSizeOverride("font_size", 38);
        box.AddChild(_resultTitle);

        _resultSummary = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "The battle has ended.",
        };
        _resultSummary.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_resultSummary);

        var restartButton = new Button
        {
            Text = "Restart Battle",
            CustomMinimumSize = new Vector2(260.0f, 58.0f),
            FocusMode = Control.FocusModeEnum.None,
        };
        restartButton.AddThemeFontSizeOverride("font_size", 24);
        restartButton.Pressed += () => RestartRequested?.Invoke();
        box.AddChild(restartButton);
    }
}
