namespace Mailgunner.Sample;

/// <summary>
/// Builds the conference-invitation batch the sample sends. The per-recipient personalization
/// (<c>name</c>, <c>ticket</c>, <c>link</c>) is deliberately <b>in-source and visible</b> (US1 #3):
/// each configured address is paired, by position, with one of the illustrative attendees below.
/// </summary>
/// <remarks>
/// The library's batch API is stored-template-only: it emits <c>template</c> + global
/// <c>t:variables</c> + per-recipient <c>recipient-variables</c> (keyed by bare address). Mailgun's
/// stored Handlebars template reads <c>{{name}}</c>/<c>{{ticket}}</c>/<c>{{link}}</c> from
/// <c>t:variables</c>, while batch per-recipient values arrive as <c>%recipient.var%</c>. We bridge
/// the two — with the library as-is — by mapping each template variable to its recipient token in the
/// global <see cref="MailgunBatchMessage.TemplateVariables"/>, so Mailgun resolves a distinct value
/// per recipient.
/// </remarks>
public static class ConferenceInvitation
{
    /// <summary>The stored-template variable names this scenario personalizes (also used by the README quickstart).</summary>
    public static readonly IReadOnlyList<string> VariableNames = new[] { "name", "ticket", "link" };

    /// <summary>
    /// The illustrative, in-source attendees. Each configured recipient address is paired with one of
    /// these (by position, cycling if there are more addresses than attendees). No real personal data.
    /// </summary>
    public static readonly IReadOnlyList<Attendee> SampleAttendees = new[]
    {
        new Attendee(name: "Ada Lovelace", ticket: "A-1024", link: "https://conf.example/t/A-1024"),
        new Attendee(name: "Alan Turing", ticket: "A-2048", link: "https://conf.example/t/A-2048"),
        new Attendee(name: "Grace Hopper", ticket: "A-4096", link: "https://conf.example/t/A-4096"),
    };

    /// <summary>
    /// Builds the personalized conference-invitation <see cref="MailgunBatchMessage"/> from the
    /// resolved settings: the stored template, the global recipient-variables bridge, and one
    /// <see cref="BatchRecipient"/> per configured address carrying its own name/ticket/link.
    /// </summary>
    /// <param name="settings">The resolved sample settings (sender, template, recipient addresses).</param>
    /// <returns>The batch ready for <see cref="IMailgunnerClient.SendBatchAsync"/>.</returns>
    public static MailgunBatchMessage Build(SampleSettings settings)
    {
        var batch = new MailgunBatchMessage
        {
            From = settings.From,
            Subject = "You're invited!",
            Template = settings.Template,
            GenerateTextFromTemplate = true,
        };

        // The bridge: each stored-template variable reads its per-recipient value from recipient-variables.
        batch.TemplateVariables["name"] = "%recipient.name%";
        batch.TemplateVariables["ticket"] = "%recipient.ticket%";
        batch.TemplateVariables["link"] = "%recipient.link%";

        for (var i = 0; i < settings.Recipients.Count; i++)
        {
            var attendee = SampleAttendees[i % SampleAttendees.Count];
            var recipient = new BatchRecipient(settings.Recipients[i]);
            recipient.Variables["name"] = attendee.Name;
            recipient.Variables["ticket"] = attendee.Ticket;
            recipient.Variables["link"] = attendee.Link;
            batch.Recipients.Add(recipient);
        }

        return batch;
    }

    /// <summary>One illustrative attendee's visible, in-source personalization values.</summary>
    public sealed class Attendee
    {
        /// <summary>Initializes a new instance of the <see cref="Attendee"/> class.</summary>
        public Attendee(string name, string ticket, string link)
        {
            Name = name;
            Ticket = ticket;
            Link = link;
        }

        /// <summary>Gets the attendee's display name (rendered as <c>{{name}}</c>).</summary>
        public string Name { get; }

        /// <summary>Gets the attendee's ticket number (rendered as <c>{{ticket}}</c>).</summary>
        public string Ticket { get; }

        /// <summary>Gets the attendee's personal link (rendered as <c>{{link}}</c>).</summary>
        public string Link { get; }
    }
}
