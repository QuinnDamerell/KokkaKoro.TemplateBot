using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace KokkaKoroBot
{
    class Logger
    {
        public static void Info(string msg)
        {
            Write($"[INFO] {msg}");
        }

        public static void Error(string msg, Exception e = null)
        {
            Write($"[ERR] {msg} - Message: {(e == null ? "" : e.Message )}");
        }

        private static void Write(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}
