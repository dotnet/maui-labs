---
name: "Expert Code Review (auto)"
description: "Automatically runs the expert-reviewer agent when a PR is opened or marked ready for review."

on:
  pull_request:
    types: [opened, ready_for_review]
    paths-ignore:
      - '*.md'
      - 'docs/**'
      - 'eng/common/**'
      - 'LICENSE'
      - 'THIRD-PARTY-NOTICES.txt'
  roles: [admin, maintainer, write]

permissions:
  contents: read
  pull-requests: read

engine:
  id: copilot
  model: claude-opus-4.6

network:
  allowed:
    - defaults
    - dotnet

imports:
  - shared/review-shared.md

timeout-minutes: 90
---

<!-- Orchestration instructions are in shared/review-shared.md -->
