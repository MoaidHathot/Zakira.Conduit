# Zakira.Conduit

> A .NET 10 global tool that **mirrors agent skills** from remote (or local) sources into one or more local target directories — driven by a single declarative manifest.

`conduit` is the missing piece between [agent skills](https://agentskills.io/what-are-skills) you've collected (on GitHub, or just on disk) and the many different folders that your local agents read them from. You describe *what to sync* and *where it should land*; `conduit sync` does the rest.

```text
+-------------------+        +---------+        +-------------------------+
|  github.com/...   |  ===>  |         |  ===>  |  ~/.config/claude/...   |
|  /vendor/skills   |  ===>  | conduit |  ===>  |  ~/projects/foo/.agents |
|  ...future...     |  ===>  |  sync   |  ===>  |  ...                    |
+-------------------+        +---------+        +-------------------------+
```

---

## Highlights

- **One manifest, many targets, many sub-paths.** Each entry has a single source (which may produce one or many content units) and a list of destinations.
- **Multiple source kinds, same model.** Ships with **GitHub** (zipball snapshot) and **local directory** sources today; new kinds (GitLab, plain HTTP archive, …) are a single record + fetcher away.
- **Browser-paste friendly.** GitHub sources accept slug (`owner/repo`), browser URL (`https://github.com/owner/repo`), or SSH form (`git@github.com:owner/repo.git`) in the same field.
- **Multi-path fetches.** A single entry can pull N sub-paths out of one source in one go (one zipball, one extraction, N destinations).
- **No `git clone` required.** GitHub sources are fetched as a *zipball snapshot* over HTTPS (one request, no `.git/` history, supports any commit/branch/tag).
- **Atomic mirroring.** Each target is written via a sibling staging directory and swapped in place, so a failure cannot leave a half-updated target.
- **Stale files are removed.** When a source changes, files that disappeared upstream disappear locally too — without nuking unrelated content in the same target directory.
- **XDG-friendly discovery.** Drops `conduit.json` at `$XDG_CONFIG_HOME/Zakira.Conduit/`, with sensible fallbacks on Windows and POSIX.
- **Safety rails.** Refuses to run when a source path overlaps with one of its targets, so a misconfigured local source can't recursively copy itself. Duplicate destination basenames in a multi-path entry are caught at validation time.
- **Extensible.** `ISkillSource` + `ISkillSourceFetcher` are first-class abstractions; adding a new source kind is a single record + fetcher away.
- **Tested.** Ships with unit, integration and end-to-end test suites; the GitHub fetcher is exercised against an in-process HTTP mock.

---

## Install

`conduit` is distributed as a [.NET global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools). With the .NET 10 SDK installed:

```bash
dotnet tool install --global Zakira.Conduit
```

Update later with:

```bash
dotnet tool update --global Zakira.Conduit
```

> Until the first NuGet release, you can `dotnet pack` the repo locally (`dotnet pack -c Release`) and install from the produced `.nupkg`:
>
> ```bash
> dotnet tool install --global --add-source ./artifacts Zakira.Conduit
> ```

---

## Quickstart

```bash
# 1. Create a starter manifest at $XDG_CONFIG_HOME/Zakira.Conduit/conduit.json
conduit init

# 2. Edit it (or point at your own with --manifest <path>) and validate
conduit validate

# 3. Preview what a sync would do
conduit sync --dry-run

# 4. Run it for real
conduit sync
```

A complete sample manifest \u2014 mixing GitHub and local sources, a pinned
commit, and a disabled entry \u2014 lives in [`example/conduit.json`](./example/conduit.json).
You can drive the CLI against it without copying:

```bash
conduit validate --manifest example/conduit.json
conduit list     --manifest example/conduit.json
conduit sync     --manifest example/conduit.json --dry-run
```

---

## The manifest (`conduit.json`)

Each entry has a source mirrored to one-or-more targets.

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/anomalyco/Zakira.Conduit/main/schemas/conduit.schema.json",
  "version": 1,
  "entries": [
    {
      "name": "code-review",
      "description": "One skill from a GitHub repo, tracked on main.",
      "source": {
        "type": "github",
        "repo": "anthropics/skills",     // also accepts https://github.com/anthropics/skills
        "path": "code-review",
        "branch": "main"
      },
      "targets": [
        "~/.config/claude/skills",
        "~/projects/foo/.agents/skills"
      ]
      // → ~/.config/claude/skills/code-review/
      // → ~/projects/foo/.agents/skills/code-review/
    },

    {
      "name": "anthropic-bundle",
      "description": "Mirror MANY skills from one repo with a single fetch.",
      "source": {
        "type": "github",
        "repo": "https://github.com/anthropics/skills",
        "paths": ["code-review", "test-writer", "refactor"],
        "branch": "main"
      },
      "targets": ["~/.config/claude/skills"]
      // → ~/.config/claude/skills/code-review/
      // → ~/.config/claude/skills/test-writer/
      // → ~/.config/claude/skills/refactor/
      // (entry name 'anthropic-bundle' is metadata only when paths.Count > 1)
    },

    {
      "name": "internal-runbooks",
      "description": "Private repo pinned to a commit SHA. Uses $CONDUIT_GITHUB_TOKEN.",
      "source": {
        "type": "github",
        "repo": "my-org/agent-runbooks",
        "commit": "1f2e3d4c5b6a"
      },
      "targets": ["$XDG_CONFIG_HOME/agents/skills"]
    },

    {
      "name": "house-style",
      "description": "An in-house skill kept on disk under version control.",
      "source": {
        "type": "local",
        "path": "./vendor/skills/house-style"
      },
      "targets": [
        "~/.config/claude/skills",
        "~/projects/bar/.agents/skills"
      ]
    },

    {
      "name": "local-bundle",
      "description": "Mirror several local directories at once.",
      "source": {
        "type": "local",
        "paths": ["./vendor/skills/a", "./vendor/skills/b"]
      },
      "targets": ["~/projects/bar/.agents/skills"]
      // → ~/projects/bar/.agents/skills/a/
      // → ~/projects/bar/.agents/skills/b/
    }
  ]
}
```

### Source kinds

#### `github`

Snapshot of a GitHub repository, fetched as a zipball over HTTPS.

| Field    | Required | Notes |
|----------|----------|-------|
| `repo`   | yes | Repository identifier. Accepts the `owner/repo` slug, a browser URL (`https://github.com/owner/repo`, with or without `.git`), `github.com/owner/repo`, or the SSH form (`git@github.com:owner/repo.git`). |
| `path`   | no  | Single repo-relative sub-path. No `..`, no leading `/`. Mutually exclusive with `paths`. |
| `paths`  | no  | List of repo-relative sub-paths. Mutually exclusive with `path`. When two or more are listed, each one mirrors to `<target>/<basename>/` and the entry's `name` becomes metadata only (used for filtering and logs). Duplicate basenames are a validation error. |
| `branch` | no  | Branch or tag name. Mutually exclusive with `commit`. |
| `commit` | no  | Pin to an immutable commit SHA. Mutually exclusive with `branch`. |

