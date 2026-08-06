using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Weapon : NetworkBehaviour
{
    [Tooltip("The prefab to spawn.")]
    [SerializeField] private NetworkPrefabRef projectilePrefab;

    [Tooltip("The position for projectile to spawn.")]
    [SerializeField] private Transform firePositionPoint;

    [Tooltip("Time in seconds between two projectiles to spawn or Time between two shots.")]
    [SerializeField] private float fireRate = 0.5f;

    [Tooltip("Cooldown timer for projectile.")]
    [Networked] private TickTimer cooldownTimer { get; set; }

    /// <summary>
    /// Processes network input and fires a projectile if the fire button is pressed and the cooldown has expired.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!GetInput(out NetworkInputData input))
        {
            return;
        }

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
            Debug.LogWarning("Projectile prefab or fire position point is not assigned.");
            return;
        }

        // Why not Instantiate? Because we are in a networked environment,
        // and we want to spawn the projectile across the network for all clients to see. Fusion handles this for us.
        //
        // The last argument is onBeforeSpawned: it runs after the object exists but before
        // Spawned() is called on it. Stamping the owner here rather than after Spawn returns
        // means the value is already correct the first time the projectile simulates, and it
        // replicates cleanly to clients. Without this, `player` stayed PlayerRef.None and the
        // self-hit check in Projectile could never match.
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
