using Godot;

public partial class BaseCore : Node3D
{
    [Export]
    public int TeamId = 0;

    [Export]
    public float MaxHealth = 300.0f;

    [Export]
    public float HitRadius = 3.2f;

    private Label3D _label;
    private MeshInstance3D _body;
    private float _runtimeMaxHealth;
    private float _health;
    private float _flashTimer;

    public bool IsAlive => _health > 0.0f;
    public float Health => _health;
    public float HealthRatio => _runtimeMaxHealth <= 0.0f ? 0.0f : _health / _runtimeMaxHealth;
    public float Radius => HitRadius;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label3D>("StateLabel");
        _body = GetNodeOrNull<MeshInstance3D>("Body");
        _runtimeMaxHealth = MaxHealth;
        _health = _runtimeMaxHealth;
        UpdateLabel();
    }

    public void ResetCore(float maxHealthMultiplier = 1.0f)
    {
        _runtimeMaxHealth = MaxHealth * Mathf.Max(0.1f, maxHealthMultiplier);
        _health = _runtimeMaxHealth;
        _flashTimer = 0.0f;
        UpdateLabel();
    }

    public override void _Process(double delta)
    {
        if (_flashTimer > 0.0f)
        {
            _flashTimer = Mathf.Max(0.0f, _flashTimer - (float)delta);
        }

        if (_body != null)
        {
            _body.Scale = Vector3.One * (1.0f + _flashTimer * 0.18f);
        }
    }

    public bool ApplyDamage(float damage)
    {
        if (!IsAlive)
        {
            return false;
        }

        _health = Mathf.Max(0.0f, _health - damage);
        _flashTimer = 0.25f;
        UpdateLabel();
        return !IsAlive;
    }

    public string GetStatusText()
    {
        return $"{Name}: {Mathf.CeilToInt(_health)}/{Mathf.CeilToInt(_runtimeMaxHealth)}";
    }

    private void UpdateLabel()
    {
        if (_label == null)
        {
            return;
        }

        _label.Text = GetStatusText();
    }
}
