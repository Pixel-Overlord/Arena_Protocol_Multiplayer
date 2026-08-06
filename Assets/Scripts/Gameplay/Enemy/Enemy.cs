using Fusion;
using UnityEngine;

/// <summary>
/// Represents an enemy character that patrols, chases, and attacks players in a networked multiplayer environment.
/// </summary>
/// <remarks>Handles state transitions, health management, and network synchronization for enemy AI behavior.
/// Integrates with the game state manager and supports debugging through replicated state properties.</remarks>
[RequireComponent(typeof(EnemyWeapon))]
public class Enemy : NetworkBehaviour, IDamageable
{
    public enum EnemyState
    {
        Patrol,
        Chase,
        Attack,
        Dead
    }

    [Tooltip("Replicated so clients can drive animations or debug what the host decided.")]
    [Networked] public EnemyState State { get; set; }

    [Networked] public float currentHealth { get; set; }

    [Tooltip("Who this enemy locked onto. Replicated purely so the state can be inspected from a client while debugging.")]
    [Networked] private PlayerRef targetPlayer { get; set; }

    [Tooltip("Patrol give-up timer, so an unreachable patrol point cannot strand the enemy forever.")]
    [Networked] private TickTimer stateTimer { get; set; }

    [Header("Ranges")]
    [Tooltip("A player inside this distance is chased.")]
    [SerializeField] private float chaseRange = 12f;

    [Tooltip("A player inside this distance is shot at. Must be smaller than chaseRange or the enemy will never close the gap.")]
    [SerializeField] private float attackRange = 5f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("Degrees per second the enemy can turn. Low values make it visibly swing around towards a player instead of snapping.")]
    [SerializeField] private float turnSpeedDegrees = 360f;

    [Tooltip("How far from its spawn point the enemy will wander while patrolling.")]
    [SerializeField] private float patrolRadius = 15f;

    [Tooltip("How close counts as having arrived at a patrol point.")]
    [SerializeField] private float arriveDistance = 0.5f;

    [Header("Timing")]
    [Tooltip("Give up on a patrol point after this long, in case it ended up somewhere unreachable.")]
    [SerializeField] private float patrolTimeoutSeconds = 8f;

    [Header("Health")]
    [SerializeField] private float maxHealth = 30f;

    private EnemyWeapon weapon;

    // Plain fields, not [Networked]: only the host ever reads them, so replicating them
    // would spend bandwidth on data no client can use.
    private Vector3 homePosition;
    private Vector3 patrolTarget;

    public bool IsDead => State == EnemyState.Dead;

    /// <summary>
    /// Runs on every spawn, including when the pool hands back a recycled instance -
    /// which is exactly why all per-life state is reset here and not in Awake. Awake fires
    /// once per GameObject; a pooled enemy would keep the previous life's health without this.
    /// </summary>
    public override void Spawned()
    {
        weapon = GetComponent<EnemyWeapon>();

        // Wherever the spawner dropped this enemy becomes the centre of its patrol area.
        homePosition = transform.position;

        if (!Object.HasStateAuthority)
        {
            return;
        }

        currentHealth = maxHealth;
        targetPlayer = PlayerRef.None;

        EnterPatrol();
    }

    public override void FixedUpdateNetwork()
    {
        // Clients render what the host decided; they must not run the AI themselves or the
        // two peers would drift apart.
        if (!Object.HasStateAuthority || State == EnemyState.Dead)
        {
            return;
        }

        // Match over - the arena freezes. Returning before the state machine leaves each
        // enemy standing exactly where it was, rather than resuming its patrol.
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
        if (arrived || stateTimer.ExpiredOrNotRunning(Runner))
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

        targetPlayer = target.Object.InputAuthority;
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
        targetPlayer = PlayerRef.None;
        patrolTarget = PickPatrolTarget();
        stateTimer = TickTimer.CreateFromSeconds(Runner, patrolTimeoutSeconds);
    }

    private void EnterChase(PlayerHealth target)
    {
        if (target == null)
        {
            EnterPatrol();
            return;
        }

        State = EnemyState.Chase;
        targetPlayer = target.Object.InputAuthority;
    }

    private void EnterAttack(PlayerHealth target)
    {
        State = EnemyState.Attack;
        targetPlayer = target.Object.InputAuthority;
    }

    #endregion

    #region Vector maths helpers

    /// <summary>
    /// Nearest living player and the squared distance to them, or null if nobody is alive.
    ///
    /// Walks Runner.ActivePlayers rather than FindObjectsOfType: this runs every tick for
    /// every enemy, and FindObjectsOfType would scan the whole scene and allocate an array
    /// each time. GetPlayerObject is a dictionary lookup that PlayerSpawner already fills in.
    /// </summary>
    private PlayerHealth FindNearestLivePlayer(out float sqrDistance)
    {
        PlayerHealth nearest = null;
        sqrDistance = float.MaxValue;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);

            // IsLive rather than a null check: a despawned player object is parked by the
            // pool rather than destroyed, so it can still be handed back here and reading
            // isDead off it would throw inside FixedUpdateNetwork.
            if (!playerObject.IsLive())
            {
                continue;
            }

            if (!playerObject.TryGetComponent(out PlayerHealth health) || health.isDead)
            {
                continue;
            }

            float candidateSqrDistance = (playerObject.transform.position - transform.position).sqrMagnitude;

            if (candidateSqrDistance < sqrDistance)
            {
                sqrDistance = candidateSqrDistance;
                nearest = health;
            }
        }

        return nearest;
    }

    /// <summary>
    /// A random point on the XZ plane within patrolRadius of the spawn position, so enemies
    /// wander their own area instead of all drifting to one corner of the arena.
    /// </summary>
    private Vector3 PickPatrolTarget()
    {
        Vector2 offset = Random.insideUnitCircle * patrolRadius;

        // Keep the enemy's current height - there is no navmesh to sample a floor from.
        return new Vector3(homePosition.x + offset.x, transform.position.y, homePosition.z + offset.y);
    }

    /// <summary>
    /// Steps towards a destination at moveSpeed and turns to face the way it is going.
    /// Moves the transform directly, exactly like Projectile does: correct here because
    /// only the state authority runs this, and NetworkTransform replicates the result.
    /// </summary>
    private void MoveTowards(Vector3 destination)
    {
        Vector3 toDestination = destination - transform.position;

        // Movement is planar. Without this an enemy would try to climb towards a player
        // standing on higher ground and lift off the floor.
        toDestination.y = 0f;

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
    /// Rotates towards a direction at a capped rate rather than snapping to it.
    /// </summary>
    private void FaceDirection(Vector3 direction)
    {
        Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeedDegrees * Runner.DeltaTime);
    }

    #endregion

    /// <summary>
    /// Reduces the enemy's health by the specified amount and handles death state transitions.
    /// </summary>
    /// <param name="damageAmount">The amount of damage to subtract from the enemy's current health.</param>
    public void applyDamage(float damageAmount)
    {
        if (!Object.HasStateAuthority || State == EnemyState.Dead)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damageAmount);

        if (currentHealth > 0f)
        {
            return;
        }

        // Set the state before despawning so anything reading it this tick sees a corpse
        // rather than a full-health enemy.
        State = EnemyState.Dead;

        // Killing enemies deliberately awards no points - the score comes only from collecting
        // energy orbs. The wave loop still needs telling so the next wave can be released.
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
