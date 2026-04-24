using Godot;
using System.Text;

public partial class SquadController : Node3D
{
    public struct CombatAction
    {
        public bool LancerImpact;
        public float LancerDamage;
        public float LancerRange;
        public float LancerArcRadians;

        public bool VolleyFired;
        public float VolleyDamage;
        public float VolleyRange;
        public float VolleyArcRadians;
        public int VolleyTargetLimit;
    }

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
    public float LancerImpactDamage = 24.0f;

    [Export]
    public float LancerImpactRange = 2.4f;

    [Export]
    public float LancerImpactArcDegrees = 36.0f;

    [Export]
    public float LancerImpactCooldown = 1.0f;

    [Export]
    public float ArcherRelocateThreshold = 0.7f;

    [Export]
    public float VolleyDamage = 12.0f;

    [Export]
    public float VolleyInterval = 0.9f;

    [Export]
    public float VolleyRange = 15.0f;

    [Export]
    public float VolleyArcDegrees = 20.0f;

    [Export]
    public int VolleyTargetLimit = 2;

    [Export]
    public int LancerUnitCount = 6;

    [Export]
    public int ArcherUnitCount = 5;

    [Export]
    public float FormationLerp = 8.0f;

    private Label3D _stateLabel;
    private MeshInstance3D _bodyMesh;
    private Node3D _unitsRoot;
    private readonly System.Collections.Generic.List<Node3D> _unitNodes = [];
    private Vector3 _velocity = Vector3.Zero;
    private float _chargePower;
    private float _aimFocus;
    private float _volleyCooldown;
    private float _lancerCooldown;
    private float _flashTimer;
    private string _statusText = "Idle";

    public bool IsSelected { get; private set; }
    public Vector3 FacingDirection => -GlobalTransform.Basis.Z;
    public float ChargePower => _chargePower;
    public float AimFocus => _aimFocus;
    public float CurrentSpeed => _velocity.Length();

    public override void _Ready()
    {
        _stateLabel = GetNodeOrNull<Label3D>("StateLabel");
        _bodyMesh = GetNodeOrNull<MeshInstance3D>("Body");
        _unitsRoot = GetNodeOrNull<Node3D>("Units");
        BuildVisualUnits();
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

        UpdateFormation((float)delta);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        UpdateLabel();
    }

    public CombatAction Simulate(float delta, Vector2 command, bool active)
    {
        var action = new CombatAction
        {
            VolleyTargetLimit = VolleyTargetLimit,
            LancerArcRadians = Mathf.DegToRad(LancerImpactArcDegrees),
            VolleyArcRadians = Mathf.DegToRad(VolleyArcDegrees),
            LancerRange = LancerImpactRange,
            VolleyRange = VolleyRange,
            VolleyDamage = VolleyDamage,
            LancerDamage = LancerImpactDamage,
        };

        _lancerCooldown = Mathf.Max(0.0f, _lancerCooldown - delta);
        switch (Role)
        {
            case SquadRole.Lancer:
                SimulateLancer(delta, command, active, ref action);
                break;
            case SquadRole.Archer:
                SimulateArcher(delta, command, active, ref action);
                break;
        }

        GlobalPosition += _velocity * delta;
        UpdateLabel();
        return action;
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

    private void SimulateLancer(float delta, Vector2 command, bool active, ref CombatAction action)
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
            _statusText = _chargePower >= 0.95f ? "Charging (ready)" : "Charging";

            if (_chargePower >= 0.95f && _lancerCooldown <= 0.0f)
            {
                action.LancerImpact = true;
                _chargePower = 0.75f;
                _lancerCooldown = LancerImpactCooldown;
                _flashTimer = 0.25f;
                _statusText = "Impact!";
            }
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

    private void SimulateArcher(float delta, Vector2 command, bool active, ref CombatAction action)
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
                action.VolleyFired = true;
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

    private void BuildVisualUnits()
    {
        if (_unitsRoot == null || _bodyMesh?.Mesh == null)
        {
            return;
        }

        _bodyMesh.Visible = false;
        var material = _bodyMesh.MaterialOverride;
        var count = Role == SquadRole.Lancer ? LancerUnitCount : ArcherUnitCount;

        for (var i = 0; i < count; i++)
        {
            var unit = new MeshInstance3D
            {
                Name = $"Unit{i + 1}",
                Mesh = _bodyMesh.Mesh,
                MaterialOverride = material,
                Scale = Vector3.One * (Role == SquadRole.Lancer ? 0.42f : 0.36f),
            };
            unit.Position = GetFormationSlot(i, 0.0f);
            _unitsRoot.AddChild(unit);
            _unitNodes.Add(unit);
        }
    }

    private void UpdateFormation(float delta)
    {
        if (_unitNodes.Count == 0)
        {
            return;
        }

        var intensity = Role == SquadRole.Lancer ? _chargePower : _aimFocus;
        for (var i = 0; i < _unitNodes.Count; i++)
        {
            var target = GetFormationSlot(i, intensity);
            var unit = _unitNodes[i];
            unit.Position = unit.Position.Lerp(target, FormationLerp * delta);

            var pulse = 1.0f + _flashTimer * 0.25f + (IsSelected ? 0.08f : 0.0f);
            unit.Scale = Vector3.One * (Role == SquadRole.Lancer ? 0.42f : 0.36f) * pulse;
        }
    }

    private Vector3 GetFormationSlot(int index, float intensity)
    {
        if (Role == SquadRole.Lancer)
        {
            var column = index % 2;
            var row = index / 2;
            var width = Mathf.Lerp(0.85f, 0.52f, intensity);
            var depth = Mathf.Lerp(0.75f, 1.05f, intensity);
            return new Vector3((column - 0.5f) * width, 0.0f, row * depth - 0.65f);
        }

        var center = (_unitNodes.Count - 1) * 0.5f;
        var spread = Mathf.Lerp(0.55f, 0.9f, intensity);
        var arc = Mathf.Sin((index - center) * 0.75f) * Mathf.Lerp(0.28f, 0.08f, intensity);
        return new Vector3((index - center) * spread, 0.0f, arc);
    }

    private void FaceDirection(Vector3 direction, float delta)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            return;
        }

        var targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
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
