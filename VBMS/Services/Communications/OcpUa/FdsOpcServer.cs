using Opc.Ua;
using Opc.Ua.Server;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using VBMS.Models;
using VBMS.Services.Evaluators;

namespace VBMS.Services.Communications.OpcUa
{
    public class FdsOpcServer : StandardServer
    {
        private readonly IOptions<FdsOptions> _options;
        private readonly IFireSignalEvaluator _signalEvaluator;

        // 외부(Orchestrator 등)에서 참조할 수 있도록 public 프로퍼티 선언
        public FdsNodeManager? NodeManager { get; private set; }

        public FdsOpcServer(IOptions<FdsOptions> options, IFireSignalEvaluator signalEvaluator)
        {
            _options = options;
            _signalEvaluator = signalEvaluator;
        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            var nodeManagers = new List<INodeManager>();

            // ★ 인스턴스를 생성하여 NodeManager 프로퍼티에 할당
            NodeManager = new FdsNodeManager(server, configuration, _options, _signalEvaluator);
            nodeManagers.Add(NodeManager);

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }
    }
}