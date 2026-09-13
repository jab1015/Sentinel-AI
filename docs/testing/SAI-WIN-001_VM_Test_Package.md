# SAI-WIN-001 — Windows VM Test Package

Version: 1.0  
Status: BLOCKED AT AUTOMATED SIGNATURE VERIFICATION — NOT YET VM-READY  
Last Updated: 2026-09-13

Copyright (c) 2026 Modern Methods.

---

## Purpose

This document is the handoff and evidence record for producing the isolated signed Sentinel AI Windows 11 VM test package. It is not a production-release or Microsoft Store authorization.

## Current Source

- Repository: `jab1015/Sentinel-AI`
- Branch: `feature/premium-privacy-foundation`
- Current package-source/live head before this documentation commit: `f51a31fe53c64cadd0e346eb32648a86353d23f2`
- Package version: `1.0.26.0`
- Architecture: `x64`
- Configuration: `Release`
- Package format: `MSIX`
- Expected test package name: `SentinelAI-WindowsVM-x64.msix`
- Test public certificate: `SentinelAI-TestSigning.cer`
- Production Store signing key used: **NO**
- Private key committed: **NO**
- Private key artifact published: **NO**

If the branch advances after this document is committed, distinguish the live documentation head from the actual package-source SHA.

## Packaging Workflow

Workflow: `.github/workflows/windows-vm-test-package.yml`

The workflow builds the Windows Application Packaging Project in Release/x64/SideloadOnly mode, creates an ephemeral runner-only RSA 3072 Code Signing certificate whose subject matches the existing production package Publisher, exports only the public `.cer` to the staged artifact, signs the MSIX, deletes the temporary PFX, verifies the signature, unpacks and inspects the package, checks manifest registrations and x64 PE architecture, stages install/uninstall helpers, computes SHA-256, and uploads the final test artifact only after all gates pass.

Production package identity remains intentionally unchanged:

- Name: `ModernMethods.SentinelAI`
- Publisher: `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`

## Required Package Contents

The final package must contain exactly one of each:

- `Sentinel.App.exe`
- `Sentinel.PrivilegedBroker.exe`
- `Sentinel.ExplorerExtension.dll`

The packaged manifest must preserve:

- `windows.comServer`
- `windows.fileExplorerContextMenus`
- `Sentinel.ExplorerExtension.dll`
- CLSID `6C5E88B7-2A44-4B6D-9A6C-4F1A5C9F6E21`
- `Type="*"`
- `Type="Directory"`

## Latest Qualification Attempt

GitHub Actions run: `34736783747`  
Job: `103669514840`  
Source SHA: `f51a31fe53c64cadd0e346eb32648a86353d23f2`

Observed results:

- Checkout: PASS
- .NET setup: PASS
- Windows build-tool discovery: PASS
- Production package identity guard: PASS
- Unsigned Release x64 MSIX build: PASS
- Ephemeral provider-independent test certificate creation: PASS
- MSIX signing: PASS
- Signed MSIX verification: **CANCELLED / BLOCKED**
- Package content/manifest inspection: NOT RUN because verification did not complete
- Artifact staging/upload: NOT RUN
- Final SHA-256: NOT YET AVAILABLE

The verification step began at approximately 04:02:52Z and remained active until approximately 04:28:58Z, when the 30-minute job limit caused cancellation. The workflow intended a 120-second bound around `SignTool verify`, so the next investigation must identify which operation actually remained blocked and correct the timeout/verification implementation without weakening cryptographic validation.

## Important Interpretation

The build and signing stages are proven to work. The package is **not yet approved for VM installation** because cryptographic signature verification, package-content inspection, manifest registration inspection, final artifact staging, and final SHA-256 publication have not completed in one successful qualification run.

Do not work around this by manually producing an unrelated Visual Studio package unless the automated path is proven unusable. The goal is one reproducible package from the recorded source SHA.

## Next Required Actions

1. Fetch the full log for job `103669514840` and isolate the last output from `Verify signed VM test MSIX`.
2. Determine whether the block is `SignTool verify`, `Get-AuthenticodeSignature`, certificate trust handling, or process-timeout behavior.
3. Correct the workflow narrowly without weakening signature validation.
4. Re-run the Windows VM test-package workflow.
5. Require PASS for signature validation and exact signer subject/thumbprint matching.
6. Require PASS for package contents, x64 PE checks, Publisher/identity, Explorer COM/context-menu registrations, and private-key absence.
7. Download and independently inspect the final artifact ZIP.
8. Confirm the artifact contains the MSIX, public `.cer`, install/uninstall scripts, `SHA256SUMS.txt`, and `PackageBuildInfo.txt`, and contains no `.pfx`, `.p12`, `.key`, private-key PEM, or signing password.
9. Record the exact successful package source SHA, workflow run, artifact, certificate, and SHA-256 here.
10. Only then mark `WINDOWS VM TESTING READY: YES` and begin clean Windows 11 VM installation one step at a time.

## Current Readiness

- WINDOWS VM TESTING READY: **NO**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**

---

End of Document
