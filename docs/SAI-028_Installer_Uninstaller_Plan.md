# SAI-028 — Installer and Uninstaller Plan

Version: 2.0

Status: Complete — Release Operations

Last Updated: 2026-08-02

Copyright (c) 2026 Modern Methods.

---

# Purpose

Define production requirements and the operator procedure for installing and removing Sentinel AI on another Windows computer.

# Supported Deployment Model

Sentinel AI uses the Windows packaging project in:

`src/SentinelAI/Sentinel.App/Sentinel.App (Package)`

The primary commercial target is **Release | x64** for normal Intel/AMD 64-bit Windows computers. x86 and ARM64 publish profiles are retained for architecture-specific release work.

# Creating an Installable Release

From Visual Studio on the release-development computer:

1. Pull the current `main` branch.
2. Open the Sentinel solution.
3. Select **Release** and **x64**.
4. Build the solution and confirm zero build errors.
5. In Solution Explorer, right-click **Sentinel.App (Package)**.
6. Use the packaging/publish command provided by Visual Studio to create the application package for sideloading/distribution.
7. Select x64 and create the release package in a dedicated release-output folder.
8. Do not distribute a package until the production publisher certificate/signing identity is configured and the generated package signature is verified.

The generated package/output folder is the deployable release artifact. Copy the complete generated package folder when moving the installer to another computer; do not copy only the application EXE from `bin`.

# Installing on Another Computer

For a properly production-signed release:

1. Copy the complete generated release package to the target Windows computer or obtain it from the approved Modern Methods distribution channel.
2. Open the generated `.msix`/`.msixbundle` package using Windows App Installer.
3. Confirm the displayed publisher is the approved Modern Methods/Sentinel AI production publisher.
4. Choose **Install**.
5. Launch **Sentinel AI** from the Windows Start menu after installation.
6. Complete the first-run experience and verify the personalized greeting, healthy-state experience, monitoring evidence, Ask Sentinel, and Technical details.

A development/test package signed with a non-public certificate may require the corresponding trusted certificate to be installed on the test computer before Windows will accept the package. This is for controlled testing only and is not the commercial distribution method.

# Production Signing Requirement

Windows installation on another user's computer should use a trusted production-signed package. Private signing keys, PFX files, passwords, tokens, and certificate secrets must never be committed to this repository.

SAI-029 defines the signing boundary. Until the production signing certificate/distribution identity is provisioned, Sentinel's implementation is complete but the package is a development/release-candidate artifact rather than a publicly distributable installer.

# Uninstallation

On the target computer:

1. Open **Settings > Apps > Installed apps**.
2. Find **Sentinel AI**.
3. Choose **Uninstall** and confirm.
4. Verify Sentinel no longer launches and its installed application package has been removed.

User-data retention/removal must follow the documented privacy and release policy. Uninstall must not claim that unrelated Windows security configuration or evidence was removed unless that removal was explicitly performed and verified.

# Installation Acceptance Check

After installing on a clean/secondary Windows computer, verify:

- Sentinel launches without `REGDB_E_CLASSNOTREG`, `ApplicationData.Current`, `XamlRoot`, or startup/debugging failures.
- First visible window appears promptly.
- Personalized greeting persists.
- CPU, memory, disk, network, process, Defender, and Firewall evidence populate.
- Ask Sentinel remains grounded in verified local evidence.
- Technical details expand normally.
- Closing and reopening Sentinel works normally.
- Windows uninstall completes successfully.

# Release Status

Installer/uninstaller implementation and local runtime verification are complete as Phase 7 item 7. Public deployment still requires the release-operations step of provisioning and applying the approved production signing identity before distributing the generated package to other users.

---

End of Document
