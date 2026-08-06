using Fusion;
using UnityEngine;

/// <summary>
/// A collectible energy orb that sits at a fixed point in the arena, is picked up by walking
/// over it, then reappears after a delay.
///
/// This is a scene NetworkObject placed directly in Arena.unity rather than something spawned
/// at runtime: the orb never moves and never really goes away, so collecting it is just a
/// state change. That keeps the whole feature in one script with no spawn-point manager, and
/// keeps it clear of the object pool.
///
/// The host owns every decision. Clients only render whatever State replication hands them,
/// which is what keeps the two peers agreeing without a single RPC.
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

    [Tooltip("Replicated so every peer shows the same orb in the same state. Drives both the visuals and whether the orb can be collected.")]
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

    [Tooltip("Points awarded to the player who collects this orb.")]
    [SerializeField] private int scoreValue = 1;

    // Found rather than wired in the inspector, so a new orb needs no setup beyond dropping
    // it in the scene and cannot end up half-configured.
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
            // Reading .material clones the shared material, so recolouring one orb does not
            // recolour every other orb using the same asset.
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

        // Applied on spawn as well as on change: OnChangedRender only fires on transitions, so
        // a client joining mid-match would otherwise render a collected orb as available.
        ApplyStateVisuals();
    }

    /// <summary>
    /// Advances the orb through its collected -> hidden -> available cycle.
    ///
    /// Host only; every other peer learns the new state through replication. The State check
    /// comes before the timer check on purpose - while the orb is Available the timer is not
    /// running, and a timer that was never started cannot be asked whether it expired.
    /// </summary>
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
    /// Collects the orb when a living player walks into it.
    ///
    /// Host only, exactly like Projectile.OnTriggerEnter: the host is the single source of
    /// truth for whether an orb was taken, so a client can never decide it collected one and
    /// no orb can be scored twice.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!Object.HasStateAuthority || State != OrbState.Available)
        {
            return;
        }

        // Colliders sit on the player's child meshes while PlayerHealth is on the prefab root,
        // so the search has to walk upwards - the same reason Projectile does.
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();

        if (player == null || player.isDead)
        {
            return;
        }

        // The score is a shared team total, so it does not matter which player walked into the
        // orb - the points go to the same place either way.
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.AddScore(scoreValue);
        }

        // Setting State here is what makes the orb uncollectible: the next trigger hit fails
        // the guard above, and ApplyStateVisuals disables the collider on every peer.
        State = OrbState.Collected;
        stateTimer = TickTimer.CreateFromSeconds(Runner, collectedFlashSeconds);
    }

    /// <summary>
    /// Renders whatever state the orb is in. Runs on every peer - on the host because it sets
    /// State, on clients because replication does - so all three peers agree on the visuals
    /// without anything being sent explicitly.
    /// </summary>
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
