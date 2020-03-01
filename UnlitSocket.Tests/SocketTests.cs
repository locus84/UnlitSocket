using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;

namespace UnlitSocket.Tests
{
    public class Tests
    {
        const int Port = 9999;
        Server server;

        [SetUp]
        public void Setup()
        {
            server = new Server();
            server.SetLogger(new TestLogger());
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
            var server = new Server();
            server.Start(Port + 1);
            server.Stop();
        }

        [Test]
        public void DisconnectImmediateTest()
        {
            Client client = new Client();
            client.SetLogger(new TestLogger());
            client.Connect( "localhost", Port);

            // I should be able to disconnect right away
            // if connection was pending,  it should just cancel
            client.Disconnect();

            Assert.IsTrue(client.Status == ConnectionStatus.Disconnected);

            //try connect with wrong server ip
            client.Connect("192.168.0.0", Port);
            client.Disconnect();

            Assert.IsTrue(client.Status == ConnectionStatus.Disconnected);
        }

        [Test]
        public void TriggerSendFail()
        {
            Client client = new Client();
            client.SetLogger(new TestLogger());
            client.Connect("127.0.0.1", Port);

            while (client.Status != ConnectionStatus.Connected) Thread.Sleep(10);

            for(int i = 0; i < 10; i++)
            {
                var message = Message.Pop();
                message.WriteBytes(new byte[ushort.MaxValue], 0, ushort.MaxValue);
                client.Send(message);
            }

            server.Disconnect(1);
        }

        [Test]
        public void GUIDConversionTest()
        {
            for(int i = 0; i < 5; i++)
            {
                var msg = Message.Pop();
                var guid = System.Guid.NewGuid();
                msg.WriteGuid(guid);
                msg.Position = 0;
                System.Console.WriteLine(guid);
                Assert.IsTrue(guid == msg.ReadGuid());
            }
        }

        [Test]
        public void MultipleClientHangTest()
        {
            var testCount = 30;
            var clientList = new List<Client>();

            for (int i = 0; i < testCount; i++)
            {
                var client = new Client();
                client.Connect("localhost", Port);
                clientList.Add(client);
            }

            foreach (var client in clientList)
            {
                Assert.IsTrue(client.Status != ConnectionStatus.Disconnected);
                client.Disconnect();
            }

            for (int i = 0; i < testCount; i++)
            {
                var client = new Client();
                client.Connect("localhost", Port);
            }

            Thread.Sleep(100);
        }

        [Test]
        public void CountEventTest()
        {
            var e = new CountdownEvent(0);
            e.Wait();
        }
    }
}