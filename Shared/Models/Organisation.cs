using System.ComponentModel.DataAnnotations;
using AcceptanceTestsShared.Enums;

namespace AcceptanceTestsShared.Models;
public class Organisation
{

    /// <summary>
    /// The unique identifier of the user. 
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the organisation. This is a required field and should be 
    /// unique across all organisations in the system.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A list of email addresses or domains associated with the organisation. 
    /// </summary>
    public List<string> Emails { get; set; } = new List<string>();

    /// <summary>
    /// The licence status of the organisation. This indicates whether the 
    /// organisation has an active licence, is pending review, or is inactive.
    /// </summary>
    public OrganisationLicenceStatus LicenceStatus { get; set; } = OrganisationLicenceStatus.Pending;

    /// <summary>
    /// The contact name for the organisation. This is a required field and 
    /// should be a valid name of person who can be contacted regarding the 
    /// organisation's licence and AcceptanceTests.
    /// </summary>
    [Required]
    public string ContactName { get; set; } = string.Empty;

    /// <summary>
    /// The contact email for the organisation. This is a required field and 
    /// should be a valid email address that can be used to contact the 
    /// organisation regarding their licence and AcceptanceTests.
    /// </summary>
    [Required]
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// The contact phone number for the organisation. This is a required field and 
    /// should be a valid phone number that can be used to contact the organisation 
    /// regarding their licence and AcceptanceTests.
    /// </summary>
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// The contact address for the organisation. This is a required field and
    /// should be a valid address that can be used to contact the organisation 
    /// regarding their licence and AcceptanceTests.
    /// </summary>
    [Required]
    public string ContactAddress { get; set; } = string.Empty;

    /// <summary>
    /// The licence pathway for the organisation. This is a required field and 
    /// should be a valid value from the LicencePathway enum.
    /// </summary>
    [Required]
    public LicencePathway LicencePathway { get; set; }

    /// <summary>
    /// The annual turnover category for the organisation. This is a required field and
    /// should be a valid value from the AnnualTurnover enum.
    /// </summary>
    [Required]
    public AnnualTurnover AnnualTurnover { get; set; }

    /// <summary>
    /// The date and time when the organisation registered. 
    /// This is automatically set to the current date and time 
    /// when the organisation object is created.
    /// </summary>
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// A string representation of the Organisation object, including the name, 
    /// contact information, licence status, licence pathway, 
    /// annual turnover, and date created.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return $"Organisation: {Name}," +
        $"Contact: {ContactName}" +
        $"Contact Email: {ContactEmail},"+
        $"Contact Phone: {ContactPhone}, " +
        $"Contact Address: {ContactAddress}, " +
        $"LicenceStatus: {LicenceStatus}, " +
        $"LicencePathway: {LicencePathway}, " +
        $"AnnualTurnover: {AnnualTurnover}, " +
        $"DateCreated: {DateCreated}";
    }

    /// <summary>
    /// Determines if the provided email address belongs to the organisation by
    /// checking if it matches any of the email addresses or domains associated 
    /// with the organisation.
    /// </summary>
    /// <param name="email">The email address to check.</param>
    /// <returns>True if the email belongs to the organisation; otherwise, false.</returns>
    public bool IsFromOrganisation(string email)
    {
        string emailDomain = email.Split('@').LastOrDefault()?.Trim() ?? string.Empty;
        bool isMatch = false;
        foreach (string registeredEmail in Emails)
        {
            string registeredEmailTrimmed = registeredEmail.Split('@').LastOrDefault()?.Trim() ?? string.Empty;
            if (string.Equals(registeredEmailTrimmed, email, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(registeredEmailTrimmed, emailDomain, StringComparison.OrdinalIgnoreCase))
            {
                isMatch = true;
                break;
            }
        }
        return isMatch;
    }



}