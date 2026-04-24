using Godot;

public partial class TargetDummy : Node3D
{
    [Export]
    public int MaxHealth = 40;

    [Export]
    public float HitRadius = 1.1f;

    [Export]
    public float FlashDuration = 0.2f;

    [Export]
    public float RespawnDelay = 4.0f;

    [Export]
    public int ScoreValue = 25;

    [Export]
    public float AiMoveSpeed = 1.8f;

    private Label3D _stateLabel;
    private MeshInstance3D _bodyMesh;
    private Vector3 _knockbackVelocity = Vector3.Zero;
    private Vector3 _spawnPosition;
    private float _flashTimer;
    private float _respawnTimer;
    private int _health;

    public bool IsAlive => _health > 0;
    public int Health => _health;
    public float Radius => HitRadius;
    public string NameTag => Name;

    public override void _Ready()
    {
        _stateLabel = GetNodeOrNull<Label3D>("StateLabel");
        _bodyMesh = GetNodeOrNull<MeshInstance3D>("Body");
        _spawnPosition = GlobalPosition;
        _health = MaxHealth;
        UpdateLabel();
    }

    public override void _Process(double delta)
    {
        if (_flashTimer > 0.0f)
        {
            _flashTimer = Mathf.Max(0.0f, _flashTimer - (float)delta);
        }

        if (!IsAlive && _respawnTimer > 0.0f)
        {
            _respawnTimer = Mathf.Max(0.0f, _respawnTimer - (float)delta);
            if (_respawnTimer <= 0.0f)
            {
                Respawn();
            }
            UpdateLabel();
        }

        if (IsAlive && _knockbackVelocity.LengthSquared() > 0.001f)
        {
            var dt = (float)delta;
            GlobalPosition += _knockbackVelocity * dt;
            _knockbackVelocity = _knockbackVelocity.MoveToward(Vector3.Zero, 12.0f * dt);
        }

        if (_bodyMesh?.Mesh == null)
        {
            return;
        }

        var pulse = 1.0f + _flashTimer * 0.35f;
        _bodyMesh.Scale = Vector3.One * pulse;
    }

    public bool ApplyDamage(float amount, string source)
    {
        if (!IsAlive)
        {
            return false;
        }

        _health = Mathf.Max(0, _health - Mathf.CeilToInt(amount));
        _flashTimer = FlashDuration;

        if (_health <= 0)
        {
            Die();
        }
        else
        {
            UpdateLabel();
        }

        return _health <= 0;
    }

    public void AddKnockback(Vector3 direction, float strength)
    {
        if (!IsAlive || direction.LengthSquared() <= 0.001f)
        {
            return;
        }

        _knockbackVelocity += direction.Normalized() * strength;
    }

    public float RemainingRespawn => _respawnTimer;

    public void SimulateAi(Vector3 destination, float delta)
    {
        if (!IsAlive || _knockbackVelocity.LengthSquared() > 0.01f)
        {
            return;
        }

        var toDestination = destination - GlobalPosition;
        toDestination.Y = 0.0f;
        if (toDestination.LengthSquared() <= 0.04f)
        {
            return;
        }

        GlobalPosition += toDestination.Normalized() * AiMoveSpeed * delta;
    }

    public string GetStatusText()
    {
        if (IsAlive)
        {
            return $"{Name}: {_health}/{MaxHealth}";
        }

        if (_respawnTimer > 0.0f)
        {
            return $"{Name}: Down ({_respawnTimer:0.0}s)";
        }

        return $"{Name}: Down";
    }

    private void UpdateLabel()
    {
        if (_stateLabel == null)
        {
            return;
        }

        _stateLabel.Text = GetStatusText();
    }

    private void Die()
    {
        _health = 0;
        _respawnTimer = RespawnDelay;
        if (_bodyMesh != null)
        {
            _bodyMesh.Visible = false;
        }
        UpdateLabel();
    }

    private void Respawn()
    {
        _health = MaxHealth;
        _respawnTimer = 0.0f;
        GlobalPosition = _spawnPosition;
        _knockbackVelocity = Vector3.Zero;
        if (_bodyMesh != null)
        {
            _bodyMesh.Visible = true;
        }
        UpdateLabel();
    }
}
