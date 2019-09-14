using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class TestTcpServer : MonoBehaviour
{
    TcpListener m_Server;
    List<Socket> m_ServerSideClientSockets = new List<Socket>();
    List<TcpClient> m_ClientSockets = new List<TcpClient>();

    // Start is called before the first frame update
    void Start()
    {
        m_Server = new TcpListener(IPAddress.Any, 3000);
        m_Server.Start(1000);

        System.Threading.Tasks.Task.Run(() =>
        {
            while (true) m_ServerSideClientSockets.Add(m_Server.AcceptSocket());
        });
    }

    private void OnGUI()
    {
        GUILayout.Label(m_ServerSideClientSockets.Count.ToString());
        if(GUILayout.Button("AddClients"))
        {
            for (int i = 0; i < 100; i++)
            {
                var newSocket = new TcpClient();
                newSocket.Connect(IPAddress.Parse("127.0.0.1"), 3000);
            }
        }
    }
}
