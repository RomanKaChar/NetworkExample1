using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Collections.Generic;
using UnityEngine;

namespace LTTDIT.Net
{
    public class NetScript1 : MonoBehaviour
    {
        private delegate void Act();
        private Act act;
        private Act availableCheck;
        private Act sendMessageAsManually;
        private Act requestInfo;
        public delegate void CreateButtonDelegate(string ip, string nick, Information.Applications app);
        public delegate void SetDelegateToButton(string ip, string nick, Information.Applications app,
            CreateButtonDelegate createButtonDelegate);
        private SetDelegateToButton setCreatedButton;
        public delegate void DoSomethingWithStringDelegate(string message);
        private DoSomethingWithStringDelegate createChatMessage;
        private DoSomethingWithStringDelegate showSecondPlayerNicknameAndSetTurn;
        public delegate string GetSomeStringDelegate();
        private GetSomeStringDelegate getManuallySendableMessage;
        private GetSomeStringDelegate messageToUdpBroadcast;
        public delegate void EnemyMadeMoveDelegate(int pos_x, int pos_y);
        private EnemyMadeMoveDelegate enemyMadeMove;
        public delegate void DoSomethingWithTwoStringDelegate(string string1, string string2);
        private DoSomethingWithTwoStringDelegate invitationReceived;

        private TcpListener tcpListener;
        private List<ClientObject> clientObjects = new List<ClientObject>();
        private int maxClientsNumber = 1;
        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private const float availableCooldown = 1f;
        private float availableTime = 0f;

        private readonly IPAddress BroadcastIpAddress = IPAddress.Parse("255.255.255.255");
        private const int BroadcastUDPport = 55558;
        private const int ListenerTCPport = 55559;
        private Dictionary<ClientObject, int> invitedClientsPorts = new Dictionary<ClientObject, int>();

        private string receivedUdpData = string.Empty;
        private float broadcastUdpTime = 0f;
        public const float BroadcastUdpCooldown = 1f;
        private float requestTime = 0f;
        private const float RequestCooldown = BroadcastUdpCooldown;
        private UdpClient udpClient;
        private System.Threading.Thread udpReceiveThread;
        private Information.Applications myApplication = Information.Applications.ApplicationError;
        private Role myRole = Role.RoleError;
        private bool isJoinButtonWillBeCreatedSoon = false;
        private bool isInviteButtonWillBeCreatedSoon = false;

        private Information.Applications receivedApplication = Information.Applications.ApplicationError;
        private string receivedInvitationNickname = string.Empty;
        private string acceptedClientId = string.Empty;
        private Information.Applications receivedInvitationApplication = Information.Applications.ApplicationError;

        private int receivedPosX = 0;
        private int receivedPosY = 0;
        private float currentTimeToShowEnemyMove = 0f;
        private const float TimeToShowEnemyMove = 0.3f;

        public static NetScript1 instance;
        public List<Information.Data> datas = new List<Information.Data>() { new Information.Data(Information.TransceiverData.MyNickname, "myNickname"), };

        private const int OpeningSceneId = 0;
        private const int ChatSceneId = 1;
        private const int TicTacToeSceneId = 2;
        private int currentSceneId = 0;

        private bool IsDatasContains(Information.TransceiverData transceiverData)
        {
            foreach (Information.Data data in datas)
            {
                if (data.Is(transceiverData)) return true;
            }
            return false;
        }

        private void AddOrUpdateData(Information.Data data)
        {
            if (IsDatasContains(data.GetTransceiverData()))
            {
                Information.Data dataDel = null;
                foreach (Information.Data dataToDel in datas)
                {
                    if (dataToDel.Is(data.GetTransceiverData()))
                    {
                        dataDel = dataToDel;
                        break;
                    }
                }
                RemoveData(dataDel);
            }
            datas.Add(data);
        }

        private void RemoveData(Information.Data data)
        {
            datas.Remove(data);
        }

        private void ClearDatas()
        {
            List<Information.Data> delDatas = new List<Information.Data>();
            foreach (Information.Data data in datas)
            {
                if (!data.Is(Information.TransceiverData.MyNickname))
                {
                    delDatas.Add(data);
                }
            }
            foreach (Information.Data data1 in delDatas)
            {
                RemoveData(data1);
            }
            delDatas.Clear();
        }

        public object GetDataByTransceiverData(Information.TransceiverData transceiverData)
        {
            Information.Data retData = null;
            foreach (Information.Data data in datas)
            {
                if (data.Is(transceiverData))
                {
                    retData = data;
                    break;
                }
            }
            return retData.GetData();
        }

        public enum Role
        {
            Host,
            Client,
            RoleError,
        }

        private Role GetRole()
        {
            return myRole;
        }

