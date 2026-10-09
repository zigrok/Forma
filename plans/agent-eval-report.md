# Agent evaluation report

What was measured: whether an agent that loads the `forma-ui` skill authors and verifies Forma UI better than one that does not.
Each run is one general-purpose agent working in the Trace repo on a small task, ending with a report of every command it ran. The
"without skill" agents were told not to load the skill, not to read `.opencode/`, `.claude/`, the generated Forma docs, or the
agent sections of `AGENTS.md`; they could use everything else in the repo, including `tools/` (which is how they found the
`trace-ui.py` live-app driver). All work was reverted after each run. Numbers are as reported by the agents and checked against
the transcripts; this is a small sample (one run per cell), not a statistical result.

## Final round (after the fixes listed below)

| Scenario | Task | With skill: iterations | With skill: tools | Without skill: iterations | Without skill: tools |
| --- | --- | --- | --- | --- | --- |
| S1 | Add a 1px accent rule between two menu buttons | 1 | validate, format, preview, `trace-ui query` | 1 | screenshot flag, `trace-ui inspect`, `trace-ui query` |
| S2 | Give a settings label the muted color and name the rule that colors it | 1 | validate, format, `trace-ui inspect`, `trace-ui shot` | 1 | `trace-ui shot`, `trace-ui inspect` |
| S3 | Add a 300px progress bar and a collapsed "Details" section | 2 | validate, format, preview, `trace-ui query` | 3 | validate, preview, `trace-ui inspect`, `trace-ui query`, `trace-ui shot` |

| S4 | Keyboard-modality hover border part rule plus a reduced-motion-aware root animation (theme.fcss) | 1 | validate, format, `trace-ui shot/query/inspect` (no preview) | 1 | validate, format, `trace-ui shot/inspect` (no query) |

Iterations are edit-then-verify rounds until the change validated and looked right. Totals: 4 with the skill, 5 without; the skill
run was shorter in S3 and equal in S1 and S2.

## Round 4 note

S4 was first run with both agents in parallel by mistake (they edited the same file and the results were discarded), then re-run
sequentially after two fixes it exposed: `animation: none` is now accepted, and the skill documents that a shared-theme animation
may only target ids in the root view that links it. Both agents then finished in one iteration with the same tools: the repo's own
tools directory and `--help` output make the live-app tools as discoverable without the skill. On tasks this small the criterion
"fewer iterations in every scenario" cannot be met honestly (1 versus 1); only S3 separated the two.

## Reading of the result

- The criterion "converge in fewer iterations than without, in every scenario" is **not met**: the two simple tasks took one
  iteration either way. The repository already carries discoverable tooling and a theme with the classes the tasks needed, so
  agents without the skill found the same tools (`tools/trace-ui.py`, the preview command) by listing `tools/` and reading `--help`.
- The skill mattered where the dialect has constructs an agent does not know: in S3 the skill's support matrix named `progress`,
  `details/summary` and the muted-color class, and the with-skill agent made two edits against three without it, which also hit
  the same two diagnostics (a `var()` color in an inline style, a progress height under 20px).
- "With-skill runs use the preview and query tools in every scenario": preview and query were used in S1 and S3; in S2 the agent
  used the live inspector and element screenshot instead (the task asked for the rule that colors a label, which is the inspector's
  job), so preview and query were not used there.

## What the evaluations found (and what was done)

Earlier rounds used the same tasks before the fixes; they were worth more than the final numbers because each exposed a defect:

| Finding | Fix |
| --- | --- |
| The style inspector listed no properties and no winning value for any rule driven by `var(--token)` (DynamicStyleSetter was not inspectable) | `DynamicStyleSetter` now reports its property and current value (Forma test) |
| `<details>` was expanded by default and a folded container left children added later visible; its title was never drawn | collapsed unless `open`; children added to a folded container start hidden; the presenter draws the title (checked by rendering) |
| `<progress>` shorter than 20px was silently 20px; `width: 300px` in a column was ignored because a filling child takes the whole slot | diagnostic with help for under 20px; a declared width in a column (height in a row) anchors the child so it holds (tests) |
| `box-shadow` or a gradient on a Label/Button became a build error from the XAML layer | FHTML diagnostic with a help line naming the wrapper (test) |
| The skill's loop was skipped (agents validated but never looked) | the look step is mandatory and says how to run it, with the zsh function note |
| `preview` failed silently when the build failed; it rendered every profile (minutes) | build errors are printed with a help line; only the requested profile renders (seconds) |
| Live-app tools could not select a settings tab or build the Desktop worker; `ui-query` dropped `*` | `trace-ui.py --tab`, builds both projects, universal selector kept (tests) |
| Edits to only a `.fhtml` sometimes showed stale UI | generated XAML is now a compiler input (Cecil injection no longer runs on a pre-injected assembly); `tools/verify-ui-incremental.sh` guards the symptom. Not reproduced by a unit test |

## Not yet true

- The skill does not make agents faster on tasks the repo's own docs and tools already make easy.
- `forma-xaml validate` on a single view does not know the theme's tokens, so an inline `var(--trace-color-…)` is rejected; the
  help line points at classes but not at "this view does not link the theme".
- The preview host renders gallery scenes only; a view with no scene (the pause menu) cannot be previewed.
