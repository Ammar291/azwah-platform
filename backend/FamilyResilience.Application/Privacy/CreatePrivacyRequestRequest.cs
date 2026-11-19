using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>
/// <c>type</c>: access, correction or erasure. <c>memberId</c>: a member the caller may act for; their own member when
/// left out. There is no free-text field: details go through the programme's support channel.
/// </summary>
public sealed record CreatePrivacyRequestRequest(PrivacyRequestType? Type, Guid? MemberId);
