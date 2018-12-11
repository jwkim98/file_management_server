using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace Server
{
    internal abstract class ServerProcess
    {
        protected User UserToken;

        private readonly BlockingCollection<Packet> _processQueue =
            new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());

        internal virtual void GetUserId() { }
        internal virtual void StartFileProcess() { }

        internal virtual void StartFileSaveProcess() { }

        public byte Pid { get; protected set; }
        protected List<ServerProcess> ProcessList;

        internal void EnqueueProcessQueue(Packet packet)
        {
            _processQueue.Add(packet);
        }

        internal Packet DequeueProcessQueue()
        {
            return _processQueue.Take();
        }

        internal Packet PeekProcessQueue()
        {
            return _processQueue.First();
        }

        internal int CountProcessQueue()
        {
            return _processQueue.Count;
        }

        internal bool AnyProcessQueue()
        {
            return _processQueue.Any();
        }

        /*
 * This method should be called before the termination of the process
 * This method removes the process from the process list
 */
        protected void TerminateProcess(ServerProcess process)
        {
            lock (ProcessList)
            {
                ProcessList.Remove(process);
            }
        }

        protected void MakePacketHeader(Packet packet)
        {
            packet.Header[HeaderMemberStartIndex.HeaderSign] = PacketConsts.HeaderSign;
            packet.Header[HeaderMemberStartIndex.PacketNum] = (byte)(packet.PacketNumber & 0xFF);
            packet.Header[HeaderMemberStartIndex.PacketNum + 1] = (byte)(packet.PacketNumber >> 8);
            packet.Header[HeaderMemberStartIndex.RequestType] = packet.RequestType;
            packet.Header[HeaderMemberStartIndex.DataSize] = (byte)(packet.DataSize & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 1] = (byte)((packet.DataSize >> 8) & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 2] = (byte)((packet.DataSize >> 16) & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 3] = (byte)((packet.DataSize >> 24) & 0xFF);
            packet.Header[HeaderMemberStartIndex.FileType] = packet.FileType;
            packet.Header[HeaderMemberStartIndex.ProcessNum] = packet.ProcessId;
            packet.Header[HeaderMemberStartIndex.EndPoint] =
                packet.MorePackets ? PacketConsts.MoreBytes : PacketConsts.LastByte;
        }
    }

    internal class FileProcess : ServerProcess
    {
        internal FileProcess(User user, List<ServerProcess> processList, byte processNum)
        {
            ProcessList = processList;
            UserToken = user;
            Pid = processNum;
        }

        /*
         * This method will Read fileRequestPacket sent from client
         * This method will call processFile method after receiving
         * This method assumes analyzer gives right packet
         * -acceptable packet types-
         * FileRequest, Suspend
         * if Suspend packet is received, This method will put itself out of processList
         */
        internal override void StartFileProcess()
        {
            Packet firstPacket = DequeueProcessQueue(); // Dequeue if there's anything to Dequeue
            if (firstPacket.RequestType == (byte)PacketType.FileRequest)
            {
                uint userId = BitConverter.ToUInt32(firstPacket.Data, 0); // 0,1,2,3
                String fileName =
                    Encoding.ASCII.GetString(firstPacket.Data, 5,
                        firstPacket.Data[4]); //first four bytes indicate userID
                FileType fileType = (FileType)firstPacket.FileType;
                ProcessFile(fileName, fileType, userId); // Get the file
            }

            if (firstPacket.RequestType != (byte)PacketType.FileRequest)
            {
                TerminateProcess(this);
            }
        }

        /*
         * This method will Read required file in filePath
         * This method makes file packets including fileInfoPacket, and Enqueues it in user's SendQueue
         * If Exception Occurs while reading the file, This method will enqueue Error packet, and Terminate
         * This process will put itself out of processList when terminates
         */
        public void ProcessFile(String fileName, FileType fileType, uint userId)
        {
            byte[] byteArray;
            try
            {
                string filePath;
                if (fileType == FileType.Dlc)
                    filePath = FileBaseDirectory.DlcDir + @"/" + fileName;
                else if (fileType == FileType.User)
                    filePath = FileBaseDirectory.UserDir + @"/" +userId.ToString()+@"/"+ fileName;
                else
                {
                    throw new ArgumentException("Invalid type");
                }

                if (userId == 0) throw new ArgumentException("Invalid Id");
                byteArray = File
                    .ReadAllBytes(
                        filePath); // Brings file byte Array TODO Sql file Request should be added here, throw Excetpion if Error occurs
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while processing the file");
                Console.WriteLine(e.Message);
                Packet errorPacket = new Packet()
                {
                    Data = new byte[2],
                    DataSize = 2,
                    FileType = (byte)FileType.NoFile,
                    MorePackets = false,
                    PacketNumber = 0,
                    ProcessId = Pid,
                    RequestType = (byte)PacketType.Error
                };
                MakePacketHeader(errorPacket);
                errorPacket.Data[0] = (byte)PacketType.FileRequest; //Received Requet
                if (e is FileNotFoundException || e is NotSupportedException)
                {
                    errorPacket.Data[1] =
                        (byte) ErrorType.FileNotFound; //TODO you can put what kind of Error has occured here
                }
                else if (e is ArgumentException)
                {
                    errorPacket.Data[1] =
                        (byte)ErrorType.ArgumentError; //TODO you can put what kind of Error has occured here
                }
                else
                {
                    errorPacket.Data[1] = (byte) ErrorType.Unknown;
                }
                UserToken.EnqueueSend(errorPacket);
                TerminateProcess(this);
                return;
            }

            if (byteArray.Length > 0)
            {
                int fileSize = byteArray.Length;
                int sourceOffset = 0;
                Packet fileInfoPacket = new Packet
                {
                    PacketNumber = 0,
                    RequestType = (byte)PacketType.FileInfo,
                    FileType = (byte)fileType,
                    MorePackets = true,
                    ProcessId = Pid
                };
                fileInfoPacket.DataSize =
                    (uint)MakeFileDataInfo(fileInfoPacket, (uint)fileSize, fileName, userId);
                MakePacketHeader(fileInfoPacket);
                UserToken.EnqueueSend(fileInfoPacket);
                //making file data into packets
                ushort packetNum = 1;
                while (sourceOffset < fileSize)
                {
                    int copySize = PacketConsts.ReceiverBufferSize;
                    Packet packet = new Packet
                    {
                        PacketNumber = packetNum,
                        RequestType = (byte)PacketType.File,
                        FileType = (byte)fileType,
                        MorePackets = true,
                        ProcessId = Pid
                    };
                    packetNum += 1;
                    if (copySize > fileSize - sourceOffset)
                    {
                        copySize = fileSize - sourceOffset;
                        packet.MorePackets = false;
                    }
                    packet.DataSize = (uint)copySize;
                    MakePacketHeader(packet);
                    packet.Data = new byte[copySize];
                    Array.Copy(byteArray, sourceOffset, packet.Data, 0, copySize);
                    UserToken.EnqueueSend(packet);
                    sourceOffset += copySize;
                } // Makes file Data array into packets

                TerminateProcess(this);
            }
            else
            {
                Packet errorPacket = new Packet
                {
                    Data = new byte[2],
                    DataSize = 2,
                    FileType = (byte)FileType.NoFile,
                    MorePackets = false,
                    PacketNumber = 0,
                    ProcessId = Pid,
                    RequestType = (byte)PacketType.Error
                };
                MakePacketHeader(errorPacket);
                errorPacket.Data[0] = (byte)PacketType.FileRequest;
                errorPacket.Data[1] = (byte)ErrorType.EmptyFile;
                UserToken.EnqueueSend(errorPacket);
                TerminateProcess(this);
            }
        }

        /*
         * This method will set Data of fileInfoPacket
         * This method returns Data size of the fileInPacket
         * This method assumes only fileInfoPacket will be passed
         */
        private int MakeFileDataInfo(Packet fileInfoPacket, uint fileSize, string fileName, uint userId)
        {
            byte[] fileNameArray = Encoding.ASCII.GetBytes(fileName);
            fileInfoPacket.Data = new byte[fileNameArray.Length + 9];
            fileInfoPacket.Data[0] = (byte)(fileSize & 0xFF);
            fileInfoPacket.Data[1] = (byte)((fileSize >> 8) & 0xFF);
            fileInfoPacket.Data[2] = (byte)((fileSize >> 16) & 0xFF);
            fileInfoPacket.Data[3] = (byte)((fileSize >> 24) & 0xFF);
            fileInfoPacket.Data[4] = (byte)(userId & 0xFF);
            fileInfoPacket.Data[5] = (byte)((userId >> 8) & 0xFF);
            fileInfoPacket.Data[6] = (byte) ((userId >> 16) & 0xFF);
            fileInfoPacket.Data[7] = (byte) ((userId >> 24) & 0xFF);
            fileInfoPacket.Data[8] = (byte)fileNameArray.Length;
            for (var i = 0; i < fileNameArray.Length; i++)
            {
                fileInfoPacket.Data[i + 9] = fileNameArray[i];
            }
            return fileNameArray.Length + 9; //Returns DataSize
        }
    }
    /*
 * IdProcess
 * This will Get ID to reply the client
 * -Receivable Packet types-
 * 
 */
    internal class FileSaveProcess: ServerProcess
    {
        private int
            ReceivedPacketCount
        {
            get;
            set;
        }
        
        internal FileSaveProcess(User user, List<ServerProcess> processList, byte processNum)
        {
            ProcessList = processList;
            UserToken = user;
            Pid = processNum;
        }

        internal override void StartFileSaveProcess()
        {
            List<byte> fileDataList = new List<byte>();
            byte[] fileData;
//          uint fileSize;
            uint userId=0;
            string fileName = "default";
            FileType fileType= FileType.NoFile;
            Packet filePacket = DequeueProcessQueue();
            if (!IsValidPacket(filePacket))
            {
                TerminateProcess(this);
                return;
            }

            if (filePacket.RequestType == (byte)PacketType.Suspend)
            {
                TerminateProcess(this);
                return;
            } //Handles suspend signal

            if (filePacket.RequestType == (byte)PacketType.FileInfo) //First Packet must be FileInfo
            {
                userId = BitConverter.ToUInt32(filePacket.Data, 4);
                fileName = Encoding.ASCII.GetString(filePacket.Data, 9, filePacket.Data[8]);
                fileType = (FileType)filePacket.FileType;
                ReceivedPacketCount = 0;
            }

            while (filePacket.MorePackets)
            {

                if (filePacket.RequestType == (byte)PacketType.File)
                {
                    fileDataList.AddRange(filePacket.Data.ToList());
                    ReceivedPacketCount++;
                }

                filePacket = DequeueProcessQueue();
                if (!IsValidPacket(filePacket))
                {
                    TerminateProcess(this);
                    return;
                }

                if (filePacket.RequestType == (byte)PacketType.Suspend)
                {
                    TerminateProcess(this);
                    return;
                } //Handles suspend signal
            }

            if (filePacket.MorePackets == false && filePacket.RequestType == (byte)PacketType.File)
            {
                fileDataList.AddRange(filePacket.Data.ToList());
                ReceivedPacketCount++;
                fileData = fileDataList.ToArray();
                Packet replyPacket;
                try
                {
                    if(userId!=0)
                        SaveFile(fileName, fileData, userId, fileType);
                    if(userId==0)
                        throw new ArgumentException();
                }
                catch(Exception e)
                {
                    replyPacket = new Packet
                    {
                        Data = new byte[2],
                        DataSize = 2,
                        FileType = (byte) FileType.NoFile,
                        MorePackets = false,
                        PacketNumber = 0,
                        ProcessId = Pid,
                        RequestType = (byte) PacketType.Error
                    };
                    MakePacketHeader(replyPacket);
                    replyPacket.Data[0] = (byte) PacketType.FileInfo;
                    if(e is ArgumentException)
                        replyPacket.Data[1] = (byte) ErrorType.ArgumentError; //TODO specify error code!
                    if (e is DirectoryNotFoundException)
                        replyPacket.Data[1] = (byte) ErrorType.FileNotFound;
                    TerminateProcess(this);
                    return;
                }

                replyPacket = new Packet
                {
                    Data=new byte[1],
                    DataSize=1,
                    FileType = (byte) FileType.NoFile,
                    MorePackets = false,
                    PacketNumber = 0,
                    ProcessId = Pid,
                    RequestType = (byte) PacketType.FileSaveDone
                };
                MakePacketHeader(replyPacket);
                replyPacket.Data[0] = (byte) ErrorType.Success;
                UserToken.EnqueueSend(replyPacket);
            }
            TerminateProcess(this); // remove itself from processList
        }

        private void SaveFile(string fileName, byte[] data, uint userId, FileType fileType)
        {
            string filePath;
            if (fileType == FileType.User)
                filePath = FileBaseDirectory.UserDir + @"/" + userId.ToString() + @"/" + fileName; // file is overwritten if it already exists
            else if (fileType == FileType.Dlc)
                filePath = FileBaseDirectory.DlcDir + @"/" + fileName;
            else
            {
                throw new DirectoryNotFoundException();
            }
            File.WriteAllBytes(filePath, data);
        }

        private bool IsValidPacket(Packet packet)
        {
            return packet.RequestType == (byte)PacketType.FileInfo || packet.RequestType == (byte)PacketType.File
                                                                   || packet.RequestType == (byte)PacketType.Error ||
                                                                   packet.RequestType == (byte)PacketType.Suspend;
        }
    }

    internal class IdProcess : ServerProcess
    {
        public IdProcess(User user, List<ServerProcess> processList, byte processNum)
        {
            UserToken = user;
            ProcessList = processList;
            Pid = processNum;
        }

        internal override void GetUserId()
        {
            Packet idRequestPacket = DequeueProcessQueue();
            if (idRequestPacket.RequestType == (byte)PacketType.IdRequest)
            {
                uint userId = ProcessId();
                if (userId == 0)
                {
                    Packet errorPacket = new Packet()
                    {
                        Data=new byte[2],
                        DataSize = 2,
                        FileType = (byte)FileType.NoFile,
                        MorePackets = false,
                        PacketNumber = 0,
                        ProcessId = Pid,
                        RequestType = (byte)PacketType.Error
                    };
                    errorPacket.Data[0] = (byte)PacketType.IdRequest; //Received Request
                    errorPacket.Data[1] = (byte)ErrorType.NoMoreId;
                    MakePacketHeader(errorPacket);
                    UserToken.EnqueueSend(errorPacket);
                    TerminateProcess(this);
                    return;
                }

                Packet idPacket = new Packet()
                {
                    Data=new byte[4],
                    DataSize = 4,
                    PacketNumber = 0,
                    FileType = (byte)FileType.NoFile,
                    MorePackets = false,
                    RequestType = (byte)PacketType.Id,
                    ProcessId = Pid
                };
                MakePacketHeader(idPacket);
                idPacket.Data[0] = (byte) (userId & 0xFF);
                idPacket.Data[1] = (byte) ((userId >> 8) & 0xFF);
                idPacket.Data[2] = (byte) ((userId >> 16) & 0xFF);
                idPacket.Data[3] = (byte) ((userId >> 24) & 0xFF);
                UserToken.EnqueueSend(idPacket);
            }
            TerminateProcess(this);
        }

        private uint ProcessId() // TODO implement id allocation process here
        {
            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(FileBaseDirectory.UserDir);

                var directoryEnumeration = directoryInfo.EnumerateDirectories().OrderBy(d =>
                {
                    int value;
                    if(int.TryParse(d.Name, out value))
                        return value;
                    else
                    {
                        return 0;
                    }
                });
                uint newId =  uint.Parse(directoryEnumeration.Last().Name) + 1;
                Directory.CreateDirectory(FileBaseDirectory.UserDir + @"/" + newId.ToString());
                return newId;
            }
            catch
            {
                Console.WriteLine("wrong file included in user directory");
                return 0;
            }
        }
    }
}
