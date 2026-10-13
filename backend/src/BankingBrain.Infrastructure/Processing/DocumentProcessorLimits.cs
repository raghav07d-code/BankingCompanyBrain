namespace BankingBrain.Infrastructure.Processing;

internal static class DocumentProcessorLimits
{
    public const int MaxPdfPages = 10_000;
    public const int MaxOpenXmlItems = 100_000;
    public const int MaxSpreadsheetRows = 100_000;
    public const int MaxSpreadsheetCells = 1_000_000;
    public const int MaxPresentationSlides = 2_000;
}
