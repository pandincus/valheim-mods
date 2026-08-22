# Valheim Mods

BepInEx mods for [Valheim](https://www.valheimgame.com/), built with HarmonyX.
One repository, one folder per mod, shared tooling at the root.

## Mods

| Mod | What it does |
|---|---|
| [FishQualityBonus](src/FishQualityBonus/) | Recipes that consume a whole fish — Fish 'n' Bread and the fish mead bases — pay out more when you spend a bigger fish. |

## Layout

```
ValheimMods.sln              every mod and its tests
Directory.Build.props        game and BepInEx paths, shared by all projects
src/<ModName>/               one folder per mod, with its own README
tests/<ModName>.Tests/       xUnit tests for that mod
tools/                       shared scripts (deploy, log watcher, decompiler)
.vscode/                     editor tasks
```

## Building

Needs the .NET SDK (8 or newer; developed against 10). No Visual Studio required —
the `Microsoft.NETFramework.ReferenceAssemblies` package supplies what net472
needs.

```
dotnet build ValheimMods.sln -c Release
```

The build copies each mod's DLL straight into the BepInEx plugins folder. Paths
live in `Directory.Build.props` and can be overridden per machine:

```
dotnet build ValheimMods.sln -c Release -p:ValheimDir="D:\Games\Valheim"
```

Pass `-p:Deploy=false` to build without copying — useful while the game is
running.

> **A running Valheim will not pick up a new build.** BepInEx reads plugin DLLs
> into memory at startup, so the copy silently succeeds while the game keeps
> using the old code. Quit to desktop and relaunch.

## Tests

```
dotnet test ValheimMods.sln
```

Mods target net472 and reference Unity and BepInEx, none of which load outside
the game. So each mod keeps its decision logic in a file with **no** Unity,
BepInEx or game types in it, and the test project compiles that file directly via
a linked `<Compile Include>` rather than referencing the mod across the framework
gap.

That constraint is deliberate: if the pure file ever grows a Unity dependency,
the test project stops building.

What tests can't cover, and never will: whether Harmony attaches to the right
methods, whether data scraped from the game is correct, and anything touching the
UI. Those need an in-game run.

## Working in VS Code

Open the repo root (the folder containing `ValheimMods.sln`). With the C#
extension installed:

- **Ctrl+Shift+B** runs the tests and deploys **only if they pass**. If they
  fail, nothing is copied and the game keeps the last good build.
- **F12** on a Valheim symbol — `Recipe.GetAmount`, `Inventory.RemoveItem` —
  decompiles and opens the game's own source inline. This is the single most
  useful thing for understanding what a patch is attaching to.
- Other tasks live under *Terminal → Run Task*, including **watch BepInEx log**,
  which live-tails the game log with mod lines highlighted. It runs
  `tools/watch-log.ps1`, which waits for the log to appear and reattaches when
  BepInEx truncates it at launch, so it can be started before or after the game.

Use **build only (no deploy)** while the game is running, and **build + deploy
(skip tests)** for a fast inner loop when you already know the tests pass.

The test gating lives in `tools/verify-and-deploy.ps1` rather than a VS Code
`dependsOn` chain, so it behaves identically from the terminal and from CI.

## Tools

| Script | Purpose |
|---|---|
| `tools/verify-and-deploy.ps1` | Run the tests, then deploy only if they pass. What Ctrl+Shift+B calls. |
| `tools/watch-log.ps1` | Live-tail the BepInEx log; survives the game restarting. |
| `tools/decompile.ps1 <Type>` | Decompile a game class into `decompiled/` for reference. Requires `ilspycmd`. |
| `tools/undeploy.ps1` | Remove the locally-built DLL from the BepInEx profile, so a Thunderstore-installed copy is the only one. |
| `tools/package.ps1` | Build and zip a mod for Thunderstore. See Releasing below. |

## Releasing

Builds happen locally, not in CI. Mods reference Valheim's own assemblies by
path into a Steam install, and those are Iron Gate's files — they can't go in
the repo and there's no public feed for them, so a GitHub runner can't compile
this project. No great loss: you want to have played a change before releasing
it, and CI couldn't do that either.

1. Bump the version in three places — the mod's `.csproj`, `PluginVersion` in
   `Plugin.cs`, and `manifest.json`. `tools/package.ps1` refuses to run if the
   first and last disagree.
2. Add a `CHANGELOG.md` entry.
3. `powershell -File tools/package.ps1` — runs the tests, builds, and writes
   `dist/<Mod>-<version>.zip` in the flat layout Thunderstore expects.
4. `gh release create v0.1.1 dist/FishQualityBonus-0.1.1.zip --notes "..."`

Publishing the release fires `.github/workflows/publish-thunderstore.yml`,
which uploads that exact zip to Thunderstore. Package metadata comes from
`manifest.json`, so it never has to be repeated in the workflow.

Needs a `THUNDERSTORE_TOKEN` repo secret — a Thunderstore service account token
for the team. **Thunderstore versions are immutable**, so a bad upload can't be
replaced, only superseded; add `dev: true` to the action to publish to
thunderstore.dev while testing the workflow itself.

## Adding a mod

1. `src/NewMod/` with a `.csproj` modelled on FishQualityBonus, plus its own `README.md`
2. `tests/NewMod.Tests/`, linking the mod's pure-logic file
3. `dotnet sln add` both

Nothing at the root needs to change — the shared props, tools and tasks already
apply to every project.
