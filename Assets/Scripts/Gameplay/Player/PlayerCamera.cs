using Fusion;
using UnityEngine;

/// <summary>
/// Makes the arena camera follow this player, but only on the peer that owns it.
/// </summary>
public class PlayerCamera : NetworkBehaviour
{
    // The offset is set to (0, 10, -10) so the camera is above and behind the player, looking down at them.
    [Tooltip("The offset camera should have with Player's position.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -10f);


    // SmoothDamp's smoothTime is the time it takes to reach the target position,
    // so a smaller value means a faster camera.
    // A value of 0.15f is a good starting point for a responsive camera that still feels smooth.
    [Tooltip("Higher values make the camera lag further behind the player.")]
    [SerializeField] private float timeToReachTargetPosition = 0.15f;

    private Transform cameraTransform;
    private Vector3 followVelocity;

    public override void Spawned()
    {
        if (!HasInputAuthority)
        {
            return;
        }

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("PlayerCamera: no camera tagged MainCamera in the arena.", this);
            return;
        }

        cameraTransform = mainCamera.transform;

        // Snap on the first frame, otherwise the camera visibly swoops in from
        // wherever the arena camera happened to be placed.
        cameraTransform.position = transform.position + offset;
        cameraTransform.LookAt(transform.position);
    }

    private void LateUpdate()
    {
        // LateUpdate is a plain Unity callback, so it runs on every copy of this player -
        // including the remote ones, where Spawned() returned before resolving a camera.
        // Without this guard those copies dereference null once per frame, on every peer.
        if (cameraTransform == null)
        {
            return;
        }

        Vector3 desiredPosition = transform.position + offset;

        // Smoothly move the camera towards the desired position using SmoothDamp for a smooth follow effect.
        cameraTransform.position = Vector3.SmoothDamp(
            cameraTransform.position,
            desiredPosition,
            ref followVelocity,
            timeToReachTargetPosition);

        cameraTransform.LookAt(transform.position);
    }
}
