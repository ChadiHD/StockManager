using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SMDesktopUI.Services
{
    public class WindowsLabelPrintService : ILabelPrintService
    {
        public LabelPrintResult Print(string jobName, string labelContent)
        {
            if (string.IsNullOrWhiteSpace(labelContent))
            {
                return new(false, false, "There is no label content to print.");
            }

            try
            {
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true)
                {
                    return new(false, true, "Label printing was cancelled.");
                }

                var document = CreateDocument(labelContent, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
                return new(true, false, "Label sent to the selected Windows printer.");
            }
            catch (InvalidOperationException ex)
            {
                return new(false, false, $"The label could not be printed: {ex.Message}");
            }
            catch (Win32Exception ex)
            {
                return new(false, false, $"Windows could not access the selected printer: {ex.Message}");
            }
        }

        private static FlowDocument CreateDocument(string content, double width, double height)
        {
            var document = new FlowDocument
            {
                PageWidth = width,
                PageHeight = height,
                PagePadding = new Thickness(24),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14
            };
            document.Blocks.Add(new Paragraph(new Run("STOCKMANAGER"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(16, 35, 63)),
                Margin = new Thickness(0, 0, 0, 12)
            });
            document.Blocks.Add(new Paragraph(new Run(content))
            {
                Margin = new Thickness(0),
                TextAlignment = TextAlignment.Left
            });
            return document;
        }
    }
}
