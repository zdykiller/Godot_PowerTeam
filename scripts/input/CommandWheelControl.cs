using Godot;

public partial class CommandWheelControl : Control
{
    [Export]
    public float Radius = 90.0f;

    [Export]
    public Color BaseColor = new(0.08f, 0.08f, 0.10f, 0.65f);

    [Export]
    public Color AccentColor = new(0.95f, 0.78f, 0.18f, 0.95f);

    private bool _active;
    private Vector2 _origin;
    private Vector2 _current;

    public Vector2 CommandVector { get; private set; } = Vector2.Zero;
    public bool IsActive => _active;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mouseButton when mouseButton.ButtonIndex == MouseButton.Left:
                HandleMouseButton(mouseButton);
                break;
            case InputEventMouseMotion mouseMotion when _active:
                UpdateDrag(mouseMotion.Position);
                break;
        }
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active)
        {
            return;
        }

        DrawCircle(_origin, Radius, BaseColor);
        DrawArc(_origin, Radius, 0.0f, Mathf.Tau, 48, AccentColor, 3.0f, true);
        DrawLine(_origin, _current, AccentColor, 4.0f, true);
        DrawCircle(_current, 18.0f, AccentColor);
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (mouseButton.Pressed)
        {
            _active = true;
            _origin = mouseButton.Position;
            _current = mouseButton.Position;
            CommandVector = Vector2.Zero;
            AcceptEvent();
            return;
        }

        _active = false;
        _current = _origin;
        CommandVector = Vector2.Zero;
        AcceptEvent();
    }

    private void UpdateDrag(Vector2 position)
    {
        var offset = position - _origin;
        if (offset.Length() > Radius)
        {
            offset = offset.Normalized() * Radius;
        }

        _current = _origin + offset;
        CommandVector = offset / Radius;
        AcceptEvent();
    }
}
