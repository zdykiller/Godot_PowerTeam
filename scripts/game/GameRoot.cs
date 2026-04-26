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
    private readonly List<string> _battleLog = [];
    private string _combatText = "Destroy the enemy base.";
    private string _gameState = "Battle";
    private float _meleeUiTimer;

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

    [Export]
    public float FlankAttackMoraleMultiplier = 1.35f;

    [Export]
    public float RearAttackMoraleMultiplier = 1.75f;

    [Export]
    public float RearAttackDotThreshold = 0.55f;

    [Export]
    public float FlankAttackAbsDotThreshold = 0.35f;

    [Export]
    public int BattleLogLimit = 8;

    [Export]
    public float FloatingTextLifetime = 0.85f;

    [Export]
    public float MeleeUiInterval = 0.75f;

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

        foreach (var squad in _squads)
        {
            squad.SetHomeBasePosition(GetBase(squad.TeamId)?.GlobalPosition ?? squad.GlobalPosition);
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

        UpdateEngagements();
        _meleeUiTimer = Mathf.Max(0.0f, _meleeUiTimer - (float)delta);

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

        ResolveEngagementAttrition((float)delta);
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
        var positional = GetPositionalMoraleBonus(squad, target);
        var damage = target.ApplyDamage(action.LancerDamage, action.LancerMoraleDamage * positional.Multiplier);
        target.AddKnockback(squad.FacingDirection, 5.0f + squad.CurrentSpeed * 0.25f);
        target.PlayImpactReaction(squad.FacingDirection);
        SpawnDamageText(targetPos, damage, "charge");
        LogDamage("charge", squad, target, damage, positional.Label);
        if (damage.Defeated)
        {
            DamageBase(GetBase(target.TeamId), target.BaseDamageOnDefeat, target.Name);
        }

        var angleText = positional.Label.Length > 0 ? $" ({positional.Label})" : "";
        _combatText = damage.Defeated ? $"{squad.Name} broke {target.Name}{angleText}" : $"{squad.Name} charged {target.Name}{angleText}";
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
            var positional = GetPositionalMoraleBonus(squad, target);
            var damage = target.ApplyDamage(action.VolleyDamage, action.VolleyMoraleDamage * positional.Multiplier);
            target.AddKnockback(pushDirection, damage.Defeated ? 4.0f : 2.0f);
            SpawnDamageText(target.GlobalPosition, damage, "volley");
            LogDamage("volley", squad, target, damage, positional.Label);
            if (damage.Defeated)
            {
                DamageBase(GetBase(target.TeamId), target.BaseDamageOnDefeat, target.Name);
            }
            SpawnImpactPoint(target.GlobalPosition);
        }

        _combatText = $"{squad.Name} volleyed {targets.Length} squad(s).";
    }

    private (float Multiplier, string Label) GetPositionalMoraleBonus(SquadController attacker, SquadController target)
    {
        var attackDirection = target.GlobalPosition - attacker.GlobalPosition;
        attackDirection.Y = 0.0f;
        if (attackDirection.LengthSquared() <= 0.001f)
        {
            return (1.0f, "");
        }

        var dot = attackDirection.Normalized().Dot(target.FacingDirection);
        if (dot >= RearAttackDotThreshold)
        {
            return (RearAttackMoraleMultiplier, "rear");
        }

        if (Mathf.Abs(dot) <= FlankAttackAbsDotThreshold)
        {
            return (FlankAttackMoraleMultiplier, "flank");
        }

        return (1.0f, "");
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

    private void UpdateEngagements()
    {
        foreach (var squad in _squads)
        {
            if (!squad.IsAlive)
            {
                squad.SetEngagement(null);
                continue;
            }

            squad.SetEngagement(FindNearestEnemySquad(squad, squad.EngagementRadius));
        }
    }

    private void ResolveEngagementAttrition(float delta)
    {
        for (var i = 0; i < _squads.Length; i++)
        {
            var a = _squads[i];
            if (!a.IsAlive)
            {
                continue;
            }

            for (var j = i + 1; j < _squads.Length; j++)
            {
                var b = _squads[j];
                if (!b.IsAlive || a.TeamId == b.TeamId)
                {
                    continue;
                }

                var distance = FlatDistance(a.GlobalPosition, b.GlobalPosition);
                if (distance > Mathf.Max(a.EngagementRadius, b.EngagementRadius))
                {
                    continue;
                }

                var bDamage = b.ApplyDamage(a.MeleeDamagePerSecond * delta, a.MeleeMoraleDamagePerSecond * delta);
                var aDamage = a.ApplyDamage(b.MeleeDamagePerSecond * delta, b.MeleeMoraleDamagePerSecond * delta);
                if (_meleeUiTimer <= 0.0f)
                {
                    SpawnDamageText(b.GlobalPosition, bDamage, "melee");
                    SpawnDamageText(a.GlobalPosition, aDamage, "melee");
                    LogDamage("melee", a, b, bDamage, "");
                    LogDamage("melee", b, a, aDamage, "");
                }

                if (bDamage.Defeated)
                {
                    DamageBase(GetBase(b.TeamId), b.BaseDamageOnDefeat, b.Name);
                    _combatText = $"{a.Name} routed {b.Name} in melee.";
                }

                if (aDamage.Defeated)
                {
                    DamageBase(GetBase(a.TeamId), a.BaseDamageOnDefeat, a.Name);
                    _combatText = $"{b.Name} routed {a.Name} in melee.";
                }
            }
        }

        if (_meleeUiTimer <= 0.0f)
        {
            _meleeUiTimer = MeleeUiInterval;
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

    private void LogDamage(string source, SquadController attacker, SquadController target, SquadController.DamageResult damage, string angle)
    {
        if (damage.HealthDamage <= 0.01f && damage.MoraleDamage <= 0.01f)
        {
            return;
        }

        var angleText = angle.Length > 0 ? $" {angle}" : "";
        var defeatedText = damage.Defeated ? " ROUTED" : "";
        _battleLog.Insert(
            0,
            $"{source}{angleText}: {attacker.Name} -> {target.Name} -{damage.HealthDamage:0.0} HP -{damage.MoraleDamage:0.0} M{defeatedText}"
        );

        if (_battleLog.Count > BattleLogLimit)
        {
            _battleLog.RemoveRange(BattleLogLimit, _battleLog.Count - BattleLogLimit);
        }
    }

    private void SpawnDamageText(Vector3 worldPos, SquadController.DamageResult damage, string source)
    {
        if (_debugRoot == null || (damage.HealthDamage <= 0.01f && damage.MoraleDamage <= 0.01f))
        {
            return;
        }

        var label = new Label3D
        {
            Text = $"{source}\n-{damage.HealthDamage:0.#} HP  -{damage.MoraleDamage:0.#} M",
            Position = worldPos + Vector3.Up * 1.4f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 32,
            Modulate = damage.Defeated ? new Color(1.0f, 0.15f, 0.08f, 1.0f) : new Color(1.0f, 0.92f, 0.25f, 1.0f),
            OutlineSize = 8,
            OutlineModulate = new Color(0.0f, 0.0f, 0.0f, 0.75f),
        };

        _debugRoot.AddChild(label);
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", worldPos + Vector3.Up * 2.45f, FloatingTextLifetime);
        tween.TweenProperty(label, "modulate:a", 0.0f, FloatingTextLifetime);
        tween.Finished += label.QueueFree;
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
                "Battle Log:",
                .. _battleLog,
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
