# Melezh bundle (OpenIntegrations)

Place `melezh.exe` and required `oint` runtime files in this directory before building the installer.

Release gate step `prepare-melezh` attempts to populate this folder automatically when possible.

Pinned OpenIntegrations version: see `VERSION`.

Expected layout after prepare:

- `melezh.exe`
- supporting `oint` binaries and dependencies from the OpenIntegrations Windows CLI bundle
