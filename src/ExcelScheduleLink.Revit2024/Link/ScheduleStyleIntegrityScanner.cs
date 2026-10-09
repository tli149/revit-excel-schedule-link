using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Revit 2024 adapter for the dependency-free style integrity decision
    /// table. Reads are deliberately transaction-scoped; this class never
    /// commits, checks out, activates, or mutates a schedule.
    /// </summary>
    internal static class ScheduleStyleIntegrityScanner
    {
        internal const string ReadTransactionName =
            "Excel Schedule Link - Check Schedule Styles";

        public static StyleIntegrityResult ScanWithRolledBackTransaction(
            Document doc, ViewSchedule schedule)
        {
            return ScanWithRolledBackTransaction(doc, schedule, null);
        }

        internal static StyleIntegrityResult ScanWithRolledBackTransaction(
            Document doc, ViewSchedule schedule, long? remainingAutomaticCellBudget)
        {
            StyleIntegrityResult result = new StyleIntegrityResult();
            if (doc == null)
            {
                return Failure(StyleIntegrityReasonCode.InvalidDocument, "Document was null.");
            }
            if (schedule == null || !schedule.IsValidObject)
            {
                return Failure(StyleIntegrityReasonCode.InvalidSchedule,
                    "The schedule was null or invalid.");
            }

            try
            {
                result.DocumentModifiedBeforeScan = doc.IsModified;
            }
            catch (Exception)
            {
                // The evidence field is optional; the scan can still be safe.
            }

            if (doc.IsReadOnly)
            {
                return Failure(StyleIntegrityReasonCode.DocumentReadOnly,
                    "The document is read-only.");
            }
            if (doc.IsModifiable)
            {
                return Failure(StyleIntegrityReasonCode.ForeignTransactionActive,
                    "A transaction is already active on the document.");
            }

            Transaction transaction = null;
            bool started = false;
            try
            {
                transaction = new Transaction(doc, ReadTransactionName);
                TransactionStatus startStatus = transaction.Start();
                result.ReadTransactionStartStatus = startStatus.ToString();
                if (startStatus != TransactionStatus.Started)
                {
                    return FailureInto(result, StyleIntegrityReasonCode.TransactionStartFailed,
                        "The scan transaction did not start.");
                }
                started = true;
                if (!doc.IsModifiable)
                {
                    return FailureInto(result, StyleIntegrityReasonCode.DocumentNotModifiable,
                        "The document was not modifiable after the transaction started.");
                }

                result = ScanInOpenTransaction(
                    doc, schedule, false, remainingAutomaticCellBudget);
                result.ReadTransactionStartStatus = startStatus.ToString();
                return result;
            }
            catch (Exception ex)
            {
                StyleIntegrityReasonCode reason = IsElementReadDenied(ex)
                    ? StyleIntegrityReasonCode.ElementReadDenied
                    : StyleIntegrityReasonCode.TableReadFailed;
                return FailureInto(result, reason, FormatException(ex));
            }
            finally
            {
                if (transaction != null && started)
                {
                    try
                    {
                        TransactionStatus? rollbackStatus =
                            TransactionRollback.RollBackIfStarted(transaction);
                        result.ReadTransactionRollbackStatus = rollbackStatus.HasValue
                            ? rollbackStatus.Value.ToString() : "Unreadable";
                        if (rollbackStatus != TransactionStatus.RolledBack)
                        {
                            result.ReadTransactionRollbackError =
                                "The scan transaction rollback final status was "
                                + result.ReadTransactionRollbackStatus + ".";
                            FailureInto(result, StyleIntegrityReasonCode.TransactionRollbackFailed,
                                result.ReadTransactionRollbackError);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ReadTransactionRollbackStatus = "Failed";
                        result.ReadTransactionRollbackError = FormatException(ex);
                        FailureInto(result, StyleIntegrityReasonCode.TransactionRollbackFailed,
                            result.ReadTransactionRollbackError);
                    }
                }

                try
                {
                    result.DocumentModifiedAfterRollback = doc.IsModified;
                }
                catch (Exception)
                {
                    // Keep the scan result, but leave the optional evidence unset.
                }
                if (transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        /// <summary>
        /// Reads one schedule while the caller's transaction is open. The caller
        /// owns that transaction; this method never starts, commits, rolls back,
        /// checks out, or changes a table.
        /// </summary>
        internal static StyleIntegrityResult ScanInOpenTransaction(
            Document doc, ViewSchedule schedule)
        {
            return ScanInOpenTransaction(doc, schedule, false);
        }

        /// <summary>
        /// Validates a header that the caller has just mutated inside its own
        /// open transaction. Revit gives cells created by InsertRow /
        /// InsertColumn their native style only when the document regenerates:
        /// before that, every inserted cell reads as the styleless fallback
        /// (verified on a real schedule with the exact v0.9.1 render steps,
        /// ESL_StyleMaterialize_20261007T235758Z / 235808Z: no regeneration ->
        /// all inserted cells styleless; Document.Regenerate() or a commit ->
        /// all healthy; section/schedule RefreshData and re-applying widths or
        /// heights do not help). The document is therefore regenerated first,
        /// so the guard judges the state that the commit would persist, and a
        /// header that is still styleless after regeneration - diagnostic defect 9
        /// state - is caught before it can be saved. Exceptions from
        /// Regenerate propagate to the caller's transaction boundary.
        /// </summary>
        internal static StyleIntegrityResult ScanAfterMutationInOpenTransaction(
            Document doc, ViewSchedule schedule, bool requireTextCells)
        {
            if (doc != null && doc.IsModifiable)
            {
                // Revit's RegenerationFailedException cannot be constructed by
                // a probe. The private marker follows the same fatal route as
                // the native exception and is consumed only once.
                if (DebugRegenerationFailureInjector.ConsumeNextRegenerationFailure())
                {
                    throw new RegenerationFailureProbeException();
                }
                doc.Regenerate();
            }
            return ScanInOpenTransaction(doc, schedule, requireTextCells);
        }

        internal static StyleIntegrityResult ScanInOpenTransaction(
            Document doc, ViewSchedule schedule, bool requireTextCells)
        {
            return ScanInOpenTransaction(doc, schedule, requireTextCells, null);
        }

        internal static StyleIntegrityResult ScanInOpenTransaction(
            Document doc, ViewSchedule schedule, bool requireTextCells,
            long? remainingAutomaticCellBudget)
        {
            if (doc == null)
            {
                return Failure(StyleIntegrityReasonCode.InvalidDocument, "Document was null.");
            }
            if (schedule == null || !schedule.IsValidObject)
            {
                return Failure(StyleIntegrityReasonCode.InvalidSchedule,
                    "The schedule was null or invalid.");
            }
            if (doc.IsReadOnly)
            {
                return Failure(StyleIntegrityReasonCode.DocumentReadOnly,
                    "The document is read-only.");
            }
            if (!doc.IsModifiable)
            {
                return Failure(StyleIntegrityReasonCode.DocumentNotModifiable,
                    "The document is not modifiable inside the caller's transaction.");
            }

            ScheduleStyleFacts facts = new ScheduleStyleFacts();
            TableSectionData header;
            try
            {
                TableData data = schedule.GetTableData();
                header = data.GetSectionData(SectionType.Header);
                if (header == null || !header.IsValidObject)
                {
                    return Failure(StyleIntegrityReasonCode.TableReadFailed,
                        "The header section was unavailable.");
                }
            }
            catch (Exception ex)
            {
                return Failure(IsElementReadDenied(ex)
                    ? StyleIntegrityReasonCode.ElementReadDenied
                    : StyleIntegrityReasonCode.TableReadFailed, FormatException(ex));
            }

            try
            {
                facts.NumberOfRows = header.NumberOfRows;
                facts.NumberOfColumns = header.NumberOfColumns;
                facts.FirstRowNumber = header.FirstRowNumber;
                facts.FirstColumnNumber = header.FirstColumnNumber;
                facts.ExpectedHeaderCellCount = checked(
                    facts.NumberOfRows * facts.NumberOfColumns);
                facts.DimensionsRead = facts.NumberOfRows > 0
                    && facts.NumberOfColumns > 0;
            }
            catch (Exception ex)
            {
                facts.FirstReadError = FormatException(ex);
                facts.DimensionsRead = false;
            }

            if (!facts.DimensionsRead)
            {
                return StyleIntegrityDecisionTable.Evaluate(facts);
            }
            if (StyleIntegrityLimits.ExceedsScheduleLimit(
                facts.NumberOfRows, facts.NumberOfColumns))
            {
                facts.LimitExceeded = true;
                return StyleIntegrityDecisionTable.Evaluate(facts);
            }
            if (remainingAutomaticCellBudget.HasValue
                && facts.ExpectedHeaderCellCount > remainingAutomaticCellBudget.Value)
            {
                facts.CellBudgetExceededBeforeScan = true;
                facts.LimitExceeded = true;
                return StyleIntegrityDecisionTable.Evaluate(facts);
            }

            try
            {
                facts.TitleTextStyle = ResolveTextStyle(
                    doc, schedule.TitleTextTypeId, "TitleTextTypeId", facts);
            }
            catch (Exception ex)
            {
                facts.TitleTextStyle = TextStyleReadFailure(
                    new TextStyleFacts { ReadAttempted = true }, facts,
                    "TitleTextTypeId text style read failed: " + FormatException(ex), ex);
            }
            try
            {
                facts.HeaderTextStyle = ResolveTextStyle(
                    doc, schedule.HeaderTextTypeId, "HeaderTextTypeId", facts);
            }
            catch (Exception ex)
            {
                facts.HeaderTextStyle = TextStyleReadFailure(
                    new TextStyleFacts { ReadAttempted = true }, facts,
                    "HeaderTextTypeId text style read failed: " + FormatException(ex), ex);
            }

            for (int rowOffset = 0; rowOffset < facts.NumberOfRows; rowOffset++)
            {
                int row = facts.FirstRowNumber + rowOffset;
                for (int columnOffset = 0; columnOffset < facts.NumberOfColumns; columnOffset++)
                {
                    int column = facts.FirstColumnNumber + columnOffset;
                    CellStyleFacts cell = ReadCell(
                        doc, header, row, column,
                        facts.FirstRowNumber, facts.FirstColumnNumber);
                    facts.Cells.Add(cell);
                    if (rowOffset == 0 && columnOffset == 0)
                    {
                        facts.ReferenceCell = cell;
                    }
                    if (!cell.ReadComplete || (requireTextCells && !cell.CellTypeReadComplete))
                    {
                        facts.FirstReadError = FirstError(
                            facts.FirstReadError, cell.ReadError);
                    }
                }
            }
            return StyleIntegrityDecisionTable.Evaluate(
                facts, ElementId.InvalidElementId.Value, requireTextCells);
        }

        /// <summary>
        /// Bounded batch seam for the document-open and Link Manager callers.
        /// The existing integrity path does not add those callers, but the limits live here so a
        /// future caller cannot accidentally create an unbounded scan.
        /// </summary>
        internal static IList<StyleIntegrityResult> ScanSchedules(
            Document doc, IList<ViewSchedule> schedules, StyleIntegrityScanScope scope)
        {
            List<StyleIntegrityResult> results = new List<StyleIntegrityResult>();
            if (schedules == null)
            {
                return results;
            }

            int maxSchedules = StyleIntegrityLimits.MaxSchedules(scope);
            long maxCells = StyleIntegrityLimits.MaxHeaderCells(scope);
            long cellsScanned = 0;
            for (int index = 0; index < schedules.Count; index++)
            {
                int remaining = schedules.Count - index;
                if (index >= maxSchedules || cellsScanned >= maxCells)
                {
                    results.Add(StyleIntegrityDecisionTable.LimitResult(remaining));
                    break;
                }

                long? remainingAutomaticCellBudget = scope == StyleIntegrityScanScope.Automatic
                    ? maxCells - cellsScanned : (long?)null;
                StyleIntegrityResult result = ScanWithRolledBackTransaction(
                    doc, schedules[index], remainingAutomaticCellBudget);
                if (result.Facts != null && result.Facts.CellBudgetExceededBeforeScan)
                {
                    results.Add(StyleIntegrityDecisionTable.LimitResult(remaining));
                    break;
                }
                results.Add(result);
                if (result.HeaderCellsExpected > 0)
                {
                    cellsScanned += result.HeaderCellsExpected;
                }
            }
            return results;
        }

        private static TextStyleFacts ResolveTextStyle(
            Document doc, ElementId id, string label, ScheduleStyleFacts facts)
        {
            TextStyleFacts result = new TextStyleFacts();
            if (id == null || id == ElementId.InvalidElementId)
            {
                result.IsMissing = true;
                result.ReadError = label + " was invalid.";
                facts.FirstReadError = FirstError(facts.FirstReadError, result.ReadError);
                return result;
            }

            result.ReadAttempted = true;
            Element element;
            try
            {
                element = doc.GetElement(id);
            }
            catch (Exception ex)
            {
                return TextStyleReadFailure(result, facts,
                    label + " could not be read: " + FormatException(ex), ex);
            }

            TextNoteType textType = element as TextNoteType;
            if (textType == null)
            {
                result.IsMissing = true;
                result.ReadError = label + " did not resolve to TextNoteType.";
                facts.FirstReadError = FirstError(facts.FirstReadError, result.ReadError);
                return result;
            }

            try
            {
                Parameter fontParameter = textType.get_Parameter(BuiltInParameter.TEXT_FONT);
                Parameter sizeParameter = textType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                if (fontParameter == null || sizeParameter == null)
                {
                    return TextStyleReadFailure(result, facts,
                        label + " text parameters were unavailable.", null);
                }
                if (fontParameter.StorageType != StorageType.String)
                {
                    return TextStyleReadFailure(result, facts,
                        label + " font parameter had storage type "
                        + fontParameter.StorageType + ".", null);
                }
                if (sizeParameter.StorageType != StorageType.Double)
                {
                    return TextStyleReadFailure(result, facts,
                        label + " size parameter had storage type "
                        + sizeParameter.StorageType + ".", null);
                }

                result.FontName = fontParameter.AsString();
                result.RawSize = sizeParameter.AsDouble();
                result.SizeInches = UnitUtils.ConvertFromInternalUnits(
                    result.RawSize, UnitTypeId.Inches);
                result.SizePoints = result.SizeInches * 72.0;
                result.SizeTableUnits = result.SizeInches * 96.0;
                result.IsResolvable = !string.IsNullOrEmpty(result.FontName)
                    && !double.IsNaN(result.SizePoints)
                    && !double.IsInfinity(result.SizePoints)
                    && !double.IsNaN(result.SizeTableUnits)
                    && !double.IsInfinity(result.SizeTableUnits);
                if (!result.IsResolvable)
                {
                    return TextStyleReadFailure(result, facts,
                        label + " font or size was unavailable.", null);
                }
                return result;
            }
            catch (Exception ex)
            {
                return TextStyleReadFailure(result, facts,
                    label + " text style read failed: " + FormatException(ex), ex);
            }
        }

        private static TextStyleFacts TextStyleReadFailure(
            TextStyleFacts result, ScheduleStyleFacts facts, string error, Exception ex)
        {
            result.ReadFailed = true;
            result.AccessDenied = ex != null && IsElementReadDenied(ex);
            result.ReadError = error ?? string.Empty;
            facts.FirstReadError = FirstError(facts.FirstReadError, result.ReadError);
            return result;
        }

        private static CellStyleFacts ReadCell(
            Document doc, TableSectionData section,
            int actualRow, int actualColumn, int firstRow, int firstColumn)
        {
            CellStyleFacts cell = new CellStyleFacts
            {
                Row = actualRow - firstRow,
                Column = actualColumn - firstColumn,
                ReadComplete = true
            };
            List<string> errors = new List<string>();

            try
            {
                cell.IsCellOverridden = section.IsCellOverridden(actualRow, actualColumn);
            }
            catch (Exception ex)
            {
                cell.ReadComplete = false;
                errors.Add("IsCellOverridden: " + FormatException(ex));
            }

            try
            {
                cell.CellTypeReadComplete = true;
                cell.IsTextCell = section.GetCellType(actualRow, actualColumn) == CellType.Text;
            }
            catch (Exception ex)
            {
                cell.ReadComplete = false;
                errors.Add("GetCellType: " + FormatException(ex));
            }

            TableCellStyle style = null;
            try
            {
                style = section.GetTableCellStyle(actualRow, actualColumn);
                if (style == null)
                {
                    cell.ReadComplete = false;
                    errors.Add("GetTableCellStyle returned null.");
                }
            }
            catch (Exception ex)
            {
                cell.ReadComplete = false;
                errors.Add("GetTableCellStyle: " + FormatException(ex));
            }

            if (style != null)
            {
                try { cell.FontName = style.FontName; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("FontName: " + FormatException(ex)); }
                try { cell.TextSize = style.TextSize; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("TextSize: " + FormatException(ex)); }
                try { cell.IsStyleOverridden = style.IsOverridden; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("IsOverridden: " + FormatException(ex)); }
                try { cell.BorderTopLineStyle = style.BorderTopLineStyle.Value; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("BorderTopLineStyle: " + FormatException(ex)); }
                try { cell.BorderBottomLineStyle = style.BorderBottomLineStyle.Value; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("BorderBottomLineStyle: " + FormatException(ex)); }
                try { cell.BorderLeftLineStyle = style.BorderLeftLineStyle.Value; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("BorderLeftLineStyle: " + FormatException(ex)); }
                try { cell.BorderRightLineStyle = style.BorderRightLineStyle.Value; }
                catch (Exception ex) { cell.ReadComplete = false; errors.Add("BorderRightLineStyle: " + FormatException(ex)); }

                try
                {
                    int errorsBeforeOptions = errors.Count;
                    TableCellStyleOverrideOptions options = style.GetCellStyleOverrideOptions();
                    if (options == null)
                    {
                        cell.ReadComplete = false;
                        errors.Add("GetCellStyleOverrideOptions returned null.");
                    }
                    else
                    {
                        cell.HasAnyOverrideOption = ReadAnyOverrideOption(options, errors);
                    }
                    if (errors.Count != errorsBeforeOptions)
                    {
                        cell.ReadComplete = false;
                    }
                }
                catch (Exception ex)
                {
                    cell.ReadComplete = false;
                    errors.Add("GetCellStyleOverrideOptions: " + FormatException(ex));
                }
            }

            cell.ReadError = string.Join("; ", errors.ToArray());
            cell.ElementReadDenied = LooksLikeElementReadDenied(cell.ReadError);
            return cell;
        }

        private static bool ReadAnyOverrideOption(
            TableCellStyleOverrideOptions options, IList<string> errors)
        {
            bool any = false;
            any |= ReadOverrideOption(options, "Font", delegate { return options.Font; }, errors);
            any |= ReadOverrideOption(options, "FontSize", delegate { return options.FontSize; }, errors);
            any |= ReadOverrideOption(options, "FontColor", delegate { return options.FontColor; }, errors);
            any |= ReadOverrideOption(options, "BackgroundColor", delegate { return options.BackgroundColor; }, errors);
            any |= ReadOverrideOption(options, "Bold", delegate { return options.Bold; }, errors);
            any |= ReadOverrideOption(options, "Italics", delegate { return options.Italics; }, errors);
            any |= ReadOverrideOption(options, "Underline", delegate { return options.Underline; }, errors);
            any |= ReadOverrideOption(options, "HorizontalAlignment", delegate { return options.HorizontalAlignment; }, errors);
            any |= ReadOverrideOption(options, "VerticalAlignment", delegate { return options.VerticalAlignment; }, errors);
            any |= ReadOverrideOption(options, "TextOrientation", delegate { return options.TextOrientation; }, errors);
            any |= ReadOverrideOption(options, "BorderLineStyle", delegate { return options.BorderLineStyle; }, errors);
            any |= ReadOverrideOption(options, "BorderTopLineStyle", delegate { return options.BorderTopLineStyle; }, errors);
            any |= ReadOverrideOption(options, "BorderBottomLineStyle", delegate { return options.BorderBottomLineStyle; }, errors);
            any |= ReadOverrideOption(options, "BorderLeftLineStyle", delegate { return options.BorderLeftLineStyle; }, errors);
            any |= ReadOverrideOption(options, "BorderRightLineStyle", delegate { return options.BorderRightLineStyle; }, errors);
            return any;
        }

        private static bool ReadOverrideOption(
            TableCellStyleOverrideOptions options, string name,
            Func<bool> read, IList<string> errors)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                errors.Add("override." + name + ": " + FormatException(ex));
                return false;
            }
        }

        private static StyleIntegrityResult Failure(
            StyleIntegrityReasonCode reason, string error)
        {
            return FailureInto(new StyleIntegrityResult(), reason, error);
        }

        private static StyleIntegrityResult FailureInto(
            StyleIntegrityResult result, StyleIntegrityReasonCode reason, string error)
        {
            result.State = StyleIntegrityState.Indeterminate;
            result.ReasonCode = reason;
            result.FirstReadError = FirstError(result.FirstReadError, error);
            return result;
        }

        private static string FirstError(string first, string next)
        {
            return string.IsNullOrEmpty(first) ? (next ?? string.Empty) : first;
        }

        private static string FormatException(Exception ex)
        {
            if (ex == null)
            {
                return "unknown failure";
            }
            return ex.GetType().Name + ": " + ex.Message;
        }

        private static bool IsElementReadDenied(Exception ex)
        {
            string name = ex == null || ex.GetType() == null
                ? string.Empty : ex.GetType().Name;
            return name.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Contention", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ObjectAccess", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ModificationForbidden", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeElementReadDenied(string text)
        {
            string value = text ?? string.Empty;
            return value.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("Contention", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("ObjectAccess", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("ModificationForbidden", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
