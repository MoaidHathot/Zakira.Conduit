# Manifest JSON Schema

`conduit.schema.json` is the [JSON Schema 2020-12](https://json-schema.org/draft/2020-12) document for the [`conduit.json` manifest](../README.md#the-manifest-conduitjson).

## How editors find it

A conduit manifest declares which schema to validate against via a `$schema` property. Two patterns are useful:

### 1. Canonical URL (default; used by `conduit init`)

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/MoaidHathot/Zakira.Conduit/main/schemas/conduit.schema.json"
}
```

This is what `conduit init` writes into a new manifest. VS Code, JetBrains IDEs, Helix and most other editors will fetch this URL on first use and cache it, giving you autocompletion + structural validation for free. Works once this repository is published publicly on GitHub.

### 2. Relative path (used by [`example/conduit.json`](../example/conduit.json))

```jsonc
{
  "$schema": "../schemas/conduit.schema.json"
}
```

Useful when:
- the manifest lives inside this repository (e.g. the runnable sample), or
- you have a copy of the schema checked in next to your manifest, and you don't want any network round-trip during editing.

## Bumping the schema

The schema and the runtime [`ManifestValidator`](../src/Zakira.Conduit.Core/Manifest/ManifestValidator.cs) intentionally describe the same vocabulary. When you add or rename a field on the manifest model:

1. Update the C# model + validator under `src/Zakira.Conduit.Core/Manifest/`.
2. Update this schema's `$defs` and field tables in the [README](../README.md).
3. The `SchemaDriftCanaryTests` under `tests/Zakira.Conduit.Core.UnitTests/` will catch a couple of common drift cases (missing source kinds, missing well-known fields), but they aren't exhaustive &mdash; treat them as a backstop, not a contract.
4. When the manifest schema gains a breaking change, bump `ManifestNames.CurrentSchemaVersion` and the schema's `version.const`.
