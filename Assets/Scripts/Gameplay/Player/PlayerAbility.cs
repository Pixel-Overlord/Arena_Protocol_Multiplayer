using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs whichever ability (Shield or Heal) this player was randomly assigned
/// activation, meter drain, cooldown, and the glow state that shows it to everyone.
/// </summary>
public class PlayerAbility : NetworkBehaviour
{
    // One can add more abilities here, but the HUD and PlayerSpawner will need to be updated to handle them.
    public enum AbilityType
    {
        Shield,
        Heal
    }

    [Tooltip("This contains the color glow around player as per the ability.")]
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

<<<<<<< Updated upstream
    [Tooltip("This tells the ability that is activated.")]
=======
    [Tooltip("This tells which glow to show as per the ability.")]
>>>>>>> Stashed changes
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

    [Tooltip("What PlayerHealth.ApplyDamage checks to decide whether to negate damage.")]
    public bool IsShieldActive => Type == AbilityType.Shield && Meter > 0f;

    /// <summary>
    /// How full the power bar is, 0-1.
    /// </summary>
    public float MeterNormalized
    {
        get { return maxMeter > 0f ? Mathf.Clamp01(Meter / maxMeter) : 0f; }
    }

    /// <summary>
    /// How much of the recovery window is left, 0-1. Lets the HUD show the bar refilling
    /// while the ability is unavailable, instead of just sitting empty with no explanation.
    /// </summary>
    public float CooldownNormalized
    {
        get
        {
            // Runner is null until this object is spawned, and RemainingTime needs it.
            if (Runner == null || cooldownSeconds <= 0f)
            {
                return 0f;
            }

            // Remaining can be nullable float.
            float? remaining = cooldown.RemainingTime(Runner);

            // No value means the timer was never started or has already expired.
            return remaining.HasValue ? Mathf.Clamp01(remaining.Value / cooldownSeconds) : 0f;
        }
    }

    /// <summary>
    /// True when the ability is spent and still recovering, so the HUD can grey the bar out.
    /// </summary>
    public bool IsOnCooldown
    {
        get { return Meter <= 0f && CooldownNormalized > 0f; }
    }

    /// <summary>
    /// Gives this player a specific ability. Called by PlayerSpawner, which decides who gets
    /// what so the two players in a match never end up with the same one.
    /// </summary>
    public void AssignAbility(AbilityType abilityType)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        Type = abilityType;
    }

    /// <summary>
    /// Picks an ability with a coin flip. Only used when there is nothing to complement -
    /// the first player into the arena. Once someone holds one, PlayerSpawner hands the
    /// other player the opposite rather than rolling again.
    /// </summary>
    public void AssignRandomAbility()
    {
        AssignAbility(Random.Range(0, 2) == 0 ? AbilityType.Shield : AbilityType.Heal);
    }

    /// <summary>
    /// Copies the ability state into a snapshot that will outlive this object, so a player
    /// who disconnects mid-cooldown cannot dodge it by rejoining.
    ///
    /// Host-side only, called just before the player is despawned on disconnect.
    /// </summary>
    public void CaptureStateInto(ref SavedPlayerState state)
    {
        state.AbilityType = Type;
        state.AbilityMeter = Meter;

        // Stored as seconds, not as the TickTimer itself - see SavedPlayerState for why.
        float? cooldownRemaining = null;

        if (Runner != null)
        {
            cooldownRemaining = cooldown.RemainingTime(Runner);
        }

        // No value means the timer was never started or has already expired, both of which
        // mean there is nothing left to serve.
        state.AbilityCooldownRemainingSeconds = cooldownRemaining ?? 0f;
    }

    /// <summary>
    /// Puts back the ability a returning player disconnected with, including how much active
    /// duration was left and how much of the cooldown still had to run.
    ///
    /// Must run after Spawned(), which zeroes the meter - that is why PlayerSpawner applies
    /// this once runner.Spawn has returned rather than in an onBeforeSpawned callback, which
    /// would run too early and be overwritten.
    /// </summary>
    public void RestoreState(SavedPlayerState state)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        Type = state.AbilityType;

        // Clamped rather than trusted: maxMeter is a serialized prefab value and could have
        // been lowered since the snapshot was taken.
        Meter = Mathf.Clamp(state.AbilityMeter, 0f, maxMeter);

        // Rebuilt against the current tick, so someone who dropped with 2s of cooldown left
        // still has 2s left when they come back - rather than a timer from a stale tick that
        // would read as already expired.
        cooldown = state.AbilityCooldownRemainingSeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, state.AbilityCooldownRemainingSeconds)
            : default;

        // The meter came back as "still running", so the glow has to come back with it -
        // otherwise a restored shield would be active but invisible to both players.
        // HealShared is not reproduced here; ApplyHealTick recomputes it on the next tick.
        if (Meter <= 0f)
        {
            Glow = GlowState.None;
        }
        else
        {
            Glow = Type == AbilityType.Shield ? GlowState.Shield : GlowState.HealSelf;
        }
    }

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

        if (GameStateManager.IsMatchOver)
        {
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

            // IsLive rather than a null check: a despawned player object is parked rather
            // than destroyed by the pool, and reading isDead off one would throw.
            if (!otherPlayer.IsLive() || otherPlayer == Object)
            {
                continue;
            }

            if ((otherPlayer.transform.position - transform.position).sqrMagnitude > sqrHealRadius)
            {
                continue;
            }

            // The host holds state authority over every player object, so healing someone
            // else's player from here is allowed - applyHealth's own guard passes.
            if (otherPlayer.TryGetComponent(out PlayerHealth otherPlayerHealth) && !otherPlayerHealth.isDead)
            {
                otherPlayerHealth.applyHealth(healThisTick);
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
