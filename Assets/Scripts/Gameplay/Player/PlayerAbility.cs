using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs whichever ability (Shield or Heal) this player was randomly assigned � 
/// activation, meter drain, cooldown, and the glow state that shows it to everyone.
/// </summary>
public class PlayerAbility : NetworkBehaviour
{
    public enum AbilityType
    {
        Shield,
        Heal
    }

    [Tooltip("This contains the amount of color glow around player as per the ability.")]
    public enum GlowState
    {
        None,
        Shield,
        HealSelf,
        HealShared
    }

    [Networked] public AbilityType Type { get; set; }

    [Tooltip("Remaining active-duration for power use.")]
    [Networked] private float Meter { get; set; }   // drains while active.

    [Tooltip("Blocks reactivating until it expires")]
    [Networked] private TickTimer cooldown { get; set; }

    [Tooltip("check which button was pressed previously.")]
    [Networked] private NetworkButtons previousButtons { get; set; }

    [Tooltip("Purely cosmetic, but it is the only way a player can tell their shield is up. OnChangedRender fires on every peer when the value changes, so the visual follows the networked state without an RPC.")]
    [Networked, OnChangedRender(nameof(OnGlowChanged))] private GlowState Glow { get; set; }

    [Tooltip("How long one activation lasts.")]
    [SerializeField] private float maxMeter = 5f;

    [Tooltip("How fast meter empties.")]
    [SerializeField] private float drainPerSecond = 1f;

    [Tooltip("Recovery time after meter empties.")]
    [SerializeField] private float cooldownSeconds = 4f;

    [Tooltip("HP restored per second, to self and to anyone in range.")]
    [SerializeField] private float healPerSecond = 15f;

    [Tooltip("How close another player should be to also receive heal.")]
    [SerializeField] private float healRadius = 3f;

    [Header("Glow visuals")]
    [Tooltip("Renderer tinted to show the ability is running. Leave empty to skip the visual entirely.")]
    [SerializeField] private Renderer glowRenderer;

    [SerializeField] private Color shieldColor = new Color(0.2f, 0.6f, 1f);
    [SerializeField] private Color healSelfColor = new Color(0.2f, 1f, 0.4f);
    [SerializeField] private Color healSharedColor = new Color(1f, 0.9f, 0.3f);

    // The material's original colour, captured once so the glow can be switched off again.
    private Color baseColor;
    private bool baseColorCaptured;

    [Tooltip("What PlayerHealth.ApplyDamage checks to decide whether to negate damage.\r\nScript changes � new file ")]
    public bool IsShieldActive => Type == AbilityType.Shield && Meter > 0f;

    public override void Spawned()
    {
        if (glowRenderer != null)
        {
            // Reading .material clones the shared material, so tinting one player doesn't
            // recolour everyone using the same asset.
            baseColor = glowRenderer.material.color;
            baseColorCaptured = true;
        }

        if (Object.HasStateAuthority)
        {
            Meter = 0f;
            Glow = GlowState.None;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!GetInput(out NetworkInputData input))
        {
            return;
        }

        if (!Object.HasStateAuthority)
        {
            previousButtons = input.Buttons;
            return;
        }

        // WasPressed compares against last tick's buttons, giving a single event on the
        // frame Q goes down. IsSet would be true for every tick the key is held, which would
        // refill the meter continuously and make the cooldown meaningless.
        bool abilityPressed = input.Buttons.WasPressed(previousButtons, (int)InputButton.Ability);

        // Must be updated on this branch too. Weapon.cs gets away without it because it uses
        // IsSet; here a stale previousButtons would make every tick look like a fresh press.
        previousButtons = input.Buttons;

        // Activation. Meter <= 0 blocks re-triggering while already running, and the
        // cooldown blocks it until the recovery window has passed.
        if (abilityPressed && Meter <= 0f && cooldown.ExpiredOrNotRunning(Runner))
        {
            Meter = maxMeter;
        }

        if (Meter <= 0f)
        {
            Glow = GlowState.None;
            return;
        }

        // Runner.DeltaTime is the fixed tick length, so the drain rate is frame-rate
        // independent and identical on every peer.
        Meter = Mathf.Max(0f, Meter - drainPerSecond * Runner.DeltaTime);

        if (Type == AbilityType.Shield)
        {
            // Nothing to do beyond staying active: PlayerHealth.applyDamage already reads
            // IsShieldActive and returns early, so the damage is negated at the source.
            Glow = GlowState.Shield;
        }
        else
        {
            ApplyHealTick();
        }

        // Meter just ran out this tick - start the recovery window.
        if (Meter <= 0f)
        {
            cooldown = TickTimer.CreateFromSeconds(Runner, cooldownSeconds);
            Glow = GlowState.None;
        }
    }

    /// <summary>
    /// Heals this player, and any other player standing within healRadius, by one tick's
    /// worth of healing.
    /// </summary>
    private void ApplyHealTick()
    {
        float healThisTick = healPerSecond * Runner.DeltaTime;

        if (TryGetComponent(out PlayerHealth self))
        {
            self.applyHealth(healThisTick);
        }

        float sqrHealRadius = healRadius * healRadius;
        bool sharedWithSomeone = false;

        // Walks ActivePlayers rather than using Physics.OverlapSphere: no colliders to
        // configure, no allocation, and it can't accidentally pick up a projectile.
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            NetworkObject otherPlayer = Runner.GetPlayerObject(player);

            if (otherPlayer == null || otherPlayer == Object)
            {
                continue;
            }

            if ((otherPlayer.transform.position - transform.position).sqrMagnitude > sqrHealRadius)
            {
                continue;
            }

            // The host holds state authority over every player object, so healing someone
            // else's player from here is allowed - applyHealth's own guard passes.
            if (otherPlayer.TryGetComponent(out PlayerHealth otherHealth) && !otherHealth.isDead)
            {
                otherHealth.applyHealth(healThisTick);
                sharedWithSomeone = true;
            }
        }

        Glow = sharedWithSomeone ? GlowState.HealShared : GlowState.HealSelf;
    }

    /// <summary>
    /// Runs on every peer whenever Glow changes, including the one that owns the player.
    /// This is render-side only - it must never write networked state.
    /// </summary>
    private void OnGlowChanged()
    {
        if (glowRenderer == null || !baseColorCaptured)
        {
            return;
        }

        switch (Glow)
        {
            case GlowState.Shield:
                glowRenderer.material.color = shieldColor;
                break;

            case GlowState.HealSelf:
                glowRenderer.material.color = healSelfColor;
                break;

            case GlowState.HealShared:
                glowRenderer.material.color = healSharedColor;
                break;

            default:
                glowRenderer.material.color = baseColor;
                break;
        }
    }
}
