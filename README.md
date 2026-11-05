# MailBridge

A Windows 11 (Fluent Design / WinUI 3, .NET 10) desktop app to **back up, restore, and migrate IMAP email accounts**.

MailBridge downloads the full contents of an IMAP mailbox (all folders, messages, and flags) to a local backup, optionally compressed into a single archive file, and can restore that backup to the same or a different IMAP account — which makes it useful for migrating mail between providers. Restore includes filtering and duplicate detection so you don't end up with the same message twice on the target server.

## Status

Early scaffold — see the project board / issues for progress. Not yet functional end-to-end.

## Planned v1 scope

- **Backup**: connect to an IMAP account (username + app password), download all folders/messages as individual `.eml` files plus a JSON index (Message-ID, UID, folder, flags, size, headers) per folder. Optional: compress the whole backup into a single `.zip` archive.
- **Restore**: pick a backup (folder or `.zip`), pick a target IMAP account, filter which messages to restore (by folder, date range, sender, subject/text search), and upload them via IMAP APPEND.
- **Duplicate detection on restore**: before uploading, MailBridge checks the target folder for an existing message that matches on multiple attributes — Message-ID header (strongest signal), plus From, Date, Subject and size as corroborating signals — to decide if a message already exists.
- **Conflict resolution**: when a duplicate (or a message with the same identifying attributes but different content) is found, you choose: **Replace**, **Replace all** (apply to remaining conflicts in this run), **Skip**, **Skip all**, or **Abort** the restore. MailBridge only ever treats a match as a true duplicate (safe to skip) when the compared attributes are actually identical; if they differ it is flagged as a conflict for you to decide rather than silently skipped or silently replaced.
- **Scheduled backups**: configure a recurring (daily/weekly) backup per account, including destination folder and compression choice. Schedules run via the Windows Task Scheduler, so they fire even if MailBridge isn't currently open; the app is launched headlessly with a `--run-scheduled-backup <id>` flag, performs the backup, records success/failure + a summary, and exits.
- Fluent Design UI (WinUI 3 `NavigationView`, Mica/acrylic, light & dark theme) with pages for Accounts, Backup, Restore, and Schedule.

## Authentication

Most providers sign in with **username + (app-)password**. The Accounts page offers templates for popular ones (`MailProviderTemplates`: AWS WorkMail, eclipso, Fastmail, freenet, Gmail with an app password, GMX, iCloud, IONOS, mailbox.org, Posteo, Strato, T-Online, WEB.DE, Yahoo, Zoho); generic IMAP/Dovecot/Cyrus or self-hosted servers can be entered manually. Passwords are stored in the Windows Credential Manager, never in MailBridge's files.

**Outlook.com and Microsoft 365** have disabled password login for IMAP and require OAuth2. Choosing the "Outlook.com / Microsoft 365" template replaces the password field with **Sign in with Microsoft**: MailBridge opens the Microsoft login page in the default browser and receives the answer on `http://localhost:<random port>` (authorization code flow with PKCE, no client secret). It stores only the refresh token, in the Windows Credential Manager like a password, and uses short-lived access tokens for IMAP (SASL XOAUTH2). Scheduled backups renew the access token without user interaction; if Microsoft rejects the refresh token (revoked, password change, 90 days unused), the run fails with a message and the account has to be signed in again on the Accounts page.

### Microsoft sign-in (OAuth2)

Microsoft sign-in needs an app registration in Microsoft Entra ID. It is created once by the app owner; its client ID is compiled into MailBridge and is not a secret. Without one, the sign-in button is disabled.

1. Open the [Microsoft Entra admin center](https://entra.microsoft.com) → **Applications** → **App registrations** → **New registration**. A free Entra tenant (e.g. from a free Azure account) is enough; personal Microsoft accounts alone cannot own app registrations.
2. **Name**: `MailBridge`. **Supported account types**: *Accounts in any organizational directory and personal Microsoft accounts*. **Redirect URI**: platform *Public client/native (mobile & desktop)*, value `http://localhost`. Register.
3. Copy the **Application (client) ID** from the overview page into `Directory.Build.props`:
   ```xml
   <MicrosoftOAuthClientId>00000000-0000-0000-0000-000000000000</MicrosoftOAuthClientId>
   ```
4. Optional, for Microsoft 365 organizations: **API permissions** → **Add a permission** → *APIs my organization uses* → *Office 365 Exchange Online* → **Delegated** → `IMAP.AccessAsUser.All`. Users are asked for this permission at their first sign-in anyway; listing it lets an administrator grant consent for the whole organization in advance. Organizations that only allow apps from verified publishers need such an admin consent.
5. Rebuild (`.\build.ps1` for the installer).

The mailbox must have IMAP enabled (on by default for Outlook.com; Microsoft 365 administrators can switch it off per mailbox).

## Solution layout

```
MailBridge.sln
src/
  MailBridge.App/     WinUI 3 application (views, view models, packaging)
  MailBridge.Core/    IMAP, backup/restore, duplicate detection, compression (no UI dependencies)
tests/
  MailBridge.Core.Tests/
```

## Requirements

- Windows 11
- .NET 10 SDK
- Windows App SDK workload

## Build

```
dotnet restore
dotnet build MailBridge.sln
```

## Test

```
dotnet test tests/MailBridge.Core.Tests
```

## Installer

```powershell
.\build.ps1                  # x64; -Runtime win-arm64 for ARM, -SkipMsi for the package only
```

Publishes the app self-contained (no installed .NET runtime or Windows App SDK needed) to
`artifacts\win-x64\` and builds `artifacts\MailBridge-<version>-x64.msi` from it with WiX Toolset 5
(restored via NuGet on the first build; the version comes from `Directory.Build.props`). The MSI
installs for all users to `C:\Program Files\MailBridge` (administrator rights required), adds a
Start menu shortcut, closes a running MailBridge before updating and offers "MailBridge jetzt
starten" at the end. Newer versions replace older ones automatically (same `UpgradeCode`).

Accounts, schedules and settings under `%LOCALAPPDATA%\MailBridge` and the passwords in the Windows
Credential Manager are kept on uninstall. The uninstalling user's scheduled-backup tasks
(`MailBridge_Backup_*` in the Task Scheduler) are removed, since they would otherwise keep starting a
program that no longer exists; upgrades leave them in place.

`installer\make-images.ps1` regenerates the installer dialog images from the app icon.

## Branches

- `main` – stable releases
- `DEV` – ongoing development; stable states are merged into `main` when a new version is released
