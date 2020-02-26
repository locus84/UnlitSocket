using System;
using System.Collections.Generic;
using System.Text;

namespace UnlitSocket.Tests
{
    public class TestLogger : ILogReceiver
    {
        public void Debug(string msg)
        {
            Console.WriteLine(msg);
        }

        public void Exception(Exception exception)
        {
            Console.WriteLine(exception.ToString());
        }

        public void Warning(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}
