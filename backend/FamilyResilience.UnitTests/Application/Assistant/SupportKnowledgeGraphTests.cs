using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Infrastructure.Content;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>The graph a family's next steps come from. Every entry but the shipped file's is synthetic.</summary>
public sealed class SupportKnowledgeGraphTests
{
    private const string Arabic = "نص تجريبي";

    private static readonly DateOnly Today = new(2026, 6, 1);

    private static JsonObject Text(string english = "Synthetic text") => new() { ["en"] = english, ["ar"] = Arabic };

    private static JsonObject Service(string code = "synthetic_service", params string[] needs) => new()
    {
        ["code"] = code,
        ["name"] = Text($"Service {code}"),
        ["summary"] = Text(),
        ["needs"] = new JsonArray((needs.Length == 0 ? ["need_a"] : needs).Select(need => (JsonNode?)need).ToArray()),
        ["procedure"] = new JsonArray(Text("Step one")),
        ["documents"] = new JsonArray(),
        ["website"] = "https://example.org/service",
        ["via"] = null,
        ["evidence"] = "opened",
        ["sources"] = new JsonArray("https://example.org/source"),
    };

    private static JsonObject Authority(string code = "synthetic_authority", string kind = "government", string emirate = "abu_dhabi", string due = "2027-01-01", params JsonObject[] services) => new()
    {
        ["code"] = code,
        ["kind"] = kind,
        ["name"] = Text($"Authority {code}"),
        ["purpose"] = Text(),
        ["emirates"] = new JsonArray(emirate),
        ["website"] = "https://example.org/authority",
        ["phone"] = "+971 2 000 0000",
        ["services"] = new JsonArray(services.Length == 0 ? [Service()] : services),
        ["sources"] = new JsonArray("https://example.org/source"),
        ["approvalReference"] = "SYNTHETIC-1",
        ["checkedOn"] = "2026-01-01",
        ["reviewDue"] = due,
    };

    private static SupportKnowledgeGraph Graph(params JsonObject[] authorities) => SupportKnowledgeGraph.Parse(new JsonObject
    {
        ["schema"] = SupportKnowledgeGraph.Schema,
        ["needs"] = new JsonArray(
            new JsonObject { ["code"] = "need_a", ["label"] = Text("Need A") },
            new JsonObject { ["code"] = "need_b", ["label"] = Text("Need B") },
            new JsonObject { ["code"] = "gap_need", ["label"] = Text("Nothing addresses this") }),
        ["signals"] = new JsonObject
        {
            ["Q1"] = new JsonArray("need_a"),
            ["Q2"] = new JsonArray("need_b"),
            ["Q3"] = new JsonArray("gap_need"),
        },
        ["authorities"] = new JsonArray(authorities.Length == 0 ? [Authority()] : authorities),
    }.ToJsonString());

    private static ScoreReportAnswer Answer(string code, decimal? points, int minimum = 1, int maximum = 4) =>
        new("An area", "A question", "An answer", points, maximum) { QuestionCode = code, MinimumPoints = minimum };

    private static SupportCandidate Candidate(string code, string kind = "government", string emirate = "abu_dhabi") =>
        Graph(Authority($"{code}_authority", kind, emirate, services: Service(code)))
            .Infer([Answer("Q1", 1m)], Today, 10)
            .Single();

    // ---- The file that ships ----

    private static SupportKnowledgeGraph Shipped() => SupportKnowledgeGraph.Parse(ContentFiles.Read(ContentFiles.SupportKnowledgeGraph));

    [Fact]
    public void The_graph_that_ships_is_valid_and_holds_the_thirteen_the_owner_named()
    {
        var shipped = Shipped();

        Assert.Equal(13, shipped.Authorities.Count);
        Assert.All(shipped.Authorities, authority => Assert.False(string.IsNullOrWhiteSpace(authority.ApprovalReference)));
    }

