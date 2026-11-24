# Changelog

## [0.2.0] - 2026-11-24

- `Hinged`: hangs a networked rigidbody on a server-simulated hinge (to the world where it spawns, or to a body).
  Limits, friction, an optional spring back to a rest angle, and a break force and torque, or unbreakable. `IsBroken`
  and `Broke` on every machine, `Angle` on every machine, `BreakNow()` on the server. `HingeMath.AngleAbout` with tests.
- Carrying: an item held by one person is pulled at its centre of mass (no jitter on light items), blending back to the grip point the heavier the load; with several holders each pull stays at its grip.
- Turning what you hold: `Grabber.Rotate(yaw, pitch)` (owner) turns the held item about the holder's up and right axes; the turn is synced in `HoldAim.Rotation`, kept relative to where they look, and reset on each grab. With several holders the first one's turn steers it. The game maps the input (e.g. hold a key and move the mouse).

## [0.1.1] - 2026-11-24

- Unity .meta files, so the package loads from a git URL.

## [0.1.0] - 2026-11-24

- First version: `Carryable` (server-simulated grab points pulled by strength-capped springs, several holders,
  slipping, turning with the holder, throwing) and `Grabber` (reach-limited grab ray, hold distance, synced holding,
  Carrying tag, Blocked By tags, `Load`). `IHoldable` for code that only needs "is it held". `CarryMath` with tests.
