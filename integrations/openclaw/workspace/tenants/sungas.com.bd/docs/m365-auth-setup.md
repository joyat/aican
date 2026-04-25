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

## Sungas Entra Values

- `AICAN_M365_CLIENT_ID=2504bc5e-3683-4f5d-8007-9c1ef6bfee1f`
- `AICAN_M365_TENANT_ID=d0dbcfe9-f3ae-4822-8ba1-6a4144e7d25c`

## Bootstrap User Seats In This Repo

- `consultant@sungas.com.bd` -> `Admin_Bot`
- `cfo_bot@sungas.com.bd`
- `joyat.biju@sungas.com.bd` -> `IT_Bot`

Replace or extend these with real employee emails once tenant onboarding is confirmed.

## Recommended Production Direction

1. Register an Entra application for the desktop client.
2. Grant delegated Microsoft Graph `User.Read`.
3. Add real `@sungas.com.bd` user emails to `users.json`.
4. Keep confidential tenant documents out of this public repository.
5. Add only approved internal files to the tenant workspace on the live server.
