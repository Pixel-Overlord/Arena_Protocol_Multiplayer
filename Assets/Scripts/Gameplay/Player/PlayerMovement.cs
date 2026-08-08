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

    [Tooltip("Timer used to track the duration of a dash action.")]
    [Networked] private TickTimer dashTimer { get; set; }

    [Tooltip("The cooldown timer for the player's dash ability.")]
    [Networked] private TickTimer dashCooldown { get; set; }

    [Tooltip("Direction the dash was launched in.")]
    [Networked] private Vector3 dashDirection { get; set; }

    [Tooltip("Last tick's buttons, so a dash fires once on the press rather than every tick the key is held.")]
    [Networked] private NetworkButtons previousButtons { get; set; }

    private NetworkCharacterController characterController;
    private PlayerHealth playerHealth;

    // Cache the controller's maxSpeed so it can be restored after a dash.
    private float baseMaxSpeed;

    /// <summary>
    /// Gets the normalized dash cooldown value, ranging from 0 to 1.
    /// </summary>
    public float DashCooldownNormalized
    {
        // since this is a property, one can only get it or set it.
        get
        {
            // If the runner is not initialized or the cooldown duration is non-positive, return 0.
            if (Runner == null || dashCooldownSeconds <= 0f)
            {
                return 0f;
            }

            // If the cooldown timer was never started or has expired, return 0.
            float? remaining = dashCooldown.RemainingTime(Runner);

            if (!remaining.HasValue)
                return 0f;

            return remaining.Value / dashCooldownSeconds;
        }
    }

    /// <summary>
    /// Indicates whether the dash ability is currently on cooldown.
    /// </summary>
    public bool IsDashOnCooldown => DashCooldownNormalized > 0f;

    private void Awake()
    {
        characterController = GetComponent<NetworkCharacterController>();
        playerHealth = GetComponent<PlayerHealth>();

        baseMaxSpeed = characterController.maxSpeed;
    }

    /// <summary>
    /// Sets the character controller's maximum speed to the base maximum speed when the object is spawned.
    /// </summary>
    public override void Spawned()
    {
        characterController.maxSpeed = baseMaxSpeed;
    }

    /// <summary>
    /// Unlike Fixed Update which runs on fixed frame update, this runs on a fixed tick.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // Exit if the player is dead or the match is over.
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
    /// Determines the movement direction based on user input and dash state.
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
    /// Starts a dash in the specified direction, setting the dash timer and cooldown timer accordingly.
    /// </summary>
    /// <param name="inputDirection">The direction to dash in.</param>
    private void StartDash(Vector3 inputDirection)
    {
        // If the input direction is nearly zero, default to dashing forward.
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
