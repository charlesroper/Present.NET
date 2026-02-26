# AGENTS.md

Pragmatic guidance for humans and coding agents working in this repo.

## Purpose

`Present.NET` is a Windows WPF presentation app for web/image slides with a phone-friendly remote control page.

## Repo Basics

- Primary project: `src/Present.NET/Present.NET.csproj`
- Solution: `Present.NET.sln`
- Platform: Windows only (`net8.0-windows`, WPF)
- Remote server: embedded Kestrel on port `9123`

## Build and Run

Use these commands from repo root:

```powershell
dotnet restore
dotnet build Present.NET.sln
dotnet run --project src/Present.NET/Present.NET.csproj
```

## Testing

Two test projects are maintained:

- `tests/Present.NET.Tests` - unit + integration
- `tests/Present.NET.UiTests` - desktop UI smoke tests (gated)

Run everything:

```powershell
dotnet test Present.NET.sln
```

Run only core tests:

```powershell
dotnet test tests/Present.NET.Tests/Present.NET.Tests.csproj
```

Run UI smoke tests explicitly:

```powershell
$env:PRESENT_UI_TESTS = "1"
dotnet test tests/Present.NET.UiTests/Present.NET.UiTests.csproj
```

## Development Workflow (Pragmatic TDD)

Prefer red/green/refactor in small slices:

1. Add or update a failing test first.
2. Make the smallest change to pass.
3. Refactor while staying green.

Guidelines:

- Keep behavior changes covered by tests in the same change.
- Prefer one logical commit per completed slice when practical.
- Avoid broad rewrites unless required.

## Remote Control Notes

- Remote endpoints are served on `http://<ip>:9123/`.
- `localhost` can work while LAN access is blocked by firewall/VPN/network policy.
- Do not reintroduce `HttpListener`/URL ACL setup requirements.
- If local networking is restrictive (conference/guest Wi-Fi), prefer Tailscale and use `http://<tailscale-ip>:9123/`.

## Change Checklist

Before finishing a change:

- Build succeeds.
- Relevant tests pass (and full suite when possible).
- User-visible behavior changes are reflected in `README.md`.
- Naming stays consistent as `Present.NET` across UI/docs.
