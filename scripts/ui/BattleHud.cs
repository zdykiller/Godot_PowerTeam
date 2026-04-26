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
    public event Action RestartRequested;

    private readonly Dictionary<AllyTactic, Button> _tacticButtons = [];
    private readonly Dictionary<BattleSkill, Button> _skillButtons = [];
    private Label _statusLabel;
    private PanelContainer _selectedSkillPanel;
    private Label _selectedSkillTitle;
    private CenterContainer _resultOverlay;
    private Label _resultTitle;
    private Label _resultSummary;

    public override void _Ready()
    {
        BuildHudPanel();
        BuildSelectedSkillPanel();
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

    private void BuildSelectedSkillPanel()
    {
        _selectedSkillPanel = new PanelContainer
        {
            Name = "SelectedSkillPanel",
            Visible = false,
            CustomMinimumSize = new Vector2(370.0f, 78.0f),
        };
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
