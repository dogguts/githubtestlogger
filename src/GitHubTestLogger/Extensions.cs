using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Microsoft.TestPlatform.Extensions.GitHub.TestLogger {
    public static class IDictionaryExtensions {
        /// <summary>Translate IDictionary to generic Dictionary<TKey, TValue>.</summary>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this System.Collections.IDictionary dictionary) {
            return dictionary.Keys.Cast<TKey>().ToDictionary(key => key, key => (TValue)dictionary[key]);
        }

        /// <summary>Gets the value associated with the specified key. If value is not found, returens defaultValue </summary>
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default) {
            return dictionary.TryGetValue(key, out TValue value) ? value : defaultValue;
        }
    }
}
