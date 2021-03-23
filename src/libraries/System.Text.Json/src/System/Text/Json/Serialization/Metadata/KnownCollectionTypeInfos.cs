// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization.Converters;

namespace System.Text.Json.Serialization.Metadata
{
    /// <summary>
    /// todo
    /// </summary>
    /// <typeparam name="TCollection"></typeparam>
    /// <typeparam name="TElement"></typeparam>
    public static class KnownCollectionTypeInfos<TCollection, TElement>
    {
        private static JsonCollectionTypeInfo<TElement[]>? s_array;
        /// <summary>
        /// todo
        /// </summary>
        public static JsonCollectionTypeInfo<TElement[]> GetArray<TElement>(JsonClassInfo elementInfo, JsonSerializerContext context, JsonNumberHandling? numberHandling)
        {
            if (s_array == null)
            {
                s_array = new JsonCollectionTypeInfo<TElement[]>(CreateList, new ArrayConverter<TElement[], TElement>(), elementInfo, numberHandling, context._options);
            }

            return s_array;
        }

        private static JsonCollectionTypeInfo<List<TElement>>? s_list;
        /// <summary>
        /// todo
        /// </summary>
        public static JsonCollectionTypeInfo<List<TElement>> GetList(JsonClassInfo elementInfo, JsonSerializerContext context, JsonNumberHandling? numberHandling)
        {
            if (s_list == null)
            {
                s_list = new JsonCollectionTypeInfo<List<TElement>>(CreateList, new ListOfTConverter<List<TElement>, TElement>(), elementInfo, numberHandling, context._options);
            }

            return s_list;
        }

        private static JsonCollectionTypeInfo<TCollection>? s_ienumerable;
        /// <summary>
        /// todo
        /// </summary>
        public static JsonCollectionTypeInfo<TCollection> GetIEnumerable(JsonClassInfo elementInfo, JsonSerializerContext context, JsonNumberHandling? numberHandling)
            where TCollection : IEnumerable
        {
            if (s_ienumerable == null)
            {
                s_ienumerable = new JsonCollectionTypeInfo<TCollection>(createObjectFunc: null, new IEnumerableConverter<TCollection>(), elementInfo, numberHandling, context._options);
            }

            return s_ienumerable;
        }

        private static JsonCollectionTypeInfo<IEnumerable<TElement>>? s_genericIEnumerable;
        /// <summary>
        /// todo
        /// </summary>
        public static JsonCollectionTypeInfo<IEnumerable<TElement>> GetGenericIEnumerable(JsonClassInfo elementInfo, JsonSerializerContext context, JsonNumberHandling? numberHandling)
        {
            if (s_genericIEnumerable == null)
            {
                s_genericIEnumerable = new JsonCollectionTypeInfo<IEnumerable<TElement>>(CreateList, new IEnumerableOfTConverter<IEnumerable<TElement>, TElement>(), elementInfo, numberHandling, context._options);
            }

            return s_genericIEnumerable;
        }

        private static JsonCollectionTypeInfo<IList<TElement>>? s_ilist;
        /// <summary>
        /// todo
        /// </summary>
        public static JsonCollectionTypeInfo<IList<TElement>> GetIList(JsonClassInfo elementInfo, JsonSerializerContext context, JsonNumberHandling? numberHandling)
        {
            if (s_ilist == null)
            {
                s_ilist = new JsonCollectionTypeInfo<IList<TElement>>(CreateList, new IListOfTConverter<IList<TElement>, TElement>(), elementInfo, numberHandling, context._options);
            }

            return s_ilist;
        }

        private static List<TElement> CreateList()
        {
            return new List<TElement>();
        }

        // todo: duplicate the above code for each supported collection type (IEnumerable, IEnumerable<T>, array, etc)
    }
}
