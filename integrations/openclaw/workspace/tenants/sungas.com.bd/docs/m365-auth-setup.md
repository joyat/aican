# Sun Gas Limited (Bangladesh) — Microsoft 365 Setup Notes

## Goal

Enable AiCan desktop sign-in with real `@sungas.com.bd` Microsoft 365 identities.

## What The Current Code Supports

- Interactive Microsoft sign-in from the Windows desktop app
- A configurable Entra application client ID via environment variable
- Optional tenant-specific authority via environment variable
- Session bootstrap into AiCan after successful Microsoft sign-in

## Required Environment Variables On Windows

- `AICAN_M365_CLIENT_ID`
- `AICAN_M365_TENANT_ID` (recommended for tenant-specific login)

## Bootstrap User Seats In This Repo

- `admin_bot@sungas.com.bd`
- `cfo_bot@sungas.com.bd`
- `it_bot@sungas.com.bd`

Replace or extend these with real employee emails once tenant onboarding is confirmed.

## Recommended Production Direction

1. Register an Entra application for the desktop client.
2. Grant delegated Microsoft Graph `User.Read`.
3. Add real `@sungas.com.bd` user emails to `users.json`.
4. Keep confidential tenant documents out of this public repository.
5. Add only approved internal files to the tenant workspace on the live server.
