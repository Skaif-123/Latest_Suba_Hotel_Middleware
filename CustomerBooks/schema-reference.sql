-- =========================================================
-- CustomerBooksSync — SQL Server schema
-- Replaces the Zoho Catalyst Data Store tables: Customer, GST_Master,
-- accesToken. Run against an empty database (e.g. CustomerBooksSync).
-- =========================================================

IF DB_ID('CustomerBooksSync') IS NULL
BEGIN
    PRINT 'Run this script against your target database (CREATE DATABASE first if needed).';
END
GO

-- ---------------------------------------------------------
-- Customer
-- Id replaces Catalyst's ROWID as the row key that result
-- columns (booksID, Response, status) are written back against.
-- CustomerID remains the business key used by GST_Master.
-- ---------------------------------------------------------
IF OBJECT_ID('dbo.Customer', 'U') IS NOT NULL DROP TABLE dbo.Customer;
GO

CREATE TABLE dbo.Customer
(
    Id                  INT IDENTITY(1,1)   NOT NULL PRIMARY KEY,
    CustomerID          NVARCHAR(50)        NOT NULL,
    Company_Name        NVARCHAR(200)       NULL,
    First_Name          NVARCHAR(100)       NULL,
    Last_Name           NVARCHAR(100)       NULL,
    Email               NVARCHAR(200)       NULL,
    Phone               NVARCHAR(50)        NULL,
    Mobile              NVARCHAR(50)        NULL,

    -- Billing_Address / Shipping_Address do NOT exist as single columns —
    -- only the *_City / *_State / *_Pincode / *_Country / *_Phone columns
    -- below do, matching the original Catalyst table shape exactly.
    Billing_City        NVARCHAR(100)       NULL,
    Billing_State       NVARCHAR(100)       NULL,
    Billing_Pincode     NVARCHAR(20)        NULL,
    Billing_Country     NVARCHAR(100)       NULL,
    Billing_Phone       NVARCHAR(50)        NULL,

    Shipping_City       NVARCHAR(100)       NULL,
    Shipping_State      NVARCHAR(100)       NULL,
    Shipping_Pincode    NVARCHAR(20)        NULL,
    Shipping_Country    NVARCHAR(100)       NULL,
    Shipping_Phone      NVARCHAR(50)        NULL,

    Customer_Sub_Type   NVARCHAR(50)        NULL,
    GST_Treatment       NVARCHAR(50)        NULL,
    GST_NO              NVARCHAR(50)        NULL,
    Pan_No              NVARCHAR(50)        NULL,
    Currency            NVARCHAR(10)        NULL,
    Place_of_Supply     NVARCHAR(50)        NULL,
    Tax_Preference      BIT                 NULL,
    Code                NVARCHAR(50)        NULL,

    -- Result columns — the only three columns the sync job writes back to.
    -- Names/casing kept exactly as in the original Catalyst table.
    booksID             NVARCHAR(50)        NULL,
    Response            NVARCHAR(MAX)       NULL,
    status              NVARCHAR(20)        NULL,

    CreatedTime         DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedTime        DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE UNIQUE INDEX UX_Customer_CustomerID ON dbo.Customer(CustomerID);
GO

-- ---------------------------------------------------------
-- GST_Master — multiple GST registrations per customer.
-- ---------------------------------------------------------
IF OBJECT_ID('dbo.GST_Master', 'U') IS NOT NULL DROP TABLE dbo.GST_Master;
GO

CREATE TABLE dbo.GST_Master
(
    Id                  INT IDENTITY(1,1)   NOT NULL PRIMARY KEY,
    CustomerID          NVARCHAR(50)        NOT NULL,
    GST_No              NVARCHAR(50)        NULL,
    Place_Of_Supply     NVARCHAR(50)        NULL,
    Name                NVARCHAR(200)       NULL,
    isDefault           BIT                 NOT NULL DEFAULT 0,
    BooksID             NVARCHAR(50)        NULL   -- Zoho Books Tax Information ID
);
GO

CREATE INDEX IX_GST_Master_CustomerID ON dbo.GST_Master(CustomerID);
GO

-- ---------------------------------------------------------
-- AccessTokens — replaces the Catalyst `accesToken` table.
-- The web app selects the most recent row for application = 'Books',
-- exactly like the original ZCQL query
-- (SELECT * FROM accesToken WHERE application = 'Books' ORDER BY
-- CREATEDTIME DESC LIMIT 1). Populate/refresh this table via your own
-- Zoho OAuth refresh-token flow (not in scope of this conversion).
-- ---------------------------------------------------------
IF OBJECT_ID('dbo.AccessTokens', 'U') IS NOT NULL DROP TABLE dbo.AccessTokens;
GO

CREATE TABLE dbo.AccessTokens
(
    Id                  INT IDENTITY(1,1)   NOT NULL PRIMARY KEY,
    Application         NVARCHAR(50)        NOT NULL,
    AccessToken         NVARCHAR(500)       NOT NULL,
    CreatedTime         DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX IX_AccessTokens_Application_CreatedTime
    ON dbo.AccessTokens(Application, CreatedTime DESC);
GO
