using Godot;
using System.Text;

public partial class SquadController : Node3D
{
    public struct CombatAction
    {
        public bool LancerImpact;
        public float LancerDamage;
        public float LancerMoraleDamage;
        public float LancerRange;
        public float LancerArcRadians;

        public bool VolleyFired;
        public float VolleyDamage;
        public float VolleyMoraleDamage;
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
    public int TeamId = 0;

    [Export]
    public bool IsPlayerControlled;

    [Export]
    public float MaxHealth = 100.0f;

    [Export]
    public float MaxMorale = 100.0f;

    [Export]
    public float Defense = 0.0f;

    [Export]
    public float HitRadius = 1.45f;

    [Export]
    public float RegroupTime = 5.0f;

    [Export]
    public float RegroupRadius = 4.5f;

    [Export]
    public float RegroupHealthRatio = 0.65f;

    [Export]
    public float RegroupMoraleRatio = 0.85f;

    [Export]
    public float RoutMoveSpeedMultiplier = 1.15f;

    [Export]
    public float BaseDamageOnDefeat = 35.0f;

    [Export]
    public float BaseAttackDamagePerSecond = 5.0f;

    [Export]
    public float BaseAttackRange = 3.0f;

    [Export]
    public float MeleeDamagePerSecond = 0.8f;

    [Export]
    public float MeleeMoraleDamagePerSecond = 1.1f;

    [Export]
    public float EngagementRadius = 3.0f;

    [Export]
    public float EngagedMoveMultiplier = 0.28f;

    [Export]
    public float DisengageMoveMultiplier = 0.75f;

    [Export]
    public float DisengageDelay = 0.55f;

    [Export]
    public float MoveSpeed = 5.2f;

    [Export]
    public float Acceleration = 14.0f;

    [Export]
    public float RotationLerp = 10.0f;

    [Export]
    public float ChargeSpeedThreshold = 3.4f;

    [Export]
    public float ChargeBuildRate = 0.7f;

    [Export]
    public float AimBuildRate = 1.2f;

    [Export]
    public float LancerImpactDamage = 12.0f;

    [Export]
    public float LancerImpactMoraleDamage = 16.0f;

    [Export]
    public float LancerImpactRange = 2.4f;

    [Export]
    public float LancerImpactArcDegrees = 36.0f;

    [Export]
    public float LancerImpactCooldown = 1.0f;

    [Export]
    public float ArcherRelocateThreshold = 0.7f;

    [Export]
    public float VolleyDamage = 6.0f;

    [Export]
    public float VolleyMoraleDamage = 8.0f;

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

    [Export]
    public PackedScene UnitModelScene;

    [Export]
    public Vector3 UnitModelScale = Vector3.One;

    [Export]
    public Vector3 UnitModelRotationDegrees = Vector3.Zero;

    [Export]
    public float TeamDiscRadius = 1.95f;

    [Export]
    public float MoraleDiscRadius = 1.25f;

    [Export]
    public int ImpactedUnitMin = 1;

    [Export]
    public int ImpactedUnitMax = 2;

    [Export]
    public float ImpactRecoverTime = 0.9f;

    [Export]
    public float ImpactScatterDistance = 1.1f;

    [Export]
    public float ImpactLift = 0.35f;

    [Export]
    public float KnockdownTiltDegrees = 72.0f;

    private Label3D _stateLabel;
    private MeshInstance3D _bodyMesh;
    private Node3D _unitsRoot;
    private MeshInstance3D _teamDisc;
    private MeshInstance3D _moraleDisc;
    private StandardMaterial3D _moraleDiscMaterial;
    private readonly System.Collections.Generic.List<Node3D> _unitNodes = [];
    private readonly System.Collections.Generic.List<UnitImpactState> _impactStates = [];
    private Vector3 _velocity = Vector3.Zero;
    private float _chargePower;
    private float _aimFocus;
    private float _formationIntent;
    private float _volleyCooldown;
    private float _lancerCooldown;
    private float _flashTimer;
    private float _health;
    private float _morale;
    private float _regroupTimer;
    private float _disengageTimer;
    private Vector3 _spawnPosition;
    private Vector3 _homeBasePosition;
    private Vector3 _engagementDirection = Vector3.Forward;
    private bool _isEngaged;
    private string _statusText = "Idle";
    private SquadState _state = SquadState.Active;

