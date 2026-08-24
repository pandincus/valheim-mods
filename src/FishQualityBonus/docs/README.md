# Screenshots

Images used by [the mod README](../README.md). Put new ones here.

## To add an image

1. Save the file in this folder. JPG or PNG for screenshots, GIF for a recording.
2. Reference it from `../README.md` using the full URL, not a relative path:

   ```markdown
   ![Short description of the image](https://raw.githubusercontent.com/pandincus/valheim-mods/main/src/FishQualityBonus/docs/YOUR-FILE.jpg)
   ```

3. Commit the image and the README change together.

## Rules

- Images use `raw.githubusercontent.com/...`. Links to documents use
  `github.com/.../blob/main/...` instead. They are not interchangeable: a `blob` URL shows a
  broken image, and a `raw` URL for a document shows plain text.
- A relative path such as `docs/shot.jpg` works on GitHub and is broken on Thunderstore.
  Always use the full URL.
- The URL points at `main`, so a newly added image shows as broken until the branch merges.
  That is expected, not a mistake to debug.
- Keep files small and cropped. Git keeps every version forever.
- Use plain `![alt](url)` markdown. Raw HTML such as `<img width="600">` may not work on
  Thunderstore, so size the file itself.

## Why the full URL is needed

`tools/package.ps1` builds a flat zip containing only `manifest.json`, `icon.png`,
`README.md`, `CHANGELOG.md` and the DLL. This folder is not in it, and Thunderstore renders
the README on its own site, so a relative path has nothing to resolve against.

A side effect worth keeping: because these files are not in the zip, they never ship to
players and the mod download stays small.

This file is never packaged, so relative links in it are fine.
