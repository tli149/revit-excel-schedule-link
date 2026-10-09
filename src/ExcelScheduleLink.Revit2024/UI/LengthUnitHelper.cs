using System;
using Autodesk.Revit.DB;

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
        public static LengthUnitChoice Resolve(Document doc)
        {
            try
            {
                ForgeTypeId unit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
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
            catch (Exception)
            {
                return LengthUnitChoice.Millimeters; // fallback per spec
            }
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
    }
}
