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

    public enum BattleSkill
    {
        Charge,
        Volley,
        Rally,
    }

    public event Action<AllyTactic> TacticSelected;
    public event Action<BattleSkill> SkillSelected;
    public event Action StartRequested;
    public event Action RestartRequested;

    private readonly Dictionary<AllyTactic, Button> _tacticButtons = [];
    private readonly Dictionary<BattleSkill, Button> _skillButtons = [];
    private Control _hudRoot;
    private Label _statusLabel;
    private CenterContainer _startOverlay;
    private PanelContainer _selectedSkillPanel;
    private Label _selectedSkillTitle;
    private CenterContainer _resultOverlay;
    private Label _resultTitle;
    private Label _resultSummary;

    public override void _Ready()
    {
        BuildStartPanel();
        BuildHudPanel();
        BuildSelectedSkillPanel();
        BuildResultPanel();
        SetTactic(AllyTactic.Assault);
        ShowStartMenu();
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

    public void ShowStartMenu()
    {
        if (_startOverlay != null)
        {
            _startOverlay.Visible = true;
        }

        if (_hudRoot != null)
        {
            _hudRoot.Visible = false;
        }

        if (_selectedSkillPanel != null)
        {
            _selectedSkillPanel.Visible = false;
        }
    }

    public void ShowBattleHud()
    {
        if (_startOverlay != null)
        {
            _startOverlay.Visible = false;
        }

        if (_hudRoot != null)
        {
            _hudRoot.Visible = true;
        }
    }

    public void SetSkillCooldowns(float charge, float volley, float rally)
    {
        SetSkillButtonState(BattleSkill.Charge, "Charge", charge, true);
        SetSkillButtonState(BattleSkill.Volley, "Volley", volley, true);
        SetSkillButtonState(BattleSkill.Rally, "Rally", rally, true);
    }

    public void SetSelectedSkillPanel(
        bool visible,
        Vector2 screenPosition,
        string selectedName,
        bool canCharge,
        bool canVolley,
        float chargeCooldown,
        float volleyCooldown,
        float rallyCooldown
    )
    {
        if (_selectedSkillPanel == null)
        {
            return;
        }

        _selectedSkillPanel.Visible = visible;
        if (!visible)
        {
            return;
        }

        _selectedSkillPanel.Position = screenPosition + new Vector2(-185.0f, -96.0f);
        _selectedSkillTitle.Text = selectedName;
        SetSkillButtonState(BattleSkill.Charge, "Charge", chargeCooldown, canCharge);
        SetSkillButtonState(BattleSkill.Volley, "Volley", volleyCooldown, canVolley);
        SetSkillButtonState(BattleSkill.Rally, "Rally", rallyCooldown, true);
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

    private void BuildHudPanel()
    {
        _hudRoot = new Control
        {
            Name = "BattleHudRoot",
        };
        _hudRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_hudRoot);

        BuildTopBar();
        BuildTacticPanel();
        BuildStatusPanel();
    }

    private void BuildTopBar()
    {
        var panel = new PanelContainer
        {
            Name = "TopBar",
            OffsetLeft = 24.0f,
            OffsetTop = 18.0f,
            OffsetRight = 1576.0f,
            OffsetBottom = 94.0f,
        };
        ApplyPanelStyle(panel, new Color(0.05f, 0.07f, 0.09f, 0.88f), new Color(0.42f, 0.56f, 0.62f, 0.8f));
        _hudRoot.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        margin.AddChild(row);

        var title = new Label
        {
            Text = "POWER TEAM",
            CustomMinimumSize = new Vector2(260.0f, 0.0f),
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        row.AddChild(title);

        var objective = new Label
        {
            Text = "3v3 Squad Battle  |  Select allied squad, tap ground to move, tap enemy to attack",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        objective.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(objective);
    }

    private void BuildTacticPanel()
    {
        var panel = new PanelContainer
        {
            Name = "TacticPanel",
            OffsetLeft = 24.0f,
            OffsetTop = 112.0f,
            OffsetRight = 286.0f,
            OffsetBottom = 386.0f,
        };
        ApplyPanelStyle(panel, new Color(0.06f, 0.08f, 0.08f, 0.86f), new Color(0.84f, 0.63f, 0.22f, 0.9f));
        _hudRoot.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        var title = new Label
        {
            Text = "TACTICS",
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(title);

        AddTacticButton(box, AllyTactic.Assault, "Assault");
        AddTacticButton(box, AllyTactic.Hold, "Hold");
        AddTacticButton(box, AllyTactic.Focus, "Focus");
        AddTacticButton(box, AllyTactic.Regroup, "Regroup");
    }

    private void BuildStatusPanel()
    {
        var panel = new PanelContainer
        {
            Name = "StatusPanel",
            OffsetLeft = 24.0f,
            OffsetTop = 650.0f,
            OffsetRight = 1576.0f,
            OffsetBottom = 876.0f,
        };
        ApplyPanelStyle(panel, new Color(0.04f, 0.055f, 0.065f, 0.9f), new Color(0.22f, 0.42f, 0.48f, 0.85f));
        _hudRoot.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        panel.AddChild(margin);

        _statusLabel = new Label
        {
            Text = "Loading prototype...",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 18);
        margin.AddChild(_statusLabel);
    }

    private void AddTacticButton(Container parent, AllyTactic tactic, string text)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(220.0f, 44.0f),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += () => TacticSelected?.Invoke(tactic);
        parent.AddChild(button);
        _tacticButtons[tactic] = button;
    }

    private void AddSkillButton(HBoxContainer row, BattleSkill skill, string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(118.0f, 44.0f),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += () => SkillSelected?.Invoke(skill);
        row.AddChild(button);
        _skillButtons[skill] = button;
    }

    private void SetSkillButtonState(BattleSkill skill, string label, float cooldown, bool available)
    {
        if (!_skillButtons.TryGetValue(skill, out var button))
        {
            return;
        }

        button.Disabled = !available || cooldown > 0.05f;
        if (!available)
        {
            button.Text = $"{label} -";
        }
        else
        {
            button.Text = cooldown > 0.05f ? $"{label} {cooldown:0.0}" : label;
        }
    }

    private void BuildStartPanel()
    {
        _startOverlay = new CenterContainer
        {
            Name = "StartOverlay",
        };
        _startOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_startOverlay);

        var panel = new PanelContainer
        {
            Name = "StartPanel",
            CustomMinimumSize = new Vector2(720.0f, 470.0f),
        };
        ApplyPanelStyle(panel, new Color(0.07f, 0.09f, 0.11f, 0.94f), new Color(0.9f, 0.62f, 0.18f, 0.95f));
        _startOverlay.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 42);
        margin.AddThemeConstantOverride("margin_top", 36);
        margin.AddThemeConstantOverride("margin_right", 42);
        margin.AddThemeConstantOverride("margin_bottom", 36);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        margin.AddChild(box);

        var title = new Label
        {
            Text = "POWER TEAM",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 48);
        box.AddChild(title);

        var subtitle = new Label
        {
            Text = "Top-down squad tactics prototype",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(subtitle);

        var rules = new Label
        {
            Text = "Command three allied squads against three enemy squads.\nSelect a squad, tap ground to move, tap an enemy to attack.\nUse squad skills above the selected unit and destroy the enemy base.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        rules.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(rules);

        var spacer = new Control
        {
            CustomMinimumSize = new Vector2(1.0f, 18.0f),
        };
        box.AddChild(spacer);

        var startButton = new Button
        {
            Text = "Start Battle",
            CustomMinimumSize = new Vector2(320.0f, 64.0f),
            FocusMode = Control.FocusModeEnum.None,
        };
        startButton.AddThemeFontSizeOverride("font_size", 28);
        startButton.Pressed += () => StartRequested?.Invoke();
        box.AddChild(startButton);
    }

    private void BuildSelectedSkillPanel()
    {
        _selectedSkillPanel = new PanelContainer
        {
            Name = "SelectedSkillPanel",
            Visible = false,
            CustomMinimumSize = new Vector2(370.0f, 78.0f),
        };
        ApplyPanelStyle(_selectedSkillPanel, new Color(0.055f, 0.065f, 0.055f, 0.9f), new Color(0.95f, 0.78f, 0.25f, 0.95f));
        AddChild(_selectedSkillPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _selectedSkillPanel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        margin.AddChild(box);

        _selectedSkillTitle = new Label
        {
            Text = "Selected Squad",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _selectedSkillTitle.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(_selectedSkillTitle);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);

        AddSkillButton(row, BattleSkill.Charge, "Charge");
        AddSkillButton(row, BattleSkill.Volley, "Volley");
        AddSkillButton(row, BattleSkill.Rally, "Rally");
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
        ApplyPanelStyle(panel, new Color(0.07f, 0.075f, 0.085f, 0.96f), new Color(0.95f, 0.74f, 0.16f, 0.95f));
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

    private void ApplyPanelStyle(PanelContainer panel, Color background, Color border)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
        };
        panel.AddThemeStyleboxOverride("panel", style);
    }
}
