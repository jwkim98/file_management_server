using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public enum CommunicatorError
    {
        ObjectDisposed,
        ReceiveCheckTimeout,
        SendCheckError,
        NoBytesReceived,
        SocketError,
        WrongHeader,
        DataSizeOverflow,
        EmptyData,
        InvalidRequest,
        WrongCloser,
        NoAdditionalReceived,
        ReceiverSideError,
        HeaderError
    }

    public enum PacketType : byte
    {
        FileInfo,
        IdRequest,
        Id,
        HealthRecord,
        Story,
        Manager,
        Update,
        NewUser,
        File,
        FileRequest,
        Error,
        Suspend,
        ConnectionCheck,
        FileSaveDone
    }

    public enum FileType : byte
    {
        Dlc,
        User,
        NoFile
    }

    public enum NetworkEvent
    {
        FileRequestDone,
        FileRequestFailed,
        IdRequestDone,
        FileSaveDone,
        FileSaveFailed,
        IdRequestFailed,
        FileProcessDone,
        FileProcessFailed,
        IdProcessDone,
        IdProcessFailed,
        Connected,
        Suspended
    }

    public enum ErrorType
    {
        Success,
        SocketClosed,
        FileNotFound,
        ArgumentError,
        EmptyFile,
        InvalidPacket,
        NoMoreId,
        ConnectionCheckFailed,
        ManualShutdown,
        Unknown
    }

    public static class PacketConsts//Header design definitions
    {
        public const byte HeaderSize = 11;
        public const byte HeaderSign = 0x01;
        public const int ReceiverBufferSize = ArgsBufferSize * 4;//should be largest packet size for 1 time
        public const int SenderBufferSize = ArgsBufferSize * 4;
        public const byte MoreBytes = 0xF0;
        public const byte LastByte = 0x0F;
        public const int ArgsBufferSize = 1024 *128;
        public const int SendSize = 1024 *128;
        public const int RequestTypeNum = 6;// Number of possible requests-1
    }

    public static class HeaderMemberStartIndex
    {
        public const byte HeaderSign = 0;
        public const byte PacketNum = 1;
        public const byte RequestType = 3;
        public const byte DataSize = 4;
        public const byte FileType = 8;
        public const byte ProcessNum = 9;
        public const byte EndPoint = 10;
    }

    public static class GeneralConstants
    {
        public const int InitializerTimeout = 1000 * 120;
        public const int CheckerTimeout = 1000 * 120;
    }

    public class ServerStatus
    {
        private object lockObject = new object();
        public int MaxClientNumber { get; private set; }
        public int CommunicatorPoolSize { get; private set; }
        public int UserPoolSize { get; private set; }


        private bool AcceptionActivated = true;

        public void SetMaxClientNumber(int num)
        {
            lock (lockObject)
            {
                MaxClientNumber = num;
                CommunicatorPoolSize = num;
                UserPoolSize = num;
            }

        }

        public void DisableAcception()
        {
            AcceptionActivated = false;
        }

        public void EnableAcception()
        {
            AcceptionActivated = true;
        }
    }

    public class Packet
    {
        internal byte[] Header = new byte[PacketConsts.HeaderSize];
        internal byte[] Data;
        internal byte RequestType;
        internal ushort PacketNumber;
        internal bool MorePackets;
        internal uint DataSize;
        internal byte FileType;
        internal byte ProcessId;
    }

    public static class FileBaseDirectory
    {
        public static string DlcDir { get; internal set; }
        public static string UserDir { get; internal set; }
    }


}


