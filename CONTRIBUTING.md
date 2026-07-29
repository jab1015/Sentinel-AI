# Contributing to Sentinel AI

Thank you for your interest in contributing to Sentinel AI.

This project follows a professional software engineering workflow focused on quality, maintainability, and incremental development.

---

# Project Goals

Sentinel AI is being developed as an AI-powered Windows desktop application that helps users monitor, understand, and improve the health and security of their computers.

Contributors should prioritize:

- Reliability
- Readability
- Maintainability
- User experience
- Security
- Performance

---

# Before You Begin

Before making changes, review the following documentation:

1. docs/SAI-000_Project_Status.md
2. docs/SAI-001_Project_Constitution.md
3. docs/SAI-002_Development_Rules.md
4. docs/SAI-003_Architecture.md

These documents explain the project's current status, architecture, and development expectations.

---

# Development Workflow

Every feature should follow this workflow:

1. Review the current project status.
2. Plan the implementation.
3. Make focused code changes.
4. Build the project.
5. Run the application.
6. Verify the feature works as intended.
7. Update documentation if needed.
8. Commit changes.
9. Push changes.

The project should remain in a buildable state after every completed task.

---

# Coding Standards

Contributors are expected to follow the standards defined in:

docs/SAI-006_Coding_Standards.md

Highlights include:

- Prefer readable code.
- Use descriptive names.
- Keep methods focused on a single responsibility.
- Avoid unnecessary complexity.
- Prefer modern .NET APIs.
- Keep the UI responsive.

---

# Project Organization

Business logic belongs in:

Services/

UI belongs in:

Views/

Data objects belong in:

Models/

Reusable utilities belong in:

Helpers/

Avoid placing new business logic directly into MainWindow.xaml.cs unless it is temporary during early development.

---

# Pull Requests

If this repository accepts external contributions in the future:

- Keep pull requests focused on a single feature or bug fix.
- Include a clear description of the changes.
- Reference related issues when applicable.
- Ensure the project builds successfully before submitting.

---

# Documentation

Documentation is considered part of the codebase.

When making significant changes:

- Update the appropriate SAI document(s).
- Update CHANGELOG.md for user-visible changes.
- Update SAI-004_Sprint_History.md with engineering details.
- Update SAI-000_Project_Status.md if the current sprint or project status changes.

---

# Definition of Done

A task is complete only when:

- The project builds successfully.
- The application runs successfully.
- Acceptance criteria are met.
- Documentation is updated.
- Changes are committed.
- Changes are pushed to GitHub.

---

# Reporting Issues

When reporting a bug, include:

- Sentinel AI version
- Windows version
- Steps to reproduce
- Expected behavior
- Actual behavior
- Screenshots or logs (if available)

---

# Feature Requests

Feature requests should include:

- Problem statement
- Proposed solution
- Expected user benefit
- Potential implementation considerations (if known)

---

# Branch Strategy

Primary branch:

main

Future enhancements may use short-lived feature branches that are merged after review and successful testing.

---

# License

By contributing to Sentinel AI, you agree that your contributions are subject to the project's license.

---

Thank you for helping improve Sentinel AI.