    private enum SquadState
    {
        Active,
        Routed,
        Regrouping,
    }

    private sealed class UnitImpactState
    {
        public float Timer;
        public float Duration;
        public Vector3 Offset;
        public Vector3 RotationDegrees;
    }

    public bool IsSelected { get; private set; }
    public bool IsAlive => _state == SquadState.Active;
    public Vector3 FacingDirection => -GlobalTransform.Basis.Z;
    public float ChargePower => _chargePower;
    public float AimFocus => _aimFocus;
    public float CurrentSpeed => _velocity.Length();
    public float Radius => HitRadius;
    public float Health => _health;
    public float Morale => _morale;

    public override void _Ready()
    {
        _stateLabel = GetNodeOrNull<Label3D>("StateLabel");
        _bodyMesh = GetNodeOrNull<MeshInstance3D>("Body");
        _unitsRoot = GetNodeOrNull<Node3D>("Units");
        _spawnPosition = GlobalPosition;
        _homeBasePosition = _spawnPosition;
        _health = MaxHealth;
        _morale = MaxMorale;
        BuildVisualUnits();
        BuildSquadIndicators();
        RefreshVisualFeedback();
        _volleyCooldown = VolleyInterval;
        UpdateLabel();
    }

    public override void _Process(double delta)
    {
        if (_flashTimer > 0.0f)
        {
            _flashTimer = Mathf.Max(0.0f, _flashTimer - (float)delta);
        }
        UpdateImpactTimers((float)delta);

        if (_state == SquadState.Routed)
        {
            SimulateRout((float)delta);
            UpdateFormation((float)delta);
            RefreshVisualFeedback();
            UpdateLabel();
            return;
        }

        if (_state == SquadState.Regrouping)
        {
            _regroupTimer = Mathf.Max(0.0f, _regroupTimer - (float)delta);
            if (_regroupTimer <= 0.0f)
            {
                Regroup();
            }
            RefreshVisualFeedback();
            UpdateLabel();
            return;
        }

        if (_bodyMesh?.Mesh != null)
        {
            _bodyMesh.Scale = Vector3.One * (1.0f + _flashTimer * 0.25f + (IsSelected ? 0.08f : 0.0f));
        }

        UpdateFormation((float)delta);
        RefreshVisualFeedback();
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        RefreshVisualFeedback();
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
            VolleyMoraleDamage = VolleyMoraleDamage,
            LancerDamage = LancerImpactDamage,
            LancerMoraleDamage = LancerImpactMoraleDamage,
        };

        if (!IsAlive)
        {
            return action;
        }

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
        builder.Append(" | HP ");
        builder.Append(Mathf.CeilToInt(_health));
        builder.Append("/");
        builder.Append(Mathf.CeilToInt(MaxHealth));
        builder.Append(" | Morale ");
        builder.Append(Mathf.CeilToInt(_morale));
        builder.Append("/");
        builder.Append(Mathf.CeilToInt(MaxMorale));
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

    public void SetHomeBasePosition(Vector3 position)
    {
        _homeBasePosition = position;
    }

    public void SetEngagement(SquadController enemy)
    {
        _isEngaged = enemy != null && IsAlive;
        if (!_isEngaged)
        {
            _disengageTimer = 0.0f;
            return;
        }

        var toEnemy = enemy.GlobalPosition - GlobalPosition;
        toEnemy.Y = 0.0f;
        if (toEnemy.LengthSquared() > 0.001f)
        {
            _engagementDirection = toEnemy.Normalized();
        }
    }

