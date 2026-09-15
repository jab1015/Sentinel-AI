# Changelog

All notable changes to Sentinel AI are documented in this file.

---

# [Unreleased]

## In Progress

- Continued security hardening and post-release qualification

---

# [1.0.28.0] - 2026-09-15

## Added

- Premium Privacy and Sentinel Vault experience
- File and folder protection from Sentinel and Windows File Explorer
- Vault folder collections with expandable folder/file browsing
- Ctrl/Shift multi-selection for restoring multiple Vault files and folders
- Progress feedback for longer Vault folder add and restore operations
- Independent Vault recovery-key path
- Portable password-protected encryption for sharing
- Verified secure-delete and encrypted-file replacement flows
- Native Windows File Explorer Sentinel actions

## Changed

- Redesigned the Sentinel dashboard as a native Windows security control center
- Improved Ask Sentinel routing so locally answerable performance and restart questions stay grounded in local evidence
- Improved startup monitoring semantics so incomplete initial evidence remains a neutral gathering state instead of appearing as a Sentinel failure
- Restoring a complete Vault folder now reconstructs its authenticated hierarchy
- Successfully restored Vault items are retired from the active Vault only after plaintext restore verification
- Strengthened LocalDev packaging and VM qualification boundaries without weakening production Store entitlement enforcement

## Security and reliability

- Preserved compile-time-only LocalDev entitlement behavior; production Release builds continue to require authoritative Store/gateway entitlement
- Hardened Vault metadata, ciphertext boundaries, restore destinations, source retirement, collision handling, reparse-point handling, and crash/failure behavior
- Kept recovery of protected user data independent of subscription state
- Preserved verified Defender, Firewall, Secure Boot, quarantine, monitoring, repair, and system-health findings as actionable conditions while suppressing false startup degradation

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
