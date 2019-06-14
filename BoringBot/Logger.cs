using ServiceSdk;
using System;
using System.Globalization;

namespace KokkaKoroBot
{
    public enum Log
    {
        Info,
        Warn,
        Error,
        Fatial
    }

    class Logger : ILogger
    {
        public static Logger s_instance = new Logger();
        public static Logger Get()
        {
            return s_instance;
        }

        public static void Log(Log type, string msg, Exception e = null)
        {
            Logger l = Get();
            switch (type)
            {
                case KokkaKoroBot.Log.Info:
                    l.Info(msg);
                    break;
                case KokkaKoroBot.Log.Warn:
                    l.Warn(msg);
                    break;
                case KokkaKoroBot.Log.Error:
                    l.Error(msg, e);
                    break;
                case KokkaKoroBot.Log.Fatial:
                    l.Fatial(msg, e);
                    break;
            }
        }

        public void Info(string msg)
        {
            Write($"[INFO] {msg}");
        }

        public void Warn(string msg)
        {
            Write($"[Warn] {msg}");
        }

        public void Error(string msg, Exception e = null)
        {
            Write($"[ERR] {msg}{(e == null ? "" : $" - Message: {e.Message}")}");
        }

        public void Fatial(string msg, Exception e = null)
        {
            Write($"[CRIT] {msg}{(e == null ? "" : $" - Message: {e.Message}")}");
        }

        private static void Write(string msg)
        {
            Console.Out.WriteLine($"[{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {msg}");
        }
    }
}
