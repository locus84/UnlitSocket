using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace UnlitSocket
{
    public class SendEventArgs : SocketAsyncEventArgs
    {
        public Message Message;
        public Connection Connection;
    }

    public class ReceiveEventArgs : SocketAsyncEventArgs
    {
        public Connection Connection;
    }
}
