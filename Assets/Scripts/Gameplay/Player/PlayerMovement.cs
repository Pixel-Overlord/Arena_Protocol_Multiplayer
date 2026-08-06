using Fusion;
using UnityEngine;

/// <summary>
/// Moves the player capsule from networked input, including the dash burst.
///
/// It lives on the spawned Player prefab's NetworkObject - that is how Fusion registers it and
/// gives it FixedUpdateNetwork and GetInput.
/// </summary>
[RequireComponent(typeof(NetworkCharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    [Tooltip("Speed of the dash burst. Must be comfortably above the controller's maxSpeed or the dash will feel like a slowdown.")]
    [SerializeField] private float dashSpeed = 22f;

    [Tooltip("How long the dash holds its speed before the player drops back to walking.")]
    [SerializeField] private float dashDurationSeconds = 0.2f;

    [Tooltip("Recovery before the player can dash again, measured from the start of the dash. Keep it comfortably longer than the duration.")]
    [SerializeField] private float dashCooldownSeconds = 2f;

    [Networked] private TickTimer dashTimer { get; set; }

    [Networked] private TickTimer dashCooldown { get; set; }

    [Tooltip("Direction the dash was launched in. Held for the whole dash so it cannot be steered mid-flight, and so a standing-still dash still has somewhere to go.")]
    [Networked] private Vector3 dashDirection { get; set; }

    [Tooltip("Last tick's buttons, so a dash fires once on the press rather than every tick the key is held.")]
    [Networked] private NetworkButtons previousButtons { get; set; }

    private NetworkCharacterController characterController;
    private PlayerHealth playerHealth;

    // The controller's configured walking speed, captured before any dash overwrites it.
    private float baseMaxSpeed;

    /// <summary>
    /// How much of the dash cooldown is still to run, 0-1. Lets the HUD show the bar refilling
    /// towards ready instead of just sitting empty with no explanation.
    /// </summary>
    public float DashCooldownNormalized
    {
        get
        {
            // Runner is null until this object is spawned, and RemainingTime needs it.
            if (Runner == null || dashCooldownSeconds <= 0f)
            {
                return 0f;
            }

            float? remaining = dashCooldown.RemainingTime(Runner);

            // No value means the timer was never started or has already expired.
            return remaining.HasValue ? Mathf.Clamp01(remaining.Value / dashCooldownSeconds) : 0f;
        }
    }

    /// <summary>
    /// True while the dash is recovering and cannot be used, so the HUD can grey the bar out.
    /// </summary>
    public bool IsDashOnCooldown => DashCooldownNormalized > 0f;

    private void Awake()
    {
        characterController = GetComponent<NetworkCharacterController>();
        playerHealth = GetComponent<PlayerHealth>();

        baseMaxSpeed = characterController.maxSpeed;
    }

    /// <summary>
    /// Clears any dash left over from a previous life.
    ///
    /// Needed because of pooling: a rejoining player reuses a parked object whose Awake will not
    /// run again, so someone who dropped mid-dash would otherwise come back permanently fast.
    /// </summary>
    public override void Spawned()
    {
        characterController.maxSpeed = baseMaxSpeed;
    }

    /// <summary>
    /// Unlike Update which runs on fixed frame update, this runs on a fixed tick.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // A corpse doesn't walk, and once the match is over nothing in the arena moves.
        if (playerHealth.isDead || GameStateManager.IsMatchOver)
        {
            return;
        }

        // Exit if this player has no input.
        if (GetInput(out NetworkInputData input) == false)
        {
            return;
        }

        characterController.Move(ResolveMoveDirection(input));
    }

    /// <summary>
    /// Decides which way the player moves this tick, starting a dash if one was requested and
    /// overriding the walk direction for as long as that dash runs.
    ///
    /// Deliberately not guarded on state authority. Movement here is already client-predicted,
    /// so the dash has to be predicted too or it would not begin until the host's confirmation
    /// arrived. Both peers derive it from the same replicated timers and the same input, so they
    /// reach the same answer on the same tick.
    /// </summary>
    private Vector3 ResolveMoveDirection(NetworkInputData input)
    {
        // WasPressed compares against last tick's buttons, giving a single event on the tick the
        // key goes down. IsSet is true for every tick it is held, which would refresh the dash
        // continuously and make the cooldown meaningless - the same trap PlayerAbility documents.
        bool dashPressed = input.Buttons.WasPressed(previousButtons, (int)InputButton.Dash);

        previousButtons = input.Buttons;

        if (dashPressed && dashCooldown.ExpiredOrNotRunning(Runner))
        {
            StartDash(input.Direction);
        }

        if (dashTimer.ExpiredOrNotRunning(Runner))
        {
            characterController.maxSpeed = baseMaxSpeed;
            return input.Direction;
        }

        // Move clamps horizontal speed to maxSpeed every tick, so the burst below would be
        // thrown away on the very next tick unless the ceiling is raised for the dash window.
        characterController.maxSpeed = dashSpeed;

        return dashDirection;
    }

    /// <summary>
    /// Launches the dash as an instant change of velocity.
    ///
    /// Set directly rather than left to the controller's acceleration, because acceleration only
    /// adds so much in a fifth of a second - far too little to read as a dash on top of a normal
    /// walk. Raising maxSpeed alone has the same problem: it lifts the ceiling without doing
    /// anything to reach it.
    /// </summary>
    private void StartDash(Vector3 inputDirection)
    {
        // A player standing still still dashes, straight ahead. Move brakes towards zero when
        // handed a zero direction, so without this a stationary dash would fizzle out on the spot.
        Vector3 direction = inputDirection.sqrMagnitude > 0.0001f
            ? inputDirection.normalized
            : transform.forward;

        dashDirection = direction;
        dashTimer = TickTimer.CreateFromSeconds(Runner, dashDurationSeconds);
        dashCooldown = TickTimer.CreateFromSeconds(Runner, dashCooldownSeconds);

        // Vertical velocity is carried over untouched so the dash cannot cancel gravity.
        Vector3 burstVelocity = direction * dashSpeed;
        burstVelocity.y = characterController.Velocity.y;

        characterController.Velocity = burstVelocity;
    }
}
