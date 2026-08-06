using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This enum is used to identify which buttons are pressed in the NetworkInputData.
/// The values correspond to the index of the button in the NetworkButtons bitfield.
/// 
/// Note: It has been separated into its own file so that both FusionBootstrap.cs and Weapon.cs can reference the same enum and avoid duplication.
/// In addition to that, it is also used in the PlayerController.cs script to handle input for dashing and using abilities.
/// </summary>
public enum InputButton
{
    Fire = 0,
    Dash = 1,
    Ability = 2
}