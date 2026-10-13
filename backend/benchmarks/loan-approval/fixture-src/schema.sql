CREATE TABLE dbo.Applicants (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    FullName NVARCHAR(200) NOT NULL,
    AnnualIncome DECIMAL(18,2) NOT NULL
);

CREATE TABLE dbo.LoanApplications (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ApplicantId UNIQUEIDENTIFIER NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(50) NOT NULL,
    CONSTRAINT FK_LoanApplications_Applicants FOREIGN KEY (ApplicantId) REFERENCES dbo.Applicants(Id)
);

CREATE TABLE dbo.ApprovalDecisions (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    LoanApplicationId UNIQUEIDENTIFIER NOT NULL,
    Approved BIT NOT NULL,
    DecidedBy NVARCHAR(200) NOT NULL,
    CONSTRAINT FK_ApprovalDecisions_LoanApplications FOREIGN KEY (LoanApplicationId) REFERENCES dbo.LoanApplications(Id)
);

CREATE TABLE dbo.AuditHistory (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    LoanApplicationId UNIQUEIDENTIFIER NOT NULL,
    Action NVARCHAR(200) NOT NULL,
    CONSTRAINT FK_AuditHistory_LoanApplications FOREIGN KEY (LoanApplicationId) REFERENCES dbo.LoanApplications(Id)
);

CREATE VIEW dbo.vw_LoanApplicationSummary AS
SELECT la.Id, la.Amount, la.Status, a.FullName, a.AnnualIncome
FROM dbo.LoanApplications la
JOIN dbo.Applicants a ON a.Id = la.ApplicantId;
GO

CREATE PROCEDURE dbo.sp_ApproveLoanApplication
    @LoanApplicationId UNIQUEIDENTIFIER,
    @DecidedBy NVARCHAR(200)
AS
BEGIN
    UPDATE dbo.LoanApplications
    SET Status = 'Approved'
    WHERE Id = @LoanApplicationId;

    INSERT INTO dbo.ApprovalDecisions (Id, LoanApplicationId, Approved, DecidedBy)
    SELECT NEWID(), @LoanApplicationId, 1, @DecidedBy;

    EXEC dbo.sp_RecordAuditEntry @LoanApplicationId, 'Approved';
END
GO

CREATE PROCEDURE dbo.sp_RecordAuditEntry
    @LoanApplicationId UNIQUEIDENTIFIER,
    @Action NVARCHAR(200)
AS
BEGIN
    INSERT INTO dbo.AuditHistory (Id, LoanApplicationId, Action)
    SELECT NEWID(), @LoanApplicationId, @Action;
END
GO

CREATE PROCEDURE dbo.sp_RejectLoanApplication
    @LoanApplicationId UNIQUEIDENTIFIER,
    @DecidedBy NVARCHAR(200),
    @NotifyProcedureName NVARCHAR(200)
AS
BEGIN
    UPDATE dbo.LoanApplications
    SET Status = 'Rejected'
    WHERE Id = @LoanApplicationId;

    -- Deliberate dynamic SQL: the called procedure name is not statically known,
    -- exercising the unresolved-reference case for SQL Lineage.
    EXEC(@NotifyProcedureName);
END
GO
