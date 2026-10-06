namespace RtspClientSharp.Web.Models;

public static class ApiErrorCodes
{
    public const string AuthenticationRequired = "authenticationRequired";
    public const string AccessRateLimited = "accessRateLimited";
    public const string InvalidAccessToken = "invalidAccessToken";
    public const string InvalidEditorPin = "invalidEditorPin";
    public const string InvalidEditorPinFormat = "invalidEditorPinFormat";
    public const string EditorAccessRequired = "editorAccessRequired";
    public const string SourceValidation = "sourceValidation";
    public const string SourceNotConfigured = "sourceNotConfigured";
    public const string Capacity = "capacity";
    public const string PresetNameRequired = "presetNameRequired";
    public const string PresetImport = "presetImport";
}
