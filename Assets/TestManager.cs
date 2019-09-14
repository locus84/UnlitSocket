using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnlitSocket;
using System;
using System.Net.Sockets;

public class TestManager : MonoBehaviour
{
    Server m_Server;
    List<Client> m_Clients = new List<Client>();

    List<int> m_ConnectedClients = new List<int>();

    void Start()
    {
        m_Server = new Server(10000);
        m_Server.Init();
        m_Server.SetLogger(new TestLogger());
        m_Server.OnDataReceived += OnServerDataReceived;
        m_Server.OnConnected += id => m_ConnectedClients.Add(id);
        m_Server.OnDisconnected += id => m_ConnectedClients.Remove(id);
        m_Server.Start(54321);

        //for (int i = 0; i < 1000; i++)
        //{
        //    var newClient = new Client(-1, 16);
        //    newClient.SetLogger(new TestLogger());
        //    newClient.OnDataReceived += OnClientDataReceived;
        //    newClient.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 54321));
        //    m_Clients.Add(newClient);
        //}
    }

    private void OnClientDataReceived(int connectionID, Message message)
    {
        Debug.Log("OnClientDataReceived : " + message.ReadString());
    }

    private void OnServerDataReceived(int connectionID, Message message)
    {
        Debug.Log("OnServerDataReceived : " + message.ReadString());
    }

    private void OnDestroy()
    {
        m_Server.Stop();
        foreach (var client in m_Clients) client.Disconnect();
    }

    string defaultVal = "Default";
    private void OnGUI()
    {
        defaultVal = GUILayout.TextField(defaultVal);

        if (GUILayout.Button("ServerSend"))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(defaultVal);
            var message = Message.Pop();

            message.WriteString("하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건aa");
            m_Server.Send(m_ConnectedClients, message);
        }


        if (GUILayout.Button("ClientSend"))
        {
            foreach (var client in m_Clients)
            {
                if(client.Status == ConnectionStatus.Connected)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(defaultVal);
                    var message = Message.Pop();

                    if(UnityEngine.Random.value > 0.5)
                    {
                        message.WriteString("하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.하하 이건 메시지입니다.");
                    }
                    else
                    {
                        message.WriteString("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                    }
                    
                    client.Send(message);
                }
            }
        }

        if(GUILayout.Button("Connect Client"))
        {
            foreach (var client in m_Clients)
            {
                if (client.Status == ConnectionStatus.Disconnected)
                {
                    client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 54321));
                    //client.Connect(new IPEndPoint(UnityEngine.Random.value > 0.5f? IPAddress.Parse("127.0.0.1") : IPAddress.Parse("::1"), 54321));
                }
            }
        }

        if (GUILayout.Button("Connect More"))
        {
            for (int i = 0; i < 1; i++)
            {
                var newClient = new Client(-1, 16);
                newClient.SetLogger(new TestLogger());
                newClient.OnDataReceived += OnClientDataReceived;
                newClient.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 54321));
                //newClient.Connect(new IPEndPoint(UnityEngine.Random.value > 0.5f ? IPAddress.Parse("127.0.0.1") : IPAddress.Parse("::1"), 54321));
                m_Clients.Add(newClient);
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
            m_Server.Start(54321);
        }
        if (m_Server.IsRunning && GUILayout.Button("Stop Server"))
        {
            m_Server.Stop();
        }
    }

    private void Update()
    {
        m_Server.Update();
        for (int i = 0; i < m_Clients.Count; i++)
            m_Clients[i].Update();
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