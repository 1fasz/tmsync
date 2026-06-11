/* =============================================================================
   TmSync SQL contract - TEMPLATE
   =============================================================================
   TmSync talks to the Time Matters database exclusively through the views and
   stored procedures defined in this script. Because the Time Matters schema is
   proprietary and differs between versions (TM 15/16/17, Enterprise vs SQL
   Express deployments), this file is a TEMPLATE: an administrator who knows the
   local schema (or a DBA) must map each view/procedure to the real Time Matters
   tables before enabling sync.

   Until then, every section below ships with a clearly marked placeholder.

   Conventions the application relies on:
     * All datetime values exchanged through this contract are UTC.
     * TmId is a stable string identifier (<= 64 chars) for the underlying
       Time Matters record - typically the record's primary key.
     * StaffCode is the Time Matters staff code the record belongs to.
     * IsDeleted = 1 rows tell TmSync the record was deleted/archived in
       Time Matters so the deletion can be propagated to Microsoft 365. If your
       schema does hard deletes, expose deletions via a tombstone/audit table,
       or leave IsDeleted as constant 0 to disable TM-side delete propagation.

   Run this against the Time Matters database after adapting it.
   ============================================================================= */

-- =============================================================================
-- 1. EVENTS (calendar)
-- =============================================================================
CREATE OR ALTER VIEW dbo.TmSync_Events AS
SELECT
    CAST(e.EventID AS varchar(64))      AS TmId,            -- TODO: real PK column
    e.StaffCode                         AS StaffCode,        -- TODO: staff column
    e.Description                       AS Subject,          -- TODO
    e.StartUtc                          AS StartUtc,         -- TODO: convert local -> UTC if needed
    e.EndUtc                            AS EndUtc,           -- TODO
    CAST(e.AllDay AS bit)               AS AllDay,           -- TODO
    e.Location                          AS Location,         -- TODO
    e.Memo                              AS Notes,            -- TODO
    CAST(NULL AS int)                   AS ReminderMinutes,  -- TODO (optional)
    e.LastModifiedUtc                   AS LastModifiedUtc,  -- TODO: change-tracking column
    CAST(0 AS bit)                      AS IsDeleted         -- TODO: tombstone flag if available
