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
    [Networked] public NetworkBool isDead { get; set; }

    [SerializeField] private float maxHealth = 100f;

    // IDamageable wants a plain bool; isDead is a NetworkBool, which is a struct with an
    // implicit conversion, so this bridges the two without changing how isDead is stored.
    public bool IsDead => isDead;

    // maxHealth is serialized and private. Exposing it read-only lets health bars and
    // any future wave-scaling read the ceiling without being able to move it.
    public float MaxHealth => maxHealth;

    /// <summary>
    /// This function sets the Health to its maxHealth when spawned.
    /// </summary>
    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            currentHealth = maxHealth;
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
