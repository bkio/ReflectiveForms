// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using Range = ReflectiveForms.Core.Attributes.Fields.Range;

namespace ReflectiveForms.Sample1.Models;

/// <summary>
/// Address sub-model – demonstrates a Group with full-width layout.
/// </summary>
internal class AddressModel : BaseModel
{
    [JsonProperty("street"),
     Text(
         label: "Street Address",
         instructions: "",
         mandatory: true,
         placeholderText: "123 Main St")]
    public string Street = "";

    [JsonProperty("city"),
     Text(
         label: "City",
         instructions: "",
         mandatory: true,
         placeholderText: "San Francisco")]
    public string City = "";

    [JsonProperty("state"),
     Text(
         label: "State / Province",
         instructions: "",
         mandatory: false,
         placeholderText: "CA")]
    public string State = "";

    [JsonProperty("postal_code"),
     Text(
         label: "Postal Code",
         instructions: "",
         mandatory: true,
         placeholderText: "94102")]
    public string PostalCode = "";

    [JsonProperty("country"),
     Select(
         label: "Country",
         instructions: "",
         defaultValue: "US",
         choices:
         [
             "US : United States",
             "CA : Canada",
             "GB : United Kingdom",
             "DE : Germany",
             "FR : France",
             "AU : Australia",
             "JP : Japan",
             "BR : Brazil",
             "IN : India",
             "other : Other"
         ])]
    public string Country = "US";
}

/// <summary>
/// Social media profile link – used in a Repeater.
/// </summary>
internal class SocialLinkModel : BaseModel
{
    [JsonProperty("platform"),
     Select(
         label: "Platform",
         instructions: "",
         defaultValue: "linkedin",
         choices:
         [
             "linkedin : LinkedIn",
             "twitter : Twitter / X",
             "github : GitHub",
             "website : Personal Website",
             "youtube : YouTube",
             "mastodon : Mastodon",
             "other : Other"
         ])]
    public string Platform = "linkedin";

    [JsonProperty("profile_url"),
     Url(
         label: "Profile URL",
         instructions: "",
         mandatory: true,
         placeholderText: "https://linkedin.com/in/username")]
    public string ProfileUrl = "";
}

/// <summary>
/// Emergency contact – used in a Repeater.
/// </summary>
internal class EmergencyContactModel : BaseModel
{
    [JsonProperty("contact_name"),
     Text(
         label: "Contact Name",
         instructions: "",
         mandatory: true,
         placeholderText: "Jane Doe")]
    public string ContactName = "";

    [JsonProperty("relationship"),
     Select(
         label: "Relationship",
         instructions: "",
         defaultValue: "spouse",
         choices:
         [
             "spouse : Spouse / Partner",
             "parent : Parent",
             "sibling : Sibling",
             "friend : Friend",
             "other : Other"
         ])]
    public string Relationship = "spouse";

    [JsonProperty("phone"),
     Text(
         label: "Phone Number",
         instructions: "",
         mandatory: true,
         placeholderText: "+1 (555) 123-4567")]
    public string Phone = "";

    [JsonProperty("email"),
     Email(
         label: "Email",
         instructions: "",
         mandatory: false,
         placeholderText: "jane.doe@example.com")]
    public string Email = "";
}

/// <summary>
/// Team member / employee entity – demonstrates:
/// - Email field
/// - Number with step size and default value
/// - Range slider
/// - Group with Grid3ElementsInRow for address
/// - Repeater for social links (with accordion)
/// - Repeater for emergency contacts (min 1, max 3)
/// - Relation to another entity type (blog-post)
/// - Checkbox with non-default value
/// - MediaSourceBase64 for avatar
/// - DisplayCondition for conditional fields
/// - Text with default value
/// </summary>
internal class TeamMemberModel : EntityFieldsModel
{
    [JsonProperty("email"),
     Email(
         label: "Work Email",
         instructions: "Primary work email address for this team member.",
         mandatory: true,
         placeholderText: "name@company.com")]
    public string Email = "";

