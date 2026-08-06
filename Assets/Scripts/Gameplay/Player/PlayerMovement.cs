using Fusion;
using UnityEngine;

/// <summary>
/// Moves the player capsule from networked input.
///
/// It lives on the spawned Player prefab's NetworkObject - that is how Fusion registers it and
/// gives it FixedUpdateNetwork and GetInput.
/// </summary>
[RequireComponent(typeof(NetworkCharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    private NetworkCharacterController characterController;
    private PlayerHealth playerHealth;

    private void Awake()
    {
        characterController = GetComponent<NetworkCharacterController>();
        playerHealth = GetComponent<PlayerHealth>();
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

        characterController.Move(input.Direction);
    }
}
