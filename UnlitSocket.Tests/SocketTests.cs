using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket.Tests
{
    public class Tests
    {
        const string Host = "localhost";
        const int Port = 9999;
        Server Server;

        [SetUp]
        public void Setup()
        {
            Server = new Server();
            Server.SetLogger(new TestLogger());
            Server.Start(Port);
        }

        [TearDown]
        public void TearDown()
        {
            Server.Stop();
        }

        [Test]
        public void MaxMessageSizeTest()
        {
            var msg = Message.Pop();
            msg.WriteBytes(new byte[ushort.MaxValue], 0, ushort.MaxValue);
        }

        [Test]
        public void ServerStartAndStop()
        {
            var server = new Server();
            server.Start(Port + 1);
            server.Stop();
            server.Start(Port + 1);
            server.Stop();
        }

        [Test]
        public void DisconnectImmediateTest()
        {
            Client client = new Client();
            client.SetLogger(new TestLogger());
            client.Connect(Host, Port);

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
            client.Connect(Host, Port).Wait();

            for (int i = 0; i < 10; i++)
            {
                var msg = Message.Pop();
                msg.WriteBytes(new byte[ushort.MaxValue], 0, ushort.MaxValue);
                client.Send(msg);
            }

            Server.Disconnect(1);
        }

        [Test]
        public void GUIDConversionTest()
        {
            for (int i = 0; i < 5; i++)
            {
                var msg = Message.Pop();
                var guid = Guid.NewGuid();
                msg.WriteGuid(guid);
                msg.Position = 0;
                Console.WriteLine(guid);
                Assert.IsTrue(guid == msg.ReadGuid());
            }
        }

        [Test]
        public void MultipleClientHangTest()
        {
            var testCount = 10;
            var clientList = new List<Client>();

            for (int i = 0; i < testCount; i++)
            {
                var client = new Client();
                client.Connect(Host, Port).Wait();
                Assert.IsTrue(client.Status == ConnectionStatus.Connected);
                clientList.Add(client);
            }

            Assert.IsTrue(Server.ConnectionCount == testCount);

            foreach (var client in clientList)
            {
                Assert.IsTrue(client.Status != ConnectionStatus.Disconnected);
                client.Disconnect();
            }

            clientList.Clear();

            for (int i = 0; i < testCount; i++)
            {
                var client = new Client();
                client.Connect(Host, Port).Wait();
                Assert.IsTrue(client.Status == ConnectionStatus.Connected);
                clientList.Add(client);
            }

            Assert.IsTrue(Server.ConnectionCount == testCount);

            foreach (var client in clientList)
            {
                Assert.IsTrue(client.Status != ConnectionStatus.Disconnected);
                client.Disconnect();
            }

            clientList.Clear();
        }

        [Test]
        public void TryAcceptAsyncWithClosedSocket()
        {
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            socket.DualMode = true;
            socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Any, Port + 1));
            socket.Listen(100);

            var resetEvent = new AutoResetEvent(false);
            var socketArg = new SocketAsyncEventArgs();
            socketArg.Completed += (sender, args) => resetEvent.Set();
            socket.AcceptAsync(socketArg);
            var client = new Client();
            client.Connect("localhost", Port + 1);
            resetEvent.WaitOne();
            Assert.IsTrue(socketArg.SocketError == SocketError.Success);
            socketArg.AcceptSocket = null;
            resetEvent.Reset();
            socket.Close();
            try
            {
                socket.AcceptAsync(socketArg);
                Assert.Fail("Exception should be occurred");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        [Test]
        public void TryAcceptAsyncWithShutdownSocket()
        {
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            socket.DualMode = true;
            socket.Connect(Host, Port);
            socket.Shutdown(SocketShutdown.Both);
            var args = new SocketAsyncEventArgs();
            args.SetBuffer(new byte[1], 0 ,1);
            socket.ReceiveAsync(args);
            Assert.IsTrue(args.SocketError == SocketError.Shutdown);
            socket.Close();
        }
        [Test]
        public void TestCountEventTest()
        {
            var e = new CountdownEvent(0);
            e.Reset(2);
            e.AddCount(1);
            e.AddCount(1);
        }

        [Test]
        public void TestCountLock()
        {
            var countLock = new CountLock();
            countLock.Reset(2);
            Assert.IsTrue(countLock.TryRetain(2));
            Assert.IsFalse(countLock.TryRetain(2));
        }
    }
}