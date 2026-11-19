using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// Support resources in PostgreSQL: the round trip, the unique indexes behind the 409 errors, the immutability trigger
/// and the generated search vector. Raw SQL cases bypass the domain to prove the database itself refuses the change.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportResourcePersistenceTests(DatabaseFixture fixture)
{
    private const string RestrictViolation = "23001";

    private static SupportResourceContent Content(string summaryEn = "Synthetic summary", string? summaryAr = "ملخص تجريبي") =>
        TestData.Ok(SupportResourceContent.Create(
            "مورد تجريبي", "Synthetic resource", summaryAr, summaryEn, "جهة تجريبية", "Synthetic provider",
            "https://example.org/help", ["synthetic_category", "synthetic_other"], ["synthetic_audience"], ["ar", "en"],
            SupportResourceVisibility.StaffOnly));

    private static SupportResource NewDraft(string code, int version = 1, SupportResourceContent? content = null) =>
        TestData.Ok(SupportResource.CreateDraft(code, version, content ?? Content(), TestData.Now));

    private static SupportResource NewApproved(string code, int version = 1, SupportResourceContent? content = null)
    {
        var resource = NewDraft(code, version, content);
        TestData.Ok(resource.Approve(TestData.TestApproval(), TestData.Now.AddDays(100), TestData.Now));
        return resource;
    }

    private async Task SaveAsync(SupportResource resource, CancellationToken cancellationToken) =>
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<ISupportResourceRepository>().Add(resource);
                return Task.CompletedTask;
            },
            cancellationToken);

    private async Task RetireAsync(SupportResource resource, CancellationToken cancellationToken) =>
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISupportResourceRepository>().GetAsync(resource.Id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(tracked.Retire(TestData.Now.AddDays(1)));
            },
            cancellationToken);

    private async Task<string> SearchVectorAsync(SupportResource resource, CancellationToken cancellationToken)
    {
        var id = resource.Id.Value;
        return await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT search_vector::text AS \"Value\" FROM support_resources WHERE id = {id}")
            .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task A_draft_round_trips_with_its_texts_and_lists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = NewDraft(TestData.UniqueCode("resource"));
        await SaveAsync(draft, cancellationToken);

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISupportResourceRepository>().GetAsync(draft.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(saved);
        Assert.Equal((draft.Code, 1, SupportResourceStatus.Draft, SupportResourceVisibility.StaffOnly), (saved.Code, saved.VersionNumber, saved.Status, saved.Visibility));
        Assert.Equal(("مورد تجريبي", "Synthetic resource"), (saved.Name.Arabic, saved.Name.English));
        Assert.Equal("ملخص تجريبي", saved.Summary.Arabic);
        Assert.Equal("Synthetic provider", saved.ProviderName.English);
        Assert.Equal("https://example.org/help", saved.Url);
        Assert.Equal(["synthetic_category", "synthetic_other"], saved.Categories);
        Assert.Equal(["synthetic_audience"], saved.Audiences);
        Assert.Equal(["ar", "en"], saved.ServiceLanguages);
    }

    [Fact]
    public async Task A_code_and_version_pair_is_unique()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = TestData.UniqueCode("resource");
        await SaveAsync(NewDraft(code), cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISupportResourceRepository>().Add(NewDraft(code));
        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(SupportResourceErrors.VersionNumberTaken, committed.Error);
    }

    [Fact]
    public async Task Only_one_version_of_a_code_can_be_approved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = TestData.UniqueCode("resource");
        await SaveAsync(NewApproved(code), cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISupportResourceRepository>().Add(NewApproved(code, 2));
        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(SupportResourceErrors.ApprovedVersionExists, committed.Error);
    }

    [Fact]
    public async Task The_database_refuses_changing_approved_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approved = NewApproved(TestData.UniqueCode("resource"));
        await SaveAsync(approved, cancellationToken);
        var id = approved.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_resources SET name_en = 'x' WHERE id = {id}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task The_database_refuses_deleting_approved_or_retired_resources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approved = NewApproved(TestData.UniqueCode("resource"));
        var retired = NewApproved(TestData.UniqueCode("resource"));
        await SaveAsync(approved, cancellationToken);
        await SaveAsync(retired, cancellationToken);
        await RetireAsync(retired, cancellationToken);
        var approvedId = approved.Id.Value;
        var retiredId = retired.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM support_resources WHERE id = {approvedId}", cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM support_resources WHERE id = {retiredId}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task The_database_allows_retiring_an_approved_resource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approved = NewApproved(TestData.UniqueCode("resource"));
        await SaveAsync(approved, cancellationToken);

        await RetireAsync(approved, cancellationToken);

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISupportResourceRepository>().GetAsync(approved.Id, cancellationToken),
            cancellationToken);
        Assert.NotNull(saved);
        Assert.Equal(SupportResourceStatus.Retired, saved.Status);
        Assert.NotNull(saved.RetiredAt);
    }

    [Fact]
    public async Task A_retired_resource_never_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = NewApproved(TestData.UniqueCode("resource"));
        await SaveAsync(resource, cancellationToken);
        await RetireAsync(resource, cancellationToken);
        var id = resource.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_resources SET updated_at = updated_at + interval '1 day' WHERE id = {id}", cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_resources SET status = 'Approved', retired_at = NULL WHERE id = {id}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task The_search_vector_folds_arabic_and_strips_the_article()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var arabic = NewDraft(TestData.UniqueCode("resource"), content: Content(summaryAr: "إدارة الديون للأسرة"));
        var english = NewDraft(TestData.UniqueCode("resource"), content: Content(summaryEn: "Debts"));
        await SaveAsync(arabic, cancellationToken);
        await SaveAsync(english, cancellationToken);

        var arabicVector = await SearchVectorAsync(arabic, cancellationToken);
        var englishVector = await SearchVectorAsync(english, cancellationToken);

        Assert.Contains("'ديون'", arabicVector, StringComparison.Ordinal);
        Assert.Contains("'اداره'", arabicVector, StringComparison.Ordinal);
        Assert.Contains("'اسره'", arabicVector, StringComparison.Ordinal);
        Assert.Contains("'debt'", englishVector, StringComparison.Ordinal);
    }
}
