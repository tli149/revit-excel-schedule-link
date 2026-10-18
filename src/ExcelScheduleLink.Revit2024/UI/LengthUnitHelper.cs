using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>Display unit used for numeric column-width editing.</summary>
    internal enum LengthUnitChoice { Millimeters, Inches }

    /// <summary>
    /// Chooses the unit for the Column Widths dialog (design v0.8.0 7): metric
    /// projects edit in millimeters, imperial projects in inches - the project's
    /// own length format is only used to decide WHICH of the two, never used
    /// verbatim (so a project formatted in meters still edits in mm). Anything
    /// unknown falls back to millimeters.
    /// </summary>
    internal static class LengthUnitHelper
    {
        public static LengthUnitChoice FromLengthUnit(ForgeTypeId unit)
        {
            // Reference check: null never reaches ForgeTypeId's overloaded ==.
            if (ReferenceEquals(unit, null))
            {
                return LengthUnitChoice.Millimeters; // fallback per spec
            }
            if (unit == UnitTypeId.Inches
                || unit == UnitTypeId.Feet
                || unit == UnitTypeId.FeetFractionalInches
                || unit == UnitTypeId.FractionalInches
                || unit == UnitTypeId.UsSurveyFeet)
            {
                return LengthUnitChoice.Inches;
            }
            return LengthUnitChoice.Millimeters; // metric or anything unexpected
        }

        /// <summary>Short label shown next to the numbers ("mm" / "in").</summary>
        public static string Label(LengthUnitChoice choice)
        {
            return choice == LengthUnitChoice.Inches ? "in" : "mm";
        }

        /// <summary>Decimal places appropriate for the unit.</summary>
        public static int Decimals(LengthUnitChoice choice)
        {
            return choice == LengthUnitChoice.Inches ? 2 : 1;
        }

        private static ForgeTypeId TypeId(LengthUnitChoice choice)
        {
            return choice == LengthUnitChoice.Inches ? UnitTypeId.Inches : UnitTypeId.Millimeters;
        }

        /// <summary>Internal feet -> display value.</summary>
        public static double FromFeet(double feet, LengthUnitChoice choice)
        {
            try { return UnitUtils.ConvertFromInternalUnits(feet, TypeId(choice)); }
            catch (Exception) { return feet; }
        }

        /// <summary>Display value -> internal feet.</summary>
        public static double ToFeet(double value, LengthUnitChoice choice)
        {
            try { return UnitUtils.ConvertToInternalUnits(value, TypeId(choice)); }
            catch (Exception) { return value; }
        }

        public static string FitContentNotes(FixedTotalFitResult result,
            LengthUnitChoice unit, IReadOnlyList<string> columnHeadings)
        {
            if (result == null) return string.Empty;
            return UserMessages.LayoutFitContentNotes(
                result,
                FromFeet(result.ActualTotalFeet, unit),
                FromFeet(result.ComfortableTotalFeet, unit),
                Label(unit), Decimals(unit), columnHeadings);
        }

        public static string FitContentComfort(FixedTotalFitResult result,
            LengthUnitChoice unit)
        {
            if (result == null) return string.Empty;
            return UserMessages.LayoutFitContentComfort(
                FromFeet(result.ComfortableTotalFeet, unit),
                Label(unit), Decimals(unit), result.ComfortableEstimateLimited);
        }

        public static string ValidationMessage(ValidationResult validation,
            LengthUnitChoice unit, IReadOnlyList<string> columnHeadings)
        {
            if (validation == null) return string.Empty;
            double? totalLimit = validation.MaxTotalFeet.HasValue
                ? (double?)FromFeet(validation.MaxTotalFeet.Value, unit)
                : null;
            return UserMessages.LayoutInvalidInput(
                validation,
                FromFeet(LayoutMetrics.MinColumnFeet, unit),
                FromFeet(LayoutMetrics.MaxColumnFeet, unit),
                FromFeet(validation.TotalFeet, unit),
                totalLimit,
                Label(unit), Decimals(unit), columnHeadings);
        }

        public static string InvalidNumbersMessage(
            IReadOnlyList<int> columns, LengthUnitChoice unit,
            IReadOnlyList<string> columnHeadings)
        {
            return UserMessages.LayoutInvalidNumbers(
                columns,
                FromFeet(LayoutMetrics.MinColumnFeet, unit),
                FromFeet(LayoutMetrics.MaxColumnFeet, unit),
                Label(unit), Decimals(unit), columnHeadings);
        }
    }
}