    public bool ApplyDamage(float damage, float moraleDamage)
    {
        if (!IsAlive)
        {
            return false;
        }

        _health = Mathf.Max(0.0f, _health - Mathf.Max(1.0f, damage - Defense));
        _morale = Mathf.Max(0.0f, _morale - moraleDamage);
        _flashTimer = 0.25f;
        RefreshVisualFeedback();

        if (_health <= 0.0f || _morale <= 0.0f)
        {
            Rout(_health <= 0.0f ? "Broken" : "Routed");
            return true;
        }

        UpdateLabel();
        return false;
    }

    public void AddKnockback(Vector3 direction, float strength)
    {
        if (!IsAlive || direction.LengthSquared() <= 0.001f)
        {
            return;
        }

        _velocity += direction.Normalized() * strength;
    }

    public void PlayImpactReaction(Vector3 worldDirection)
    {
        if (_unitNodes.Count == 0 || worldDirection.LengthSquared() <= 0.001f)
        {
            return;
        }

        var visibleIndexes = new System.Collections.Generic.List<int>();
        for (var i = 0; i < _unitNodes.Count; i++)
        {
            if (_unitNodes[i].Visible)
            {
                visibleIndexes.Add(i);
            }
        }

        if (visibleIndexes.Count == 0)
        {
            return;
        }

        var hitCount = Mathf.Clamp(GD.RandRange(ImpactedUnitMin, ImpactedUnitMax), 1, visibleIndexes.Count);
        var localDirection = GlobalTransform.Basis.Inverse() * worldDirection.Normalized();
        localDirection.Y = 0.0f;
        if (localDirection.LengthSquared() <= 0.001f)
        {
            localDirection = Vector3.Back;
        }
        localDirection = localDirection.Normalized();

        for (var i = 0; i < hitCount; i++)
        {
            var pick = GD.RandRange(0, visibleIndexes.Count - 1);
            var unitIndex = visibleIndexes[pick];
            visibleIndexes.RemoveAt(pick);

            var side = new Vector3(-localDirection.Z, 0.0f, localDirection.X) * (float)GD.RandRange(-0.45, 0.45);
            var offset = (localDirection + side).Normalized() * (float)GD.RandRange(ImpactScatterDistance * 0.55f, ImpactScatterDistance);
            _impactStates[unitIndex].Timer = ImpactRecoverTime;
            _impactStates[unitIndex].Duration = ImpactRecoverTime;
            _impactStates[unitIndex].Offset = offset;
            _impactStates[unitIndex].RotationDegrees = new Vector3(
                KnockdownTiltDegrees * (float)GD.RandRange(0.75, 1.05),
                0.0f,
                KnockdownTiltDegrees * 0.25f * (float)GD.RandRange(-1.0, 1.0)
            );
        }
    }

    public string GetStatusText()
    {
        if (IsAlive)
        {
            return $"{Name}: HP {Mathf.CeilToInt(_health)}/{Mathf.CeilToInt(MaxHealth)} M {Mathf.CeilToInt(_morale)}/{Mathf.CeilToInt(MaxMorale)}";
        }

        if (_state == SquadState.Routed)
        {
            return $"{Name}: Routed";
        }

        return $"{Name}: Regrouping ({_regroupTimer:0.0}s)";
    }

    private void SimulateLancer(float delta, Vector2 command, bool active, ref CombatAction action)
    {
        var moveIntent = active ? command : Vector2.Zero;
        var desiredVelocity = ToWorld(moveIntent) * MoveSpeed * moveIntent.Length();
        desiredVelocity = ApplyEngagementDrag(delta, desiredVelocity);
        _velocity = _velocity.MoveToward(desiredVelocity, Acceleration * delta);

        var speed = _velocity.Length();
        _formationIntent = Mathf.MoveToward(_formationIntent, Mathf.Clamp(speed / MoveSpeed, 0.0f, 1.0f), delta * 4.0f);
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
            _statusText = _isEngaged ? "Locked" : "Advancing";
        }
        else
        {
            _chargePower = Mathf.Max(0.0f, _chargePower - delta * 0.8f);
            _statusText = _isEngaged ? "Holding Line" : "Recovering";
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
            desiredVelocity = ApplyEngagementDrag(delta, desiredVelocity);
            _velocity = _velocity.MoveToward(desiredVelocity, Acceleration * delta);
            _formationIntent = Mathf.MoveToward(_formationIntent, 0.0f, delta * 5.0f);
            FaceDirection(direction, delta);
            _aimFocus = Mathf.Max(0.0f, _aimFocus - delta * 1.5f);
            _statusText = _isEngaged ? "Disengaging" : "Repositioning";
            _volleyCooldown = Mathf.Min(VolleyInterval, _volleyCooldown + delta * 0.5f);
            return;
        }

