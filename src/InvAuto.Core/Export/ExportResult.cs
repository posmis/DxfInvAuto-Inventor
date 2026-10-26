namespace InvAuto.Core.Export
{
    public enum ExportStatus
    {
        Exported,
        Skipped,
        Failed
    }

    public sealed class ExportResult
    {
        public ExportResult(SheetMetalPart part, ExportStatus status, string filePath, string message)
        {
            Part = part;
            Status = status;
            FilePath = filePath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public SheetMetalPart Part { get; }
        public ExportStatus Status { get; }
        public string FilePath { get; }
        public string Message { get; }
    }
}