        public bool IsHost()
        {
            return GetRole() == Role.Host;
        }

        public bool IsClient()
        {
            return GetRole() == Role.Client;
        }

        public bool SetNickname(string nick_name)
        {
            if (nick_name.Length < 3) return false;
            AddOrUpdateData(new Information.Data(Information.TransceiverData.MyNickname, nick_name));
            return true;
        }

        public bool HasNickname()
        {
            return GetNickname().Length >= 3;
        }

        public string GetNickname()
        {
            return (string)GetDataByTransceiverData(Information.TransceiverData.MyNickname);
        }

        private void JoinButtonPressed(string ip, string nick, Information.Applications app)
        {
            StopClientListen();
            myRole = Role.Client;
            myApplication = app;
            StopUdpProcess();
            StartTcpClientProcess(ip);
            if (app == Information.Applications.Chat)
            {
                LoadChatScene();
                sendMessageAsManually = SendMessageAsClientManually;
            }
            else if (app == Information.Applications.TicTacToe)
            {
                requestInfo = SendRequestTicTacToeInfo;
                SetAct(SendRequestInfoAuto);
            }
        }

        private void InviteButtonPressed(string ip, string nick, Information.Applications app)
        {
            try
            {
                int port = GetFreePort();
                tcpClient = new TcpClient(new IPEndPoint(IPAddress.Any, port));
                tcpClient.Connect(IPAddress.Parse(ip), ListenerTCPport);
                networkStream = tcpClient.GetStream();
                ClientObject clientObjectl = new ClientObject(tcpClient, nick);
                AddTcpConnection(clientObjectl, port);
                string helloMessage = string.Format("\"{0}\" invited!", clientObjectl.userName);
                ShowInfo(helloMessage);
                SendBroadcastTcpMessage(helloMessage, clientObjectl.UID);
                SendTcpMessageAsClient(Information.SetInvitationCommand(GetNickname(), myApplication));
                if (ShouldTheRoomBeClosed()) CloseRoomByHost();
            }
            catch (Exception ex)
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }
                ShowInfo("InviteButtonPressed - " + ex.Message);
            }
        }

        private int GetFreePort()
        {
            int port = ListenerTCPport + 1;
            while (!IsPortFree(port))
            {
                port++;
            }
            return port;
        }

        private bool IsPortFree(int port)
        {
            foreach (int p in invitedClientsPorts.Values)
            {
                if (port == p) return false;
            }
            return true;
        }

        public void AcceptInvitation()
        {
            SendTcpMessageAsClient(Information.SetInvitationAcceptedCommand(GetNickname()));
            myApplication = receivedInvitationApplication;
            if (myApplication == Information.Applications.Chat)
            {
                LoadChatScene();
                sendMessageAsManually = SendMessageAsClientManually;
            }
            else if (myApplication == Information.Applications.TicTacToe)
            {
                requestInfo = SendRequestTicTacToeInfo;
                SetAct(SendRequestInfoAuto);
            }
        }

        private void SendRequestTicTacToeInfo()
        {
            Information.TransceiverData[] requestData = new Information.TransceiverData[]
            {
                Information.TransceiverData.OtherNickname,
                Information.TransceiverData.TicTacToeBoardSize,
                Information.TransceiverData.TicTacToeWinSize,
            };
            SendTcpMessageAsClient(Information.SetRequestCommand(requestData));
        }

        private void SendRequestOtherNicknameAsHost()
        {
            Information.TransceiverData[] requestData = new Information.TransceiverData[]
            {
                Information.TransceiverData.OtherNickname,
            };
            SendUniastTcpMessage(Information.SetRequestCommand(requestData), acceptedClientId);
        }

        private void DoSomethingWhenAcceptClient(string clientId)
        {
            acceptedClientId = clientId;
            if (IsHost() && (myApplication == Information.Applications.TicTacToe))
            {
                requestInfo = SendRequestOtherNicknameAsHost;
                SetAct(SendRequestInfoAuto);
            }
        }

        public void RefuseInvitation()
        {
            SendTcpMessageAsClient(Information.SetInvitationRefusedCommand(GetNickname()));
            DisconnectTcpClient();
            JoinRoomPressed();
        }

        public void JoinRoomPressed()
        {
            GetMyIp();
            messageToUdpBroadcast = GetInvitationInformation;
            myRole = Role.Client;
            StartUdpProcess();
            StartClientListen();
        }

        public void ChatSelected()
        {
            myRole = Role.Host;
            myApplication = Information.Applications.Chat;
            maxClientsNumber = 10;
            GetMyIp();
            LoadChatScene();
            messageToUdpBroadcast = GetCreatedRoomInformation;
            StartUdpProcess();
            StartTcpHostProcess();
            sendMessageAsManually = SendMessageAsHostManually;
        }

        public void TicTacToeSelected(int boardSize, int winSize, TicTacToe.TicTacToeSettings.TicTacToeEnemies enemy)
        {
            myApplication = Information.Applications.TicTacToe;
            AddOrUpdateData(new Information.Data(Information.TransceiverData.TicTacToeBoardSize, boardSize));
            AddOrUpdateData(new Information.Data(Information.TransceiverData.TicTacToeWinSize, winSize));
            if (enemy.Equals(TicTacToe.TicTacToeSettings.TicTacToeEnemies.LocalNetwork))
            {
                myRole = Role.Host;
                maxClientsNumber = 1;
                GetMyIp();
                LoadTicTacToeScene();
                messageToUdpBroadcast = GetCreatedRoomInformation;
                StartUdpProcess();
                StartTcpHostProcess();
            }
        }

        private void ChangeScene(int sceneId)
        {
            if (currentSceneId != sceneId) UnityEngine.SceneManagement.SceneManager.LoadScene(sceneId);
            currentSceneId = sceneId;
        }

        private void LoadChatScene()
        {
            ChangeScene(ChatSceneId);
        }

        private void LoadTicTacToeScene()
        {
            ChangeScene(TicTacToeSceneId);
        }

        private void LoadOpeningScene()
        {
            ChangeScene(OpeningSceneId);
        }

        private void AddTcpConnection(ClientObject clientObject)
        {
            clientObjects.Add(clientObject);
        }

        private void AddTcpConnection(ClientObject clientObject, int host_port)
        {
            AddTcpConnection(clientObject);
            invitedClientsPorts.Add(clientObject, host_port);
        }

        private void RemoveTcpConnection(ClientObject clientObject)
        {
            if (clientObject != null)
            {
                if (clientObjects.Contains(clientObject)) clientObjects.Remove(clientObject);
                if (invitedClientsPorts.ContainsKey(clientObject)) invitedClientsPorts.Remove(clientObject);
            }
        }

        private void AvailableCheckAuto()
        {
            availableCheck?.Invoke();
        }

        private void StartAvailableCheck()
        {
            SetAct(AvailableCheckAuto);
        }

        private void StopAvailableCheck()
        {
            RemAct(AvailableCheckAuto);
            availableCheck = null;
        }

        private void AvailableCheckAsHost()
        {
            availableTime += Time.deltaTime;
            if (availableTime >= availableCooldown)
            {
                SendBroadcastTcpMessage(Information.GetAvailableCommand(), string.Empty);
            }
        }

        private void AvailableCheckAsClient()
        {
            availableTime += Time.deltaTime;
            if (availableTime >= availableCooldown)
            {
                SendTcpMessageAsClient(Information.GetAvailableCommand());
            }
        }

        private void SendBroadcastTcpMessage(string message, string id)
        {
            availableTime = 0f;
            ClientObject clientObj = null;
            try
            {
                byte[] data = GetByteArrayFromString(message);
                for (int i = 0; i < clientObjects.Count; i++)
                {
                    clientObj = clientObjects[i];
                    if (clientObjects[i].UID != id) clientObjects[i].UserStream.Write(data, 0, data.Length);
                }
            }
            catch (System.IO.IOException)
            {
                RemoveTcpConnection(clientObj);
                clientObj.Close();
                string messagex = string.Format("\"{0}\" left the room!", clientObj.userName);
                ShowInfo(messagex);
                SendBroadcastTcpMessage(messagex, clientObj.UID);
            }
            catch (Exception ex)
            {
                DisconnectTcpHost();
                ShowInfo("SendBroadcastTcpMessage - " + ex.Message);
            }
        }

        private void SendUniastTcpMessage(string message, string id)
        {
            availableTime = 0f;
            ClientObject clientObj = null;
            try
            {
                byte[] data = GetByteArrayFromString(message);
                for (int i = 0; i < clientObjects.Count; i++)
                {
                    clientObj = clientObjects[i];
                    if (clientObjects[i].UID == id) clientObjects[i].UserStream.Write(data, 0, data.Length);
                }
            }
            catch (System.IO.IOException)
            {
                RemoveTcpConnection(clientObj);
                clientObj.Close();
                string messagex = string.Format("\"{0}\" left the room!", clientObj.userName);
                ShowInfo(messagex);
                SendBroadcastTcpMessage(messagex, clientObj.UID);
            }
            catch (Exception ex)
            {
                DisconnectTcpHost();
                ShowInfo("SendBroadcastTcpMessage - " + ex.Message);
            }
        }

        private void BroadcastTcpResendAuto()
        {
            try
            {
                for (int i = 0; i < clientObjects.Count; i++)
                {
                    if (clientObjects[i].HasData())
                    {
                        string message = clientObjects[i].GetMessage();
                        ProcessTcpCommand(message, clientObjects[i].UID);
                    }
                }
            }
            catch (Exception ex)
            {
                DisconnectTcpHost();
                ShowInfo("BroadcastTcpResendAuto - " + ex.Message);
            }
        }

        private void ListenTcpAuto()
        {
            try
            {
                if (tcpListener.Pending())
                {
                    TcpClient tcpClientl = tcpListener.AcceptTcpClient();
                    ClientObject clientObjectl = new ClientObject(tcpClientl);
                    AddTcpConnection(clientObjectl);
                    string helloMessage = string.Format("\"{0}\" joined!", clientObjectl.userName);
                    ShowInfo(helloMessage);
                    SendBroadcastTcpMessage(helloMessage, clientObjectl.UID);
                    DoSomethingWhenAcceptClient(clientObjectl.UID);
                    if (ShouldTheRoomBeClosed()) CloseRoomByHost();
                }
            }
            catch (Exception ex)
            {
                DisconnectTcpHost();
                ShowInfo("ListenTcpAuto - " + ex.Message);
            }
        }

        private void StartTcpHostProcess()
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, ListenerTCPport);
                tcpListener.Start();
                availableCheck = AvailableCheckAsHost;
                StartAvailableCheck();
                SetAct(ListenTcpAuto);
                SetAct(BroadcastTcpResendAuto);
            }
            catch (Exception ex)
            {
                DisconnectTcpHost();
                ShowInfo("StartTcpHostProcess - " + ex.Message);
            }
        }

        private bool ShouldTheRoomBeClosed()
        {
            return clientObjects.Count >= maxClientsNumber;
        }

        private void CloseRoomByHost()
        {
            StopUdpProcess();
            RemAct(ListenTcpAuto);
            if (tcpListener != null)
            {
                tcpListener.Stop();
                tcpListener = null;
            }
            ShowInfo("Room closed!");
        }

        private void DisconnectTcpHost()
        {
            StopAvailableCheck();
            RemAct(SendRequestInfoAuto);
            RemAct(ListenTcpAuto);
            RemAct(BroadcastTcpResendAuto);
            createChatMessage = null;
            sendMessageAsManually = null;
            enemyMadeMove = null;
            if (tcpListener != null)
            {
                tcpListener.Stop();
                tcpListener = null;
            }
            for (int i = 0; i < clientObjects.Count; i++)
            {
                clientObjects[i].Close();
            }
            clientObjects.Clear();
        }

        private void StartClientListen()
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, ListenerTCPport);
                tcpListener.Start();
                SetAct(ListenAsClientAuto);
            }
            catch (Exception ex)
            {
                StopClientListen();
                ShowInfo("StartClientListen - " + ex.Message);
            }
        }

        private void ListenAsClientAuto()
        {
            try
            {
                if (tcpListener.Pending())
                {
                    StopUdpProcess();
                    tcpClient = tcpListener.AcceptTcpClient();
                    StopClientListen();
                    networkStream = tcpClient.GetStream();
                    availableCheck = AvailableCheckAsClient;
                    StartAvailableCheck();
                    SetAct(ReceiveTcpMessageAuto);
                }
            }
            catch (Exception ex)
            {
                StopClientListen();
                ShowInfo("ListenAsClientAuto - " + ex.Message);
            }
        }

        private void StopClientListen()
        {
            RemAct(ListenAsClientAuto);
            if (tcpListener != null)
            {
                tcpListener.Stop();
                tcpListener = null;
            }
        }

        private void ReceiveTcpMessageAuto()
        {
            try
            {
                if (networkStream.DataAvailable)
                {
                    byte[] data = new byte[256];
                    StringBuilder builder = new StringBuilder();
                    int bytes = 0;
                    do
                    {
                        bytes = networkStream.Read(data, 0, data.Length);
                        builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                    }
                    while (networkStream.DataAvailable);
                    string message = builder.ToString();
                    ProcessTcpCommand(message);
                }
            }
            catch (Exception ex)
            {
                DisconnectTcpClient();
                ShowInfo("ReceiveTcpMessageAuto - " + ex.Message);
            }
        }

        private void SendTcpMessageAsClient(string message)
        {
            try
            {
                availableTime = 0f;
                byte[] data = GetByteArrayFromString(message);
                networkStream.Write(data, 0, data.Length);
            }
            catch (System.IO.IOException)
            {
                ShowInfo("The connection with the host is broken!");
                DisconnectTcpClient();
            }
            catch (Exception ex)
            {
                DisconnectTcpClient();
                ShowInfo("SendTcpMessageAsClient - " + ex.Message);
            }
        }

        private void StartTcpClientProcess(string hostIP)
        {
            try
            {
                tcpClient = new TcpClient(new IPEndPoint(IPAddress.Any, ListenerTCPport));
                tcpClient.Connect(IPAddress.Parse(hostIP), ListenerTCPport);
                networkStream = tcpClient.GetStream();
                string firstMessage = GetNickname();
                byte[] data = GetByteArrayFromString(firstMessage);
                networkStream.Write(data, 0, data.Length);
                availableCheck = AvailableCheckAsClient;
                StartAvailableCheck();
                SetAct(ReceiveTcpMessageAuto);
            }
            catch (Exception ex)
            {
                DisconnectTcpClient();
                ShowInfo("StartTcpClientProcess - " + ex.Message);
            }
        }

        private void DisconnectTcpClient()
        {
            StopAvailableCheck();
            RemAct(SendRequestInfoAuto);
            RemAct(ReceiveTcpMessageAuto);
            createChatMessage = null;
            sendMessageAsManually = null;
            enemyMadeMove = null;
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream = null;
            }
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
        }

        private void ProcessTcpCommand(string data, string Uid = "")
        {
            List<string> divided = Information.GetDividedCommands(data);
            foreach (string dividedCommand in divided)
            {
                ShowInfo(dividedCommand + " - command");
                if (Information.IsData(dividedCommand))
                {
                    Information.TransceiverData receivedTypeOfData = Information.GetTypeOfData(dividedCommand);
                    if (receivedTypeOfData == Information.TransceiverData.TicTacToePosX)
                    {
                        receivedPosX = int.Parse(Information.GetData(dividedCommand, receivedTypeOfData));
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.TicTacToePosY)
                    {
                        receivedPosY = int.Parse(Information.GetData(dividedCommand, receivedTypeOfData));
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.TurnWasMade)
                    {
                        SetAct(PositionsReceivedAndWillBeSentSoon);
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.ChatMessage)
                    {
                        ShowInfo((string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname) + ": " +
                            Information.GetData(dividedCommand, receivedTypeOfData));
                        SendBroadcastTcpMessage(dividedCommand, Uid);
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.TicTacToeBoardSize)
                    {
                        AddOrUpdateData(new Information.Data(receivedTypeOfData, int.Parse(Information.GetData(dividedCommand, receivedTypeOfData))));
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.TicTacToeWinSize)
                    {
                        AddOrUpdateData(new Information.Data(receivedTypeOfData, int.Parse(Information.GetData(dividedCommand, receivedTypeOfData))));
                        AllRequestTicTacToeDataReceived();
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.OtherNickname)
                    {
                        AddOrUpdateData(new Information.Data(receivedTypeOfData, Information.GetData(dividedCommand, receivedTypeOfData)));
                        if (IsHost() && (myApplication == Information.Applications.TicTacToe))
                        {
                            showSecondPlayerNicknameAndSetTurn?.Invoke((string)GetDataByTransceiverData(receivedTypeOfData));
                            showSecondPlayerNicknameAndSetTurn = null;
                            RequestAsHostOtherNicknameReceived();
                        }
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.MyNickname)
                    {
                        AddOrUpdateData(new Information.Data(Information.TransceiverData.OtherNickname,
                            Information.GetData(dividedCommand, receivedTypeOfData)));
                        if (myApplication == Information.Applications.Chat)
                        {
                            SendBroadcastTcpMessage(dividedCommand, Uid);
                        }
                    }
                    else if (receivedTypeOfData == Information.TransceiverData.OtherApplication)
                    {
                        receivedApplication = Information.GetApplicationByString(Information.GetData(dividedCommand, receivedTypeOfData));
                    }
                }
                else if (Information.IsInvitationAcceptedCommand(dividedCommand))
                {
                    OtherPlayerAcceptInvitation((string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname));
                }
                else if (Information.IsInvitationRefusedCommand(dividedCommand))
                {
                    OtherPlayerRefuseInvitation((string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname));
                }
                else if (Information.IsInvitationCommand(dividedCommand))
                {
                    InvitationReceived((string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname), receivedApplication);
                }
                else if (Information.IsRequestCommand(dividedCommand))
                {
                    Information.TransceiverData transceiverData = Information.GetTransceiverDataFromRequestCommand(dividedCommand);
                    if ((transceiverData == Information.TransceiverData.TicTacToeBoardSize) ||
                        (transceiverData == Information.TransceiverData.TicTacToeWinSize))
                    {
                        if (myRole == Role.Host)
                        {
                            SendUniastTcpMessage(Information.SetDataCommand(transceiverData,
                                ((int)GetDataByTransceiverData(transceiverData)).ToString()), Uid);
                        }
                        else if (myRole == Role.Client)
                        {
                            SendTcpMessageAsClient(Information.SetDataCommand(transceiverData,
                                ((int)GetDataByTransceiverData(transceiverData)).ToString()));
                        }
                    }
                    else if (transceiverData == Information.TransceiverData.OtherNickname)
                    {
                        if (myRole == Role.Host)
                        {
                            SendUniastTcpMessage(Information.SetDataCommand(transceiverData, GetNickname()), Uid);
                        }
                        else if (myRole == Role.Client)
                        {
                            SendTcpMessageAsClient(Information.SetDataCommand(transceiverData, GetNickname()));
                        }
                    }
                }
            }
        }

        private void SendRequestInfoAuto()
        {
            requestTime += Time.deltaTime;
            if (requestTime >= RequestCooldown)
            {
                requestTime = 0f;
                requestInfo?.Invoke();
            }
        }

        private void AllRequestTicTacToeDataReceived()
        {
            RemAct(SendRequestInfoAuto);
            LoadTicTacToeScene();
        }

        private void RequestAsHostOtherNicknameReceived()
        {
            RemAct(SendRequestInfoAuto);
        }

        private void OtherPlayerAcceptInvitation(string other_nickname)
        {
            ShowInfo("\"" + other_nickname + "\"" + " accept invitation!");
        }

        private void OtherPlayerRefuseInvitation(string other_nickname)
        {
            ShowInfo("\"" + other_nickname + "\"" + " refuse invitation!");
            ClientObject clientObject = GetClientByNickname(other_nickname);
            clientObject?.Close();
            RemoveTcpConnection(clientObject);
        }

        private void InvitationReceived(string nickname, Information.Applications app)
        {
            receivedInvitationNickname = nickname;
            receivedInvitationApplication = app;
            invitationReceived?.Invoke(Information.StringApplications[app], nickname);
        }

        private ClientObject GetClientByNickname(string nickname)
        {
            foreach (ClientObject client in clientObjects)
            {
                if (client.userName == nickname)
                {
                    return client;
                }
            }
            return null;
        }

        private void PositionsReceivedAndWillBeSentSoon()
        {
            currentTimeToShowEnemyMove += Time.deltaTime;
            if ((currentTimeToShowEnemyMove >= TimeToShowEnemyMove) && (receivedPosX != 0) && (receivedPosY != 0))
            {
                RemAct(PositionsReceivedAndWillBeSentSoon);
                enemyMadeMove?.Invoke(receivedPosX, receivedPosY);
                receivedPosX = 0;
                receivedPosY = 0;
                currentTimeToShowEnemyMove = 0f;
            }
        }

        private void SetAct(Act _act)
        {
            act += _act;
        }

        private void RemAct(Act _act)
        {
            act -= _act;
        }

        private void ShowInfo(string info)
        {
            Debug.Log(info);
            createChatMessage?.Invoke(info);
        }

        public void SendHiMessageChatHost()
        {
            ShowInfo("Room created, waiting for connections...");
            ShowInfo("Ip: " + (string)GetDataByTransceiverData(Information.TransceiverData.MyIpAddress) + ",   Nickname: " + GetNickname());
            try
            {
                test1();
            }
            catch (Exception ex)
            {
                ShowInfo(ex.Message);
            }
        }

        private void test1()
        {
            foreach (IPAddress iPAddress in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                ShowInfo(iPAddress.ToString() + " - " + iPAddress.AddressFamily.ToString());
            }
        }

        public void SendHiMessageChatClient()
        {
            ShowInfo("Welcome, " + GetNickname());
        }

        public void Exitt()
        {
            ClearDatas();
            myRole = Role.RoleError;
            myApplication = Information.Applications.ApplicationError;
            DisconnectTcpHost();
            DisconnectTcpClient();
            StopUdpProcess();
            StopClientListen();
            LoadOpeningScene();
        }

        private void Update()
        {
            act?.Invoke();
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(instance);
            }
        }

        private void GetMyIp()
        {
            foreach (IPAddress iPAddress in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (iPAddress.AddressFamily.Equals(AddressFamily.InterNetwork))
                {
                    AddOrUpdateData(new Information.Data(Information.TransceiverData.MyIpAddress, iPAddress.ToString()));
                }
            }
        }

        private void ReceiveUdpMessageAuto()
        {
            if (receivedUdpData.Length > 0)
            {
                ShowInfo(receivedUdpData);
                receivedUdpData = string.Empty;
            }
        }

        private void StartUdpProcess()
        {
            try
            {
                udpClient = new UdpClient(BroadcastUDPport);
                SetAct(ReceiveUdpMessageAuto);
                if (!IsIHostByIp())
                {
                    SetAct(BroadcastUdpAuto);
                }
                udpReceiveThread = new System.Threading.Thread(new System.Threading.ThreadStart(ReceiveUdp));
                udpReceiveThread.Start();
            }
            catch (Exception ex)
            {
                StopUdpProcess();
                ShowInfo("StartUdpProcess - " + ex.Message);
            }
        }

        private bool IsIHostByIp()
        {
            return ((string)GetDataByTransceiverData(Information.TransceiverData.MyIpAddress)).EndsWith(".1");
        }

        private void StopUdpProcess()
        {
            RemAct(ReceiveUdpMessageAuto);
            RemAct(BroadcastUdpAuto);
            ReceiveUdpMessageAuto();
            messageToUdpBroadcast = null;
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }
            if ((udpReceiveThread != null) && udpReceiveThread.IsAlive)
            {
                udpReceiveThread.Interrupt();
                udpReceiveThread = null;
            }
        }

        private void ReceiveUdp()
        {
            IPEndPoint udpEndPointClient = null;
            while (true)
            {
                try
                {
                    if (!isJoinButtonWillBeCreatedSoon && !isInviteButtonWillBeCreatedSoon)
                    {
                        byte[] data = udpClient.Receive(ref udpEndPointClient);
                        string message = GetStringFromByteArray(data);
                        if  (message == messageToUdpBroadcast.Invoke()) continue;
                        List<string> divided = Information.GetDividedCommands(message);
                        foreach (string command in divided)
                        {
                            if (Information.IsData(command))
                            {
                                Information.TransceiverData receivedTypeOfData = Information.GetTypeOfData(command);
                                if (receivedTypeOfData == Information.TransceiverData.MyNickname)
                                {
                                    AddOrUpdateData(new Information.Data(Information.TransceiverData.OtherNickname,
                                        Information.GetData(command, receivedTypeOfData)));
                                }
                                else if (receivedTypeOfData == Information.TransceiverData.OtherIpAddress)
                                {
                                    AddOrUpdateData(new Information.Data(receivedTypeOfData, Information.GetData(command, receivedTypeOfData)));
                                }
                                else if (receivedTypeOfData == Information.TransceiverData.OtherApplication)
                                {
                                    receivedApplication = Information.GetApplicationByString(Information.GetData(command, receivedTypeOfData));
                                }
                            }
                        }
                        if (IsDatasContains(Information.TransceiverData.OtherIpAddress) &&
                            IsDatasContains(Information.TransceiverData.OtherNickname) &&
                            ((string)GetDataByTransceiverData(Information.TransceiverData.OtherIpAddress) != string.Empty) &&
                            ((string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname) != string.Empty))
                        {
                            if (receivedApplication != Information.Applications.ApplicationError)
                            {
                                isJoinButtonWillBeCreatedSoon = true;
                                SetAct(CreateJoinButtonFromMainThread);
                                continue;
                            }
                            else if (receivedApplication == Information.Applications.ApplicationError)
                            {
                                isInviteButtonWillBeCreatedSoon = true;
                                SetAct(CreateInviteButtonFromMainThread);
                                continue;
                            }
                        }
                        else receivedUdpData = message;
                    }
                }
                catch (Exception ex)
                {
                    receivedUdpData = "ReceiveUdp - " + ex.Message;
                    SetAct(StopReceiveUdpFromMainThread);
                    break;
                }
            }
        }

        private void StopReceiveUdpFromMainThread()
        {
            RemAct(StopReceiveUdpFromMainThread);
            StopUdpProcess();
        }

        private void CreateJoinButtonFromMainThread()
        {
            RemAct(CreateJoinButtonFromMainThread);
            setCreatedButton?.Invoke((string)GetDataByTransceiverData(Information.TransceiverData.OtherIpAddress),
                (string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname),
                receivedApplication, JoinButtonPressed);
            AddOrUpdateData(new Information.Data(Information.TransceiverData.OtherIpAddress, string.Empty));
            AddOrUpdateData(new Information.Data(Information.TransceiverData.OtherNickname, string.Empty));
            receivedApplication = Information.Applications.ApplicationError;
            isJoinButtonWillBeCreatedSoon = false;
        }

        private void CreateInviteButtonFromMainThread()
        {
            RemAct(CreateInviteButtonFromMainThread);
            setCreatedButton?.Invoke((string)GetDataByTransceiverData(Information.TransceiverData.OtherIpAddress),
                (string)GetDataByTransceiverData(Information.TransceiverData.OtherNickname),
                myApplication, InviteButtonPressed);
            AddOrUpdateData(new Information.Data(Information.TransceiverData.OtherIpAddress, string.Empty));
            AddOrUpdateData(new Information.Data(Information.TransceiverData.OtherNickname, string.Empty));
            receivedApplication = Information.Applications.ApplicationError;
            isInviteButtonWillBeCreatedSoon = false;
        }

        public void SetCreateButtonDelegate(SetDelegateToButton setDelegateToButton)
        {
            setCreatedButton = setDelegateToButton;
        }

        public void SetCreateChatMessageDelegate(DoSomethingWithStringDelegate createChatMessageDelegate)
        {
            createChatMessage = createChatMessageDelegate;
        }

        public void SetShowSecondPlayerNicknameAndSetTurnDelegate(DoSomethingWithStringDelegate _showSecondPlayerNicknameAndSetTurn)
        {
            showSecondPlayerNicknameAndSetTurn = _showSecondPlayerNicknameAndSetTurn;
        }

        public void SetGetManuallySendableMessageDelegate(GetSomeStringDelegate getSomeStringDelegate)
        {
            getManuallySendableMessage = getSomeStringDelegate;
        }

        public void SetEnemyMadeMoveDelegate(EnemyMadeMoveDelegate enemyMadeMoveDelegate)
        {
            enemyMadeMove = enemyMadeMoveDelegate;
        }

        public void SetInvitationReceivedDelegate(DoSomethingWithTwoStringDelegate _invitationReceived)
        {
            invitationReceived = _invitationReceived;
        }

        private void BroadcastUdpAuto()
        {
            broadcastUdpTime += Time.deltaTime;
            if (broadcastUdpTime >= BroadcastUdpCooldown)
            {
                broadcastUdpTime = 0f;
                try
                {
                    string message = messageToUdpBroadcast.Invoke();
                    byte[] data = GetByteArrayFromString(message);
                    udpClient.Send(data, data.Length, new IPEndPoint(BroadcastIpAddress, BroadcastUDPport));
                }
                catch (Exception ex)
                {
                    StopUdpProcess();
                    ShowInfo("BroadcastUdpAuto - " + ex.Message);
                }
            }
        }

        private string GetCreatedRoomInformation()
        {
            return Information.SetJoinCommand((string)GetDataByTransceiverData(Information.TransceiverData.MyIpAddress), myApplication, GetNickname());
        }

        private string GetInvitationInformation()
        {
            return Information.SetInviteMeCommand((string)GetDataByTransceiverData(Information.TransceiverData.MyIpAddress), GetNickname());
        }

        private void SendMessageAsHostManually()
        {
            string message = getManuallySendableMessage?.Invoke();
            ShowInfo(GetNickname() + "(host): " + message);
            message = Information.SetChatMessageCommand(GetNickname() + "(host)", message);
            SendBroadcastTcpMessage(message, string.Empty);
        }

        private void SendMessageAsClientManually()
        {
            string message = getManuallySendableMessage?.Invoke();
            ShowInfo(GetNickname() + ": " + message);
            message = Information.SetChatMessageCommand(GetNickname(), message);
            SendTcpMessageAsClient(message);
        }

        public void SendMessageAsManually()
        {
            sendMessageAsManually?.Invoke();
        }

        public void SendXODataAsHost(string data)
        {
            SendBroadcastTcpMessage(data, string.Empty);
        }

        public void SendXODataAsClient(string data)
        {
            SendTcpMessageAsClient(data);
        }

        private static byte[] GetByteArrayFromString(string infoToEncode)
        {
            return Encoding.Unicode.GetBytes(infoToEncode);
        }

        private static string GetStringFromByteArray(byte[] infoToEncode)
        {
            return Encoding.Unicode.GetString(infoToEncode);
        }

        public class ClientObject
        {
            protected internal string UID { get; private set; }
            protected internal NetworkStream UserStream { get; private set; }
            protected internal readonly string userName;
            private readonly TcpClient userClient;

            public ClientObject(TcpClient tcpClient)
            {
                UID = Guid.NewGuid().ToString();
                userClient = tcpClient;
                UserStream = userClient.GetStream();
                userName = GetMessage();
            }

            public ClientObject(TcpClient tcpClient, string nickname)
            {
                UID = Guid.NewGuid().ToString();
                userClient = tcpClient;
                UserStream = userClient.GetStream();
                userName = nickname;
            }

            protected internal bool HasData()
            {
                return UserStream.DataAvailable;
            }

            protected internal string GetMessage()
            {
                byte[] data = new byte[64];
                StringBuilder builder = new StringBuilder();
                do
                {
                    int bytes = UserStream.Read(data, 0, data.Length);
                    builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                }
                while (HasData());
                return builder.ToString();
            }

            protected internal void Close()
            {
                if (UserStream != null) UserStream.Close();
                if (userClient != null) userClient.Close();
            }
        }
    }
}
