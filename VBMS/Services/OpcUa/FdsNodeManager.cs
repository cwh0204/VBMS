using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using VBMS.Models;
using VBMS.Services.Evaluators;

namespace VBMS.Services.OpcUa
{
    /// <summary>
    /// FDS OPC UA 노드 매니저
    /// </summary>
    public class FdsNodeManager : CustomNodeManager2
    {
        private BaseDataVariableState? _aliveNode;
        private BaseDataVariableState? _lane1RackNode;
        private BaseDataVariableState? _lane2RackNode;
        private readonly FdsOptions _options;
        private readonly IFireSignalEvaluator _signalEvaluator;

        public FdsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IOptions<FdsOptions> options,
            IFireSignalEvaluator signalEvaluator)
            : base(server, configuration, "http://vbms.com/FDS/")
        {
            _options = options.Value;
            _signalEvaluator = signalEvaluator;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                SystemContext.EncodeableFactory.AddEncodeableType(typeof(FireSignalInfo));

                NodeId dataTypeId = CreateFireSignalInfoDataType();

                FolderState fdsFolder = CreateFolder(null!, "FDS", "FDS");
                fdsFolder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, fdsFolder.NodeId));

                FolderState f1fds01Folder = CreateFolder(fdsFolder, "F1FDS01", "F1FDS01");

                FolderState commonFolder = CreateFolder(f1fds01Folder, "Common", "Common");
                _aliveNode = CreateVariable(commonFolder, "Alive", "Alive", DataTypeIds.Boolean, ValueRanks.Scalar);
                _aliveNode.Value = true;

                var lane1Opt = _options.Lanes?.FirstOrDefault(l => l.LaneNumber == 1);
                int lane1Bay = lane1Opt != null && lane1Opt.TargetBay > 0 ? lane1Opt.TargetBay : 70;
                int lane1Level = lane1Opt != null && lane1Opt.TargetLevel > 0 ? lane1Opt.TargetLevel : 13;

                var lane2Opt = _options.Lanes?.FirstOrDefault(l => l.LaneNumber == 2);
                int lane2Bay = lane2Opt != null && lane2Opt.TargetBay > 0 ? lane2Opt.TargetBay : 54;
                int lane2Level = lane2Opt != null && lane2Opt.TargetLevel > 0 ? lane2Opt.TargetLevel : 13;

                FolderState lane1Folder = CreateFolder(f1fds01Folder, "Lane1", "Lane1");
                _lane1RackNode = CreateRackVariable(lane1Folder, "Lane1_Rack", "Rack", dataTypeId, lane1Bay, lane1Level);

                FolderState lane2Folder = CreateFolder(f1fds01Folder, "Lane2", "Lane2");
                _lane2RackNode = CreateRackVariable(lane2Folder, "Lane2_Rack", "Rack", dataTypeId, lane2Bay, lane2Level);
            }
        }

        private NodeId CreateFireSignalInfoDataType()
        {
            NodeId dataTypeId = new NodeId("FireSignalInfo", NamespaceIndex);
            NodeId encodingId = new NodeId("FireSignalInfo_Encoding_DefaultBinary", NamespaceIndex);

            var structDef = new StructureDefinition
            {
                DefaultEncodingId = encodingId,
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection
                {
                    new StructureField { Name = "Signal", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "TimeStamp", DataType = DataTypeIds.DateTime, ValueRank = ValueRanks.Scalar }
                }
            };

            DataTypeState dataTypeNode = new DataTypeState
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName("FireSignalInfo", NamespaceIndex),
                DisplayName = new LocalizedText("en", "FireSignalInfo"),
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(structDef)
            };

            BaseObjectState encodingNode = new BaseObjectState(dataTypeNode)
            {
                NodeId = encodingId,
                BrowseName = new QualifiedName("Default Binary", 0),
                DisplayName = new LocalizedText("en", "Default Binary"),
                TypeDefinitionId = ObjectTypeIds.DataTypeEncodingType
            };

            dataTypeNode.AddChild(encodingNode);
            AddPredefinedNode(SystemContext, dataTypeNode);
            AddPredefinedNode(SystemContext, encodingNode);

            return dataTypeId;
        }

        private FolderState CreateFolder(NodeState parent, string path, string name)
        {
            FolderState folder = new FolderState(parent)
            {
                SymbolicName = path,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText("en", name)
            };

            if (parent != null) parent.AddChild(folder);
            AddPredefinedNode(SystemContext, folder);
            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent)
            {
                SymbolicName = path,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                DataType = dataType,
                ValueRank = valueRank,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };

            if (parent != null) parent.AddChild(variable);
            AddPredefinedNode(SystemContext, variable);
            return variable;
        }

        private BaseDataVariableState CreateRackVariable(NodeState parent, string path, string name, NodeId dataTypeId, int targetBay, int targetLevel)
        {
            var variable = CreateVariable(parent, path, name, dataTypeId, ValueRanks.TwoDimensions);

            variable.ArrayDimensions = new ReadOnlyList<uint>(new uint[] { (uint)targetBay, (uint)targetLevel });

            var matrix = new ExtensionObject[targetBay, targetLevel];
            DateTime now = DateTime.Now;

            for (int i = 0; i < targetBay; i++)
            {
                for (int j = 0; j < targetLevel; j++)
                {
                    matrix[i, j] = new ExtensionObject(new FireSignalInfo
                    {
                        Signal = 0,
                        TimeStamp = now
                    });
                }
            }

            variable.Value = matrix;
            return variable;
        }

        public void UpdateRackFromDetectors(int lane, int bayOffset, List<DetectorData> detectors)
        {
            if (detectors == null || detectors.Count == 0) return;

            var laneOpt = _options.Lanes?.FirstOrDefault(l => l.LaneNumber == lane);

            int maxBay = laneOpt != null && laneOpt.TargetBay > 0 ? laneOpt.TargetBay : lane == 1 ? 70 : 54;
            int maxLevel = laneOpt != null && laneOpt.TargetLevel > 0 ? laneOpt.TargetLevel : 13;

            lock (Lock)
            {
                BaseDataVariableState? targetNode = lane == 1 ? _lane1RackNode : _lane2RackNode;
                if (targetNode == null) return;

                var matrix = targetNode.Value as ExtensionObject[,];
                if (matrix == null || matrix.GetLength(0) != maxBay || matrix.GetLength(1) != maxLevel)
                {
                    matrix = new ExtensionObject[maxBay, maxLevel];
                    targetNode.ArrayDimensions = new ReadOnlyList<uint>(new uint[] { (uint)maxBay, (uint)maxLevel });
                }

                DateTime now = DateTime.Now;
                bool isUpdated = false;

                foreach (var det in detectors)
                {
                    int globalRow = det.Bay - 1 + bayOffset;
                    int col = det.Level >= 13 ? det.Level - 1 : det.Level;

                    if (globalRow >= 0 && globalRow < maxBay && col >= 0 && col < maxLevel)
                    {
                        uint finalSignal = _signalEvaluator.Evaluate(det.Status, det.Temperature);

                        matrix[globalRow, col] = new ExtensionObject(new FireSignalInfo
                        {
                            Signal = finalSignal,
                            TimeStamp = now
                        });

                        isUpdated = true;
                    }
                }

                if (isUpdated)
                {
                    targetNode.Value = matrix;
                    targetNode.ClearChangeMasks(SystemContext, true);
                }
            }
        }

        public void UpdateRackCell(int lane, int row, int col, uint signal)
        {
            var laneOpt = _options.Lanes?.FirstOrDefault(l => l.LaneNumber == lane);
            int targetBay = laneOpt != null && laneOpt.TargetBay > 0 ? laneOpt.TargetBay : lane == 1 ? 70 : 54;
            int targetLevel = laneOpt != null && laneOpt.TargetLevel > 0 ? laneOpt.TargetLevel : 13;

            lock (Lock)
            {
                BaseDataVariableState? targetNode = lane == 1 ? _lane1RackNode : _lane2RackNode;
                if (targetNode == null) return;

                var matrix = targetNode.Value as ExtensionObject[,];
                if (matrix == null) return;

                if (row < 0 || row >= targetBay || col < 0 || col >= targetLevel) return;

                matrix[row, col] = new ExtensionObject(new FireSignalInfo
                {
                    Signal = signal,
                    TimeStamp = DateTime.Now
                });

                targetNode.Value = matrix;
                targetNode.ClearChangeMasks(SystemContext, true);
            }
        }

        public void UpdateRackAll(int lane, uint[,] signalMatrix)
        {
            var laneOpt = _options.Lanes?.FirstOrDefault(l => l.LaneNumber == lane);
            int targetBay = laneOpt != null && laneOpt.TargetBay > 0 ? laneOpt.TargetBay : lane == 1 ? 70 : 54;
            int targetLevel = laneOpt != null && laneOpt.TargetLevel > 0 ? laneOpt.TargetLevel : 13;

            lock (Lock)
            {
                BaseDataVariableState? targetNode = lane == 1 ? _lane1RackNode : _lane2RackNode;
                if (targetNode == null) return;

                int inputRows = signalMatrix.GetLength(0);
                int inputCols = signalMatrix.GetLength(1);

                DateTime now = DateTime.Now;
                var matrix = new ExtensionObject[targetBay, targetLevel];

                for (int i = 0; i < targetBay; i++)
                {
                    for (int j = 0; j < targetLevel; j++)
                    {
                        uint sig = i < inputRows && j < inputCols ? signalMatrix[i, j] : 0;

                        matrix[i, j] = new ExtensionObject(new FireSignalInfo
                        {
                            Signal = sig,
                            TimeStamp = now
                        });
                    }
                }

                targetNode.Value = matrix;
                targetNode.ClearChangeMasks(SystemContext, true);
            }
        }

        public void SetBoardCommunicationFault(int lane, int bayOffset, int bayCount = 16)
        {
            lock (Lock)
            {
                BaseDataVariableState? targetNode = lane == 1 ? _lane1RackNode : _lane2RackNode;
                if (targetNode?.Value is not ExtensionObject[,] matrix) return;

                int maxBay = matrix.GetLength(0);
                int maxLevel = matrix.GetLength(1);
                DateTime now = DateTime.Now;
                bool isUpdated = false;

                for (int bay = bayOffset; bay < bayOffset + bayCount && bay < maxBay; bay++)
                {
                    for (int level = 0; level < maxLevel; level++)
                    {
                        matrix[bay, level] = new ExtensionObject(new FireSignalInfo
                        {
                            Signal = 3, // 3: 통신 오류 / 미연결
                            TimeStamp = now
                        });
                        isUpdated = true;
                    }
                }

                if (isUpdated)
                {
                    targetNode.Value = matrix;
                    targetNode.ClearChangeMasks(SystemContext, true);
                }
            }
        }
    }
}