# Manifest field reference

Every field accepted by a `conduit.json` manifest. The runtime validator (`ManifestValidator`) and the JSON Schema at `schemas/conduit.schema.json` are the authoritative specs; this document mirrors them in prose.

## File format and discovery

- **File name**: `conduit.json` (preferred) or `conduit.jsonc` (editor-hint alias). Both are probed at every discovery location; `.json` wins when both exist.
- **Format**: JSONC. Line comments (`// ...`), block comments (`/* ... */`), and trailing commas are accepted regardless of file extension.
- **Discovery order** (when `--manifest` is not supplied): `$XDG_CONFIG_HOME/Zakira.Conduit/conduit.{json,jsonc}` -> `$HOME/.config/Zakira.Conduit/conduit.{json,jsonc}` -> `./conduit.{json,jsonc}`.
- **Round-trip trivia**: `conduit pin` / `unpin` / `update` preserve comments and trailing commas when every touched entry has a string-shaped source on disk (URL rewrite). When an object-shaped source needs key insertion/removal (e.g. dropping `branch` on pin), the write falls back to a full reformat that loses trivia.

## Top-level object

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/MoaidHathot/Zakira.Conduit/main/schemas/conduit.schema.json",
  "version": 1,
  "entries": [ /* ... */ ]
}
```

| Field      | Required | Notes |
|------------|----------|-------|
| `$schema`  | no       | Editor hint. Ignored at runtime. |
| `version`  | no       | Schema version. Must be `1`. Default is `1`. |
| `entries`  | yes      | Non-empty array of entry objects. |

## Entry object

```jsonc
{
  "name": "code-review",
  "description": "Optional human-readable notes.",
  "disabled": false,
  "source": { /* see below */ },
  "targets": [ /* see below */ ]
}
```

| Field         | Required | Notes |
|---------------|----------|-------|
| `name`        | no       | Matches `[A-Za-z0-9._-]+`. Unique within the manifest. **Optional**: when omitted, Conduit derives a default from the source (GitHub/AzDO repo name, single-path local directory basename) or from an explicit alias supplied via the in-string `... -> Name` shorthand or the `{ "source": ..., "as": "Name" }` wrapper. Becomes the destination subdirectory name when the source produces exactly one content unit (and no per-target `as:` override is set). When the source produces multiple content units, the name is metadata only (used by `--entry` filtering and logs). |
| `description` | no       | Free-form documentation; ignored at runtime. |
| `disabled`    | no       | Default `false`. When `true`, `sync` skips the entry. |
| `source`      | yes      | The remote or local source. See "Source object". |
| `targets`     | yes      | Non-empty array. See "Target item". |

## Source object

Discriminator field `type` selects one of:

### `type: "github"`

```jsonc
{
  "type": "github",
  "repo": "owner/repo",
  "path": "sub/path",
  "paths": [ "skills/a", { "path": "skills/b", "as": "renamed" } ],
  "branch": "main",
  "commit": "abc123def..."
}
```

| Field    | Required | Notes |
|----------|----------|-------|
| `repo`   | yes      | Repository identifier. Accepts `owner/repo`, `github.com/owner/repo`, `https://github.com/owner/repo[.git]`, or `git@github.com:owner/repo[.git]`. |
| `path`   | no       | Single sub-path inside the repo. Mutually exclusive with `paths`. Must not begin with `/` or contain `..`. |
| `paths`  | no       | Array of sub-paths. Mutually exclusive with `path`. Each element is either a string or an object `{ path, as? }`. Duplicate destination basenames across the array are a validation error. |
| `branch` | no       | Branch or tag name. May coexist with `commit`: when both are set, `branch` is the tracking intent and `commit` is the snapshot fetched. |
| `commit` | no       | Commit SHA. Wins over `branch` for fetching. |

If neither `branch` nor `commit` is set, the repository's default branch is used. If neither `path` nor `paths` is set, the entire repository is mirrored.

### `type: "local"`

```jsonc
{
  "type": "local",
  "path": "./local-dir",
  "paths": [ "./a", { "path": "./b", "as": "renamed" } ]
}
```

| Field   | Required | Notes |
|---------|----------|-------|
| `path`  | one of `path` / `paths` | Single source directory. Mutually exclusive with `paths`. |
| `paths` | one of `path` / `paths` | Array of source directories. Mutually exclusive with `path`. |

Local paths support `~`, `$VAR`, `${VAR}`, and `%VAR%` (Windows) expansion. Relative paths are anchored at the manifest's directory.

## Target item

Each element of `targets` is either:

- a **bare string** &mdash; the target directory path, or
- an **object** `{ "path": "...", "as": "..." }` &mdash; the same path plus an alias that overrides the entry's `name` for that one destination only.

Per-target `as:` aliases are forbidden on multi-unit entries (where a source produces multiple destinations) because the basename rule would silently override them.

## Path expansion

Both target paths and local source paths support:

- `~` &mdash; expands to the user's home directory.
- `$NAME` and `${NAME}` &mdash; environment variable expansion, on all platforms.
- `%NAME%` &mdash; Windows-style environment variable expansion (Windows only).
- Relative paths &mdash; anchored at the manifest's own directory.

## Validation summary

- `entries` is non-empty.
- Every `name` matches the allowed character set and is unique.
- Every entry resolves to a non-null `name` (either explicit, alias-supplied, or source-derived).
- Every `targets[]` is non-empty and contains at least one valid path.
- For github sources: `repo` must parse to a valid `owner/name`.
- `path` + `paths` are mutually exclusive on every source.
- For multi-element `paths`: every resolved destination name (alias or basename) is unique.
- For multi-unit sources: no target may carry a per-target `as:` alias.
- A source path may not begin with `/` or contain `..` for github sources.
- **Cross-entry destination uniqueness**: two entries (or two array-expanded sub-entries) must not write into the same `<target>/<destName>/` directory.

Run `conduit validate` to surface every violation. The exit code is `2` when the manifest fails to load or validate.

## Source aliases (`... -> Name` and `{ source, as }`)

Two equivalent shorthand forms attach a destination alias to a source without
having to spell out the full entry-level `name` field. Useful when you want
multiple array elements to land in well-named folders without writing each
one out:

```jsonc
// In-string arrow suffix on a bare URI. The alias after ' -> ' must match
// [A-Za-z0-9._-]+. Reserved exclusively for the bare-string shorthand;
// concrete `{type, ...}` objects use the wrapper form below.
"source": "https://github.com/anthropics/skills/code-review -> CodeReview"

// Object wrapper. Recognised by the absence of a `type` discriminator.
// Works around any inner source shape: a bare URI, a concrete object, etc.
"source": { "source": { "type": "github", "repo": "anthropics/skills", "path": "code-review" }, "as": "CodeReview" }
```

Both set the entry name to `CodeReview`. The wrapper is rejected if it
contains anything other than `source` and `as`, if `source` is another
wrapper (only one alias per source), or if `source` is an array (aliases
apply to single sources only).
