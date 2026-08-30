# Changelog

## Unreleased

Summoned skeletons talk. They react to being raised, to picking a fight, to
winning or losing one, to being hurt or shielded, and to you doing any of the same
— and they react to each other, by name.

Twenty-one things they notice, across four personalities (cowardly, boastful,
dutiful, veteran) assigned at summon and remembered in the save. They will tell
you what hit them and what it was made of, complain specifically about being on
fire, and rib each other by name when nothing is happening.

They also watch how a fight is going, not just how it ends — a parry, a dodge
that actually turned a blow, and either of you being knocked off balance. Only
the good version of each: a skeleton that praised every raised shield would be
one you stopped listening to.

Every line lives in `BepInEx/config/ChattyBones.lines.yaml`, written for you on
first run and yours to rewrite. Edit it while the game is running and the change
takes effect immediately — no restart, no leaving the world. A pack that will not
parse leaves the skeletons saying what they said a minute ago, and puts the
reason and a line number in the log. A copy of what the mod shipped with is kept
alongside and refreshed each launch, so there is always something known-good to
compare against. The set of lines that comes with it is still thin; the point of
this release is that replacing it is a text editor away.

Lines are colored by what happened rather than by who is speaking, so a death
cry reads as bad news before you have read a word of it. The palette is part of
the pack. `TextColor` in the config still overrides the lot if you would rather
have one color and no argument.

How talkative they are is configurable. The squad stays quiet for a moment after
any one of them speaks, an individual waits rather longer, and one remark about a
thing stops the others repeating it — five skeletons all reacting to the same
greydwarf would be an unreadable wall of text otherwise. Important things
interrupt trivial ones, and a death or a new arrival gets an answer from somebody
else in the same breath.

Not yet: other players see nothing. Everything is decided and drawn on the
machine that owns a skeleton, so your squad talks on your screen alone. The
groundwork for mirroring is written but nothing reads it yet.
