using Godot;
using System.Text;

public partial class SquadController : Node3D
{
    public enum SquadRole
    {
        Lancer,
        Archer,
    }

    [Export]
    public SquadRole Role = SquadRole.Lancer;

    [Export]
    public float MoveSpeed = 8.0f;

    [Export]
    public float Acceleration = 20.0f;

    [Export]
    public float RotationLerp = 10.0f;

    [Export]
    public float ChargeSpeedThreshold = 5.0f;

    [Export]
    public float ChargeBuildRate = 0.7f;

    [Export]
    public float AimBuildRate = 1.2f;

    [Export]
    public float ArcherRelocateThreshold = 0.7f;

    [Export]
    public float VolleyInterval = 0.9f;

    private Label3D _stateLabel;
    private MeshInstance3D _bodyMesh;
    private Vector3 _velocity = Vector3.Zero;
    private float _chargePower;
    private float _aimFocus;
    private float _volleyCooldown;
    private float _flashTimer;
    private string _statusText = "Idle";

    public bool IsSelected { get; private set; }

    public override void _Ready()
    {
        _stateLabel = GetNodeOrNull<Label3D>("StateLabel");
        _bodyMesh = GetNodeOrNull<MeshInstance3D>("Body");
        _volleyCooldown = VolleyInterval;
        UpdateLabel();
    }

    public override void _Process(double delta)
    {
        if (_flashTimer > 0.0f)
        {
            _flashTimer = Mathf.Max(0.0f, _flashTimer - (float)delta);
        }

        if (_bodyMesh?.Mesh != null)
        {
            _bodyMesh.Scale = Vector3.One * (1.0f + _flashTimer * 0.25f + (IsSelected ? 0.08f : 0.0f));
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        UpdateLabel();
    }

    public void Simulate(float delta, Vector2 command, bool active)
    {
        switch (Role)
        {
            case SquadRole.Lancer:
                SimulateLancer(delta, command, active);
                break;
            case SquadRole.Archer:
                SimulateArcher(delta, command, active);
                break;
        }

        GlobalPosition += _velocity * delta;
        UpdateLabel();
    }

    public string BuildStatusReport()
    {
        var builder = new StringBuilder();
        builder.Append(Role);
        builder.Append(" | ");
        builder.Append(_statusText);
        builder.Append(" | Speed ");
        builder.Append(_velocity.Length().ToString("0.0"));

        if (Role == SquadRole.Lancer)
        {
            builder.Append(" | Charge ");
            builder.Append(_chargePower.ToString("0.00"));
        }
        else
        {
            builder.Append(" | Focus ");
            builder.Append(_aimFocus.ToString("0.00"));
        }

        return builder.ToString();
    }

    private void SimulateLancer(float delta, Vector2 command, bool active)
    {
        var moveIntent = active ? command : Vector2.Zero;
        var desiredVelocity = ToWorld(moveIntent) * MoveSpeed * moveIntent.Length();
        _velocity = _velocity.MoveToward(desiredVelocity, Acceleration * delta);

        var speed = _velocity.Length();
        if (speed > 0.2f)
        {
            FaceDirection(_velocity.Normalized(), delta);
        }

        if (speed >= ChargeSpeedThreshold)
        {
            _chargePower = Mathf.Clamp(_chargePower + ChargeBuildRate * delta, 0.0f, 1.0f);
            _statusText = "Charging";
        }
        else if (speed > 0.5f)
        {
            _chargePower = Mathf.Max(0.0f, _chargePower - delta * 0.25f);
            _statusText = "Advancing";
        }
        else
        {
            _chargePower = Mathf.Max(0.0f, _chargePower - delta * 0.8f);
            _statusText = "Recovering";
        }
    }

    private void SimulateArcher(float delta, Vector2 command, bool active)
    {
        var strength = active ? command.Length() : 0.0f;
        var direction = strength > 0.01f ? ToWorld(command).Normalized() : -Basis.Z;

        if (strength >= ArcherRelocateThreshold)
        {
            var travel = Mathf.InverseLerp(ArcherRelocateThreshold, 1.0f, strength);
            var desiredVelocity = direction * MoveSpeed * 0.45f * travel;
            _velocity = _velocity.MoveToward(desiredVelocity, Acceleration * delta);
            FaceDirection(direction, delta);
            _aimFocus = Mathf.Max(0.0f, _aimFocus - delta * 1.5f);
            _statusText = "Repositioning";
            _volleyCooldown = Mathf.Min(VolleyInterval, _volleyCooldown + delta * 0.5f);
            return;
        }

        _velocity = _velocity.MoveToward(Vector3.Zero, Acceleration * delta);

        if (active && strength > 0.15f)
        {
            FaceDirection(direction, delta);
            _aimFocus = Mathf.Clamp(_aimFocus + AimBuildRate * delta, 0.0f, 1.0f);
            _volleyCooldown -= delta;
            _statusText = _aimFocus >= 0.9f ? "Volley Ready" : "Aiming";

            if (_volleyCooldown <= 0.0f && _aimFocus >= 0.55f)
            {
                _volleyCooldown = VolleyInterval;
                _flashTimer = 0.2f;
                _statusText = "Volley Fired";
            }

            return;
        }

        _aimFocus = Mathf.Max(0.0f, _aimFocus - delta);
        _volleyCooldown = Mathf.Min(VolleyInterval, _volleyCooldown + delta * 0.4f);
        _statusText = "Holding";
    }

    private Vector3 ToWorld(Vector2 command)
    {
        return new Vector3(command.X, 0.0f, command.Y);
    }

    private void FaceDirection(Vector3 direction, float delta)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            return;
        }

        var targetYaw = Mathf.Atan2(direction.X, direction.Z);
        Rotation = new Vector3(
            Rotation.X,
            Mathf.LerpAngle(Rotation.Y, targetYaw, RotationLerp * delta),
            Rotation.Z
        );
    }

    private void UpdateLabel()
    {
        if (_stateLabel == null)
        {
            return;
        }

        _stateLabel.Text = $"{(IsSelected ? "> " : "")}{Role}\n{_statusText}";
    }
}
