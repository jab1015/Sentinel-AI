# SAI-004 — System Architecture Document

**Document ID:** SAI-004  
**Title:** System Architecture Document  
**Version:** 1.0  
**Status:** Approved (Working Draft)  
**Project:** Sentinel AI

---

# Revision History

| Version | Date | Author | Description |
|---------|------|--------|-------------|
| 1.0 | 2026-07-28 | Sentinel AI Team | Initial Release |

---

# 1. Purpose

This document defines the software architecture for Sentinel AI.

Its purpose is to provide a stable, scalable, maintainable architecture capable of supporting future features without major redesign.

---

# 2. Architectural Goals

The architecture shall prioritize:

- Reliability
- Performance
- Modularity
- Security
- Testability
- Scalability
- Maintainability

---

# 3. High-Level Architecture

Sentinel AI is composed of independent modules communicating through clearly defined interfaces.

```
┌──────────────────────────────┐
│          Sentinel AI         │
└──────────────┬───────────────┘
               │
 ┌─────────────┴───────────────────────────┐
 │                                         │
 ▼                                         ▼
Presentation Layer                  Core Services
 │                                         │
 ▼                                         ▼
Monitoring Engine               Threat Analysis Engine
 │                                         │
 ▼                                         ▼
Windows APIs                    AI Analysis Engine
 │                                         │
 ▼                                         ▼
SQLite Database                 Notification Service
```

---

# 4. Solution Structure

```
SentinelAI/

docs/

src/

Sentinel.UI/

Sentinel.Core/

Sentinel.Monitoring/

Sentinel.Network/

Sentinel.Process/

Sentinel.Security/

Sentinel.Firewall/

Sentinel.AI/

Sentinel.Database/

Sentinel.Logging/

Sentinel.Notifications/

Sentinel.Update/

tests/

Sentinel.Tests/

installer/

assets/
```

---

# 5. Layered Architecture

## Presentation Layer

Responsibilities:

- Dashboard
- Windows
- Dialogs
- Charts
- User interaction

No business logic shall exist in the UI layer.

---

## Application Layer

Responsibilities:

- Coordination
- Commands
- Navigation
- Dependency Injection
- Configuration

---

## Domain Layer

Contains:

- Threat models
- Security logic
- Risk calculations
- Business rules

This layer shall remain independent of UI technology.

---

## Infrastructure Layer

Provides:

- Database access
- Windows API access
- Logging
- Networking
- File system access

---

# 6. Major Components

## Sentinel.UI

Responsibilities:

- Dashboard
- Settings
- Event Viewer
- Threat Viewer
- Timeline
- Reports

---

## Sentinel.Core

Responsibilities:

- Configuration
- Dependency Injection
- Startup
- Global services

---

## Sentinel.Monitoring

Responsible for:

- CPU
- RAM
- Disk
- GPU
- Services
- Startup entries

---

## Sentinel.Process

Responsible for:

- Process enumeration
- Parent-child relationships
- Executable metadata
- Digital signatures

---

## Sentinel.Network

Responsible for:

- TCP monitoring
- UDP monitoring
- Connections
- Listening ports
- Bandwidth

---

## Sentinel.Security

Responsible for:

- Defender status
- Firewall status
- SmartScreen
- Secure Boot
- BitLocker

---

## Sentinel.Firewall

Responsibilities:

- Read firewall status
- Create rules
- Remove rules
- Temporary blocking
- Rule management

---

## Sentinel.AI

Responsible for:

- AI explanations
- Threat summaries
- Recommendations
- Natural language interaction

---

## Sentinel.Database

Responsible for:

- SQLite
- Entity Framework
- Migrations
- Data persistence

---

## Sentinel.Logging

Responsibilities:

- JSON logs
- Application logs
- Error logs
- Audit logs

---

## Sentinel.Notifications

Responsible for:

- Toast notifications
- Alerts
- Warning dialogs
- Notification history

---

## Sentinel.Update

Responsible for:

- Update detection
- Download
- Verification
- Installation

---

# 7. Windows Integration

Sentinel AI shall integrate with:

- Windows Management Instrumentation (WMI)
- Event Tracing for Windows (ETW)
- Windows Firewall APIs
- Windows Security Center APIs
- Performance Counters
- Windows Event Log
- Windows Registry
- Task Scheduler
- Windows Services

All integrations should use documented Microsoft APIs whenever available.

---

# 8. Data Flow

```
Windows

↓

Monitoring Engine

↓

Event Processor

↓

Threat Engine

↓

AI Analysis

↓

SQLite Database

↓

User Interface

↓

Notifications
```

---

# 9. Dependency Injection

The application shall use Microsoft's built-in Dependency Injection framework.

Benefits include:

- Easier testing
- Loose coupling
- Better maintainability
- Cleaner architecture

---

# 10. Database Architecture

Primary storage:

SQLite

ORM:

Entity Framework Core

Database responsibilities:

- Store events
- Store alerts
- Store threats
- Store settings
- Store reports

---

# 11. Logging Architecture

All components shall use centralized structured logging.

Each log entry shall include:

- Timestamp
- Severity
- Component
- Event ID
- Message
- Exception
- Thread ID

Log levels:

- Trace
- Debug
- Information
- Warning
- Error
- Critical

---

# 12. Security Architecture

Security responsibilities include:

- Code signing verification
- Secure configuration storage
- Integrity validation
- Tamper detection
- Least-privilege execution
- Secure update verification

---

# 13. Threading Model

Sentinel AI shall use asynchronous programming where appropriate.

Background tasks include:

- Monitoring
- Database writes
- AI analysis
- Update checks
- Threat analysis

The UI thread shall never perform long-running operations.

---

# 14. Error Handling

Every component shall:

- Catch recoverable exceptions.
- Log failures.
- Continue operating whenever safe.
- Notify the user only when appropriate.

Unexpected failures shall not terminate the monitoring engine.

---

# 15. Scalability

The architecture shall support future additions including:

- Enterprise edition
- Multi-device management
- Cloud synchronization
- Plugin architecture
- Additional AI providers
- Linux support (future)
- macOS support (future)

No redesign should be required for these additions.

---

# 16. Performance Goals

Startup:

Less than 5 seconds

Dashboard refresh:

Less than 1 second

Idle CPU:

Less than 1%

Idle RAM:

Less than 200 MB

SQLite writes:

Less than 10 ms average

---

# 17. Coding Standards

The architecture requires:

- SOLID principles
- Dependency Injection
- Async/Await
- MVVM
- Unit testing
- Code reviews
- XML documentation
- Nullable reference types enabled

---

# 18. Architectural Constraints

The following technologies are mandatory:

Language:

- C#

Framework:

- .NET 8 LTS (or current LTS)

UI:

- WinUI 3

Database:

- SQLite

Logging:

- Serilog

Testing:

- xUnit

Version Control:

- Git

Repository Hosting:

- GitHub

---

# 19. Future Architecture

Future releases may include:

- Local AI models
- Cloud AI providers
- Plugin SDK
- REST API
- Mobile companion app
- Enterprise console
- Cross-device synchronization

The modular architecture is intended to accommodate these capabilities without major refactoring.

---

# 20. Conclusion

This architecture provides a stable foundation for Sentinel AI by separating responsibilities into independent, testable components.

Future enhancements should be implemented as new modules whenever possible rather than increasing coupling between existing components.

Maintaining architectural discipline is essential to ensuring that Sentinel AI remains scalable, maintainable, and reliable throughout its lifecycle.

---

# End of Document

**Document ID:** SAI-004  
**Version:** 1.0  
**Status:** Approved (Working Draft)