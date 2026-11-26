# Changelog

## [0.4.0] - 2026-11-26

- Carryable: Loose Linear/Angular Damping while not held, so dropped and thrown things settle.
- Grabber: Throw Mass Floor, and Max Throw Speed default 12 -> 8, so light things aren't thrown so hard.

## [0.3.0] - 2026-11-24

- `Rolling`: wheels for a rigidbody (a cart). Fixed wheels roll almost freely along its forward axis and grip hard
  sideways (Side Grip, Side Friction cap so a hard shove skids, Rolling Resistance, anti-roll); swivelling wheels
  (casters) only roll. Wheel contacts are made frictionless. Wheels spin and casters trail on every machine from how
  it moves. Optional centre of mass. `RollMath` with tests.
- `CargoBed`: a load box that tracks the rigidbodies in it (`Contents`, `Entered`, `Left`, every machine) and brings
  loose ones to the bed's speed so they ride along; held things still count but aren't pushed.
- `Carryable`: *Lift* (1 = lifted as before, 0 = only pushed and pulled along the ground, pushed where it is held),
  *Push Effort* and `LoadMass` (what `Grabber.Load` now measures against), *Turn Yaw Only*.
- Pushing on wheels is light the way it rolls and heavy across: with `Rolling` on it, `Carryable.LoadMassToward(direction)`
  takes the wheels' resistance that way (`Rolling.PushResistance`, `RollMath.PushResistance`: Rolling Resistance along,
  easing to Side Friction across) instead of Push Effort, and `Grabber.Load` uses the way the holder walks (owner) or
  their hold point moves (elsewhere). A holder pushing a cart forward is barely slowed and can sprint; across, they slow
  right down.

## [0.2.0] - 2026-11-24

- `Hinged`: hangs a networked rigidbody on a server-simulated hinge (to the world where it spawns, or to a body).
  Limits, friction, an optional spring back to a rest angle, and a break force and torque, or unbreakable. `IsBroken`
  and `Broke` on every machine, `Angle` on every machine, `BreakNow()` on the server. `HingeMath.AngleAbout` with tests.
- Carrying: an item held by one person is pulled at its centre of mass (no jitter on light items), blending back to the grip point the heavier the load; with several holders each pull stays at its grip.
- Turning what you hold: `Grabber.Rotate(yaw, pitch)` (owner) turns the held item about the holder's up and right axes; the turn is synced in `HoldAim.Rotation`, kept relative to where they look, and reset on each grab. With several holders the first one's turn steers it. The game maps the input (e.g. hold a key and move the mouse).
- `HoldTether`: a rope-like tube from the holder's hand to their grip, on every machine, bending toward the pull, sagging when slack and turning from Slack to Taut colour as the grip nears slipping. `Grabber.Strain` (0..1, synced), `GripPoint`, `HoldPoint`; `CarryMath.Strain` (stretch up to Break Distance, then the slip timer), `Curve`, `TetherControl`, with tests. Optional tether prefab so games can add their own components.

## [0.1.1] - 2026-11-24

- Unity .meta files, so the package loads from a git URL.

## [0.1.0] - 2026-11-24

- First version: `Carryable` (server-simulated grab points pulled by strength-capped springs, several holders,
  slipping, turning with the holder, throwing) and `Grabber` (reach-limited grab ray, hold distance, synced holding,
  Carrying tag, Blocked By tags, `Load`). `IHoldable` for code that only needs "is it held". `CarryMath` with tests.
