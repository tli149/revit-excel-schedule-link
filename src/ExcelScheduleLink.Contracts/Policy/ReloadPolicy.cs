// Revit-free decision logic, kept in Contracts so the net8 tests can exercise it.
using System;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>Identity state used by the Phase 1 reload decision table.</summary>
    public enum SourceIdentityState
    {
        Match,
        Differs,
        Unknown,
        ReadFailed
    }

    /// <summary>Relationship between the schedule creator and the current user.</summary>
    public enum CreatorRelation
    {
        Same,
        Different,
        Unavailable,
        NotApplicable
    }

    public enum ReloadScope
    {
        SingleLink,
        AllLinks
    }

    public enum ReloadDecisionKind
    {
        Proceed,
        WarnContentChanged,
        RequireConfirmation,
        RequireStrongConfirmation,
        SkipConfirmationRequired,
        FailRead
    }

    /// <summary>Pure result of applying design §7.1 to one link.</summary>
    public sealed class ReloadDecision
    {
        internal ReloadDecision(ReloadDecisionKind kind)
        {
            Kind = kind;
        }

        public ReloadDecisionKind Kind { get; }
    }

    /// <summary>
    /// The Phase 1 decision table. It deliberately returns a value only; dialogs,
    /// transactions, and bulk summary presentation belong to later layers.
    /// </summary>
    public static class ReloadDecisionTable
    {
        public static ReloadDecision Decide(
            SourceIdentityState identity,
            CreatorRelation creator,
            ReloadScope scope)
        {
            return Decide(identity, creator, scope, false);
        }

        /// <summary>
        /// Applies the decision table and, when requested, the separate
        /// cross-user ApplyExcelWidths protection rule from design §10.1.
        /// </summary>
        public static ReloadDecision Decide(
            SourceIdentityState identity,
            CreatorRelation creator,
            ReloadScope scope,
            bool crossUserManualWidthRisk)
        {
            if (identity == SourceIdentityState.ReadFailed)
            {
                return new ReloadDecision(ReloadDecisionKind.FailRead);
            }

            if (identity == SourceIdentityState.Match)
            {
                if (crossUserManualWidthRisk)
                {
                    return new ReloadDecision(scope == ReloadScope.AllLinks
                        ? ReloadDecisionKind.SkipConfirmationRequired
                        : ReloadDecisionKind.RequireConfirmation);
                }
                return new ReloadDecision(ReloadDecisionKind.Proceed);
            }

            if (scope == ReloadScope.AllLinks)
            {
                return new ReloadDecision(ReloadDecisionKind.SkipConfirmationRequired);
            }

            bool creatorIsDifferent = creator == CreatorRelation.Different
                || creator == CreatorRelation.Unavailable;

            if (identity == SourceIdentityState.Differs)
            {
                return new ReloadDecision(
                    creatorIsDifferent
                        ? ReloadDecisionKind.RequireStrongConfirmation
                        : ReloadDecisionKind.WarnContentChanged);
            }

            if (identity == SourceIdentityState.Unknown)
            {
                return new ReloadDecision(
                    creatorIsDifferent
                        ? ReloadDecisionKind.RequireStrongConfirmation
                        : ReloadDecisionKind.RequireConfirmation);
            }

            // An out-of-range enum value is safer as a confirmation boundary
            // than as an implicit proceed. This keeps the function fail-closed.
            return new ReloadDecision(ReloadDecisionKind.RequireStrongConfirmation);
        }
    }

    /// <summary>SHA-256 identity comparison. Empty current data is a read failure.</summary>
    public static class SourceIdentity
    {
        public static SourceIdentityState Compare(string acceptedSha256, string currentSha256)
        {
            if (string.IsNullOrWhiteSpace(currentSha256))
            {
                return SourceIdentityState.ReadFailed;
            }
            if (string.IsNullOrWhiteSpace(acceptedSha256))
            {
                return SourceIdentityState.Unknown;
            }
            return string.Equals(
                acceptedSha256.Trim(), currentSha256.Trim(), StringComparison.OrdinalIgnoreCase)
                ? SourceIdentityState.Match
                : SourceIdentityState.Differs;
        }
    }
}
