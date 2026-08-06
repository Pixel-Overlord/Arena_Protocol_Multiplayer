using Fusion;
using UnityEngine;

/// <summary>/// 
/// Moves forward at a fixed speed after being fired,
/// deals damage on hitting a player, and
/// despawns on hit or after its lifetime expires.
/// 
/// Note : This class is not responsible for instantiating the projectile; that is handled by another script such as ShootBullet.cs or PlayerShoot.cs.
/// </summary>
public class Projectile : NetworkBehaviour
{
    [Tooltip("Player who fired it, replicated. Used to skip self-hits.")]
    [Networked] public PlayerRef player { get; set; }

    [Tooltip("True when an enemy fired this instead of a player. Set by EnemyWeapon. Lets one projectile script serve both sides while still knowing who not to hurt.")]
    [Networked] public NetworkBool firedByEnemy { get; set; }

    [Tooltip("Counts down lifetimeSeconds; when expired, the projectile despawns even if it never hits anything so stray shots don't leak objects forever.")]
    [Networked] private TickTimer life { get; set; }

    [Tooltip("Foward movement speed per second")]
    [SerializeField] private float speed = 15f;

    [Tooltip("Passed straight to PlayerHealth.ApplyDamage.")]
    [SerializeField] private float damage = 10f;

    [Tooltip("How long before an unused projectile destroy.")]
    [SerializeField] private float lifetimeSeconds = 3f;

    /// <summary>
    /// On spawn, if this object has state authority, create a TickTimer for its lifetime.
    /// </summary>
    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            life = TickTimer.CreateFromSeconds(Runner, lifetimeSeconds);
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

        if (life.ExpiredOrNotRunning(Runner))
        {
            Runner.Despawn(Object);
            return;
        }

        transform.position += transform.forward * speed * Runner.DeltaTime;
    }

    /// <summary>
    /// Resolves what was hit to an IDamageable, filters out friendly fire, applies damage
    /// and despawns.
    ///
    /// Targets IDamageable rather than PlayerHealth so the same script serves both player
    /// bullets (which need to hurt enemies) and enemy bullets (which need to hurt players).
    ///
    /// Note this only runs on the state authority, so the host is the single source of
    /// truth for every hit - clients never decide that they were shot.
    /// </summary>
    /// <param name="hitCollider">Collider of the object that's been hit.</param>
    private void OnTriggerEnter(Collider hitCollider)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        // The visual colliders sit on child objects (Sphere/Capsule), while the health
        // component lives on the prefab root, so a parent search is needed as a fallback.
        // GetComponent is used instead of TryGetComponent because interfaces are involved.
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
            // A player must not shoot themselves. This check is only meaningful for
            // player-fired shots: enemy bullets carry PlayerRef.None, which would never
            // match a real player's InputAuthority anyway.
            if (!firedByEnemy && playerTarget.Object.InputAuthority == player)
            {
                return;
            }
        }
        else if (firedByEnemy)
        {
            // An enemy bullet hit another enemy. The layer collision matrix should already
            // prevent this, but a mis-set layer in the inspector shouldn't turn into
            // enemies killing each other.
            return;
        }

        // Enemy fire scores for nobody, so it passes None rather than its own meaningless
        // PlayerRef. A player's shot carries their PlayerRef down so Enemy can credit the kill.
        target.applyDamage(damage, firedByEnemy ? PlayerRef.None : player);

        Runner.Despawn(Object);
    }
}