FROM dbo.YOUR_EVENT_TABLE e;            -- TODO: real table name
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_CreateEvent
    @StaffCode       varchar(20),
    @Subject         nvarchar(255),
    @StartUtc        datetime2,
    @EndUtc          datetime2,
    @AllDay          bit,
    @Location        nvarchar(255) = NULL,
    @Notes           nvarchar(max) = NULL,
    @ReminderMinutes int = NULL,
    @TmId            varchar(64) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- TODO: INSERT into the real event table and set @TmId to the new key, e.g.:
    -- INSERT INTO dbo.YOUR_EVENT_TABLE (...) VALUES (...);
    -- SET @TmId = CAST(SCOPE_IDENTITY() AS varchar(64));
    RAISERROR('TmSync_CreateEvent has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_UpdateEvent
    @TmId            varchar(64),
    @Subject         nvarchar(255),
    @StartUtc        datetime2,
    @EndUtc          datetime2,
    @AllDay          bit,
    @Location        nvarchar(255) = NULL,
    @Notes           nvarchar(max) = NULL,
    @ReminderMinutes int = NULL
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_UpdateEvent has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_DeleteEvent
    @TmId varchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_DeleteEvent has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

-- =============================================================================
-- 2. CONTACTS
-- =============================================================================
CREATE OR ALTER VIEW dbo.TmSync_Contacts AS
SELECT
    CAST(c.ContactID AS varchar(64)) AS TmId,            -- TODO
    c.StaffCode                      AS StaffCode,        -- TODO
    c.FirstName                      AS FirstName,        -- TODO
    c.LastName                       AS LastName,         -- TODO
    c.Company                        AS Company,          -- TODO
    c.Title                          AS JobTitle,         -- TODO
    c.Email                          AS Email1,           -- TODO
    CAST(NULL AS nvarchar(255))      AS Email2,           -- TODO (optional)
    c.BusinessPhone                  AS BusinessPhone,    -- TODO
    c.MobilePhone                    AS MobilePhone,      -- TODO
    c.HomePhone                      AS HomePhone,        -- TODO
    c.Street                         AS Street,           -- TODO
    c.City                           AS City,             -- TODO
    c.State                          AS State,            -- TODO
    c.PostalCode                     AS PostalCode,       -- TODO
    c.Country                        AS Country,          -- TODO
    c.Memo                           AS Notes,            -- TODO
    c.LastModifiedUtc                AS LastModifiedUtc,  -- TODO
    CAST(0 AS bit)                   AS IsDeleted         -- TODO
FROM dbo.YOUR_CONTACT_TABLE c;       -- TODO
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_CreateContact
    @StaffCode     varchar(20),
    @FirstName     nvarchar(100) = NULL,
    @LastName      nvarchar(100) = NULL,
    @Company       nvarchar(255) = NULL,
    @JobTitle      nvarchar(255) = NULL,
    @Email1        nvarchar(255) = NULL,
    @Email2        nvarchar(255) = NULL,
    @BusinessPhone nvarchar(50)  = NULL,
    @MobilePhone   nvarchar(50)  = NULL,
    @HomePhone     nvarchar(50)  = NULL,
    @Street        nvarchar(255) = NULL,
    @City          nvarchar(100) = NULL,
    @State         nvarchar(100) = NULL,
    @PostalCode    nvarchar(20)  = NULL,
    @Country       nvarchar(100) = NULL,
    @Notes         nvarchar(max) = NULL,
    @TmId          varchar(64) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_CreateContact has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_UpdateContact
    @TmId          varchar(64),
    @FirstName     nvarchar(100) = NULL,
    @LastName      nvarchar(100) = NULL,
    @Company       nvarchar(255) = NULL,
    @JobTitle      nvarchar(255) = NULL,
    @Email1        nvarchar(255) = NULL,
    @Email2        nvarchar(255) = NULL,
    @BusinessPhone nvarchar(50)  = NULL,
    @MobilePhone   nvarchar(50)  = NULL,
    @HomePhone     nvarchar(50)  = NULL,
    @Street        nvarchar(255) = NULL,
    @City          nvarchar(100) = NULL,
    @State         nvarchar(100) = NULL,
    @PostalCode    nvarchar(20)  = NULL,
    @Country       nvarchar(100) = NULL,
    @Notes         nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_UpdateContact has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_DeleteContact
    @TmId varchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_DeleteContact has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

-- =============================================================================
-- 3. TODOS (tasks)
-- =============================================================================
CREATE OR ALTER VIEW dbo.TmSync_Todos AS
SELECT
    CAST(t.TodoID AS varchar(64)) AS TmId,            -- TODO
    t.StaffCode                   AS StaffCode,        -- TODO
    t.Description                 AS Subject,          -- TODO
    t.DueDateUtc                  AS DueDateUtc,       -- TODO
    CAST(t.Done AS bit)           AS Completed,        -- TODO
    t.CompletedUtc                AS CompletedUtc,     -- TODO (optional)
    CAST(1 AS int)                AS Priority,         -- TODO: 0=low 1=normal 2=high
    t.Memo                        AS Notes,            -- TODO
    t.LastModifiedUtc             AS LastModifiedUtc,  -- TODO
    CAST(0 AS bit)                AS IsDeleted         -- TODO
FROM dbo.YOUR_TODO_TABLE t;       -- TODO
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_CreateTodo
    @StaffCode    varchar(20),
    @Subject      nvarchar(255),
    @DueDateUtc   datetime2 = NULL,
    @Completed    bit,
    @CompletedUtc datetime2 = NULL,
    @Priority     int = 1,
    @Notes        nvarchar(max) = NULL,
    @TmId         varchar(64) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_CreateTodo has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_UpdateTodo
    @TmId         varchar(64),
    @Subject      nvarchar(255),
    @DueDateUtc   datetime2 = NULL,
    @Completed    bit,
    @CompletedUtc datetime2 = NULL,
    @Priority     int = 1,
    @Notes        nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_UpdateTodo has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE dbo.TmSync_DeleteTodo
    @TmId varchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('TmSync_DeleteTodo has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO

-- =============================================================================
-- 4. EMAIL JOURNALING (one-way: Office 365 -> Time Matters)
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.TmSync_SaveEmail
    @StaffCode         varchar(20),
    @Direction         varchar(10),       -- 'Incoming' or 'Outgoing'
    @FromAddress       nvarchar(255) = NULL,
    @ToAddresses       nvarchar(max) = NULL,
    @CcAddresses       nvarchar(max) = NULL,
    @Subject           nvarchar(500) = NULL,
    @SentUtc           datetime2,
    @Body              nvarchar(max) = NULL,
    @InternetMessageId nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- TODO: INSERT into the Time Matters email/notes table, ideally auto-relating
    -- to the contact/matter by matching @FromAddress / @ToAddresses.
    RAISERROR('TmSync_SaveEmail has not been mapped to the local Time Matters schema yet.', 16, 1);
END
GO
