using Fusion;

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
    /// <param name="damageAmount">How much health to take off.</param>
    /// <param name="attacker">
    /// Who fired the shot, or PlayerRef.None for an enemy's. Carried so Enemy can pay the
    /// kill to the right player's score - scores are per player, not a shared total.
    /// </param>
    void applyDamage(float damageAmount, PlayerRef attacker);

    /// <summary>
    /// True once this target is out of health. Used to skip corpses.
    /// </summary>
    bool IsDead { get; }
}
