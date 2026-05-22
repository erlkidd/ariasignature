# Melezh project template

`AriaSignature.melezh` is created at runtime by `AriaSignature.MelezhHost` under:

`%ProgramData%\AriaSignature\melezh\AriaSignature.melezh`

On first start the host either copies `AriaSignature.melezh` from `{app}\melezh\templates\` (if present) or runs `СоздатьПроект`, then seeds handlers:

| Handler key | OInt | Method | Role |
|-------------|------|--------|------|
| `aria_ping` | `http` / `Get` | GET | Health probe (`GET http://127.0.0.1:7788/aria_ping`) |
| `aria_sync` | `http` / `PostСТелом` | JSON | Inbound telemetry JSON → ACK `POST :5160/api/v1/melezh/ingest` |

To bake a golden template into the installer, copy a seeded project file here before building the setup.
