using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// Writes a <see cref="DemonstrationPlan"/> in timeline order through the domain and the platform's own services, all
/// in the caller's one transaction and one scope: the approvals through <see cref="ApproveInstrumentVersion"/> and
/// <see cref="ApproveRuleSet"/>, every assignment through <see cref="HouseholdAssignmentService"/> (the approved
/// eligibility rule decides who answers), every score through <see cref="SessionScoringService"/> (the approved rule set
/// and the platform's strategy compute the total and tier) and every routing through <see cref="ReferralRouter"/>. Those
/// services read the simulated clock and audit as the demonstration seed. Steps the database checks against each other
/// commit separately, as the platform's use cases do: content then approval, answers then submission, submission then
/// score then routing, and a head of household moved in two commits.
/// </summary>
internal sealed class DemonstrationBuilder
{
    private readonly DemonstrationPlan _plan;
    private readonly FamilyResilienceDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DemonstrationClock _clock = new();
    private readonly DemonstrationSeedCaller _caller = new();
    private readonly AuditLog _auditLog;
    private readonly IInstrumentRepository _instruments;
    private readonly IInstrumentVersionRepository _versions;
    private readonly IRuleSetRepository _ruleSets;
    private readonly IEnumerable<IRuleDefinitionValidator> _validators;
    private readonly IConsentPolicyVersionRepository _policies;
    private readonly IConsentRecordRepository _consents;
    private readonly IWaveRepository _waves;
    private readonly IHouseholdRepository _households;
    private readonly IPlatformUserRepository _users;
    private readonly IAssessmentAssignmentRepository _assignments;
    private readonly IAssessmentSessionRepository _sessions;
    private readonly HouseholdAssignmentService _assignmentService;
    private readonly SessionScoringService _scoring;
    private readonly ReferralRouter _router;
    private readonly Dictionary<int, SeededHousehold> _seeded = [];
    private readonly Dictionary<ConsentType, ConsentPolicyVersionId> _policyIds = [];
    private InstrumentVersionId _versionId;
    private RuleSetId _scoringRuleSetId;
    private RuleSetId _eligibilityRuleSetId;
    private WaveId _waveOneId;
    private WaveId _waveTwoId;

    public DemonstrationBuilder(IServiceProvider provider, DemonstrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _plan = plan;
        _dbContext = provider.GetRequiredService<FamilyResilienceDbContext>();
        _unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        _auditLog = new AuditLog(provider.GetRequiredService<IAuditEventRepository>(), _unitOfWork, _caller, _clock);
        _instruments = provider.GetRequiredService<IInstrumentRepository>();
        _versions = provider.GetRequiredService<IInstrumentVersionRepository>();
        _ruleSets = provider.GetRequiredService<IRuleSetRepository>();
        _validators = provider.GetRequiredService<IEnumerable<IRuleDefinitionValidator>>();
        _policies = provider.GetRequiredService<IConsentPolicyVersionRepository>();
        _consents = provider.GetRequiredService<IConsentRecordRepository>();
        _waves = provider.GetRequiredService<IWaveRepository>();
        _households = provider.GetRequiredService<IHouseholdRepository>();
        _users = provider.GetRequiredService<IPlatformUserRepository>();
        _assignments = provider.GetRequiredService<IAssessmentAssignmentRepository>();
        _sessions = provider.GetRequiredService<IAssessmentSessionRepository>();
        _assignmentService = new HouseholdAssignmentService(
            provider.GetRequiredService<HouseholdAssignmentPlanner>(), _assignments, provider.GetRequiredService<IHouseholdRunLock>(),
            _unitOfWork, _auditLog, _clock);
        _scoring = new SessionScoringService(
            _versions, _ruleSets, provider.GetRequiredService<IScoreResultRepository>(),
            provider.GetRequiredService<ScoringStrategyCatalog>(), _auditLog, _clock);
        _router = new ReferralRouter(
            _ruleSets, _consents, provider.GetRequiredService<ISupportReferralRepository>(),
            provider.GetRequiredService<ISupportRecommendationRepository>(), _auditLog, _clock);
    }

