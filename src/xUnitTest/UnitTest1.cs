using Microsoft.VisualStudio.TestPlatform.Utilities;
using System;
using Xunit;
using Xunit.Abstractions;

namespace xUnitTest {
    public class UnitTest1 {
        private ITestOutputHelper _outputHelper;
        public UnitTest1(ITestOutputHelper outputHelper) {
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
            Console.WriteLine("is this message? (Console)");
            _outputHelper.WriteLine("is this message? (ITestOutputHelper)");
            MoreStackDepth();
        }

    }
}
