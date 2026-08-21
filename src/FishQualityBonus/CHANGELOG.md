# Changelog

## 0.1.0 — 2026-08-21

Initial release.

- Recipes that consume a whole fish pay out more for a higher-quality fish,
  using the same formula the game applies to Fish (raw).
- Each fish species' flat +0/+1/+2 bonus is read from the game at load and
  applied too, so an anglerfish is worth more than a perch. Can be turned off
  with `UseSpeciesBonus`.
- Which fish gets spent is now a deliberate choice — smallest or largest first —
  instead of whichever one you happened to pick up first.
- The crafting panel shows the real total before you craft.
- Everything is configurable, including a master switch that restores vanilla
  behaviour completely.

In vanilla this affects Fish 'n' Bread and three mead bases:
MeadBaseBugRepellent, MeadBaseStrength and MeadBaseSwimmer.
