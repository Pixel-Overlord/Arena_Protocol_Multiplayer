using Fusion;
using UnityEngine;

/// <summary>
/// Represents a networked enemy character that patrols, chases, and attacks players in the arena.
/// </summary>
public class Enemy : NetworkBehaviour, IDamageable
{
    public enum EnemyState
    {
        Patrol,
        Chase,
        Attack,
        Dead
    }

    [Tooltip("The current State of Enemy.")]
    [Networked] public EnemyState State { get; set; }

    [Tooltip("The current health of Enemy.")]
    [Networked] public float CurrentHealth { get; set; }

    [Tooltip("Who this enemy locked onto.")]
    [Networked] private PlayerRef TargetPlayer { get; set; }

    [Tooltip("Patrol give-up timer, so an unreachable patrol point cannot strand the enemy forever.")]
    [Networked] private TickTimer StateTimer { get; set; }

    [Header("Ranges")]
    [Tooltip("A player inside this distance is chased.")]
    [SerializeField] private float chaseRange = 12f;

    [Tooltip("A player inside this distance is shot at. Must be smaller than chaseRange or the enemy will never close the gap.")]
    [SerializeField] private float attackRange = 5f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("Degrees per second the enemy can turn.")]
    [SerializeField] private float turnSpeedDegrees = 360f;

    [Tooltip("How far from its spawn point the enemy will wander while patrolling.")]
    [SerializeField] private float patrolRadius = 15f;

    [Tooltip("How close counts as having arrived at a patrol point.")]
    [SerializeField] private float arriveDistance = 0.5f;

    [Header("Timing")]
    [Tooltip("Give up on a patrol point after this long, in case it ended up somewhere unreachable.")]
    [SerializeField] private float patrolTimeoutSeconds = 8f;

    [Header("Health")]
    [Tooltip("Max Health for the Enemy.")]
    [SerializeField] private float maxHealth = 30f;

    private EnemyWeapon weapon;

    // The position where the enemy was spawned, which is also the center of its patrol area.
    private Vector3 homePosition;

    // The current patrol target point, which is randomly chosen within patrolRadius of homePosition.
    private Vector3 patrolTarget;

    public bool IsDead => State == EnemyState.Dead;

    /// <summary>
    /// Initializes the enemy's weapon, sets the patrol area center, and resets health and target state when spawned.
    /// </summary>
    /// <remarks>Called when the enemy is spawned to prepare it for gameplay.</remarks>
    public override void Spawned()
    {
        weapon = GetComponent<EnemyWeapon>();

        // Wherever the spawner dropped this enemy becomes the centre of its patrol area.
        homePosition = transform.position;

        if (!Object.HasStateAuthority)
        {
            return;
        }

        CurrentHealth = maxHealth;
        TargetPlayer = PlayerRef.None;

        EnterPatrol();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || State == EnemyState.Dead)
        {
            return;
        }

        if (GameStateManager.IsMatchOver)
        {
            return;
        }

        PlayerHealth target = FindNearestLivePlayer(out float sqrDistanceToTarget);

        // Squared distances throughout: comparing squares avoids a square root every tick
        // per enemy, and gives the identical answer for a >= / <= test.
        bool inChaseRange = target != null && sqrDistanceToTarget <= chaseRange * chaseRange;
        bool inAttackRange = target != null && sqrDistanceToTarget <= attackRange * attackRange;

        switch (State)
        {
            case EnemyState.Patrol:
                TickPatrol(target, inChaseRange);
                break;

            case EnemyState.Chase:
                TickChase(target, inChaseRange, inAttackRange);
                break;

            case EnemyState.Attack:
                TickAttack(target, inChaseRange, inAttackRange);
                break;
        }
    }

    #region States

    /// <summary>
    /// Wandering between random points near the spawn position, which is also what brings an
    /// enemy back home after it gives up a chase. Chasing always wins over patrolling, so the
    /// range check comes first.
    /// </summary>
    private void TickPatrol(PlayerHealth target, bool inChaseRange)
    {
        if (inChaseRange)
        {
            EnterChase(target);
            return;
        }

        MoveTowards(patrolTarget);

        // Flatten before measuring: the enemy walks on the XZ plane, so a difference in
        // height would otherwise stop it ever registering as "arrived".
        Vector3 toPatrolTarget = patrolTarget - transform.position;
        toPatrolTarget.y = 0f;

        bool arrived = toPatrolTarget.sqrMagnitude <= arriveDistance * arriveDistance;

        // Arriving picks the next point rather than stopping, so patrol is a continuous
        // wander. The timer covers a point that turned out to be unreachable.
        if (arrived || StateTimer.ExpiredOrNotRunning(Runner))
        {
            EnterPatrol();
        }
    }

    /// <summary>
    /// Closing on a player until they are near enough to shoot.
    /// </summary>
    private void TickChase(PlayerHealth target, bool inChaseRange, bool inAttackRange)
    {
        // Target died, disconnected, or outran the enemy - give up and go back to wandering.
        if (target == null || !inChaseRange)
        {
            EnterPatrol();
            return;
        }

        if (inAttackRange)
        {
            EnterAttack(target);
            return;
        }

        TargetPlayer = target.Object.InputAuthority;
        MoveTowards(target.transform.position);
    }

    /// <summary>
    /// In range and shooting. Stands its ground so it doesn't walk into the player, and
    /// keeps facing them so the projectiles leave in the right direction.
    /// </summary>
    private void TickAttack(PlayerHealth target, bool inChaseRange, bool inAttackRange)
    {
        if (target == null || !inChaseRange)
        {
            EnterPatrol();
            return;
        }

        // Drifted out of weapon range but still worth pursuing.
        if (!inAttackRange)
        {
            EnterChase(target);
            return;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > 0.0001f)
        {
            FaceDirection(toTarget.normalized);
        }

        // The weapon owns its own fire-rate cooldown, so this can be called every tick.
        weapon.TryFire(target.transform.position);
    }

    #endregion

    #region Transitions

    private void EnterPatrol()
    {
        State = EnemyState.Patrol;
        TargetPlayer = PlayerRef.None;
        patrolTarget = PickPatrolTarget();
        StateTimer = TickTimer.CreateFromSeconds(Runner, patrolTimeoutSeconds);
    }

    private void EnterChase(PlayerHealth target)
    {
        if (target == null)
        {
            EnterPatrol();
            return;
        }

        State = EnemyState.Chase;
        TargetPlayer = target.Object.InputAuthority;
    }

    private void EnterAttack(PlayerHealth target)
    {
        State = EnemyState.Attack;
        TargetPlayer = target.Object.InputAuthority;
    }

    #endregion

    #region Vector maths helpers

    /// <summary>
    /// Finds the nearest live player and calculates the squared distance to that player.
    /// </summary>
    /// <param name="sqrDistance">The squared distance to the nearest live player.</param>
    /// <returns>The PlayerHealth of the nearest live player, or null if no live players are found.</returns>
    private PlayerHealth FindNearestLivePlayer(out float sqrDistance)
    {
        PlayerHealth nearest = null;
        sqrDistance = float.MaxValue;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);

            if (!playerObject.IsLive() ||
                !playerObject.TryGetComponent(out PlayerHealth health) ||
                health.isDead)
            {
                continue;
            }

            float distance = (playerObject.transform.position - transform.position).sqrMagnitude;

            if (distance < sqrDistance)
            {
                sqrDistance = distance;
                nearest = health;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Picks a random point within patrolRadius of the home position to serve as the next patrol target.
    /// </summary>
    /// <returns>The target Vector.</returns>
    private Vector3 PickPatrolTarget()
    {
        Vector2 offset = Random.insideUnitCircle * patrolRadius;

        Vector3 target = homePosition;
        target.x += offset.x;
        target.z += offset.y;
        target.y = transform.position.y;

        return target;
    }

    /// <summary>
    /// This moves the enemy towards a destination at a constant speed, ignoring vertical differences
    /// to keep movement planar.
    /// </summary>
    /// <param name="destination">To where the Enemy will move.</param>
    private void MoveTowards(Vector3 destination)
    {
        Vector3 toDestination = destination - transform.position;

        // the enemy walks on the XZ plane.
        toDestination.y = 0f;

        // Squared distance check: avoids a square root every tick per enemy.
        if (toDestination.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 direction = toDestination.normalized;

        // Runner.DeltaTime, not Time.deltaTime: this is the fixed network tick length, so
        // the distance covered per tick is identical on every peer.
        transform.position += direction * moveSpeed * Runner.DeltaTime;

        FaceDirection(direction);
    }

    /// <summary>
    /// Makes the enemy face the specified direction, turning at a maximum rate of turnSpeedDegrees per second.
    /// </summary>
    private void FaceDirection(Vector3 direction)
    {
        Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeedDegrees * Runner.DeltaTime);
    }

    #endregion

    /// <summary>
    /// Reduces the enemy's health by the specified amount and handles death state and despawning if health reaches
    /// zero.
    /// </summary>
    /// <param name="damageAmount">The amount to subtract from the enemy's current health.</param>
    public void applyDamage(float damageAmount)
    {
        if (!Object.HasStateAuthority || State == EnemyState.Dead)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damageAmount);

        if (CurrentHealth > 0f)
        {
            return;
        }

        State = EnemyState.Dead;

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.NotifyEnemyKilled();
        }

        Runner.Despawn(Object);
    }

    /// <summary>
    /// Draws the two detection radii in the editor. Selecting an enemy in the scene view
    /// makes the chase/attack ranges checkable without guessing at the numbers.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
