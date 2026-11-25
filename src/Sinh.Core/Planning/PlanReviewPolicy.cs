namespace Sinh.Core;

public static class PlanReviewPolicy
{
    public static bool IsMassDeletion(SyncPlan plan) => plan.Deletes >= 10 || plan.Deletes >= 3 && plan.Deletes >= Math.Max(1, plan.Destination.Entries.Count) * .2;
    public static bool RequiresReview(SyncPlan plan, bool automatic, string? approvedFingerprint) => !automatic
        || approvedFingerprint != plan.Fingerprint
        || plan.Request.Mode != SyncMode.TwoWay && plan.DestinationChanged
        || plan.Conflicts.Count > 0
        || IsMassDeletion(plan);
}
