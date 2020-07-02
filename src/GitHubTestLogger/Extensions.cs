using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace GitHubTestLogger {
    public static class IDictionaryExtensions {
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this System.Collections.IDictionary dictionary) {
            return dictionary.Keys.Cast<TKey>().ToDictionary(key => key, key => (TValue)dictionary[key]);
        }
    }
}
