namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// The OpenAI settings under <c>Assistant:Model</c> (D-112, D-116, D-119, D-124, D-126, D-141): one key for the assistant,
/// the consultant and the written score report, each with its own model. The key lives in user secrets, the git-ignored
/// <c>.env</c> in Development, or the deployment's secret store, never in the repository; without one all three stay
/// switched off. <see cref="ToString"/> never prints the key.
/// </summary>
/// <param name="ApiKey">The OpenAI API key, or null.</param>
/// <param name="ChatModel">The agent's model on the Responses API, for the chat and the memory.</param>
/// <param name="ChatReasoningEffort">How much the chat model reasons before it answers.</param>
/// <param name="VoiceModel">The live consultant's full-duplex voice model (GPT-Live).</param>
/// <param name="Voice">The consultant's voice.</param>
/// <param name="DelegateModel">The Responses model a live call delegates its reasoning and tools to.</param>
/// <param name="DelegateReasoningEffort">How much the delegated model reasons.</param>
/// <param name="ModerationModel">The model that checks what the person says for danger (D-126).</param>
/// <param name="ReportModel">The Responses model that writes the score report, its parts and its summary (D-116).</param>
/// <param name="CrisisModel">The Responses model that writes the consultant's supportive reply after OpenAI's filter stopped a call (D-141).</param>
/// <param name="CrisisReasoningEffort">How much the crisis model reasons: none by default, because the reply must come within seconds.</param>
public sealed record OpenAiSettings(
    string? ApiKey,
    string ChatModel,
    string ChatReasoningEffort,
    string VoiceModel,
    string Voice,
    string DelegateModel,
    string DelegateReasoningEffort,
    string ModerationModel,
    string ReportModel,
    string CrisisModel,
    string CrisisReasoningEffort)
{
    public const string ApiKeySetting = "Assistant:Model:ApiKey";
    public const string ChatModelSetting = "Assistant:Model:ChatName";
    public const string ChatReasoningEffortSetting = "Assistant:Model:ChatReasoningEffort";
    public const string VoiceModelSetting = "Assistant:Model:VoiceName";
    public const string VoiceSetting = "Assistant:Model:Voice";
    public const string DelegateModelSetting = "Assistant:Model:DelegateName";
    public const string DelegateReasoningEffortSetting = "Assistant:Model:DelegateReasoningEffort";
    public const string ModerationModelSetting = "Assistant:Model:ModerationName";
    public const string ReportModelSetting = "Assistant:Model:ReportName";
    public const string CrisisModelSetting = "Assistant:Model:CrisisName";
    public const string CrisisReasoningEffortSetting = "Assistant:Model:CrisisReasoningEffort";

    /// <summary>
    /// The decision that approves sending family data to OpenAI; a deployed API with a key refuses to start without it.
    /// One reference covers the assistant, the consultant and the score report, which share the key.
    /// </summary>
    public const string ApprovalReferenceSetting = "Assistant:Model:ApprovalReference";

    /// <summary>OpenAI's fast model for clear, natural writing: about 2 s a reply, 3 to 4 s with a tool round trip.</summary>
    public const string DefaultChatModel = "gpt-6.1-terra";

    /// <summary>OpenAI's full-duplex voice model: listens while it speaks and delegates reasoning (US$0.05 a minute).</summary>
    public const string DefaultVoiceModel = "gpt-live-1";

    /// <summary>Low effort: a little reasoning where it helps, without a delay the person notices.</summary>
    public const string DefaultReasoningEffort = "low";

    /// <summary>One of the two voices OpenAI recommends for the best quality.</summary>
    public const string DefaultVoice = "marin";

    /// <summary>OpenAI's free moderation model, with self-harm and violence categories.</summary>
    public const string DefaultModerationModel = "omni-moderation-latest";

    /// <summary>
    /// Chosen for speed after comparing real report sections: about twice as fast as gpt-6.1-terra, which writes slightly
    /// better, while gpt-5.4-nano's Arabic was not good enough (D-116). Change it with <see cref="ReportModelSetting"/>.
    /// </summary>
    public const string DefaultReportModel = "gpt-5.4-mini";

    /// <summary>
    /// Chosen for speed (D-141): with no reasoning it wrote the supportive reply in 1.8 to 2.9 s on 2026-11-10, against
    /// 2.8 to 7 s for gpt-6.1-terra, and both wrote what the instructions ask. Change it with <see cref="CrisisModelSetting"/>.
    /// </summary>
    public const string DefaultCrisisModel = "gpt-5.4-mini";

    /// <summary>No reasoning: the reply is short and must be ready while the next session opens.</summary>
    public const string DefaultCrisisReasoningEffort = "none";

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    public static OpenAiSettings From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        string Or(string setting, string fallback) => read(setting) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
        var chatModel = Or(ChatModelSetting, DefaultChatModel);
        return new OpenAiSettings(
            read(ApiKeySetting)?.Trim(),
            chatModel,
            Or(ChatReasoningEffortSetting, DefaultReasoningEffort),
            Or(VoiceModelSetting, DefaultVoiceModel),
            Or(VoiceSetting, DefaultVoice),
            Or(DelegateModelSetting, chatModel),
            Or(DelegateReasoningEffortSetting, DefaultReasoningEffort),
            Or(ModerationModelSetting, DefaultModerationModel),
            Or(ReportModelSetting, DefaultReportModel),
            Or(CrisisModelSetting, DefaultCrisisModel),
            Or(CrisisReasoningEffortSetting, DefaultCrisisReasoningEffort));
    }

    public override string ToString() =>
        $"OpenAiSettings {{ HasKey = {HasKey}, ChatModel = {ChatModel}, VoiceModel = {VoiceModel}, Voice = {Voice}, DelegateModel = {DelegateModel}, ModerationModel = {ModerationModel}, ReportModel = {ReportModel}, CrisisModel = {CrisisModel} }}";
}
