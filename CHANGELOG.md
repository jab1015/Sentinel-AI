# Changelog

All notable changes to Sentinel AI are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning where practical.

---

# [Unreleased]

## In Progress

- Bind existing disk metrics to the dashboard
- Verify disk values at runtime
- Implement live network download throughput
- Implement live network upload throughput
- Display process count and expand process intelligence
- Implement Microsoft Defender operational status
- Implement Windows Firewall operational status
- Add monitoring integration and failure-path tests

## Known Limitations

- The dashboard currently displays CPU, memory, and timestamp only
- Disk values are collected by MonitoringEngine but are not rendered by MainWindow
- Download and upload values are currently assigned zero
- Process count is collected but is not rendered by MainWindow
- Defender and Firewall checks are not yet complete production status implementations

---

# [0.3.0] - 2026-07-29

## Added

### Native Windows Monitoring

- Microsoft.Windows.CsWin32 integration
- `NativeMethods.txt`
- Native `GetSystemTimes` binding
- Native `GlobalMemoryStatusEx` binding
- Production CPU utilization sampling
- Production physical-memory used, total, and percentage reporting
- Graceful Win32 failure handling

### Dashboard

- Real CPU utilization display
- Real physical-memory display
- One-second monitoring refresh
- Live last-updated timestamp

## Changed

- Replaced placeholder and random CPU values with native Windows data
- Preserved the existing SystemMonitor interface for MonitoringEngine call sites
- Updated project status, sprint history, roadmap, implementation tracker, README, and release checklist

## Fixed

- CsWin32 FILETIME compatibility
- Incorrect physical-memory reporting
- CPU sampling edge cases for first, invalid, and reversed samples

## Verified

- Solution builds successfully
- Application launches successfully
- CPU values update at runtime
- Physical-memory values update at runtime
- Timestamp updates once per second
- Runtime behavior verified by the Product Owner

---

# [0.2.0] - Architecture and Documentation Foundation

## Added

### Documentation

- Project status
- Project Constitution
- Development Rules
- Software Architecture
- Sprint History
- Product Roadmap
- Coding Standards
- Chat Continuation Guide
- Release Checklist
- Implementation Tracker

### Application

- MonitoringEngine
- SystemSnapshot
- Monitor-service architecture
- Initial SystemMonitor
- DiskMonitor
- NetworkMonitor
- ProcessMonitor
- SecurityMonitor
- WindowsInfoMonitor
- DispatcherTimer refresh engine

### Development

- Git repository
- GitHub connection
- `main` production branch
- Standardized development workflow

---

# [0.1.0] - Initial Foundation

## Added

- Sentinel AI solution
- WinUI 3 application
- Initial dashboard
- Initial repository structure
- Successful build configuration

---

# Version Numbering

Sentinel AI uses semantic versioning where practical.

Version format:

`Major.Minor.Patch`

- Major: breaking architectural change or commercial release
- Minor: new features and capabilities
- Patch: bug fixes and maintenance updates

---

# Release Philosophy

Every release should:

- Build successfully
- Launch successfully
- Replace placeholders with verified behavior
- Preserve existing functionality
- Improve user value
- Maintain code quality
- Synchronize documentation
- Be committed and pushed to `main`

---

End of Changelog