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

    private Renderer[] bodyRenderers;
    private Collider[] bodyColliders;

    public bool IsDead => isDead;

    public float MaxHealth => maxHealth;

    private void Awake()
    {
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

        ApplyDeathVisuals();
    }

    /// <summary>
    /// Applies visual effects related to the death state.
    /// </summary>
    private void OnDeathStateChanged()
    {
        ApplyDeathVisuals();
    }

    /// <summary>
    /// Enables or disables all body renderers and colliders based on the character's death state.
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
    /// Reduces the player's current health by the specified amount unless the player is dead, lacks state authority, or
    /// has an active shield.
    /// </summary>
    /// <param name="damageAmount">The amount of damage to subtract from the player's health.</param>
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

    /// <summary>
    /// Captures the player's current health state into the specified SavedPlayerState reference.
    /// </summary>
    /// <param name="state"></param>
    public void CaptureStateInto(ref SavedPlayerState state)
    {
        state.Health = isDead ? 0f : currentHealth;
    }

    public void RestoreState(SavedPlayerState state)
    {
        if (!Object.HasStateAuthority || state.Health <= 0f)
        {
            return;
        }

        // Clamped rather than trusted: maxHealth is a serialized prefab value and could
        // have been lowered since the snapshot was taken.
        currentHealth = Mathf.Clamp(state.Health, 0f, maxHealth);
    }
}
