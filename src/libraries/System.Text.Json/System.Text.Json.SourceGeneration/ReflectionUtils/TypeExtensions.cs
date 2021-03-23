// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace System
{
    internal static partial class TypeExtensions
    {
        private static Type s_nullableOfTType;

        private static Type s_immutableDictionaryType;
        private static Type s_iimmutableDictionaryType;
        private static Type s_immutableSortedDictionaryType;

        private static Type s_immutableArrayType;
        private static Type s_immutableListType;
        private static Type s_iimmutableListType;
        private static Type s_immutableStackType;
        private static Type s_iimmutableStackType;
        private static Type s_immutableQueueType;
        private static Type s_iimmutableQueueType;
        private static Type s_immutableSortedSetType;
        private static Type s_immutableHashSetType;
        private static Type s_iimmutableSetType;

        public static void InitializeTypes(MetadataLoadContext metadataLoadContext)
        {
            s_nullableOfTType = metadataLoadContext.Resolve(typeof(Nullable<>));
            s_immutableDictionaryType = metadataLoadContext.Resolve(typeof(ImmutableDictionary<,>));
            s_iimmutableDictionaryType = metadataLoadContext.Resolve(typeof(IImmutableDictionary<,>));
            s_immutableSortedDictionaryType = metadataLoadContext.Resolve(typeof(ImmutableSortedDictionary<,>));
            s_immutableArrayType = metadataLoadContext.Resolve(typeof(ImmutableArray<>));
            s_immutableListType = metadataLoadContext.Resolve(typeof(ImmutableList<>));
            s_iimmutableListType = metadataLoadContext.Resolve(typeof(IImmutableList<>));
            s_immutableStackType = metadataLoadContext.Resolve(typeof(ImmutableStack<>));
            s_iimmutableStackType = metadataLoadContext.Resolve(typeof(IImmutableStack<>));
            s_immutableQueueType = metadataLoadContext.Resolve(typeof(ImmutableQueue<>));
            s_iimmutableQueueType = metadataLoadContext.Resolve(typeof(IImmutableQueue<>));
            s_immutableSortedSetType = metadataLoadContext.Resolve(typeof(ImmutableSortedSet<>));
            s_immutableHashSetType = metadataLoadContext.Resolve(typeof(ImmutableHashSet<>));
            s_iimmutableSetType = metadataLoadContext.Resolve(typeof(IImmutableSet<>));
        }

        public static string GetUniqueCompilableTypeName(this Type type) => GetCompilableTypeName(type, type.FullName);

        public static string GetCompilableTypeName(this Type type) => GetCompilableTypeName(type, type.Name);

        private static string GetCompilableTypeName(Type type, string name)
        {
            if (!type.IsGenericType)
            {
                return name.Replace('+', '.');
            }

            // TODO: Guard upstream against open generics.
            Debug.Assert(!type.ContainsGenericParameters);

            int backTickIndex = name.IndexOf('`');
            string baseName = name.Substring(0, backTickIndex).Replace('+', '.');

            return $"{baseName}<{string.Join(",", type.GetGenericArguments().Select(arg => GetUniqueCompilableTypeName(arg)))}>";
        }

        public static string GetUniqueFriendlyTypeName(this Type type)
        {
            return GetFriendlyTypeName(type.GetUniqueCompilableTypeName());
        }

        public static string GetFriendlyTypeName(this Type type)
        {
            return GetFriendlyTypeName(type.GetCompilableTypeName());
        }

        private static string GetFriendlyTypeName(string compilableName)
        {
            return compilableName.Replace(".", "").Replace("<", "").Replace(">", "").Replace(",", "").Replace("[]", "Array");
        }

        public static bool IsNullableValueType(this Type type, out Type? underlyingType)
        {
            Debug.Assert(s_nullableOfTType != null);

            // TODO: log bug because Nullable.GetUnderlyingType doesn't work due to
            // https://github.com/dotnet/runtimelab/blob/7472c863db6ec5ddab7f411ddb134a6e9f3c105f/src/libraries/System.Private.CoreLib/src/System/Nullable.cs#L124
            // i.e. type.GetGenericTypeDefinition() will never equal typeof(Nullable<>), as expected in that code segment.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == s_nullableOfTType)
            {
                underlyingType = type.GetGenericArguments()[0];
                return true;
            }

            underlyingType = null;
            return false;
        }

        public static bool IsImmutableDictionaryType(this Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            Type genericType = type.GetGenericTypeDefinition();
            return genericType == s_immutableDictionaryType || genericType == s_iimmutableDictionaryType || genericType == s_immutableSortedDictionaryType;
        }

        public static bool IsImmutableEnumerableType(this Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            Type genericType = type.GetGenericTypeDefinition();
            return genericType == s_immutableArrayType ||
                genericType == s_immutableListType ||
                genericType == s_iimmutableListType ||
                genericType == s_immutableStackType ||
                genericType == s_iimmutableStackType ||
                genericType == s_immutableQueueType ||
                genericType == s_iimmutableQueueType ||
                genericType == s_immutableSortedSetType ||
                genericType == s_immutableHashSetType ||
                genericType == s_iimmutableSetType;
        }
    }
}
