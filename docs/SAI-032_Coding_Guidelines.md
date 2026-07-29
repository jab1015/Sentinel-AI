# SAI-032 — Coding Guidelines

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document establishes the coding guidelines for Sentinel AI.

Its objective is to ensure that every line of code written for the project is consistent, maintainable, readable, secure, and production-ready.

These guidelines apply to all contributors and all future development.

---

# Engineering Philosophy

Code should be written for the next engineer—not just the current one.

The preferred order of priorities is:

1. Correctness
2. Readability
3. Maintainability
4. Security
5. Performance
6. Optimization

Readable code is almost always preferred over clever code.

---

# General Principles

Every class should:

- Have one responsibility.
- Have a clearly defined purpose.
- Be easy to test.
- Be easy to extend.
- Be easy to understand.

Every method should:

- Perform one task.
- Be short whenever practical.
- Return predictable results.
- Handle errors appropriately.

---

# Naming Standards

## Classes

Use PascalCase.

Examples

```
MonitoringEngine
SystemSnapshot
ProcessMonitor
```

---

## Methods

Use PascalCase.

Examples

```
RefreshAsync()
CollectSnapshot()
CalculateCpuUsage()
```

---

## Properties

Use PascalCase.

Examples

```
CpuUsage
MemoryUsage
ThreatScore
```

---

## Private Fields

Use underscore camelCase.

Examples

```
_monitoringEngine
_refreshTimer
_currentSnapshot
```

---

## Constants

Use PascalCase.

Examples

```
DefaultRefreshInterval
MaximumHistoryItems
```

---

## Interfaces

Prefix with I.

Examples

```
IMonitor
ILogger
IThreatAnalyzer
```

---

# Method Design

Methods should:

- Perform one operation.
- Avoid excessive nesting.
- Exit early when appropriate.
- Avoid side effects whenever possible.

Prefer:

```
Validate

↓

Process

↓

Return
```

---

# Class Design

Each class should:

- Have a single responsibility.
- Minimize dependencies.
- Hide implementation details.
- Expose a clean public API.

---

# Asynchronous Code

Use async/await for:

- Monitoring
- File operations
- Network operations
- Windows API calls that may block

Avoid:

- Blocking the UI thread
- Task.Result
- Task.Wait()

---

# Exception Handling

Handle expected failures.

Do not silently ignore exceptions.

When catching exceptions:

- Recover if possible.
- Log appropriately.
- Return safe values.
- Preserve application stability.

---

# Dependency Rules

Allowed

```
UI

↓

Monitoring Engine

↓

Monitor Services

↓

Windows APIs
```

Not Allowed

```
UI

↓

Windows APIs
```

---

# Comments

Write comments that explain:

- Why something exists.
- Why an unusual implementation is required.
- Important architectural decisions.

Avoid comments that simply repeat the code.

Bad

```csharp
// Increment i
i++;
```

Good

```csharp
// Required because Windows reports processor time cumulatively.
```

---

# Error Messages

Error messages should:

- Be clear.
- Be actionable.
- Avoid exposing implementation details.

---

# Performance Guidelines

Prefer:

- Efficient algorithms
- Strong typing
- Asynchronous operations
- Minimal allocations

Avoid premature optimization.

Measure performance before optimizing.

---

# Security Guidelines

Always:

- Validate external input.
- Use official Microsoft APIs.
- Follow least privilege.
- Protect sensitive information.

Never:

- Hard-code secrets.
- Expose internal exceptions.
- Trust external data without validation.

---

# Code Reviews

Every review should verify:

- Correctness
- Readability
- Architecture compliance
- Error handling
- Security
- Maintainability

---

# Definition of Quality

Quality code is:

- Correct
- Understandable
- Maintainable
- Testable
- Secure
- Production-ready

---

# Long-Term Goal

Maintain a consistent coding style that allows Sentinel AI to scale into a large, enterprise-quality codebase while remaining approachable for future contributors and maintainers.

---

End of Document