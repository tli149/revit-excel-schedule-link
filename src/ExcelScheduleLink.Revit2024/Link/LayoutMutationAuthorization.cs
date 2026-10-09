using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Authorizes a Layout mutation immediately before its single transaction.
    /// A source fingerprint is supplied only by Restore Excel Widths, after its
    /// read has completed outside the transaction.
    /// </summary>
    internal static class LayoutMutationAuthorization
    {
        public static bool Authorize(
            Document doc, ViewSchedule schedule, LayoutSourceSnapshot sourceSnapshot)
        {
            ExcelLinkStorage storage = schedule == null
                ? null : ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                return false;
            }

            ExcelLinkInfo info = storage.Info;
            string creator;
            CreatorRelation relation = WorksharingContext.GetCreatorRelation(
                doc, schedule.Id, out creator);

            if (sourceSnapshot != null && sourceSnapshot.Fingerprint != null)
            {
                SourceIdentityState identity = storage.IsV2
                    ? SourceIdentity.Compare(
                        info.LastAcceptedSourceSha256,
                        sourceSnapshot.Fingerprint.Sha256)
                    : SourceIdentityState.Unknown;
                ReloadDecision decision = ReloadDecisionTable.Decide(
                    identity, relation, ReloadScope.SingleLink);
                if (decision.Kind != ReloadDecisionKind.Proceed)
                {
                    return ReloadDialogs.Authorize(doc, new ReloadPromptContext
                    {
                        Identity = identity,
                        CreatorRelation = relation,
                        DecisionKind = decision.Kind,
                        ScheduleName = schedule.Name,
                        Creator = creator,
                        ResolvedPath = sourceSnapshot.ResolvedPath,
                        IsLegacy = !storage.IsV2,
                        IsLayoutSource = true
                    });
                }
            }

            if (!storage.IsV2)
            {
                return ReloadDialogs.ConfirmLegacyMigration(doc, schedule.Name);
            }

            WorksharingAdvisory.ShowIfNeeded(doc);
            return true;
        }
    }
}
