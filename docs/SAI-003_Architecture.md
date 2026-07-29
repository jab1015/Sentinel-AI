# SAI-003 — Software Architecture
Version: 1.0
Status: Active
Last Updated: 2026-07-28

---

# Purpose

This document defines the architecture of Sentinel AI.

It serves as the blueprint for all future development.

---

# Architecture Philosophy

Sentinel AI uses a layered architecture.

Each layer has a single responsibility.

This improves:

- Maintainability
- Testability
- Scalability
- Readability

---

# High-Level Architecture

┌────────────────────────────┐
│        User Interface       │
├────────────────────────────┤
│         View Models         │
├────────────────────────────┤
│          Services           │
├────────────────────────────┤
│       Windows APIs          │
├────────────────────────────┤
│      Artificial Intelligence│
├────────────────────────────┤
│      Local Data Storage     │
└────────────────────────────┘

---

# Repository Structure

Sentinel AI

docs/

assets/

installer/

src/

tests/

---

# Solution Structure

SentinelAI

Sentinel.App

Sentinel.App (Package)

Future Projects

Sentinel.Core

Sentinel.Services

Sentinel.Security

Sentinel.AI

Sentinel.Data

Sentinel.Tests

---

# Current Folder Structure

Sentinel.App

Assets/

Services/

MainWindow.xaml

MainWindow.xaml.cs

App.xaml

App.xaml.cs

---

# Planned Folder Structure

Sentinel.App

Assets/

Services/

Models/

ViewModels/

Views/

Helpers/

Resources/

Themes/

---

# Services

Services contain business logic.

Examples:

SystemMonitor

CpuMonitor

MemoryMonitor

DiskMonitor

NetworkMonitor

ProcessMonitor

DefenderMonitor

ThreatScanner

AIAnalysisService

AlertService

UpdateService

---

# Models

Models represent data.

Examples:

SystemStatus

CpuStatus

MemoryStatus

DiskStatus

NetworkStatus

ThreatReport

Alert

Recommendation

---

# Views

Views contain user interface.

Examples:

Dashboard

Threat Center

History

Settings

AI Analysis

Notifications

About

---

# View Models

ViewModels connect Views and Services.

Responsibilities:

- UI state
- Commands
- Data binding
- View updates

---

# Helpers

Utility classes.

Examples:

Logger

Configuration

Extensions

Formatting

Time Utilities

---

# AI Layer

Future AI responsibilities:

Threat detection

Security recommendations

Event correlation

Risk scoring

Natural language explanations

Root cause analysis

Auto-remediation suggestions

---

# Security Layer

Future capabilities:

Windows Defender

Firewall

SmartScreen

Windows Security Center

Running processes

Startup applications

Scheduled tasks

Registry monitoring

Network monitoring

Event logs

---

# Data Storage

Future options:

SQLite

Encrypted local configuration

Application settings

Historical monitoring data

Threat history

---

# Design Principles

Single Responsibility

Loose Coupling

High Cohesion

Dependency Injection

Modern .NET Practices

Clean Code

---

# Current Version

Version 0.2.0

Current Sprint

Sprint 2

Live Monitoring Dashboard

---

End of Document