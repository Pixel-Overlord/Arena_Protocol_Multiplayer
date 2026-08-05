using Fusion;
using UnityEngine;

/// <summary>
/// Makes the arena camera follow this player, but only on the peer that owns it.
///
/// Every peer spawns a copy of every player prefab, so this has to opt out on the
/// remote copies - otherwise two players would fight over the same camera.
/// HasInputAuthority is true on exactly one peer per player, which makes it the
/// right test for "is this my player".
/// </summary>
public class PlayerCamera : NetworkBehaviour
{
    [Tooltip("World-space offset from the player. Not rotated with the player, so " +
             "the view stays aligned with the WASD directions.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -10f);

    [Tooltip("Higher values make the camera lag further behind the player.")]
    [SerializeField] private float smoothTime = 0.15f;

    private Transform cameraTransform;
    private Vector3 followVelocity;

    public override void Spawned()
    {
        // Remote copies leave cameraTransform null and do nothing in LateUpdate.
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

    /// <summary>
    /// LateUpdate rather than Update so the player has already moved this frame -
    /// following in Update would leave the camera one frame behind and judder.
    /// </summary>
    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            return;
        }

        Vector3 desiredPosition = transform.position + offset;

        cameraTransform.position = Vector3.SmoothDamp(
            cameraTransform.position,
            desiredPosition,
            ref followVelocity,
            smoothTime);

        cameraTransform.LookAt(transform.position);
    }
}
