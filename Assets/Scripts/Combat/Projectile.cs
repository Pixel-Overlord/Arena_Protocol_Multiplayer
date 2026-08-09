using Fusion;
using UnityEngine;

/// <summary>
/// Represents a networked projectile that moves forward, applies damage to valid targets, and manages its own lifetime.
/// </summary>
/// <remarks>Handles both player- and enemy-fired projectiles</remarks>
public class Projectile : NetworkBehaviour
{
    [Tooltip("Player who fired it. Used to skip self-hits.")]
    [Networked] public PlayerRef player { get; set; }

    [Tooltip("True when an enemy fired this instead of a player. Set by EnemyWeapon.")]
    [Networked] public NetworkBool firedByEnemy { get; set; }

    [Tooltip("Counts down lifetimeSeconds; when expired, the projectile despawns even if it never hits anything.")]
    [Networked] private TickTimer projectileLife { get; set; }

    [Tooltip("Foward movement speed per second")]
    [SerializeField] private float speed = 15f;

    [Tooltip("Passed straight to PlayerHealth.ApplyDamage.")]
    [SerializeField] private float damage = 10f;

    [Tooltip("How long before an unused projectile destroy.")]
    [SerializeField] private float lifetimeSeconds = 3f;

    /// <summary>
    /// On spawn, if this object has state authority, create a TickTimer for projectile's lifetime.
    /// </summary>
    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            projectileLife = TickTimer.CreateFromSeconds(Runner, lifetimeSeconds);
        }
    }

    /// <summary>
    /// Handles the projectile's forward movement and lifetime expiration.
    /// If the projectile's lifetime has expired, it despawns.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        // Freeze in flight once the match is over. Returning before the lifetime check too,
        // so bullets don't quietly despawn out from under a frozen scene.
        if (GameStateManager.IsMatchOver)
        {
            return;
        }

        if (projectileLife.ExpiredOrNotRunning(Runner))
        {
            Runner.Despawn(Object);
            return;
        }

        transform.position += transform.forward * speed * Runner.DeltaTime;
    }

    /// <summary>
    /// Handles collision events with the projectile's trigger collider, applying damage to valid targets and despawning
    /// the projectile as appropriate.
    /// </summary>
    /// <param name="hitCollider">The collider that enters the projectile's trigger collider.</param>
    private void OnTriggerEnter(Collider hitCollider)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        // Ignore collisions with other projectiles, including our own.
        IDamageable target = hitCollider.GetComponent<IDamageable>();

        if (target == null)
        {
            target = hitCollider.GetComponentInParent<IDamageable>();
        }

        // Hit the arena floor or a wall. Despawn so stray shots don't sail on forever.
        if (target == null)
        {
            Runner.Despawn(Object);
            return;
        }

        // Pass straight through corpses rather than wasting the shot on them.
        if (target.IsDead)
        {
            return;
        }

        if (target is PlayerHealth playerTarget)
        {
            // If the projectile was fired by a player, ignore collisions with that same player.
            if (!firedByEnemy && playerTarget.Object.InputAuthority == player)
            {
                return;
            }
        }
        else if (firedByEnemy)
        {
            // If the projectile was fired by an enemy, ignore collisions with other enemies.
            return;
        }

        target.applyDamage(damage);

        Runner.Despawn(Object);
    }
}