If neither `branch` nor `commit` is given, the repository's default branch is used. If neither `path` nor `paths` is given, the **whole repository** is mirrored.

#### `local`

One or more directories on the local filesystem. Useful for in-repo skills, in-house skills you check in next to other code, or anything you'd otherwise `cp -R` manually.

| Field   | Required | Notes |
|---------|----------|-------|
| `path`  | one of `path`/`paths` | Single source directory. Absolute, or relative to the manifest's directory. Mutually exclusive with `paths`. |
| `paths` | one of `path`/`paths` | Multiple source directories. Mutually exclusive with `path`. When two or more are listed, each mirrors to `<target>/<basename>/` and the entry's `name` becomes metadata only. |

Paths support `~` and environment-variable expansion (`$VAR`, `${VAR}`, and `%VAR%` on Windows). No copy is made on the way in: directories are read directly and mirrored into each target. `conduit` refuses to run when a source path overlaps with one of its targets, so you can't accidentally recurse into your own output.

### Destination naming

| Source produces | Destination per target |
|---|---|
| 1 content unit (no `paths`, or 1-element `paths`) | `<target>/<entry.name>/` |
| N ≥ 2 content units (`paths` with multiple elements) | `<target>/<basename(path)>/` per unit. The entry's `name` is metadata only. |

This keeps the simple case ergonomic ("name the entry after the skill, target gets one folder by that name") while letting one entry mirror N skills out of one source with a single fetch.

### Field reference

