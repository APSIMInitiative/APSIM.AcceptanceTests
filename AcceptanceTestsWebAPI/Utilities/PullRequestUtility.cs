using System.Globalization;
using AcceptanceTestsWebAPI.Requests;

namespace AcceptanceTestsWebAPI.Utilities;

/// <summary>
/// Utilities for AcceptanceJobEntity 
/// </summary>
public static class PullRequestUtility
{
    /// <summary>Validates a start request and parses its UTC timestamp.</summary>
    internal static List<string> ValidateStartRequest(
        PullRequestEndpoints.StartJSON request, out DateTime parsedDateTime)
    {
        List<string> errors = new();
        if (string.IsNullOrEmpty(request.PullRequest))
            errors.Add("Json fragment missing pullRequest number");

        if (string.IsNullOrEmpty(request.Commit))
            errors.Add("Json fragment missing commit hash");

        if (request.Paths == null || request.Paths.Length == 0 ||
            request.Paths.Any(string.IsNullOrWhiteSpace))
            errors.Add("A non-empty list of job paths is required");

        if (request.Paths != null && request.Paths.Distinct(StringComparer.Ordinal).Count() != request.Paths.Length)
            errors.Add("Job paths must be unique within a run");

        bool isDateTimeValid = DateTime.TryParseExact(
            request.Datetime,
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsedDateTime);
        if (string.IsNullOrEmpty(request.Datetime))
            errors.Add("Json fragment missing dateTime string");
        else if (!isDateTimeValid)
            errors.Add("dateTime must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss format");

        return errors;
    }

    /// <summary>
    /// Creates a name for an AcceptanceJobEntity
    /// </summary>
    /// <param name="prNumber"></param>
    /// <param name="datetime"></param>
    /// <returns></returns>
    public static string CreateName(string prNumber, DateTime datetime)
    {
        return $"{datetime.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture)}-acceptance-tests-pr-{prNumber}";
    }

    /// <summary>
    /// Creates a ContainerName from an AcceptanceJobEntitys' Name.
    /// </summary>
    /// <param name="acceptanceJobEntityName"></param>
    /// <returns></returns>
    public static string CreateAzureStorageContainerName(string acceptanceJobEntityName )
    {
        return $"{acceptanceJobEntityName}-storage";
    }
}