        _velocity = _velocity.MoveToward(ApplyEngagementDrag(delta, Vector3.Zero), Acceleration * delta);

        if (active && strength > 0.15f)
        {
            FaceDirection(direction, delta);
            _aimFocus = Mathf.Clamp(_aimFocus + AimBuildRate * delta, 0.0f, 1.0f);
            _formationIntent = Mathf.MoveToward(_formationIntent, Mathf.Max(0.35f, _aimFocus), delta * 4.0f);
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
        _formationIntent = Mathf.MoveToward(_formationIntent, 0.15f, delta * 2.5f);
        _volleyCooldown = Mathf.Min(VolleyInterval, _volleyCooldown + delta * 0.4f);
        _statusText = _isEngaged ? "Pinned" : "Holding";
    }

    private Vector3 ToWorld(Vector2 command)
    {
        return new Vector3(command.X, 0.0f, command.Y);
    }

    private Vector3 ApplyEngagementDrag(float delta, Vector3 desiredVelocity)
    {
        if (!_isEngaged || desiredVelocity.LengthSquared() <= 0.01f)
        {
            _disengageTimer = Mathf.Max(0.0f, _disengageTimer - delta * 1.5f);
            return _isEngaged ? desiredVelocity * EngagedMoveMultiplier : desiredVelocity;
        }

        var isPullingAway = desiredVelocity.Normalized().Dot(-_engagementDirection) > 0.45f;
        if (isPullingAway)
        {
            _disengageTimer = Mathf.Min(DisengageDelay, _disengageTimer + delta);
        }
        else
        {
            _disengageTimer = Mathf.Max(0.0f, _disengageTimer - delta * 2.0f);
        }

        var ratio = DisengageDelay <= 0.0f ? 1.0f : _disengageTimer / DisengageDelay;
        var multiplier = isPullingAway
            ? Mathf.Lerp(EngagedMoveMultiplier, DisengageMoveMultiplier, ratio)
            : EngagedMoveMultiplier;
        return desiredVelocity * multiplier;
    }

    private void BuildVisualUnits()
    {
        if (_unitsRoot == null || _bodyMesh?.Mesh == null)
        {
            return;
        }

        _bodyMesh.Visible = false;
        var count = Role == SquadRole.Lancer ? LancerUnitCount : ArcherUnitCount;

        for (var i = 0; i < count; i++)
        {
            var unit = CreateVisualUnit(i);
            unit.Position = GetFormationSlot(i, 0.0f);
            _unitsRoot.AddChild(unit);
            _unitNodes.Add(unit);
            _impactStates.Add(new UnitImpactState());
        }
    }

    private Node3D CreateVisualUnit(int index)
    {
        if (UnitModelScene != null)
        {
            var modelRoot = new Node3D
            {
                Name = $"Unit{index + 1}",
                Scale = UnitModelScale,
                RotationDegrees = UnitModelRotationDegrees,
            };
            var modelInstance = UnitModelScene.Instantiate<Node3D>();
            modelRoot.AddChild(modelInstance);
            return modelRoot;
        }

        return new MeshInstance3D
        {
            Name = $"Unit{index + 1}",
            Mesh = _bodyMesh.Mesh,
            MaterialOverride = _bodyMesh.MaterialOverride,
            Scale = Vector3.One * GetUnitVisualScale(),
        };
    }

    private float GetUnitVisualScale()
    {
        return Role == SquadRole.Lancer ? 0.42f : 0.48f;
    }

