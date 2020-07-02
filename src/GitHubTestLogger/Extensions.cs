using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace GitHubTestLogger {
    public static class IDictionaryExtensions {
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this System.Collections.IDictionary dictionary) {
            return dictionary.Keys.Cast<TKey>().ToDictionary(key => key, key => (TValue)dictionary[key]);
        }

        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue)) {
            TValue value;
            return dictionary.TryGetValue(key, out value) ? value : defaultValue;
        }

        // public static void AppendFormatLine(this StringBuilder stringbuilder, IFormatProvider provider, string format, params object[] args) {
        //     stringbuilder.AppendLine(string.Format(provider, format, args));
        // }
        // public static void AppendFormatLine(this StringBuilder stringbuilder, string format, params object[] args) {
        //     stringbuilder.AppendLine(string.Format(format, args));
        // }
    }
}
