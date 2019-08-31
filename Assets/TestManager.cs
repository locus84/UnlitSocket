using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using TcpNetworking;
using System;

public class TestManager : MonoBehaviour
{
    Server m_Server;
    List<Client> m_Clients = new List<Client>();
    // Start is called before the first frame update
    void Start()
    {
        m_Server = new Server(5, 16);
        m_Server.Init();
        m_Server.SetLogger(new TestLogger());
        for(int i = 0; i < 10; i++)
        {
            var newClient = new Client(16);
            newClient.SetLogger(new TestLogger());
            m_Clients.Add(newClient);
        }
    }

    private void OnDestroy()
    {
        m_Server.Stop();
        foreach (var client in m_Clients) client.Disconnect();
    }

    public class CustomMessage : IDataProvider
    {
        ArraySegment<byte> m_Bytes;
        public ArraySegment<byte> GetData()
        {
            return m_Bytes;
        }

        public CustomMessage(ArraySegment<byte> data)
        {
            m_Bytes = data;
        }

        public CustomMessage(byte[] data)
        {
            m_Bytes = new ArraySegment<byte>(data);
        }
    }

    string defaultVal = "Default";
    private void OnGUI()
    {
        defaultVal = GUILayout.TextField(defaultVal);
        if(GUILayout.Button("Send"))
        {
            foreach (var client in m_Clients)
            {
                if(client.Status == ConnectionStatus.Connected)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(defaultVal);
                    var message = client.CreateMessage(new CustomMessage(bytes));
                    client.Send(message);
                }
            }
        }

        if(GUILayout.Button("Connect Client"))
        {
            foreach (var client in m_Clients)
            {
                if (client.Status == ConnectionStatus.Disconnected)
                    client.Connect(new IPEndPoint(UnityEngine.Random.value > 0.5f? IPAddress.Parse("127.0.0.1") : IPAddress.Parse("::1"), 3000));
            }
        }

        if (GUILayout.Button("Disconnect Clinet"))
        {
            foreach(var client in m_Clients)
            {
                if(client.Status == ConnectionStatus.Connected)
                    client.Disconnect();
            }
        }

        if (!m_Server.IsRunning && GUILayout.Button("Start Server"))
        {
            m_Server.Start(3000);
        }
        if (m_Server.IsRunning && GUILayout.Button("Stop Server"))
        {
            m_Server.Stop();
        }

        foreach(var client in m_Clients)
        {
            GUILayout.Label(client.Status.ToString());
        }
    }
}


public class TestLogger : ILogReceiver
{
    public void Debug(string str)
    {
        UnityEngine.Debug.Log(str);
    }

    public void Exception(string exception)
    {
        UnityEngine.Debug.Log(exception);
    }
}