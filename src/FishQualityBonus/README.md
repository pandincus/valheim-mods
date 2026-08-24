# FishQualityBonus

In base game, recipes that consume a **whole fish** (Fish 'n' Bread, the fish mead bases)
output the same amount whether you spent a quality-1 perch or a quality-4 anglerfish. This is
totally inconsistent with how Raw Fish behaves.

This mod adjusts the behavior in the following ways: 

- **Bigger fish make more.** Output scales with the fish's quality, using the same formula
  the game already applies to Raw Fish. For example, using a quality-5 fish makes 13 instead of 1.
- **Rarer fish make more.** Each species' flat +0/+1/+2 tier applies on top, read out of
  the game at load rather than hard-coded. Fish 'n' Bread takes a +2 species, so that
  quality-5 craft actually lands at 15 meals.
- **Fish of different sizes can be combined.** The base game refuses to craft when your
  fish qualities don't match (e.g. two trollfish of different sizes can't brew a mead that needs two,
  even though the ingredient list says you have enough). This fixes that, and the output is based on
  the average size of what you spent.
- **Choose which fish size to prioritize.** Configuration options include *which* fish gets consumed first
  (e.g. prioritize higher or lower quality in your inventory when crafting).

Most of the above parameters are configurable, including a master switch that restores vanilla exactly.

![A quality-2 anglerfish and two bread dough producing Uncooked Fish 'n' Bread x6](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/FishQualityBonus/docs/fish-quality-bonus-anglerfish.jpg)


## Why I made this

Hello! I created this mod to fix a nitpick that felt, in my opinion, like an oversight, which
is simply this: fishing recipes should reward you for the quality of the fish.

In Valheim, [Raw Fish](https://valheim.fandom.com/wiki/Raw_Fish), prepared from
an actual caught fish, are multiplied in value based on the quality of the fish caught.
If I catch a big, quality-5 tuna, I can actually prepare 14 "raw fish" from that single tuna.

However, this does not apply to any recipes that consume a **whole fish**, which in my opinion
completely wastes the higher quality fish in those recipes.

Since recipes like Fish 'n' Bread, currently one of the best stamina foods in the game, take
a whole fish, the player is effectively penalized for using the better quality anglerfish
in that recipe. (Making the joy of actually hooking a high-quality fish not worth the stamina
spent to reel it in)

This mod adjusts it by applying the same raw fish computation formula to other whole-fish
recipes, as well! And while I was in the code, I decided to fix the mixed-quality-crafting issue as well.

## Behavior

What a recipe that normally makes 1 gives you, at the default settings:

| Fish quality | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| A +0 species (perch, pike, tetra, trollfish) | 1 | 4 | 7 | 10 | 13 |
| A +2 species, which is what Fish 'n' Bread takes | 3 | 6 | 9 | 12 | 15 |

The formula behind that:

    sizeBonus = floor( (averageQuality - 1) * amount * BonusPerQualityLevel )
    output    = amount + sizeBonus + speciesBonus

`amount` is what the recipe normally makes. `averageQuality` is the mean quality of the fish
spent, which is just the fish's own quality when they're all the same size.

`BonusPerQualityLevel` is configurable and defaults to `3`, matching the game's own Raw Fish behavior.

`speciesBonus` is the game's +0/+1/+2 tier for the species — see [Raw Fish](https://valheim.fandom.com/wiki/Raw_Fish).
Like vanilla, it's flat: even a small quality-1 anglerfish still earns its +2. Also configurable.

### Which fish gets eaten

Vanilla spends whichever fish you picked up first, and it doesn't even matter where it sits in your inventory ( effectively random). This mod picks on purpose, smallest or largest first, via `FishToSpend`.

### Fish of different sizes

Vanilla won't let you craft when your fish don't match. It checks your biggest single-size
stack instead of your total, so two trollfish of different sizes can't brew a Troll Endurance
mead that needs two. Confusingly, the ingredient list shows 2 of 2 in white, and the Craft button
stays grayed out with nothing explaining why. This is really only a problem that affects fish recipes,
as every other crafting material has only one quality level.

This mod allows the craft and pays out on the average of what you spent. Two trollfish in a
mead base that normally makes 1:

| Fish spent | Output |
|---|---|
| two quality-1 | 1 |
| one quality-1, one quality-2 | 2 |
| two quality-2 | 4 |

Set `AllowMixedQualities` to `false` to keep vanilla's rules and disallow the craft in these mixed cases.

![One quality-2 and one quality-1 trollfish brewing Mead Base: Troll Endurance x2](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/FishQualityBonus/docs/mixed-fish-qualities.jpg)


### Which recipes qualify

This **should** be safe to pick up new recipes added via updates and mods, because the mod
loads recipes at runtime and matches via `ItemType.Fish`. A recipe qualifies only if all of these are met:

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

![The mod's settings listed in ConfigurationManager in-game](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/FishQualityBonus/docs/config-options.jpg)

## Multiplayer

This is a client-side mod. The crafting result is computed locally and synced like any other
inventory change, so the server does not need the mod. But everyone who crafts and wants this
behavior needs it installed, or identical ingredients will give different results for
different players.

## Developing

Build instructions, tooling and tests are in the
[repo README](https://github.com/pandincus/valheim-mods/blob/main/README.md).

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

See the
[CHANGELOG](https://github.com/pandincus/valheim-mods/blob/main/src/FishQualityBonus/CHANGELOG.md)
for release notes. On Thunderstore it is also the Changelog tab on this package's page.
