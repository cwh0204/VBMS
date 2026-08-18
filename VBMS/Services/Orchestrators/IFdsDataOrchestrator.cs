using System;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Orchestrators
{
    public interface IFdsDataOrchestrator : IDisposable
    {
        event Action<CrpPacket>? OnPacketProcessed;
        event Action<int, CrpPacket>? OnPacketProcessedWithOffset;
        event Action<bool>? OnConnectionChanged;
        event Action<string, string>? OnLogMessage;

        Task StartServerAsync(int port);
        void StopServer();

    }
}