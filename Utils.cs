namespace CardCollectionBuilder
{
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;

    public static class Utils
    {
        public static ILogger Logger = Utils.SetupLogger(Utils.GetAppRelativePath("log.txt"));

        public static ILogger SetupLogger(string logPath)
        {
            return new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(logPath, shared: true, flushToDiskInterval: TimeSpan.FromMinutes(1))
                .MinimumLevel.Debug()
                .CreateLogger();
        }

        private static string basedir = null;

        public static string GetAppDir()
        {
            if (basedir == null)
            {
#if NET5_0
                basedir = AppContext.BaseDirectory;
#else
                basedir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);   // for self-contained exe
#endif
                basedir = Path.GetFullPath(basedir);
            }

            return basedir;
        }

        public static string GetAppRelativePath(string path)
        {
            return Path.Combine(GetAppDir(), path);
        }

        public static string PrepareDirectory(this string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)));
            return filePath;
        }

        public static IEnumerable<T> TruncateTrailings<T>(this IEnumerable<T> source, Func<T, bool> truncateIf)
        {
            return source.Reverse().SkipWhile(truncateIf).Reverse();
        }

        public static IEnumerable<T> PadTo<T>(this IEnumerable<T> source, int length, Func<T> createPad)
        {
            int i = 0;
            foreach (var item in source)
            {
                i++;
                yield return item;
            }
            while (i++ < length)
            {
                yield return createPad();
            }
        }

        public static int IndexOf<T>(this IEnumerable<T> source, Func<T, bool> matcher)
        {
            int idx = 0;
            foreach (var item in source)
            {
                if (matcher(item))
                {
                    return idx;
                }
                idx++;
            }
            return -1;
        }

        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue val)
        {
            key = kvp.Key;
            val = kvp.Value;
        }

        public static bool TryParseColorHexString(string str, out int color)
        {
            color = 0;
            if (string.IsNullOrEmpty(str) || !str.StartsWith('#'))
            {
                return false;
            }
            if (str.StartsWith('#'))
            {
                str = str.Substring(1);
            }
            if (str.Length == 3)
            {
                str = $"{str[0]}{str[0]}{str[1]}{str[1]}{str[2]}{str[2]}";
            }
            else if (str.Length != 6)
            {
                return false;
            }
            return int.TryParse(str, System.Globalization.NumberStyles.HexNumber, null, out color);
        }

        public static string UnescapeTsvCell(string cell)
        {
            return (cell ?? "").Replace("#R#", "\r").Replace("#N#", "\n").Replace("#TAB#", "\t");
        }

        public static string WhiteSpaceOrDefault(this string str, string _default)
        {
            return string.IsNullOrWhiteSpace(str) ? _default : str;
        }

        #region StreamReaderWithLno

        // assume streaming reading from beginning and never seek back
        public class StreamReaderWithLno : IDisposable
        {
            public string CurrentLine { get; private set; }
            public int LineNumber { get; private set; } = 0;

            private bool disposedValue;
            private readonly StreamReader sr;
            private readonly string path;

            public StreamReaderWithLno(string path)
            {
                this.path = path;
                this.sr = new StreamReader(path, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                this.ReadLine();
            }

            public string ReadLine()
            {
                this.CurrentLine = sr.ReadLine();
                this.LineNumber++;
                return this.CurrentLine;
            }

            /// <summary>
            /// Skip white space line
            /// </summary>
            /// <returns>if reached EOF</returns>
            public bool SkipWhiteSpaceLines()
            {
                while (this.CurrentLine != null)
                {
                    if (!string.IsNullOrWhiteSpace(this.CurrentLine))
                    {
                        return true;
                    }
                    this.ReadLine();
                }
                return false;
            }

            public override string ToString()
            {
                var lineString = CurrentLine == null ? "<EOF>" : $"{{{CurrentLine}}}";
                return $"{path}[{LineNumber}] {lineString}";
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        sr?.Dispose();
                    }
                    disposedValue = true;
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        #endregion StreamReaderWithLno
    }
}
