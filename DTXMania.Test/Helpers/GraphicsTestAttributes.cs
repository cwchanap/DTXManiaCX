using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Shared opt-in for tests that need a real GraphicsDevice. Headless CI never
    /// sets the variable, so device tests report Skipped instead of fake-passing.
    /// </summary>
    public static class GraphicsTestGate
    {
        public const string OptInVariable = "DTXMANIACX_ENABLE_GRAPHICS_TESTS";

        public const string SkipReason =
            "Requires a real GraphicsDevice; set DTXMANIACX_ENABLE_GRAPHICS_TESTS=1 to run.";

        public static bool IsEnabled =>
            string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fact for a device-required test: Skipped (never passed) unless graphics
    /// tests are opted in.
    /// </summary>
    public sealed class GraphicsFactAttribute : FactAttribute
    {
        public GraphicsFactAttribute()
        {
            if (!GraphicsTestGate.IsEnabled)
            {
                Skip = GraphicsTestGate.SkipReason;
            }
        }
    }

    /// <summary>
    /// Theory for a device-required test. A skipped theory is normally reported as
    /// a single test case, so GraphicsTheoryDiscoverer expands the data rows and
    /// reports each one (all InlineData cases) as Skipped.
    /// </summary>
    [XunitTestCaseDiscoverer("DTXMania.Test.Helpers.GraphicsTheoryDiscoverer", "DTXMania.Test")]
    public sealed class GraphicsTheoryAttribute : TheoryAttribute
    {
        public GraphicsTheoryAttribute()
        {
            if (!GraphicsTestGate.IsEnabled)
            {
                Skip = GraphicsTestGate.SkipReason;
            }
        }
    }

    /// <summary>Reports every data row of a skipped GraphicsTheory as Skipped.</summary>
    public sealed class GraphicsTheoryDiscoverer : TheoryDiscoverer
    {
        public GraphicsTheoryDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkip(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo theoryAttribute,
            string skipReason)
        {
            var testCases = new List<IXunitTestCase>();

            foreach (var dataAttribute in testMethod.Method.GetCustomAttributes(typeof(DataAttribute)))
            {
                var discovererAttribute = dataAttribute.GetCustomAttributes(typeof(DataDiscovererAttribute)).First();
                var dataDiscoverer = ExtensibilityPointFactory.GetDataDiscoverer(DiagnosticMessageSink, discovererAttribute);
                if (dataDiscoverer == null || !dataDiscoverer.SupportsDiscoveryEnumeration(dataAttribute, testMethod.Method))
                {
                    return base.CreateTestCasesForSkip(discoveryOptions, testMethod, theoryAttribute, skipReason);
                }

                var data = dataDiscoverer.GetData(dataAttribute, testMethod.Method);
                if (data == null)
                {
                    continue;
                }

                foreach (var dataRow in data)
                {
                    // Rows that cannot be serialized cannot be reported as distinct cases.
                    if (!SerializationHelper.IsSerializable(dataRow))
                    {
                        return base.CreateTestCasesForSkip(discoveryOptions, testMethod, theoryAttribute, skipReason);
                    }

                    testCases.AddRange(CreateTestCasesForSkippedDataRow(
                        discoveryOptions, testMethod, theoryAttribute, dataRow, skipReason));
                }
            }

            return testCases.Count == 0
                ? base.CreateTestCasesForSkip(discoveryOptions, testMethod, theoryAttribute, skipReason)
                : testCases;
        }
    }
}
