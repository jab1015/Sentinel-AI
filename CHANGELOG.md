# Changelog

All notable changes to Sentinel AI are documented in this file.

---

# [Unreleased]

## In Progress

- Windows Event Log monitoring
- Security-event classification
- Suspicious-process indicators
- Startup application monitoring
- Service-health monitoring
- Monitoring integration and failure-path tests
- Alerting and notification foundation

---

# [0.4.0] - 2026-07-29

## Added

### Core Monitoring

- Live system-drive used, total, and percentage reporting
- Live network download throughput
- Live network upload throughput
- Running process count
- Highest-memory process identification
- Highest-memory process usage reporting
- Microsoft Defender enabled status
- Windows Firewall enabled status
- Security status dashboard row

### Dashboard

- Disk metrics connected to the live dashboard
- Network metrics connected to the live dashboard
- Process metrics connected to the live dashboard
- Security metrics connected to the live dashboard

## Changed

- Replaced all remaining core dashboard placeholders with real system data
- Advanced active development from Core Monitoring to Security Intelligence
- Synchronized project status, sprint history, roadmap, release checklist, implementation tracker, and README

## Verified

- Solution builds successfully
- Application launches successfully
- CPU, memory, disk, network, process, Defender, and Firewall values display correctly
- Dashboard refreshes once per second
- Runtime behavior verified by the Product Owner

---

# [0.3.0] - 2026-07-29

## Added

- Microsoft.Windows.CsWin32 integration
- `NativeMethods.txt`
- Native `GetSystemTimes` binding
- Native `GlobalMemoryStatusEx` binding
- Production CPU utilization sampling
- Production physical-memory reporting
- One-second dashboard refresh
- Live timestamp updates

## Fixed

- CsWin32 FILETIME compatibility
- Physical-memory reporting
- CPU first-sample and reversed-sample handling

---

# [0.2.0] - Architecture and Documentation Foundation

- MonitoringEngine and SystemSnapshot architecture
- Monitor-service structure
- Core project documentation and tracking system
- GitHub workflow using `main`

---

# [0.1.0] - Initial Foundation

- Sentinel AI solution
- WinUI 3 application
- Initial dashboard
- Successful build configuration

---

End of Changelog
