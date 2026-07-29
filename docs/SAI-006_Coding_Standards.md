# SAI-006 — Coding Standards
Version: 1.0
Status: Active
Last Updated: 2026-07-28

---

# Purpose

This document defines the coding standards for Sentinel AI.

The objective is to keep the codebase consistent, readable, maintainable, and scalable throughout the life of the project.

---

# General Philosophy

Code is written for humans first and computers second.

Readable code is preferred over clever code.

Future maintainability is more important than short-term convenience.

---

# Naming Conventions

## Projects

PascalCase

Examples

Sentinel.App

Sentinel.Services

Sentinel.Security

Sentinel.AI

---

## Classes

PascalCase

Examples

SystemMonitor

ThreatScanner

NetworkMonitor

AlertService

CpuStatus

MemoryStatus

---

## Methods

PascalCase

Examples

GetCpuUsage()

UpdateDashboard()

AnalyzeThreat()

RefreshData()

---

## Properties

PascalCase

Examples

CpuUsage

MemoryUsage

ThreatLevel

SystemStatus

---

## Private Fields

Prefix with underscore.

Examples

_timer

_cpuMonitor

_logger

_refreshInterval

---

## Constants

PascalCase

Examples

DefaultRefreshRate

MaxHistoryEntries

MinimumSupportedVersion

---

# Folder Organization

Business logic belongs in Services.

Models contain data only.

Views contain UI.

ViewModels connect UI to Services.

Helpers contain reusable utilities.

Avoid placing unrelated functionality into MainWindow.xaml.cs.

---

# File Size

Target:

Less than 300 lines.

Maximum:

Approximately 500 lines.

If a file becomes too large, refactor into additional classes.

---

# Methods

Methods should perform one clear responsibility.

Prefer smaller methods over large monolithic methods.

---

# Comments

Explain WHY.

Do not explain obvious code.

Good:

// Refresh every second to provide responsive monitoring.

Avoid:

// Add one to i.

---

# Error Handling

Never silently ignore exceptions.

Log unexpected errors.

Display meaningful messages to users when appropriate.

---

# User Interface

The UI should remain responsive.

Avoid blocking the UI thread.

Long-running operations should execute asynchronously whenever practical.

---

# Performance

Avoid unnecessary allocations.

Reuse objects when appropriate.

Optimize only after measuring.

Readability comes first.

---

# Testing

Every feature should be:

Compiled

Executed

Verified

Before committing.

---

# Git Workflow

Feature completed

↓

Build

↓

Run

↓

Verify

↓

Commit

↓

Push

↓

Update Sprint History

---

# Documentation

Major architectural changes require documentation updates.

New services should be documented.

Roadmap changes should update SAI-005.

Sprint completion should update SAI-004.

---

# AI Development Guidelines

AI-generated code should:

Compile successfully.

Follow project architecture.

Prefer complete file replacements.

Avoid introducing unnecessary complexity.

Avoid deprecated Windows APIs.

Explain major architectural decisions.

---

# Code Review Checklist

Before considering work complete:

✔ Builds successfully

✔ Runs successfully

✔ Matches coding standards

✔ Documentation updated

✔ Git commit completed

✔ GitHub push completed

---

# Definition of Done

A feature is complete only when:

It builds.

It runs.

It works.

It is documented.

It is committed.

It is pushed.

---

End of Document