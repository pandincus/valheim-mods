# Changelog

## Unreleased

Summoned skeletons talk. They react to being raised, to picking a fight, to
winning or losing one, to being hurt or shielded, and to you doing any of the same
— and they react to each other, by name.

Thirty-two things they notice, across four personalities (cowardly, boastful,
dutiful, veteran) assigned at summon and remembered in the save. They will tell
you what hit them and what it was made of, complain specifically about being on
fire, and rib each other by name when nothing is happening.

They also watch how a fight is going, not just how it ends — a parry, a dodge
that actually turned a blow, and either of you being knocked off balance. Only
the good version of each: a skeleton that praised every raised shield would be
one you stopped listening to.

And they notice the world you are walking through. Sunrise and nightfall,
crossing into a new biome, a raid arriving and then being seen off, something
much larger than a raid turning up, and the moment you finally settle somewhere
with a roof and a fire and nothing hunting you. They will remark on what you pick
up, what you eat, what you brew or cook at a cauldron, and what you are getting
better at.

Lines can also be attached to where the skeleton is standing. Write
`Idle[biome=Swamp]` beside your ordinary `Idle` and those lines are used only in
the Swamp, so a remark about the open sky stops turning up under the trees. Each
personality has its own take on the Swamp and the Mistlands out of the box.

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

How talkative they are is configurable, and it is two words rather than seven
numbers. `ChatterFrequency` and `IdleChatter` each run from Never to Always, and
they are kept apart because they are different complaints — a squad that talks
over a fight and a squad that will not stop musing on the weather want opposite
answers. Between them they can also say "only mutter to yourselves" and "only
speak when something happens", neither of which the master switch can express.

Picking one writes the numbers it stands for, so the settings file never shows a
value that is not in force; move one of those numbers yourself and the dial says
Custom. Everything else is marked advanced and stays out of the way until you tick
the box that asks for it, and `SilencedEvents` switches off events by name for
anyone who wants one thing quiet rather than all of it.

Underneath, the squad stays quiet for a moment after any one of them speaks, an
individual waits rather longer, and one remark about a thing stops the others
repeating it — five skeletons all reacting to the same greydwarf would be an
unreadable wall of text otherwise. Important things interrupt trivial ones, and a
death or a new arrival gets an answer from somebody else in the same breath.

In a shared world everybody with the mod sees everybody's skeletons talk, and
there is nothing to install on a server to get it. A skeleton records what it
said in the same place the game already keeps its name and its health, and the
other players read it from there — so a player without the mod sees nothing at
all and nothing goes wrong for them. What travels is which line rather than the
line itself, so you and your friends do not need matching files: your skeletons
say your lines on your screen and theirs on theirs, and if the files do match you
both read the same words. `HearOthers` in the config switches it off for your
screen.

Which is also why they can now greet each other. Walk up to somebody and their
squad says hail to you by name, and yours says hail to them — the only thing in
here that does nothing at all when you play on your own. Names travel as
identities rather than as text, so everybody's own language and everybody's own
name filtering are applied on their own machine.

And they will keep talking to whoever is standing there. `{ally}` names another
player and `{companion}` names another of your skeletons, and both can go in any
line at all rather than only in the ones about arriving or about each other — so
"So, how are things, {ally}?" is an ordinary idle line that simply never comes up
until somebody is actually there to ask.
