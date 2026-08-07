using Fusion;

/// <summary>
/// Defines a contract for objects that can receive damage and report their health status.
/// Such as players and enemies.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Applies damage to this target, reducing its health.
    /// </summary>
    /// <param name="damageAmount">Amount of damage the target would get.</param>
    void applyDamage(float damageAmount);

    /// <summary>
    /// This property indicates whether the target is dead (health <= 0).
    /// </summary>
    bool IsDead { get; }
}
