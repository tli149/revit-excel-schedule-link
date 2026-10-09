using System;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// Represents a failure while reading an .xlsx file.
    /// When this exception is thrown the caller (a Revit command) must not
    /// start a Transaction and must not modify any existing schedule.
    /// </summary>
    public class ExcelReadException : Exception
    {
        public ExcelReadException(string message) : base(message)
        {
        }

        public ExcelReadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
