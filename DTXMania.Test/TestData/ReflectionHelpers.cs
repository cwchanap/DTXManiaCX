using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Test.TestData
{
    internal static class ReflectionHelpers
    {
        internal static BaseGame CreateGame(double totalGameTime = 0.0, double lastStageTransitionTime = 0.0)
        {
#pragma warning disable SYSLIB0050
            var game = (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050
            SetPrivateField(game, "_mainThreadActions", new ConcurrentQueue<Action>());
            SetPrivateField(game, "_pendingScreenshot", null);
            SetPrivateField(game, "_totalGameTime", totalGameTime);
            SetPrivateField(game, "_lastStageTransitionTime", lastStageTransitionTime);
            return game;
        }

        internal static BaseGame CreateGame(IResourceManager resourceManager)
        {
#pragma warning disable SYSLIB0050
            var game = (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050
            SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager);
            return game;
        }

        internal static T CreateUninitialized<T>() where T : class
        {
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }

        internal static T? GetPrivateField<T>(object target, string fieldName)
        {
            var field = GetField(target.GetType(), fieldName);
            Assert.NotNull(field);
            return (T?)field!.GetValue(target);
        }

        internal static object? InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = GetMethod(target.GetType(), methodName);
            Assert.NotNull(method);
            return method!.Invoke(target, args);
        }

        internal static T? InvokePrivateMethod<T>(object target, string methodName, params object[] args)
        {
            var result = InvokePrivateMethod(target, methodName, args);
            if (result is null)
            {
                return default;
            }

            return (T)result;
        }

        internal static async Task<T?> InvokePrivateMethodAsync<T>(object target, string methodName, params object[] args)
        {
            var method = GetMethod(target.GetType(), methodName);
            Assert.NotNull(method);

            var task = method!.Invoke(target, args);
            Assert.NotNull(task);

            return await Assert.IsAssignableFrom<Task<T>>(task);
        }

        internal static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = GetField(target.GetType(), fieldName);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        internal static void SetProperty(object target, string propertyName, object? value)
        {
            var property = GetProperty(target.GetType(), propertyName);
            Assert.NotNull(property);

            var setter = property!.GetSetMethod(nonPublic: true);
            Assert.NotNull(setter);
            setter!.Invoke(target, new[] { value });
        }

        internal static FieldInfo? GetField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType!;
            }

            return null;
        }

        internal static MethodInfo? GetMethod(Type type, string methodName)
        {
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (method != null)
                {
                    return method;
                }

                type = type.BaseType!;
            }

            return null;
        }

        internal static PropertyInfo? GetProperty(Type type, string propertyName)
        {
            while (type != null)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null)
                {
                    return property;
                }

                type = type.BaseType!;
            }

            return null;
        }
    }
}
