using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary><c>resolution</c>: completed, declined or withdrawn.</summary>
public sealed record ClosePrivacyRequestRequest(PrivacyRequestResolution? Resolution);
