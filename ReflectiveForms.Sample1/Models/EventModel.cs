// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using Range = ReflectiveForms.Core.Attributes.Fields.Range;

namespace ReflectiveForms.Sample1.Models;

/// <summary>
/// Session/talk details for the event – used in a Repeater.
/// </summary>
internal class EventSessionModel : BaseModel
{
    [JsonProperty("session_title"),
     Text(
         label: "Session Title",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Keynote: Future of AI")]
    public string SessionTitle = "";

    [JsonProperty("speaker_name"),
     Text(
         label: "Speaker Name",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Dr. Jane Smith")]
    public string SpeakerName = "";

    [JsonProperty("speaker_email"),
     Email(
         label: "Speaker Email",
         instructions: "Contact email for the speaker.",
         mandatory: false,
         placeholderText: "speaker@conference.com")]
    public string SpeakerEmail = "";

    [JsonProperty("session_date"),
     DatePicker(
         label: "Session Date",
         instructions: "",
         mandatory: true,
         dateFormat: "yyyyMMdd")]
    public string SessionDate = "";

    [JsonProperty("duration_minutes"),
     Number(
         label: "Duration (minutes)",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. 45",
         defaultValue: 30,
         minimumMaximumValues: [5, 480],
         stepSize: 5)]
    public double DurationMinutes = 30;

    [JsonProperty("session_type"),
     Select(
         label: "Session Type",
         instructions: "",
         defaultValue: "talk",
         choices:
         [
             "keynote : Keynote",
             "talk : Talk",
             "workshop : Workshop",
             "panel : Panel Discussion",
             "lightning : Lightning Talk",
             "break : Break / Networking"
         ])]
    public string SessionType = "talk";

    [JsonProperty("session_description"),
     TextArea(
         label: "Session Description",
         instructions: "",
         mandatory: false,
         placeholderText: "Brief description of this session...")]
    public string SessionDescription = "";
}

/// <summary>
/// Sponsor entry – used in a Repeater.
/// </summary>
internal class EventSponsorModel : BaseModel
{
    [JsonProperty("sponsor_name"),
     Text(
         label: "Sponsor Name",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Acme Corp")]
    public string SponsorName = "";

    [JsonProperty("sponsor_tier"),
     Select(
         label: "Sponsor Tier",
         instructions: "",
         defaultValue: "silver",
         choices:
         [
             "platinum : Platinum",
             "gold : Gold",
             "silver : Silver",
             "bronze : Bronze",
             "community : Community Partner"
         ])]
    public string SponsorTier = "silver";

    [JsonProperty("sponsor_logo"),
     MediaSourceBase64(
         label: "Sponsor Logo",
         instructions: "Upload the sponsor's logo.",
         mandatory: false)]
    public string SponsorLogo = "";

    [JsonProperty("sponsor_url"),
     Url(
         label: "Sponsor Website",
         instructions: "",
         mandatory: false,
         placeholderText: "https://sponsor.com")]
    public string SponsorUrl = "";
}

/// <summary>
/// Venue details – demonstrates Group with Grid3ElementsInRow.
/// </summary>
internal class VenueModel : BaseModel
{
    [JsonProperty("venue_name"),
     Text(
         label: "Venue Name",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Convention Center")]
    public string VenueName = "";

    [JsonProperty("venue_address"),
     Group(
         label: "Venue Address",
         instructions: "",
         groupFor: typeof(AddressModel),
         renderStyle: GroupRenderStyle.Grid3ElementsInRow)]
    public AddressModel VenueAddress = new();

    [JsonProperty("capacity"),
     Number(
         label: "Venue Capacity",
         instructions: "Maximum number of attendees.",
         mandatory: false,
         placeholderText: "e.g. 500",
         minimumMaximumValues: [1, 100000])]
    public double Capacity;

    [JsonProperty("venue_url"),
     Url(
         label: "Venue Website",
         instructions: "",
         mandatory: false,
         placeholderText: "https://venue.com")]
    public string VenueUrl = "";
}

/// <summary>
/// Event entity – demonstrates:
/// - Deeply nested Groups (VenueModel → AddressModel)
/// - Repeater for sessions with all field types inside
/// - Repeater for sponsors with media
/// - DisplayCondition for online vs. in-person
/// - Range slider for ticket pricing
/// - Multiple DatePickers
/// - Email field
/// - Complex Select with many options
/// - No parent-child, no tags — shows different entity configuration
/// </summary>
internal class EventModel : EntityFieldsModel
{
    [JsonProperty("description"),
     WysiwygEditor(
         label: "Event Description",
         instructions: "Describe the event — its purpose, audience, and what attendees can expect.",
         mandatory: true)]
    public string Description = "";

    [JsonProperty("event_type"),
     Select(
         label: "Event Type",
         instructions: "",
         defaultValue: "conference",
         choices:
         [
             "conference : Conference",
             "meetup : Meetup",
             "workshop : Workshop",
             "webinar : Webinar",
             "hackathon : Hackathon",
             "summit : Summit",
             "tradeshow : Trade Show"
         ])]
    public string EventType = "conference";

    [JsonProperty("start_date"),
     DatePicker(
         label: "Start Date",
         instructions: "When the event begins.",
         mandatory: true,
         dateFormat: "yyyyMMdd")]
    public string StartDate = "";
    public Task<object?> StartDate___DynamicDefaultValueAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(DateTime.Now.ToString("yyyyMMdd"));
    }

    [JsonProperty("end_date"),
     DatePicker(
         label: "End Date",
         instructions: "When the event ends.",
         mandatory: true,
         dateFormat: "yyyyMMdd")]
    public string EndDate = "";
    public Task<object?> EndDate___DynamicDefaultValueAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(DateTime.Now.AddDays(1).ToString("yyyyMMdd"));
    }

    [JsonProperty("is_online"),
     Checkbox(
         label: "Online Event",
         instructions: "Check this if the event is virtual / online.",
         defaultValue: false)]
    public bool IsOnline;

    [JsonProperty("meeting_url"),
     DisplayCondition("is_online == true"),
     Url(
         label: "Meeting URL",
         instructions: "Link to the virtual meeting room.",
         mandatory: true,
         placeholderText: "https://zoom.us/j/123456789")]
    public string MeetingUrl = "";

    [JsonProperty("venue"),
     DisplayCondition("is_online == false"),
     Group(
         label: "Venue Details",
         instructions: "Location of the in-person event.",
         groupFor: typeof(VenueModel))]
    public VenueModel Venue = new();

    [JsonProperty("max_attendees"),
     Number(
         label: "Maximum Attendees",
         instructions: "Set to 0 for unlimited.",
         mandatory: false,
         placeholderText: "e.g. 200",
         defaultValue: 0,
         minimumMaximumValues: [0, 100000])]
    public double MaxAttendees;

    [JsonProperty("ticket_price"),
     Range(
         label: "Ticket Price (USD)",
         instructions: "Set to 0 for free events.",
         mandatory: false,
         defaultValue: 0,
         minimumValue: 0,
         maximumValue: 5000,
         stepSize: 25)]
    public double TicketPrice;

    [JsonProperty("registration_email"),
     Email(
         label: "Registration Contact Email",
         instructions: "Email address for registration inquiries.",
         mandatory: true,
         placeholderText: "events@company.com")]
    public string RegistrationEmail = "";

    [JsonProperty("banner_image"),
     MediaSourceBase64(
         label: "Event Banner",
         instructions: "A wide banner image for the event page (recommended 1200×400px).",
         mandatory: false)]
    public string BannerImage = "";

    [JsonProperty("sessions", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Sessions / Agenda",
         instructions: "Define the schedule of talks, workshops, and breaks.",
         repeaterFor: typeof(EventSessionModel),
         addButtonLabel: "Add Session",
         groupRenderStyle: GroupRenderStyle.Full,
         useAccordion: RepeatUseAccordion.Yes)]
    public List<EventSessionModel> Sessions = [];

    [JsonProperty("sponsors", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Sponsors",
         instructions: "Add event sponsors grouped by tier.",
         repeaterFor: typeof(EventSponsorModel),
         addButtonLabel: "Add Sponsor",
         minimumRows: 0,
         maximumRows: 30,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<EventSponsorModel> Sponsors = [];

    [JsonProperty("event_coordinator"),
     Relation(
         label: "Event Coordinator",
         instructions: "The team member responsible for organizing this event.",
         mandatory: false,
         relationEntityName: "team-member",
         isRelationEntityNotExistsOk: true)]
    public int EventCoordinatorId = -1;

    [JsonProperty("registration_url"),
     Url(
         label: "Registration Page URL",
         instructions: "External registration link (e.g., Eventbrite, Meetup).",
         mandatory: false,
         placeholderText: "https://eventbrite.com/e/your-event")]
    public string RegistrationUrl = "";
}
