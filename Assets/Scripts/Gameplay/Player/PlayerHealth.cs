using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks a Player's health and death state, and applies damage/heal to it.
/// 
/// This is one shared place to read/write Health and Death for every class (Projectile, Shield, Heal).
/// </summary>
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Tooltip("Current HP/Health of player.")]
    [Networked] public float currentHealth { get; set; }

    [Tooltip("Boolean for dead or alive state of player. If Boolean is true, player is dead.")]
    [Networked, OnChangedRender(nameof(OnDeathStateChanged))] public NetworkBool isDead { get; set; }

    [SerializeField] private float maxHealth = 100f;

    // Collected in Awake rather than wired in the inspector, so the Player prefab needs no
    // extra setup and can't be half-configured. Cached because OnChangedRender can fire
    // often and GetComponentsInChildren allocates.
    private Renderer[] bodyRenderers;
    private Collider[] bodyColliders;

    public bool IsDead => isDead;

    public float MaxHealth => maxHealth;

    private void Awake()
    {
        // true = include inactive, so a body part that starts disabled is still tracked.
        bodyRenderers = GetComponentsInChildren<Renderer>(true);
        bodyColliders = GetComponentsInChildren<Collider>(true);
    }

    /// <summary>
    /// This function sets the Health to its maxHealth when spawned.
    /// </summary>
    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            currentHealth = maxHealth;
        }

        // Applied on spawn as well as on change, so a peer that joins mid-match sees an
        // already-dead player correctly hidden. OnChangedRender only fires on transitions.
        ApplyDeathVisuals();
    }

    /// <summary>
    /// Fires on every peer when isDead flips, because the property is replicated. This is
    /// why hiding the body needs no RPC - the networked flag is the single source of truth
    /// and each peer reacts to it locally.
    /// </summary>
    private void OnDeathStateChanged()
    {
        ApplyDeathVisuals();
    }

    /// <summary>
    /// Hides or shows the whole body. Colliders go with the renderers so a corpse cannot
    /// soak enemy projectiles or block movement.
    /// </summary>
    private void ApplyDeathVisuals()
    {
        bool isAlive = !isDead;

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] != null)
            {
                bodyRenderers[i].enabled = isAlive;
            }
        }

        for (int i = 0; i < bodyColliders.Length; i++)
        {
            if (bodyColliders[i] != null)
            {
                bodyColliders[i].enabled = isAlive;
            }
        }
    }

    /// <summary>
    /// Reduces the player's current health by the specified amount
    /// unless the player is dead or shielded.
    /// 
    /// Marks the player as dead if health reaches zero.
    /// 
    /// </summary>
    /// <param name="damageAmount">The amount of damage to subtract from the player's current health.</param>
    public void applyDamage(float damageAmount)
    {
        if (!Object.HasStateAuthority || isDead)
        {
            return;
        }

        // if player's ability is shield and shield is active, no damage.
        if (TryGetComponent(out PlayerAbility ability) && ability.IsShieldActive)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damageAmount);

        if (currentHealth <= 0f)
        {
            isDead = true;
        }
    }

    /// <summary>
    /// Restores health by the specified amount 
    /// without exceeding the maximum health.
    /// </summary>
    /// <param name="healAmount">The amount of health to restore.</param>
    public void applyHealth(float healAmount)
    {
        if (!Object.HasStateAuthority || isDead)
        {
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + healAmount);
    }
}
