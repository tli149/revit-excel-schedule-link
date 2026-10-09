using ExcelScheduleLink.Contracts;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public class WorksharingLogicTests
    {
        [Theory]
        [InlineData("abc", "ABC", SourceIdentityState.Match)]
        [InlineData("abc", "def", SourceIdentityState.Differs)]
        [InlineData("", "abc", SourceIdentityState.Unknown)]
        [InlineData(null, "abc", SourceIdentityState.Unknown)]
        [InlineData("abc", "", SourceIdentityState.ReadFailed)]
        [InlineData("abc", null, SourceIdentityState.ReadFailed)]
        public void SourceIdentity_Compare_UsesHashAvailabilityAndValue(
            string accepted, string current, SourceIdentityState expected)
        {
            Assert.Equal(expected, SourceIdentity.Compare(accepted, current));
        }

        [Theory]
        [InlineData(SourceIdentityState.Match, CreatorRelation.Different, ReloadScope.SingleLink, false, ReloadDecisionKind.Proceed)]
        [InlineData(SourceIdentityState.Match, CreatorRelation.Different, ReloadScope.SingleLink, true, ReloadDecisionKind.RequireConfirmation)]
        [InlineData(SourceIdentityState.Match, CreatorRelation.Unavailable, ReloadScope.AllLinks, true, ReloadDecisionKind.SkipConfirmationRequired)]
        [InlineData(SourceIdentityState.Match, CreatorRelation.Same, ReloadScope.AllLinks, false, ReloadDecisionKind.Proceed)]
        [InlineData(SourceIdentityState.Differs, CreatorRelation.Same, ReloadScope.SingleLink, false, ReloadDecisionKind.WarnContentChanged)]
        [InlineData(SourceIdentityState.Differs, CreatorRelation.NotApplicable, ReloadScope.SingleLink, false, ReloadDecisionKind.WarnContentChanged)]
        [InlineData(SourceIdentityState.Differs, CreatorRelation.Different, ReloadScope.SingleLink, false, ReloadDecisionKind.RequireStrongConfirmation)]
        [InlineData(SourceIdentityState.Differs, CreatorRelation.Unavailable, ReloadScope.SingleLink, false, ReloadDecisionKind.RequireStrongConfirmation)]
        [InlineData(SourceIdentityState.Unknown, CreatorRelation.Same, ReloadScope.SingleLink, false, ReloadDecisionKind.RequireConfirmation)]
        [InlineData(SourceIdentityState.Unknown, CreatorRelation.Different, ReloadScope.SingleLink, false, ReloadDecisionKind.RequireStrongConfirmation)]
        [InlineData(SourceIdentityState.Unknown, CreatorRelation.Unavailable, ReloadScope.SingleLink, false, ReloadDecisionKind.RequireStrongConfirmation)]
        [InlineData(SourceIdentityState.Differs, CreatorRelation.Same, ReloadScope.AllLinks, false, ReloadDecisionKind.SkipConfirmationRequired)]
        [InlineData(SourceIdentityState.Unknown, CreatorRelation.Same, ReloadScope.AllLinks, false, ReloadDecisionKind.SkipConfirmationRequired)]
        [InlineData(SourceIdentityState.ReadFailed, CreatorRelation.Same, ReloadScope.SingleLink, false, ReloadDecisionKind.FailRead)]
        [InlineData(SourceIdentityState.ReadFailed, CreatorRelation.Same, ReloadScope.AllLinks, false, ReloadDecisionKind.FailRead)]
        public void ReloadDecisionTable_ReturnsDesignDecision(
            SourceIdentityState identity,
            CreatorRelation creator,
            ReloadScope scope,
            bool crossUserManualWidthRisk,
            ReloadDecisionKind expected)
        {
            Assert.Equal(expected, ReloadDecisionTable.Decide(
                identity, creator, scope, crossUserManualWidthRisk).Kind);
        }

        [Fact]
        public void RelativePathPolicy_DerivesAndResolvesAgainstStableAnchor()
        {
            string anchor = @"C:\ESL\Central\model.rvt";
            string source = @"C:\ESL\Central\Source\shared.xlsx";

            string relative = RelativePathPolicy.Derive(anchor, source);

            Assert.Equal(@"Source\shared.xlsx", relative);
            Assert.True(RelativePathPolicy.IsTrulyRelative(relative));
            Assert.Equal(source, RelativePathPolicy.Resolve(anchor, relative));
        }

        [Fact]
        public void RelativePathPolicy_DegradesCrossVolumeAndRejectsRootedLegacyValue()
        {
            Assert.Equal(string.Empty, RelativePathPolicy.Derive(
                null, @"C:\ESL\Source\shared.xlsx"));
            Assert.Equal(string.Empty, RelativePathPolicy.Derive(
                @"C:\ESL\Central\model.rvt", @"E:\ESL\Source\shared.xlsx"));
            Assert.False(RelativePathPolicy.IsTrulyRelative(@"E:\ESL\Source\shared.xlsx"));
            Assert.False(RelativePathPolicy.IsTrulyRelative(@"\\server\share\shared.xlsx"));
            Assert.False(RelativePathPolicy.IsTrulyRelative("file:///C:/ESL/shared.xlsx"));
            Assert.Null(RelativePathPolicy.Resolve(
                @"C:\ESL\Central\model.rvt", @"E:\ESL\Source\shared.xlsx"));
        }

        [Fact]
        public void LinkMigrationNormalizer_CopiesV1AndClearsLegacyOrUnknownIdentityFields()
        {
            var v1 = new LinkFieldSnapshot
            {
                SchemaVersion = LinkFieldSnapshot.LegacySchemaVersion,
                RendererVersion = 7,
                AbsolutePath = @"C:\ESL\Source\shared.xlsx",
                RelativePath = @"C:\legacy\shared.xlsx",
                PathMode = 1,
                SheetName = "Sheet1",
                RangeMode = 1,
                RangeAddress = "A1:C3",
                SourceLastWriteUtcTicks = 11,
                LastLoadedUtcTicks = 22,
                ColumnWidthMode = 2,
                RowHeightMode = 3,
                WidthScale = 1.25,
                CreatedSourceSha256 = "should-not-be-carried",
                LastAcceptedSourceSha256 = "should-not-be-carried",
                LastAcceptedSourceSizeBytes = 99
            };

            LinkFieldSnapshot result = LinkMigrationNormalizer.ToV2(
                v1, @"Source\shared.xlsx");

            Assert.Equal(LinkFieldSnapshot.CurrentSchemaVersion, result.SchemaVersion);
            Assert.Equal(2, result.SchemaVersion);
            Assert.Equal(v1.RendererVersion, result.RendererVersion);
            Assert.Equal(v1.AbsolutePath, result.AbsolutePath);
            Assert.Equal(@"Source\shared.xlsx", result.RelativePath);
            Assert.Equal(0, result.PathMode);
            Assert.Equal(v1.SheetName, result.SheetName);
            Assert.Equal(v1.RangeMode, result.RangeMode);
            Assert.Equal(v1.RangeAddress, result.RangeAddress);
            Assert.Equal(v1.SourceLastWriteUtcTicks, result.SourceLastWriteUtcTicks);
            Assert.Equal(v1.LastLoadedUtcTicks, result.LastLoadedUtcTicks);
            Assert.Equal(v1.ColumnWidthMode, result.ColumnWidthMode);
            Assert.Equal(v1.RowHeightMode, result.RowHeightMode);
            Assert.Equal(v1.WidthScale, result.WidthScale);
            Assert.Equal(string.Empty, result.CreatedSourceSha256);
            Assert.Equal(string.Empty, result.LastAcceptedSourceSha256);
            Assert.Equal(0, result.LastAcceptedSourceSizeBytes);

            LinkFieldSnapshot noAnchor = LinkMigrationNormalizer.ToV2(v1, @"C:\legacy\shared.xlsx");
            Assert.Equal(string.Empty, noAnchor.RelativePath);
        }
    }
}
