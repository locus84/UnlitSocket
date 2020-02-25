using NUnit.Framework;
using UnlitSocket;

namespace UnlitSocket.Tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void MaxMessageSizeTest()
        {
            var message = Message.Pop();
            message.WriteBytes(new byte[ushort.MaxValue], 0, ushort.MaxValue);
        }

        [Test]
        public void MaxMessageSizeTest1()
        {
            var message = Message.Pop();
            message.WriteBytes(new byte[ushort.MaxValue], 0, ushort.MaxValue);
        }
    }
}