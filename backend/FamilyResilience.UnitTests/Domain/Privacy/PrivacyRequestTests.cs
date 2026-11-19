using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Privacy;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Privacy;

public sealed class PrivacyRequestTests
{
    private static readonly HouseholdId Household = HouseholdId.From(Guid.CreateVersion7());
    private static readonly FamilyMemberId Member = FamilyMemberId.From(Guid.CreateVersion7());

    private static PrivacyRequest Open(PrivacyRequestType type = PrivacyRequestType.Erasure) =>
        Ok(PrivacyRequest.Create(Household, Member, type, "subject-1", Now));

    [Fact]
    public void A_new_request_is_open_for_its_member_and_names_who_asked()
    {
        var request = Open(PrivacyRequestType.Correction);

        Assert.Equal((Household, Member, PrivacyRequestType.Correction), (request.HouseholdId, request.FamilyMemberId, request.Type));
        Assert.Equal(PrivacyRequestStatus.Open, request.Status);
        Assert.Equal(("subject-1", Now), (request.RequestedBy, request.CreatedAt));
        Assert.Null(request.ClosedAt);
        Assert.Null(request.ClosedBy);
        Assert.Null(request.Resolution);
    }

    [Fact]
    public void A_request_needs_a_known_type_a_requester_and_a_utc_time()
    {
        Assert.Equal(PrivacyErrors.TypeInvalid, PrivacyRequest.Create(Household, Member, (PrivacyRequestType)99, "subject-1", Now).Error);
        Assert.Equal(PrivacyErrors.ActorInvalid, PrivacyRequest.Create(Household, Member, PrivacyRequestType.Access, " ", Now).Error);
        Assert.Equal(PrivacyErrors.ActorInvalid, PrivacyRequest.Create(Household, Member, PrivacyRequestType.Access, null, Now).Error);
        Assert.Equal(
            PrivacyErrors.ActorInvalid,
            PrivacyRequest.Create(Household, Member, PrivacyRequestType.Access, new string('s', PrivacyRequest.MaxActorLength + 1), Now).Error);
        Assert.Equal(
            CommonErrors.TimeNotUtc,
            PrivacyRequest.Create(Household, Member, PrivacyRequestType.Access, "subject-1", Now.ToOffset(TimeSpan.FromHours(4))).Error);
    }

    [Fact]
    public void Closing_records_the_resolution_who_closed_it_and_when()
    {
        var request = Open();

        Ok(request.Close(PrivacyRequestResolution.Declined, "staff-1", Now.AddDays(2)));

        Assert.Equal(PrivacyRequestStatus.Closed, request.Status);
        Assert.Equal((PrivacyRequestResolution.Declined, "staff-1", Now.AddDays(2)), (request.Resolution, request.ClosedBy, request.ClosedAt));
    }

    [Fact]
    public void A_closed_request_never_changes()
    {
        var request = Open();
        Ok(request.Close(PrivacyRequestResolution.Completed, "staff-1", Now.AddDays(1)));

        Assert.Equal(PrivacyErrors.RequestAlreadyClosed, request.Close(PrivacyRequestResolution.Withdrawn, "staff-2", Now.AddDays(3)).Error);
        Assert.Equal((PrivacyRequestResolution.Completed, "staff-1", Now.AddDays(1)), (request.Resolution, request.ClosedBy, request.ClosedAt));
    }

    [Fact]
    public void Closing_needs_a_known_resolution_a_closer_and_a_utc_time()
    {
        var request = Open();

        Assert.Equal(PrivacyErrors.ResolutionInvalid, request.Close((PrivacyRequestResolution)0, "staff-1", Now).Error);
        Assert.Equal(PrivacyErrors.ActorInvalid, request.Close(PrivacyRequestResolution.Completed, "", Now).Error);
        Assert.Equal(CommonErrors.TimeNotUtc, request.Close(PrivacyRequestResolution.Completed, "staff-1", Now.ToOffset(TimeSpan.FromHours(4))).Error);
        Assert.Equal(PrivacyRequestStatus.Open, request.Status);
    }

    [Fact]
    public void A_closing_time_before_the_request_is_moved_up_to_the_request_time()
    {
        var request = Open();

        Ok(request.Close(PrivacyRequestResolution.Completed, "staff-1", Now.AddMinutes(-5)));

        Assert.Equal(Now, request.ClosedAt);
    }
}
