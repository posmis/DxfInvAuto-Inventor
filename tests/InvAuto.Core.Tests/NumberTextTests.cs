using InvAuto.Core.Formatting;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class NumberTextTests
    {
        [Theory]
        [InlineData("2,5", 2.5)]
        [InlineData(" 2.5 ", 2.5)]
        [InlineData("1 234,5", 1234.5)]
        [InlineData("1 234,5", 1234.5)]   // неразрывный пробел (Excel)
        [InlineData("1 234,5", 1234.5)]   // узкий неразрывный пробел (разделитель тысяч в ru-RU)
        [InlineData("7850", 7850)]
        [InlineData("-3", -3)]
        public void Parses_CommaDotAndSpaces(string text, double expected)
        {
            Assert.True(NumberText.TryParse(text, out double v));
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("1,2,3")]
        public void Rejects_NotNumbers(string text) => Assert.Null(NumberText.Parse(text));

        [Fact]
        public void Range()
        {
            Assert.True(NumberText.TryParse("5", 0, 10, out _));
            Assert.False(NumberText.TryParse("11", 0, 10, out _));
        }
    }
}
