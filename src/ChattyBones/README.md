# ChattyBones

Summon a skeleton with the Dead Raiser and it just... works, silently, forever.
This mod gives it a mouth.

Your skeletons call out what they are charging at, yelp when something hits them,
thank you when you drop a shield on them, and mutter to themselves when there is
nothing to fight. They also notice each other — congratulating a kill by name,
welcoming a new arrival, and mourning one that falls. Each is assigned a
personality the moment you summon it, so the cowardly one and the boastful one
react to the same greydwarf very differently.

**Status: not released.** It works, and it is not finished. See below.

## What it does

- Reacts to fifteen things: being raised, picking a target, getting hurt, gaining
  a status effect, killing something, dying, being unsummoned, and idling — plus
  the same happening to you, and to the skeleton standing next to it
- A personality per skeleton, assigned at summon and remembered in your save
- A squad of five will not all talk over each other. The group stays quiet for a
  moment after any one of them speaks, an individual waits rather longer, and one
  remark about a thing stops the others repeating it
- Important things interrupt trivial ones, and a death or an arrival gets an
  answer from somebody else in the same breath
- Chattiness settings in the config, editable in-game with ConfigurationManager
  (F1)

## Not yet

- **Other players see nothing.** Everything is decided and drawn on the machine
  that owns a skeleton, so your squad talks on your screen alone. If you both run
  the mod, you each hear your own.
- The lines are a small built-in placeholder. A proper pack, kept in a plain file
  you can edit and swap — so a group playing together can agree on one and all
  hear the same skeletons — is the next job.
- Per-event toggles, so you can switch off just the idle chatter.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Master switch. Off means complete silence. Safe to flip mid-game. |
| `MinGapSeconds` | `2.5` | How long the whole squad stays quiet after any one of them speaks. The main dial for how talkative they are. |
| `SpeakerCooldownSeconds` | `8` | How long one skeleton waits before speaking again. |
| `SquadEchoWindowSeconds` | `6` | How long one remark about a thing stops the others repeating it. |
| `IdleSeconds` | `45` | Roughly how often a skeleton with nothing to do says something anyway. |
| `HurtFraction` | `0.15` | How big a hit has to be before it is worth mentioning, as a share of the victim's health. Lower it if you are well armoured for where you are. |
| `TextHeight` | `0.3` | How far above the head the line sits, in metres. |
| `TextColour` | *(empty)* | Hex code like `#C8FFC8`, or empty for Valheim's usual white. |

There are a few more; ConfigurationManager lists them all with descriptions.
