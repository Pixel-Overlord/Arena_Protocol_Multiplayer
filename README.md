# Arena Protocol

A two-player co-op survival arena built with Unity and Photon Fusion 2. Two players share an
arena, fight waves of enemies, collect energy orbs for a shared score, and lose together the
moment either of them falls.

| | |
|---|---|
| **Unity** | 2022.3.62f3 (Built-in Render Pipeline, 3D) |
| **Networking** | Photon Fusion 2.1.1 Stable — Host mode (client/server) |
| **Players** | 2 |
| **Art** | Primitive meshes only, by design |

---

## Setup

### 1. Photon App ID

The project ships with a Fusion App ID already configured in
`Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`. To use your own, replace
`AppIdFusion` with an App ID from the [Photon dashboard](https://dashboard.photonengine.com)
(app type: **Fusion**).

### 2. Build

`File → Build Settings` — scene order must be:

| Index | Scene |
|---|---|
| 0 | `Assets/Scenes/Menu.unity` |
| 1 | `Assets/Scenes/Arena.unity` |

`PhotonAppSettings` is baked into the build, so **rebuild the standalone player after changing
the region**, or the editor and the build will disagree.

---

## Running a match

Two peers are needed. The usual loop is the Unity editor as one and a standalone build as the
other (a build is included at `GameBuild/`).

1. Launch both peers.
2. On the first: type a room name → **Host**.
3. On the second: type the **same** room name → **Join**.
4. The match begins once both players have spawned. Until then the HUD reads
   *"Waiting for players..."*.

`requiredPlayers` on the Arena's `GameStateManager` controls how many players must arrive before
wave 1 releases. Set it to **1** to play solo while testing.

> Running the editor and a build on one machine is supported. They deliberately use different
> `PlayerPrefs` keys for their player identity, because a shared identity would make Photon
> reject the second peer.

### Controls

| Input | Action |
|---|---|
| `W` `A` `S` `D` | Move |
| `Left Mouse` | Fire |
| `Left Shift` | Dash |
| `Q` | Ability (Shield or Heal) |

---

## Features

| GDD requirement | Implementation |
|---|---|
| Networked movement + follow camera | `PlayerMovement`, `PlayerCamera` |
| Health & damage | `PlayerHealth`, `IDamageable`, `Projectile` |
| Dash | `PlayerMovement` — velocity impulse on a networked cooldown |
| Projectile attack | `Weapon` — host-spawned, networked fire-rate |
| Shield **or** Heal | `PlayerAbility` — one player gets each, never both the same |
| Enemy AI | `Enemy` — Patrol → Chase → Attack → Dead |
| Wave system | `GameStateManager`, `EnemySpawner` |
| Energy orbs + team score | `EnergyOrb`, `GameStateManager.TeamScore` |
| HUD: health, cooldowns, score, ping | `PlayerHUD` |
| Reconnection with state restoration | `FusionBootstrap`, `PlayerStateStore`, `PlayerSpawner` |
| Game over on a player's death | `GameStateManager` |

### Abilities

The two players never receive the same ability. The first into the arena rolls at random; the
second is handed whichever one is left (`PlayerSpawner.AssignComplementaryAbility`). Both run on
a networked meter and cooldown, shown on the HUD's power bar.

- **Shield** — negates all incoming damage while active.
- **Heal** — restores health to the caster and to any player within `healRadius`.

---

## Technical approach

### Host-authoritative, no RPCs

One peer is both host and player. The host owns every gameplay decision: spawning, damage, enemy
AI, orb collection, scoring, match state. Clients never decide any of it.

Nothing in the project uses an RPC. Every piece of shared truth is a `[Networked]` property, and
peers react to changes locally through `OnChangedRender`. Death visuals, the ability glow and the
orb's colour flash all work this way — the flag is the single source of truth and each peer
renders it. This keeps the two peers incapable of disagreeing about state, because there is only
one copy of it.

### Client prediction

Movement and dash are **not** authority-guarded, deliberately. Fusion predicts the local player's
movement and reconciles against the host, so gating them on the host would mean nothing happened
until a round trip completed. Both peers derive the dash from the same replicated timers and the
same input, so they reach the same result on the same tick.

Everything that affects *another* player — damage, healing, scoring, orb collection — is strictly
host-only.

### Reconnection

Rejoining works because identity survives the process dying. Each installation generates a GUID
once, stores it in `PlayerPrefs`, and passes it as the Photon `UserId` via `StartGameArgs.AuthValues`.

When a player disconnects, the host reads that id back with `Runner.GetPlayerUserId` and snapshots
their state into `PlayerStateStore` in the instant before their object is despawned. Rejoining the
same room restores it.

| Restored | Not restored |
|---|---|
| Health | Position (you respawn at a spawn point) |
| Ability type | |
| Ability meter and cooldown | |

The cooldown is stored in **seconds**, not as a `TickTimer`. A `TickTimer` is anchored to a tick
in the running session and keeps counting while you are away, so it would read as long expired on
return — and quitting would become a way to skip cooldowns.

The team score needs no snapshot: it lives on `GameStateManager`, a scene object that never
despawns.

A disconnected peer is returned to the menu with the reason for the drop, which is what makes
rejoining possible at all — there is no way back from a frozen arena.

### Object pooling

`PooledNetworkObjectProvider` recycles projectiles, enemies and players instead of
instantiating and destroying them. It plugs into Fusion's own spawn path, so `Runner.Spawn` and
`Runner.Despawn` are unchanged at every call site and each peer pools independently.

**This has one consequence worth knowing before reading the code.** Despawned objects are parked
with `SetActive(false)`, not destroyed — so Unity's fake-null never trips, and a cached reference
to a departed player stays non-null. Reading a `[Networked]` property off one throws
*"can only be accessed when Spawned() has been called"*.

Every site that touches networked state on an object it did not spawn therefore checks
`IsLive()` (`NetworkObjectExtensions`) rather than `!= null`.

### Scene objects vs spawned objects

`GameStateManager` and the energy orbs are **scene** `NetworkObject`s placed in `Arena.unity`,
not runtime spawns. An orb never moves and never truly leaves, so collecting it is a state change
rather than a despawn — which keeps the whole feature in one script with no spawn-point manager,
and keeps it clear of the pooling caveat above.

### Project layout

```
Assets/Scripts/
├── Combat/        IDamageable, Projectile, Weapon
├── Gameplay/      GameStateManager, EnergyOrb
│   ├── Enemy/     Enemy, EnemySpawner, EnemyWeapon
│   └── Player/    PlayerMovement, PlayerHealth, PlayerAbility,
│                  PlayerCamera, PlayerSpawner
├── Networking/    FusionBootstrap, PlayerStateStore, NetworkInputData,
│                  InputButton, PooledNetworkObjectProvider,
│                  NetworkObjectExtensions
└── UI/            MenuUI, PlayerHUD
```

---

## Engineering tradeoffs

**No host migration.** If the host quits, the session ends and every client is returned to the
menu with an explanation. Supporting migration would mean transferring authority over every
enemy, orb and match-state object mid-flight — a large amount of work and the hardest thing in
the project to test, for a scenario a two-player demo can reasonably declare out of scope.

**Fixed region rather than best-region.** Costs a little latency for players far from the chosen
region, but removes an entire class of intermittent "room not found" failures. For something
graded by someone I cannot watch, predictable beats optimal.

**Scoring comes only from orbs.** Killing enemies awards nothing. This keeps a single scoring
rule that is trivial to explain, and makes orbs worth breaking cover for rather than a sideshow
to combat.

**Heal affects nearby allies, not just the caster.** The GDD specifies "Shield or Heal" without
saying whether Heal is self-only. Since the match ends the moment either player dies, keeping a
teammate alive is the whole point of the ability — a self-only Heal would be strictly worse than
Shield and the two would stop being complementary.

**Enemies patrol continuously with no idle pause.** An earlier version had an `Idle` dwell state,
removed to match the GDD's four states exactly. "Return to spawn" is expressed through patrol —
waypoints are drawn around each enemy's spawn position, so giving up a chase walks it home.

**No respawn.** A player who dies stays dead, which follows from the match ending on the first
death. A player who dies and *then* reconnects comes back alive at full health, because
restoring them dead would hand back a character they could never play.

**The two ping readouts measure different things.** The client shows its round trip to the host.
The host is the server, so its round trip to itself is zero and useless — it shows its trip to
the Photon relay instead. The client's figure therefore contains the host's.

**Enemies use distance maths, not NavMesh.** Ranges are straight line-of-sight distances on the
arena floor. Enemies will walk into obstacles rather than around them, which is acceptable in an
open arena and avoids baking navigation for primitive geometry.

---

## Known issue

**GAME OVER has no exit.** When the match ends the arena freezes and the banner appears, but
there is no way back to the menu — the game must be closed. The fix is a timer on
`GameStateManager` that ends the session a few seconds after the match, returning both peers to
the menu through the disconnect path that already works.