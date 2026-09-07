namespace SpatialCircuits.Core;

public enum DiagnosticSeverity
{
    Error,
    Warning
}

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Path,
    string Message);

public static class DiagnosticCodes
{
    public const string InvalidCircuitId = "ID_CIRCUIT_INVALID";
    public const string InvalidDefinitionId = "ID_DEFINITION_INVALID";
    public const string InvalidComponentId = "ID_COMPONENT_INVALID";
    public const string InvalidPortId = "ID_PORT_INVALID";
    public const string InvalidOwnerId = "ID_OWNER_INVALID";
    public const string InvalidBehaviorId = "BEHAVIOR_ID_INVALID";
    public const string ClrBehaviorType = "BEHAVIOR_ID_CLR_TYPE";
    public const string DuplicateDefinition = "DEFINITION_ID_DUPLICATE";
    public const string DuplicateComponent = "COMPONENT_ID_DUPLICATE";
    public const string DuplicatePort = "PORT_ID_DUPLICATE";
    public const string MissingDefinitionReference = "REFERENCE_DEFINITION_MISSING";
    public const string MissingComponentReference = "REFERENCE_COMPONENT_MISSING";
    public const string InvalidParameter = "PARAMETER_INVALID";
    public const string UnknownParameter = "PARAMETER_UNKNOWN";
    public const string OwnershipConflict = "OWNERSHIP_CONFLICT";
}
