using System;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal enum ModelMutationStatus
    {
        Committed,
        NotStarted,
        StartThrew,
        Aborted,
        CommitNotCommitted,
        FatalRegeneration,
        Failed
    }

    internal enum ModelMutationLinkLoadStatus
    {
        Loaded,
        StorageUnavailable,
        LegacyMigrationRefused,
        LegacyMigrationFailed,
        SchemaV2Unavailable
    }

    internal sealed class ModelMutationOptions
    {
        public IFailuresPreprocessor FailuresPreprocessor;
        public bool ClearAfterRollback;
    }

    internal sealed class ModelMutationResult<T>
    {
        public ModelMutationStatus Status;
        public T Value;
        public string AbortReason = string.Empty;
        public Exception Exception;
        public bool TransactionStarted;
        public TransactionStatus? StartStatus;
        public TransactionStatus? CommitStatus;
        public TransactionStatus? RollbackStatus;

        public bool RollbackVerified
        {
            get
            {
                return RollbackStatus.HasValue
                    && RollbackStatus.Value == TransactionStatus.RolledBack;
            }
        }
    }

    internal sealed class ModelMutationLinkLoadResult
    {
        public ModelMutationLinkLoadStatus Status;
        public ExcelLinkInfo Info;

        public bool IsLoaded
        {
            get { return Status == ModelMutationLinkLoadStatus.Loaded; }
        }
    }

    internal sealed class ModelMutationContext
    {
        private bool _abortRequested;
        private string _abortReason = string.Empty;

        internal ModelMutationContext()
        {
        }

        internal bool AbortRequested
        {
            get { return _abortRequested; }
        }

        internal string AbortReason
        {
            get { return _abortReason; }
        }

        public void Abort(string reason)
        {
            _abortRequested = true;
            _abortReason = reason ?? string.Empty;
        }

        public ModelMutationLinkLoadResult LoadV2Link(
            ViewSchedule schedule, bool allowLegacyMigration)
        {
            ExcelLinkStorage storage = schedule != null
                ? ExcelLinkSchema.LoadAny(schedule) : null;
            if (storage == null || storage.Info == null)
            {
                return new ModelMutationLinkLoadResult
                {
                    Status = ModelMutationLinkLoadStatus.StorageUnavailable
                };
            }
            if (storage.IsV2)
            {
                return new ModelMutationLinkLoadResult
                {
                    Status = ModelMutationLinkLoadStatus.Loaded,
                    Info = storage.Info
                };
            }
            if (!allowLegacyMigration)
            {
                return new ModelMutationLinkLoadResult
                {
                    Status = ModelMutationLinkLoadStatus.LegacyMigrationRefused
                };
            }
            if (!ExcelLinkMigration.MigrateInOpenTransaction(schedule))
            {
                return new ModelMutationLinkLoadResult
                {
                    Status = ModelMutationLinkLoadStatus.LegacyMigrationFailed
                };
            }

            ExcelLinkInfo migrated = ExcelLinkSchema.LoadV2(schedule);
            if (migrated == null)
            {
                return new ModelMutationLinkLoadResult
                {
                    Status = ModelMutationLinkLoadStatus.SchemaV2Unavailable
                };
            }
            return new ModelMutationLinkLoadResult
            {
                Status = ModelMutationLinkLoadStatus.Loaded,
                Info = migrated
            };
        }
    }

    internal static class ModelMutation
    {
        public static ModelMutationResult<T> Run<T>(
            Document doc,
            string transactionName,
            Func<ModelMutationContext, T> body,
            ModelMutationOptions options = null)
        {
            ModelMutationResult<T> result = new ModelMutationResult<T>();
            Transaction transaction = new Transaction(doc, transactionName);
            try
            {
                try
                {
                    result.StartStatus = transaction.Start();
                }
                catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
                {
                    throw;
                }
                catch (RegenerationFailureProbeException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Status = ModelMutationStatus.StartThrew;
                    result.Exception = ex;
                    result.RollbackStatus =
                        TransactionRollback.RollBackIfStarted(transaction);
                    return result;
                }

                if (result.StartStatus.Value != TransactionStatus.Started)
                {
                    result.Status = ModelMutationStatus.NotStarted;
                    return result;
                }
                result.TransactionStarted = true;

                ConfigureFailureHandling(transaction, options);

                ModelMutationContext context = new ModelMutationContext();
                T value = body(context);
                if (context.AbortRequested)
                {
                    result.Status = ModelMutationStatus.Aborted;
                    result.AbortReason = context.AbortReason;
                    result.RollbackStatus =
                        TransactionRollback.RollBackIfStarted(transaction);
                    return result;
                }

                result.CommitStatus = transaction.Commit();
                if (result.CommitStatus.Value != TransactionStatus.Committed)
                {
                    result.Status = ModelMutationStatus.CommitNotCommitted;
                    result.RollbackStatus =
                        TransactionRollback.RollBackIfStarted(transaction);
                    return result;
                }

                result.Status = ModelMutationStatus.Committed;
                result.Value = value;
                return result;
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException ex)
            {
                result.Status = ModelMutationStatus.FatalRegeneration;
                result.Exception = ex;
                result.RollbackStatus =
                    TransactionRollback.RollBackIfStarted(transaction);
                return result;
            }
            catch (RegenerationFailureProbeException ex)
            {
                result.Status = ModelMutationStatus.FatalRegeneration;
                result.Exception = ex;
                result.RollbackStatus =
                    TransactionRollback.RollBackIfStarted(transaction);
                return result;
            }
            catch (Exception ex)
            {
                result.Status = ModelMutationStatus.Failed;
                result.Exception = ex;
                result.RollbackStatus =
                    TransactionRollback.RollBackIfStarted(transaction);
                return result;
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        private static void ConfigureFailureHandling(
            Transaction transaction, ModelMutationOptions options)
        {
            if (options == null
                || (options.FailuresPreprocessor == null && !options.ClearAfterRollback))
            {
                return;
            }

            try
            {
                FailureHandlingOptions failureOptions =
                    transaction.GetFailureHandlingOptions();
                if (options.FailuresPreprocessor != null)
                {
                    failureOptions.SetFailuresPreprocessor(options.FailuresPreprocessor);
                }
                if (options.ClearAfterRollback)
                {
                    failureOptions.SetClearAfterRollback(true);
                }
                transaction.SetFailureHandlingOptions(failureOptions);
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                throw;
            }
            catch (RegenerationFailureProbeException)
            {
                throw;
            }
            catch (Exception)
            {
                // Failure-handling setup is best effort. The transaction result
                // still decides whether the mutation may be reported as success.
            }
        }
    }
}
