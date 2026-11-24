# Heft

Networked physics grab-and-carry for co-op games, on Netcode for GameObjects. Look at an object within your reach,
grab it, and it hangs from that point on a spring in front of you. How hard the spring can pull is your **strength**:
light things follow your hand, heavy things sag and drag along the floor, and two or three players grabbing the same
thing share its weight and can lift what none of them could alone.

## Requirements

- Unity 6.3 (6000.3) or newer, Netcode for GameObjects 2.7+
- **Gameplay Tags** and **Character Stats** (`com.aidancmays.gameplay-tags`, `com.aidancmays.character-stats`). Add
  them first, in that order.

## Setup

**Objects**: add **Carryable** to a networked prefab with a NetworkObject, a Rigidbody (its mass is what strength is
measured against) and colliders. Sync it with a NetworkTransform in **Server** authority plus a NetworkRigidbody: the
server simulates it and clients watch. Register the prefab in your network prefab list as usual.

**Players**: add **Grabber** to the player root (with a NetworkObject). Set *Aim* to the camera or head. Optionally:

- *Strength Stat* and *Reach Stat*: stats on the player's StatSheet (else *Default Strength* and *Default Reach*).
  Grip force = strength x *Force Per Strength* newtons.
- *Carrying Tag*: held on every machine while carrying (condition stat modifiers on it, or slow the player).
- *Blocked By*: tags that make the player let go and stop grabbing (knocked out, dead).

Grabber reads no input. Your owner-side input code calls `TryGrab()`, `Release()`, `Throw()` and
`AdjustHoldDistance(delta)`.

**Hinged objects** (a door, a lid, a book cover): add **Hinged** next to the Carryable. The hinge is made where the
object spawns, about *Axis* through *Anchor* (local space), hanging from *Connected Body* or the world. Optional
limits, *Friction* (how stiffly it swings) and *Spring* back to *Rest Angle*. *Unbreakable*, or it tears off at
*Break Force* / *Break Torque* and becomes a loose object (`IsBroken`, `Broke`). The server simulates it. Set the
Carryable's *Turn Strength* to 0 so the grip doesn't fight the hinge.

## Reading it

| | |
|---|---|
| `Grabber.Holding`, `IsHolding`, `HoldingChanged` | What a player holds, on every machine. Put weapons away from here. |
| `Grabber.Hovered` | Owner: what `TryGrab` would take now (for a crosshair). |
| `Grabber.Load` | Their share of the weight against their grip force: under 1 they lift it, 1+ it drags. |
| `Grabber.Strength`, `Reach`, `MaxForce` | Read from the stats. |
| `Carryable.IsHeld`, `HolderCount`, `HeldChanged` | Synced. `Grabbed` / `Released` events on the server. |
| `IHoldable.IsHeld` | For code that shouldn't depend on Heft's types (a delivery zone that only takes dropped items). |

Server only: `Carryable.ReleaseAll()`. Despawning a held object lets everyone go on every machine.

## How a grip works

Each grip pulls its grab point toward the holder's hold point (their aim, *Hold Distance* ahead) with acceleration
`error x Stiffness - relative velocity x Damping`, adds its share of holding the object up against gravity, and caps
the force at the holder's max force. The force is applied at the grab point, so things swing and tip naturally. The
first holder also turns the object with them, as far as they're strong enough to. If a grab point stays more than
*Break Distance* from where it should be for *Slip Time*, the grip slips. Holders don't collide with what they hold.

The owner sends their hold point and its velocity; the server extrapolates it a little for lag. Holders on clients
see their object one round trip late, which reads as weight.
