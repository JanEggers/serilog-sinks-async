using System;
using System.IO;
using System.Text;

namespace Serilog.Sinks.Async.PerformanceTests.Support
{
    public class TerminatingTextWriter : TextWriter
    {
        public override Encoding Encoding { get; } = Encoding.ASCII;

        //public override void Write(char value)
        //{
        //    Console.WriteLine("SelfLog triggered");
        //    Environment.Exit(1);
        //}

        public override void WriteLine(string format, object arg0)
        {
            Console.WriteLine("SelfLog triggered" + string.Format(format, arg0));
            Environment.Exit(1);
        }

        public override void WriteLine(string format, object arg0, object arg1)
        {
            Console.WriteLine("SelfLog triggered" + string.Format(format, arg0, arg1));
            Environment.Exit(1);
        }

        public override void WriteLine(string format, object arg0, object arg1, object arg2)
        {
            Console.WriteLine("SelfLog triggered" + string.Format(format, arg0, arg1, arg2));
            Environment.Exit(1);
        }

        public override void WriteLine(string format, params object[] arg)
        {
            Console.WriteLine("SelfLog triggered" + string.Format(format, arg));
            Environment.Exit(1);
        }
    }
}