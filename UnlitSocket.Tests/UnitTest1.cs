using NUnit.Framework;
using UnlitSocket;

namespace UnlitSocket.Tests
{
    public class Tests
    {
        const int Port = 9999;
        Server server;

        [SetUp]
        public void Setup()
        {
            server = new Server(1000);
            server.Start(Port);
        }

        [TearDown]
        public void TearDown()
        {
            server.Stop();
        }

        [Test]
        public void MaxMessageSizeTest()
        {
            var message = Message.Pop();
            message.WriteBytes(new byte[ushort.MaxValue], 0, ushort.MaxValue);
        }

        [Test]
        public void ServerStartAndStop()
        {
            var server = new Server(20000);
            server.Start(Port + 1);
            System.Threading.Thread.Sleep(100);
            server.Stop();
        }
    }
}