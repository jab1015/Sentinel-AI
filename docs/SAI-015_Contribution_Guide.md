# SAI-015 — Contribution Guide

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document defines the standards for contributing to the Sentinel AI codebase.

Its objective is to ensure that all code, documentation, testing, and architectural changes maintain a consistent level of quality regardless of the contributor.

---

# Core Principles

Every contribution should be:

- Correct
- Readable
- Testable
- Maintainable
- Secure
- Production-ready

---

# Before Writing Code

Contributors should:

- Review the applicable architecture documents.
- Understand the feature requirements.
- Verify dependencies.
- Avoid duplicate implementations.
- Reuse existing services whenever practical.

---

# Coding Standards

Follow established project conventions:

- PascalCase for public members.
- Meaningful class and method names.
- Small, focused methods.
- Single Responsibility Principle.
- Asynchronous APIs for long-running operations.
- Prefer composition over duplication.

---

# File Organization

Application code belongs under:

```
src/
```

Project documentation belongs under:

```
docs/
```

Tests belong under:

```
tests/
```

Generated artifacts should never be committed unless explicitly required.

---

# Documentation Requirements

Documentation should be updated whenever:

- Architecture changes.
- Public APIs change.
- New components are introduced.
- User workflows change.
- Build requirements change.

Minor internal refactoring does not require documentation updates unless behavior changes.

---

# Testing Requirements

Before submitting changes:

- Solution builds successfully.
- Application launches successfully.
- New functionality is manually verified.
- Existing functionality is not broken.

When automated tests exist, they should also pass before merging.

---

# Code Review Checklist

Reviewers should verify:

- Correctness
- Readability
- Performance
- Security
- Error handling
- Thread safety
- Consistency with project architecture

---

# Commit Standards

Commits should:

- Represent a logical unit of work.
- Build successfully.
- Avoid unrelated changes.
- Use descriptive commit messages.

Example:

```
Sprint 3: Implement native CPU monitoring
```

---

# Pull Request Guidelines

Each pull request should include:

- Summary of changes
- Reason for the change
- Testing performed
- Documentation updated (if applicable)
- Known limitations

---

# Definition of Done

A task is considered complete when:

- Requirements satisfied
- Code reviewed
- Solution builds successfully
- Application runs successfully
- Documentation updated
- Changes committed
- Changes pushed

---

# Long-Term Goal

Maintain a clean, professional, and scalable codebase that supports long-term product growth while minimizing technical debt and simplifying future maintenance.

---

End of Document