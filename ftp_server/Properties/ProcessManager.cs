using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Runtime.Remoting;
using System.Timers;

namespace Server
{
    public class ServerProcessManager //Should be made when connection is newely established
    {
        private readonly List<ServerProcess> _processList = new List<ServerProcess>();
        private readonly Communicator _serverCommunicator;
        private const int CheckSendTimeInterval = 1000;
        private const int CheckReceiveTimeInterval = CheckSendTimeInterval * 400;
        private const int InitializeTimerInterval = 60000;

        private readonly User _user;
        private bool Activated { get; set; }

        internal ServerProcessManager(User user, Communicator communicator)
        {
            _user = user;
            _serverCommunicator = communicator;
            Activated = false;
        }

        private void SetSocket(Socket socket)
        {
            _user.SetUserSocket(socket);
            _serverCommunicator.SetUser(_user);
        } //Sets socket for the communicator

        private void AddProcess(ServerProcess serverProcess)
        {
            lock (_processList)
            {
                _processList.Add(serverProcess);
                _processList.Sort((x, y) => x.Pid.CompareTo(y.Pid)); //sorts processList by Pid
            }
        }

        private ServerProcess GetProcess(byte processId)
        {
            ServerProcess process;
            lock (_processList)
            {
                process = _processList.Find(x => x.Pid == processId); //returns null if not found
            }
            return process;
        }

        public ServerProcessManager(uint userId)
        {
            _user = new User(userId);
        }
        
        /**
         * Processor methods for various operations
         * 
         */
        public void ProcessUserId(byte processId, Packet idReqestPacket)
        {
            ServerProcess idProcess = new IdProcess(_user, _processList, processId);
            Thread getIdThread = new Thread(idProcess.GetUserId);
            AddProcess(idProcess);
            getIdThread.Start();
            idProcess.EnqueueProcessQueue(idReqestPacket);
        }

        public void ProcessFile(byte processId, Packet fileRequestPacket)
        {
            ServerProcess fileProcess = new FileProcess(_user, _processList, processId);
            Thread getFileThread = new Thread(fileProcess.StartFileProcess);
            AddProcess(fileProcess);
            getFileThread.Start();
            fileProcess.EnqueueProcessQueue(fileRequestPacket);
        }

        public void ProcessFileSave(byte processId, Packet fileInfoPacket)
        {
            ServerProcess fileSaveProcess = new FileSaveProcess(_user, _processList, processId);
            Thread saveFileThread = new Thread(fileSaveProcess.StartFileSaveProcess);
            AddProcess(fileSaveProcess);
            saveFileThread.Start();
            fileSaveProcess.EnqueueProcessQueue(fileInfoPacket);
        }

        private void Analyzer()
        {
            System.Timers.Timer checkTimer = new System.Timers.Timer(CheckReceiveTimeInterval);
            System.Timers.Timer initializeTimer = new System.Timers.Timer(InitializeTimerInterval);
            initializeTimer.Elapsed += CheckTimerCallback;
            checkTimer.Elapsed += CheckTimerCallback;
            initializeTimer.Start();
            Packet packet;
            while ((packet = GetPacket(checkTimer, initializeTimer)) != null)
            {
                switch (packet.RequestType)
                {
                    case (byte) PacketType.FileRequest:
                    {
                        byte processId = packet.ProcessId;
                        ProcessFile(processId, packet);
                        break;
                    }
                    case (byte) PacketType.IdRequest:
                    {
                        byte processId = packet.ProcessId;
                        ProcessUserId(processId, packet);
                        break;
                    }
                    case (byte) PacketType.FileInfo:
                    {
                        byte processId = packet.ProcessId;
                        ProcessFileSave(processId, packet);
                        break;
                    }
                    case (byte) PacketType.File:
                    {
                        ServerProcess process = GetProcess(packet.ProcessId);
                        process.EnqueueProcessQueue(packet);
                        break;
                    }
                }
            }

            checkTimer.Stop();
            Activated = false;
        }

