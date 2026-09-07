using System.IO.Compression;
using System.Text;
using UniversitySchedule.ScheduleImporter;

namespace UniversitySchedule.Importer.Tests;

public sealed class ExcelScheduleImporterTests
{
    [Fact]
    public void Parse_ZipWithCfuGrid_ProducesParityLessonsAndRooms()
    {
        string root = Directory.CreateTempSubdirectory("cfu-excel-test-").FullName;
        try
        {
            string workbook = Path.Combine(root, "schedule.xlsx");
            WriteMinimalWorkbook(workbook);
            string zipPath = Path.Combine(root, "schedule.zip");
            using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(workbook, "schedule.xlsx");
            }

            ManualScheduleOverrideDocument result = new ExcelScheduleImporter().Parse(zipPath, academicYear: 2026);
            var group = Assert.Single(result.Groups);
            Assert.Equal("ПИ-б-о-252", group.Code);
            var lesson = Assert.Single(group.Lessons);
            Assert.Equal(1, lesson.Day);
            Assert.Equal(1, lesson.PairNumber);
            Assert.Equal("четная", lesson.Parity);
            Assert.Equal("Математика", lesson.Subject);
            Assert.Equal("Иванов И.И.", Assert.Single(lesson.Teachers));
            Assert.Equal("301", lesson.Classroom);
            Assert.Equal("пр.Вернадского 4", lesson.Building);
            Assert.Contains("2026-09-07", result.Weeks.EvenWeekMondays);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteMinimalWorkbook(string path)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="1 курс" sheetId="1" r:id="rId1" /></sheets>
            </workbook>
            """);
        Write(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
            </Relationships>
            """);
        Write(archive, "xl/worksheets/sheet1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>Неделя четная: 07.09; 21.09</t></is></c></row>
                <row r="2"><c r="A2" t="inlineStr"><is><t>Курс: 2</t></is></c></row>
                <row r="3"><c r="A3" t="inlineStr"><is><t>Дни недели</t></is></c><c r="B3" t="inlineStr"><is><t>пара</t></is></c><c r="C3" t="inlineStr"><is><t>вид занятий</t></is></c><c r="D3" t="inlineStr"><is><t>ПИ-б-о-252</t></is></c></row>
                <row r="4"><c r="D4" t="inlineStr"><is><t>ПИ-б-о-252(1)</t></is></c></row>
                <row r="5"><c r="A5" t="inlineStr"><is><t>понедельник</t></is></c><c r="B5" t="inlineStr"><is><t>1</t></is></c><c r="C5" t="inlineStr"><is><t>ЛК</t></is></c><c r="D5" t="inlineStr"><is><t>Математика</t></is></c></row>
                <row r="6"><c r="D6" t="inlineStr"><is><t>доц.Иванов И.И.</t></is></c></row>
                <row r="7"><c r="D7" t="inlineStr"><is><t>ауд.301(пр.Вернадского 4)</t></is></c></row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
