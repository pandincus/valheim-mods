# PassThroughSkeletts

![Look! I'm standing inside one of my Skeletts!](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/PassThroughSkeletts/docs/passthroughskeletts.jpg)

Have you ever had an impressive squad of Skeletts, but you get pushed around by them, maybe even trapped in a dangerous or sticky situation? Pushed out of a dungeon accidentally? Caught between them and a Seeker? Maybe it wasn't even your Skeletts, but a friend's!

I got tired of this happening, so this mod just does one thing: it makes it so you can pass through your Skeletts. That's it! They still collide with the ground, enemies, buildings, etc. But now you'll not be stuck in any tight spaces!

## Some More Details

- **This only affects players and summoned Skeletts.** As stated above, Skeletts still collide properly with the ground, walls, doors, enemies and everything else. I'm actively testing this, but do report if you notice any odd behavior -- they really shouldn't fall through the floor or walls, enemies etc.
- **Hover commands still work.** Cursor functions still work (e.g. renaming, petting). Do let me know if there's a mod that adds MORE hover-commands that this conflicts with.
- **Multiplayer-compatible!** Once the mod is installed, it'll apply to any Skeletts, even those summoned by other players.

## How does this work in multiplayer for other players?

Valheim's physics runs on each player's own game, so this works fine, even if other players don't have the mod. They'll just see you walking through Skeletts, but won't be able to do the same on their game.

Essentially:
- If you have it installed, you pass through ALL Skeletts. Another player without the mod will see it the same way you do, as you walking through them.
- If the other player doesn't have the mod, they will still get blocked and pushed by Skeletts, including yours.
- One very minor quirk: a Skelett run by that friend's (without the mod) game can get nudged slightly if you stand inside it, because their game is computing that the Skelett needs to move out of your way. This won't happen for *your* Skeletts.

I've only tested with two players so far (my wife and I), so shout out if something gets weird at 3+.

## Config

When you first run the mod, the config file `BepInEx/config/pandincus.passthroughskeletts.cfg` will appear. It can be edited in the file, but I recommend the BepInEx ConfigurationManager (F1 key). Changes apply immediately.

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | `true` | Master switch. `false` is completely vanilla: Skeletts are solid again. You can flip this back and forth in-game, even while inside of a Skelett. Once colliding works again they'll just get shoved. |

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

## Developing

Build instructions, tooling and tests are in the
[repo README](https://github.com/pandincus/valheim-mods/blob/main/README.md). As with my other mods, I have used Claude to assist in code generation. The terrible icon artwork is mine alone ;-)