    private DemonstrationTimeline Timeline => _plan.Timeline;

    private DateOnly Today => DateOnly.FromDateTime(Timeline.Now.UtcDateTime);

    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        await ImportContentAsync(cancellationToken);
        await ApproveAsync(cancellationToken);
        await EnrolWaveOneHouseholdsAsync(cancellationToken);
        await RunWaveOneAsync(cancellationToken);
        await CloseWaveOneAsync(cancellationToken);
        await WithdrawAsync(cancellationToken);
        await RunWaveTwoAsync(cancellationToken);

        _clock.Now = Timeline.Now;
        Ensure(_auditLog.Stage(new AuditRecord(
            DemonstrationDataSeeder.SeededAuditAction, DemonstrationDatabase.MarkerResourceType, null, AuditOutcome.Succeeded)));
        await CommitAsync(cancellationToken);
    }

    /// <summary>The development seed's content, unchanged: both instruments as drafts with their proposals, plus the demonstration's eligibility proposal.</summary>
    private async Task ImportContentAsync(CancellationToken cancellationToken)
    {
        var now = Timeline.ContentImported;
        _clock.Now = now;

        var package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);
        var instrument = Required(InstrumentContentImporter.CreateInstrument(package.Instrument, now));
        var draft = Required(InstrumentContentImporter.BuildDraft(instrument, 1, package.Version, now));
        _instruments.Add(instrument);
        _versions.Add(draft);
        _versionId = draft.Id;

        foreach (var (code, fileName) in ContentFiles.ProposedScoringRules)
        {
            var definition = ContentFiles.Read(fileName);
            Ensure(Validator(RuleSetKind.Scoring).Validate(definition, instrument, draft));
            var proposed = Required(RuleSet.Propose(RuleSetKind.Scoring, code, 1, draft, definition, now));
            _ruleSets.Add(proposed);
            _scoringRuleSetId = proposed.Id;
        }

        Ensure(Validator(RuleSetKind.Eligibility).Validate(DemonstrationDataSeeder.EligibilityDefinition, instrument, draft));
        var eligibility = Required(RuleSet.Propose(
            RuleSetKind.Eligibility, DemonstrationDataSeeder.EligibilityRuleSetCode, 1, draft, DemonstrationDataSeeder.EligibilityDefinition, now));
        _ruleSets.Add(eligibility);
        _eligibilityRuleSetId = eligibility.Id;

        var survey = ContentFiles.ReadPackage(ContentFiles.AbuDhabiFamilySurvey);
        var surveyInstrument = Required(InstrumentContentImporter.CreateInstrument(survey.Instrument, now));
        var surveyDraft = Required(InstrumentContentImporter.BuildDraft(surveyInstrument, 1, survey.Version, now));
        _instruments.Add(surveyInstrument);
        _versions.Add(surveyDraft);
        foreach (var (code, fileName) in ContentFiles.ProposedEligibilityRules)
        {
            var definition = ContentFiles.Read(fileName);
            Ensure(Validator(RuleSetKind.Eligibility).Validate(definition, surveyInstrument, surveyDraft));
            _ruleSets.Add(Required(RuleSet.Propose(RuleSetKind.Eligibility, code, 1, surveyDraft, definition, now)));
        }

        foreach (var type in Enum.GetValues<ConsentType>())
        {
            var policy = Required(ConsentPolicyVersion.CreateDraft(
                type, 1, DemonstrationDataSeeder.ConsentDocumentReference, DemonstrationDataSeeder.ConsentDocumentReference, now));
            _policies.Add(policy);
            _policyIds[type] = policy.Id;
        }

        await CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The local demonstration approvals (D-140), each recording the seed as approver and the labelled reference: the
    /// household assessment version, its proposed scoring, the demonstration's eligibility rule and the placeholder
    /// consent policies. The family survey stays a draft and its rules stay proposed.
    /// </summary>
    private async Task ApproveAsync(CancellationToken cancellationToken)
    {
        _clock.Now = Timeline.Approved;
        Required(await new ApproveInstrumentVersion(_instruments, _versions, _caller, _unitOfWork, _auditLog, _clock).HandleAsync(
            _versionId.Value,
            new ApproveInstrumentVersionRequest(DemonstrationDataSeeder.ApprovalReference, Timeline.Approved, null),
            cancellationToken));

        var approveRuleSet = new ApproveRuleSet(_instruments, _versions, _ruleSets, _validators, _caller, _unitOfWork, _auditLog, _clock);
        _clock.Now = Timeline.Approved.AddMinutes(10);
        Required(await approveRuleSet.HandleAsync(
            _scoringRuleSetId.Value, new ApproveRuleSetRequest(DemonstrationDataSeeder.ApprovalReference), cancellationToken));
        _clock.Now = Timeline.Approved.AddMinutes(15);
        Required(await approveRuleSet.HandleAsync(
            _eligibilityRuleSetId.Value, new ApproveRuleSetRequest(DemonstrationDataSeeder.ApprovalReference), cancellationToken));

        var now = Timeline.Approved.AddMinutes(20);
        _clock.Now = now;
        var approval = Required(Approval.Create(DemonstrationDataSeeder.SeedSubject, now, DemonstrationDataSeeder.ApprovalReference));
        foreach (var id in _policyIds.Values)
        {
            var policy = await _policies.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("A seeded policy must exist.");
            Ensure(policy.Approve(approval, now));
        }

        await CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Wave one is planned; the households that take part from it enrol in the weeks before it opens; then it opens.
    /// </summary>
    private async Task EnrolWaveOneHouseholdsAsync(CancellationToken cancellationToken)
    {
        _clock.Now = Timeline.WaveOnePlanned;
        var planned = Required(Wave.Plan(
            DemonstrationDataSeeder.WaveOneCode, Timeline.WaveOneOpens, Timeline.WaveOneCloses, Timeline.WaveOnePlanned));
        _waves.Add(planned);
        _waveOneId = planned.Id;
        await CommitAsync(cancellationToken);

        foreach (var household in _plan.Households.Where(household => household.InWaveOne))
        {
            await EnrolAsync(household, cancellationToken);
        }

        await CommitAsync(cancellationToken);

        // The household-member persona: the lead's spouse gets an account of their own and answers only for themselves.
        var lead = _plan.Households.Single(household => household.IsLeadPersonaHousehold);
        var at = _plan.MemberPersonaLinkedAt;
        var leadHousehold = await LoadAsync(lead.Number, cancellationToken);
        var member = Required(PlatformUser.Register(DemonstrationDataSeeder.MemberSubject, at));
        _users.Add(member);
        Required(leadHousehold.LinkAccount(member.Id, _seeded[lead.Number].Members[1], at));

        var waveOne = await LoadWaveAsync(_waveOneId, cancellationToken);
        Ensure(waveOne.Open(Timeline.WaveOneOpens));
        await CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task RunWaveOneAsync(CancellationToken cancellationToken)
    {
        foreach (var household in _plan.Households.Where(household => household.WaveOne is not null))
        {
            var sitting = household.WaveOne!;
            var assignmentId = await AssignHeadAsync(household.Number, 0, _waveOneId, sitting.AssignedAt, cancellationToken);
            await SitAsync(assignmentId, sitting, cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The wave closes; within the hour maintenance marks what was left open as expired, abandoning a session still in
    /// progress first, as <see cref="ExpireOverdueAssignment"/> does (its own transaction cannot nest in this one), with
    /// the same audit events, recorded as the demonstration seed.
    /// </summary>
    private async Task CloseWaveOneAsync(CancellationToken cancellationToken)
    {
        _clock.Now = Timeline.WaveOneCloses;
        var waveOne = await LoadWaveAsync(_waveOneId, cancellationToken);
        Ensure(waveOne.Close(Timeline.WaveOneCloses));
        await CommitAsync(cancellationToken);

        var at = Timeline.WaveOneCloses.AddHours(1);
        _clock.Now = at;
        var overdue = await _assignments.ListOverdueIdsAsync(at, ExpireOverdueAssignments.MaxPerRun, [], cancellationToken);
        var expected = _plan.Households.Count(household =>
            household.WaveOne is { Outcome: DemonstrationOutcome.NotStarted or DemonstrationOutcome.Abandoned });
        if (overdue.Count != expected)
        {
            throw new InvalidOperationException("Building the demonstration database failed: wave one left unexpected assignments open.");
        }

        foreach (var assignmentId in overdue)
        {
            var assignment = await _assignments.GetAsync(assignmentId, cancellationToken)
                ?? throw new InvalidOperationException("A listed assignment must exist.");
            if (await _sessions.GetLiveForAssignmentAsync(assignmentId, cancellationToken) is { Status: SessionStatus.InProgress } session)
            {
                Ensure(session.Abandon(assignment, at));
                Audit("sessions.session.abandoned", "assessment_session", session.Id.Value);
                await CommitAsync(cancellationToken);
            }

            Ensure(assignment.Expire(at));
            Audit("assignments.assignment.expired", "assessment_assignment", assignment.Id.Value);
            await CommitAsync(cancellationToken);
        }

        _dbContext.ChangeTracker.Clear();
    }

    /// <summary>Two households withdraw after wave one, each audited as the platform audits a manager's withdrawal.</summary>
    private async Task WithdrawAsync(CancellationToken cancellationToken)
    {
        foreach (var household in _plan.Households)
        {
            if (household.WithdrawnAt is not { } withdrawnAt)
            {
                continue;
            }

            _clock.Now = withdrawnAt;
            var withdrawn = await LoadAsync(household.Number, cancellationToken);
            Ensure(withdrawn.Withdraw(withdrawnAt));
            Audit("households.household.withdrawn", "household", withdrawn.Id.Value);
            await CommitAsync(cancellationToken);
        }

        _dbContext.ChangeTracker.Clear();
    }

    /// <summary>Stages the platform's own audit event for a change, at the simulated time, with the seed as the actor.</summary>
    private void Audit(string action, string resourceType, Guid resourceId) =>
        Ensure(_auditLog.Stage(new AuditRecord(action, resourceType, resourceId.ToString(), AuditOutcome.Succeeded)));

    /// <summary>
    /// Wave two opens and every active household is assigned; one household's head changes, which cancels the former
    /// head's assignment and assigns the new head; four households join and are assigned when they confirm their
    /// roster; one enrols without confirming it, so nothing is assigned to it.
    /// </summary>
    private async Task RunWaveTwoAsync(CancellationToken cancellationToken)
    {
        _clock.Now = Timeline.WaveTwoPlanned;
        var waveTwo = Required(Wave.Plan(DemonstrationDataSeeder.WaveTwoCode, Timeline.WaveTwoOpens, Timeline.WaveTwoCloses, Timeline.WaveTwoPlanned));
        _waves.Add(waveTwo);
        _waveTwoId = waveTwo.Id;
        await CommitAsync(cancellationToken);
        Ensure(waveTwo.Open(Timeline.WaveTwoOpens));
        await CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();

        foreach (var household in _plan.Households)
        {
            if (!household.InWaveOne)
            {
                await EnrolAsync(household, cancellationToken);
                await CommitAsync(cancellationToken);
            }

            if (household.WaveTwo is { } sitting)
            {
                var assignmentId = await AssignHeadAsync(household.Number, 0, _waveTwoId, sitting.AssignedAt, cancellationToken);
                if (household.HeadChange is { } change)
                {
                    assignmentId = await ChangeHeadAsync(household.Number, change, cancellationToken);
                }

                await SitAsync(assignmentId, sitting, cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The household's roster, its consents and its roster confirmation. The lead persona's household enrols through
    /// its account, as the app does; the other households have none and their consent is recorded as assisted.
    /// </summary>
    private async Task EnrolAsync(DemonstrationHousehold planned, CancellationToken cancellationToken)
    {
        var reference = Required(HouseholdReference.Parse(planned.Reference));
        var roster = planned.Members.Select(Details).ToList();
        Household household;
        if (planned.IsLeadPersonaHousehold)
        {
            var lead = Required(PlatformUser.Register(DemonstrationDataSeeder.LeadSubject, planned.CreatedAt));
            _users.Add(lead);
            household = Required(Household.Enrol(reference, roster, 0, lead.Id, planned.CreatedAt));
        }
        else
        {
            household = Required(Household.Create(reference, roster, planned.CreatedAt));
        }

        _households.Add(household);
        _seeded[planned.Number] = new SeededHousehold(household.Id, household.Members.Select(member => member.Id).ToList());

        var grantedBy = planned.IsLeadPersonaHousehold ? DemonstrationDataSeeder.LeadSubject : DemonstrationDataSeeder.SeedSubject;
        var method = planned.IsLeadPersonaHousehold ? ConsentCaptureMethod.InApp : ConsentCaptureMethod.AssistedByStaff;
        var types = planned.ResearchUse ? new[] { ConsentType.Participation, ConsentType.ResearchUse } : new[] { ConsentType.Participation };
        foreach (var index in planned.ConsentingMembers)
        {
            foreach (var type in types)
            {
                var policy = await _policies.GetAsync(_policyIds[type], cancellationToken)
                    ?? throw new InvalidOperationException("A seeded policy must exist.");
                _consents.Add(Required(ConsentRecord.Grant(household.Members[index], policy, method, grantedBy, planned.ConsentedAt)));
            }
        }

        if (planned.RosterConfirmedAt is { } confirmedAt)
        {
            Ensure(household.ConfirmRoster(confirmedAt));
        }
    }

    /// <summary>
    /// Runs the platform's assignment service for the household at <paramref name="at"/>. The approved eligibility rule
    /// must assign exactly the planned head; anything else means the plan and the rule disagree, and the build stops.
    /// </summary>
    private async Task<AssessmentAssignmentId> AssignHeadAsync(
        int number, int headIndex, WaveId waveId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        _clock.Now = at;
        var run = Required(await _assignmentService.AssignAsync(_seeded[number].Id, waveId, cancellationToken));
        if (run.Created.Count != 1 || run.Created[0].MemberId != _seeded[number].Members[headIndex].Value)
        {
            throw new InvalidOperationException("Building the demonstration database failed: the eligibility rule did not assign the head.");
        }

        return AssessmentAssignmentId.From(run.Created[0].AssignmentId);
    }

    /// <summary>
    /// The head moves in two commits (the partial unique index is checked row by row), the roster is confirmed again and
    /// the assignment service runs: the former head's waiting assignment is cancelled and the new head is assigned.
    /// </summary>
    private async Task<AssessmentAssignmentId> ChangeHeadAsync(int number, DemonstrationHeadChange change, CancellationToken cancellationToken)
    {
        var seeded = _seeded[number];
        var household = await LoadAsync(number, cancellationToken);
        var former = household.FindMember(seeded.Members[0]) ?? throw new InvalidOperationException("The head must exist.");
        var next = household.FindMember(seeded.Members[change.NewHeadIndex]) ?? throw new InvalidOperationException("The new head must exist.");

        Ensure(household.UpdateMember(
            former.Id,
            new FamilyMemberDetails(former.BirthMonth, change.FormerHeadRelationship, former.EmploymentStatus, false, former.IsPrimaryIncomeEarner),
            change.At));
        await CommitAsync(cancellationToken);
        Ensure(household.UpdateMember(
            next.Id,
            new FamilyMemberDetails(next.BirthMonth, null, next.EmploymentStatus, true, next.IsPrimaryIncomeEarner),
            change.At.AddSeconds(1)));
        await CommitAsync(cancellationToken);
        Ensure(household.ConfirmRoster(change.At.AddMinutes(2)));
        await CommitAsync(cancellationToken);

        _clock.Now = change.At.AddMinutes(5);
        var run = Required(await _assignmentService.AssignAsync(seeded.Id, _waveTwoId, cancellationToken));
        if (run.Cancelled.Count != 1 || run.Cancelled[0].MemberId != former.Id.Value
            || run.Created.Count != 1 || run.Created[0].MemberId != next.Id.Value)
        {
            throw new InvalidOperationException("Building the demonstration database failed: the head change did not move the assignment.");
        }

        return AssessmentAssignmentId.From(run.Created[0].AssignmentId);
    }

    /// <summary>
    /// One sitting: the session starts and its answers are saved (one commit), then, when complete, it is submitted,
    /// scored and routed, each in its own commit as <c>SubmitSession</c> does. The total and tier come from the engine.
    /// </summary>
    private async Task SitAsync(AssessmentAssignmentId assignmentId, DemonstrationSitting sitting, CancellationToken cancellationToken)
    {
        if (sitting.StartedAt is not { } startedAt)
        {
            return;
        }

        var assignment = await _assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw new InvalidOperationException("A created assignment must exist.");
        var version = await _versions.GetAsync(_versionId, cancellationToken)
            ?? throw new InvalidOperationException("The approved version must exist.");
        var items = version.GetItems().ToDictionary(item => item.Code, StringComparer.Ordinal);

        _clock.Now = startedAt;
        var session = Required(AssessmentSession.Start(assignment, startedAt));
        _sessions.Add(session);
        foreach (var answer in sitting.Answers)
        {
            var item = items[answer.ItemCode];
            var option = item.GetSubstantiveOptions().Single(candidate => candidate.Value == answer.RawValue);
            Ensure(session.Answer(version, item.Id, option.Id, answer.AnsweredAt));
        }

        await CommitAsync(cancellationToken);
        if (sitting.Outcome != DemonstrationOutcome.Completed)
        {
            return;
        }

        var submittedAt = sitting.SubmittedAt ?? throw new InvalidOperationException("A completed sitting has a submission time.");
        _clock.Now = submittedAt;
        if (Required(session.Submit(version, assignment, submittedAt)) != SubmissionOutcome.Submitted)
        {
            throw new InvalidOperationException("Building the demonstration database failed: a session was submitted twice.");
        }

        await CommitAsync(cancellationToken);

        _clock.Now = submittedAt.AddSeconds(1);
        var attempt = Required(await _scoring.ScoreAsync(session, cancellationToken));
        if (!attempt.Created || attempt.Result is not { Status: ScoreStatus.Complete, TierCode: not null })
        {
            throw new InvalidOperationException("Building the demonstration database failed: a session did not receive a complete score.");
        }

        await CommitAsync(cancellationToken);
        Required(await _router.RouteAsync(session, attempt.Result, attempt.SectionCode, cancellationToken));
        await CommitAsync(cancellationToken);
    }

    private FamilyMemberDetails Details(DemonstrationMember member)
    {
        var birthYear = Today.Year - member.Age - (Today.Month < member.BirthMonth ? 1 : 0);
        return new FamilyMemberDetails(
            Required(YearMonth.Create(birthYear, member.BirthMonth, Today)),
            member.Relationship,
            member.Employment,
            member.IsHead,
            member.IsPrimaryEarner);
    }

    private IRuleDefinitionValidator Validator(RuleSetKind kind) => _validators.Single(validator => validator.Kind == kind);

    private async Task<Household> LoadAsync(int number, CancellationToken cancellationToken) =>
        await _households.GetAsync(_seeded[number].Id, cancellationToken)
        ?? throw new InvalidOperationException("A seeded household must exist.");

    private async Task<Wave> LoadWaveAsync(WaveId waveId, CancellationToken cancellationToken) =>
        await _waves.GetAsync(waveId, cancellationToken) ?? throw new InvalidOperationException("A seeded wave must exist.");

    private async Task CommitAsync(CancellationToken cancellationToken) => Ensure(await _unitOfWork.CommitAsync(cancellationToken));

    private static T Required<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Building the demonstration database failed: {result.Error!.Code}");

    private static void Ensure(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Building the demonstration database failed: {result.Error!.Code}");
        }
    }

    private sealed record SeededHousehold(HouseholdId Id, IReadOnlyList<FamilyMemberId> Members);
}
