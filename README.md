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

## Authentication note

Gmail and Microsoft 365/Outlook.com have disabled plain username/password IMAP login and require OAuth2, which needs an app registration (Google Cloud / Entra ID client ID) that only you, the app owner, can create. v1 targets **username + app-password** IMAP providers (e.g. GMX, Web.de, Fastmail, generic IMAP/Dovecot/Cyrus servers, self-hosted). OAuth2 support for Gmail/Outlook is planned as a follow-up once a client ID is registered.

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
