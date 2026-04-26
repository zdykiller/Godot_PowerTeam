using Godot;
using System.Linq;
using System.Collections.Generic;

public partial class GameRoot : Node3D
{
    private CommandWheelControl _wheel;
    private Label _hudLabel;
    private SquadController[] _squads = [];
    private BaseCore[] _bases = [];
    private Node3D _debugRoot;
    private SquadController _playerSquad;
    private string _combatText = "Destroy the enemy base.";
    private string _gameState = "Battle";

    [Export]
    public float EffectLifetime = 0.65f;

    [Export]
    public float VolleyBeamHeight = 0.45f;

    [Export]
    public float VolleyBeamSpacing = 0.35f;

    [Export]
    public float AiEngageRange = 9.5f;

    [Export]
    public float AiCommandStrength = 0.85f;

    public override void _Ready()
    {
        _wheel = GetNode<CommandWheelControl>("UI/CommandWheel");
        _hudLabel = GetNode<Label>("UI/HudPanel/Margin/VBox/StatusLabel");
        _debugRoot = GetNodeOrNull<Node3D>("Debug");
        if (_debugRoot == null)
        {
            _debugRoot = new Node3D { Name = "Debug" };
            AddChild(_debugRoot);
        }

        _squads = GetNode<Node>("Squads").GetChildren().OfType<SquadController>().ToArray();
        _bases = GetNode<Node>("Bases").GetChildren().OfType<BaseCore>().ToArray();
        _playerSquad = _squads.FirstOrDefault(squad => squad.IsPlayerControlled) ?? _squads.FirstOrDefault(squad => squad.TeamId == 0);

        if (_playerSquad == null || _bases.Length < 2)
        {
            _hudLabel.Text = "Battle scene missing player squad or bases.";
            return;
        }

        SetSelectedSquad(_playerSquad);
        UpdateHud();
    }

    public override void _Process(double delta)
    {
        if (_playerSquad == null || _gameState != "Battle")
        {
            UpdateHud();
            return;
        }

        foreach (var squad in _squads)
        {
            var command = Vector2.Zero;
            var active = false;

            if (squad == _playerSquad)
            {
                command = _wheel.CommandVector;
                active = _wheel.IsActive;
            }
            else
            {
                command = BuildAiCommand(squad);
                active = command.LengthSquared() > 0.001f;
            }

            var action = squad.Simulate((float)delta, command, active);
            if (action.LancerImpact)
            {
                ResolveLancerImpact(squad, action);
            }

            if (action.VolleyFired)
            {
                ResolveVolley(squad, action);
            }
        }

        ResolveBaseAttacks((float)delta);
        CheckVictory();
        UpdateHud();
        ClearExpiredEffectTimers();
    }

    private void SetSelectedSquad(SquadController selected)
    {
        foreach (var squad in _squads)
        {
            squad.SetSelected(squad == selected);
        }
    }

    private Vector2 BuildAiCommand(SquadController squad)
    {
        if (!squad.IsAlive)
        {
            return Vector2.Zero;
        }

        var nearestEnemy = FindNearestEnemySquad(squad, AiEngageRange);
        var targetPosition = nearestEnemy?.GlobalPosition ?? GetEnemyBase(squad.TeamId)?.GlobalPosition ?? squad.GlobalPosition;
        var toTarget = targetPosition - squad.GlobalPosition;
        toTarget.Y = 0.0f;

        if (toTarget.LengthSquared() <= 0.05f)
        {
            return Vector2.Zero;
        }

        var direction = toTarget.Normalized();
        var strength = squad.Role == SquadController.SquadRole.Archer && nearestEnemy != null ? 0.45f : AiCommandStrength;
        return new Vector2(direction.X, direction.Z) * strength;
    }

