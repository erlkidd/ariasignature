# Incident: startup regression on Win10/Win11

## Purpose

This document fixes one target problem permanently: service/UI startup instability after installer run, including the loop of `SC 1053`, `ServiceCrashedOnStart`, and "nothing happens" user experience.

The goal is to keep one deterministic playbook so this incident does not return.

## User-visible failure pattern

- Installer reports service auto-start failure.
- Bootstrap detail contains `stage=post-1053-check` and `category=ServiceCrashedOnStart`.
- `sc start` returns `1053`, then service process is missing.
- UI may look like it does not start (single-instance + tray behavior can hide feedback).

## Root causes

### 1) Early service startup path was too fragile

`AriaSignature.Service` had extra work in critical startup path before stable run state.
Any early exception led to process exit, which appears as:

- SCM timeout `1053`
- post-check sees service stopped and process missing
- bootstrap reports runtime failure

### 2) Installer used hard-fail behavior for recoverable startup delays

Installer treated delayed API/service readiness as terminal in several branches.
That created half-installed perception and repeated recovery loops.

### 3) UI startup had silent outcomes in single-instance branch

When another instance existed, second launch could terminate quickly.
In failure/hung/tray-hidden scenarios this looked like "app does not launch at all".

## Final remediation strategy

Do not roll back branches or history. Fix only in-place by hardening each startup stage.

### A. Service process hardening (highest priority)

File: `src/service/AriaSignature.Service/Program.cs`

- Keep startup path minimal and exception-safe.
- Avoid fragile identity/log calls in the critical pre-run section.
- Wrap build/run startup path with fatal guard and always write deterministic fatal log:
  - `%ProgramData%\AriaSignature\logs\service-startup-fatal.log`
- Emit clear stage markers:
  - `startup-enter`
  - `host-built`
  - `run-enter`
  - `fatal`

Expected result: no silent process death without root cause in logs.

### B. API hosted service startup reporting

File: `src/service/AriaSignature.Service/LocalApiHostedService.cs`

- Keep API start non-blocking for SCM startup.
- On bind/listen failure, emit explicit structured log marker with bind mode, URL, and native error if available.
- Never leave ambiguous "service crashed" without details in service logs.

Expected result: API bind issues are diagnosable in one read.

### C. Installer startup behavior: degrade, do not dead-end

File: `installer/inno/AriaSignature.iss`

- Keep installation successful when failure is recoverable (delayed readiness, temporary probe fail).
- Replace hard abort branches with deterministic degraded flow and actionable message.
- Surface bootstrap detail and always include log paths.

Expected result: installer does not trap user in dead-end state for transient startup conditions.

### D. Post-1053 classification and hinting

File: `src/core/AriaSignature.WindowsServiceSetup/WindowsServiceInstaller.cs`

- Keep extended post-1053 verification.
- Distinguish between:
  - recoverable delayed warmup
  - real process crash
- In crash path, hint primary log:
  - `%ProgramData%\AriaSignature\logs\service-startup-fatal.log`

Expected result: 1053 branch gives concrete next action, not generic loop.

### E. UI launch visibility and single-instance behavior

Files:
- `src/ui/AriaSignature.UI/App.xaml.cs`
- `src/ui/AriaSignature.UI/SingleInstanceActivator.cs`

- Do not silently exit in second-instance path.
- If existing instance activation fails, show explicit user message.
- Add stale mutex recovery attempt before giving up.

Expected result: no "nothing happened" behavior on manual launch.

## Verification protocol (mandatory)

Run in this order for each release candidate:

1. `dotnet build AriaSignature.slnx -c Release`
2. `dotnet test AriaSignature.slnx -c Release`
3. `powershell -ExecutionPolicy Bypass -File .\scripts\release-gate.ps1`
4. Fresh install smoke on Win10 and Win11:
   - installer completes
   - `AriaSignatureService` is present and starts
   - `http://127.0.0.1:5160/api/v1/status` returns `200`
   - UI opens from installer launch and desktop shortcut
5. If failure:
   - collect `%ProgramData%\AriaSignature\logs\bootstrap-last-result.txt`
   - collect latest `%ProgramData%\AriaSignature\logs\service-*.log`
   - collect `%ProgramData%\AriaSignature\logs\service-startup-fatal.log`
   - attach exact installer/UI message text

No release is accepted without this matrix.

## Non-regression guardrails

- Do not add risky OS/security identity calls in service pre-run path unless wrapped and non-fatal.
- Do not convert recoverable startup delays into installer hard-fail.
- Do not add silent-exit paths in UI startup flow.
- Every startup-stage failure must map to one deterministic log file and one explicit user-facing hint.

## Ownership

- Startup pipeline owner: service + installer maintainers.
- Any startup change must include:
  - explicit stage logs
  - Win10/Win11 smoke evidence
  - update to this document when behavior changes

