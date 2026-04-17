# Contributing Skills

This guide covers how to add agent skills to the maui-labs marketplace.

## Skill Structure

```
plugins/dotnet-maui/skills/<skill-name>/
├── SKILL.md           # Skill definition (required)
├── references/        # Detailed reference docs (optional)
│   └── *.md
└── scripts/           # Helper scripts (optional)
    └── *.ps1 / *.sh
```

## SKILL.md Format

Every skill must have YAML frontmatter:

```yaml
---
name: my-skill-name
description: >-
  Short description of what this skill does.
  USE FOR: the specific scenarios where this skill applies.
  DO NOT USE FOR: scenarios where this skill should NOT be activated.
---
```

### Frontmatter Rules

- **`name`** — kebab-case, descriptive (e.g., `devflow-visual-tree`, `maui-build-diagnosis`)
- **`description`** — This is the only thing agent runtimes read first to decide activation. Be explicit about scope.

### Body Guidelines

- Keep under 500 lines; split detailed content into `references/` files
- Include: Purpose, When to Use, Inputs, Workflow (numbered steps), Validation
- Use concrete checklists and CLI commands, not vague guidance
- Reference the `maui` CLI and DevFlow APIs where applicable

## Evaluation Tests

Every skill should have evaluation scenarios:

```
tests/<plugin-name>/<skill-name>/
└── eval.yaml
```

### eval.yaml Format

```yaml
scenarios:
  - name: "Descriptive scenario name"
    prompt: |
      The prompt sent to the agent. Be specific about the user's
      situation, platform, error messages, etc.
    assertions:
      - type: "output_contains"
        value: "expected text in response"
      - type: "output_matches"
        pattern: "regex.*pattern"
      - type: "output_not_contains"
        value: "text that should NOT appear"
    rubric:
      - "Agent recommends the correct diagnostic tool"
      - "Agent provides platform-appropriate commands"
      - "Agent does not suggest deprecated approaches"
    timeout: 120
```

### Assertion Types

| Type | Description |
|------|-------------|
| `output_contains` | Response includes exact text |
| `output_not_contains` | Response must NOT include text |
| `output_matches` | Response matches regex pattern |
| `output_not_matches` | Response must NOT match regex |

### Rubric Guidelines

Rubric items are judged by an LLM comparing responses with and without the skill. Good rubric items are:
- **Specific** — "Recommends `maui doctor` as first step" not "Gives good advice"
- **Measurable** — Can be objectively evaluated from the response
- **Relevant** — Tests what the skill actually teaches

## Writing Good Evaluations

Evaluations use **pairwise comparison**: an LLM generates a response _with_ the skill injected and another _without_. A judge LLM scores which response is better. This means your scenarios must test knowledge the agent **wouldn't have without the skill**.

### The Three Mistakes to Avoid

1. **Testing common knowledge** — If a top LLM already knows the answer (e.g., "use `@objc` for Swift interop"), the skill won't improve the response and your scenario measures nothing. Test specific details from your skill document: exact API names, version conversion formulas, decision trees, specific error codes.

2. **Brittle assertions** — `output_contains: "Run the following command"` will break on rephrasing. Use `output_matches` with regex alternatives: `"gradlew|gradle wrapper|./gradlew"`. Check concepts, not exact phrasing.

3. **Vague rubric items** — "Provides helpful guidance" can't be objectively measured. Instead: "Maps `androidx.core:core` to `Xamarin.AndroidX.Core` NuGet package" — this is either present or not.

### What Makes a Good Scenario

| Good Scenario | Bad Scenario |
|---|---|
| Tests skill-specific knowledge (version math, API URLs, error codes) | Tests general programming knowledge |
| Prompt includes specific versions, error messages, platform details | Prompt is vague ("how do I do bindings?") |
| Assertions check for concrete artifacts (package names, commands, patterns) | Assertions check for generic phrases ("build succeeded") |
| Rubric items describe specific findings | Rubric says "gives good advice" |
| Negative assertions catch common wrong answers | No guardrails |

### Scenario Design Tips

- **Include version numbers** in prompts — forces specific, testable answers
- **Include error messages** — tests diagnostic knowledge unique to the skill
- **Test the decision tree** — when the skill teaches "if X then Y, else Z", write a scenario for each branch
- **Use negative assertions** — `output_not_contains` catches wrong platform advice (e.g., no `adb reverse` for iOS)
- **One aspect per scenario** — don't test everything in one massive prompt
- **Realistic context** — write prompts that sound like real developer questions, not test questions

### Example: Good vs Bad

**Bad** (tests common knowledge, vague rubric):
```yaml
- name: "Create Android binding"
  prompt: "How do I bind an Android library in .NET MAUI?"
  assertions:
    - type: "output_contains"
      value: "binding"
  rubric:
    - "Provides helpful information about Android bindings"
```

**Good** (tests specific skill knowledge, precise assertions):
```yaml
- name: "Resolve XA4241 and XA4242 dependency errors"
  prompt: |
    My Android binding project fails with:
    error XA4241: Java dependency 'javax.inject:javax.inject' is not satisfied.
    error XA4242: Java dependency 'com.google.firebase:firebase-common:21.0.0'
    is not satisfied. Suggested fix: Install NuGet 'Xamarin.Firebase.Common'.
    How do I fix each of these? I don't need them from C# directly.
  assertions:
    - type: "output_matches"
      pattern: "AndroidMavenLibrary|AndroidIgnoredJavaDependency"
    - type: "output_matches"
      pattern: "Bind.*false"
  rubric:
    - "For XA4242 with suggestion, recommends installing the suggested NuGet"
    - "For compile-time-only deps, suggests AndroidIgnoredJavaDependency"
    - "Explains the decision tree: NuGet suggestion → use it; runtime needed → AndroidMavenLibrary; compile-only → ignore"
```

## Validation

### Local Testing

Download the skill-validator from [dotnet/skills releases](https://github.com/dotnet/skills/releases/tag/skill-validator-nightly):

```bash
# Static validation (no LLM)
skill-validator check --plugin plugins/<plugin-name>

# LLM evaluation (requires GitHub auth)
skill-validator evaluate \
  --runs 3 \
  --tests-dir tests/<plugin-name> \
  plugins/<plugin-name>/skills
```

### CI

- **skill-check** — Runs automatically on every PR that modifies `plugins/` or `tests/`
- **skill-evaluation** — Triggered by posting `/evaluate` on a PR (write access required)

## PR Checklist

- [ ] `SKILL.md` has valid YAML frontmatter with `name` and `description`
- [ ] Description includes "USE FOR" and "DO NOT USE FOR"
- [ ] `eval.yaml` has at least 3 scenarios with assertions and rubric
- [ ] `skill-validator check` passes locally
- [ ] Body under 500 lines (use `references/` for detailed content)
