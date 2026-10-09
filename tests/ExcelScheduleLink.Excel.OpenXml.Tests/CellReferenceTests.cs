using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml.Internal;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public class CellReferenceTests
    {
        [Theory]
        [InlineData("A1", 0, 0)]
        [InlineData("B13", 12, 1)]
        [InlineData("Z99", 98, 25)]
        [InlineData("AA10", 9, 26)]
        [InlineData("BC23", 22, 54)]
        [InlineData("b2", 1, 1)] // lowercase is accepted
        public void Parse_ValidReferences(string reference, int expectedRow, int expectedCol)
        {
            CellReference.Parse(reference, out int row, out int col);
            Assert.Equal(expectedRow, row);
            Assert.Equal(expectedCol, col);
        }

        [Theory]
        [InlineData("")]
        [InlineData("13")]
        [InlineData("ABC")]
        [InlineData("A0")]
        public void Parse_InvalidReferences_Throw(string reference)
        {
            Assert.Throws<ExcelReadException>(
                () => CellReference.Parse(reference, out _, out _));
        }

        [Fact]
        public void ParseRange_TwoCells()
        {
            CellReference.ParseRange("A1:F30", out int r1, out int c1, out int r2, out int c2);
            Assert.Equal(0, r1);
            Assert.Equal(0, c1);
            Assert.Equal(29, r2);
            Assert.Equal(5, c2);
        }

        [Fact]
        public void ParseRange_SingleCell()
        {
            CellReference.ParseRange("B2", out int r1, out int c1, out int r2, out int c2);
            Assert.Equal(1, r1);
            Assert.Equal(1, c1);
            Assert.Equal(r1, r2);
            Assert.Equal(c1, c2);
        }

        [Fact]
        public void ParseRange_Reversed_Throws()
        {
            Assert.Throws<ExcelReadException>(
                () => CellReference.ParseRange("F30:A1", out _, out _, out _, out _));
        }
    }
}
