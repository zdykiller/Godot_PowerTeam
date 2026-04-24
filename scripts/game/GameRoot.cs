using Godot;
using System.Linq;
using System.Collections.Generic;

public partial class GameRoot : Node3D
{
    private CommandWheelControl _wheel;
    private Label _hudLabel;
    private SquadController[] _squads = [];
    private TargetDummy[] _targets = [];
    private ControlPoint _controlPoint;
    private Node3D _debugRoot;
    private int _selectedIndex;
    private string _combatText = "No combat yet";
    private int _targetsBroken;
    private int _comboCount;
    private int _playerScore;
    private int _enemyScore;
    private string _objectiveText = "Hold the point or break enemies.";

    [Export]
    public float EffectLifetime = 0.65f;

    [Export]
    public float VolleyBeamHeight = 0.45f;

    [Export]
    public float VolleyBeamSpacing = 0.35f;

    [Export]
    public int ScoreToWin = 100;

    [Export]
    public float CaptureScorePerSecond = 7.0f;

    [Export]
    public float EnemyCaptureScorePerSecond = 5.0f;

    private float _playerScoreRemainder;
    private float _enemyScoreRemainder;

    public override void _Ready()
    {
        _wheel = GetNode<CommandWheelControl>("UI/CommandWheel");
        _hudLabel = GetNode<Label>("UI/HudPanel/Margin/VBox/StatusLabel");
        _controlPoint = GetNodeOrNull<ControlPoint>("ControlPoint");
        _debugRoot = GetNodeOrNull<Node3D>("Debug");
        if (_debugRoot == null)
        {
            _debugRoot = new Node3D { Name = "Debug" };
            AddChild(_debugRoot);
        }

        var squadRoot = GetNode<Node>("Squads");
        _squads = squadRoot.GetChildren().OfType<SquadController>().ToArray();
        var targetRoot = GetNode<Node>("Targets");
        _targets = targetRoot.GetChildren().OfType<TargetDummy>().ToArray();

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
            var action = _squads[index].Simulate((float)delta, command, active);

            if (action.LancerImpact)
            {
                ResolveLancerImpact(_squads[index], action);
            }

            if (action.VolleyFired)
            {
                ResolveVolley(_squads[index], action);
            }
        }

