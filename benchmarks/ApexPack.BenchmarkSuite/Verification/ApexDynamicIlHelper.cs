using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace ApexPack.BenchmarkSuite.Verification;

/// <summary>
/// Fast delegate compiler using DynamicMethod (IL Emit).
/// Completely avoids slow Reflection.Invoke, FieldInfo.GetValue/SetValue, and Activator.CreateInstance.
/// Compiled delegates are statically cached for instant zero-overhead execution.
/// </summary>
public static class ApexDynamicIlHelper
{
    private static readonly ConcurrentDictionary<Type, Func<object>> s_factories = new();
    private static readonly ConcurrentDictionary<FieldInfo, Action<object, object?>> s_fieldSetters = new();
    private static readonly ConcurrentDictionary<FieldInfo, Func<object, object?>> s_fieldGetters = new();

    /// <summary>
    /// Compiles an IL factory delegate to instantiate a type (even with private parameterless constructor).
    /// </summary>
    public static Func<object> GetFactory(Type type)
    {
        return s_factories.GetOrAdd(type, static t =>
        {
            var dynMethod = new DynamicMethod($"Apex_Create_{t.Name}", typeof(object), Type.EmptyTypes, t.Module, skipVisibility: true);
            var il = dynMethod.GetILGenerator();

            if (t.IsValueType)
            {
                var local = il.DeclareLocal(t);
                il.Emit(OpCodes.Ldloca_S, local);
                il.Emit(OpCodes.Initobj, t);
                il.Emit(OpCodes.Ldloc, local);
                il.Emit(OpCodes.Box, t);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                var ctor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (ctor == null)
                {
                    throw new InvalidOperationException($"Type '{t.FullName}' does not have a parameterless constructor.");
                }
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
            }

            return (Func<object>)dynMethod.CreateDelegate(typeof(Func<object>));
        });
    }

    /// <summary>
    /// Compiles an IL delegate to set a field (even private or init/readonly) directly.
    /// </summary>
    public static Action<object, object?> GetFieldSetter(FieldInfo field)
    {
        return s_fieldSetters.GetOrAdd(field, static f =>
        {
            var dynMethod = new DynamicMethod($"Apex_Set_{f.DeclaringType?.Name}_{f.Name}", null, new[] { typeof(object), typeof(object) }, f.Module, skipVisibility: true);
            var il = dynMethod.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            if (f.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Unbox, f.DeclaringType);
            }
            else
            {
                il.Emit(OpCodes.Castclass, f.DeclaringType);
            }

            il.Emit(OpCodes.Ldarg_1);
            if (f.FieldType.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, f.FieldType);
            }
            else
            {
                il.Emit(OpCodes.Castclass, f.FieldType);
            }

            il.Emit(OpCodes.Stfld, f);
            il.Emit(OpCodes.Ret);

            return (Action<object, object?>)dynMethod.CreateDelegate(typeof(Action<object, object?>));
        });
    }

    /// <summary>
    /// Compiles an IL delegate to get a field value directly without boxing overhead when possible.
    /// </summary>
    public static Func<object, object?> GetFieldGetter(FieldInfo field)
    {
        return s_fieldGetters.GetOrAdd(field, static f =>
        {
            var dynMethod = new DynamicMethod($"Apex_Get_{f.DeclaringType?.Name}_{f.Name}", typeof(object), new[] { typeof(object) }, f.Module, skipVisibility: true);
            var il = dynMethod.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            if (f.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Unbox, f.DeclaringType);
            }
            else
            {
                il.Emit(OpCodes.Castclass, f.DeclaringType);
            }

            il.Emit(OpCodes.Ldfld, f);
            if (f.FieldType.IsValueType)
            {
                il.Emit(OpCodes.Box, f.FieldType);
            }

            il.Emit(OpCodes.Ret);

            return (Func<object, object?>)dynMethod.CreateDelegate(typeof(Func<object, object?>));
        });
    }
}
