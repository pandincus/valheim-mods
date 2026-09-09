# ChattyBones

Tired of your summoned Skeletts being silent companions? Now they can chat with you, with ChattyBones!

![An example of them chattering after having killed a Greydwarf](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/ChattyBones/docs/chattybones-squad.jpg)

## What the heck does this do?

This mod is truly a silly thing, and provides no mechanical benefit (that I can think of, at least).

It enables your Skeletts to react to things happening around them (combat, getting hurt, catching fire, dying, transitioning to new biomes, looting, eating, cooking, etc.). There are **32** event-types in total that I support today!

They can reference things in the world (you, themselves, each other, the enemies, other players) by name, and when you summon a Skelett, it will randomly get assigned a 'personality' to give them a little unique character.

The lines are totally customizable via a YML file, and there's configuration options for how frequently they speak and several other things, too.

## Does this work in multiplayer?

It does! But only if all players who want to see the dialogue have the mod installed. References to lines are sent over the wire (packed into an int so it should be very efficient), so if all players have the same lines and version of the mod, they should see the same dialogue play out. It should also work back and forth between different skeletons summoned by different players! (Though I haven't tested that much)

## Known Bugs

There's a lot of little bugs, but none of them stop the mod from working.

* The Skeletts sometimes react to things based on where the player is, not necessarily where they are, for a variety of reasons. I might not be able to do anything about this one, and you likely won't notice it unless the Skeletts get separated from you. (e.g. you go to your base and they comment on how nice and cozy it is, but they're still outside)
* Not all of the 'tokens' (the references they make to things in the game) work all the time, e.g. I don't have an easy way for the Skeletts to comment about their own weapons, but they can comment on the players' easily. It isn't a problem; if you write a line with a token that isn't available, that line simply won't be picked to be spoken.
* The code could use a nice, big ole' pass through and I'm pretty sure we could make the mod a bit more efficient. Shout at me if you notice any performance issues. I haven't in my testing, though.

## Localization?

Because all the lines are in a YML file, we can definitely localize! Sorry, all the lines are just in English right now. I haven't yet built the support for multiple line packs of different languages to be stored in the mod at the same time.  But if you are interested in localizing, let me know! I'll build that support and then we can ship this with multiple languages, too.

All of the 'things' referenced by the Skeletts (biomes, items, etc) are all coming straight from Valheim, so the built-in localization SHOULD take care of that.

## How to Edit Lines

In your BepInEx config folder for this mod, you'll find a file like this:

```
BepInEx/config/ChattyBones.lines.yaml
```

Go ahead and edit it and save it **while the game is running**! The change takes effect with a hot reload, so no restart of the game is needed. If you break the formatting of the file, you should see a warning in the console, but your Skeletts keep using the last version that worked.

The file has some comments that should explain how it is organized. A second file next to it,
`ChattyBones.lines.default.yaml`, is delivered with new updates to the mod, so if you ever see something not working you can compare against it — or copy it over your `ChattyBones.lines.yaml` to start fresh, though that will replace any edits you have made.

## Config

When you first run the mod, the config file `BepInEx/config/pandincus.chattybones.cfg` will appear. Can be edited in the file, but I recommend the BepInEx ConfigurationManager (F1 key). Changes apply immediately.

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | `true` | Master switch. `false` is complete silence; they say nothing. |
| `Chatter.ChatterFrequency` | `Often` | How much they react to things happening. |
| `Chatter.IdleChatter` | `Sometimes` | How much they idly mutter when nothing is going on. |
| `Chatter.SilencedEvents` | *(empty)* | Events to switch off, comma-separated, e.g. `Weather, PlayerAte`. The names are listed at the top of the line pack. |
| `Appearance.BubbleStyle` | `FloatingText` | Text that follows the head, or `DialoguePanel` for the Hugin box — easier to read, more obtrusive. |
| `Multiplayer.HearOthers` | `true` | Whether other players' Skeletts running the mod talk on your screen. |

There's a bunch more specific dials for controlling individual timing, but those are hidden under **Advanced**. Tick that checkbox and you'll see those show up.

## Why I made this

Truly, I'm not sure. I just liked the idea of your little guys having things to say while you were out braving the ash lands.

## Future Work

This is just a 0.1.0 right now, and it is very basic. I expect to come:
* more events to react to
* more unique dialogue based on the environment around them (e.g. a comfy base vs. a rickety one)
* maybe fixing some of the token bugs
* better localization support (e.g. multiple languages packed with the mod)
* more banter back-and-forth between the skeletons
* maybe something more fun than just straight-up 'personality' types. Unique, named Skeletts? Maybe!

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [YamlDotNet](https://thunderstore.io/c/valheim/p/ValheimModding/YamlDotNet/)

## Developing

Build instructions, tooling and tests are in the
[repo README](https://github.com/pandincus/valheim-mods/blob/main/README.md).

This was built through a combination of code-diving, wiki reading, and usage of Claude Code. Though Claude has helped me generate the code, I reviewed a significant portion (but admittedly, not all) of the generated code.

Please feel free to offer feedback and I would happily accept community contributions.

See the
[CHANGELOG](https://github.com/pandincus/valheim-mods/blob/main/src/ChattyBones/CHANGELOG.md)
for release notes. On Thunderstore it is also the Changelog tab on this package's page.
