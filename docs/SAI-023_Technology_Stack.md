# SAI-023 — Technology Stack

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document defines the approved technology stack for Sentinel AI.

It serves as the authoritative reference for frameworks, libraries, languages, and development tools used throughout the project.

---

# Design Goals

The technology stack should prioritize:

- Long-term Microsoft support
- Native Windows integration
- High performance
- Maintainability
- Security
- Extensibility

---

# Application Platform

Application Type

- Windows Desktop Application

Framework

- .NET 8

User Interface

- WinUI 3

Language

- C#

IDE

- Visual Studio 2022

---

# Operating System Support

Primary

- Windows 11

Secondary

- Windows 10 (where supported)

Architecture

- x64

Future

- ARM64 evaluation

---

# Windows Integration

Preferred APIs

- Win32 APIs
- Windows Runtime (WinRT)
- Windows Management Instrumentation (WMI), where appropriate
- Microsoft-supported Windows SDK APIs

Interop

- CsWin32 source-generated bindings
- P/Invoke only when required

---

# Data Models

Primary Format

- Strongly typed C# models

Serialization

- System.Text.Json

Configuration

- JSON configuration files

---

# Asynchronous Programming

Preferred Pattern

- async/await

Scheduling

- Task-based asynchronous programming

UI Thread

- Never block the UI thread

---

# Dependency Management

Package Manager

- NuGet

Approved Sources

- Microsoft packages
- Official vendor packages
- Well-maintained open-source libraries after review

Avoid unnecessary third-party dependencies.

---

# Logging

Current

- Basic diagnostic logging

Future

- Microsoft.Extensions.Logging

Potential Targets

- File
- Windows Event Log
- Structured logging providers

---

# Testing

Framework

- xUnit

Mocking

- Microsoft-compatible mocking framework

Future

- UI automation testing
- Integration testing
- Performance benchmarking

---

# Version Control

Repository

- Git

Hosting

- GitHub

Branch Strategy

- Main
- Feature branches
- Release branches (future)

---

# Build System

Build Tool

- MSBuild

Configuration

- Debug
- Release

Future

- GitHub Actions
- Automated CI/CD

---

# Installer

Current

- Manual packaging

Planned

- MSIX installer
- Signed installer
- Automatic update support

---

# Security

Development Practices

- Least privilege
- Secure coding standards
- Official Microsoft APIs
- Input validation
- Exception handling

Future

- Code signing
- Secure update verification
- Enterprise policy support

---

# AI Roadmap

Future Components

- Threat Analysis Engine
- Recommendation Engine
- Explainable AI
- Confidence Scoring
- Local AI model evaluation
- Enterprise intelligence integrations

---

# Guiding Principle

Technology choices should favor long-term stability, Microsoft ecosystem compatibility, and production-quality engineering over short-term convenience.

---

End of Document