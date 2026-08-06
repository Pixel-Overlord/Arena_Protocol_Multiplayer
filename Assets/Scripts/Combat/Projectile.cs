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

        if (life.ExpiredOrNotRunning(Runner))
        {
            Runner.Despawn(Object);
            return;
        }

        transform.position += transform.forward * speed * Runner.DeltaTime;
    }

    /// <summary>
    /// On trigger enter, check if the other object has a PlayerHealth component.
    /// If it does, apply damage and despawn the projectile. Ignore self-hits.
    /// </summary>
    /// <param name="other">Mesh collider of the object that's been hit.</param>
    private void OnTriggerEnter(Collider enemyCollider)
    {
        Debug.Log("Collision");
        if (!Object.HasStateAuthority)
        {
            return;
        }
        if (!enemyCollider.TryGetComponent(out PlayerHealth health))
        { 
            health = enemyCollider.GetComponentInParent<PlayerHealth>();
        }
        if (health == null || health.Object.InputAuthority == player)
        { 
            return; 
        }

        health.applyDamage(damage);

        Runner.Despawn(Object);
    }
}
