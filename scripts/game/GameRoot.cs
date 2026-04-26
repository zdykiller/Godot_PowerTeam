using Godot;
using System.Linq;
using System.Collections.Generic;

public partial class GameRoot : Node3D
{
    private enum ManualOrderType
    {
        Move,
        Attack,
    }

    private sealed class ManualOrder
    {
        public ManualOrderType Type;
        public Vector3 TargetPosition;
        public SquadController TargetSquad;
    }

    private BattleHud _hud;
    private Camera3D _camera;
    private SquadController[] _squads = [];
    private BaseCore[] _bases = [];
    private Node3D _debugRoot;
    private SquadController _playerSquad;
    private SquadController _selectedSquad;
    private readonly List<string> _battleLog = [];
    private readonly Dictionary<SquadController, ManualOrder> _manualOrders = [];
    private string _combatText = "Destroy the enemy base.";
    private string _gameState = "Menu";
    private float _meleeUiTimer;
    private float _chargeOrderCooldown;
    private float _volleyOrderCooldown;
    private float _rallyOrderCooldown;
    private BattleHud.AllyTactic _allyTactic = BattleHud.AllyTactic.Assault;

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
    public float AllyHoldRadius = 5.0f;

    [Export]
    public float AllyBaseGuardRadius = 7.0f;

    [Export]
    public float AllyFocusRange = 18.0f;

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

    [Export]
    public float ChargeOrderCooldown = 8.0f;

    [Export]
    public float VolleyOrderCooldown = 6.0f;

    [Export]
    public float RallyOrderCooldown = 12.0f;

    [Export]
    public float RallyMoraleRestore = 18.0f;

    [Export]
    public float GroundPlaneY = 0.0f;

    [Export]
    public float ClickSelectRadius = 3.2f;

    [Export]
    public float ManualMoveStopRadius = 1.6f;

    [Export]
    public float ManualMoveStrength = 0.95f;

    public override void _Ready()
    {
        _hud = GetNode<BattleHud>("UI");
        _hud.TacticSelected += SetAllyTactic;
        _hud.SkillSelected += TriggerBattleSkill;
        _hud.StartRequested += StartBattle;
        _hud.RestartRequested += RestartBattle;
        _camera = GetViewport().GetCamera3D() ?? GetNodeOrNull<Camera3D>("CameraRig/Camera3D");

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
            _hud.SetStatusLines(["Battle scene missing player squad or bases."]);
            return;
        }

        foreach (var squad in _squads)
        {
            squad.SetHomeBasePosition(GetBase(squad.TeamId)?.GlobalPosition ?? squad.GlobalPosition);
        }

