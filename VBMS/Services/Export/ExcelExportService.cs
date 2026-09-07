using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Export
{
    public class ExcelExportService : IExcelExportService
    {
        public Task ExportEventLogsAsync(string filePath, IEnumerable<EventLogModel> logs)
        {
            var exportList = logs.ToList();

            return Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("이벤트로그");

                // 1. 기본 그리드선 표시 설정
                sheet.ShowGridLines = true;

                // 2. 헤더 정의
                sheet.Cell(1, 1).Value = "발생 시간";
                sheet.Cell(1, 2).Value = "라인";
                sheet.Cell(1, 3).Value = "연";
                sheet.Cell(1, 4).Value = "단";
                sheet.Cell(1, 5).Value = "내용";

                var headerRange = sheet.Range(1, 1, 1, 5);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontSize = 11;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#334155");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                sheet.Row(1).Height = 26; // 헤더 높이 여유 확보

                // 3. 데이터 입력
                for (int i = 0; i < exportList.Count; i++)
                {
                    int row = i + 2;
                    var item = exportList[i];

                    // 날짜 서식 적용 (### 방지)
                    var dateCell = sheet.Cell(row, 1);
                    dateCell.Value = item.DateTime;
                    dateCell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    dateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    sheet.Cell(row, 2).Value = item.Line;
                    sheet.Cell(row, 3).Value = item.Bay;
                    sheet.Cell(row, 4).Value = item.Level;
                    sheet.Cell(row, 5).Value = item.Content;

                    sheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    // 행 높이 및 수직 중앙 정렬
                    sheet.Row(row).Height = 20;
                }

                // 4. 전체 데이터 테두리(얇은 회색선) 및 수직 중앙 정렬
                if (exportList.Count > 0)
                {
                    var dataRange = sheet.Range(1, 1, exportList.Count + 1, 5);
                    dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
                    dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#E2E8F0");
                }

                // 5. ⭐ 글자 크기에 맞춰 자동 조절 후, 한글 잘림 방지 여유폭(+4) 부여
                sheet.Columns().AdjustToContents();
                foreach (var col in sheet.ColumnsUsed())
                {
                    // 기본 측정값에 +4를 더해 한글/볼드체 잘림을 완벽 차단하고 최소 너비 12 보장
                    col.Width = Math.Max(col.Width + 4, 12);
                }

                workbook.SaveAs(filePath);
            });
        }

        public Task ExportDumpLogsAsync(string filePath, IEnumerable<DetectorDumpLogModel> logs)
        {
            var exportList = logs.ToList();

            return Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("이상징후_덤프로그");

                sheet.ShowGridLines = true;

                // 1. 헤더 정의
                sheet.Cell(1, 1).Value = "수신 시간";
                sheet.Cell(1, 2).Value = "라인";
                sheet.Cell(1, 3).Value = "연";
                sheet.Cell(1, 4).Value = "단";
                sheet.Cell(1, 5).Value = "온도(°C)";
                sheet.Cell(1, 6).Value = "화재 여부";
                sheet.Cell(1, 7).Value = "통신 상태";

                var headerRange = sheet.Range(1, 1, 1, 7);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontSize = 11;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#B91C1C");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                sheet.Row(1).Height = 26;

                // 2. 데이터 입력
                for (int i = 0; i < exportList.Count; i++)
                {
                    int row = i + 2;
                    var item = exportList[i];

                    // 밀리초 포함 날짜 문자열
                    sheet.Cell(row, 1).Value = item.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    sheet.Cell(row, 2).Value = item.Line;
                    sheet.Cell(row, 3).Value = item.Bay;
                    sheet.Cell(row, 4).Value = item.Level;

                    // 온도 숫자 포맷 (소수점 1자리)
                    var tempCell = sheet.Cell(row, 5);
                    tempCell.Value = item.Temperature;
                    tempCell.Style.NumberFormat.Format = "0.0";

                    sheet.Cell(row, 6).Value = item.IsFire ? "화재 발생" : "정상";
                    sheet.Cell(row, 7).Value = item.IsError ? "통신 오류" : "정상";

                    sheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    sheet.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    sheet.Row(row).Height = 20;
                }

                // 3. 테두리 적용
                if (exportList.Count > 0)
                {
                    var dataRange = sheet.Range(1, 1, exportList.Count + 1, 7);
                    dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
                    dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#E2E8F0");
                }

                // 4. ⭐ 글자 크기에 맞춰 자동 조절 후, 한글 잘림 방지 여유폭(+4) 부여
                sheet.Columns().AdjustToContents();
                foreach (var col in sheet.ColumnsUsed())
                {
                    col.Width = Math.Max(col.Width + 4, 12);
                }

                workbook.SaveAs(filePath);
            });
        }
    }
}