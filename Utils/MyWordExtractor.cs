using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
/// <summary>
/// Extracts structured text sections from DOCX documents.
/// </summary>
public class MyWordExtractor
{
    /// <summary>
    /// Represents a document section and its approximate page number.
    /// </summary>
    /// <summary>
    /// Represents a document section and its approximate page number.
    /// </summary>
    public class Section
    {
        /// <summary>
        /// Gets or sets the section title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the extracted section content.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Gets or sets the approximate one-based page number.
        /// </summary>
        public int Page { get; set; }
    }
    /// <summary>
    /// Reads a DOCX document, groups content by heading, and approximates section page numbers.
    /// </summary>
    /// <param name="filename">The path of the DOCX file.</param>
    /// <returns>The extracted document sections.</returns>
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
    /// Estimates the page number of an Open XML element by counting rendered page breaks before it.
    /// </summary>
    /// <param name="element">The document element whose approximate page number is required.</param>
    /// <returns>The estimated one-based page number.</returns>
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
            tmpElement = tmpElement.Parent;
        }
        return pageNumber;
    }
    /// <summary>
    /// Extracts visible text from paragraph runs while excluding field-code instructions.
    /// </summary>
    /// <param name="paragraph">The paragraph to extract.</param>
    /// <returns>The visible paragraph text, or an empty string when no text is present.</returns>
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
    /// Builds a map from Word style identifiers to heading levels.
    /// </summary>
    /// <param name="mainPart">The DOCX main document part containing the style definitions.</param>
    /// <returns>A style identifier to heading-level map.</returns>
    private Dictionary<string, int> GetHeadingStyles(MainDocumentPart mainPart)
    {
        var headingStyles = new Dictionary<string, int>();
        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart != null)
        {
            foreach (var style in stylesPart.Styles.Elements<Style>())
            {
                var styleParagraphProperties = style.StyleParagraphProperties;
                if (styleParagraphProperties != null)
                {
                    var outlineLevel = styleParagraphProperties.OutlineLevel?.Val?.Value;
                    if (outlineLevel != null)
                    {
                        headingStyles[style.StyleId] = (int)outlineLevel + 1;
                    }
                    else
                    {
                        var basedOn = style.BasedOn?.Val?.Value;
                        var link = style.LinkedStyle?.Val?.Value;
                        if (basedOn != null)
                        {
                            if (headingStyles.ContainsKey(basedOn))
                            {
                                headingStyles[style.StyleId] = headingStyles[basedOn];
                            }
                        }
                        else if (link != null)
                        {
                            if (headingStyles.ContainsKey(link))
                            {
                                headingStyles[style.StyleId] = headingStyles[link];
                            }
                        }
                    }
                }
            }
        }
        return headingStyles;
    }
    /// <summary>
    /// Resolves the heading level of an Open XML element from its paragraph style.
    /// </summary>
    /// <param name="element">The element to inspect.</param>
    /// <param name="styles">The style-to-level map.</param>
    /// <returns>The heading level, or -1 when the element is not a recognized heading.</returns>
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
    /// Converts a table to a JSON representation using the first bold row as a header when possible.
    /// </summary>
    /// <param name="table">The table to extract.</param>
    /// <returns>The table data serialized as indented JSON.</returns>
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
    /// Determines whether all cells in a row contain bold text.
    /// </summary>
    /// <param name="cells">The cells of the candidate header row.</param>
    /// <returns><see langword="true"/> when every cell contains a bold run.</returns>
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
