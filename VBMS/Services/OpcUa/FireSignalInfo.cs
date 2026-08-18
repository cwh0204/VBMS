using Opc.Ua;
using System;

namespace VBMS.Services.OpcUa
{
    /// <summary>
    /// OPC UA 직렬화를 위한 화재 신호 정보 객체
    /// </summary>
    public class FireSignalInfo : IEncodeable
    {
        public uint Signal { get; set; }
        public DateTime TimeStamp { get; set; }

        public ExpandedNodeId TypeId => new ExpandedNodeId(new NodeId("FireSignalInfo", 2));
        public ExpandedNodeId BinaryEncodingId => new ExpandedNodeId(new NodeId("FireSignalInfo_Encoding_DefaultBinary", 2));
        public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

        public void Encode(IEncoder encoder)
        {
            encoder.WriteUInt32("Signal", Signal);
            encoder.WriteDateTime("TimeStamp", TimeStamp);
        }

        public void Decode(IDecoder decoder)
        {
            Signal = decoder.ReadUInt32("Signal");
            TimeStamp = decoder.ReadDateTime("TimeStamp");
        }

        public bool IsEqual(IEncodeable value)
        {
            if (value is FireSignalInfo target)
            {
                return Signal == target.Signal && TimeStamp == target.TimeStamp;
            }
            return false;
        }

        public object Clone() => MemberwiseClone();
    }
}