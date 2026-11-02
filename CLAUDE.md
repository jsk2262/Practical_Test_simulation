# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes.

**Language:** Always respond to the user in Korean, regardless of this file's language.

**Tradeoff:** Bias toward caution over speed. Use judgment for trivial tasks.

## 1. Think Before Coding
- State assumptions explicitly; ask if uncertain.
- Present multiple interpretations instead of picking silently.
- Suggest simpler approaches; push back when warranted.
- Stop and ask when something is unclear.

## 2. Simplicity First
- Build only what's asked — no speculative features, abstractions, or configurability.
- No error handling for impossible scenarios.
- Rewrite if 200 lines could be 50.
- Ask: "Would a senior engineer call this overcomplicated?"

## 3. Surgical Changes
- Touch only what the task requires. Don't refactor or reformat unrelated code.
- Match existing style even if you'd do it differently.
- Mention unrelated dead code instead of deleting it.
- Remove only imports/variables/functions your own change made unused.
- Every changed line should trace to the user's request.

## 4. Goal-Driven Execution
- Turn tasks into verifiable goals (e.g., "fix the bug" → reproduce with a test, then make it pass).
- For multi-step tasks, state a brief plan with a verification step per item.

## 5. Performance-Aware, Not Premature
- Avoid obvious inefficiencies (repeated work, N+1 queries, redundant I/O).
- Prefer the algorithmically better option when equally simple.
- Skip caching/memoization/micro-optimization unless measured or requested.
- Name the tradeoff if an optimization adds real complexity.

---

# Unity Project Rules

Before starting work, ask up to 3 clarifying questions if you need more information to fully understand the task.

## 1. Architecture & Managers
- Centralize shared systems (state, sound, UI) in Manager classes.
- Singletons allowed only for Managers (`GameManager.Instance`) — never for regular objects (player, monsters, etc.).
- Single Responsibility Principle: one script, one role (e.g., split `Player` into `Movement`, `Health`, `Inventory`).

## 2. Object Communication
- Never use `GameObject.Find()`. Reference objects via Inspector assignment or Manager caching.
- Use event-driven communication (C# `event`/`Action`) to minimize coupling — e.g., `Player` fires a "hit" event; UI subscribes and updates itself.

## 3. Object References
- Prefer `GetComponent` over direct Inspector assignment, except in special cases.
- Never use hierarchy-search functions like `GameObject.Find` (runtime performance cost).
- Avoid string comparisons (tags, names) — costly and error-prone.

## 4. Resources & Assets
- Prefab-first: make dynamic/repeated objects prefabs. Edit originals only in Prefab Mode, never directly in the scene.
- Use prefix naming for clarity (`P_` prefab, `T_` texture, `M_` material).
- Organize prefabs by folder under `@Resources` so the user can swap them easily.

## 5. No Per-Frame Allocation
- Don't allocate objects dynamically in per-frame code (e.g., `Update`), except in special cases.

## 6. Planning
> "We're starting Unity/C# game development. The attached CLAUDE.md is our architecture and coding convention, and Plan.md is the design spec for the system to build. Fully internalize both. Once understood, don't write code yet — summarize the design's core loop in 3 lines."

## 7. Object Hierarchy & Instantiation
- If an object in the Hierarchy needs management, give it a Manager and register it as that Manager's child.
- For dynamically instantiated objects, don't leave a prefab instance in the Hierarchy — spawn from the original prefab at runtime.
- The scene is grouped under top-level empty parents: `Managers`, `Background`, `Player`, `Enemies`, `UI`. Put every new scene object under the group it belongs to; create a new top-level group only when none of them fits. Never leave new objects loose at the scene root.
- Group parents must stay at position (0,0,0), identity rotation, scale 1, so child world transforms are unaffected. When reparenting, preserve world position (`SetParent(parent, true)`) and afterward verify both transforms and Inspector references.
- Never parent anything under `GridManager` — `ClearAllBlocks()` destroys all of its children, so only locked blocks belong there.

## 8. Inline Comments
- Add a short comment next to each line explaining what action it performs, not just the non-obvious ones. This overrides the "no comments" default in the general guidelines above for this project.
- Write these comments in Korean.

---

**These guidelines are working if:** fewer unnecessary diff changes, fewer overcomplication-driven rewrites, and clarifying questions come before implementation rather than after mistakes.
