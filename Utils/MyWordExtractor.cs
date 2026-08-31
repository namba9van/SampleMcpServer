using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
/// <summary>
/// извлекает structured текст разделы из DOCX документы.
/// </summary>
public class MyWordExtractor
{
    /// <summary>
    /// Представляет раздел документа и его приблизительный номер страницы.
    /// </summary>
    /// <summary>
    /// Представляет раздел документа и его приблизительный номер страницы.
    /// </summary>
    public class Section
    {
        /// <summary>
        /// Получает или задаёт раздел заголовок.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Получает или задаёт извлечённый раздел содержимое.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Получает или задаёт приблизительный номер страницы, начиная с единицы.
        /// </summary>
        public int Page { get; set; }
    }
    /// <summary>
    /// Читает DOCX-документ, группирует содержимое по заголовкам и приблизительно определяет номера страниц разделов.
    /// </summary>
    /// <param name="filename">путь из DOCX файл.</param>
    /// <returns>извлечённый документ разделы.</returns>
    public List<Section> DecodeAsync(string filename)
    {
        var result = new List<Section>();

        StringBuilder currentContent = new StringBuilder();
        string currentHeading = "No Heading";

        using (WordprocessingDocument wordDocument = WordprocessingDocument.Open(filename, false))
        {
            var mainPart = wordDocument.MainDocumentPart;
            if (mainPart == null || mainPart.Document.Body == null)
            {
                return result;
            }

            var styles = GetHeadingStyles(mainPart);

            foreach (var element in mainPart.Document.Body.Elements())
            {
                var level = HeadingLevel(element, styles);
                if (level > -1)
                {
                    if (currentContent.Length > 0 || currentHeading != "No Heading")
                    {
                        var page = GetPageNumberApproximation(element);
                        result.Add(new Section() { Title = currentHeading, Content = currentContent.ToString(), Page = page });
                    }

                    if (level > 1)
                    currentHeading = currentHeading + ". " + element.InnerText;
                    else
                    currentHeading = element.InnerText;

                    currentContent = new StringBuilder();
                }
                else
                {
                    if (element is Paragraph paragraph)
                    {
                        currentContent.AppendLine(ExtractParagraphText(paragraph));
                    }
                    else if (element is Table table)
                    {
                        currentContent.AppendLine(ExtractTableText(table));
                    }
                }
            }
        }

        if (currentContent.Length > 0)
        {
            var lastpage = 1;
            if (result.Count > 0)
                lastpage = result.Max(x => x.Page) + 1;
            result.Add(new Section() { Title = currentHeading, Content = currentContent.ToString(), Page = lastpage });
        }

        return result;
    }
    /// <summary>
    /// Оценивает номер страницы элемента Open XML по количеству разрывов страниц перед ним.
    /// </summary>
    /// <param name="element">документ element для которого приблизительный страница число является требуемый.</param>
    /// <returns>Приблизительный номер страницы, начиная с единицы.</returns>
    public static int GetPageNumberApproximation(OpenXmlElement element)
    {
        int pageNumber = 1;

        var root = element.Ancestors<Body>().FirstOrDefault();
        if (root == null)
        {
            return 1;
        }

        var tmpElement = element;
        while (tmpElement != root)
        {
            var sibling = tmpElement.PreviousSibling();
            while (sibling != null)
            {
                pageNumber += sibling.Descendants<LastRenderedPageBreak>().Count();
                sibling = sibling.PreviousSibling();
            }

            var parent = tmpElement.Parent;
            if (parent == null)
                break;

            tmpElement = parent;
        }
        return pageNumber;
    }
    /// <summary>
    /// Описывает назначение элемента.
    /// </summary>
    /// <param name="paragraph">абзац для extract.</param>
    /// <returns>Видимый текст абзаца или пустая строка, если текст отсутствует.</returns>
    private string? ExtractParagraphText(Paragraph paragraph)
    {
        var textBuilder = new StringBuilder();

        foreach (var run in paragraph.Elements<Run>())
        {
            bool inComplexFieldCode = false;

            var fieldChar = run.Elements<FieldChar>().FirstOrDefault();
            if (fieldChar != null)
            {
                if (fieldChar.FieldCharType?.Value == FieldCharValues.Begin)
                {
                    inComplexFieldCode = true;
                }
                else if (fieldChar.FieldCharType?.Value == FieldCharValues.Separate)
                {
                    inComplexFieldCode = false;
                }
                else if (fieldChar.FieldCharType?.Value == FieldCharValues.End)
                {
                    inComplexFieldCode = false;
                }
                continue;
            }

            var fieldCodeElement = run.Elements<FieldCode>().FirstOrDefault();
            if (fieldCodeElement != null)
            {
                continue;
            }

            var simpleField = run.Elements<SimpleField>().FirstOrDefault();
            if (simpleField != null)
            {
                textBuilder.Append(simpleField.InnerText);
                continue;
            }

            var hyperlink = run.Elements<Hyperlink>().FirstOrDefault();
            if (hyperlink != null)
            {
                if (!string.IsNullOrEmpty(hyperlink.InnerText))
                {
                    textBuilder.Append(hyperlink.InnerText);
                }
                continue;
            }

            if (!inComplexFieldCode)
            {
                var runText = run.InnerText;
                textBuilder.Append(runText);
            }
        }

        if (textBuilder.ToString().Trim().Length == 0)
        {
            foreach (var hyperlink in paragraph.Descendants<Hyperlink>())
            {
                foreach (var text in hyperlink.Descendants<Text>())
                {
                    textBuilder.Append(text.InnerText + " ");
                }
            }
        }

        return textBuilder.ToString().Trim();
    }
    /// <summary>
    /// Создаёт соответствие идентификаторов стилей Word уровням заголовков.
    /// </summary>
    /// <param name="mainPart">DOCX main документ part containing стиль definitions.</param>
    /// <returns>стиль идентификатор для heading-level соответствие.</returns>
    private Dictionary<string, int> GetHeadingStyles(MainDocumentPart mainPart)
    {
        var headingStyles = new Dictionary<string, int>();
        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart != null)
        {
            foreach (var style in stylesPart.Styles?.Elements<Style>() ?? Enumerable.Empty<Style>())
            {
                var styleId = style.StyleId?.Value;
                if (string.IsNullOrWhiteSpace(styleId))
                    continue;

                var styleParagraphProperties = style.StyleParagraphProperties;
                if (styleParagraphProperties != null)
                {
                    var outlineLevel = styleParagraphProperties.OutlineLevel?.Val?.Value;
                    if (outlineLevel != null)
                    {
                        headingStyles[styleId] = (int)outlineLevel + 1;
                    }
                    else
                    {
                        var basedOn = style.BasedOn?.Val?.Value;
                        var link = style.LinkedStyle?.Val?.Value;
                        if (basedOn != null)
                        {
                            if (headingStyles.ContainsKey(basedOn))
                            {
                                headingStyles[styleId] = headingStyles[basedOn];
                            }
                        }
                        else if (link != null)
                        {
                            if (headingStyles.ContainsKey(link))
                            {
                                headingStyles[styleId] = headingStyles[link];
                            }
                        }
                    }
                }
            }
        }
        return headingStyles;
    }
    /// <summary>
    /// Определяет уровень заголовка элемента Open XML по стилю его абзаца.
    /// </summary>
    /// <param name="element">element для проверить.</param>
    /// <param name="styles">style-to-level соответствие.</param>
    /// <returns>Уровень заголовка или -1, если элемент не является распознанным заголовком.</returns>
    private int HeadingLevel(OpenXmlElement element, Dictionary<string, int> styles)
    {
        if (element is Paragraph paragraph)
        {
            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (styleId != null && styles.ContainsKey(styleId))
            {
                return styles[styleId];
            }
        }
        return -1;
    }
    /// <summary>
    /// Преобразует таблицу в представление JSON, используя первую жирную строку как заголовок, если это возможно.
    /// </summary>
    /// <param name="table">таблица для extract.</param>
    /// <returns>Данные таблицы в формате JSON с отступами.</returns>
    private string? ExtractTableText(Table table)
    {
        var tableData = new List<Dictionary<string, string>>();
        var rows = table.Elements<TableRow>().ToList();

        if (rows.Any())
        {
            var headerCells = rows.First().Elements<TableCell>().ToList();
            bool hasHeader = IsHeaderRow(headerCells);

            int startRowIndex = hasHeader ? 1 : 0;

            for (int i = startRowIndex; i < rows.Count; i++)
            {
                var rowData = new Dictionary<string, string>();
                var cells = rows[i].Elements<TableCell>().ToList();

                for (int j = 0; j < cells.Count; j++)
                {
                    string headerText = hasHeader && j < headerCells.Count ? headerCells[j].InnerText : $"Column_{j + 1}";
                    rowData[headerText] = cells[j].InnerText;
                }
                tableData.Add(rowData);
            }
        }

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Create(new TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All)) };
        return System.Text.Json.JsonSerializer.Serialize(tableData, options);
    }
    /// <summary>
    /// Определяет ли все cells в один row contain bold текст.
    /// </summary>
    /// <param name="cells">cells из кандидат заголовок row.</param>
    /// <returns><see langword="true"/> когда every cell содержит один bold run.</returns>
    private bool IsHeaderRow(IEnumerable<TableCell> cells)
    {
        foreach (var cell in cells)
        {
            var boldRun = cell.Descendants<Bold>().FirstOrDefault();
            if (boldRun == null)
            {
                return false;
            }
        }
        return true;
    }
}