    private void BuildSquadIndicators()
    {
        if (_teamDisc != null)
        {
            return;
        }

        _teamDisc = CreateDisc("TeamDisc", TeamDiscRadius, GetTeamColor(), -0.88f);
        _moraleDiscMaterial = CreateMaterial(GetMoraleColor(1.0f));
        _moraleDisc = CreateDisc("MoraleDisc", MoraleDiscRadius, GetMoraleColor(1.0f), -0.84f);
        _moraleDisc.MaterialOverride = _moraleDiscMaterial;
        AddChild(_teamDisc);
        AddChild(_moraleDisc);
    }

    private MeshInstance3D CreateDisc(string name, float radius, Color color, float y)
    {
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = 0.035f,
            },
            MaterialOverride = CreateMaterial(color),
            Position = new Vector3(0.0f, y, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private StandardMaterial3D CreateMaterial(Color color)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.85f,
        };
    }

    private Color GetTeamColor()
    {
        return TeamId == 0 ? new Color(0.95f, 0.67f, 0.12f, 1.0f) : new Color(0.78f, 0.08f, 0.08f, 1.0f);
    }

    private Color GetMoraleColor(float moraleRatio)
    {
        if (_state == SquadState.Routed)
        {
            return new Color(1.0f, 0.18f, 0.08f, 1.0f);
        }

        if (_state == SquadState.Regrouping)
        {
            return new Color(0.25f, 0.6f, 1.0f, 1.0f);
        }

        return moraleRatio switch
        {
            > 0.55f => new Color(0.25f, 0.9f, 0.35f, 1.0f),
            > 0.25f => new Color(1.0f, 0.75f, 0.12f, 1.0f),
            _ => new Color(1.0f, 0.22f, 0.12f, 1.0f),
        };
    }

    private void RefreshVisualFeedback()
    {
        UpdateCasualtyVisuals();
        UpdateSquadIndicators();
    }

    private void UpdateCasualtyVisuals()
    {
        if (_unitNodes.Count == 0)
        {
            return;
        }

        var healthRatio = MaxHealth <= 0.0f ? 0.0f : Mathf.Clamp(_health / MaxHealth, 0.0f, 1.0f);
        var visibleCount = Mathf.CeilToInt(_unitNodes.Count * healthRatio);

        if (_state == SquadState.Routed || _state == SquadState.Regrouping)
        {
            visibleCount = Mathf.Max(1, visibleCount);
        }
        else
        {
            visibleCount = Mathf.Clamp(visibleCount, 1, _unitNodes.Count);
        }

        for (var i = 0; i < _unitNodes.Count; i++)
        {
            _unitNodes[i].Visible = i < visibleCount;
        }
    }

    private void UpdateSquadIndicators()
    {
        if (_teamDisc == null || _moraleDisc == null || _moraleDiscMaterial == null)
        {
            return;
        }

        var healthRatio = MaxHealth <= 0.0f ? 0.0f : Mathf.Clamp(_health / MaxHealth, 0.0f, 1.0f);
        var moraleRatio = MaxMorale <= 0.0f ? 0.0f : Mathf.Clamp(_morale / MaxMorale, 0.0f, 1.0f);
        var selectedPulse = IsSelected ? 1.12f : 1.0f;

        _teamDisc.Scale = new Vector3(selectedPulse, 1.0f, selectedPulse);
        _moraleDisc.Scale = new Vector3(Mathf.Lerp(0.35f, 1.0f, moraleRatio), 1.0f, Mathf.Lerp(0.35f, 1.0f, healthRatio));
        _moraleDiscMaterial.AlbedoColor = GetMoraleColor(moraleRatio);
    }

    private void SetUnitScale(Node3D unit, float pulse)
    {
        if (UnitModelScene != null)
        {
            unit.Scale = UnitModelScale * pulse;
            return;
        }

        unit.Scale = Vector3.One * GetUnitVisualScale() * pulse;
    }