        private Packet GetPacket(System.Timers.Timer checkTimer, System.Timers.Timer initializeTimer)
        {
            Packet packet = _user.DequeueReceive();
            if (packet?.RequestType == (byte) PacketType.Suspend)
            {
                lock (_processList)
                {
                    foreach (ServerProcess process in _processList) //send Suspend packet to all processes
                    {
                        process.EnqueueProcessQueue(packet);
                    }
                }

                _serverCommunicator.OperationState = false;
                packet = null; //return null
            }

            initializeTimer.Stop();
            checkTimer.Stop();
            checkTimer.Start(); // Resets timer

            return packet;
        }

        private void CheckTimerCallback(object e, ElapsedEventArgs args)
        {
            Packet timeoutSuspendPacket = new Packet
            {
                Data = new byte[1],
                DataSize = 1,
                FileType = (byte) FileType.NoFile,
                MorePackets = false,
                PacketNumber = 0,
                ProcessId = 0,
                RequestType = (byte) PacketType.Suspend
            };
            MakePacketHeader(timeoutSuspendPacket);
            timeoutSuspendPacket.Data[0] = (byte) ErrorType.ConnectionCheckFailed;
            _user.EnqueueReceive(timeoutSuspendPacket);
            _serverCommunicator.OperationState = false;
        }

        private void ConnectionChecker()
        {
            while (Activated)
            {
                if (_user.CountSend() == 0)
                {
                    Packet connectionCheckPacket = new Packet
                    {
                        Data = new byte[1],
                        DataSize = 1,
                        FileType = (byte) FileType.NoFile,
                        MorePackets = false,
                        PacketNumber = 0,
                        ProcessId = 0,
                        RequestType = (byte) PacketType.ConnectionCheck
                    };
                    MakePacketHeader(connectionCheckPacket);
                    connectionCheckPacket.Data[0] = (byte) PacketType.ConnectionCheck;
                    _user.EnqueueSend(connectionCheckPacket);
                }

                Thread.Sleep(CheckSendTimeInterval); //wait
            }
        }

        private static void MakePacketHeader(Packet packet)
        {
            packet.Header[HeaderMemberStartIndex.HeaderSign] = PacketConsts.HeaderSign;
            packet.Header[HeaderMemberStartIndex.PacketNum] = (byte) (packet.PacketNumber & 0xFF);
            packet.Header[HeaderMemberStartIndex.PacketNum + 1] = (byte) (packet.PacketNumber >> 8);
            packet.Header[HeaderMemberStartIndex.RequestType] = packet.RequestType;
            packet.Header[HeaderMemberStartIndex.DataSize] = (byte) (packet.DataSize & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 1] = (byte) ((packet.DataSize >> 8) & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 2] = (byte) ((packet.DataSize >> 16) & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 3] = (byte) ((packet.DataSize >> 24) & 0xFF);
            packet.Header[HeaderMemberStartIndex.FileType] = packet.FileType;
            packet.Header[HeaderMemberStartIndex.ProcessNum] = packet.ProcessId;
            packet.Header[HeaderMemberStartIndex.EndPoint] =
                packet.MorePackets ? PacketConsts.MoreBytes : PacketConsts.LastByte;
        }

        internal void Start(Socket socket)
        {
            SetSocket(socket);
            _serverCommunicator.StartReceiveThread();
            _serverCommunicator.StartSendThread();
            Thread checkThread = new Thread(ConnectionChecker);
            Activated = true;
            checkThread.Start(); // TODO how will you stop this thread?
            Analyzer(); // program ends when Analyzer terminates
        }

        internal static bool SuspendCommunicator(Communicator communicator)
        {
            try
            {
                lock (communicator.CancelSendDequeue)
                {
                    communicator.CancelSendDequeue.Cancel();
                }

                if (communicator.UserToken.ClientSocket != null)
                {
                    try
                    {
                        communicator.UserToken.ClientSocket.Shutdown(SocketShutdown.Both);
                    }
                    catch
                    {
                    }

                    while (communicator.UserToken.ClientSocket.Connected)
                    {
                        communicator.UserToken.ClientSocket.Disconnect(false);
                    }

                    communicator.UserToken.ClientSocket.Close(); //close the socket
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}