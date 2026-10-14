using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    // Culture changes are restored by TestCultureScope, and these collections
    // are also kept out of parallel execution so a test cannot observe a
    // neighboring test's temporary culture.
    [CollectionDefinition("CultureSensitive", DisableParallelization = true)]
    public sealed class CultureSensitiveCollectionDefinition
    {
    }

    [CollectionDefinition("WorkbookFileSystem", DisableParallelization = true)]
    public sealed class WorkbookFileSystemCollectionDefinition
    {
    }
}