    private void ResolveLancerImpact(SquadController squad, SquadController.CombatAction action)
    {
        if (!squad.IsAlive)
        {
            return;
        }

        var target = FindClosestEnemyInArc(squad, action.LancerRange, action.LancerArcRadians);
        if (target == null)
        {
            var enemyBase = GetEnemyBase(squad.TeamId);
            if (enemyBase != null && IsBaseInRange(squad, enemyBase, action.LancerRange, action.LancerArcRadians))
            {
                DamageBase(enemyBase, action.LancerDamage * 0.7f, squad.Name);
                SpawnLancerTrail(squad.GlobalPosition, enemyBase.GlobalPosition, new Color(1.0f, 0.55f, 0.15f, 1.0f));
                return;
            }

            _combatText = $"{squad.Name} charge missed.";
            SpawnLancerTrail(
                squad.GlobalPosition,
                squad.GlobalPosition + squad.FacingDirection * action.LancerRange,
                new Color(0.93f, 0.55f, 0.06f, 1.0f)
            );
            return;
        }

        var targetPos = target.GlobalPosition;
        var defeated = target.ApplyDamage(action.LancerDamage);
        target.AddKnockback(squad.FacingDirection, 7.5f + squad.CurrentSpeed * 0.35f);
        if (defeated)
        {
            DamageBase(GetBase(target.TeamId), target.BaseDamageOnDefeat, target.Name);
        }

        _combatText = defeated ? $"{squad.Name} broke {target.Name}" : $"{squad.Name} charged {target.Name}";
        SpawnImpactPoint(targetPos);
        SpawnLancerTrail(squad.GlobalPosition, targetPos, new Color(0.98f, 0.32f, 0.15f, 1.0f));
    }

    private void ResolveVolley(SquadController squad, SquadController.CombatAction action)
    {
        if (!squad.IsAlive)
        {
            return;
        }

        var targets = FindEnemiesInArc(squad, action.VolleyRange, action.VolleyArcRadians)
            .Take(action.VolleyTargetLimit <= 0 ? int.MaxValue : action.VolleyTargetLimit)
            .ToArray();

        if (targets.Length == 0)
        {
            var enemyBase = GetEnemyBase(squad.TeamId);
            if (enemyBase != null && IsBaseInRange(squad, enemyBase, action.VolleyRange, action.VolleyArcRadians))
            {
                DamageBase(enemyBase, action.VolleyDamage * 0.45f, squad.Name);
                SpawnVolleyBeam(
                    squad.GlobalPosition + Vector3.Up * VolleyBeamHeight,
                    enemyBase.GlobalPosition + Vector3.Up * VolleyBeamHeight,
                    new Color(0.9f, 0.9f, 0.3f, 1.0f),
                    false
                );
                return;
            }

            _combatText = $"{squad.Name} volley missed.";
            SpawnVolleyBeam(
                squad.GlobalPosition,
                squad.GlobalPosition + squad.FacingDirection * action.VolleyRange,
                new Color(0.4f, 0.95f, 1.0f, 1.0f),
                true
            );
            return;
        }

        SpawnVolleyBeam(
            squad.GlobalPosition + Vector3.Up * VolleyBeamHeight,
            targets[0].GlobalPosition + Vector3.Up * VolleyBeamHeight,
            new Color(0.9f, 0.9f, 0.3f, 1.0f),
            false
        );

        foreach (var target in targets)
        {
            var pushDirection = target.GlobalPosition - squad.GlobalPosition;
            var defeated = target.ApplyDamage(action.VolleyDamage);
            target.AddKnockback(pushDirection, defeated ? 4.0f : 2.0f);
            if (defeated)
            {
                DamageBase(GetBase(target.TeamId), target.BaseDamageOnDefeat, target.Name);
            }
            SpawnImpactPoint(target.GlobalPosition);
        }

        _combatText = $"{squad.Name} volleyed {targets.Length} squad(s).";
    }

    private void ResolveBaseAttacks(float delta)
    {
        foreach (var squad in _squads)
        {
            if (!squad.IsAlive)
            {
                continue;
            }

            var enemyBase = GetEnemyBase(squad.TeamId);
            if (enemyBase == null || !enemyBase.IsAlive)
            {
                continue;
            }

            var distance = FlatDistance(squad.GlobalPosition, enemyBase.GlobalPosition);
            if (distance <= squad.BaseAttackRange + enemyBase.Radius)
            {
                DamageBase(enemyBase, squad.BaseAttackDamagePerSecond * delta, squad.Name);
            }
        }
    }

    private void DamageBase(BaseCore targetBase, float damage, string source)
    {
        if (targetBase == null || !targetBase.IsAlive)
        {
            return;
        }

        var destroyed = targetBase.ApplyDamage(damage);
        _combatText = destroyed ? $"{source} destroyed {targetBase.Name}" : $"{source} hit {targetBase.Name}";
        SpawnImpactPoint(targetBase.GlobalPosition);
    }

