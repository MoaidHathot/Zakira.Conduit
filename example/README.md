# Example manifests

This directory holds sample `conduit.json` files and any supporting files
they reference.

| File | What it demonstrates |
|------|-----------------------|
| [`conduit.json`](./conduit.json) | A complete manifest mixing GitHub sources (tracked branch, pinned commit) and a `local` source. Includes one disabled entry. |
| [`local-skill-sample/`](./local-skill-sample/) | The on-disk content referenced by the `local` entry in `conduit.json`. Edit and re-sync to see updates propagate. |

## Try it

```bash
# Parse & validate the sample manifest:
conduit validate --manifest example/conduit.json

# See what the sample manifest would do:
conduit list     --manifest example/conduit.json
conduit sync     --manifest example/conduit.json --dry-run

# Sync for real (this writes into ~/.config/claude/skills and the other
# configured targets; edit conduit.json first if those paths aren't right
# for your machine).
conduit sync     --manifest example/conduit.json
```

The two GitHub entries are intentionally pointed at repositories that don't
necessarily exist on your account; you'll get a clean per-entry error report
for those, while the `local-skill-sample` entry will succeed and mirror this
directory into every target you've configured.