    private void UpdateFormation(float delta)
    {
        if (_unitNodes.Count == 0)
        {
            return;
        }

        var intensity = Mathf.Clamp(_formationIntent, 0.0f, 1.0f);
        for (var i = 0; i < _unitNodes.Count; i++)
        {
            var target = GetFormationSlot(i, intensity);
            var unit = _unitNodes[i];
            var impactState = _impactStates[i];
            var impactRatio = GetImpactRatio(impactState);
            if (impactRatio > 0.0f)
            {
                var phase = 1.0f - impactRatio;
                target += impactState.Offset * impactRatio;
                target.Y += Mathf.Sin(phase * Mathf.Pi) * ImpactLift;
            }

            unit.Position = unit.Position.Lerp(target, FormationLerp * delta);
            unit.RotationDegrees = GetBaseUnitRotationDegrees().Lerp(
                GetBaseUnitRotationDegrees() + impactState.RotationDegrees,
                impactRatio
            );

            var pulse = 1.0f + _flashTimer * 0.25f + (IsSelected ? 0.08f : 0.0f);
            SetUnitScale(unit, pulse);
        }
    }

    private void UpdateImpactTimers(float delta)
    {
        foreach (var state in _impactStates)
        {
            if (state.Timer <= 0.0f)
            {
                continue;
            }

            state.Timer = Mathf.Max(0.0f, state.Timer - delta);
        }
    }

    private float GetImpactRatio(UnitImpactState state)
    {
        if (state.Timer <= 0.0f || state.Duration <= 0.0f)
        {
            return 0.0f;
        }

        var ratio = state.Timer / state.Duration;
        return ratio * ratio;
    }

    private Vector3 GetBaseUnitRotationDegrees()
    {
        return UnitModelScene != null ? UnitModelRotationDegrees : Vector3.Zero;
    }

    private Vector3 GetFormationSlot(int index, float intensity)
    {
        if (Role == SquadRole.Lancer)
        {
            var column = index % 2;
            var row = index / 2;
            var width = Mathf.Lerp(1.35f, 0.28f, intensity);
            var depth = Mathf.Lerp(0.55f, 1.45f, intensity);
            var forwardPull = Mathf.Lerp(0.0f, -0.75f, intensity);
            return new Vector3((column - 0.5f) * width, 0.0f, row * depth - 0.55f + forwardPull);
        }

        var center = (_unitNodes.Count - 1) * 0.5f;
        var spread = Mathf.Lerp(0.45f, 1.15f, intensity);
        var arc = Mathf.Sin((index - center) * 0.75f) * Mathf.Lerp(0.45f, 0.02f, intensity);
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

        _stateLabel.Text = $"{(IsSelected ? "> " : "")}{Role} T{TeamId}\n{_statusText}\nHP {Mathf.CeilToInt(_health)} M {Mathf.CeilToInt(_morale)}";
    }

    private void Rout(string reason)
    {
        _chargePower = 0.0f;
        _aimFocus = 0.0f;
        _formationIntent = 1.0f;
        _state = SquadState.Routed;
        _statusText = reason;
        RefreshVisualFeedback();
        UpdateLabel();
    }

    private void SimulateRout(float delta)
    {
        var toHome = _homeBasePosition - GlobalPosition;
        toHome.Y = 0.0f;

        if (toHome.Length() <= RegroupRadius)
        {
            _state = SquadState.Regrouping;
            _velocity = Vector3.Zero;
            _regroupTimer = RegroupTime;
            _statusText = "Regrouping";
            return;
        }

        var direction = toHome.Normalized();
        _velocity = _velocity.MoveToward(direction * MoveSpeed * RoutMoveSpeedMultiplier, Acceleration * delta);
        FaceDirection(_velocity.Normalized(), delta);
        GlobalPosition += _velocity * delta;
    }

    private void Regroup()
    {
        _health = MaxHealth * RegroupHealthRatio;
        _morale = MaxMorale * RegroupMoraleRatio;
        _regroupTimer = 0.0f;
        _state = SquadState.Active;
        _statusText = "Regrouped";
        RefreshVisualFeedback();
        UpdateLabel();
    }
}