    [JsonProperty("avatar"),
     MediaSourceBase64(
         label: "Profile Photo",
         instructions: "Upload a square photo (recommended 400×400px).",
         mandatory: false)]
    public string Avatar = "";

    [JsonProperty("department"),
     Select(
         label: "Department",
         instructions: "",
         defaultValue: "engineering",
         choices:
         [
             "engineering : Engineering",
             "design : Design",
             "product : Product",
             "marketing : Marketing",
             "sales : Sales",
             "hr : Human Resources",
             "finance : Finance",
             "operations : Operations",
             "executive : Executive"
         ])]
    public string Department = "engineering";

    [JsonProperty("job_title"),
     Text(
         label: "Job Title",
         instructions: "",
         mandatory: true,
         defaultValue: "Software Engineer",
         placeholderText: "e.g. Senior Backend Engineer")]
    public string JobTitle = "Software Engineer";

    [JsonProperty("years_of_experience"),
     Number(
         label: "Years of Experience",
         instructions: "Total professional experience in years.",
         mandatory: false,
         placeholderText: "e.g. 5",
         defaultValue: 0,
         minimumMaximumValues: [0, 50],
         stepSize: 0.5)]
    public double YearsOfExperience;

    [JsonProperty("performance_score"),
     Range(
         label: "Performance Score",
         instructions: "Annual performance rating on a scale of 1 to 10.",
         mandatory: false,
         defaultValue: 5,
         minimumValue: 1,
         maximumValue: 10,
         stepSize: 0.5)]
    public double PerformanceScore = 5;

    [JsonProperty("is_remote"),
     Checkbox(
         label: "Remote Worker",
         instructions: "Check if this team member works remotely.",
         defaultValue: false)]
    public bool IsRemote;

    [JsonProperty("office_address"),
     DisplayCondition("is_remote == false"),
     Group(
         label: "Office Address",
         instructions: "Physical office location. Only required for on-site team members.",
         groupFor: typeof(AddressModel),
         renderStyle: GroupRenderStyle.Grid3ElementsInRow)]
    public AddressModel OfficeAddress = new();

    [JsonProperty("bio"),
     WysiwygEditor(
         label: "Biography",
         instructions: "A short bio that appears on the team page. Supports rich text.",
         mandatory: false)]
    public string Bio = "";

    [JsonProperty("social_links", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Social Links",
         instructions: "Add links to professional profiles.",
         repeaterFor: typeof(SocialLinkModel),
         addButtonLabel: "Add Social Link",
         useAccordion: RepeatUseAccordion.Yes)]
    public List<SocialLinkModel> SocialLinks = [];

    [JsonProperty("emergency_contacts", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Emergency Contacts",
         instructions: "At least one emergency contact is required. Maximum of 3.",
         repeaterFor: typeof(EmergencyContactModel),
         addButtonLabel: "Add Emergency Contact",
         minimumRows: 1,
         maximumRows: 3,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<EmergencyContactModel> EmergencyContacts = [];

    [JsonProperty("favorite_blog_post"),
     Relation(
         label: "Favorite Blog Post",
         instructions: "Link to this member's favorite blog post (optional).",
         mandatory: false,
         relationEntityName: "blog-post",
         isRelationEntityNotExistsOk: true)]
    public int FavoriteBlogPostId = -1;

    [JsonProperty("hire_date"),
     DatePicker(
         label: "Hire Date",
         instructions: "The date this team member joined the company.",
         mandatory: true,
         dateFormat: "yyyyMMdd")]
    public string HireDate = "";

    [JsonProperty("salary"),
     Number(
         label: "Annual Salary",
         instructions: "Annual compensation in USD.",
         mandatory: false,
         placeholderText: "e.g. 120000",
         minimumMaximumValues: [0, 10000000],
         stepSize: 1000)]
    public double Salary;
}
