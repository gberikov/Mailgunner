namespace Mailgunner;

/// <summary>
/// The Mailgunner client resolved from the dependency-injection container. This is the entry
/// point that every other Mailgunner capability builds on; operational members (sending,
/// suppressions, webhooks) are introduced by later features.
/// </summary>
public interface IMailgunnerClient
{
}
