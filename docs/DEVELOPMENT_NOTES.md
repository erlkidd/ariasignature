# AriaSignature — development notes

## Release 0.2.2

### Functional changes
- Added low-level disk telemetry channel via `smartctl` (ATA/NVMe JSON), merged with WMI and Storage Reliability counters.
- Improved SMART/health/temperature coverage for devices where WMI alone is incomplete.
- Added physical-drive mapping (`PhysicalDriveN`) and stronger collector warnings when telemetry signals are missing.
- Added user-facing schedule presets for SMART polling in Settings (interval/daily/custom Quartz).
- Improved form validation UX for backup task creation (field-level errors + human-readable messages).
- Fixed backup log filter side effect: changing status filter no longer triggers disk refresh.

### Runtime and shell
- Fixed tray restore crash caused by invalid WPF state combination (`ShowActivated=false` + `WindowState=Maximized`).
- Kept single-instance behavior with foreground activation of existing process.

### UI and consistency
- Normalized service status presentation in Russian.
- Unified visual style for panels/controls and tightened section hierarchy.
- Title bar controls aligned to right side and drag behavior from maximized state made closer to system window behavior.

### Documentation
- Rewritten user/API/developer/installer/release-gate docs in a uniform technical style.
- Clarified system purpose: autonomous service operation + API publication for external consumption.

## Release 0.2.1

- Stabilized API startup and Swagger compatibility.
- Hardened WMI telemetry collection and refresh endpoint behavior.
- Improved installer service registration flow (`sc create`, quoting, verification).
- Synchronized versions across build artifacts and docs.

## Branching policy

- Release branch: `production`.
- Working branch: `test/agent-work`.
- Merge to `production` is controlled by repository owner.
