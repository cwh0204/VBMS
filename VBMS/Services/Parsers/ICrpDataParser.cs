using VBMS.Models;

namespace VBMS.Services.Parsers
{
    public interface ICrpDataParser
    {
        /// <summary>
        /// CRP Raw 문자열 패킷을 자라 CrpPacket DTO로 변환합니다.
        /// </summary>
        /// <param name="rawData">수신된 Raw 문자열 (예: "(00116150,025000025...98)")</param>
        /// <returns>파싱된 CrpPacket 객체 (유효하지 않을 경우 null)</returns>
        CrpPacket Parse(string rawData);
    }
}