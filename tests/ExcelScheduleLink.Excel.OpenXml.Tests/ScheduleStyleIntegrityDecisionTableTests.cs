using System;
using System.Collections.Generic;
using ExcelScheduleLink.Contracts;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public class ScheduleStyleIntegrityDecisionTableTests
    {
        [Theory]
        [InlineData(51, 50)]
        [InlineData(3, 2)]
        public void AT88_VerifiedShapes_ClassifyStrictStylelessCells(
            int cellCount, int expectedStylelessCount)
        {
            ScheduleStyleFacts facts = MakeFacts(cellCount);
            for (int i = 1; i <= expectedStylelessCount; i++)
            {
                MakeStyleless(facts.Cells[i], "Some Fallback " + i, 9.0);
            }

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(StyleIntegrityReasonCode.StylelessCellDetected, result.ReasonCode);
            Assert.Equal(expectedStylelessCount, result.StylelessCellCount);
            Assert.Equal(cellCount, result.HeaderCellsExpected);
            Assert.Equal(cellCount, result.HeaderCellsRead);
        }

        [Fact]
        public void AT88_BorderlessHealthyTable_AllCellsMatchReferenceAndTextType()
        {
            ScheduleStyleFacts facts = MakeFacts(4);
            foreach (CellStyleFacts cell in facts.Cells)
            {
                SetAllBordersInvalid(cell);
                cell.FontName = " Arial ";
                cell.TextSize = 12.0;
            }

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(StyleIntegrityReasonCode.Healthy, result.ReasonCode);
            Assert.Equal(0, result.StylelessCellCount);
        }

        [Fact]
        public void AT90_BorderlessCellsUseOrdinaryReferenceWhenTextTypePairsDiffer()
        {
            ScheduleStyleFacts facts = MakeFacts(3);
            facts.TitleTextStyle = Text("Arial", 13.0);
            facts.HeaderTextStyle = Text("Calibri", 14.0);

            // The reference has ordinary borders, while every other cell is
            // borderless and carries the reference pair. This models a
            // An ordinary reference pair remains a usable baseline even when it
            // differs from both optional text-type pairs.
            for (int i = 1; i < facts.Cells.Count; i++)
            {
                SetAllBordersInvalid(facts.Cells[i]);
                facts.Cells[i].FontName = facts.ReferenceCell.FontName;
                facts.Cells[i].TextSize = facts.ReferenceCell.TextSize;
            }

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(0, result.StylelessCellCount);
        }

        [Fact]
        public void AT90_FullyBorderlessArial9AgainstArial12TextTypes_IsAffected()
        {
            // Adversarial case: 12 is in the verified 1/96-inch table
            // units, while the all-invalid cells report a real 9-unit pair.
            ScheduleStyleFacts facts = MakeFacts(3);
            facts.TitleTextStyle = Text("Arial", 12.0);
            facts.HeaderTextStyle = Text("Arial", 12.0);
            foreach (CellStyleFacts cell in facts.Cells)
            {
                SetAllBordersInvalid(cell);
                cell.FontName = "Arial";
                cell.TextSize = 9.0;
            }

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(3, result.StylelessCellCount);
        }

        [Fact]
        public void AT90_HealthyArial12TableWithOrdinaryBorders_IsHealthy()
        {
            ScheduleStyleFacts facts = MakeFacts(3);
            facts.TitleTextStyle = Text("Arial", 12.0);
            facts.HeaderTextStyle = Text("Arial", 12.0);

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(0, result.StylelessCellCount);
        }

        [Fact]
        public void AT90_AllInvalidReferenceMatchingTextType_IsUsableAndHealthy()
        {
            ScheduleStyleFacts facts = MakeFacts(3);
            facts.TitleTextStyle = Text("Arial", 12.0);
            facts.HeaderTextStyle = Text("Arial", 12.0);
            foreach (CellStyleFacts cell in facts.Cells)
            {
                SetAllBordersInvalid(cell);
                cell.FontName = "Arial";
                cell.TextSize = 12.0;
            }

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(0, result.StylelessCellCount);
        }

        [Fact]
        public void AT88_BorderlessCellMatchingTitleOrHeaderIsHealthy()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            SetAllBordersInvalid(facts.Cells[0]);
            facts.Cells[0].FontName = "Arial";
            facts.Cells[0].TextSize = 12.0;
            SetAllBordersInvalid(facts.Cells[1]);
            facts.Cells[1].FontName = "Calibri";
            facts.Cells[1].TextSize = 11.0;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(0, result.StylelessCellCount);
        }

        [Fact]
        public void AT88_ReferenceCellItselfStylelessWithOthersHealthy_IsAffected()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            MakeStyleless(facts.ReferenceCell, "Some Fallback", 9.0);
            facts.Cells[0] = facts.ReferenceCell;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(StyleIntegrityReasonCode.StylelessCellDetected, result.ReasonCode);
            Assert.Equal(1, result.StylelessCellCount);
            Assert.Equal(0, result.StylelessCells[0].Row);
            Assert.Equal(0, result.StylelessCells[0].Column);
        }

        [Fact]
        public void AT88_NoTextTypesResolvable_UsableReferenceStillClassifies()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            facts.TitleTextStyle = null;
            facts.HeaderTextStyle = new TextStyleFacts
            {
                IsResolvable = false,
                ReadError = "HeaderTextTypeId unavailable"
            };
            MakeStyleless(facts.Cells[1], "Some Fallback", 9.0);

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(1, result.StylelessCellCount);
        }

        [Fact]
        public void AT88_NoTextTypesAndNothingUsable_IsIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.TitleTextStyle = null;
            facts.HeaderTextStyle = null;
            facts.ReferenceCell = null;
            facts.Cells[0].Row = 1;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.TextStyleUnavailable, result.ReasonCode);
        }

        [Fact]
        public void AT88_OneThreeAndFourInvalidBorders_RequireAllFourForCandidate()
        {
            for (int invalidCount = 1; invalidCount <= 3; invalidCount++)
            {
                ScheduleStyleFacts facts = MakeFacts(1);
                if (invalidCount >= 1) facts.Cells[0].BorderTopLineStyle = -1;
                if (invalidCount >= 2) facts.Cells[0].BorderBottomLineStyle = -1;
                if (invalidCount >= 3) facts.Cells[0].BorderLeftLineStyle = -1;

                StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

                Assert.Equal(StyleIntegrityState.Healthy, result.State);
            }

            ScheduleStyleFacts fourInvalid = MakeFacts(1);
            MakeStyleless(fourInvalid.Cells[0], "Some Fallback", 9.0);

            StyleIntegrityResult affected = StyleIntegrityDecisionTable.Evaluate(fourInvalid);

            Assert.Equal(StyleIntegrityState.Affected, affected.State);
            Assert.Equal(1, affected.StylelessCellCount);
        }

        [Fact]
        public void AT88_FontMismatchWithEqualSize_IsStyleless()
        {
            CellStyleFacts cell = MakeCandidate("Calibri", 12.0);
            Assert.True(StyleIntegrityDecisionTable.IsStylelessCell(
                cell, Text("Arial", 12.0), Text("Times New Roman", 11.0),
                MakeReference()));
        }

        [Fact]
        public void AT88_SizeMismatchWithEqualFont_IsStyleless()
        {
            CellStyleFacts cell = MakeCandidate("Arial", 12.51);
            Assert.True(StyleIntegrityDecisionTable.IsStylelessCell(
                cell, Text("Arial", 12.0), Text("Calibri", 11.0),
                MakeReference()));
        }

        [Theory]
        [InlineData(12.49)]
        [InlineData(12.5)]
        public void AT88_SizeWithinTableUnitTolerance_MatchesBaseline(double cellSize)
        {
            CellStyleFacts cell = MakeCandidate("Arial", cellSize);
            Assert.False(StyleIntegrityDecisionTable.IsStylelessCell(
                cell, Text("Arial", 12.0), Text("Calibri", 11.0),
                MakeReference()));
        }

        [Fact]
        public void AT90_TwoPointFiveMillimeterVsArialNine_IsAmbiguousIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            double expectedUnits = 2.5 / 25.4 * 96.0;
            facts.TitleTextStyle = Text("Arial", expectedUnits);
            facts.HeaderTextStyle = Text("Arial", expectedUnits);
            MakeStyleless(facts.Cells[0], " Arial ", 9.0);

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.AmbiguousTextSizeQuantization,
                result.ReasonCode);
            Assert.Equal(0, result.StylelessCellCount);
            Assert.Equal(
                StyleIntegrityDecisionTable.TextPairMatchKind.QuantizedAmbiguous,
                StyleIntegrityDecisionTable.ClassifyTextPair(
                    "Arial", 9.0, Text("Arial", expectedUnits)));
        }

        [Fact]
        public void AT90_OneEighthInchVsArialTwelve_IsAnExactNormalMatch()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.TitleTextStyle = Text("Arial", 12.0);
            facts.HeaderTextStyle = Text("Arial", 12.0);
            MakeStyleless(facts.Cells[0], "Arial", 12.0);

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(StyleIntegrityReasonCode.Healthy, result.ReasonCode);
            Assert.Equal(
                StyleIntegrityDecisionTable.TextPairMatchKind.Exact,
                StyleIntegrityDecisionTable.ClassifyTextPair(
                    " Arial ", 12.0, Text("Arial", 12.0)));
        }

        [Fact]
        public void AT90_AmbiguousTopLeftDoesNotBecomeReferenceBaseline()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            double expectedUnits = 2.5 / 25.4 * 96.0;
            facts.TitleTextStyle = Text("Arial", expectedUnits);
            facts.HeaderTextStyle = Text("Arial", expectedUnits);
            MakeStyleless(facts.ReferenceCell, "Arial", 9.0);
            facts.Cells[0] = facts.ReferenceCell;
            MakeStyleless(facts.Cells[1], "Calibri", 11.0);

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(1, result.StylelessCellCount);
            Assert.Equal(1, result.StylelessCells[0].Row);
            Assert.Null(StyleIntegrityDecisionTable.ReferenceBaseline(
                facts.ReferenceCell, facts.TitleTextStyle, facts.HeaderTextStyle));
        }

        [Fact]
        public void AT88_SizeBeyondTableUnitTolerance_DoesNotMatchBaseline()
        {
            CellStyleFacts cell = MakeCandidate("Arial", 12.501);
            Assert.True(StyleIntegrityDecisionTable.IsStylelessCell(
                cell, Text("Arial", 12.0), Text("Calibri", 11.0),
                MakeReference()));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void AT88_EveryExplicitOverrideSignal_ExcludesCell(
            bool cellOverride, bool styleOverride, bool optionOverride)
        {
            CellStyleFacts cell = MakeCandidate("Some Fallback", 9.0);
            cell.IsCellOverridden = cellOverride;
            cell.IsStyleOverridden = styleOverride;
            cell.HasAnyOverrideOption = optionOverride;

            Assert.False(StyleIntegrityDecisionTable.IsStylelessCell(
                cell, Text("Arial", 12.0), Text("Calibri", 11.0),
                MakeReference()));
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.Cells[0] = cell;
            facts.ReferenceCell = cell;
            Assert.Equal(StyleIntegrityState.Healthy,
                StyleIntegrityDecisionTable.Evaluate(facts).State);
        }

        [Fact]
        public void AT88_LegitimateFourBorderOverride_IsNotAffected()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            SetAllBordersInvalid(facts.Cells[0]);
            facts.Cells[0].HasAnyOverrideOption = true;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(0, result.StylelessCellCount);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void AT88_MissingOneTextBaseline_UsesRemainingBaseline(
            bool missingTitle, bool missingHeader)
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            if (missingTitle) facts.TitleTextStyle = null;
            if (missingHeader) facts.HeaderTextStyle = null;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
        }

        [Fact]
        public void AT89_MissingOrNonTextNoteTypeBaseline_IsOptional()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.TitleTextStyle = new TextStyleFacts
            {
                IsMissing = true,
                ReadError = "TitleTextTypeId did not resolve to TextNoteType."
            };

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
            Assert.Equal(StyleIntegrityReasonCode.Healthy, result.ReasonCode);
        }

        [Fact]
        public void AT88_ExplicitReferencePairIsUsableEvenWhenBorderless()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            SetAllBordersInvalid(facts.ReferenceCell);
            facts.Cells[0] = facts.ReferenceCell;
            SetAllBordersInvalid(facts.Cells[1]);
            facts.Cells[1].FontName = facts.ReferenceCell.FontName;
            facts.Cells[1].TextSize = facts.ReferenceCell.TextSize;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Healthy, result.State);
        }

        [Fact]
        public void AT88_KnownAffectedWinsOverAnotherReadFailure()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            MakeStyleless(facts.Cells[1], "Some Fallback", 9.0);
            facts.Cells[0].ReadComplete = false;
            facts.Cells[0].ReadError = "second cell failed";

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(1, result.StylelessCellCount);
        }

        [Fact]
        public void AT89_PropertyReadFailure_IsIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.Cells[0].ReadComplete = false;
            facts.Cells[0].ReadError = "style property failed";

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.CellReadFailed, result.ReasonCode);
            Assert.Equal("style property failed", result.FirstReadError);
        }

        [Fact]
        public void AT89_InvalidFacts_IsIndeterminate()
        {
            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(null);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.InvalidFacts, result.ReasonCode);
        }

        [Fact]
        public void AT89_DimensionReadFailed_IsIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.DimensionsRead = false;
            facts.FirstReadError = "dimensions failed";

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.DimensionReadFailed, result.ReasonCode);
        }

        [Fact]
        public void AT89_ZeroExpectedCells_IsIncompleteScan()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.ExpectedHeaderCellCount = 0;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.IncompleteScan, result.ReasonCode);
        }

        [Fact]
        public void AT89_CellCountMismatch_IsIncompleteScan()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            facts.ExpectedHeaderCellCount = 3;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.IncompleteScan, result.ReasonCode);
        }

        [Fact]
        public void AT89_ElementReadDenied_IsIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.Cells[0].ReadComplete = false;
            facts.Cells[0].ElementReadDenied = true;
            facts.Cells[0].ReadError = "element access denied";

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.ElementReadDenied, result.ReasonCode);
        }

        [Fact]
        public void AT89_CellTypeReadIncompleteUnderRequireTextCells_IsIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.Cells[0].CellTypeReadComplete = false;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(
                facts, StyleIntegrityDecisionTable.DefaultInvalidElementId, true);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.UnexpectedCellType, result.ReasonCode);
        }

        [Fact]
        public void AT89_AffectedPrecedesUnexpectedCellType()
        {
            ScheduleStyleFacts facts = MakeFacts(2);
            MakeStyleless(facts.Cells[1], "Fallback", 9.0);
            facts.Cells[0].IsTextCell = false;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(
                facts, StyleIntegrityDecisionTable.DefaultInvalidElementId, true);

            Assert.Equal(StyleIntegrityState.Affected, result.State);
            Assert.Equal(StyleIntegrityReasonCode.StylelessCellDetected, result.ReasonCode);
        }

        [Fact]
        public void AT89_TextStyleReadThrow_IsIndeterminateWithDedicatedReason()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.TitleTextStyle.IsResolvable = false;
            facts.TitleTextStyle.ReadAttempted = true;
            facts.TitleTextStyle.ReadFailed = true;
            facts.TitleTextStyle.ReadError = "TEXT_SIZE read threw";

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.TextStyleReadFailed, result.ReasonCode);
            Assert.Equal("TEXT_SIZE read threw", result.FirstReadError);
        }

        [Fact]
        public void AT89_TextStyleAccessDenied_IsElementReadDenied()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.HeaderTextStyle.IsResolvable = false;
            facts.HeaderTextStyle.ReadAttempted = true;
            facts.HeaderTextStyle.ReadFailed = true;
            facts.HeaderTextStyle.AccessDenied = true;
            facts.HeaderTextStyle.ReadError = "text type access denied";

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.ElementReadDenied, result.ReasonCode);
        }

        [Fact]
        public void AT89_UnexpectedRequiredCellType_IsIndeterminate()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.Cells[0].IsTextCell = false;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(
                facts, StyleIntegrityDecisionTable.DefaultInvalidElementId, true);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.UnexpectedCellType, result.ReasonCode);
        }

        [Fact]
        public void AT89_ScheduleLimit_IsIndeterminateAndNeverPartialHealthy()
        {
            ScheduleStyleFacts facts = MakeFacts(1);
            facts.LimitExceeded = true;
            facts.SkippedScheduleCount = 3;

            StyleIntegrityResult result = StyleIntegrityDecisionTable.Evaluate(facts);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.ScanLimit, result.ReasonCode);
            Assert.Equal(3, result.SkippedScheduleCount);
        }

        [Theory]
        [InlineData(500, 50, false)]
        [InlineData(501, 50, true)]
        [InlineData(500, 51, true)]
        [InlineData(25000, 1, true)]
        public void AT89_PerScheduleLimit_IsExplicit(int rows, int columns, bool exceeded)
        {
            Assert.Equal(exceeded,
                StyleIntegrityLimits.ExceedsScheduleLimit(rows, columns));
        }

        [Fact]
        public void AT89_RenderedSize_UsesTheSharedRenderLimits()
        {
            StyleIntegrityLimits.RenderedSize size =
                StyleIntegrityLimits.GetEffectiveRenderedSize(501, 51);

            Assert.Equal(500, size.Rows);
            Assert.Equal(50, size.Columns);
        }

        [Fact]
        public void AT89_ManualAndAutomaticBatchLimits_AreStable()
        {
            Assert.Equal(256, StyleIntegrityLimits.MaxSchedules(StyleIntegrityScanScope.Manual));
            Assert.Equal(64, StyleIntegrityLimits.MaxSchedules(StyleIntegrityScanScope.Automatic));
            Assert.Equal(100000L,
                StyleIntegrityLimits.MaxHeaderCells(StyleIntegrityScanScope.Automatic));
            Assert.Equal(long.MaxValue,
                StyleIntegrityLimits.MaxHeaderCells(StyleIntegrityScanScope.Manual));
        }

        [Fact]
        public void AT89_LimitResult_ReportsSkippedSchedulesAsNotChecked()
        {
            StyleIntegrityResult result = StyleIntegrityDecisionTable.LimitResult(7);

            Assert.Equal(StyleIntegrityState.Indeterminate, result.State);
            Assert.Equal(StyleIntegrityReasonCode.ScanLimit, result.ReasonCode);
            Assert.Equal(7, result.SkippedScheduleCount);
        }

        [Fact]
        public void AT89_AutomaticCellBudgetCrossing_IsNotIncludedInBudget()
        {
            Assert.False(StyleIntegrityLimits.ExceedsHeaderCellBudget(
                StyleIntegrityScanScope.Automatic, 99999, 1));
            Assert.True(StyleIntegrityLimits.ExceedsHeaderCellBudget(
                StyleIntegrityScanScope.Automatic, 99999, 2));
            Assert.True(StyleIntegrityLimits.ExceedsHeaderCellBudget(
                StyleIntegrityScanScope.Automatic, 100000, 1));
            Assert.False(StyleIntegrityLimits.ExceedsHeaderCellBudget(
                StyleIntegrityScanScope.Manual, 99999, 2));
        }

        private static ScheduleStyleFacts MakeFacts(int count)
        {
            ScheduleStyleFacts facts = new ScheduleStyleFacts
            {
                DimensionsRead = true,
                NumberOfRows = count,
                NumberOfColumns = 1,
                FirstRowNumber = 5,
                FirstColumnNumber = 8,
                ExpectedHeaderCellCount = count,
                TitleTextStyle = Text("Arial", 12.0),
                HeaderTextStyle = Text("Calibri", 11.0),
                Cells = new List<CellStyleFacts>()
            };
            for (int i = 0; i < count; i++)
            {
                facts.Cells.Add(MakeCell(i, 0));
            }
            facts.ReferenceCell = facts.Cells[0];
            return facts;
        }

        private static CellStyleFacts MakeCell(int row, int column)
        {
            return new CellStyleFacts
            {
                Row = row,
                Column = column,
                ReadComplete = true,
                CellTypeReadComplete = true,
                IsTextCell = true,
                FontName = "Arial",
                TextSize = 12.0,
                BorderTopLineStyle = 10,
                BorderBottomLineStyle = 11,
                BorderLeftLineStyle = 12,
                BorderRightLineStyle = 13
            };
        }

        private static CellStyleFacts MakeReference()
        {
            return MakeCell(0, 0);
        }

        private static CellStyleFacts MakeCandidate(string font, double size)
        {
            CellStyleFacts cell = MakeReference();
            MakeStyleless(cell, font, size);
            return cell;
        }

        private static void MakeStyleless(CellStyleFacts cell, string font, double size)
        {
            SetAllBordersInvalid(cell);
            cell.FontName = font;
            cell.TextSize = size;
        }

        private static TextStyleFacts Text(string font, double size)
        {
            return new TextStyleFacts
            {
                IsResolvable = true,
                FontName = font,
                SizePoints = size,
                SizeTableUnits = size
            };
        }

        private static void SetAllBordersInvalid(CellStyleFacts cell)
        {
            cell.BorderTopLineStyle = -1;
            cell.BorderBottomLineStyle = -1;
            cell.BorderLeftLineStyle = -1;
            cell.BorderRightLineStyle = -1;
        }
    }
}
