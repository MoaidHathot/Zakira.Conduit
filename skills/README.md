# Skills bundled with Zakira.Conduit

Each subdirectory of `skills/` is an [agent skill](https://agentskills.io/) &mdash; a self-contained folder with a `SKILL.md` (YAML frontmatter + markdown body) that an agent can load on demand. They follow the open [Agent Skills](https://agentskills.io/specification) format.

| Skill | What it does |
|---|---|
| [`zakira-conduit/`](./zakira-conduit/) | Teaches any agent how to use, configure, and troubleshoot the `conduit` CLI. |

## Using these skills

The skills here are themselves designed to be mirrored via `conduit`. The simplest manifest entry is just the bare URL:

```jsonc
{
  "source": "https://github.com/MoaidHathot/Zakira.Conduit/skills/zakira-conduit",
  "targets": [ "~/.config/agents/skills" ]
}
```

That tracks the default branch and lands the skill at `~/.config/agents/skills/Zakira.Conduit/` (the destination folder is derived from the repo name). Use the explicit object form when you need to set `branch`, `commit`, `include`/`exclude`, or an entry-level `name`:

```jsonc
{
  "name": "zakira-conduit",
  "source": {
    "type": "github",
    "repo": "MoaidHathot/Zakira.Conduit",
    "path": "skills/zakira-conduit",
    "branch": "main"
  },
  "targets": [ "~/.config/agents/skills" ]
}
```

After `conduit sync`, your agent's skill folder gains a `zakira-conduit/` (or `Zakira.Conduit/`) subdirectory containing the full skill (`SKILL.md` + `references/` + `assets/`).

## Layout

Every skill in this directory follows the spec:

```
<skill-name>/
├── SKILL.md                # required: YAML frontmatter (name + description) + body
├── references/             # optional: detail docs loaded only when needed
└── assets/                 # optional: templates, sample data, copy-paste-ready files
```

The skill's directory name **must** match the `name:` field of its frontmatter.

A repository-level unit test (`SkillsValidationTests`) checks that every `SKILL.md` here has valid frontmatter and that names + directories agree.
