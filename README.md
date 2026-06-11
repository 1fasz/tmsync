# TmSync — Time Matters ↔ Office 365 sync via Microsoft Graph

A modern replacement for the retiring LexisNexis **Time Matters Exchange sync**.
The old sync relies on Exchange Web Services (EWS) and legacy Exchange protocols
that Microsoft is shutting down for Exchange Online (EWS retires **October 1, 2026**).
TmSync talks to Office 365 exclusively through the **Microsoft Graph API**, the
supported successor, and runs as a Windows service alongside your Time Matters
SQL Server database.

## What it syncs

| Module | Direction | Office 365 target |
|---|---|---|
| Calendar / Events | two-way | user's default Outlook calendar |
| Contacts | two-way | user's default Outlook contacts folder |
| Tasks / ToDos | two-way | user's default Microsoft To Do list |
| Email journaling | one-way (O365 → TM) | Inbox + Sent Items copied into Time Matters |

Every module can be switched on/off globally (`appsettings.json`) and per user
(CLI). Conflicts are resolved by policy (`NewestWins` by default; `TimeMattersWins`
and `Microsoft365Wins` are also available). Delete propagation can be disabled.

On the very first run, items that already exist on **both** sides (left behind by
the old Exchange sync) are matched by subject/start-time (events), email/name
(contacts) or subject/due-date (tasks) and linked instead of duplicated.

## Components

```
src/TmSync.Core         Sync engine, conflict resolution, SQLite state store
src/TmSync.TimeMatters  SQL Server access to the Time Matters database
src/TmSync.Graph        Microsoft Graph (Office 365) access — delta queries, app-only auth
src/TmSync.Service      Windows service host (background sync every N minutes)
src/TmSync.Cli          Management CLI: add/remove users, run syncs, status
sql/                    SQL contract template to adapt to your Time Matters version
```

## Setup

### 1. Register an app in Microsoft Entra ID (Azure AD)

1. [Entra admin center](https://entra.microsoft.com) → **App registrations** → **New registration** (single tenant).
2. Under **API permissions**, add these **Application** permissions for Microsoft Graph,
   then click **Grant admin consent**:
   - `Calendars.ReadWrite`
   - `Contacts.ReadWrite`
   - `Tasks.ReadWrite.All`
   - `Mail.Read` (only needed if you enable email journaling)
3. Under **Certificates & secrets**, create a client secret and note it down.
4. Note the **Tenant ID** and **Application (client) ID** from the Overview page.

> Recommended: restrict which mailboxes the app can touch with an
> [application access policy](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access)
> scoped to a mail-enabled security group containing the synced users.

### 2. Adapt the SQL contract to your Time Matters database

TmSync reads/writes Time Matters only through the views and stored procedures in
[`sql/TmSync_Contract_Template.sql`](sql/TmSync_Contract_Template.sql)
(`TmSync_Events`, `TmSync_Contacts`, `TmSync_Todos`, plus create/update/delete
procs and `TmSync_SaveEmail`). The Time Matters schema is proprietary and varies
by version, so the template ships with `TODO` placeholders — map them to your
actual tables and run the script against the Time Matters database.

This isolation means nothing in the application has to change when the schema
differs between TM versions, and your DBA keeps full control over exactly what
the sync may touch.

### 3. Configure

Edit `appsettings.json` (deployed next to the service executable):

```jsonc
{
  "TimeMatters": {
    "ConnectionString": "Server=SQLSERVER;Database=TimeMatters;Integrated Security=true;TrustServerCertificate=true"
  },
  "Microsoft365": {
    "TenantId": "...",
    "ClientId": "...",
    "ClientSecret": "..."
  },
  "Sync": {
    "StateDatabasePath": "C:\\ProgramData\\TmSync\\tmsync-state.db",
    "IntervalMinutes": 5,
    "ConflictPolicy": "NewestWins",       // NewestWins | TimeMattersWins | Microsoft365Wins
    "PropagateDeletes": true,
    "Modules": { "Calendar": true, "Contacts": true, "Tasks": true, "EmailJournal": false },
    "Calendar": { "PastDays": 30, "FutureDays": 365 }   // rolling calendar sync window
  }
}
```

### 4. Build, install the service, add users

```powershell
# Build (requires .NET 8 SDK)
dotnet publish src/TmSync.Service -c Release -r win-x64 --self-contained -o C:\TmSync
dotnet publish src/TmSync.Cli     -c Release -r win-x64 --self-contained -o C:\TmSync

# Install as a Windows service
sc.exe create TmSync binPath= "C:\TmSync\TmSync.Service.exe" start= auto obj= "DOMAIN\svc-tmsync" password= "..."
sc.exe start TmSync
```

Manage users and sync from the CLI (uses the same `appsettings.json`):

```text
tmsync users add JDOE jdoe@yourfirm.com            # add a user (all modules per global config)
tmsync users add MSMITH msmith@yourfirm.com --no-contacts --email-journal
tmsync users remove JDOE                           # stop syncing (records stay put)
tmsync users disable JDOE                          # pause without removing
tmsync users list
tmsync sync --user JDOE --module calendar          # run one pass manually
tmsync status                                      # last-run times per user/module
```

## How it works

- **Incremental on both sides.** Microsoft 365 changes are picked up with Graph
  **delta queries** (per user, per module); Time Matters changes via a
  last-modified watermark. Expired delta tokens trigger an automatic full resync.
- **No duplicates, no loops.** Every linked pair carries a content hash in the
  local SQLite state DB; echoes of the sync's own writes and no-op updates are skipped.
- **Email journaling baseline.** Enabling journaling for a user does not back-fill
  their whole mailbox — only mail arriving afterwards is journaled, deduplicated
  by internet message id.
- **App-only auth.** The service authenticates with client credentials; no
  per-user passwords, no Basic Auth, nothing that Microsoft is deprecating.

## Known limitations (v1)

- Recurring events are synced as the individual occurrences that fall inside the
  configured calendar window, not as recurrence rules.
- Tasks sync targets the user's default To Do list only.
- Email journaling stores the plain-text body (no attachments).

## Development

```bash
dotnet build TmSync.sln
dotnet test TmSync.sln
```
