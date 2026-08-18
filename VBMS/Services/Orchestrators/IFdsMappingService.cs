namespace VBMS.Services.Orchestrators
{
    public interface IFdsMappingService
    {
        // 기존 메서드
        bool TryGetBoardMapping(string boardId, int maxLine, out int lane, out int bayOffset);

        bool TryGetBoardMapping(string boardId, int maxLine, out int lane, out int bayOffset, out int targetBay, out int targetLevel);
    }
}