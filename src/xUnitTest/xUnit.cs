using Microsoft.VisualStudio.TestPlatform.Utilities;
using System;
using Xunit;
using Xunit.Abstractions;


namespace GitHub.TestLogger.Example.UnitTests {

#pragma warning disable IDE1006 // Naming Styles
    public class xUnit {
#pragma warning restore IDE1006 // Naming Styles

        private readonly ITestOutputHelper _outputHelper;

        public xUnit(ITestOutputHelper outputHelper) {
            _outputHelper = outputHelper;
        }

        [Fact]
        public void FactTestSuccess() {
            var value = 1;
            Assert.Equal(1, value);
        }

        private void MoreStackDepth() {
            var value = 2;
            Assert.Equal(1, value);
        }

        [Fact]
        public void FactTestFailure() {
            _outputHelper.WriteLine("Hello world...");
            MoreStackDepth();
            _outputHelper.WriteLine("Goodbye world!");
        }

    }
}
