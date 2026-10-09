using Autodesk.Revit.DB;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Rolls back only an actually started transaction and returns the status
    /// observed after the operation. A null result means the final status could
    /// not be read, so callers must not claim that the rollback completed.
    /// </summary>
    internal static class TransactionRollback
    {
        public static TransactionStatus? RollBackIfStarted(Transaction transaction)
        {
            if (transaction == null)
            {
                return null;
            }

            try
            {
                TransactionStatus status = transaction.GetStatus();
                if (status == TransactionStatus.Pending)
                {
                    // Pending is Revit failure handling still in progress. Do
                    // not call RollBack or turn it into a model-change claim.
                    return status;
                }
                if (status == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }
                return transaction.GetStatus();
            }
            catch (System.Exception)
            {
                try
                {
                    return transaction.GetStatus();
                }
                catch (System.Exception)
                {
                    return null;
                }
            }
        }
    }
}
