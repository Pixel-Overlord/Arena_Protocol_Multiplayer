using Fusion;
using UnityEngine;

/// <summary>
/// Represents an energy orb that can be collected by players, transitioning through available, collected, and hidden
/// states with synchronized visuals and scoring.
/// </summary>
public class EnergyOrb : NetworkBehaviour
{
    public enum OrbState
    {
        /// <summary>Visible in its normal colour and ready to be picked up.</summary>
        Available,

        /// <summary>Just taken - showing the flash colour, no longer collectible.</summary>
        Collected,

        /// <summary>Invisible, waiting out the respawn delay.</summary>
        Hidden
    }

    /// <summary>
    /// Fusion automatically calls ApplyStateVisuals whenever this property changes, so the orb's appearance is always in sync with its state.
    /// </summary>
    [Tooltip("Current state of Orb.")]
    [Networked, OnChangedRender(nameof(ApplyStateVisuals))]
    public OrbState State { get; set; }

    [Tooltip("Counts out the current state - the colour flash, then the respawn wait. Replicated so a late-joining client inherits the correct remaining time.")]
    [Networked] private TickTimer stateTimer { get; set; }

    [Tooltip("How long after being collected before the orb comes back.")]
    [SerializeField] private float respawnDelaySeconds = 5f;

    [Tooltip("How long the orb stays visible in its collected colour before disappearing.")]
    [SerializeField] private float collectedFlashSeconds = 0.25f;

    [Tooltip("Colour shown for the brief moment between being collected and disappearing.")]
    [SerializeField] private Color collectedColor = new Color(1f, 0.9f, 0.3f);

    [Tooltip("Points awarded to the overall Team Score.")]
    [SerializeField] private int scoreValue = 1;

    // Cached references to the orb's Renderer and Collider, so they can be disabled when the orb is hidden or collected.
    private Renderer orbRenderer;
    private Collider orbCollider;

    // The orb's normal colour, captured once so the flash can be undone on respawn.
    private Color availableColor;

    private void Awake()
    {
        orbRenderer = GetComponentInChildren<Renderer>(true);
        orbCollider = GetComponent<Collider>();

        if (orbRenderer != null)
        {
            availableColor = orbRenderer.material.color;
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            State = OrbState.Available;
            stateTimer = default;
        }

        // Apply the visuals immediately on spawn so the orb is visible to all peers.
        ApplyStateVisuals();
    }

    /// <summary>
    /// Updates the network state of the energy orb based on its current state and the expiration of the state timer.
    /// </summary>
    /// <remarks>Handles transitions between available, collected, and hidden states, and manages the respawn
    /// timer.</remarks>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || State == OrbState.Available)
        {
            return;
        }

        if (!stateTimer.Expired(Runner))
        {
            return;
        }

        if (State == OrbState.Collected)
        {
            // Flash is over - disappear and start the respawn wait.
            State = OrbState.Hidden;
            stateTimer = TickTimer.CreateFromSeconds(Runner, respawnDelaySeconds);
            return;
        }

        State = OrbState.Available;
        stateTimer = default;
    }

    /// <summary>
    /// Handles the event when another collider enters the orb's trigger zone, awarding score and updating the orb's
    /// state if conditions are met.
    /// </summary>
    /// <remarks>Awards score to the team.</remarks>
    /// <param name="other">The collider that entered the trigger zone.</param>
    private void OnTriggerEnter(Collider other)
    {
        if (!Object.HasStateAuthority || State != OrbState.Available)
        {
            return;
        }

        // Only award points to a player that is alive - dead players should not be able to collect orbs.
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();

        if (player == null || player.isDead)
        {
            return;
        }

        // Award points to the team score. The GameStateManager is a singleton, so we can access it directly.
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.AddScore(scoreValue);
        }

        // Transition to the Collected state and start the flash timer.
        State = OrbState.Collected;
        stateTimer = TickTimer.CreateFromSeconds(Runner, collectedFlashSeconds);
    }

    /// <summary>
    /// Updates the orb's visual appearance and collider state based on its current state.
    /// </summary>
    /// <remarks>Only orbs in the Available state have an active collider, preventing interaction with hidden
    /// or collected orbs.</remarks>
    private void ApplyStateVisuals()
    {
        if (orbRenderer != null)
        {
            orbRenderer.enabled = State != OrbState.Hidden;
            orbRenderer.material.color = State == OrbState.Collected ? collectedColor : availableColor;
        }

        // Only an Available orb has a live collider, so a hidden orb cannot be walked into and
        // a flashing one cannot be collected a second time.
        if (orbCollider != null)
        {
            orbCollider.enabled = State == OrbState.Available;
        }
    }
}
