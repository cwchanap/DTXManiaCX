using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for ResultStage focusing on pure logic methods
    /// that do not require graphics initialization.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ResultStageTests
    {
        private const string PerformanceSummaryKey = "performanceSummary";
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ResultStage(null));
        }

        #endregion

        #region Type Property Tests

        [Fact]
        public void Type_Property_ShouldExistAndReturnStageType()
        {
            var property = typeof(ResultStage).GetProperty(
                "Type",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            Assert.NotNull(property);
            Assert.Equal(typeof(StageType), property!.PropertyType);
        }

        [Fact]
        public void Type_Value_ShouldBeResult()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            Assert.Equal(StageType.Result, stage.Type);
        }

        #endregion

        #region ExtractSharedData Tests

        [Fact]
        public void ExtractSharedData_WithNullSharedData_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_sharedData", null);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.Equal(0, summary.MaxCombo);
            Assert.False(summary.ClearFlag);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_WithMissingPerformanceSummaryKey_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var sharedData = new Dictionary<string, object>
            {
                { "otherKey", "otherValue" }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.False(summary.ClearFlag);
        }

        [Fact]
        public void ExtractSharedData_WithValidPerformanceSummary_ShouldUseProvidedSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var expectedSummary = new PerformanceSummary
            {
                Score = 987654,
                MaxCombo = 250,
                ClearFlag = true,
                CompletionReason = CompletionReason.SongComplete
            };

            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, expectedSummary }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(987654, summary!.Score);
            Assert.Equal(250, summary.MaxCombo);
            Assert.True(summary.ClearFlag);
            Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_WithWrongTypeForSummaryKey_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            // Put wrong type under the performanceSummary key
            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, "not a PerformanceSummary" }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_DefaultSummary_ShouldHaveZeroJudgementCounts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_sharedData", null);
            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.JustCount);
            Assert.Equal(0, summary.GreatCount);
            Assert.Equal(0, summary.GoodCount);
            Assert.Equal(0, summary.PoorCount);
            Assert.Equal(0, summary.MissCount);
        }

        [Fact]
        public void ExtractSharedData_ValidSummary_PreservesJudgementCounts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var expectedSummary = new PerformanceSummary
            {
                Score = 500000,
                JustCount = 100,
                GreatCount = 50,
                GoodCount = 20,
                PoorCount = 5,
                MissCount = 10,
                MaxCombo = 80,
                ClearFlag = false
            };

            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, expectedSummary }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(100, summary!.JustCount);
            Assert.Equal(50, summary.GreatCount);
            Assert.Equal(20, summary.GoodCount);
            Assert.Equal(5, summary.PoorCount);
            Assert.Equal(10, summary.MissCount);
        }

        #endregion

        #region Inheritance and Interface Tests

        [Fact]
        public void ResultStage_ShouldInheritFromBaseStage()
        {
            Assert.True(typeof(BaseStage).IsAssignableFrom(typeof(ResultStage)));
        }

        [Fact]
        public void ResultStage_ShouldImplementIStage()
        {
            Assert.True(typeof(IStage).IsAssignableFrom(typeof(ResultStage)));
        }

        #endregion

        #region Helper Methods

        private static void InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var type = target.GetType();
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(target, args);
                    return;
                }
                type = type.BaseType;
            }
            Assert.Fail($"Method '{methodName}' not found");
        }

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    return (T?)field.GetValue(target);
                type = type.BaseType;
            }
            Assert.Fail($"Field '{fieldName}' not found");
            return default;
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                type = type.BaseType;
            }
            Assert.Fail($"Field '{fieldName}' not found");
        }

        #endregion
    }
}
