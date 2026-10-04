---
name: grill-me
description: A user-invoked entry point for pressure-testing a plan, idea, or design before implementation.
disable-model-invocation: true
---

Call the Skill tool with "grilling".

Pass the user's plan, idea, or design to the sibling `grilling` Skill and let it own the interview. The sibling asks every ready frontier in each round, includes a recommendation for each question, and waits for the user's answers before recomputing the next frontier.

Completion criterion: do not implement or declare the planning session complete until `grilling` reports an empty frontier and the user confirms the resulting shared-understanding synthesis.
