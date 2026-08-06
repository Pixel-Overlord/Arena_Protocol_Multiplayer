using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs whichever ability (Shield or Heal) this player was randomly assigned — 
/// activation, meter drain, cooldown, and the glow state that shows it to everyone.
/// </summary>
public class PlayerAbility : NetworkBehaviour
{
    public enum AbilityType
    {
        Shield,
        Heal
    }

    [Tooltip("This contains the amount of color glow around player as per the ability.")]
    public enum GlowState
    {
        None,
        Shield,
        HealSelf,
        HealShared
    }

    [Networked] public AbilityType Type { get; set; }

    [Tooltip("Remaining active-duration for power use.")]
    [Networked] private float Meter { get; set; }   // drains while active.

    [Tooltip("Blocks reactivating until it expires")]
    [Networked] private TickTimer cooldown { get; set; }

    [Tooltip("check which button was pressed previously.")]
    [Networked] private NetworkButtons previousButtons { get; set; }

    //[Networked, OnChangedRender(nameof(OnGlowChanged))] private GlowState Glow { get; set; }

    [Tooltip("How long one activation lasts.")]
    [SerializeField] private float maxMeter = 5f;

    [Tooltip("How fast meter empties.")]
    [SerializeField] private float drainPerSecond = 1f;

    [Tooltip("Recovery time after meter empties.")]
    [SerializeField] private float cooldownSeconds = 4f;

    [Tooltip("HP restored per second, to self and to anyone in range.")]
    [SerializeField] private float healPerSecond = 15f;

    [Tooltip("How close another player should be to also receive heal.")]
    [SerializeField] private float healRadius = 3f;

    [Tooltip("What PlayerHealth.ApplyDamage checks to decide whether to negate damage.\r\nScript changes — new file ")]
    public bool IsShieldActive => Type == AbilityType.Shield && Meter > 0f;

    public override void FixedUpdateNetwork()
    {
        if (!GetInput(out NetworkInputData input))
        {
            return;
        }

        if (!Object.HasStateAuthority)
        {
            previousButtons = input.Buttons;
            return;
        }
    }
}
