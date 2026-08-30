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

- Reacts to thirty things: being raised, picking a target, getting hurt, gaining
  a status effect, catching fire or poison, killing something, dying, being
  unsummoned, and idling — plus the same happening to you, and to the skeleton
  standing next to it
- Notices how a fight is going rather than only how it ends: a parry, a dodge
  that turned a blow, and either of you being knocked off balance
- And notices the world around it: sunrise and nightfall, crossing into a new
  biome, a raid arriving and being seen off, settling in somewhere safe, and
  what you pick up, eat and get better at
- Lines can name the weapon that hit them, what kind it was, the damage type,
  the status effect, the biome, what you just picked up or ate, the skill that
  went up, and the skeleton standing next to them
- A personality per skeleton, assigned at summon and remembered in your save
- A squad of five will not all talk over each other. The group stays quiet for a
  moment after any one of them speaks, an individual waits rather longer, and one
  remark about a thing stops the others repeating it
- Important things interrupt trivial ones, and a death or an arrival gets an
  answer from somebody else in the same breath
- Every line lives in a plain file you can edit, swap and hand to somebody else
- Chattiness settings in the config, editable in-game with ConfigurationManager
  (F1)

## The line pack

Everything the skeletons say is in one file, and it is yours to rewrite:

```
BepInEx/config/ChattyBones.lines.yaml
```

It is written for you the first time you run the game, and never touched again.
**Edit it and save it while the game is running** — the change takes effect on
the spot, with no restart and without leaving the world. If you break something,
your skeletons keep using the last version that worked and the reason, with a
line number, goes to the BepInEx log.

The file explains itself: which events exist, which tokens each one can fill in,
and how the personalities and the colors work. A second file next to it,
`ChattyBones.lines.default.yaml`, is refreshed on every launch with exactly what
the mod shipped with — so there is always a known-good copy to compare against or
start over from.

The point of a file rather than a config screen is that a pack is something you
can hand to somebody. A group playing together can agree on one, drop it in, and
hear the same skeletons say the same things.

## Not yet

- **Other players see nothing.** Everything is decided and drawn on the machine
  that owns a skeleton, so your squad talks on your screen alone. If you both run
  the mod, you each hear your own — which is also why a shared pack is currently
  a matter of you both installing the same file.
- The lines that come with it are thin. The machinery is finished; writing a
  proper pack on top of it is the next job.
- Per-event toggles, so you can switch off just the idle chatter. For now,
  deleting an event from the pack does the same thing.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Master switch. Off means complete silence. Safe to flip mid-game. |
| `MinGapSeconds` | `2.5` | How long the whole squad stays quiet after any one of them speaks. The main dial for how talkative they are. |
| `SpeakerCooldownSeconds` | `8` | How long one skeleton waits before speaking again. |
| `SquadEchoWindowSeconds` | `6` | How long one remark about a thing stops the others repeating it. |
| `IdleSeconds` | `45` | Roughly how often a skeleton with nothing to do says something anyway. |
| `HurtFraction` | `0.15` | How big a hit has to be before it is worth mentioning, as a share of the victim's health. Lower it if you are well armored for where you are. |
| `TextHeight` | `0.3` | How far above the head the line sits, in meters. |
| `TextColor` | *(empty)* | One color for everything, as a hex code like `#C8FFC8`. Empty — the default — lets the pack color by event instead. |

There are a few more; ConfigurationManager lists them all with descriptions.

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [YamlDotNet](https://thunderstore.io/c/valheim/p/ValheimModding/YamlDotNet/), for
  reading the line pack. A mod manager installs it for you.