        SetSelectedSquad(_playerSquad);
        _hud.SetTactic(_allyTactic);
        UpdateHud();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_gameState != "Battle" || _playerSquad == null)
        {
            return;
        }

        if (@event is not InputEventMouseButton mouse || !mouse.Pressed || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        HandleWorldClick(mouse.Position);
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
        UpdateSkillCooldowns((float)delta);
        UpdateTacticInput();

        foreach (var squad in _squads)
        {
            var command = BuildAiCommand(squad);
            var active = command.LengthSquared() > 0.001f;

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
        _selectedSquad = selected;
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

        if (squad.TeamId == 0)
        {
            if (TryBuildManualCommand(squad, out var manualCommand))
            {
                return manualCommand;
            }

            return BuildAllyCommand(squad);
        }

        return BuildEnemyCommand(squad);
    }

    private Vector2 BuildEnemyCommand(SquadController squad)
    {
        var nearestEnemy = FindNearestEnemySquad(squad, AiEngageRange);
        var targetPosition = nearestEnemy?.GlobalPosition ?? GetEnemyBase(squad.TeamId)?.GlobalPosition ?? squad.GlobalPosition;
        return BuildMoveCommandTo(squad, targetPosition, nearestEnemy);
    }

    private bool TryBuildManualCommand(SquadController squad, out Vector2 command)
    {
        command = Vector2.Zero;
        if (!_manualOrders.TryGetValue(squad, out var order))
        {
            return false;
        }

        switch (order.Type)
        {
            case ManualOrderType.Move:
                if (FlatDistance(squad.GlobalPosition, order.TargetPosition) <= ManualMoveStopRadius)
                {
                    _manualOrders.Remove(squad);
                    return false;
                }

                command = BuildMoveCommandTo(squad, order.TargetPosition, null, ManualMoveStrength);
                return true;
            case ManualOrderType.Attack:
                if (order.TargetSquad == null || !order.TargetSquad.IsAlive)
                {
                    _manualOrders.Remove(squad);
                    return false;
                }

                if (squad.Role == SquadController.SquadRole.Archer && FlatDistance(squad.GlobalPosition, order.TargetSquad.GlobalPosition) > squad.VolleyRange * 0.85f)
                {
                    command = BuildMoveCommandTo(squad, order.TargetSquad.GlobalPosition, null, 0.7f);
                    return true;
                }

                command = BuildMoveCommandTo(squad, order.TargetSquad.GlobalPosition, order.TargetSquad);
                return true;
            default:
                return false;
        }
    }

    private Vector2 BuildAllyCommand(SquadController squad)
    {
        switch (_allyTactic)
        {
            case BattleHud.AllyTactic.Hold:
                return BuildHoldCommand(squad);
            case BattleHud.AllyTactic.Focus:
                return BuildFocusCommand(squad);
            case BattleHud.AllyTactic.Regroup:
                return BuildRegroupCommand(squad);
            default:
                var nearestEnemy = FindNearestEnemySquad(squad, AiEngageRange);
                var targetPosition = nearestEnemy?.GlobalPosition ?? GetEnemyBase(squad.TeamId)?.GlobalPosition ?? squad.GlobalPosition;
                return BuildMoveCommandTo(squad, targetPosition, nearestEnemy);
        }
    }

    private Vector2 BuildHoldCommand(SquadController squad)
    {
        var nearestEnemy = FindNearestEnemySquad(squad, AiEngageRange * 0.75f);
        if (nearestEnemy != null)
        {
            return BuildMoveCommandTo(squad, nearestEnemy.GlobalPosition, nearestEnemy, 0.55f);
        }

        var homeBase = GetBase(squad.TeamId);
        var anchor = _playerSquad != null && _playerSquad.IsAlive ? _playerSquad.GlobalPosition : homeBase?.GlobalPosition ?? squad.GlobalPosition;
        var playerNearBase = _playerSquad != null && homeBase != null && FlatDistance(_playerSquad.GlobalPosition, homeBase.GlobalPosition) <= AllyBaseGuardRadius;
        var maxRadius = playerNearBase
            ? AllyBaseGuardRadius
            : AllyHoldRadius;

        if (FlatDistance(squad.GlobalPosition, anchor) <= maxRadius)
        {
            return Vector2.Zero;
        }

        return BuildMoveCommandTo(squad, anchor, null, 0.65f);
    }

    private Vector2 BuildFocusCommand(SquadController squad)
    {
        var focusTarget = FindPlayerFocusTarget();
        if (focusTarget != null)
        {
            return BuildMoveCommandTo(squad, focusTarget.GlobalPosition, focusTarget);
        }

        return BuildHoldCommand(squad);
    }

    private Vector2 BuildRegroupCommand(SquadController squad)
    {
        var homeBase = GetBase(squad.TeamId);
        if (homeBase == null)
        {
            return Vector2.Zero;
        }

        if (FlatDistance(squad.GlobalPosition, homeBase.GlobalPosition) <= AllyBaseGuardRadius)
        {
            return Vector2.Zero;
        }

        return BuildMoveCommandTo(squad, homeBase.GlobalPosition, null, 0.85f);
    }

    private SquadController FindPlayerFocusTarget()
    {
        if (_playerSquad == null || !_playerSquad.IsAlive)
        {
            return null;
        }

        var candidates = FindEnemiesInArc(_playerSquad, AllyFocusRange, Mathf.DegToRad(80.0f));
        return candidates.FirstOrDefault() ?? FindNearestEnemySquad(_playerSquad, AllyFocusRange);
    }

    private Vector2 BuildMoveCommandTo(SquadController squad, Vector3 targetPosition, SquadController targetEnemy, float strengthOverride = -1.0f)
    {
        var toTarget = targetPosition - squad.GlobalPosition;
        toTarget.Y = 0.0f;

        if (toTarget.LengthSquared() <= 0.05f)
        {
            return Vector2.Zero;
        }

        var direction = toTarget.Normalized();
        var strength = strengthOverride >= 0.0f
            ? strengthOverride
            : squad.Role == SquadController.SquadRole.Archer && targetEnemy != null ? 0.45f : AiCommandStrength;
        return new Vector2(direction.X, direction.Z) * strength;
    }

    private void UpdateTacticInput()
    {
        if (Input.IsKeyPressed(Key.Key1))
        {
            SetAllyTactic(BattleHud.AllyTactic.Assault);
        }
        else if (Input.IsKeyPressed(Key.Key2))
        {
            SetAllyTactic(BattleHud.AllyTactic.Hold);
        }
        else if (Input.IsKeyPressed(Key.Key3))
        {
            SetAllyTactic(BattleHud.AllyTactic.Focus);
        }
        else if (Input.IsKeyPressed(Key.Key4))
        {
            SetAllyTactic(BattleHud.AllyTactic.Regroup);
        }
    }

    private void SetAllyTactic(BattleHud.AllyTactic tactic)
    {
        if (_allyTactic == tactic)
        {
            return;
        }

        _allyTactic = tactic;
        _combatText = $"Allies: {_allyTactic}";
        _hud.SetTactic(_allyTactic);
    }

    private void TriggerBattleSkill(BattleHud.BattleSkill skill)
    {
        if (_gameState != "Battle")
        {
            return;
        }

        switch (skill)
        {
            case BattleHud.BattleSkill.Charge:
                TriggerChargeOrder();
                break;
            case BattleHud.BattleSkill.Volley:
                TriggerVolleyOrder();
                break;
            case BattleHud.BattleSkill.Rally:
                TriggerRallyOrder();
                break;
        }
    }

    private void TriggerChargeOrder()
    {
        if (_chargeOrderCooldown > 0.0f || _selectedSquad == null || !_selectedSquad.IsAlive || _selectedSquad.Role != SquadController.SquadRole.Lancer)
        {
            return;
        }

        _selectedSquad.ApplyChargeOrder();
        _chargeOrderCooldown = ChargeOrderCooldown;
        _combatText = $"{_selectedSquad.Name}: Charge!";
        AddBattleEvent($"order: {_selectedSquad.Name} charge");
    }

    private void TriggerVolleyOrder()
    {
        if (_volleyOrderCooldown > 0.0f || _selectedSquad == null || !_selectedSquad.IsAlive || _selectedSquad.Role != SquadController.SquadRole.Archer)
        {
            return;
        }

        _selectedSquad.ApplyVolleyOrder();
        _volleyOrderCooldown = VolleyOrderCooldown;
        _combatText = $"{_selectedSquad.Name}: Volley!";
        AddBattleEvent($"order: {_selectedSquad.Name} volley");
    }

    private void TriggerRallyOrder()
    {
        if (_rallyOrderCooldown > 0.0f || _selectedSquad == null || !_selectedSquad.IsAlive)
        {
            return;
        }

        var restored = _selectedSquad.ApplyRally(RallyMoraleRestore);
        _rallyOrderCooldown = RallyOrderCooldown;
        _combatText = $"{_selectedSquad.Name}: Rally +{restored:0} morale.";
        AddBattleEvent($"order: {_selectedSquad.Name} rally +{restored:0} morale");
    }

    private void UpdateSkillCooldowns(float delta)
    {
        _chargeOrderCooldown = Mathf.Max(0.0f, _chargeOrderCooldown - delta);
        _volleyOrderCooldown = Mathf.Max(0.0f, _volleyOrderCooldown - delta);
        _rallyOrderCooldown = Mathf.Max(0.0f, _rallyOrderCooldown - delta);
        _hud.SetSkillCooldowns(_chargeOrderCooldown, _volleyOrderCooldown, _rallyOrderCooldown);
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
        AddBattleEvent($"{source}{angleText}: {attacker.Name} -> {target.Name} -{damage.HealthDamage:0.0} HP -{damage.MoraleDamage:0.0} M{defeatedText}");
    }

    private void AddBattleEvent(string text)
    {
        _battleLog.Insert(0, text);
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
            EndBattle("Defeat", "Your base fell.");
        }
        else if (enemyBase != null && !enemyBase.IsAlive)
        {
            EndBattle("Victory", "Enemy base destroyed.");
        }
    }

    private void EndBattle(string result, string summary)
    {
        if (_gameState != "Battle")
        {
            return;
        }

        _gameState = result;
        _combatText = $"{result}: {summary}";
        _hud.ShowResult(result, summary);
    }

    private void StartBattle()
    {
        if (_gameState != "Menu")
        {
            return;
        }

        _gameState = "Battle";
        _combatText = "Battle started. Select a squad and issue orders.";
        AddBattleEvent("battle: started");
        _hud.ShowBattleHud();
        UpdateHud();
    }

    private void RestartBattle()
    {
        GetTree().ReloadCurrentScene();
    }

    private void UpdateHud()
    {
        if (_playerSquad == null)
        {
            return;
        }

        var playerBase = GetBase(0);
        var enemyBase = GetBase(1);
        var selected = _selectedSquad ?? _playerSquad;
        var selectedOrder = GetManualOrderText(selected);

        _hud.SetStatusLines(
            [
                $"STATE: {_gameState}    TACTIC: {_allyTactic}    OBJECTIVE: Destroy enemy base",
                $"BASES: {playerBase?.GetStatusText() ?? "PlayerBase missing"}    |    {enemyBase?.GetStatusText() ?? "EnemyBase missing"}",
                $"SELECTED: {selected.Name}    ORDER: {selectedOrder}",
                selected.BuildStatusReport(),
                $"SKILLS: Charge {_chargeOrderCooldown:0.0}s    Volley {_volleyOrderCooldown:0.0}s    Rally {_rallyOrderCooldown:0.0}s",
                $"COMBAT: {_combatText}",
                $"ALLIES: {string.Join("  |  ", _squads.Where(squad => squad.TeamId == 0).Select(squad => squad.GetStatusText()))}",
                $"ENEMIES: {string.Join("  |  ", _squads.Where(squad => squad.TeamId == 1).Select(squad => squad.GetStatusText()))}",
                $"LOG: {string.Join("    /    ", _battleLog.Take(4))}",
            ]
        );

        UpdateSelectedSkillPanel(selected);
    }

    private void UpdateSelectedSkillPanel(SquadController selected)
    {
        if (_gameState != "Battle" || _camera == null || selected == null || !selected.IsAlive || selected.TeamId != 0)
        {
            _hud.SetSelectedSkillPanel(false, Vector2.Zero, "", false, false, 0.0f, 0.0f, 0.0f);
            return;
        }

        var screenPosition = _camera.UnprojectPosition(selected.GlobalPosition + Vector3.Up * 3.5f);
        _hud.SetSelectedSkillPanel(
            true,
            screenPosition,
            selected.Name,
            selected.Role == SquadController.SquadRole.Lancer,
            selected.Role == SquadController.SquadRole.Archer,
            _chargeOrderCooldown,
            _volleyOrderCooldown,
            _rallyOrderCooldown
        );
    }

    private string GetManualOrderText(SquadController squad)
    {
        if (squad == null || !_manualOrders.TryGetValue(squad, out var order))
        {
            return "Auto";
        }

        if (order.Type == ManualOrderType.Attack && order.TargetSquad != null)
        {
            return $"Attack {order.TargetSquad.Name}";
        }

        return "Move";
    }

    private void HandleWorldClick(Vector2 screenPosition)
    {
        if (!TryProjectToGround(screenPosition, out var worldPosition))
        {
            return;
        }

        var clickedAlly = FindClosestSquadAt(worldPosition, squad => squad.TeamId == 0 && squad.IsAlive);
        if (clickedAlly != null)
        {
            SetSelectedSquad(clickedAlly);
            _combatText = $"Selected {clickedAlly.Name}";
            AddBattleEvent($"select: {clickedAlly.Name}");
            return;
        }

        var selected = _selectedSquad;
        if (selected == null || !selected.IsAlive || selected.TeamId != 0)
        {
            return;
        }

        var clickedEnemy = FindClosestSquadAt(worldPosition, squad => squad.TeamId != selected.TeamId && squad.IsAlive);
        if (clickedEnemy != null)
        {
            _manualOrders[selected] = new ManualOrder
            {
                Type = ManualOrderType.Attack,
                TargetSquad = clickedEnemy,
                TargetPosition = clickedEnemy.GlobalPosition,
            };
            _combatText = $"{selected.Name}: attack {clickedEnemy.Name}";
            AddBattleEvent($"command: {selected.Name} attack {clickedEnemy.Name}");
            SpawnEffectPoint(clickedEnemy.GlobalPosition + Vector3.Up * 0.1f, 0.22f, new Color(1.0f, 0.25f, 0.15f, 1.0f), 0.8f);
            return;
        }

        _manualOrders[selected] = new ManualOrder
        {
            Type = ManualOrderType.Move,
            TargetPosition = worldPosition,
        };
        _combatText = $"{selected.Name}: move";
        AddBattleEvent($"command: {selected.Name} move");
        SpawnEffectPoint(worldPosition + Vector3.Up * 0.1f, 0.2f, new Color(0.25f, 0.85f, 1.0f, 1.0f), 0.8f);
    }

    private bool TryProjectToGround(Vector2 screenPosition, out Vector3 worldPosition)
    {
        worldPosition = Vector3.Zero;
        if (_camera == null)
        {
            return false;
        }

        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        if (Mathf.Abs(direction.Y) <= 0.001f)
        {
            return false;
        }

        var t = (GroundPlaneY - origin.Y) / direction.Y;
        if (t < 0.0f)
        {
            return false;
        }

        worldPosition = origin + direction * t;
        return true;
    }

    private SquadController FindClosestSquadAt(Vector3 worldPosition, System.Func<SquadController, bool> predicate)
    {
        SquadController best = null;
        var bestDistance = ClickSelectRadius;
        foreach (var squad in _squads)
        {
            if (!predicate(squad))
            {
                continue;
            }

            var distance = FlatDistance(worldPosition, squad.GlobalPosition);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = squad;
            }
        }

        return best;
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
