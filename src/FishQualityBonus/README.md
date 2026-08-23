# FishQualityBonus

Hello! I created this mod to fix a nitpick that felt, in my opinion, like an oversight, which
is simply this: fishing recipes should reward you for the quality of the fish.

In Valheim, [Raw Fish](https://valheim.fandom.com/wiki/Raw_Fish), prepared from
an actual caught fish, are multiplied in value based on the quality of the fish caught.
If I catch a big, quality-5 tuna, I can actually prepare 14 "raw fish" from that single tuna.

However, this does not apply to any recipes that consume a **whole fish**, which in my opinion
completely wastes the higher quality fish in those recipes.

Since recipes like Fish 'n' Bread, currently one of the best stamina foods in the game, take
a whole fish, the player is effectively penalized for using the better quality anglerfish
in that recipe.

This mod adjusts it by applying the same raw fish computation formula to other whole-fish
recipes, as well!

## Behavior

    output = amount + (fishQuality - 1) * amount * BonusPerQualityLevel + speciesBonus

where `fishQuality` is the average size of the fish the craft spends, rounded down —
which is just the fish's own quality whenever they are all the same size.

`BonusPerQualityLevel` is configurable, but defaulted to `3` to match the existing
values used in computation of Fish (raw) (in the base game).

`speciesBonus` is the game's own +0/+1/+2 fish tiering based on the species of fish.
See [Raw Fish](https://valheim.fandom.com/wiki/Raw_Fish), and note the flat bonus
based on the type. We access this data from `m_extraAmountOnlyOneIngredient` on
the Fish (raw) recipe and treat the values as a property of the species.
Essentially, this means that one anglerfish makes as many Fish 'n' Bread as it would raw fish.
Note that this is a flat bonus that doesn't scale with quality, just like vanilla.
So, even a little quality-1 anglerfish gets you 3 Fish 'n' Bread with this enabled.
If you would prefer the bonus from this mod more related to how big the fish was, you can
turn `UseSpeciesBonus` to false in the config and the species bonus will not apply.

This mod also makes ingredient selection deliberate: vanilla spends whichever fish you
picked up first, no matter where it sits in your inventory, which is effectively
random. This mod picks a quality on purpose (e.g. largest first or smallest first, configurable)
and spends that.

### Fish of different sizes

Vanilla will not let you craft with mismatched fish. Its requirement check looks at your
biggest single-quality stack rather than your total, so two trollfish of different sizes
cannot brew a Troll Endurance mead that needs two — and the ingredient list still shows
2 of 2 in white while the Craft button sits greyed out. Every other crafting material has
only one quality level, so the rule is invisible everywhere except on fish.

This mod lifts that for the recipes it already handles, and prices the craft on the
**average** size of the fish you spent, rounded down. One quality-1 and one quality-2
trollfish give you 2 mead bases; two quality-1 give 1, and two quality-2 give 4. Set
`AllowMixedQualities` to `false` to keep vanilla's rule and change only the payout.

This is also what makes `SmallestFirst` mean what it says. A two-fish craft with one small
fish and three big ones now spends the small one and a single big one, rather than passing
over the small fish because it could not cover the craft alone.

This **should** be safe to pick up new recipes added via updates and mods, because we're
loading recipes at runtime and matching via `ItemType.Fish`. A recipe qualifies only if all of these hold:

1. it is not flagged `m_requireOnlyOneIngredient` (currently only `FishRaw` uses this),
2. its output is not equipment, by the game's own `IsEquipable()` check,
3. it requires exactly **one** fish species; scaling when more than one is involved feels messy,
4. it passes the `IncludeMeadRecipes` and `ExcludedRecipes` configurable settings.

In vanilla and with default config, that means this mod applies to **Fish 'n' Bread** plus three mead bases: **MeadBaseBugRepellent**, **MeadBaseStrength**, and **MeadBaseSwimmer**.

## Config

Written to `BepInEx/config/pandincus.fishqualitybonus.cfg` on first run, and
editable in-game with ConfigurationManager (F1). Changes apply immediately.

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | `true` | Master switch; `false` restores vanilla behaviour entirely. |
| `Bonus.BonusPerQualityLevel` | `3` | Whole-number multiplier, 1-10. At `3` a 1-item recipe yields 1/4/7/10/13 for quality 1/2/3/4/5 — matching the game's own Fish (raw) tuning. At `1`, 1/2/3/4/5. |
| `Bonus.FishToSpend` | `SmallestFirst` | `SmallestFirst` spends the lowest-quality fish first,  `LargestFirst` always spends your best fish first. |
| `Bonus.AllowMixedQualities` | `true` | Let a craft draw on several sizes of the same fish at once, which vanilla refuses, and pay out on their average. `false` keeps vanilla's matching-sizes rule and changes only the payout. Applies to the same recipes the bonus does. |
| `Bonus.UseSpeciesBonus` | `true` | Grant each species' flat +0/+1/+2 tier as well, read from the Fish (raw) recipe at load. Anglerfish is +2, so Fish 'n' Bread gains a flat 2. Not scaled by quality. |
| `Bonus.IncludeMeadRecipes` | `true` | Whether mead bases brewed at the mead cauldron get the bonus too, if they use a whole fish as an ingredient. `false` effectively restricts the mod to food. |
| `Bonus.ExcludedRecipes` | *(empty)* | Comma-separated output prefab names to skip individually, e.g. `MeadBaseStrength,MeadBaseSwimmer`. |
| `Diagnostics.LogRecipeReport` | `false` | Dumps every fish and fish-consuming recipe to the log, annotated with whether the bonus applies and why. Development aid; off by default, and also requires `Enabled`. |

## Multiplayer

This is a client-side mod. The crafting result is computed locally and synced like any other
inventory change, so the server does not need the mod. But everyone who crafts and wants this
behavior needs it installed, or identical ingredients will give different results for
different players.

## Developing

Build instructions, tooling and tests are in the [repo README](../../README.md).

This is my first mod! This was built through a combination of code-diving, wiki reading, and usage
of Claude Code. Note that even though I used Claude Code to develop the code changes, I've reviewed
each line of code, applied manual refactors, and have heavily rewritten code comments.

Please feel free to offer feedback and I would happily accept community contributions.

## Future Work

This isn't perfect. If we get recipes in the future that involve more than one whole fish, I'll
need to revisit this mod to eliminate the single-ingredient requirement. Further, I don't love that
we get the fish-recipe-output multiplied, but don't also consume more of the other ingredients. Using
Fish 'n' Bread as an example, additional bread dough should probably also be consumed, even though we've
got 'more' fish to go around. The latter issue is one I will consider addressing in an upcoming release.

With Valheim 1.0 coming out soon, I'm not sure what will change that might impact this mod. Perhaps
the devs have already fixed this issue! Or perhaps they'll add more fishing recipes and we'll want to test
that this continues to work. But since I wrote this mod to use it, I will revisit and update in the near future.

See [CHANGELOG.md](CHANGELOG.md) for release notes.
