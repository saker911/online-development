namespace VehiclePermitSystemWeb.Services.Gate
{
    public enum GateDecisionOutcome
    {
        AllowEntry,
        AllowExit,
        AllowFinalExit,
        DenyNoPermission,
        DenyPendingReview,
        WaitForWorkEndClosure,
        DenyStopped,
        DenyExpired,
    }

    public sealed record GateDecision(
        GateDecisionOutcome Outcome,
        string ReasonCode,
        string Reason,
        bool CreatePendingUnauthorizedExit = false
    )
    {
        public bool IsAllowed =>
            Outcome == GateDecisionOutcome.AllowEntry
            || Outcome == GateDecisionOutcome.AllowExit
            || Outcome == GateDecisionOutcome.AllowFinalExit;
    }
}
