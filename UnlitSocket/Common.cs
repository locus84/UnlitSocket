using System;
using System.Runtime.InteropServices;

namespace UnlitSocket
{
    public delegate void ConnectionStatusChangeDelegate(int connectionID);
    public delegate void DataReceivedDelegate(int connectionID, Message message, ref bool autoRecycle);

    public enum ConnectionStatus { Disconnected, Connecting, Connected }
    public enum MessageType { Connected, Disconnected, Data }

    public interface IConnection
    {
        UserToken UserToken { get; set; }
        void OnConnected();
        void OnDisconnected();
        void OnDataReceived(Message msg);
    }

    public static class IConnectionExtension
    {
        public static int GetConnectionID(this IConnection connection) => connection.UserToken.ConnectionID;

        public static void Send(this IConnection connection, Message msg)
        {
            connection.UserToken.Peer.Send(connection.UserToken.ConnectionID, msg);
        }

        public static void Disconnect(this IConnection connection)
        {
            connection.UserToken.Peer.Disconnect(connection.UserToken.ConnectionID);
        }
    }

    public interface ILogReceiver
    {
        void Debug(string str);
        void Exception(Exception exception);
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntFloat
    {
        [FieldOffset(0)]
        public float floatValue;

        [FieldOffset(0)]
        public uint intValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntDouble
    {
        [FieldOffset(0)]
        public double doubleValue;

        [FieldOffset(0)]
        public ulong longValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntDecimal
    {
        [FieldOffset(0)]
        public ulong longValue1;

        [FieldOffset(8)]
        public ulong longValue2;

        [FieldOffset(0)]
        public decimal decimalValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntGuid
    {
        [FieldOffset(0)]
        public ulong longValue1;

        [FieldOffset(8)]
        public ulong longValue2;

        [FieldOffset(0)]
        public Guid guidValue;
    }
}

