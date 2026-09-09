# Changelog

## 0.1.1 — 2026-09-09

**Fixes for Valheim 1.0.**

ChattyBones was way, way broken in Valheim 1.0. Oops.
Fixed so that now all of the breaking changes are working again!

- The Skeletts chat again! (Because of biomes, status effects, and a few other things,
  everything had broken, boooo)
- Biome lines work again. 1.0 changed how the game reports which biome you have
  walked into.
- Status effect lines work again.
- Mead drawn off a fermenter is announced again. 1.0 started identifying what is
  fermenting by a hash rather than by name.
- General improvement: One broken 'hook' (into Valheim's code) can no longer take the whole mod down with it.
  If a future update moves something, now we lose only the reactions that depend on that one thing. Safer!

## 0.1.0 — 2026-09-09

Initial release! Your Dead Raiser Skeletts talk to you now.

- 32 events they react to
- A 'personality' per Skelett, assigned when they're summoned
- They can refer to each other, you, and other players by name (and lots of other things)
- Every line is in a YAML file you can edit while the game is running
- Multiplayer compatible!
- Configurable chattiness, and per-event silencing