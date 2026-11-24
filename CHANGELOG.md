# Changelog

## [0.2.0] - 2026-11-24

- `Hinged`: hangs a networked rigidbody on a server-simulated hinge (to the world where it spawns, or to a body).
  Limits, friction, an optional spring back to a rest angle, and a break force and torque, or unbreakable. `IsBroken`
  and `Broke` on every machine, `Angle` on every machine, `BreakNow()` on the server. `HingeMath.AngleAbout` with tests.

## [0.1.1] - 2026-11-24

- Unity .meta files, so the package loads from a git URL.

## [0.1.0] - 2026-11-24

- First version: `Carryable` (server-simulated grab points pulled by strength-capped springs, several holders,
  slipping, turning with the holder, throwing) and `Grabber` (reach-limited grab ray, hold distance, synced holding,
  Carrying tag, Blocked By tags, `Load`). `IHoldable` for code that only needs "is it held". `CarryMath` with tests.
