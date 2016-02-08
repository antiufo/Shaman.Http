using Shaman.Runtime;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
#if !NET35
using TaskEx = System.Threading.Tasks.Task;
#endif

namespace Shaman
{
    internal static partial class Utils
    {
        
        #if NET35
        public static Task CompletedTask = TaskEx.FromResult(true);
        #endif
        
        internal static void RaiseWebRequestEvent(LazyUri url, bool fromCache)
        {
        }

        internal static Task CheckLocalFileAccessAsync(Uri url)
        {
#if NET35
            return CompletedTask;
#else
            return Task.CompletedTask;
#endif
        }
        internal static Task CheckLocalFileAccessAsync(LazyUri url)
        {
#if NET35
            return CompletedTask;
#else
            return Task.CompletedTask;
#endif
        }


        public static int IndexOf(this StringBuilder sb, char ch)
        {
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == ch) return i;
            }
            return -1;
        }



        public static Task CreateTask(Func<Task> asyncFunc)
        {
            return asyncFunc();
        }
        public static Task<T> CreateTask<T>(Func<Task<T>> asyncFunc)
        {
            return asyncFunc();
        }
#if NET35

        public static IDictionary<string, string> ToDictionary(this IEnumerable<KeyValuePair<string, string>> values)
        {
            return ToMutableDictionary(values);
        }
#else
        public static IReadOnlyDictionary<string, string> ToDictionary(this IEnumerable<KeyValuePair<string, string>> values)
        {
            return (IReadOnlyDictionary<string, string>)ToMutableDictionary(values);
        }
#endif


        public static IDictionary<string, string> ToMutableDictionary(this IEnumerable<KeyValuePair<string, string>> values)
        {
            var dictionary = new Dictionary<string, string>();
            foreach (var item in values)
            {
                dictionary[item.Key] = item.Value;
            }
            return dictionary;
        }

        private static int _mainThread = -1;
        public static void AssertMainThread()
        {
#if NET35
            var th = System.Threading.Thread.CurrentThread.ManagedThreadId;
#else
            var th = Environment.CurrentManagedThreadId;
#endif
            if (_mainThread == -1) _mainThread = th;
            else if (_mainThread != th) throw new InvalidOperationException("This feature must be consistently used from the same thread. Another thread was previously used.");

        }
        public static void FireAndForget(this Task task)
        {
            task.GetAwaiter();
        }

        public static void AssumeCompleted(this Task task)
        {
            if (task.Exception != null) throw task.Exception;
            if (task.IsCanceled) throw new TaskCanceledException(task);
            if (task.Status != TaskStatus.RanToCompletion) throw new InvalidOperationException();
        }

        public static T AssumeCompleted<T>(this Task<T> task)
        {
            if (task.Exception != null) throw task.Exception;
            if (task.IsCanceled) throw new TaskCanceledException(task);
            if (task.Status != TaskStatus.RanToCompletion) throw new InvalidOperationException();
            return task.Result;
        }

    }
#if !NET35
    namespace Runtime
    {
        internal static class ReadonlyDictExtensions
        {
            public static TValue TryGetValue<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key) where TValue : class
            {

                TValue value;
                if (dict.TryGetValue(key, out value)) return value;
                else return null;
            }
        }
    }
#endif
}