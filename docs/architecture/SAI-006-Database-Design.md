# SAI-006 — Database Design Document

**Document ID:** SAI-006  
**Title:** Database Design Document  
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

This document defines the database architecture for Sentinel AI.

The database is responsible for storing system events, security alerts, threat assessments, application settings, monitoring history, and user configuration while maintaining high performance and data integrity.

---

# 2. Database Technology

Primary Database

- SQLite

Object Relational Mapper

- Entity Framework Core

Database Versioning

- EF Core Migrations

---

# 3. Database Goals

The database shall:

- Be lightweight.
- Require no server installation.
- Support millions of events.
- Maintain referential integrity.
- Support rapid searches.
- Support historical analysis.
- Support future expansion.

---

# 4. Database Structure

The initial database shall contain the following tables.

| Table | Purpose |
|--------|---------|
| Events | Stores all monitored events |
| Alerts | Stores user alerts |
| Threats | Stores threat assessments |
| Processes | Stores process history |
| NetworkConnections | Stores connection history |
| Devices | Stores hardware devices |
| Settings | Stores application settings |
| Users | Reserved for future multi-user support |
| Reports | Stores generated reports |
| AuditLog | Stores internal audit events |

---

# 5. Events Table

Purpose:

Stores every monitored event.

Columns

| Column | Type |
|---------|------|
| EventId | INTEGER (PK) |
| Timestamp | DATETIME |
| EventType | TEXT |
| Severity | INTEGER |
| Source | TEXT |
| Description | TEXT |
| ProcessId | INTEGER |
| ThreatId | INTEGER (Nullable) |

Indexes

- Timestamp
- EventType
- Severity

---

# 6. Alerts Table

Purpose

Stores notifications presented to users.

Columns

| Column | Type |
|---------|------|
| AlertId | INTEGER (PK) |
| EventId | INTEGER |
| Created | DATETIME |
| Level | INTEGER |
| Title | TEXT |
| Message | TEXT |
| Acknowledged | BOOLEAN |

Indexes

- Created
- Level

---

# 7. Threats Table

Purpose

Stores threat analysis results.

Columns

| Column | Type |
|---------|------|
| ThreatId | INTEGER (PK) |
| RiskScore | INTEGER |
| ConfidenceScore | INTEGER |
| Classification | TEXT |
| Recommendation | TEXT |
| Evidence | TEXT |
| Created | DATETIME |

Indexes

- RiskScore
- Classification

---

# 8. Processes Table

Purpose

Maintains process history.

Columns

| Column | Type |
|---------|------|
| ProcessId | INTEGER (PK) |
| Name | TEXT |
| ExecutablePath | TEXT |
| Publisher | TEXT |
| DigitalSignature | TEXT |
| ParentProcess | INTEGER |
| FirstSeen | DATETIME |
| LastSeen | DATETIME |

Indexes

- Name
- ExecutablePath

---

# 9. NetworkConnections Table

Purpose

Stores historical network connections.

Columns

| Column | Type |
|---------|------|
| ConnectionId | INTEGER (PK) |
| ProcessId | INTEGER |
| LocalAddress | TEXT |
| LocalPort | INTEGER |
| RemoteAddress | TEXT |
| RemotePort | INTEGER |
| Protocol | TEXT |
| State | TEXT |
| FirstSeen | DATETIME |
| LastSeen | DATETIME |

Indexes

- RemoteAddress
- LocalPort
- ProcessId

---

# 10. Devices Table

Purpose

Stores connected hardware devices.

Columns

| Column | Type |
|---------|------|
| DeviceId | INTEGER (PK) |
| DeviceName | TEXT |
| DeviceType | TEXT |
| Manufacturer | TEXT |
| Connected | DATETIME |
| Removed | DATETIME (Nullable) |

---

# 11. Settings Table

Purpose

Stores user preferences.

Columns

| Column | Type |
|---------|------|
| SettingKey | TEXT (PK) |
| SettingValue | TEXT |
| Updated | DATETIME |

Examples

- Theme
- NotificationLevel
- AutoBlockEnabled
- DatabaseRetentionDays
- AIProvider

---

# 12. Reports Table

Purpose

Stores metadata for generated reports.

Columns

| Column | Type |
|---------|------|
| ReportId | INTEGER (PK) |
| Name | TEXT |
| Type | TEXT |
| Generated | DATETIME |
| FilePath | TEXT |

---

# 13. AuditLog Table

Purpose

Stores internal Sentinel AI actions.

Examples

- Settings changed
- Database upgraded
- Threat rule added
- Update installed
- Firewall rule created

Columns

| Column | Type |
|---------|------|
| AuditId | INTEGER (PK) |
| Timestamp | DATETIME |
| Action | TEXT |
| User | TEXT |
| Details | TEXT |

---

# 14. Relationships

Events

↓

Threats

↓

Alerts

Processes

↓

Network Connections

Settings

↓

Application Configuration

AuditLog

↓

Internal Operations

---

# 15. Data Retention

Default retention:

Events

365 Days

Logs

180 Days

Threat History

Unlimited

Reports

Unlimited

Retention shall be configurable.

---

# 16. Backup Strategy

Sentinel AI shall support:

- Manual backup
- Automatic backup
- Database export
- Database import
- Integrity verification

Backup format:

SQLite database file

---

# 17. Performance Requirements

Database queries shall return:

Dashboard

<100 ms

Search

<500 ms

Timeline

<250 ms

Database startup

<1 second

---

# 18. Future Tables

Future releases may include:

- Threat Intelligence
- AI Conversations
- Quarantine
- Firewall Rules
- USB History
- Browser Extensions
- Startup Changes
- Registry History
- Scheduled Tasks
- Services
- Certificates
- DNS Cache
- Login History

---

# 19. Migration Strategy

All schema changes shall:

- Use Entity Framework Core migrations.
- Be version controlled.
- Support rollback where practical.
- Preserve user data.

---

# 20. Database Integrity

The database shall enforce:

- Primary keys
- Foreign keys
- Transactions
- Referential integrity
- Index optimization
- Automatic integrity checks

Database corruption shall be detected and reported.

---

# 21. Security

The database shall:

- Store only required information.
- Protect sensitive settings.
- Support encrypted configuration values.
- Validate database integrity during startup.

---

# 22. Conclusion

The Sentinel AI database has been designed to provide high performance, scalability, and long-term maintainability while supporting future product expansion.

The modular schema allows additional monitoring capabilities to be introduced without major architectural changes.

---

# End of Document

**Document ID:** SAI-006  
**Version:** 1.0  
**Status:** Approved (Working Draft)