| Field                       | Required | Notes |
|-----------------------------|----------|-------|
| `version`                   | yes      | Schema version. Currently `1`. |
| `entries[]`                 | yes      | At least one. |
| `entries[].name`            | yes      | `[A-Za-z0-9._-]+`. Used as the destination subdirectory when the source produces exactly one content unit; metadata only otherwise. Must be unique within the manifest. |
| `entries[].description`     | no       | Free-form documentation; ignored at runtime. |
| `entries[].disabled`        | no       | `true` skips the entry during `sync`. |
| `entries[].source.type`     | yes      | `"github"` or `"local"`. The discriminator for future source kinds. |
| `entries[].source.*`        | varies   | See the per-kind tables above. |
| `entries[].targets[]`       | yes      | List of directory paths. `~`, `$VAR`, `${VAR}` and (on Windows) `%VAR%` are expanded. Relative paths are rooted against the manifest's directory. |

### Manifest discovery

When `--manifest` / `-m` is **not** provided, `conduit` looks (in order) at:

1. `$XDG_CONFIG_HOME/Zakira.Conduit/conduit.json`
2. `$HOME/.config/Zakira.Conduit/conduit.json` *(XDG-style fallback)*
3. `%APPDATA%/Zakira.Conduit/conduit.json` *(Windows-only fallback)*
4. `./conduit.json` *(current working directory)*

The first one that exists wins.

---

## Commands

```text
conduit [--manifest <path>] [--verbosity <level>|--quiet|--verbose] <command>
```

| Command | Description |
|---|---|
| `conduit init [--force]`   | Write a starter `conduit.json`. Refuses to overwrite an existing file unless `--force` is set. |
| `conduit validate`         | Parse and validate the manifest. **Does not** touch the network. |
| `conduit list`             | Print a one-line summary of every entry. |
| `conduit sync [options]`   | Fetch sources and mirror them into each target. |

### `conduit sync` options

| Option | Description |
|---|---|
| `--entry <name>`            | Restrict the run to specific entries. Repeatable: `--entry a --entry b`. |
| `--dry-run`                 | Fetch sources and report what *would* change, without writing to targets. |
| `--stop-on-first-error`     | Abort on the first failing entry instead of attempting the rest. |
| `-m, --manifest <path>`     | Override manifest discovery (global). |
| `-v, --verbose` / `--verbosity detailed` | Verbose output. |
| `-q, --quiet`               | Warnings and errors only. |

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | One or more entries failed during `sync`. |
| `2` | Manifest could not be located, parsed or validated. |

---

## Authentication & rate limits

For private repositories or to lift the anonymous GitHub rate-limit, set a token before running `conduit`:

```bash
export CONDUIT_GITHUB_TOKEN=ghp_********************************
# or, if you already export it for other tools:
export GITHUB_TOKEN=ghp_********************************
```

`conduit` sends the token as `Authorization: Bearer …` only against the configured GitHub API base address. It is never persisted.

For testing or to point at a different host, set `CONDUIT_GITHUB_API_BASE` (e.g. `http://127.0.0.1:5050/`).

---

## How it works

```text
+---------+    1. resolve manifest path     +-----------+
| CLI args| ----------------------------->  | Manifest  |
+---------+    (--manifest > XDG > ...)     +-----------+
                                                 |
                          2. load + validate     v
                                            +----+----+
                                            | Entries |
                                            +----+----+
                                                 |
                  for each entry:                |
                                                 v
                                       +------------------+
                                       | SkillSourceFetcher|
                                       +--------+----------+
                                                |  (GitHub zipball ->
                                                |   stream -> temp dir,
                                                |   optionally sub-pathed)
                                                v
                                          +-----------+
                                          | Local copy |
                                          +-----+------+
                                                |
                                                v   (per target)
                                +-----------------------------------+
                                | AtomicDirectoryMirror              |
                                |  - write into .staging-<guid>     |
                                |  - move existing aside            |
                                |  - rename staging into place      |
                                |  - delete the aside copy          |
                                +-----------------------------------+
```

Key properties of the mirror step:

- **Per-entry sub-directory:** the entry `name` is appended to every configured target, so the target dir itself is not destroyed.
- **Atomic-ish swap:** the new content is fully materialized in a sibling directory, then renamed into place. On the same filesystem this rename is atomic.
- **No leaked files:** when content shrinks (a file is deleted upstream), the target shrinks too, because the swap replaces the *whole* sub-directory.

---

## Extending: add a new source kind

Implement two small types and register them with DI.

