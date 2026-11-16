using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace STS2Mobile.Patches;

// Mono may execute beforefieldinit type initializers earlier than CoreCLR.
// Order canonical models by the ModelDb references in their static initializers;
// keep the original constructors, registration, duplicate checks and failures.
public static class ModelDbInitPatch
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null))
        .ToDictionary(opcode => opcode.Value);

    public static void Apply(Harmony harmony)
    {
        PatchHelper.PatchCritical(harmony, typeof(ModelDb), "Init",
            prefix: PatchHelper.Method(typeof(ModelDbInitPatch), nameof(InitPrefix)));
    }

    public static void InitPrefix(ref Type[] __0)
    {
        __0 = OrderForInitialization(__0 ?? ModelDb.AllAbstractModelSubtypes);
        PatchHelper.Log($"[ModelDb] Static dependency order applied: {__0.Length} models; original constructors preserved [v4]");
    }

    public static Type[] OrderForInitialization(Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var available = new HashSet<Type>(types);
        var states = new Dictionary<Type, int>();
        var ordered = new List<Type>(types.Length);
        var chain = new List<Type>();

        void Visit(Type type)
        {
            if (states.TryGetValue(type, out int state))
            {
                if (state == 1)
                    throw new InvalidOperationException("Model static dependency cycle: "
                        + string.Join(" -> ", chain.Select(item => item.Name).Append(type.Name)));
                return;
            }
            states[type] = 1;
            chain.Add(type);
            var dependencies = GetStaticDependencies(type);
            // Stable order follows the caller's model list, not hash iteration.
            foreach (var candidate in types)
                if (dependencies.Contains(candidate) && available.Contains(candidate))
                    Visit(candidate);
            chain.RemoveAt(chain.Count - 1);
            states[type] = 2;
            ordered.Add(type);
        }

        foreach (var type in types)
            Visit(type);
        return ordered.ToArray();
    }

    public static HashSet<Type> GetStaticDependencies(Type type)
    {
        var dependencies = new HashSet<Type>();
        var visited = new HashSet<MethodBase>();
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            Scan(current.TypeInitializer, dependencies, visited);
        return dependencies;
    }

    private static void Scan(MethodBase method, HashSet<Type> dependencies, HashSet<MethodBase> visited)
    {
        if (method == null || !visited.Add(method))
            return;
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes == null)
            return;
        for (int offset = 0; offset < bytes.Length;)
        {
            short value = bytes[offset++];
            if (value == 0xfe)
                value = unchecked((short)(0xfe00 | bytes[offset++]));
            if (!Opcodes.TryGetValue(value, out var opcode))
                throw new InvalidOperationException($"Unknown IL opcode in {method}");
            int size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
            if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj)
                && opcode.OperandType == OperandType.InlineMethod)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset),
                    method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (called.DeclaringType == typeof(ModelDb))
                {
                    // GetId<T>() computes an ID without reading the registry.
                    // Only lookups that return a model require prior registration.
                    if (called.IsGenericMethod && called is MethodInfo lookup
                        && typeof(AbstractModel).IsAssignableFrom(lookup.ReturnType))
                        foreach (var argument in called.GetGenericArguments())
                            if (!argument.ContainsGenericParameters && !argument.IsAbstract
                                && typeof(AbstractModel).IsAssignableFrom(argument))
                                dependencies.Add(argument);
                }
            }
            offset += size;
        }
    }
}
