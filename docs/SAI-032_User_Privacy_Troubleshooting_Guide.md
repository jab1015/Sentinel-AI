# SAI-032 — Sentinel AI User, Privacy & Troubleshooting Guide

Version: 1.0

Status: Release Candidate

Last Updated: 2026-08-02

Copyright (c) 2026 Modern Methods.

---

# Sentinel AI

Sentinel AI is a Windows investigation and security assistant designed to monitor quietly when your computer is healthy and surface clear guidance when verified evidence requires attention.

## What Sentinel Does

Sentinel monitors supported local Windows system evidence, investigates relevant observations, correlates evidence before escalating security conclusions, and explains findings in plain language. Technical evidence remains available through the Technical details section.

Sentinel does not treat every Windows error, unusual process, or network connection as a threat. Recommendations are based on the evidence Sentinel can verify.

## Ask Sentinel

Ask Sentinel answers questions using the local evidence Sentinel can currently verify on the computer. If the available evidence does not support a conclusion, Sentinel should say so rather than invent an answer or make an unsupported security claim.

## Privacy

Sentinel is designed around local system investigation. System evidence displayed by the application is collected from the Windows computer for monitoring and investigation purposes.

Sentinel must not silently upload private user files, document contents, passwords, authentication secrets, or unrelated personal content as part of normal monitoring. Any future feature that requires external processing must clearly document what information is transmitted, why it is required, and the applicable user control before commercial activation.

Diagnostic and investigation records may contain technical information such as process names, service names, Windows event information, network endpoints, resource usage, timestamps, and remediation outcomes. Users should review diagnostic material before sharing it with another person because technical logs can reveal information about the computer environment.

## Healthy State

When Sentinel displays **Your computer is healthy**, it means Sentinel has not found verified evidence that currently requires the user's attention. It is not a guarantee that no unknown or undetectable security issue exists.

## Action Required

When Sentinel determines that action is required, review:

- what Sentinel found;
- why it matters;
- what Sentinel investigated;
- what Sentinel needs from you; and
- any available safe remediation action.

Sentinel must request approval before an action when the configured safety boundary requires user authorization.

## Troubleshooting

### Sentinel does not open

1. Restart Windows and try Sentinel again.
2. Confirm Windows is fully updated and the installed Sentinel package has not been manually modified.
3. If Sentinel was recently updated, verify the update completed successfully.
4. If the problem persists, reinstall Sentinel using the authorized installer/release package.

### Monitoring information does not appear

Allow Sentinel a short initial sampling period. If system evidence remains empty, close Sentinel normally and reopen it. Do not repeatedly force-terminate Sentinel unless the application is unresponsive.

### Ask Sentinel cannot answer a question

Ask Sentinel intentionally refuses to guess when verified local evidence is insufficient. Rephrase the question around information Sentinel currently monitors or review Technical details for the evidence available.

### Sentinel reports an issue

Read the Investigation Summary before taking action. Do not assume an item is malware solely because it is unfamiliar. Follow Sentinel's verified recommendation and approval flow.

### Sentinel appears unresponsive

Wait briefly for an active investigation or Windows operation to finish. If the interface remains unresponsive, close the application normally if possible and reopen it. Record what was happening immediately before the problem if support is required.

### Reinstalling Sentinel

Use only an authorized Modern Methods release package. Normal application updates and reinstall operations are intended to preserve user state designated for retention. Uninstall behavior follows the release uninstall policy and may differ depending on whether retained user data is explicitly removed.

## Support Evidence

When reporting a problem, provide the Sentinel version, Windows version, what you were doing when the issue occurred, the exact message shown by Sentinel, and relevant Sentinel diagnostic information. Do not send passwords, private keys, authentication tokens, or unrelated personal files.

## Security Reporting

Potential security defects should be reported privately to Modern Methods through the official support/security channel established for the commercial release. Do not publish exploitable details before Modern Methods has had a reasonable opportunity to investigate and remediate the issue.

---

End of Document
