using System;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Communications
{
    public interface ICrpCommunicationService
    {
        bool IsConnected { get; }

        event Action<CrpPacket> OnPacketReceived;
        event Action<string, string> OnLogMessage;
        event Action<bool> OnConnectionChanged;

        Task StartServerAsync(int port);
        void Disconnect();
        Task SendCommandAsync(string command);
    }
}