using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

public static class AssistantErrors
{
    /// <summary>No model is configured: the assistant and the consultant are not switched on (503).</summary>
    public static readonly Error Unavailable =
        new("assistant.unavailable", "The assistant is not switched on.");

    /// <summary>The model call failed or took longer than its time limit (502). Nothing about the call is kept.</summary>
    public static readonly Error ModelFailed =
        new("assistant.model_failed", "The assistant could not answer. Try again.");

    /// <summary>
    /// A server fault (the database, the audit) stopped a reply after a crisis signal (500). Only then is the fault caught
    /// rather than left to the global handler, so the problem can still carry the crisis notice (D-138).
    /// </summary>
    public static readonly Error Faulted =
        new("assistant.reply_faulted", "The assistant could not answer. Try again.");

    /// <summary>A written report states the score, which only the household's head may see (403, D-103).</summary>
    public static readonly Error ScoreReportHeadOnly =
        new("assistant.score_report_head_only", "Only the head of household can create this report.");

    /// <summary>The session has no complete score on a graded scale to write about (409).</summary>
    public static readonly Error ScoreReportNotReady =
        new("assistant.score_report_not_ready", "This session has no score to report on yet.");

    /// <summary>The live consultant speaks WebSocket only (400).</summary>
    public static readonly Error WebSocketRequired =
        new("assistant.websocket_required", "The live consultant accepts WebSocket requests only.");
}