```csharp
// 1. The manifest-shape: implement ISkillSource and register the discriminator.
public sealed record GitLabSkillSource : ISkillSource
{
    public const string TypeDiscriminator = "gitlab";

    [JsonPropertyName("project")] public required string Project { get; init; }
    [JsonPropertyName("ref")]     public string?         Ref     { get; init; }

    [JsonIgnore] public string Kind => TypeDiscriminator;
}
```

Add `[JsonDerivedType(typeof(GitLabSkillSource), GitLabSkillSource.TypeDiscriminator)]` on `ISkillSource`.

```csharp
// 2. The fetcher: turn a source into a local content directory.
public sealed class GitLabSkillSourceFetcher : ISkillSourceFetcher
{
    public string SourceKind => GitLabSkillSource.TypeDiscriminator;

    public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken ct = default)
    {
        // `context.ManifestDirectory` lets you resolve any path-shaped fields
        // in the source relative to the manifest, mirroring how the local
        // and target paths are handled.
        // Download a zip, extract to a temp dir, return a FetchedSource pointing
        // at that dir with a cleanup callback.
    }
}
```

Register it:

```csharp
services.AddConduitCore();
services.AddSingleton<ISkillSourceFetcher, GitLabSkillSourceFetcher>();
```

The synchronizer and mirror are source-agnostic, so that's all that's needed.

---

## Repository layout

```text
src/
  Zakira.Conduit.Core/   # Manifest model, source abstractions, mirror, sync engine.
  Zakira.Conduit/        # CLI (packaged as a global tool, command name: conduit).
tests/
  Zakira.Conduit.Core.UnitTests/   # Pure unit tests, no network, no real fs except tmp.
  Zakira.Conduit.IntegrationTests/ # GitHub fetcher + synchronizer against an in-process HTTP mock.
  Zakira.Conduit.E2ETests/         # Spawns the built conduit.dll as a subprocess.
example/
  conduit.json           # Runnable sample manifest covering every supported feature.
  local-skill-sample/    # On-disk content referenced by the sample's `local` entry.
schemas/
  conduit.schema.json    # JSON-Schema for editor IntelliSense.
```

Tooling: `global.json` pins the SDK to .NET 10; `Directory.Build.props` and `Directory.Packages.props` set shared properties and central package versions; `NuGet.config` pins the package source to `nuget.org`.

---

## Building & testing

```bash
# Restore + build everything.
dotnet build

# Run every test suite.
dotnet test

# Run a single suite.
dotnet test tests/Zakira.Conduit.Core.UnitTests/Zakira.Conduit.Core.UnitTests.csproj
dotnet test tests/Zakira.Conduit.IntegrationTests/Zakira.Conduit.IntegrationTests.csproj
dotnet test tests/Zakira.Conduit.E2ETests/Zakira.Conduit.E2ETests.csproj

# Pack the global tool.
dotnet pack src/Zakira.Conduit/Zakira.Conduit.csproj -c Release -o ./artifacts

# Install locally and try it.
dotnet tool install --global --add-source ./artifacts Zakira.Conduit
conduit --help
```

A convenience script wraps pack + push for releases:

```powershell
# Pack only (writes Zakira.Conduit.*.nupkg + Zakira.Conduit.Core.*.nupkg into ./artifacts).
./pack.ps1

# Pack + push to nuget.org. Reads the key from $env:NUGET_API_KEY when -ApiKey is omitted.
$env:NUGET_API_KEY = 'oy2abc...'
./pack.ps1 -Push
```

### Versioning

Every package, assembly, and file in this repository ships with the **same version**, stamped from a single line in [`Directory.Build.props`](./Directory.Build.props):

```xml
<VersionPrefix>0.1.0</VersionPrefix>
```

To cut a release:

1. Edit that number.
2. Commit and push.
3. Run `./pack.ps1 -Push`.

`pack.ps1` intentionally does **not** expose a `-Version` override. The only way to change what gets shipped is to change the file. That keeps git history, NuGet, and the embedded assembly version in lock-step, and prevents accidental publishes whose version isn't recorded anywhere.

If a new packable project is added later it inherits the same `VersionPrefix` automatically &mdash; no per-project version is ever declared.

The integration & E2E tests use [`System.Net.HttpListener`](https://learn.microsoft.com/dotnet/api/system.net.httplistener) to stand up an in-process server that emulates GitHub's `zipball` endpoint, so they run offline and are deterministic.

---

## License

MIT — see [`LICENSE`](./LICENSE).
