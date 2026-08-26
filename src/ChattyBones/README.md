# ChattyBones

Summon a skeleton with the Dead Raiser and it just... works, silently, forever.
This mod gives it a mouth.

Your skeletons call out what they are charging at, yelp when something hits
them, thank you when you drop a shield on them, and mutter to themselves when
there is nothing to fight. Each one is assigned a personality the moment you
summon it, so the cowardly one and the boastful one react to the same greydwarf
very differently.

**Status: not released.** This is scaffolding. The plugin loads and logs that it
loaded; nothing talks yet.

## Planned

- Reacts to acquiring a target, taking damage, gaining a status effect, getting
  a kill, dying, and being idle
- A personality per skeleton, assigned at summon and stable for as long as it
  lives
- Every line is yours: the pack is a plain JSON file you can edit, and it
  reloads while the game is running
- Per-event toggles and chattiness settings in the config, editable in-game with
  ConfigurationManager (F1)
- A squad of five skeletons will not all talk over each other

## Settings

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Master switch. Off means complete silence. Safe to flip mid-game. |

More arrive as the mod does.
