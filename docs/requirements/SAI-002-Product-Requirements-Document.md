# SAI-002 — Product Requirements Document (PRD)

**Document ID:** SAI-002  
**Title:** Product Requirements Document  
**Version:** 1.0  
**Status:** Approved (Working Draft)  
**Project:** Sentinel AI

---

# Revision History

| Version | Date | Author | Description |
|---------|------|--------|-------------|
| 1.0 | 2026-07-28 | Sentinel AI Team | Initial Release |

---

# 1. Executive Summary

Sentinel AI is an AI-powered Windows security platform designed to provide users with complete visibility into their computer's behavior while delivering intelligent, evidence-based security analysis in plain language.

Rather than replacing Microsoft Defender or Windows Firewall, Sentinel AI enhances existing Windows security by monitoring, analyzing, explaining, and assisting users in responding to suspicious activity.

The product emphasizes transparency, user control, privacy, and performance.

---

# 2. Product Vision

Create the most trusted AI-powered security companion for Windows by making advanced cybersecurity understandable, actionable, and accessible.

---

# 3. Product Goals

The primary goals are:

- Monitor Windows in real time.
- Detect suspicious behavior.
- Explain security events in plain English.
- Recommend appropriate actions.
- Automate defensive actions when authorized.
- Maintain low resource usage.
- Respect user privacy.

---

# 4. Target Platforms

Supported Operating Systems:

- Windows 10 (22H2 or later)
- Windows 11
- 64-bit only

Future Considerations:

- Windows Server
- Enterprise Edition
- Managed Business Deployments

---

# 5. Target Users

## Consumer Users

Users wanting a clearer understanding of their computer's security.

Examples:

- Families
- Students
- Professionals
- Gamers
- Home users

---

## Power Users

Users interested in advanced visibility.

Examples:

- Developers
- IT professionals
- Security enthusiasts
- System administrators

---

## Enterprise (Future)

Businesses requiring centralized monitoring and reporting.

---

# 6. Product Objectives

Sentinel AI shall:

- Monitor system activity continuously.
- Monitor network activity.
- Monitor running processes.
- Monitor startup entries.
- Monitor registry persistence.
- Monitor scheduled tasks.
- Monitor services.
- Monitor Windows Security.
- Monitor Defender status.
- Monitor Firewall status.
- Monitor USB devices.
- Maintain historical records.
- Generate intelligent recommendations.

---

# 7. Functional Requirements

## Dashboard

The application shall display:

- Overall security score
- System health
- CPU usage
- Memory usage
- Disk usage
- Network activity
- Active threats
- Defender status
- Firewall status

---

## Process Monitoring

Sentinel AI shall:

- Enumerate running processes.
- Display process path.
- Display publisher.
- Display digital signature.
- Display parent process.
- Detect newly started processes.
- Detect terminated processes.

---

## Network Monitoring

Sentinel AI shall display:

- Active TCP connections
- Active UDP endpoints
- Listening ports
- Remote IP addresses
- Local IP addresses
- Process ownership
- Connection duration
- Bytes sent
- Bytes received

---

## Windows Security Monitoring

Monitor:

- Microsoft Defender
- Windows Firewall
- Windows Update
- Secure Boot
- SmartScreen
- BitLocker status

---

## Event Timeline

Every important event shall be recorded.

Examples:

- New process started
- New connection created
- Firewall disabled
- Defender disabled
- USB inserted
- Startup modified
- Scheduled task added

---

# 8. Threat Detection

Sentinel AI shall evaluate:

- Unsigned executables
- Executables in temporary folders
- PowerShell activity
- Command Prompt activity
- Script execution
- Registry persistence
- Startup persistence
- Scheduled tasks
- Services
- Unusual network activity
- Suspicious parent-child processes

Each threat shall include:

- Risk Score
- Confidence Score
- Evidence
- Recommended Action

---

# 9. AI Assistant

The AI assistant shall answer questions such as:

- Why was this blocked?
- Is this safe?
- What happened today?
- Why is my computer slow?
- Explain this process.
- Explain this network connection.
- Show suspicious events this week.

AI explanations shall always distinguish between facts, observations, and recommendations.

---

# 10. Notifications

Notification levels:

- Information
- Recommendation
- Warning
- Critical

Users may customize notification behavior.

---

# 11. Data Storage

Sentinel AI shall store:

- Events
- Alerts
- Connection history
- Process history
- Threat history
- User settings

Primary database:

SQLite

Structured logs:

JSON

---

# 12. Performance Requirements

Idle CPU:

Less than 1%

Idle RAM:

Less than 200 MB

Startup:

Less than 5 seconds

Dashboard refresh:

Less than 1 second

---

# 13. Security Requirements

Sentinel AI shall:

- Use signed binaries.
- Validate updates.
- Encrypt sensitive configuration data.
- Protect stored credentials.
- Detect tampering.
- Support secure logging.

---

# 14. Privacy Requirements

Sentinel AI shall:

- Operate locally by default.
- Require consent for cloud features.
- Never sell user data.
- Clearly document telemetry.
- Allow users to export and delete stored data.

---

# 15. User Interface Requirements

The interface shall:

- Be modern.
- Be responsive.
- Use Fluent Design.
- Support light mode.
- Support dark mode.
- Be keyboard accessible.
- Scale correctly on high-DPI displays.

---

# 16. Release Strategy

## MVP

- Dashboard
- Process Monitor
- Network Monitor
- Defender Status
- Firewall Status
- SQLite Database
- Event Timeline
- Logging

---

## Version 1.1

- Threat Intelligence
- AI Explanations
- Reputation Checking

---

## Version 1.2

- Automatic Firewall Rules
- Registry Monitoring
- Startup Monitoring

---

## Version 2.0

- AI Security Analyst
- Natural Language Search
- Home Network Monitoring
- USB Protection
- Behavioral Learning

---

# 17. Success Metrics

Success shall be measured by:

- Low false positive rate
- Low CPU usage
- Fast startup
- User satisfaction
- Threat detection accuracy
- Stability
- Minimal crashes

---

# 18. Out of Scope (MVP)

The following features are intentionally excluded from the MVP:

- Antivirus engine
- Cloud management
- Enterprise console
- Mobile applications
- Linux support
- macOS support

These may be considered in future releases.

---

# 19. Risks

Potential risks include:

- Windows API changes
- Performance regressions
- False positives
- User fatigue from excessive alerts
- Third-party software conflicts

Mitigation strategies shall be documented in the Risk Register.

---

# 20. Acceptance Criteria

The MVP shall be considered complete when it can:

- Launch successfully on Windows 10 and Windows 11.
- Display live system health.
- Display running processes.
- Display live network connections.
- Display Defender status.
- Display Firewall status.
- Record events in SQLite.
- Maintain structured logs.
- Display a live event timeline.
- Operate continuously without crashes during extended testing.

---

# End of Document

**Document ID:** SAI-002  
**Version:** 1.0  
**Status:** Approved (Working Draft)