    [Fact]
    public void Every_question_of_the_household_assessment_has_an_edge_and_no_other_question_does()
    {
        using var assessment = JsonDocument.Parse(ContentFiles.Read("household-assessment.v1.json"));
        var questions = assessment.RootElement.GetProperty("version").GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("code").GetString()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(questions, Shipped().Questions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void What_the_graph_cannot_yet_point_a_family_to_is_known() =>
        Assert.Equal(["financial_planning"], Shipped().Gaps);

    [Fact]
    public void Inference_never_sends_a_family_to_a_body_with_no_service_for_families()
    {
        var everyAnswerWeak = Shipped().Questions.Select(question => Answer(question, 1m)).ToList();

        var reached = Shipped().Infer(everyAnswerWeak, new DateOnly(2026, 11, 6), 100).Select(candidate => candidate.Authority.Code).ToHashSet();

        Assert.DoesNotContain("sharjah_statistics_community_development", reached);
        Assert.DoesNotContain("supreme_council_motherhood_childhood", reached);
        Assert.Contains("family_care_authority", reached);
    }

    [Fact]
    public void No_answer_alone_can_raise_protection_from_harm_because_no_question_asks_about_it()
    {
        var everyAnswerWeak = Shipped().Questions.Select(question => Answer(question, 1m)).ToList();

        var services = Shipped().Infer(everyAnswerWeak, new DateOnly(2026, 11, 6), 100).Select(candidate => candidate.Service.Code);

        Assert.DoesNotContain("fca_protective_care", services);
        Assert.DoesNotContain("cda_child_protection", services);
    }

    // ---- Inference ----

    [Fact]
    public void An_answer_with_room_to_improve_reaches_the_service_addressing_its_need()
    {
        var graph = Graph(Authority(services: [Service("for_a", "need_a"), Service("for_b", "need_b")]));

        var candidate = Assert.Single(graph.Infer([Answer("Q1", 2m)], Today, 10));

        Assert.Equal(("for_a", "need_a"), (candidate.Service.Code, candidate.Need.Code));
    }

    [Fact]
    public void An_answer_at_the_top_of_its_scale_or_outside_it_reaches_nothing()
    {
        var graph = Graph();

        Assert.Empty(graph.Infer([Answer("Q1", 4m), Answer("Q1", null)], Today, 10));
    }

    [Fact]
    public void The_lowest_answers_come_first_then_the_files_order()
    {
        var graph = Graph(Authority(services: [Service("first_in_file", "need_a"), Service("second_in_file", "need_a"), Service("weakest", "need_b")]));

        var codes = graph.Infer([Answer("Q1", 3m), Answer("Q2", 1m)], Today, 10).Select(candidate => candidate.Service.Code);

        Assert.Equal(["weakest", "first_in_file", "second_in_file"], codes);
    }

    [Fact]
    public void Inference_gives_the_same_order_every_time_and_stops_at_its_limit()
    {
        var graph = Graph(Authority(services: [Service("one"), Service("two"), Service("three")]));
        var answers = new[] { Answer("Q1", 1m) };

        Assert.Equal(["one", "two"], graph.Infer(answers, Today, 2).Select(candidate => candidate.Service.Code));
        Assert.Equal(graph.Infer(answers, Today, 2).Select(candidate => candidate.Service.Code), graph.Infer(answers, Today, 2).Select(candidate => candidate.Service.Code));
    }

    [Theory]
    [InlineData(2027, 1, 1, true)]
    [InlineData(2027, 1, 2, false)]
    public void An_authority_past_its_review_date_leaves_so_a_stale_procedure_never_reaches_a_family(int year, int month, int day, bool found)
    {
        var today = new DateOnly(year, month, day);
        var graph = Graph();

        Assert.Equal(found, graph.Infer([Answer("Q1", 1m)], today, 10).Count == 1);
        Assert.Equal(found, graph.Current(today).Count == 1);
    }

    [Fact]
    public void A_question_the_graph_does_not_know_reaches_nothing() =>
        Assert.Empty(Graph().Infer([Answer("Q99", 1m)], Today, 10));

    // ---- What a family is shown ----

    [Fact]
    public void A_models_pick_narrows_the_candidates_and_a_made_up_code_adds_nothing()
    {
        var graph = Graph(Authority(services: [Service("one"), Service("two"), Service("three")]));
        var candidates = graph.Infer([Answer("Q1", 1m)], Today, 10);

        var chosen = ScoreReportNextStep.Choose(candidates, ["three", "ministry_the_model_invented", "three"]);

        Assert.Equal("three", Assert.Single(chosen).Service.Code);
    }

    [Fact]
    public void With_no_valid_pick_the_graphs_own_order_decides_among_public_services()
    {
        var clinic = Candidate("clinic", "private", "dubai");
        var ministry = Candidate("ministry");

        var chosen = ScoreReportNextStep.Choose([clinic, ministry], ["made_up"]);

        Assert.Equal("ministry", Assert.Single(chosen).Service.Code);
    }

    [Fact]
    public void A_private_provider_is_shown_when_a_model_picks_it()
    {
        var clinic = Candidate("clinic", "private");

        Assert.Equal("clinic", Assert.Single(ScoreReportNextStep.Choose([clinic], ["clinic"])).Service.Code);
    }

    [Fact]
    public void A_private_provider_is_shown_when_nothing_else_was_reached()
    {
        var clinic = Candidate("clinic", "private");

        Assert.Equal("clinic", Assert.Single(ScoreReportNextStep.Choose([clinic], [])).Service.Code);
    }

    [Fact]
    public void Each_emirate_gets_a_public_service_because_a_familys_emirate_is_not_known()
    {
        var abuDhabi = Candidate("in_abu_dhabi");
        var dubai = Candidate("in_dubai", emirate: "dubai");

        var chosen = ScoreReportNextStep.Choose([abuDhabi, dubai], ["in_abu_dhabi"]);

        Assert.Equal(["in_abu_dhabi", "in_dubai"], chosen.Select(candidate => candidate.Service.Code));
    }

    [Fact]
    public void One_emirate_shows_one_service_so_a_step_stays_short()
    {
        var chosen = ScoreReportNextStep.Choose([Candidate("one"), Candidate("two"), Candidate("three")], ["two", "three"]);

        Assert.Equal(1, ScoreReportNextStep.MaxServicesPerCoverage);
        Assert.Equal("two", Assert.Single(chosen).Service.Code);
    }

    [Fact]
    public void A_step_takes_its_authority_word_for_word_in_the_readers_language()
    {
        var graph = Graph();
        var step = ScoreReportNextStep.From(
            new ScoreReportImprovement("An area", "A reason", ["A step"]), "wealth_debt", 0.25m, "ar", graph.Infer([Answer("Q1", 1m)], Today, 10), graph);

        var authority = Assert.Single(step.Authorities);
        var service = Assert.Single(authority.Services);
        Assert.Equal(("wealth_debt", 0.25m, Arabic), (step.SectionCode, step.Standing, authority.Name));
        Assert.Equal(Arabic, Assert.Single(service.Procedure));
        Assert.Equal(Arabic, service.Need);
        Assert.Equal(("https://example.org/authority", "+971 2 000 0000"), (authority.Website, authority.Phone));
        Assert.Equal(new DateOnly(2026, 1, 1), authority.CheckedOn);
    }

    [Fact]
    public void A_service_applied_for_through_another_authority_names_it()
    {
        var via = Service("through_portal");
        via["via"] = "portal";
        var graph = Graph(Authority("owner", services: via), Authority("portal", services: Service("own_service", "need_b")));

        var step = ScoreReportNextStep.From(new ScoreReportImprovement("An area", "A reason", ["A step"]), "a", 0m, "en", graph.Infer([Answer("Q1", 1m)], Today, 10), graph);

        Assert.Equal("Authority portal", Assert.Single(Assert.Single(step.Authorities).Services).Via);
    }

    // ---- The rules of the file ----

    public static TheoryData<string, object?> AuthorityMistakes => new()
    {
        { "code", "Not Snake" },
        { "kind", "ministry" },
        { "approvalReference", " " },
        { "approvalReference", null },
        { "emirates", Array.Empty<string>() },
        { "emirates", new[] { "atlantis" } },
        { "website", "http://example.org/not-secure" },
        { "website", "not a link" },
        { "phone", "call us" },
        { "sources", Array.Empty<string>() },
        { "sources", new[] { "http://example.org/not-secure" } },
        { "reviewDue", "2025-01-01" },
        { "services", null },
    };

    [Theory]
    [MemberData(nameof(AuthorityMistakes))]
    public void A_mistake_in_an_authority_stops_the_graph_loading(string field, object? value)
    {
        var authority = Authority();
        authority[field] = JsonSerializer.SerializeToNode(value);

        var problem = Assert.Throws<InvalidOperationException>(() => Graph(authority));

        Assert.Contains("Support authority 1", problem.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, object?> ServiceMistakes => new()
    {
        { "code", "Not Snake" },
        { "needs", Array.Empty<string>() },
        { "needs", new[] { "a_need_the_graph_does_not_hold" } },
        { "procedure", null },
        { "documents", null },
        { "website", "http://example.org/not-secure" },
        { "via", "an_authority_the_graph_does_not_hold" },
        { "evidence", "a_friend_said" },
        { "sources", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(ServiceMistakes))]
    public void A_mistake_in_a_service_stops_the_graph_loading(string field, object? value)
    {
        var service = Service();
        service[field] = JsonSerializer.SerializeToNode(value);

        Assert.Throws<InvalidOperationException>(() => Graph(Authority(services: service)));
    }

    [Theory]
    [InlineData("en", "")]
    [InlineData("ar", "")]
    [InlineData("ar", "Not Arabic")]
    public void Every_text_needs_english_and_arabic(string language, string value)
    {
        var name = Text();
        name[language] = value;
        var authority = Authority();
        authority["name"] = name;
        var service = Service();
        service["summary"] = name.DeepClone();
        var step = Service();
        step["procedure"] = new JsonArray(name.DeepClone());

        Assert.Throws<InvalidOperationException>(() => Graph(authority));
        Assert.Throws<InvalidOperationException>(() => Graph(Authority(services: service)));
        Assert.Throws<InvalidOperationException>(() => Graph(Authority(services: step)));
    }

    [Fact]
    public void A_service_may_have_no_published_procedure_and_an_authority_no_services()
    {
        var service = Service();
        service["procedure"] = new JsonArray();
        var reference = Authority();
        reference["services"] = new JsonArray();

        Assert.NotNull(Graph(Authority(services: service)));
        Assert.Empty(Assert.Single(Graph(reference).Authorities).Services);
    }

    [Fact]
    public void Two_authorities_or_services_cannot_share_a_code()
    {
        Assert.Throws<InvalidOperationException>(() => Graph(Authority(), Authority()));
        Assert.Throws<InvalidOperationException>(() => Graph(Authority("one", services: Service("same")), Authority("two", services: Service("same"))));
    }

    [Fact]
    public void A_question_must_lead_to_needs_the_graph_holds()
    {
        const string File = """
            {"schema":"support-knowledge-graph.v1","needs":[],"signals":{"Q1":["missing"]},"authorities":[]}
            """;

        Assert.Throws<InvalidOperationException>(() => SupportKnowledgeGraph.Parse(File));
    }

    [Theory]
    [InlineData("""{"schema":"something-else","needs":[],"signals":{},"authorities":[]}""")]
    [InlineData("""{"schema":"support-knowledge-graph.v1"}""")]
    public void The_file_must_state_its_schema_and_lists(string json) =>
        Assert.Throws<InvalidOperationException>(() => SupportKnowledgeGraph.Parse(json));

    [Theory]
    [InlineData(4, 4, 16, 0)]
    [InlineData(16, 4, 16, 1)]
    [InlineData(10, 4, 16, 0.5)]
    [InlineData(3, 4, 16, 0)]
    [InlineData(5, 5, 5, 1)]
    public void Standing_runs_from_the_areas_lowest_to_its_highest(int score, int minimum, int maximum, double expected) =>
        Assert.Equal((decimal)expected, ScoreReportNextStep.StandingOf(score, minimum, maximum));

    [Theory]
    [InlineData(4, 0)]
    [InlineData(1, 1)]
    [InlineData(2.5, 0.5)]
    public void Room_to_improve_runs_from_the_top_of_the_scale_to_its_bottom(double points, double expected) =>
        Assert.Equal((decimal)expected, Answer("Q1", (decimal)points).RoomToImprove());

    [Fact]
    public void A_candidate_prints_no_weight_from_a_familys_answers() =>
        Assert.DoesNotContain("3", new SupportCandidate(
            Candidate("x").Authority, Candidate("x").Service, Candidate("x").Need, 3.3333m).ToString(), StringComparison.Ordinal);
}
