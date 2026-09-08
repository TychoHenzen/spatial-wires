namespace SpatialCircuits.Runner;

public static class FixtureDiagnosticCodes
{
    public const string JsonInvalid = "FIXTURE_JSON_INVALID";
    public const string StructureInvalid = "FIXTURE_STRUCTURE_INVALID";
    public const string SchemaUnsupported = "FIXTURE_SCHEMA_UNSUPPORTED";
    public const string TraceSchemaUnsupported = "TRACE_SCHEMA_UNSUPPORTED";
    public const string IdInvalid = "FIXTURE_ID_INVALID";
    public const string ActionUnsupported = "FIXTURE_ACTION_UNSUPPORTED";
    public const string CasesRequired = "FIXTURE_CASES_REQUIRED";
    public const string CaseIdInvalid = "FIXTURE_CASE_ID_INVALID";
    public const string CaseIdDuplicate = "FIXTURE_CASE_ID_DUPLICATE";
    public const string DriveInvalid = "FIXTURE_DRIVE_INVALID";
    public const string ExpectedInvalid = "FIXTURE_EXPECTED_INVALID";
    public const string ScheduledDriveRequired = "FIXTURE_SCHEDULED_DRIVE_REQUIRED";
    public const string ScheduledDriveInvalid = "FIXTURE_SCHEDULED_DRIVE_INVALID";
    public const string PathRequired = "FIXTURE_PATH_REQUIRED";
    public const string FileNotFound = "FIXTURE_FILE_NOT_FOUND";
    public const string FileUnreadable = "FIXTURE_FILE_UNREADABLE";
}
