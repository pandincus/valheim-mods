# Changelog

## 0.2.0 — 2026-08-22

**You can now craft with fish of different sizes.** Vanilla will not let you: its
requirement check looks at your biggest single-quality stack instead of your
total, so two trollfish of different sizes cannot brew a Troll Endurance mead
that needs two — even though the ingredient list shows 2 of 2 and looks perfectly
happy. The Craft button just sits there greyed out. This release lifts that,
for the recipes the mod already handles.

- A craft that spends fish of several sizes is paid out on their average, rounded
  down. Two trollfish, one quality-1 and one quality-2, now brew 2 mead bases
  instead of being impossible.
- `SmallestFirst` now genuinely saves your best catches. Before, a two-fish craft
  with one small fish and three big ones would pass over the small one and spend
  two big ones, because it needed a single size that could cover the whole craft.
  It now spends the small fish and only one big one.
- New `AllowMixedQualities` setting, on by default. Turn it off to keep vanilla's
  rule about matching sizes and let the mod change only the payout.

Nothing changes for a craft where every fish is the same size — the payout is
identical to 0.1.0. `ExcludedRecipes` and `IncludeMeadRecipes` still hand a
recipe back to vanilla, mixed-size crafting included.

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
