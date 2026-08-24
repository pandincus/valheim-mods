# Screenshots for the mod README

Drop screenshots (`.png`) or recordings (`.gif`) in this folder and link them from
[`../README.md`](../README.md).

## Link them with absolute URLs, not relative paths

This is the one rule that matters, and it is easy to get wrong because the broken version
looks fine on GitHub.

```markdown
![Two differently-sized trollfish brewing a mead](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/FishQualityBonus/docs/mixed-qualities.png)
```

A relative path like `docs/mixed-qualities.png` renders correctly in the GitHub repo view
and is a broken image on Thunderstore, for two separate reasons:

1. The file is not in the package. `tools/package.ps1` builds a **flat** zip from a fixed
   list — `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`, `<Mod>.dll` — and this
   folder is not on it.
2. Even if it were, Thunderstore renders the README on its own site, so a relative path
   resolves against `thunderstore.io` rather than against the package.

An absolute `raw.githubusercontent.com` URL works in both places.

## Why that is a good thing

Because these files are not in the zip, they never ship to players. The mod download stays
a few KB rather than carrying screenshots that every installer has to fetch.

## Worth knowing

- **A `main` URL is dead until the branch merges.** Images added on a feature branch will
  show as broken in the pull request and start working once it lands. That is fine for
  release — Thunderstore only publishes on a GitHub Release — but do not spend time
  debugging it.
- **Git keeps blobs forever.** A file committed here is in every future clone even after
  it is deleted, so crop and compress before committing. Screenshots should be tens of KB;
  a GIF should be a few seconds, not a whole crafting session.
- **Use plain `![alt](url)` markdown.** Whether Thunderstore's renderer allows raw HTML
  such as `<img width="600">` is unconfirmed, so size the image file itself rather than
  relying on an attribute to constrain it.
- `.gitattributes` marks `*.png`, `*.jpg`, `*.jpeg` and `*.gif` as binary, so git will not
  try to normalise line endings inside them.

## Shots worth having

- The crafting panel showing the boosted output count on a high-quality fish.
- The mixed-quality case: the ingredient list reading 2 of 2 with the Craft button dead in
  vanilla, next to the same inventory crafting successfully with the mod on. That one is
  hard to convey in prose and is the clearest demonstration of what 0.2.0 added.
