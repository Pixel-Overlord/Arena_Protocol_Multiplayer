using Fusion;
using UnityEngine;

/// <summary>
/// The per-tick input this peer sends to the host.
///
/// Fusion collects this in FusionBootstrap.OnInput, sends it to the state
/// authority, and replays it during resimulation - which is what keeps the
/// simulation identical on both ends. Keep it small: it goes over the wire
/// every tick.
/// </summary>
public struct NetworkInputData : INetworkInput
{
    /// <summary>
    /// Normalized world-space direction built from WASD. Zero when idle.
    /// </summary>
    public Vector3 Direction;

    public NetworkButtons Buttons;
}
