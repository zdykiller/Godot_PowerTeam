using Godot;

public partial class ControlPoint : Node3D
{
    [Export]
    public float Radius = 3.0f;

    private Label3D _label;
    private MeshInstance3D _body;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label3D>("StateLabel");
        _body = GetNodeOrNull<MeshInstance3D>("Body");
        SetStatus("Neutral", new Color(0.8f, 0.8f, 0.8f, 1.0f));
    }

    public void SetStatus(string text, Color color)
    {
        if (_label != null)
        {
            _label.Text = text;
            _label.Modulate = color;
        }

        if (_body?.MaterialOverride is StandardMaterial3D material)
        {
            material.AlbedoColor = color;
        }
    }

    public bool Contains(Vector3 worldPosition)
    {
        var delta = worldPosition - GlobalPosition;
        delta.Y = 0.0f;
        return delta.Length() <= Radius;
    }
}
