using Godot;
using System.Linq;

public partial class GameRoot : Node3D
{
    private CommandWheelControl _wheel;
    private Label _hudLabel;
    private SquadController[] _squads = [];
    private int _selectedIndex;

    public override void _Ready()
    {
        _wheel = GetNode<CommandWheelControl>("UI/CommandWheel");
        _hudLabel = GetNode<Label>("UI/HudPanel/Margin/VBox/StatusLabel");

        var squadRoot = GetNode<Node>("Squads");
        _squads = squadRoot.GetChildren().OfType<SquadController>().ToArray();

        if (_squads.Length == 0)
        {
            _hudLabel.Text = "No squads configured.";
            return;
        }

        SetSelectedSquad(0);
        UpdateHud();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Tab)
        {
            SetSelectedSquad((_selectedIndex + 1) % _squads.Length);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        for (var index = 0; index < _squads.Length; index++)
        {
            var isSelected = index == _selectedIndex;
            var command = isSelected ? _wheel.CommandVector : Vector2.Zero;
            var active = isSelected && _wheel.IsActive;
            _squads[index].Simulate((float)delta, command, active);
        }

        UpdateHud();
    }

    private void SetSelectedSquad(int index)
    {
        _selectedIndex = index;
        for (var squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            _squads[squadIndex].SetSelected(squadIndex == _selectedIndex);
        }
    }

    private void UpdateHud()
    {
        if (_squads.Length == 0)
        {
            return;
        }

        var selected = _squads[_selectedIndex];
        _hudLabel.Text = string.Join(
            "\n",
            [
                "Power Team Prototype",
                "LMB drag: command wheel",
                "Tab: switch squad",
                $"Selected: {selected.Name}",
                selected.BuildStatusReport(),
                "",
                "Design read:",
                "Lancer = momentum and charge",
                "Archer = hold, aim, volley / hard drag to reposition",
            ]
        );
    }
}
