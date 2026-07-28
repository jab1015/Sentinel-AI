# SAI-005 — Threat Model

**Document ID:** SAI-005  
**Title:** Threat Model  
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

This document defines the threat model for Sentinel AI.

Its purpose is to identify threats that Sentinel AI must detect, assess, explain, and, where appropriate, mitigate while operating within supported Windows security boundaries.

The threat model guides engineering, testing, validation, and future product development.

---

# 2. Security Objectives

Sentinel AI shall:

- Detect malicious behavior.
- Detect suspicious behavior.
- Explain why behavior is suspicious.
- Preserve evidence.
- Minimize false positives.
- Avoid unnecessary user disruption.
- Integrate with native Windows security.

---

# 3. Assets to Protect

The following assets are considered high value.

## User Assets

- Personal files
- Photos
- Videos
- Documents
- Password databases
- Browser profiles
- Email
- Financial information

---

## System Assets

- Windows installation
- Registry
- Startup configuration
- Services
- Drivers
- Scheduled tasks
- Security settings

---

## Sentinel AI Assets

- Event database
- Threat database
- Configuration
- Logs
- AI explanations
- User preferences

---

# 4. Threat Categories

Sentinel AI classifies threats into the following categories.

| Category | Description |
|----------|-------------|
| Malware | Malicious software |
| Spyware | Data collection without consent |
| Ransomware | File encryption attacks |
| Trojan | Disguised malicious software |
| Rootkit | Privilege hiding software |
| Worm | Self-propagating malware |
| Botnet | Remote command and control |
| Credential Theft | Password theft |
| Persistence | Unauthorized startup mechanisms |
| Insider Misuse | Legitimate software used maliciously |

---

# 5. Attack Surface

Sentinel AI monitors the following attack surfaces.

## Process Execution

Threats include:

- Unknown executable
- Unsigned executable
- Temporary folder execution
- Parent-child anomalies
- Script interpreters
- LOLBins

---

## Network

Threats include:

- Unknown outbound connections
- Unexpected inbound connections
- Command-and-control servers
- DNS tunneling
- Port scanning
- Beaconing
- Excessive failed connections

---

## Registry

Threats include:

- Run Keys
- RunOnce Keys
- IFEO modifications
- Defender configuration changes
- Firewall configuration changes

---

## Startup

Monitor:

- Startup folders
- Registry startup entries
- Scheduled startup tasks
- Services configured for automatic startup

---

## File System

Monitor:

- Temporary directories
- Downloads
- Desktop
- Startup folders
- User profile
- System folders

---

## Windows Security

Monitor:

- Defender disabled
- Firewall disabled
- SmartScreen disabled
- Secure Boot disabled
- Windows Update disabled

---

# 6. Threat Severity

Threats are assigned a severity level.

| Severity | Description |
|----------|-------------|
| Informational | No security impact |
| Low | Minimal concern |
| Medium | Suspicious activity |
| High | Likely malicious |
| Critical | Immediate action recommended |

---

# 7. Risk Scoring

Every detected event shall receive a risk score.

| Score | Meaning |
|--------|---------|
| 0–19 | Safe |
| 20–39 | Low Risk |
| 40–59 | Medium Risk |
| 60–79 | High Risk |
| 80–100 | Critical |

Risk scores are calculated using multiple indicators.

---

# 8. Confidence Scoring

Confidence reflects how certain Sentinel AI is in its assessment.

| Confidence | Meaning |
|-----------|---------|
| 0–25% | Weak evidence |
| 26–50% | Moderate evidence |
| 51–75% | Strong evidence |
| 76–100% | Very strong evidence |

Risk and confidence are independent values.

---

# 9. Threat Indicators

Examples include:

- Unsigned executable
- Invalid digital signature
- Process injection
- DLL injection
- PowerShell download
- Encoded PowerShell
- Hidden PowerShell window
- Scheduled task creation
- Registry persistence
- Service installation
- Firewall modification
- Defender exclusion
- Defender disabled
- Unusual outbound traffic
- Network beaconing
- Connection to known malicious IP
- Connection to newly registered domain

No single indicator alone should automatically classify software as malicious.

---

# 10. Threat Correlation

Sentinel AI shall correlate multiple indicators before escalating alerts.

Example:

PowerShell + Registry Modification + Defender Disabled + Outbound Connection

↓

Higher confidence

↓

Higher risk score

Correlated events reduce false positives.

---

# 11. Evidence Collection

Every alert shall preserve:

- Timestamp
- Executable path
- SHA-256 hash (future)
- Publisher
- Process ID
- Parent Process ID
- Command line
- User account
- Remote IP
- Local IP
- Port
- Event source

Evidence shall remain available for later investigation.

---

# 12. Defensive Actions

Possible responses include:

- Notify only
- Recommend action
- Block connection
- Kill process
- Quarantine file (future)
- Create firewall rule
- Disable scheduled task
- Disable startup entry

Automatic actions shall require user configuration.

---

# 13. False Positive Strategy

Sentinel AI shall prioritize accuracy.

Methods include:

- Behavioral correlation
- Reputation checking
- Digital signatures
- Historical behavior
- User trust lists
- Confidence scoring

Users shall always be able to override recommendations.

---

# 14. Threat Intelligence

Future releases may incorporate:

- Known malicious IP feeds
- Malware hash databases
- Domain reputation
- Certificate reputation
- Community intelligence
- Enterprise intelligence feeds

Threat intelligence shall supplement—not replace—local analysis.

---

# 15. AI Threat Analysis

The AI engine shall explain:

- What happened
- Why it matters
- Supporting evidence
- Confidence level
- Recommended response

AI explanations shall never present speculation as confirmed fact.

---

# 16. Security Boundaries

Sentinel AI shall not:

- Modify Windows kernel components.
- Replace Microsoft Defender.
- Replace Windows Firewall.
- Install undocumented drivers.
- Circumvent Windows security protections.

Sentinel AI operates within supported Microsoft APIs.

---

# 17. Future Threat Coverage

Planned future capabilities include:

- Memory injection detection
- Ransomware heuristics
- Credential dumping detection
- Browser theft detection
- USB attack detection
- Lateral movement detection
- Living-off-the-land attack detection
- AI anomaly detection
- Local network intrusion detection

---

# 18. Validation

Every threat detection rule shall include:

- Test case
- Expected result
- False positive evaluation
- False negative evaluation
- Performance validation

No production rule shall be enabled without validation.

---

# 19. Threat Modeling Process

Threat modeling shall be reviewed:

- Before every major release
- After major architecture changes
- Following significant Windows API changes
- After major security incidents

This document shall evolve throughout the project lifecycle.

---

# 20. Conclusion

The Sentinel AI Threat Model establishes the security foundation for the application.

All threat detection, AI analysis, defensive actions, and testing activities shall align with this model to ensure consistent, evidence-based security decisions.

---

# End of Document

**Document ID:** SAI-005  
**Version:** 1.0  
**Status:** Approved (Working Draft)