    private void CheckVictory()
    {
        var playerBase = GetBase(0);
        var enemyBase = GetBase(1);

        if (playerBase != null && !playerBase.IsAlive)
        {
            _gameState = "Defeat";
            _combatText = "Defeat: your base fell.";
        }
        else if (enemyBase != null && !enemyBase.IsAlive)
        {
            _gameState = "Victory";
            _combatText = "Victory: enemy base destroyed.";
        }
    }

    private void UpdateHud()
    {
        if (_playerSquad == null)
        {
            return;
        }

        var playerBase = GetBase(0);
        var enemyBase = GetBase(1);

        _hudLabel.Text = string.Join(
            "\n",
            [
                $"Power Team - {_gameState}",
                "LMB drag: command squad intent",
                "Win: destroy enemy base",
                playerBase?.GetStatusText() ?? "PlayerBase missing",
                enemyBase?.GetStatusText() ?? "EnemyBase missing",
                "",
                $"Player: {_playerSquad.Name}",
                _playerSquad.BuildStatusReport(),
                _combatText,
                "",
                "Allied Squads:",
                .. _squads.Where(squad => squad.TeamId == 0).Select(squad => squad.GetStatusText()),
                "",
                "Enemy Squads:",
                .. _squads.Where(squad => squad.TeamId == 1).Select(squad => squad.GetStatusText()),
            ]
        );
    }

    private SquadController FindNearestEnemySquad(SquadController seeker, float range)
    {
        SquadController best = null;
        var bestDistance = range;

        foreach (var squad in _squads)
        {
            if (squad.TeamId == seeker.TeamId || !squad.IsAlive)
            {
                continue;
            }

            var distance = FlatDistance(seeker.GlobalPosition, squad.GlobalPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = squad;
            }
        }

        return best;
    }

    private SquadController FindClosestEnemyInArc(SquadController seeker, float range, float arcRad)
    {
        return FindEnemiesInArc(seeker, range, arcRad).FirstOrDefault();
    }

    private SquadController[] FindEnemiesInArc(SquadController seeker, float range, float arcRad)
    {
        var list = new List<SquadController>();
        var cosThreshold = Mathf.Cos(arcRad * 0.5f);

        foreach (var squad in _squads)
        {
            if (squad.TeamId == seeker.TeamId || !squad.IsAlive)
            {
                continue;
            }

            var toTarget = squad.GlobalPosition - seeker.GlobalPosition;
            toTarget.Y = 0.0f;
            var distance = toTarget.Length();
            if (distance > range + squad.Radius || distance < 0.001f)
            {
                continue;
            }

            if (toTarget.Normalized().Dot(seeker.FacingDirection) < cosThreshold)
            {
                continue;
            }

            list.Add(squad);
        }

        return [.. list.OrderBy(squad => FlatDistance(seeker.GlobalPosition, squad.GlobalPosition))];
    }

    private bool IsBaseInRange(SquadController seeker, BaseCore targetBase, float range, float arcRad)
    {
        var toBase = targetBase.GlobalPosition - seeker.GlobalPosition;
        toBase.Y = 0.0f;
        var distance = toBase.Length();
        if (distance > range + targetBase.Radius || distance < 0.001f)
        {
            return false;
        }

        return toBase.Normalized().Dot(seeker.FacingDirection) >= Mathf.Cos(arcRad * 0.5f);
    }

    private BaseCore GetBase(int teamId)
    {
        return _bases.FirstOrDefault(baseCore => baseCore.TeamId == teamId);
    }

    private BaseCore GetEnemyBase(int teamId)
    {
        return _bases.FirstOrDefault(baseCore => baseCore.TeamId != teamId);
    }

    private float FlatDistance(Vector3 a, Vector3 b)
    {
        var delta = a - b;
        delta.Y = 0.0f;
        return delta.Length();
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
        SpawnEffectPoint(point + Vector3.Up * 0.12f, 0.18f, new Color(1.0f, 0.2f, 0.2f, 1.0f), EffectLifetime);
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

    private void ClearExpiredEffectTimers()
    {
        if (_debugRoot == null)
        {
            return;
        }

        var remove = new List<Node>();
        foreach (var child in _debugRoot.GetChildren())
        {
            if (child is MeshInstance3D mesh && !mesh.Visible)
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