        SimulateEnemies((float)delta);
        UpdateControlPointScoring((float)delta);
        UpdateHud();
        ClearExpiredEffectTimers(delta);
    }

    private void SetSelectedSquad(int index)
    {
        _selectedIndex = index;
        for (var squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            _squads[squadIndex].SetSelected(squadIndex == _selectedIndex);
        }
    }

    private void ResolveLancerImpact(SquadController squad, SquadController.CombatAction action)
    {
        var best = FindClosestTarget(squad.GlobalPosition, action.LancerRange, squad.FacingDirection, action.LancerArcRadians, _targets);
        if (best == null)
        {
            _combatText = $"{squad.Name} impact missed.";
            SpawnLancerTrail(
                squad.GlobalPosition,
                squad.GlobalPosition + squad.FacingDirection * action.LancerRange,
                new Color(0.93f, 0.55f, 0.06f, 1f)
            );
            return;
        }

        var targetPos = best.GlobalPosition;
        var defeated = best.ApplyDamage(action.LancerDamage, squad.Name);
        best.AddKnockback(squad.FacingDirection, 7.5f + squad.CurrentSpeed * 0.35f);
        RegisterHit(defeated, best.ScoreValue);
        _combatText = defeated ? $"{squad.Name} shattered {best.Name}" : $"{squad.Name} impaled {best.Name}";
        SpawnImpactPoint(targetPos);
        SpawnLancerTrail(
            squad.GlobalPosition,
            targetPos,
            new Color(0.98f, 0.32f, 0.15f, 1f)
        );
    }

    private void ResolveVolley(SquadController squad, SquadController.CombatAction action)
    {
        var volleyTargets = FindTargetsInArc(
            squad.GlobalPosition,
            action.VolleyRange,
            squad.FacingDirection,
            action.VolleyArcRadians,
            _targets
        )
        .Take(action.VolleyTargetLimit <= 0 ? int.MaxValue : action.VolleyTargetLimit)
        .ToArray();

        if (volleyTargets.Length == 0)
        {
            _combatText = $"{squad.Name} volley empty.";
            SpawnVolleyBeam(
                squad.GlobalPosition,
                squad.GlobalPosition + squad.FacingDirection * action.VolleyRange,
                new Color(0.4f, 0.95f, 1f, 1f),
                true
            );
            return;
        }

        var first = volleyTargets[0];
        SpawnVolleyBeam(
            squad.GlobalPosition + Vector3.Up * VolleyBeamHeight,
            first.GlobalPosition + Vector3.Up * VolleyBeamHeight,
            new Color(0.9f, 0.9f, 0.3f, 1f),
            false
        );

        foreach (var target in volleyTargets)
        {
            var pushDirection = target.GlobalPosition - squad.GlobalPosition;
            var defeated = target.ApplyDamage(action.VolleyDamage, squad.Name);
            target.AddKnockback(pushDirection, defeated ? 4.0f : 2.0f);
            RegisterHit(defeated, target.ScoreValue);
            SpawnImpactPoint(target.GlobalPosition);
        }

        _combatText = $"{squad.Name} volleyed {volleyTargets.Length} target(s).";
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
                $"Score: Player {_playerScore}/{ScoreToWin} | Enemy {_enemyScore}/{ScoreToWin}",
                _objectiveText,
                $"Selected: {selected.Name}",
                selected.BuildStatusReport(),
                _combatText,
                $"Broken targets: {_targetsBroken} | Combo: {_comboCount}",
                "",
                "Target Status:",
                .. _targets.Select(target => target.GetStatusText()),
                "",
                "Goal: score by breaking enemies or holding the point",
                "Lancer: drag hard to build charge and ram",
                "Archer: light drag to aim, hard drag to move",
            ]
        );
    }

    private TargetDummy FindClosestTarget(
        Vector3 origin,
        float range,
        Vector3 forward,
        float arcRad,
        TargetDummy[] targets)
    {
        TargetDummy bestTarget = null;
        var bestDistance = float.MaxValue;
        var cosThreshold = Mathf.Cos(arcRad * 0.5f);
        var minDot = -0.15f;

        foreach (var target in targets)
        {
            if (!target.IsAlive)
            {
                continue;
            }

            var toTarget = target.GlobalPosition - origin;
            var distance = toTarget.Length();
            if (distance > range + target.Radius)
            {
                continue;
            }

            if (toTarget.LengthSquared() > 0.0001f)
            {
                var direction = toTarget / distance;
                var dot = direction.Dot(forward);
                if (dot < cosThreshold || dot < minDot)
                {
                    continue;
                }
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTarget = target;
            }
        }

        return bestTarget;
    }

    private TargetDummy[] FindTargetsInArc(
        Vector3 origin,
        float range,
        Vector3 forward,
        float arcRad,
        TargetDummy[] targets)
    {
        var list = new List<TargetDummy>();
        var cosThreshold = Mathf.Cos(arcRad * 0.5f);

        foreach (var target in targets)
        {
            if (!target.IsAlive)
            {
                continue;
            }

            var toTarget = target.GlobalPosition - origin;
            var distance = toTarget.Length();
            if (distance > range + target.Radius || distance < 0.001f)
            {
                continue;
            }

            var direction = toTarget / distance;
            if (direction.Dot(forward) < cosThreshold)
            {
                continue;
            }

            list.Add(target);
        }

        return [.. list.OrderBy(target => (target.GlobalPosition - origin).Length())];
    }

    private void SpawnLancerTrail(Vector3 origin, Vector3 hitPoint, Color color)
    {
        var segmentCount = 12;
        for (var i = 0; i <= segmentCount; i++)
        {
            var t = (float)i / segmentCount;
            SpawnEffectPoint(origin.Lerp(hitPoint, t) + Vector3.Up * 0.4f, 0.06f, color, 0.2f);
        }
    }

    private void SpawnVolleyBeam(Vector3 origin, Vector3 end, Color color, bool miss)
    {
        var distance = (end - origin).Length();
        if (distance < 0.01f)
        {
            return;
        }

        var step = Mathf.Max(VolleyBeamSpacing, 0.12f);
        var pointCount = Mathf.CeilToInt(distance / step) + 2;
        for (var i = 0; i <= pointCount; i++)
        {
            var t = (float)i / pointCount;
            SpawnEffectPoint(origin.Lerp(end, t), miss ? 0.045f : 0.03f, color, miss ? 0.15f : EffectLifetime);
        }
    }

    private void SpawnImpactPoint(Vector3 point)
    {
        SpawnEffectPoint(point + Vector3.Up * 0.12f, 0.18f, new Color(1f, 0.2f, 0.2f, 1f), EffectLifetime);
    }

    private void SpawnEffectPoint(Vector3 worldPos, float scale, Color color, float lifetime)
    {
        var sphere = new SphereMesh
        {
            Radius = scale * 0.55f,
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Emission = color,
        };
        var mesh = new MeshInstance3D
        {
            Mesh = sphere,
            MaterialOverride = material,
            Position = worldPos,
            Scale = Vector3.One * scale * 2.2f,
        };
        var timer = new Timer
        {
            OneShot = true,
            WaitTime = lifetime,
            Autostart = true,
        };
        timer.Timeout += () =>
        {
            timer.QueueFree();
            mesh.QueueFree();
        };
        _debugRoot.AddChild(mesh);
        _debugRoot.AddChild(timer);
        timer.Start();
    }

    private void SimulateEnemies(float delta)
    {
        if (_controlPoint == null)
        {
            return;
        }

        foreach (var target in _targets)
        {
            target.SimulateAi(_controlPoint.GlobalPosition, delta);
        }
    }

    private void UpdateControlPointScoring(float delta)
    {
        if (_controlPoint == null)
        {
            return;
        }

        var playerOnPoint = _squads.Any(squad => _controlPoint.Contains(squad.GlobalPosition));
        var enemyOnPoint = _targets.Any(target => target.IsAlive && _controlPoint.Contains(target.GlobalPosition));

        if (playerOnPoint && !enemyOnPoint)
        {
            AddScore(ref _playerScore, ref _playerScoreRemainder, CaptureScorePerSecond * delta);
            _objectiveText = "Player is holding the point.";
            _controlPoint.SetStatus("Player +", new Color(0.96f, 0.72f, 0.14f, 1.0f));
            return;
        }

        if (enemyOnPoint && !playerOnPoint)
        {
            AddScore(ref _enemyScore, ref _enemyScoreRemainder, EnemyCaptureScorePerSecond * delta);
            _objectiveText = "Enemies are scoring on the point.";
            _controlPoint.SetStatus("Enemy +", new Color(0.9f, 0.15f, 0.12f, 1.0f));
            return;
        }

        if (playerOnPoint && enemyOnPoint)
        {
            _objectiveText = "Point contested.";
            _controlPoint.SetStatus("Contested", new Color(1.0f, 0.95f, 0.2f, 1.0f));
            return;
        }

        _objectiveText = "Move onto the point or break enemies.";
        _controlPoint.SetStatus("Neutral", new Color(0.8f, 0.8f, 0.8f, 1.0f));
    }

    private void AddScore(ref int score, ref float remainder, float amount)
    {
        remainder += amount;
        var whole = Mathf.FloorToInt(remainder);
        if (whole <= 0)
        {
            return;
        }

        score = Mathf.Min(ScoreToWin, score + whole);
        remainder -= whole;
    }

    private void RegisterHit(bool defeated, int scoreValue)
    {
        _comboCount++;
        if (defeated)
        {
            _targetsBroken++;
            _playerScore = Mathf.Min(ScoreToWin, _playerScore + scoreValue);
        }
    }

    private void ClearExpiredEffectTimers(double _delta)
    {
        if (_debugRoot == null)
        {
            return;
        }

        var remove = new List<Node>();
        foreach (var child in _debugRoot.GetChildren())
        {
            if (child is not MeshInstance3D mesh)
            {
                continue;
            }

            if (!mesh.Visible)
            {
                remove.Add(child);
            }
        }

        foreach (var child in remove)
        {
            child.QueueFree();
        }
    }
}
