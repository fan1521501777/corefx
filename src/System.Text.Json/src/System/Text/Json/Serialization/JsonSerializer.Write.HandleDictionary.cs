﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace System.Text.Json
{
    public static partial class JsonSerializer
    {
        private static bool HandleDictionary(
            JsonClassInfo elementClassInfo,
            JsonSerializerOptions options,
            Utf8JsonWriter writer,
            ref WriteStack state)
        {
            JsonPropertyInfo jsonPropertyInfo = state.Current.JsonPropertyInfo;
            if (state.Current.Enumerator == null)
            {
                IEnumerable enumerable;

                enumerable = (IEnumerable)jsonPropertyInfo.GetValueAsObject(state.Current.CurrentValue);
                if (enumerable == null)
                {
                    if (!state.Current.JsonPropertyInfo.IgnoreNullValues)
                    {
                        // Write a null object or enumerable.
                        state.Current.WriteObjectOrArrayStart(ClassType.Enumerable, writer, writeNull: true);
                    }

                    return true;
                }

                if (enumerable is IDictionary dictionary)
                {
                    state.Current.Enumerator = dictionary.GetEnumerator();
                }
                else
                {
                    state.Current.Enumerator = enumerable.GetEnumerator();
                }

                state.Current.WriteObjectOrArrayStart(ClassType.Dictionary, writer);
            }

            if (state.Current.Enumerator.MoveNext())
            {
                // Check for polymorphism.
                if (elementClassInfo.ClassType == ClassType.Unknown)
                {
                    object currentValue = ((IDictionaryEnumerator)state.Current.Enumerator).Entry.Value;
                    GetRuntimeClassInfo(currentValue, ref elementClassInfo, options);
                }

                if (elementClassInfo.ClassType == ClassType.Value)
                {
                    elementClassInfo.GetPolicyProperty().WriteDictionary(ref state, writer);
                }
                else if (state.Current.Enumerator.Current == null)
                {
                    writer.WriteNull(jsonPropertyInfo.Name);
                }
                else
                {
                    // An object or another enumerator requires a new stack frame.
                    var enumerator = (IDictionaryEnumerator)state.Current.Enumerator;
                    object value = enumerator.Value;
                    state.Push(elementClassInfo, value);
                    state.Current.KeyName = (string)enumerator.Key;
                }

                return false;
            }

            // We are done enumerating.
            writer.WriteEndObject();

            if (state.Current.PopStackOnEnd)
            {
                state.Pop();
            }
            else
            {
                state.Current.EndDictionary();
            }

            return true;
        }

        internal static void WriteDictionary<TProperty>(
            JsonConverter<TProperty> converter,
            JsonSerializerOptions options,
            ref WriteStackFrame current,
            Utf8JsonWriter writer)
        {
            if (converter == null)
            {
                return;
            }

            Debug.Assert(current.Enumerator != null);

            string key;
            TProperty value;
            if (current.Enumerator is IEnumerator<KeyValuePair<string, TProperty>> enumerator)
            {
                // Avoid boxing for strongly-typed enumerators such as returned from IDictionary<string, TRuntimeProperty>
                value = enumerator.Current.Value;
                key = enumerator.Current.Key;
            }
            else if (current.Enumerator is IEnumerator<KeyValuePair<string, object>> polymorphicEnumerator)
            {
                value = (TProperty)polymorphicEnumerator.Current.Value;
                key = polymorphicEnumerator.Current.Key;
            }
            else if (current.IsIDictionaryConstructible || current.IsIDictionaryConstructibleProperty)
            {
                value = (TProperty)((DictionaryEntry)current.Enumerator.Current).Value;
                key = (string)((DictionaryEntry)current.Enumerator.Current).Key;
            }
            else
            {
                // Todo: support non-generic Dictionary here (IDictionaryEnumerator)
                throw new NotSupportedException();
            }

            if (value == null)
            {
                writer.WriteNull(key);
            }
            else
            {
                JsonEncodedText escapedKey = JsonEncodedText.Encode(key);
                converter.Write(writer, value, escapedKey, options);
            }
        }
    }
}
