using Fusion;
using UnityEngine;

/// <summary>
/// Handles enemy projectile firing, managing cooldowns and targeting logic in a networked environment.
/// </summary>
/// <remarks>Ensures projectiles are spawned only by the state authority, respects match state, and adjusts aim to
/// account for player pivot offsets. Integrates with Fusion networking for authoritative shot spawning.</remarks>
public class EnemyWeapon : NetworkBehaviour
{
    [Tooltip("The enemy projectile prefab. Must be registered in the Fusion prefab table.")]
    [SerializeField] private NetworkPrefabRef enemyProjectilePrefab;

    [Tooltip("Where shots leave from - use the 'Enemy Gun' child on the prefab.")]
    [SerializeField] private Transform firePositionPoint;

    [Tooltip("Seconds between shots. Slower than the player's weapon so a single enemy isn't overwhelming.")]
    [SerializeField] private float fireRate = 1.5f;

    [Tooltip("Raises the aim point above the player's pivot. Without this the shot passes under the capsule, because a player's transform sits at their feet.")]
    [SerializeField] private float aimHeightOffset = 1f;

    [Networked] private TickTimer cooldownTimer { get; set; }

    /// <summary>
    /// Fires at a target if the cooldown has expired.
    /// </summary>
    /// <param name="targetPosition">World position of the player being shot at.</param>
    /// <returns>True if a shot was actually spawned this tick.</returns>
    public bool TryFire(Vector3 targetPosition)
    {
        // Enemies are host-driven, so only the state authority ever spawns their shots.
        if (!Object.HasStateAuthority)
        {
            return false;
        }

        if (GameStateManager.IsMatchOver)
        {
            return false;
        }

        if (!cooldownTimer.ExpiredOrNotRunning(Runner))
        {
            return false;
        }

        if (!enemyProjectilePrefab.IsValid || firePositionPoint == null)
        {
            Debug.LogWarning("EnemyWeapon: projectile prefab or fire position point is not assigned.", this);
            return false;
        }

        Vector3 aimPoint = targetPosition + Vector3.up * aimHeightOffset;
        Vector3 toTarget = aimPoint - firePositionPoint.position;

        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        // The projectile flies along its own forward axis, so the direction has to be baked
        // into the spawn rotation rather than passed to the projectile separately.
        Quaternion aimRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        Runner.Spawn(
            enemyProjectilePrefab,
            firePositionPoint.position,
            aimRotation,
            null,
            (runner, spawnedObject) =>
            {
                if (spawnedObject.TryGetComponent(out Projectile projectile))
                {
                    projectile.player = PlayerRef.None;
                    projectile.firedByEnemy = true;
                }
            });

        cooldownTimer = TickTimer.CreateFromSeconds(Runner, fireRate);
        return true;
    }
}
