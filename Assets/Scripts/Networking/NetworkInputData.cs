using Fusion;
using UnityEngine;

/// <summary>
/// Represents input data for networked interactions, including movement direction and button states.
/// </summary>
public struct NetworkInputData : INetworkInput
{
    public Vector3 Direction;

    public NetworkButtons Buttons;
}
