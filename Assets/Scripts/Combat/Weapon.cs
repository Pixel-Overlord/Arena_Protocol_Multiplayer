using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a networked weapon that manages projectile firing, cooldowns, and player input in a multiplayer
/// environment.
/// </summary>
public class Weapon : NetworkBehaviour
{
    [Tooltip("The prefab to spawn.")]
    [SerializeField] private NetworkPrefabRef projectilePrefab;

    [Tooltip("The position for projectile to spawn.")]
    [SerializeField] private Transform firePositionPoint;

    [Tooltip("Time in seconds between two projectiles to spawn or Time between two shots.")]
    [SerializeField] private float fireRate = 0.25f;

    [Tooltip("Cooldown timer for projectile.")]
    [Networked] private TickTimer cooldownTimer { get; set; }

    private PlayerHealth playerHealth;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
    }

    /// <summary>
    /// Processes network input and fires a projectile if the fire button is pressed and the cooldown has expired.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // If the player is dead or the match is over, do not process input or fire projectiles.
        if (playerHealth.isDead || GameStateManager.IsMatchOver)
        {
            return;
        }

        // If we cannot get the input data, do not proceed with firing projectiles.
        if (!GetInput(out NetworkInputData input))
        {
            return;
        }

        // Only the player with state authority can fire projectiles.
        if (!Object.HasStateAuthority)
        {
            return;
        }

        // If the fire button is pressed and the cooldown timer has expired, fire a projectile and reset the cooldown timer.
        if (input.Buttons.IsSet((int)InputButton.Fire) && cooldownTimer.ExpiredOrNotRunning(Runner))
        {
            fireProjectile();
            cooldownTimer = TickTimer.CreateFromSeconds(Runner, fireRate);
        }
    }

    /// <summary>
    /// Spawns a projectile at the fire position with the correct rotation.
    /// </summary>
    private void fireProjectile()
    {
        if (projectilePrefab == null || firePositionPoint == null)
        {
            Debug.LogWarning("Projectile prefab or fire position point is not assigned.", this);
            return;
        }

        // Why not Instantiate? Because we are in a networked environment,
        // and we want to spawn the projectile across the network for all clients to see. Fusion handles this for us.
        Runner.Spawn(
            projectilePrefab,
            firePositionPoint.position,
            firePositionPoint.rotation,
            Object.InputAuthority,
            (runner, spawnedObject) =>
            {
                if (spawnedObject.TryGetComponent(out Projectile projectile))
                {
                    projectile.player = Object.InputAuthority;
                    projectile.firedByEnemy = false;
                }
            });
    }
}
