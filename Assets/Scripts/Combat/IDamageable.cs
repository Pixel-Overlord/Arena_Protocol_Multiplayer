/// <summary>
/// Anything a projectile can hurt. Lets Projectile.cs stop hardcoding PlayerHealth,
/// so the same bullet script works for players shooting enemies and enemies shooting players.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Subtract health. Implementations are expected to guard on state authority
    /// themselves, exactly as PlayerHealth.applyDamage already does.
    /// </summary>
    void applyDamage(float damageAmount);

    /// <summary>
    /// True once this target is out of health. Used to skip corpses.
    /// </summary>
    bool IsDead { get; }
}
